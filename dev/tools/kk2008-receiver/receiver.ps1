# 꿈플 2008/2009 진단 보고서 수신기
# 2008 PC의 연결기가 ntfy.sh 중계 주소로 보낸 보고서 조각을 받아 이 폴더에 .txt로 합쳐 저장한다.
#   powershell -ExecutionPolicy Bypass -File receiver.ps1          (계속 수신, 20초 간격)
#   powershell -ExecutionPolicy Bypass -File receiver.ps1 -Once    (한 번만 받기)
# 중계 서버는 메시지를 약 12시간 보관하므로, 수신기가 꺼져 있던 동안의 보고서도 12시간 안에 켜면 받는다.
param([switch]$Once, [int]$Interval = 20)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
$Topic = 'kk2008-REDACTED-see-local-file'
$Root = $PSScriptRoot
$Parts = Join-Path $Root '.parts'
$StateFile = Join-Path $Root '.last-id'
New-Item -ItemType Directory -Force $Parts | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($true)

function Write-Log($s) { $line = (Get-Date -Format 'MM-dd HH:mm:ss') + ' ' + $s; Write-Host $line; [IO.File]::AppendAllText((Join-Path $Root 'receiver.log'), $line + "`r`n", $utf8) }

function Receive-Once {
    $since = if (Test-Path $StateFile) { (Get-Content $StateFile -Raw).Trim() } else { '12h' }
    if (-not $since) { $since = '12h' }
    $wc = New-Object Net.WebClient
    $wc.Encoding = [Text.Encoding]::UTF8
    $raw = $wc.DownloadString("https://ntfy.sh/$Topic/json?poll=1&since=$since")
    $done = 0
    foreach ($line in ($raw -split "`n")) {
        if (-not $line.Trim()) { continue }
        $m = $line | ConvertFrom-Json
        if ($m.event -ne 'message') { continue }
        Set-Content -LiteralPath $StateFile -Value $m.id -Encoding ASCII
        if ($m.title -notmatch '^KK2008 (\S+) (\d+)/(\d+) ?(\S*) ?(\S*)') { continue }
        $id = $Matches[1]; $i = [int]$Matches[2]; $n = [int]$Matches[3]; $reason = $Matches[4]; $machine = $Matches[5]
        $dir = Join-Path $Parts $id
        New-Item -ItemType Directory -Force $dir | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir ('{0:D3}.part' -f $i)), [string]$m.message, (New-Object System.Text.UTF8Encoding($false)))
        $have = @(Get-ChildItem $dir -Filter *.part).Count
        if ($have -ge $n) {
            $text = (1..$n | ForEach-Object { [IO.File]::ReadAllText((Join-Path $dir ('{0:D3}.part' -f $_)), [Text.Encoding]::UTF8) }) -join ''
            $name = "$id-$reason-$machine.txt" -replace '[\\/:*?"<>|]', '_'
            [IO.File]::WriteAllText((Join-Path $Root $name), $text, $utf8)
            Remove-Item -Recurse -Force $dir
            Write-Log "받음: $name ($n 조각)"
            $done++
        }
    }
    return $done
}

if ($Once) { $c = Receive-Once; Write-Log "한 번 받기 완료: 새 보고서 $c 개"; return }
Write-Log "수신기 시작 (중계 주소 $Topic, $Interval 초 간격)"
while ($true) {
    try { [void](Receive-Once) } catch { Write-Log ("수신 실패: " + $_.Exception.Message) }
    Start-Sleep -Seconds $Interval
}
