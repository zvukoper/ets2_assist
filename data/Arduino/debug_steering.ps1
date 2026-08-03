# Debug Steering Sensitivity
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DataRoot = Split-Path -Parent $ScriptRoot
$ProjectRoot = Split-Path -Parent $DataRoot
$configPath = Join-Path $DataRoot "config.json"
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$langCode = $config.language
$langPath = Join-Path $ProjectRoot "language\$langCode.csv"
$lang = @{}
Import-Csv $langPath | ForEach-Object { $lang[$_.key] = $_.text }
function Get-Text { param($key) return $lang[$key] }

function Get-ArduinoPort {
    $port = Get-CimInstance -ClassName Win32_SerialPort | Where-Object { $_.Description -like "*Arduino Micro*" } | Select-Object -ExpandProperty DeviceID
    if ($port) { return $port }
    $device = Get-CimInstance -ClassName Win32_PnPEntity | Where-Object { $_.PNPDeviceID -like "*VID_2341*PID_8037*" } | Select-Object -First 1
    if ($device -and $device.Name -match "(COM\d+)") { return $matches[1] }
    return $null
}

$comPort = Get-ArduinoPort
if (-not $comPort) {
    Write-Host (Get-Text "arduino_not_found") -ForegroundColor Red
    pause; exit
}
Write-Host ((Get-Text "arduino_found") -f $comPort) -ForegroundColor Green
Write-Host ""
Write-Host "=== Steering Sensitivity Debug ===" -ForegroundColor Cyan
Write-Host "Select mode (sends fake speed to force range):" -ForegroundColor White
Write-Host ""
Write-Host " 1 - Mode 1  ( <30 km/h)  > Range 256-768"
Write-Host " 2 - Mode 2  ( 30-60 km/h) > Range 179-844"
Write-Host " 3 - Mode 3  ( 60-80 km/h) > Range 128-896"
Write-Host " 4 - Mode 4  ( 80-100 km/h) > Range 77-947"
Write-Host " 5 - Mode 5  (100-140 km/h) > Range 38-986"
Write-Host " 6 - Mode 6  ( >140 km/h)  > Range 0-1023"
Write-Host " 0 - Exit"
Write-Host ""
$modeSpeeds = @(15, 45, 70, 90, 120, 150)
while ($true) {
    $key = Read-Host "Select mode"
    if ($key -eq "0") { break }
    $index = [int]$key - 1
    if ($index -ge 0 -and $index -lt $modeSpeeds.Count) {
        $speed = $modeSpeeds[$index]
        Write-Host "> Sending speed: $speed km/h" -ForegroundColor Yellow
        try {
            $port = New-Object System.IO.Ports.SerialPort
            $port.PortName = $comPort; $port.BaudRate = 9600
            $port.Parity = [System.IO.Ports.Parity]::None; $port.DataBits = 8; $port.StopBits = [System.IO.Ports.StopBits]::One
            $port.ReadTimeout = 1000; $port.DtrEnable = $true
            $port.Open(); Start-Sleep -Milliseconds 100
            $dataString = "connected,S:$speed,PB:0,EO:1,F:50"
            $port.WriteLine($dataString)
            $range_line = $null; $stopReading = (Get-Date).AddMilliseconds(500)
            while ((Get-Date) -lt $stopReading) {
                if ($port.BytesToRead -gt 0) {
                    $line = $port.ReadLine().Trim()
                    if ($line -match "RANGE:(\d+),(\d+)") { $range_line = $line; break }
                }
                Start-Sleep -Milliseconds 10
            }
            $port.Close()
            if ($range_line -match "RANGE:(\d+),(\d+)") {
                $min = $matches[1]; $max = $matches[2]
                Write-Host "? Range applied: [$min-$max]" -ForegroundColor Green
            } else { Write-Host "?? No response from Arduino." -ForegroundColor Red }
        } catch { Write-Host "? Error: $_" -ForegroundColor Red }
    } else { Write-Host "? Invalid choice. Please enter 0-6." -ForegroundColor Red }
    Write-Host ""
}
Write-Host "Exited." -ForegroundColor Cyan
pause