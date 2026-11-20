[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$distribution = Join-Path (Split-Path -Parent $PSScriptRoot) '배포'
$testRoot = [IO.Path]::GetFullPath($WorkDirectory)
if (-not $testRoot.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Tests must run below the current user profile.'
}
if (Test-Path -LiteralPath $testRoot) { throw 'Use a new empty test directory for each run.' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
$source = Join-Path $testRoot 'package 한글 and spaces'
$installed = Join-Path $testRoot 'installed 한글 and spaces'
New-Item -ItemType Directory -Path $source | Out-Null
foreach ($name in @('꿈플-서비스종료.exe','꿈플-설치.exe','config.json','consent-config.json',
    'agent-common.ps1','install-agent.ps1','remove-agent.ps1','자동점검-제거.cmd')) {
    Copy-Item -LiteralPath (Join-Path $distribution $name) -Destination (Join-Path $source $name)
}
$configFile = Join-Path $source 'config.json'
$config = Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.DryRun -ne $false) { throw 'The release configuration was not armed.' }
$config.DryRun = $true # Every real scheduled execution in these tests is non-destructive.
$config | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $configFile -Encoding UTF8
$gate = Get-Content -LiteralPath (Join-Path $source 'consent-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($gate.DisclosureText))).Replace('-', '') }
finally { $sha.Dispose() }
$consent = [ordered]@{agreed=$true;service=$gate.ServiceName;version=$gate.Version;timestampIso=[DateTimeOffset]::Now.ToString('o');disclosureSha256=$hash}
$record = Join-Path $testRoot 'test-only-consent.json'
$consent | ConvertTo-Json | Set-Content -LiteralPath $record -Encoding UTF8
$suffix = [Guid]::NewGuid().ToString('N')
$taskName = 'KkumpleServiceEnd-Test-' + $suffix
$foreignName = 'KkumpleServiceEnd-Test-Foreign-' + $suffix
$tasksCreated = @($taskName, $foreignName)
$passed = New-Object 'Collections.Generic.List[string]'
$failure = $null
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Check([bool]$Value, [string]$Name) {
    if (-not $Value) { throw "FAILED: $Name" }
    $passed.Add($Name)
}
function Run-Install([string]$Target, [string]$Name) {
    $text = & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $source 'install-agent.ps1') -ConsentRecord $record -InstallDirectory $Target -TaskName $Name 2>&1
    $code = $LASTEXITCODE
    Add-Content -LiteralPath (Join-Path $testRoot 'subprocess.log') -Value ($text | Out-String) -Encoding UTF8
    return $code
}
function Run-Remove([string]$Target, [string]$Name) {
    $text = & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $source 'remove-agent.ps1') -InstallDirectory $Target -TaskName $Name -Quiet 2>&1
    $code = $LASTEXITCODE
    Add-Content -LiteralPath (Join-Path $testRoot 'subprocess.log') -Value ($text | Out-String) -Encoding UTF8
    return $code
}
function Write-Record { $consent | ConvertTo-Json | Set-Content -LiteralPath $record -Encoding UTF8 }

try {
    foreach ($file in Get-ChildItem -LiteralPath $distribution -Filter '*.ps1') {
        $tokens = $null; $errors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        Check ($errors.Count -eq 0) ('PowerShell 5 parser: ' + $file.Name)
    }
    $consent.agreed = $false; Write-Record
    Check ((Run-Install $installed $taskName) -ne 0 -and -not (Test-Path -LiteralPath $installed)) 'refused consent cannot install'
    $consent.agreed = $true
    $consent.disclosureSha256 = 'wrong'; Write-Record
    Check ((Run-Install $installed $taskName) -ne 0) 'mismatched disclosure cannot install'
    $consent.disclosureSha256 = $hash
    $consent.timestampIso = [DateTimeOffset]::Now.AddHours(-1).ToString('o'); Write-Record
    Check ((Run-Install $installed $taskName) -ne 0) 'stale consent cannot install'
    $consent.timestampIso = [DateTimeOffset]::Now.ToString('o'); Write-Record
    Check ((Run-Install $installed $taskName) -eq 0) 'isolated agent and daily task install'
    $task = $folder.GetTask($taskName)
    $definition = $task.Definition
    Check ($definition.Principal.LogonType -eq 3 -and $definition.Principal.RunLevel -eq 0) 'current user interactive token without elevation'
    Check ($definition.Triggers.Count -eq 1 -and $definition.Triggers.Item(1).Type -eq 2 -and $definition.Triggers.Item(1).DaysInterval -eq 1 -and $definition.Triggers.Item(1).StartBoundary -match 'T09:00:00$') 'one daily 09:00 trigger'
    Check ($definition.Settings.StartWhenAvailable -and $definition.Settings.MultipleInstances -eq 2 -and -not $definition.Settings.DisallowStartIfOnBatteries) 'catch-up and overlap and battery settings'
    Check ($definition.Actions.Item(1).Path -eq (Join-Path $installed '꿈플-서비스종료.exe') -and $definition.Actions.Item(1).Arguments -eq ('--config "' + (Join-Path $installed 'config.json') + '"')) 'Korean and space paths are quoted correctly'
    $installedCfg = Get-Content -LiteralPath (Join-Path $installed 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check ($installedCfg.ScheduledTaskName -eq $taskName -and $installedCfg.DryRun -eq $true -and $installedCfg.SelfRemove -eq $false) 'installed task name and test-only dry run are correct'
    Check ((Get-FileHash -LiteralPath (Join-Path $source '꿈플-서비스종료.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $installed '꿈플-서비스종료.exe')).Hash) 'installed executable matches release bytes'
    $before = $task.LastRunTime
    $null = $task.Run($null)
    $deadline = (Get-Date).AddSeconds(40)
    do { Start-Sleep -Milliseconds 300; $task = $folder.GetTask($taskName) }
    while ((($task.LastRunTime -le $before) -or $task.State -eq 4) -and (Get-Date) -lt $deadline)
    Check ($task.LastRunTime -gt $before -and $task.State -ne 4 -and $task.LastTaskResult -eq 0) 'Task Scheduler actually launches the agent successfully'
    Check ((Get-Content -LiteralPath (Join-Path $installed 'service-end.log') -Raw -Encoding UTF8) -match 'DryRun=True') 'scheduled process loads its isolated practice config'
    Set-Content -LiteralPath (Join-Path $installed 'user-note.txt') -Value 'keep this user file' -Encoding UTF8
    Check ((Run-Install $installed $taskName) -eq 0) 'reinstall updates own task without duplicates'
    Check ((Get-Content -LiteralPath (Join-Path $installed 'user-note.txt') -Raw -Encoding UTF8) -match 'keep this') 'reinstall preserves unrelated files'
    Check ((Run-Remove $installed $taskName) -eq 0) 'agent uninstall succeeds'
    $remainingTask = @($folder.GetTasks(1) | Where-Object { $_.Name -eq $taskName })
    Check ($remainingTask.Count -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $installed '꿈플-서비스종료.exe'))) 'uninstall removes only own task and managed binary'
    Check ((Test-Path -LiteralPath (Join-Path $installed 'user-note.txt')) -and (Test-Path -LiteralPath (Join-Path $installed 'service-end.log')) -and (Test-Path -LiteralPath $record)) 'uninstall preserves unrelated files logs and consent'
    Check ((Run-Remove $installed $taskName) -eq 0) 'repeat uninstall succeeds'

    $unmarked = Join-Path $testRoot 'unmarked'
    New-Item -ItemType Directory -Path $unmarked | Out-Null
    Set-Content -LiteralPath (Join-Path $unmarked 'config.json') -Value 'unrelated original' -Encoding UTF8
    Check ((Run-Install $unmarked $taskName) -ne 0 -and (Run-Remove $unmarked $taskName) -ne 0) 'unmarked existing folder rejects install and removal'
    Check ((Get-Content -LiteralPath (Join-Path $unmarked 'config.json') -Raw -Encoding UTF8) -match 'unrelated original') 'refused folder contents stay intact'

    $foreign = $scheduler.NewTask(0)
    $foreign.RegistrationInfo.Description = 'isolated foreign task fixture'
    $foreign.Principal.UserId = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $foreign.Principal.LogonType = 3
    $foreign.Principal.RunLevel = 0
    $foreign.Settings.Enabled = $false
    $action = $foreign.Actions.Create(0); $action.Path = Join-Path $env:WINDIR 'System32\cmd.exe'
    $null = $folder.RegisterTaskDefinition($foreignName, $foreign, 6, [Security.Principal.WindowsIdentity]::GetCurrent().User.Value, $null, 3)
    $foreignPath = Join-Path $testRoot 'foreign-install'
    Check ((Run-Install $foreignPath $foreignName) -ne 0 -and -not (Test-Path -LiteralPath $foreignPath)) 'foreign task collision refuses install before copying'
    Check ((Run-Remove $foreignPath $foreignName) -ne 0 -and $folder.GetTask($foreignName).Definition.RegistrationInfo.Description -eq 'isolated foreign task fixture') 'foreign task cannot be removed'
    $external = Join-Path $testRoot 'junction-target'; New-Item -ItemType Directory -Path $external | Out-Null
    $junction = Join-Path $testRoot 'junction-install'
    New-Item -ItemType Junction -Path $junction -Target $external | Out-Null
    Check ((Run-Install $junction $taskName) -ne 0 -and (Run-Remove $junction $taskName) -ne 0) 'installer and remover reject junctions'
    [IO.Directory]::Delete($junction, $false)

    $bad = Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $bad.PathsToRemove = @('%USERPROFILE%')
    $bad | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $configFile -Encoding UTF8
    $badPath = Join-Path $testRoot 'bad-config'
    Check ((Run-Install $badPath $taskName) -ne 0 -and -not (Test-Path -LiteralPath $badPath)) 'unexpected cleanup path is refused before installation'
} catch { $failure = $_.Exception.ToString() }
finally {
    foreach ($name in $tasksCreated) {
        foreach ($task in $folder.GetTasks(1)) {
            if ($task.Name -eq $name) { $task.Stop(0); $folder.DeleteTask($name, 0) }
        }
    }
    $leftovers = @($folder.GetTasks(1) | Where-Object { $tasksCreated -contains $_.Name })
    if ($leftovers.Count -ne 0) { $failure += ' Test tasks were not cleaned up.' }
    [ordered]@{count=$passed.Count;passed=@($passed);failure=$failure;temporaryTasksRemaining=$leftovers.Count} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
}
Write-Output ('Scheduler integration: ' + $passed.Count + ' passed')
if ($failure) { Write-Output $failure; exit 1 }
exit 0
