# Генератор viewport_placements (default.sii) для ВСЕХ категорий SDO.
# Для каждой точки категории создаёт запись камеры, смотрящей на точку
# сверху под углом 40° с расстояния 10 м.
#
# Использование:  .\generate_viewports_all.ps1
# Выход:          viewports_<имя_файла>.sii в E:\Users\Docs\Euro Truck Simulator 2\editor\storages\
#
# ПРЕДПОЛОЖЕНИЯ (подтверждены тестовой категорией shashlik):
#  - Система координат ETS2: X=восток, Y=высота(вверх), Z=юг (север=-Z).
#  - Камера ставится к ЮГУ от точки (+Z), поднята на vert, смещена на horiz.
#  - Rotation = кватернион (w; x, y, z), big-endian hex float.
#  - Identity-камера смотрит вдоль -Z.

$ErrorActionPreference = "Stop"

function FloatToHex([float]$f) {
    $bytes = [BitConverter]::GetBytes($f)
    [Array]::Reverse($bytes)   # big-endian
    return ($bytes | ForEach-Object { $_.ToString("x2") }) -join ""
}

$sdoDir = "data\editor_static_data"
$outDir = "E:\Users\Docs\Euro Truck Simulator 2\editor\storages"
if (-not (Test-Path $outDir)) {
    Write-Host "Папка не найдена: $outDir — создаю файлы в корне проекта."
    $outDir = (Get-Location).Path
}

$dist = 20.0
$angleDeg = 40.0
$angleRad = $angleDeg * [Math]::PI / 180.0
$horiz = $dist * [Math]::Cos($angleRad)   # 7.6604
$vert  = $dist * [Math]::Sin($angleRad)   # 6.4279

$files = Get-ChildItem $sdoDir -Filter *.json | Where-Object { $_.Name -ne "meta.json" } | Sort-Object Name
Write-Host "Найдено категорий: $($files.Count)"

$now = [int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

foreach ($file in $files) {
    $json = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $objects = @($json.objects)
    if ($objects.Count -eq 0) {
        Write-Host "Пропуск (0 точек): $($file.Name)"
        continue
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("SiiNunit")
    $lines.Add("{")
    $lines.Add("editor_item_storage : _nameless.278.335e.44f8 {")
    $lines.Add(' map_name: "/map/europe.mbd"')
    $lines.Add(" version: 1")
    $lines.Add(" map_items: 0")
    $lines.Add(" map_item_colors: 0")
    $lines.Add(" map_item_names: 0")
    $lines.Add(" map_item_timestamps: 0")
    $lines.Add(" viewport_placements: $($objects.Count)")

    for ($i = 0; $i -lt $objects.Count; $i++) {
        $o = $objects[$i]
        $x = [double]$o.x; $y = [double]$o.y; $z = [double]$o.z

        # Позиция камеры: к югу от точки (+Z), поднята на vert, смещена на horiz.
        $cx = $x
        $cy = $y + $vert
        $cz = $z + $horiz

        # Направление взгляда от камеры к точке (нормализованное).
        $dx = $x - $cx; $dy = $y - $cy; $dz = $z - $cz
        $len = [Math]::Sqrt($dx*$dx + $dy*$dy + $dz*$dz)
        $dx /= $len; $dy /= $len; $dz /= $len

        # Кватернион поворота от (0,0,-1) к (dx,dy,dz).
        $w  = 1 - $dz
        $qx = $dy
        $qy = -$dx
        $qz = 0.0
        $qlen = [Math]::Sqrt($w*$w + $qx*$qx + $qy*$qy + $qz*$qz)
        $w /= $qlen; $qx /= $qlen; $qy /= $qlen; $qz /= $qlen

        $hx  = FloatToHex ([float]$cx)
        $hy  = FloatToHex ([float]$cy)
        $hz  = FloatToHex ([float]$cz)
        $hw  = FloatToHex ([float]$w)
        $hqx = FloatToHex ([float]$qx)
        $hqy = FloatToHex ([float]$qy)
        $hqz = FloatToHex ([float]$qz)

        $lines.Add(" viewport_placements[$i]: (&$hx, &$hy, &$hz) (&$hw; &$hqx, &$hqy, &$hqz)")
    }

    $lines.Add(" viewport_types: $($objects.Count)")
    for ($i = 0; $i -lt $objects.Count; $i++) { $lines.Add(" viewport_types[$i]: free_camera") }
    $lines.Add(" viewport_colors: $($objects.Count)")
    for ($i = 0; $i -lt $objects.Count; $i++) { $lines.Add(" viewport_colors[$i]: 16777215") }
    $lines.Add(" viewport_names: $($objects.Count)")
    for ($i = 0; $i -lt $objects.Count; $i++) { $lines.Add(" viewport_names[$i]: `"`"") }
    $lines.Add(" viewport_timestamps: $($objects.Count)")
    for ($i = 0; $i -lt $objects.Count; $i++) { $lines.Add(" viewport_timestamps[$i]: $now") }
    $lines.Add(" selected_viewport: (0, 0, 0) (1; 0, 0, 0)")
    $lines.Add("}")
    $lines.Add("")
    $lines.Add("}")

    $base = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $outFull = Join-Path $outDir "viewports_$base.sii"
    [System.IO.File]::WriteAllLines($outFull, $lines, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Created: $outFull ($($objects.Count) viewports)"
}

Write-Host "Done."
