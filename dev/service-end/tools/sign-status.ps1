<#
  꿈플 서비스 종료 - 원격 상태 서명 도구 (운영자/원작자 전용)

  1) 키 생성 (최초 1회):
     powershell -ExecutionPolicy Bypass -File sign-status.ps1 -GenerateKey
     → privatekey.xml (비밀! 절대 배포 금지), publickey.xml (config.json 에 넣을 공개키)

  2) 종료 신호 서명:
     # payload.json 을 원하는 내용으로 수정한 뒤:
     powershell -ExecutionPolicy Bypass -File sign-status.ps1 -Sign -PayloadFile payload.json -Out status.json
     → status.json 을 StatusUrl 위치(정적 호스팅)에 업로드

  private key 는 오프라인에 보관하세요. 이 키를 가진 사람만 킬스위치를 누를 수 있습니다.
#>
[CmdletBinding()]
param(
  [switch]$GenerateKey,
  [switch]$Sign,
  [string]$PrivateKey = "privatekey.xml",
  [string]$PublicKey  = "publickey.xml",
  [string]$PayloadFile,
  [string]$Out = "status.json"
)

$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# .NET 의 현재 디렉터리는 PowerShell 과 다를 수 있으므로 상대경로를 $PWD 기준 절대경로로 변환
function Resolve-OutPath([string]$p) {
  if ([System.IO.Path]::IsPathRooted($p)) { return $p }
  return (Join-Path (Get-Location).Path $p)
}

if ($GenerateKey) {
  $privPath = Resolve-OutPath $PrivateKey
  $pubPath  = Resolve-OutPath $PublicKey
  $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048)
  try {
    [System.IO.File]::WriteAllText($privPath, $rsa.ToXmlString($true),  $utf8NoBom)
    [System.IO.File]::WriteAllText($pubPath,  $rsa.ToXmlString($false), $utf8NoBom)
    Write-Host "개인키 생성: $privPath  (비밀 보관! 배포 금지)"
    Write-Host "공개키 생성: $pubPath   (config.json 의 UpdatePublicKeyXml 값으로 사용)"
  } finally { $rsa.Dispose() }
  return
}

if ($Sign) {
  if (-not $PayloadFile) { throw "서명하려면 -PayloadFile 을 지정하세요." }
  $privPath    = Resolve-OutPath $PrivateKey
  $payloadPath = Resolve-OutPath $PayloadFile
  $outPath     = Resolve-OutPath $Out
  if (-not (Test-Path -LiteralPath $privPath))    { throw "개인키가 없습니다: $privPath (먼저 -GenerateKey)" }
  if (-not (Test-Path -LiteralPath $payloadPath)) { throw "payload 파일이 없습니다: $payloadPath" }

  $privXml = [System.IO.File]::ReadAllText($privPath, [System.Text.Encoding]::UTF8)
  $payload = [System.IO.File]::ReadAllText($payloadPath, [System.Text.Encoding]::UTF8)   # BOM 자동 제거
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

  $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
  try {
    $rsa.FromXmlString($privXml)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
      $hash = $sha.ComputeHash($bytes)
      $fmt = New-Object System.Security.Cryptography.RSAPKCS1SignatureFormatter($rsa)
      $fmt.SetHashAlgorithm("SHA256")
      $sig = $fmt.CreateSignature($hash)
    } finally { $sha.Dispose() }
  } finally { $rsa.Dispose() }

  $sigB64 = [Convert]::ToBase64String($sig)
  $obj = [ordered]@{ payload = $payload; signature = $sigB64 }
  $json = ($obj | ConvertTo-Json -Compress -Depth 5)
  [System.IO.File]::WriteAllText($outPath, $json, $utf8NoBom)
  Write-Host "서명 완료 → $outPath  (이 파일을 StatusUrl 에 업로드하세요)"
  return
}

Write-Host "사용법: -GenerateKey  또는  -Sign -PayloadFile payload.json -Out status.json"
