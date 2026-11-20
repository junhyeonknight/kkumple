#requires -Version 5
# 꿈플 설치 도우미 빌드
#   .\build.ps1
# build\꿈플-설치.exe     전체판(본체 포함). 사용자가 처음 설치할 때 받는 파일. 켜면 자기 업데이트.
# build\Kkumple.Setup.exe  관리용 경량판(본체 미포함). CAD 꿈플 탭 '꿈플 관리' 버튼이 연다. CAD 자동 업데이트 패키지로 배포.
#                          CAD 시작 시 '--refresh-installer'로 실행되어 기억된 전체판 파일을 최신으로 바꾼다.
# payload\payload.zip / compact-payload.zip = 설치 도우미 2.2.4와 같은 검증된 설치 구성
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null
$refs = @('/nologo','/codepage:65001','/langversion:5','/nowarn:649','/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll',
          '/r:System.IO.Compression.FileSystem.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Xml.dll')
$common = @(Get-ChildItem -LiteralPath "$root\src" -Recurse -Filter *.cs | ForEach-Object FullName)

$full = Join-Path $out '꿈플-설치.exe'
& $csc @refs /target:winexe "/out:$full" "/resource:$root\payload\payload.zip,Kkumple.InstallPayload.zip" @common "$root\src-variant\Full\EmbeddedPayload.cs"
if ($LASTEXITCODE -ne 0) { throw '전체판 컴파일 실패' }
$compact = Join-Path $out 'Kkumple.Setup.exe'
& $csc @refs /target:winexe "/out:$compact" "/resource:$root\payload\compact-payload.zip,Kkumple.InstallPayload.zip" @common "$root\src-variant\Compact\EmbeddedPayload.cs"
if ($LASTEXITCODE -ne 0) { throw '경량판 컴파일 실패' }
foreach ($f in $full, $compact) {
    '{0}  {1}  {2}  {3}' -f (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLower(), (Get-Item -LiteralPath $f).Length, (Get-Item -LiteralPath $f).VersionInfo.FileVersion, (Split-Path $f -Leaf)
}
