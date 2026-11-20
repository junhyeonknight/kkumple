[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [Parameter(Mandatory=$true)][string]$VerifierExe
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2.0
$sourceProject=Split-Path -Parent $PSScriptRoot
$root=[IO.Path]::GetFullPath($WorkDirectory)
$VerifierExe=[IO.Path]::GetFullPath($VerifierExe)
if (Test-Path -LiteralPath $root) { throw 'Use a new test directory.' }
$tools=Join-Path $root 'template\tools'
$deployment=Join-Path $root 'template\배포'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
New-Item -ItemType Directory -Path $deployment -Force | Out-Null
$builder=Join-Path $tools 'new-update-release.ps1'
Copy-Item -LiteralPath (Join-Path $sourceProject 'tools\new-update-release.ps1') -Destination $builder
$cfg=Get-Content -LiteralPath (Join-Path $sourceProject '배포\updater-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$keyFile=Join-Path $root 'temporary-test-privatekey.xml'
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
$rsa.PersistKeyInCsp=$false
$utf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($keyFile,$rsa.ToXmlString($true),$utf8)
$cfg.PublicKeyXml=$rsa.ToXmlString($false)
$rsa.Dispose()
$cfgFile=Join-Path $deployment 'updater-config.json'
$cfg | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cfgFile -Encoding UTF8
$powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$passed=New-Object 'Collections.Generic.List[string]'
$failure=$null
function Check([bool]$Value,[string]$Name) { if (-not $Value) { throw "FAILED: $Name" };$passed.Add($Name) }
function Run-Builder([string[]]$Arguments) {
    $previousPreference=$ErrorActionPreference
    $ErrorActionPreference='Continue'
    try {
        $text=& $powershell -NoProfile -ExecutionPolicy Bypass -File $builder @Arguments 2>&1
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$previousPreference }
    Add-Content -LiteralPath (Join-Path $root 'subprocess.log') -Encoding UTF8 -Value ($text|Out-String)
    return $code
}
try {
    $tokens=$null;$errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($builder,[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0) 'release builder parses on Windows PowerShell 5'
    $baseline=Join-Path $root 'baseline'
    Check ((Run-Builder @('-Version','2.0.3','-PrivateKey',$keyFile,'-OutputDirectory',$baseline,'-Baseline')) -eq 0) 'baseline signed manifest is generated'
    $baselineFile=Join-Path $baseline 'update.json'
    $text=& $VerifierExe --verify $baselineFile $cfgFile 2>&1
    Check ($LASTEXITCODE -eq 0) 'client C# verifier accepts PowerShell baseline signature'
    $envelope=Get-Content -LiteralPath $baselineFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest=$envelope.payload | ConvertFrom-Json
    Check ($manifest.version -eq '2.0.3' -and $manifest.packages.Count -eq 0) 'baseline advertises existing version without inventing a new release'
    Check (@(Get-ChildItem -LiteralPath $baseline -File -Filter '*private*').Count -eq 0) 'private key is not copied into release output'
    $before=(Get-FileHash -LiteralPath $baselineFile).Hash
    Check ((Run-Builder @('-Version','2.0.3','-PrivateKey',$keyFile,'-OutputDirectory',$baseline,'-Baseline')) -ne 0 -and (Get-FileHash -LiteralPath $baselineFile).Hash -eq $before) 'existing release cannot be overwritten'
    $bundle=Join-Path $root 'new source 한글 and spaces'
    $family=Join-Path $bundle 'Contents\Versions\2015'
    New-Item -ItemType Directory -Path (Join-Path $family 'Features') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $bundle '.kkumple-owner') -Value 'KKUMPLE_FREE_NONCOMMERCIAL_V2' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Value '<ApplicationPackage AppVersion="2.0.4" />' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $family 'KkCore0.dll') -Value 'synthetic new program' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $family 'Features\feature.lsp') -Value '(princ)' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $family 'private-drawing.dwg') -Value 'preserve test drawing' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $family 'user.cfg') -Value 'preserve test settings' -Encoding UTF8
    $release=Join-Path $root 'release-2.0.4'
    $url='https://github.com/example/fixture/releases/download/v2.0.4'
    Check ((Run-Builder @('-Version','2.0.4','-PrivateKey',$keyFile,'-OutputDirectory',$release,'-SourceBundle',$bundle,'-BaseUrl',$url)) -eq 0) 'new-version family package and signed manifest are generated'
    $text=& $VerifierExe --verify (Join-Path $release 'update.json') $cfgFile 2>&1
    Check ($LASTEXITCODE -eq 0) 'client C# verifier accepts the generated release manifest'
    $envelope=Get-Content -LiteralPath (Join-Path $release 'update.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest=$envelope.payload | ConvertFrom-Json
    $zip=Join-Path $release 'kkumple-2.0.4-2015.zip'
    Check ($manifest.packages.Count -eq 1 -and $manifest.packages[0].family -eq '2015' -and
        $manifest.packages[0].sha256 -eq (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() -and
        $manifest.packages[0].bytes -eq (Get-Item -LiteralPath $zip).Length) 'release package hash byte length and CAD family match the archive'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names=@($archive.Entries | ForEach-Object { $_.FullName })
        Check ($names.Count -eq 2 -and $names -contains 'KkCore0.dll' -and $names -contains 'Features/feature.lsp') 'only intended module files are archived and drawings/settings are excluded'
    } finally { $archive.Dispose() }
    $mismatch=Join-Path $root 'mismatch-version'
    Check ((Run-Builder @('-Version','2.0.5','-PrivateKey',$keyFile,'-OutputDirectory',$mismatch,'-SourceBundle',$bundle,'-BaseUrl',$url)) -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $mismatch 'update.json'))) 'release version must match actual source version'
    $insecure=Join-Path $root 'http-release'
    Check ((Run-Builder @('-Version','2.0.4','-PrivateKey',$keyFile,'-OutputDirectory',$insecure,'-SourceBundle',$bundle,'-BaseUrl','http://example.invalid')) -ne 0) 'insecure release asset base URL is refused'
    Set-Content -LiteralPath (Join-Path $family 'secret.key') -Value 'synthetic forbidden fixture' -Encoding UTF8
    $secretOutput=Join-Path $root 'secret-refused'
    Check ((Run-Builder @('-Version','2.0.4','-PrivateKey',$keyFile,'-OutputDirectory',$secretOutput,'-SourceBundle',$bundle,'-BaseUrl',$url)) -ne 0 -and -not (Test-Path -LiteralPath (Join-Path $secretOutput 'update.json'))) 'possible secret source files prevent a signed release'
} catch { $failure=$_.Exception.ToString() }
finally {
    if (Test-Path -LiteralPath $keyFile -PathType Leaf) { Remove-Item -LiteralPath $keyFile -Force }
    [ordered]@{count=$passed.Count;passed=@($passed);failure=$failure;temporaryPrivateKeyRemoved=(-not(Test-Path -LiteralPath $keyFile))} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
}
Write-Output ('Release tools: '+$passed.Count+' passed')
if ($failure) { Write-Output $failure;exit 1 }
exit 0
