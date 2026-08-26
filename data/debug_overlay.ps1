# =====================================================
# Debug Overlay – проверка работы Pano
# =====================================================

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DataRoot = $ScriptRoot
$ProjectRoot = Split-Path -Parent $DataRoot

Write-Host "ProjectRoot: $ProjectRoot" -ForegroundColor Cyan

# Загружаем конфиг
$configPath = Join-Path $DataRoot "config.json"
if (-not (Test-Path $configPath)) {
    Write-Host "config.json not found at $configPath" -ForegroundColor Red
    pause
    exit
}
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$overlay = $config.web.overlay

# Путь к Pano
$paneExe = Join-Path $ProjectRoot $overlay.executable
if (-not (Test-Path $paneExe)) {
    # Альтернативные пути
    $altPaths = @(
        Join-Path $DataRoot "bin\pano.exe",
        Join-Path $DataRoot "pano.exe"
    )
    foreach ($alt in $altPaths) {
        if (Test-Path $alt) {
            $paneExe = $alt
            break
        }
    }
    if (-not (Test-Path $paneExe)) {
        Write-Host "pano.exe not found in any known location." -ForegroundColor Red
        Write-Host "Searched paths:" -ForegroundColor Yellow
        Write-Host "  - $($overlay.executable)" -ForegroundColor Yellow
        foreach ($alt in $altPaths) {
            Write-Host "  - $alt" -ForegroundColor Yellow
        }
        pause
        exit
    }
}

Write-Host "Using Pano: $paneExe" -ForegroundColor Green
Write-Host "File exists: $(Test-Path $paneExe)" -ForegroundColor Green

# Создаём тестовую HTML-страницу
$htmlContent = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Debug Overlay</title>
    <style>
        body { background: rgba(0,0,0,0.7); color: white; font-family: Arial; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; }
        .container { text-align: center; }
        h1 { font-size: 48px; color: #8ab4f8; }
        p { font-size: 24px; }
        .time { font-size: 32px; color: #ffcc66; }
    </style>
</head>
<body>
    <div class="container">
        <h1>?? Debug Overlay</h1>
        <p>Pano is working!</p>
        <p class="time" id="time"></p>
    </div>
    <script>
        setInterval(() => {
            document.getElementById('time').textContent = new Date().toLocaleTimeString();
        }, 1000);
    </script>
</body>
</html>
"@

$debugHtmlPath = Join-Path $DataRoot "debug_overlay.html"
$htmlContent | Set-Content -Path $debugHtmlPath -Encoding UTF8
Write-Host "Created debug HTML at: $debugHtmlPath" -ForegroundColor Green

$port = $config.web.port
$url = "http://localhost:$port/debug_overlay.html"
Write-Host "URL: $url" -ForegroundColor Cyan

# Проверяем HTTP-сервер
try {
    $req = [System.Net.WebRequest]::Create($url)
    $req.Timeout = 2000
    $resp = $req.GetResponse()
    $resp.Close()
    Write-Host "HTTP server is running." -ForegroundColor Green
} catch {
    Write-Host "HTTP server not responding. Trying to start python server..." -ForegroundColor Yellow
    $pyExe = "python"
    $pyScript = "-m http.server $port"
    Start-Process -FilePath $pyExe -ArgumentList $pyScript -WindowStyle Minimized
    Start-Sleep -Seconds 3
}

# Формируем аргументы для Pano (как массив, без лишних кавычек)
$argsList = @(
    "--url", $url,
    "--width", $overlay.width,
    "--height", $overlay.height,
    "-x", $overlay.x,
    "-y", $overlay.y
)
if ($overlay.debug) { $argsList += "--debug" }

# Выводим команду для справки
Write-Host "Command: $paneExe $($argsList -join ' ')" -ForegroundColor Cyan
Write-Host ""

# Запускаем Pano в отдельном окне (видимом)
try {
    Write-Host "Starting Pano..." -ForegroundColor Green
    $process = Start-Process -FilePath $paneExe -ArgumentList $argsList -WindowStyle Normal -PassThru
    Write-Host "Pano started with PID: $($process.Id)" -ForegroundColor Green
    Write-Host "Close the Pano window manually to continue." -ForegroundColor Yellow
    
    # Ждем, пока процесс завершится (это заблокирует скрипт, но для отладки нормально)
    $process.WaitForExit()
    Write-Host "Pano process exited." -ForegroundColor Gray
} catch {
    Write-Host "Failed to start Pano: $_" -ForegroundColor Red
    Write-Host "Try running the command manually in Command Prompt:" -ForegroundColor Yellow
    Write-Host "`"$paneExe`" $($argsList -join ' ')" -ForegroundColor White
    pause
}