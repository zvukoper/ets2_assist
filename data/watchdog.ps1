# =====================================================
# Watchdog – завершает дочерние процессы, если основной скрипт упал
# =====================================================

param(
    [int]$MainPid,
    [int]$CheckInterval = 3   # секунды
)

if (-not $MainPid -or $MainPid -eq 0) {
    Write-Host "Usage: watchdog.ps1 -MainPid <PID>"
    exit 1
}

$processesToKill = @("python", "pythonw", "WebOverlay", "Ets2Telemetry")

Write-Host "Watchdog started (watching PID $MainPid)" -ForegroundColor Cyan

while ($true) {
    try {
        $mainProcess = Get-Process -Id $MainPid -ErrorAction SilentlyContinue
        if (-not $mainProcess) {
            Write-Host "Main process ($MainPid) is dead. Killing child processes..." -ForegroundColor Red
            foreach ($procName in $processesToKill) {
                try {
                    Get-Process -Name $procName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                } catch { }
            }
            break
        }
    } catch {
        break
    }
    Start-Sleep -Seconds $CheckInterval
}