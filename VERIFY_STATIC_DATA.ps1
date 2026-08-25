$ErrorActionPreference='Stop'
$root = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64\publish\data'
$required = @(
  'GeoJson\roads.geojson',
  'GeoJson\cities.geojson',
  'Overlays.json',
  'localized_cities\cities_sibirmap.json'
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $root $_)) }
Write-Host "ETS2 Assist static-data check: $root"
if ($missing.Count -gt 0) {
  Write-Host "MISSING STATIC FILES:" -ForegroundColor Red
  $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
  exit 2
}
Write-Host "All required static map files are present." -ForegroundColor Green
