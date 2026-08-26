# Перезапуск оверлея (WebOverlay.exe) с абсолютным путём
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DataRoot = $ScriptRoot
$ProjectRoot = Split-Path -Parent $DataRoot

$configPath = Join-Path $DataRoot "config.json"
$config = Get-Content $configPath -Raw | ConvertFrom-Json

$overlay = $config.web.overlay
$port = $config.web.port
$url = "http://localhost:$port/web_ui_hybrid.html"

# Получаем абсолютный путь
$exePath = $overlay.executable
if (-not [System.IO.Path]::IsPathRooted($exePath)) {
    $exePath = Join-Path $ProjectRoot $exePath
}

# Убить все процессы WebOverlay
Get-Process -Name "WebOverlay" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $exePath) {
    Write-Host "Restarting WebOverlay..."
    # Передаём URL как первый аргумент (без --url)
    Start-Process -FilePath $exePath -ArgumentList "`"$url`"" -WindowStyle Minimized
    Write-Host "Overlay restarted."
} else {
    Write-Host "WebOverlay executable not found at $exePath"
}
pause