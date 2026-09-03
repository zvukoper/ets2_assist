# =====================================================================
# SDO-парсер (Static Data Objects): raw\*.txt -> *.json (+ отчёт по meta.json)
# Формат строки raw: [prefix] (uid 0x...);[дата] (sec+XXXX-YYYY);X;Y;Z
#   X,Y,Z — мировые метры игры (ЕДИНАЯ СК с Overlays.json/городами/миникартой/АР);
#   floor(X/4000) = sectorX, floor(Z/4000) = sectorZ (проверяется ниже);
#   Y — высота (в JSON сохраняется, конвейером пока игнорируется: minicарта/АР 2D).
# Правила:
#   - пустые файлы НЕ обрабатываются;
#   - категория = имя файла без префикса model_/overlay_ (для easter_* — "Easter eggs");
#   - имя объекта: easter_* -> часть имени файла после первого '_' (cottage, garage...);
#     остальные — по uid (в JSON имя-префикс строки НЕ пишется);
#   - meta.json НЕ перезаписывается: если отсутствует — создаётся скелет;
#     отсутствующие категории репортируются в консоль (C# работает и без meta).
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File parse_raw.ps1
# =====================================================================
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$raw  = Join-Path $root 'raw'

$rxUid  = [regex]'uid\s*0x([0-9A-Fa-f]+)'
$rxSec  = [regex]'\(sec([+-])0*(\d+)([+-])0*(\d+)\)'
$rxTail = [regex]';\s*(-?\d+(?:\.\d+)?)\s*;\s*(-?\d+(?:\.\d+)?)\s*;\s*(-?\d+(?:\.\d+)?)\s*$'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$metaPath = Join-Path $root 'meta.json'
$metaMissingReport = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $metaPath)) {
    # Скелет meta.json создаётся только при отсутствии (ASCII — правится вручную).
    $skeleton = [ordered]@{
        comment = 'SDO categories reference: name = readable name (empty = key); color = "#rrggbb" (empty = default); icon = png 50x50 file name in icons\ (empty = colored dot).'
        categories = [ordered]@{}
    }
    [IO.File]::WriteAllText($metaPath, ($skeleton | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    Write-Host 'meta.json создан (скелет).'
}

$metaKeys = @()
try {
    $mj = Get-Content $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $metaKeys = @($mj.categories.PSObject.Properties.Name)
} catch { Write-Warning "meta.json не читается: $($_.Exception.Message)" }

$report = New-Object System.Collections.Generic.List[string]
$totalObjects = 0
$totalFiles = 0

Get-ChildItem $raw -Filter *.txt | Sort-Object Name | ForEach-Object {
    $file = $_
    $base = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $lines = @(Get-Content $file.FullName | Where-Object { $_ -match '\S' })
    if ($lines.Count -eq 0) { $report.Add("SKIP (пустой): $($file.Name)"); return }

    $isEaster = $base -like 'easter_*'
    $catKey  = if ($isEaster) { 'Easter eggs' } else { $base -replace '^[^_]*_', '' }
    $objName = $base -replace '^[^_]*_', ''   # часть после первого подчёркивания

    $objs   = New-Object System.Collections.Generic.List[object]
    $mismatch = 0
    $skipped  = 0

    foreach ($ln in $lines) {
        $mu = $rxUid.Match($ln);  if (-not $mu.Success)  { $skipped++; continue }
        $ms = $rxSec.Match($ln);  if (-not $ms.Success)  { $skipped++; continue }
        $mt = $rxTail.Match($ln); if (-not $mt.Success)  { $skipped++; continue }

        $sx = [int]$ms.Groups[2].Value; if ($ms.Groups[1].Value -eq '-') { $sx = -$sx }
        $sz = [int]$ms.Groups[4].Value; if ($ms.Groups[3].Value -eq '-') { $sz = -$sz }
        $x = [double]$mt.Groups[1].Value
        $y = [double]$mt.Groups[2].Value
        $z = [double]$mt.Groups[3].Value

        # Контроль СК: сектор должен совпадать с floor(координата/4000).
        $fx = [math]::Floor($x / 4000.0)
        $fz = [math]::Floor($z / 4000.0)
        if ($fx -ne $sx -or $fz -ne $sz) { $mismatch++ }

        $o = [ordered]@{
            uid      = ('0x' + $mu.Groups[1].Value)
            sector   = $ms.Value.Trim('(', ')')
            sectorX  = $sx
            sectorZ  = $sz
            x        = $x
            y        = $y
            z        = $z
        }
        if ($isEaster) { $o['name'] = $objName }  # объекты easter называются после '_'
        $objs.Add([PSCustomObject]$o)
    }

    if ($objs.Count -eq 0) { $report.Add("SKIP (нет валидных строк): $($file.Name)"); return }

    $json = [ordered]@{
        category = $catKey
        source   = $file.Name
        easter   = $isEaster
        count    = $objs.Count
        objects  = $objs
    }
    $outPath = Join-Path $root ($base + '.json')
    [IO.File]::WriteAllText($outPath, ($json | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    $totalObjects += $objs.Count
    $totalFiles++
    $report.Add(("OK: {0} -> {1}.json объектов={2} пропуск_строк={3} несовпадений_сектора={4}" -f $file.Name, $base, $objs.Count, $skipped, $mismatch))

    if ($metaKeys -notcontains $catKey) { $metaMissingReport.Add($catKey) }
}

Write-Host '=== SDO ПАРСИНГ ==='
$report | ForEach-Object { Write-Host $_ }
Write-Host ("ИТОГ: файлов={0} объектов={1}" -f $totalFiles, $totalObjects)
if ($metaMissingReport.Count -gt 0) {
    Write-Warning ('Категории, отсутствующие в meta.json (добавьте вручную): ' + ($metaMissingReport -join ', '))
}