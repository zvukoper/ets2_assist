# Генератор viewport_placements (default.sii) для категории SDO.
# Для каждой точки категории создаёт запись камеры, смотрящей на точку
# сверху под углом 30° с расстояния 4 м.
#
# Использование:  .\generate_viewports.ps1 -Category shashlik
# Выход:          viewports_<Category>.sii  (в корне проекта)
#
# ПРЕДПОЛОЖЕНИЯ (проверить на тестовой категории):
#  - Система координат ETS2: X=восток, Y=высота(вверх), Z=юг (север=-Z).
#  - Камера ставится к ЮГУ от точки (смотрит на север), поднята на 2 м,
#    горизонтальное смещение 3.464 м (итого 4 м под 30°).
#  - Rotation = кватернион (w; x, y, z), big-endian hex float.
#  - Identity-камера смотрит вдоль -Z.

param(
    [Parameter(Mandatory=$true)][string]$Category
)

function FloatToHex([float]$f) {
    $bytes = [BitConverter]::GetBytes($f)
    [Array]::Reverse($bytes)   # big-endian
    return ($bytes | ForEach-Object { $_.ToString("x2") }) -join ""
}

$jsonPath = "data\editor_static_data\model_$Category.json"
if (-not (Test-Path $jsonPath)) {
    Write-Error "Файл не найден: $jsonPath"
    exit 1
}

$json = Get-Content $jsonPath -Raw | ConvertFrom-Json
$objects = @($json.objects)
Write-Host "Category '$Category': $($objects.Count) points"

$dist = 4.0
$horiz = $dist * [Math]::Cos([Math]::PI / 6)   # 3.4641
$vert  = $dist * [Math]::Sin([Math]::PI / 6)   # 2.0

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

$now = [int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

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

$outPath = "viewports_$Category.sii"
$outDir = "E:\Users\Docs\Euro Truck Simulator 2\editor\storages"
if (-not (Test-Path $outDir)) {
    Write-Host "Папка не найдена: $outDir — создаю файл в корне проекта."
    $outDir = (Get-Location).Path
}
$outFull = Join-Path $outDir $outPath
[System.IO.File]::WriteAllLines($outFull, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Created: $outFull ($($objects.Count) viewports)"
