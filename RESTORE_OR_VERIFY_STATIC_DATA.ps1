$ErrorActionPreference='Stop'
$publish = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64\publish\data'
$required = @('GeoJson\roads.geojson','GeoJson\cities.geojson','Overlays.json','localized_cities\cities_sibirmap.json')
Write-Host "Publish data: $publish"
$missing = $required | Where-Object { -not (Test-Path (Join-Path $publish $_)) }
if (-not $missing) { Write-Host 'STATIC DATA OK'; exit 0 }
Write-Host 'Missing static data:' -ForegroundColor Yellow
$missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
# Search up to 8 parent levels for legacy data folder. Never overwrite an existing destination file.
$dir = (Get-Item $publish).Parent
$found = $false
for ($i=0; $i -lt 8 -and $dir; $i++) {
  $candidate = Join-Path $dir.FullName 'data'
  if (Test-Path $candidate) {
    $hasAny = $required | Where-Object { Test-Path (Join-Path $candidate $_) }
    if ($hasAny) {
      $found = $true
      foreach ($rel in $required) {
        $src = Join-Path $candidate $rel; $dst = Join-Path $publish $rel
        if ((Test-Path $src) -and -not (Test-Path $dst)) {
          New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
          Copy-Item $src $dst
          Write-Host "Recovered $rel" -ForegroundColor Green
        }
      }
      break
    }
  }
  $dir = $dir.Parent
}
if (-not $found) { Write-Host 'No legacy data folder with static map files was found.' -ForegroundColor Red }
$stillMissing = $required | Where-Object { -not (Test-Path (Join-Path $publish $_)) }
if ($stillMissing) { Write-Host 'Still missing:' -ForegroundColor Red; $stillMissing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }; exit 2 }
Write-Host 'STATIC DATA OK after recovery.' -ForegroundColor Green
