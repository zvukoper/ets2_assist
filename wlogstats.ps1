# WORKLOG.md analysis (temporary script, ASCII-only output)
$path = 'F:\repo\ets2_assist\MemoryAI\WORKLOG.md'
$c = Get-Content $path -Encoding UTF8
$starts = @()
for ($i = 0; $i -lt $c.Count; $i++) { if ($c[$i] -match '^## ') { $starts += $i } }
$starts += $c.Count

$blocks = @()
for ($k = 0; $k -lt $starts.Count - 1; $k++) {
    $body = ($c[$starts[$k]..($starts[$k + 1] - 1)] | Where-Object { $_ -match '\S' }) -join "`n"
    $md5 = (Get-FileHash -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($body))) -Algorithm MD5).Hash
    $blocks += [pscustomobject]@{ Line = $starts[$k] + 1; Title = $c[$starts[$k]].Substring(3).Trim(); Len = $body.Length; Hash = $md5 }
}

Write-Output "=== BLOCKS ==="
Write-Output ("total_h2_blocks: {0}" -f $blocks.Count)
Write-Output ("total_block_chars: {0}" -f (($blocks | Measure-Object Len -Sum).Sum))

$g = $blocks | Group-Object Hash
$dups = $g | Where-Object { $_.Count -gt 1 }
$dupVol = 0
foreach ($grp in $dups) { $dupVol += ($grp.Count - 1) * $grp.Group[0].Len }
Write-Output ("dup_groups: {0}" -f $dups.Count)
Write-Output ("dup_excess_chars: {0}" -f $dupVol)
Write-Output "--- top dups ---"
$dups | Sort-Object { -($_.Group[0].Len) } | Select-Object -First 8 | ForEach-Object {
    Write-Output ("  {0}x len={1} line={2}" -f $_.Count, $_.Group[0].Len, $_.Group[0].Line)
}

# near-duplicates: strip all digits before hashing
$blocks2 = @()
for ($k = 0; $k -lt $starts.Count - 1; $k++) {
    $body2 = ((($c[$starts[$k]..($starts[$k + 1] - 1)] | Where-Object { $_ -match '\S' }) -join '') -replace '[0-9]', '')
    $h2 = (Get-FileHash -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($body2))) -Algorithm MD5).Hash
    $blocks2 += [pscustomobject]@{ Line = $starts[$k] + 1; Title = $c[$starts[$k]].Substring(3).Trim(); Len = $body2.Length; Hash = $h2 }
}
Write-Output ""
Write-Output "=== NEAR-DUP (digits stripped) ==="
$g2 = $blocks2 | Group-Object Hash | Where-Object { $_.Count -gt 1 }
$dupVol2 = 0
foreach ($grp in $g2) { $dupVol2 += ($grp.Count - 1) * $grp.Group[0].Len }
Write-Output ("neardup_groups: {0}" -f $g2.Count)
Write-Output ("neardup_excess_chars: {0}" -f $dupVol2)
$g2 | Sort-Object { -($_.Group[0].Len) } | Select-Object -First 10 | ForEach-Object {
    Write-Output ("  {0}x len={1} lines={2}" -f $_.Count, $_.Group[0].Len, (($_.Group | ForEach-Object { $_.Line }) -join ','))
}

Write-Output ""
Write-Output "=== DATES ==="
$dates = @{}
foreach ($b in $blocks) {
    if ($b.Title -match '(\d{2}\.\d{2}\.2026)') {
        $d = $Matches[1]
        if (-not $dates.ContainsKey($d)) { $dates[$d] = 0 }
        $dates[$d]++
    }
}
foreach ($k in ($dates.Keys | Sort-Object { [datetime]::ParseExact($_, 'dd.MM.yyyy', $null) })) {
    Write-Output ("  {0} : {1}" -f $k, $dates[$k])
}

Write-Output ""
Write-Output "=== CONTENT METRICS ==="
$text = ($c -join "`n")
Write-Output ("lines: {0}" -f $c.Count)
Write-Output ("chars: {0}" -f $text.Length)
Write-Output ("bytes_utf8: {0}" -f (Get-Item $path).Length)
Write-Output ("blank_lines: {0}" -f ($c | Where-Object { $_ -notmatch '\S' }).Count)
Write-Output ("mentions_urok: {0}" -f ([regex]::Matches($text, '(?i)\u0443\u0440\u043e\u043a')).Count)
Write-Output ("mentions_pending: {0}" -f ([regex]::Matches($text, '(?i)\u043f\u0435\u043d\u0434\u0438\u043d\u0433')).Count)
Write-Output ("mentions_koren: {0}" -f ([regex]::Matches($text, '(?i)\u043a\u043e\u0440\u0435\u043d\u044c')).Count)
Write-Output ("mentions_todo: {0}" -f ([regex]::Matches($text, 'TODO')).Count)
Write-Output ("mentions_fix: {0}" -f ([regex]::Matches($text, '(?i)\u0444\u0438\u043a\u0441')).Count)

Write-Output ""
Write-Output "=== VERSIONS 1.0.A.B ==="
$v = [regex]::Matches($text, '1\.0\.\d{1,2}\.\d{1,3}') | ForEach-Object { $_.Value } | Sort-Object -Unique
Write-Output ("unique_count: {0}" -f $v.Count)
Write-Output ($v -join ', ')

Write-Output ""
Write-Output "=== TOP FILES ==="
[regex]::Matches($text, '[A-Za-z0-9_\-]+\.(cs|js|html|json|ps1|geojson|sii)') |
    ForEach-Object { $_.Value } | Group-Object | Sort-Object Count -Descending |
    Select-Object -First 25 | ForEach-Object { Write-Output ("  {0,-42} {1}" -f $_.Name, $_.Count) }
