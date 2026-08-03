@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo Starting HTTP server...
start /b pythonw -m http.server 8082
timeout /t 2 /nobreak >nul

set EXE=bin\WebOverlay.exe
set URL=http://localhost:8082/web_ui_hybrid.html

if exist "%EXE%" (
    echo Starting WebOverlay...
    start "" "%EXE%" "%URL%"
) else (
    echo WebOverlay not found. Starting browser...
    start http://localhost:8082/web_ui_hybrid.html
)