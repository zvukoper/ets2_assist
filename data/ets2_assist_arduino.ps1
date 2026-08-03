# ============================================================
# ETS2 Assist – Main Script (Headless, no interactive prompts)
# ============================================================

param(
    [switch]$Headless   # Если указан, скрипт работает без запросов и минимальным выводом
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DataRoot = $ScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptRoot

# ---- Load config ----
$configPath = Join-Path $DataRoot "config.json"
if (-not (Test-Path $configPath)) { 
    Write-Error "ERROR: config.json not found!"
    exit 1 
}
$config = Get-Content $configPath -Raw | ConvertFrom-Json

# ---- Load language ----
$langCode = $config.language
$langPath = Join-Path $ProjectRoot "language\$langCode.csv"
if (-not (Test-Path $langPath)) {
    Write-Error "ERROR: Language file '$langCode.csv' not found!"
    exit 1
}
$lang = @{}
Import-Csv $langPath | ForEach-Object { $lang[$_.key] = $_.text }
function Get-Text { param($key) return $lang[$key] }

# ---- Generate lang.json for web (в папку data) ----
function Generate-LangJson {
    $langJson = @{}
    $map = @{
        'web_speed'='speed'; 'web_fuel'='fuel'; 'web_rest'='rest'; 'web_job'='job';
        'web_parking'='parking'; 'web_engine'='engine'; 'web_mode'='mode';
        'web_destination'='destination'; 'web_steering_sensitivity'='steering_sensitivity';
        'web_data_updates'='data_updates'; 'web_powered_by'='powered_by';
        'web_status_active'='status_active'; 'web_status_waiting'='status_waiting';
        'web_status_error'='status_error'; 'web_ontime'='ontime'; 'web_hurry'='hurry';
        'web_late'='late'; 'web_max'='max'; 'web_up_to_30'='up_to_30';
        'web_up_to_60'='up_to_60'; 'web_up_to_80'='up_to_80'; 'web_up_to_100'='up_to_100';
        'web_up_to_140'='up_to_140'; 'web_over_140'='over_140';
        'web_range_min'='range_min'; 'web_range_max'='range_max';
        'web_destination_label'='destination_label'
    }
    foreach ($csvKey in $map.Keys) {
        $jsonKey = $map[$csvKey]
        $langJson[$jsonKey] = (Get-Text $csvKey)
    }
    $langJsonPath = Join-Path $DataRoot "lang.json"
    $langJson | ConvertTo-Json | Set-Content -Path $langJsonPath -Encoding UTF8
}
Generate-LangJson

# ---- Paths ----
$jobsFolder = Join-Path $DataRoot "Jobs"
$stateFilePath = Join-Path $DataRoot "job_state.json"
$webDataPath = Join-Path $DataRoot "web_data.json"
$ets2Url = "http://localhost:25555/api/ets2/telemetry"
$baudRate = $config.arduino.baud_rate
$updateIntervalMs = 50

# ---- Helper functions ----
function Get-ArduinoPort {
    $port = Get-CimInstance -ClassName Win32_SerialPort | Where-Object { $_.Description -like "*Arduino Micro*" } | Select-Object -ExpandProperty DeviceID
    if ($port) { return $port }
    $device = Get-CimInstance -ClassName Win32_PnPEntity | Where-Object { $_.PNPDeviceID -like "*VID_2341*PID_8037*" } | Select-Object -First 1
    if ($device -and $device.Name -match "(COM\d+)") { return $matches[1] }
    return $null
}

function Parse-Ets2Time {
    param($timeStr)
    if ([string]::IsNullOrEmpty($timeStr)) { return $null }
    try { return [DateTime]::ParseExact($timeStr, "yyyy-MM-ddTHH:mm:ssZ", $null) }
    catch { return $null }
}

function Parse-Timer {
    param($timeStr)
    if ([string]::IsNullOrEmpty($timeStr)) { return $null }
    if ($timeStr -match "T(\d{2}):(\d{2}):(\d{2})Z") {
        $hours = [int]$matches[1]; $minutes = [int]$matches[2]; $seconds = [int]$matches[3]
        return [math]::Round($hours + $minutes/60 + $seconds/3600, 1)
    }
    return $null
}

function Format-Hours {
    param($hours)
    if ($hours -eq $null -or $hours -le 0) { return "--" }
    $totalMinutes = [math]::Round($hours * 60)
    $h = [math]::Floor($totalMinutes / 60)
    $m = $totalMinutes % 60
    return "$h`:$($m.ToString('00'))"
}

function Load-JobState {
    if (Test-Path $stateFilePath) {
        try { $json = Get-Content $stateFilePath -Raw | ConvertFrom-Json; return $json }
        catch { return $null }
    }
    return $null
}

function Save-JobState {
    param($jobId, $jobInitial, $initialDistance)
    $state = @{ jobId=$jobId; jobInitial=$jobInitial; initialDistance=$initialDistance; timestamp=(Get-Date -Format "yyyy-MM-dd HH:mm:ss") }
    $json = $state | ConvertTo-Json
    Set-Content -Path $stateFilePath -Value $json -Encoding UTF8
}

# ---- Load saved job state ----
$savedState = Load-JobState
$initialJobTime = if ($savedState -and $savedState.jobInitial) { $savedState.jobInitial } else { $null }
$initialDistance = if ($savedState -and $savedState.initialDistance) { $savedState.initialDistance } else { 0 }
$savedJobId = if ($savedState -and $savedState.jobId) { $savedState.jobId } else { $null }

# ---- Main loop (без интерактива) ----
$lastDisplay = ""
$lastBgColor = "Black"
$serverErrorCount = 0
$maxServerErrors = 5
$prevStatus = "unknown"
$arduinoEnabled = $config.arduino.enabled
$arduinoAvailable = $false
$comPort = $null
$speedZoneEnabled = $config.visual.speed_zone.enabled
$speedZoneMin = $config.visual.speed_zone.min_speed
$speedZoneMax = $config.visual.speed_zone.max_speed
$gameDataReceived = $false

# ---- Основной цикл ----
while ($true) {
    $startTime = Get-Date
    if ($arduinoEnabled) {
        $comPort = Get-ArduinoPort
        $arduinoAvailable = ($comPort -ne $null)
    } else { $arduinoAvailable = $false }
    $serverAvailable = $false; $gameConnected = $false
    $speedKMH = 0; $parkBrakeOn = "OFF"; $engineOn = "OFF"; $fuelPercent = 0
    $restHours = 0; $jobHours = 0; $hasJob = $false
    $estimatedHours = 0; $estimatedDistance = 0; $currentJobId = $null; $trailerAttached = $false

    try {
        $response = Invoke-RestMethod -Uri $ets2Url -TimeoutSec 1 -ErrorAction Stop
        $serverAvailable = $true
        $gameConnected = $response.game.connected -eq $true
        $serverErrorCount = 0
        if ($gameConnected) {
            $rawSpeed = $response.truck.speed
            $speedKMH = [math]::Round([math]::Abs($rawSpeed))
            $parkBrakeOn = if ($response.truck.parkBrakeOn) { "ON" } else { "OFF" }
            $engineOn = if ($response.truck.engineOn) { "ON" } else { "OFF" }
            $fuelPercent = [math]::Round(($response.truck.fuel / $response.truck.fuelCapacity) * 100)
            if ($response.game.nextRestStopTime) {
                $restHours = Parse-Timer $response.game.nextRestStopTime
                if ($restHours -eq $null) { $restHours = 0 }
            } else { $restHours = 0 }
            $currentTime = Parse-Ets2Time $response.game.time
            if ($currentTime) {
                if ($response.job -and $response.job.deadlineTime) {
                    $deadline = Parse-Ets2Time $response.job.deadlineTime
                    if ($deadline) {
                        $diff = $deadline - $currentTime
                        if ($diff.TotalSeconds -gt 0) {
                            $jobHours = [math]::Round($diff.TotalHours, 1)
                            $hasJob = $true
                            $src = if ($response.job.sourceCity) { $response.job.sourceCity } else { "" }
                            $dst = if ($response.job.destinationCity) { $response.job.destinationCity } else { "" }
                            $dl = if ($response.job.deadlineTime) { $response.job.deadlineTime } else { "" }
                            $currentJobId = "$src-$dst-$dl"
                        } else { $jobHours = 0; $hasJob = $false; $currentJobId = $null }
                    }
                } else { $hasJob = $false; $currentJobId = $null }
            } else {
                if ($response.job -and $response.job.remainingTime) {
                    $jobHours = Parse-Timer $response.job.remainingTime
                    if ($jobHours -ne $null -and $jobHours -gt 0) {
                        $hasJob = $true
                        $src = if ($response.job.sourceCity) { $response.job.sourceCity } else { "" }
                        $dst = if ($response.job.destinationCity) { $response.job.destinationCity } else { "" }
                        $dl = if ($response.job.deadlineTime) { $response.job.deadlineTime } else { "" }
                        $currentJobId = "$src-$dst-$dl"
                    } else { $jobHours = 0; $hasJob = $false; $currentJobId = $null }
                }
            }
            if ($response.navigation -and $response.navigation.estimatedTime) {
                $est = Parse-Timer $response.navigation.estimatedTime
                if ($est -ne $null) { $estimatedHours = $est } else { $estimatedHours = 0 }
            } else { $estimatedHours = 0 }
            if ($response.navigation -and $response.navigation.estimatedDistance) {
                $estimatedDistance = [int]$response.navigation.estimatedDistance
            } else { $estimatedDistance = 0 }
            if ($response.trailer -and $response.trailer.attached) { $trailerAttached = $response.trailer.attached -eq $true }
            if ($hasJob -and $trailerAttached -and $currentJobId) {
                if ($currentJobId -ne $savedJobId) {
                    $initialJobTime = $jobHours
                    $initialDistance = if ($estimatedDistance -gt 0) { $estimatedDistance } else { 0 }
                    $savedJobId = $currentJobId
                    Save-JobState -jobId $savedJobId -jobInitial $initialJobTime -initialDistance $initialDistance
                    $jobRecord = @{ jobId=$currentJobId; started=(Get-Date -Format "yyyy-MM-dd HH:mm:ss"); source=$src; destination=$dst; initialTime=$initialJobTime; initialDistance=$initialDistance }
                    $jobFile = Join-Path $jobsFolder "$currentJobId.json"
                    $jobRecord | ConvertTo-Json | Set-Content -Path $jobFile -Encoding UTF8
                }
            }
        }
    } catch {
        $serverAvailable = $false; $gameConnected = $false; $serverErrorCount++
        # В headless-режиме просто продолжаем, не выводим ошибки
    }

    # ---- Запись web_data.json всегда, если сервер доступен ----
    if ($serverAvailable) {
        $rangeDesc = if ($speedKMH -lt 1) { (Get-Text "web_max") }
                    elseif ($speedKMH -lt 30) { (Get-Text "web_up_to_30") }
                    elseif ($speedKMH -lt 60) { (Get-Text "web_up_to_60") }
                    elseif ($speedKMH -lt 80) { (Get-Text "web_up_to_80") }
                    elseif ($speedKMH -lt 100) { (Get-Text "web_up_to_100") }
                    elseif ($speedKMH -lt 140) { (Get-Text "web_up_to_140") }
                    else { (Get-Text "web_over_140") }

        $calcMin = 400; $calcMax = 623
        if ($speedKMH -lt 1) { $calcMin = 282; $calcMax = 742 }
        elseif ($speedKMH -lt 30) { $calcMin = 256; $calcMax = 768 }
        elseif ($speedKMH -lt 60) { $calcMin = 179; $calcMax = 844 }
        elseif ($speedKMH -lt 80) { $calcMin = 128; $calcMax = 896 }
        elseif ($speedKMH -lt 100) { $calcMin = 77; $calcMax = 947 }
        elseif ($speedKMH -lt 140) { $calcMin = 38; $calcMax = 986 }
        else { $calcMin = 0; $calcMax = 1023 }

        $webData = @{
            timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
            speed = $speedKMH
            parking = $parkBrakeOn
            engine = $engineOn
            fuel = $fuelPercent
            rangeMin = $calcMin
            rangeMax = $calcMax
            mode = $rangeDesc
            restHours = if ($restHours -ne $null) { $restHours } else { 0 }
            hasJob = $hasJob
            jobRemaining = if ($hasJob) { $jobHours } else { 0 }
            jobInitial = if ($initialJobTime -ne $null -and $initialJobTime -gt 0) { $initialJobTime } else { 0 }
            estimatedHours = $estimatedHours
            estimatedDistance = $estimatedDistance
            initialDistance = $initialDistance
            jobId = $currentJobId
            status = if ($gameConnected) { "active" } else { "waiting_game" }
        } | ConvertTo-Json

        $retries = 3
        while ($retries -gt 0) {
            try {
                [System.IO.File]::WriteAllText($webDataPath, $webData, [System.Text.Encoding]::UTF8)
                break
            } catch {
                $retries--
                if ($retries -eq 0) {
                    try { Set-Content -Path $webDataPath -Value $webData -Encoding UTF8 -ErrorAction Stop } catch { }
                } else {
                    Start-Sleep -Milliseconds 10
                }
            }
        }

        # Если данные получены, но gameConnected = false, то сигнализируем GUI через вывод
        if (-not $gameDataReceived -and $gameConnected) {
            $gameDataReceived = $true
            Write-Host "GAME_DATA_STARTED"
        }
    }

    # ---- Отправка в Arduino (если активен) ----
    if ($arduinoAvailable -and $arduinoEnabled -and $gameConnected) {
        try {
            $port = New-Object System.IO.Ports.SerialPort
            $port.PortName = $comPort; $port.BaudRate = $baudRate
            $port.Parity = [System.IO.Ports.Parity]::None; $port.DataBits = 8; $port.StopBits = [System.IO.Ports.StopBits]::One
            $port.ReadTimeout = 500; $port.DtrEnable = $true
            $port.Open(); Start-Sleep -Milliseconds 10
            $pbValue = if ($parkBrakeOn -eq "ON") { 1 } else { 0 }
            $engValue = if ($engineOn -eq "ON") { 1 } else { 0 }
            $dataString = "connected,S:$speedKMH,PB:$pbValue,EO:$engValue,F:$fuelPercent"
            $port.WriteLine($dataString)
            $port.Close()
        } catch { }
    }

    # ---- Точный интервал ----
    $elapsed = (Get-Date) - $startTime
    $sleepTime = $updateIntervalMs - $elapsed.TotalMilliseconds
    if ($sleepTime -gt 0) { Start-Sleep -Milliseconds $sleepTime }
}