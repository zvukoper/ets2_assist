& "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\ETS2_Assist.exe" --shutdown
# Stage 1: aggressive WebView2 cache wipe
# 1a. Kill any leftover msedgewebview2 processes from previous sessions.
Get-Process -Name msedgewebview2 -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Kill stuck msedgewebview2 PID=$($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 500
# 1b. Remove the per-user WebView2 profile (Service Worker, IndexedDB,
# HTTP cache, GrResourceCache) - the most reliable cache reset.
$userProfileEBWebView = Join-Path $env:LOCALAPPDATA 'ETS2_Assist\EBWebView'
if (Test-Path $userProfileEBWebView) {
    Write-Host "Remove user WebView2 profile: $userProfileEBWebView"
    Remove-Item -LiteralPath $userProfileEBWebView -Recurse -Force -ErrorAction SilentlyContinue
}
# 1c. Remove <exe>.WebView2 folders next to ETS2_Assist.exe (per-process
# GrResourceCache / EBWebView) - both publish/ and bin/ locations.
$publishRoot = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64\publish'
$binRoot     = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64'
foreach ($root in @($publishRoot, $binRoot)) {
    Get-ChildItem -Path $root -Filter '*.WebView2' -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Remove WebView2 cache: $($_.FullName)"
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
# Stage 2: build
dotnet clean
dotnet restore
dotnet publish -c Release
# Stage 3: post-publish cache wipe (in case publish restored anything)
foreach ($root in @($publishRoot, $binRoot)) {
    Get-ChildItem -Path $root -Filter '*.WebView2' -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "[post-publish] Remove WebView2 cache: $($_.FullName)"
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
# Stage 4: launch
Start-Process "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist.exe" -Verb RunAs
