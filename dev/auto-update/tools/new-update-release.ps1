[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+(\.\d+){0,2}$')][string]$Version,
    [Parameter(Mandatory = $true)][string]$PrivateKey,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$SourceBundle,
    [string]$BaseUrl,
    [ValidateRange(1,365)][int]$ValidDays = 365,
    [switch]$Baseline
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = New-Object Text.UTF8Encoding($false)
$appId = 'KKUMPLE_FREE_NONCOMMERCIAL_V2'
$configPath = Join-Path (Split-Path -Parent $PSScriptRoot) '배포\updater-config.json'
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
function Assert-NoLink([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Source/output links are not allowed.'
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
function Get-Hash([string]$File) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($File)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Normalize-Version([string]$Value) {
    $parsed = [version]$Value
    return New-Object version($parsed.Major,$parsed.Minor,[Math]::Max(0,$parsed.Build),[Math]::Max(0,$parsed.Revision))
}
if (-not [IO.Path]::IsPathRooted($PrivateKey)) { $PrivateKey = Join-Path (Get-Location).Path $PrivateKey }
if (-not (Test-Path -LiteralPath $PrivateKey -PathType Leaf)) { throw 'The existing operator private key was not found.' }
Assert-NoLink $PrivateKey
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
Assert-NoLink $OutputDirectory
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -ne 0) {
    throw 'Use a new empty output directory. Existing releases are never overwritten.'
}
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
try {
    # The private key is used locally for signing only; it is never printed or copied.
    $rsa.FromXmlString([IO.File]::ReadAllText($PrivateKey, [Text.Encoding]::UTF8))
    if ($rsa.PublicOnly -or $rsa.KeySize -lt 2048) { throw 'An RSA-2048 or larger private key is required.' }
    $public = New-Object Security.Cryptography.RSACryptoServiceProvider
    $public.PersistKeyInCsp = $false
    try {
        $public.FromXmlString($config.PublicKeyXml)
        if ($public.ToXmlString($false) -cne $rsa.ToXmlString($false)) { throw 'The signing key does not match the client public key.' }
    } finally { $public.Dispose() }
    $packages = New-Object 'Collections.Generic.List[object]'
    $skipped = 0
    if (-not $Baseline) {
        if (-not $SourceBundle -or -not $BaseUrl) { throw 'A clean new-version source bundle and HTTPS release asset base URL are required.' }
        $baseUri = [uri]$BaseUrl
        if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -ne 'https' -or $baseUri.UserInfo) { throw 'Use an HTTPS release asset URL.' }
        $SourceBundle = [IO.Path]::GetFullPath($SourceBundle).TrimEnd('\')
        Assert-NoLink $SourceBundle
        if ($OutputDirectory.StartsWith($SourceBundle + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output must be outside the source bundle.' }
        $marker = Join-Path $SourceBundle '.kkumple-owner'
        Assert-NoLink $marker
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
            (Get-Content -LiteralPath $marker -Raw -Encoding UTF8) -notmatch 'KKUMPLE_FREE_NONCOMMERCIAL_V2') { throw 'Source bundle owner marker is missing.' }
        $xmlFile = Join-Path $SourceBundle 'PackageContents.xml'
        Assert-NoLink $xmlFile
        $settings = New-Object Xml.XmlReaderSettings
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $reader = [Xml.XmlReader]::Create($xmlFile,$settings)
        try { $doc = New-Object Xml.XmlDocument; $doc.XmlResolver=$null; $doc.Load($reader) } finally { $reader.Dispose() }
        if ((Normalize-Version $doc.DocumentElement.GetAttribute('AppVersion')) -ne (Normalize-Version $Version)) {
            throw 'The requested release version must match the actual new-version bundle.'
        }
        $versions = Join-Path $SourceBundle 'Contents\Versions'
        Assert-NoLink $versions
        $families = @(Get-ChildItem -LiteralPath $versions -Directory | Where-Object { $_.Name -match '^(2010|2013|2015|2025)$' })
        if ($families.Count -eq 0) { throw 'No supported CAD-family module folders were found.' }
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
        foreach ($family in $families) {
            Assert-NoLink $family.FullName
            $files = New-Object 'Collections.Generic.List[string]'
            $stack = New-Object 'Collections.Generic.Stack[string]'
            $stack.Push($family.FullName)
            while ($stack.Count -gt 0) {
                $directory = $stack.Pop()
                foreach ($entry in [IO.Directory]::GetFileSystemEntries($directory)) {
                    Assert-NoLink $entry
                    if (([IO.File]::GetAttributes($entry) -band [IO.FileAttributes]::Directory) -ne 0) { $stack.Push($entry); continue }
                    $name = [IO.Path]::GetFileName($entry)
                    $extension = [IO.Path]::GetExtension($entry)
                    if ($name -match '(?i)(private.?key|\.pfx$|\.p12$|\.pem$|\.key$)') { throw 'A possible secret file was found in the release source.' }
                    if ($extension -match '(?i)^\.(dwg|dxf|dwt|dws|bak|ini|cfg|settings)$' -or $name -eq '.kkumple-update-transaction') { $skipped++; continue }
                    $files.Add($entry)
                }
            }
            if ($files.Count -eq 0 -or $files.Count -gt $config.MaxEntries) { throw 'The package file count is invalid.' }
            $archiveName = 'kkumple-' + $Version + '-' + $family.Name + '.zip'
            $archivePath = Join-Path $OutputDirectory $archiveName
            $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
            $expanded = [long]0
            try {
                foreach ($file in $files) {
                    $expanded += (Get-Item -LiteralPath $file).Length
                    if ($expanded -gt $config.MaxExpandedBytes) { throw 'Package expanded size is above the client limit.' }
                    $relative = $file.Substring($family.FullName.Length).TrimStart('\').Replace('\','/')
                    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $relative, [IO.Compression.CompressionLevel]::Optimal)
                }
            } finally { $archive.Dispose() }
            $bytes = (Get-Item -LiteralPath $archivePath).Length
            if ($bytes -gt $config.MaxPackageBytes) { throw 'Package download size is above the client limit.' }
            $packages.Add([ordered]@{ family=$family.Name;url=$BaseUrl.TrimEnd('/')+'/'+$archiveName;sha256=(Get-Hash $archivePath);bytes=$bytes })
        }
    } else {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }
    $now = [DateTimeOffset]::UtcNow
    $payloadObject = [ordered]@{schema=1;appId=$appId;version=$Version;issuedAtUtc=$now.ToString('o');expiresAtUtc=$now.AddDays($ValidDays).ToString('o');packages=@($packages.ToArray())}
    $payload = $payloadObject | ConvertTo-Json -Depth 10 -Compress
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $formatter = New-Object Security.Cryptography.RSAPKCS1SignatureFormatter($rsa)
        $formatter.SetHashAlgorithm('SHA256')
        $signature = $formatter.CreateSignature($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload)))
    } finally { $sha.Dispose() }
    $envelope = [ordered]@{payload=$payload;signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'update.json'), $envelope, $utf8)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'release-info.json'), ($payloadObject | ConvertTo-Json -Depth 10), $utf8)
    Write-Output ('Signed release prepared: version=' + $Version + '; packages=' + $packages.Count + '; protected files excluded=' + $skipped)
    Write-Output 'Upload ZIP assets first; replace update.json only after every required asset is reachable. Private key was not copied.'
} finally { $rsa.Dispose() }
