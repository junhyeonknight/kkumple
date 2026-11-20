#requires -Version 5
# 꿈플 AutoCAD 2008/2009 연결기(시험판) 빌드
#   .\build.ps1 -Body <꿈플.exe 경로>
# 꿈플.exe(본체 2.0.3)에 들어 있는 2010 계열 모듈을 해시 검증 후 사용한다.
param(
    [Parameter(Mandatory = $true)][string]$Body
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$build = Join-Path $root 'build'
$dist = Join-Path $root '배포'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

& $csc /nologo /platform:anycpu /out:"$build\LegacyBuilder.exe" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Xml.dll "$root\tools\LegacyBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw 'LegacyBuilder 컴파일 실패' }
& "$build\LegacyBuilder.exe" --body $Body --out "$build\out"
if ($LASTEXITCODE -ne 0) { throw '페이로드 생성 실패' }

$exe = Join-Path $dist '꿈플-2008-2009-연결.exe'
& $csc /nologo /target:winexe /platform:anycpu /out:$exe `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "/resource:$build\out\LegacyPayload.zip,LegacyPayload.zip" "/resource:$root\src\loader-template.lsp,loader-template.lsp" `
    "$root\src\Connector.cs"
if ($LASTEXITCODE -ne 0) { throw '연결기 컴파일 실패' }
Set-Content -LiteralPath "$exe.config" -Encoding UTF8 -Value '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5" /></startup></configuration>'
Copy-Item -LiteralPath "$root\사용방법.md" -Destination $dist -Force
$sums = Get-ChildItem -LiteralPath $dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value $sums -Encoding UTF8
Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
