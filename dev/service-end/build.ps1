[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$distribution = Join-Path $PSScriptRoot '배포'
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
& $compiler /nologo /target:winexe /codepage:65001 /langversion:5 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/out:$distribution\꿈플-서비스종료.exe" "$PSScriptRoot\ServiceEndAgent\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Agent compilation failed.' }
& $compiler /nologo /target:winexe /codepage:65001 /langversion:5 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$distribution\꿈플-설치.exe" "$PSScriptRoot\InstallConsent\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Consent installer compilation failed.' }
Copy-Item -LiteralPath "$PSScriptRoot\ServiceEndAgent\config.json" -Destination "$distribution\config.json" -Force
Copy-Item -LiteralPath "$PSScriptRoot\InstallConsent\consent-config.json" -Destination "$distribution\consent-config.json" -Force
Write-Output 'Build completed. No installation was performed.'
