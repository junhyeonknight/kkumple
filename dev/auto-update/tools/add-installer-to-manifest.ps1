#requires -Version 5
# 이미 서명된 update.json에 설치 도우미 정보(installer)를 넣어 다시 서명한다.
# 꿈플 패키지 목록·버전·패치노트는 그대로 두고, 발급/만료 시각만 새로 정한다.
# 개인키는 서명에만 쓰며 복사하거나 출력하지 않는다.
param(
    [Parameter(Mandatory = $true)][string]$SignedManifest,
    [Parameter(Mandatory = $true)][string]$PrivateKey,
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$InstallerVersion,
    [Parameter(Mandatory = $true)][ValidatePattern('^https://')][string]$InstallerUrl,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1,365)][int]$ValidDays = 365
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$utf8 = New-Object Text.UTF8Encoding($false)
$config = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '배포\updater-config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) { throw 'Use a new empty output directory.' }
$js = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$js.MaxJsonLength = 2097152
$envelope = $js.DeserializeObject([IO.File]::ReadAllText($SignedManifest, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF))
$payload = $js.DeserializeObject($envelope['payload'])
$file = Get-Item -LiteralPath $InstallerPath
$sha = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$fileVersion = $file.VersionInfo.FileVersion
if (-not $fileVersion.StartsWith($InstallerVersion + '.') -and $fileVersion -ne $InstallerVersion) { throw "Installer file version $fileVersion does not match $InstallerVersion." }
if ($payload.ContainsKey('installer')) { throw 'The manifest already has an installer entry. Start from the package-only manifest.' }
$now = [DateTimeOffset]::UtcNow
# 원래 서명된 payload 문자열을 그대로 두고 시각 두 개만 바꾼 뒤 installer 항목을 덧붙인다.
$text = [string]$envelope['payload']
$issuedRx = '"issuedAtUtc":"[^"]*"'; $expiresRx = '"expiresAtUtc":"[^"]*"'
if (([regex]::Matches($text, $issuedRx)).Count -ne 1 -or ([regex]::Matches($text, $expiresRx)).Count -ne 1 -or -not $text.EndsWith('}')) { throw 'Unexpected manifest payload format.' }
$text = [regex]::Replace($text, $issuedRx, '"issuedAtUtc":"' + $now.ToString('o') + '"')
$text = [regex]::Replace($text, $expiresRx, '"expiresAtUtc":"' + $now.AddDays($ValidDays).ToString('o') + '"')
$installerJson = '"installer":{"version":"' + $InstallerVersion + '","url":"' + $InstallerUrl + '","sha256":"' + $sha + '","bytes":' + $file.Length + '}'
$newPayload = $text.Substring(0, $text.Length - 1) + ',' + $installerJson + '}'
$check = $js.DeserializeObject($newPayload)
if ($check['version'] -ne $payload['version'] -or $check['installer']['sha256'] -ne $sha) { throw 'Rebuilt payload check failed.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([IO.File]::ReadAllText($PrivateKey, [Text.Encoding]::UTF8))
    if ($rsa.PublicOnly -or $rsa.KeySize -lt 2048) { throw 'An RSA-2048 or larger private key is required.' }
    $public = New-Object Security.Cryptography.RSACryptoServiceProvider
    $public.PersistKeyInCsp = $false
    try {
        $public.FromXmlString($config.PublicKeyXml)
        if ($public.ToXmlString($false) -cne $rsa.ToXmlString($false)) { throw 'The signing key does not match the client public key.' }
    } finally { $public.Dispose() }
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $formatter = New-Object Security.Cryptography.RSAPKCS1SignatureFormatter($rsa)
        $formatter.SetHashAlgorithm('SHA256')
        $signature = $formatter.CreateSignature($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($newPayload)))
    } finally { $hasher.Dispose() }
} finally { $rsa.Dispose() }
$envelopeJson = '{"payload":' + $js.Serialize($newPayload) + ',"signature":"' + [Convert]::ToBase64String($signature) + '"}'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'update.json'), $envelopeJson, $utf8)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'payload.json'), $newPayload, $utf8)
Write-Output ("Signed: version=" + $check['version'] + "; installer=" + $InstallerVersion + "; sha256=" + $sha + "; bytes=" + $file.Length)
