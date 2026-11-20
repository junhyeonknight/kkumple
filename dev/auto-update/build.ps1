$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$output = Join-Path $PSScriptRoot '배포\Kkumple.Updater.exe'
& $compiler /nologo /target:winexe /codepage:65001 /langversion:5 /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/out:$output" "$PSScriptRoot\Updater\UpdateCore.cs" "$PSScriptRoot\Updater\GateInstaller.cs" "$PSScriptRoot\Updater\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Updater compilation failed.' }
Write-Output 'Updater built. No installation was performed.'
