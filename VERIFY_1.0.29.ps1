$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pub = Join-Path $root 'bin\Release\net10.0-windows\win-x64\publish'
Write-Host "Publish: $pub"
@('data\GeoJson\roads.geojson','data\GeoJson\cities.geojson','data\Overlays.json') | ForEach-Object {
  $p=Join-Path $pub $_
  Write-Host "$_ : $(Test-Path $p)"
}
$build=Join-Path $pub 'data\ets2_assist_build.txt'
if(Test-Path $build){Write-Host 'Published build file:'; Get-Content $build}
