Get-Process -Name ETS2_Assist -ErrorAction SilentlyContinue | Out-Null
if ($?) {
    Write-Host "ETS2_Assist is running, sending shutdown..."
    & "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\ETS2_Assist.exe" --shutdown
} else {
    Write-Host "ETS2_Assist is not running, skipping shutdown."
}
# Stage 1: aggressive WebView2 cache wipe
# 1a. Kill any leftover msedgewebview2 processes from previous sessions.
Get-Process -Name msedgewebview2 -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Kill stuck msedgewebview2 PID=$($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
# Also kill the overlay host: it holds the WebView2 profile locks open and would
# otherwise re-create its cache right after we delete it.
Get-Process -Name WebOverlay -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Kill WebOverlay PID=$($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

$publishRoot = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64\publish'
$binRoot     = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\win-x64'

# 1b. Remove the per-user WebView2 profiles. ETS2_Assist (main window) uses
# %LOCALAPPDATA%\ETS2_Assist\EBWebView; WebOverlay stores its profile NEXT TO ITS
# OWN EXE (data\bin\WebOverlay.exe.WebView2) — see 1c.
$userProfiles = @(
    (Join-Path $env:LOCALAPPDATA 'ETS2_Assist\EBWebView'),
    (Join-Path $env:APPDATA 'ETS2_Assist\EBWebView')
)
foreach ($prof in $userProfiles) {
    if (Test-Path $prof) {
        Write-Host "Remove user WebView2 profile: $prof"
        Remove-Item -LiteralPath $prof -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 1c. Remove EVERY WebView2 profile under publish/ and bin/, RECURSIVELY.
# ROOT CAUSE of "AR HUD (web_ar_hud.html) shows stale content": the overlay host
# WebOverlay.exe keeps its WebView2 user-data folder at
#   publish\data\bin\WebOverlay.exe.WebView2\EBWebView
# i.e. TWO levels below publish. The previous version of this script scanned only
# the TOP level of publish/ and bin/ (Get-ChildItem without -Recurse), so this
# ~34 MB profile - including Cache / Code Cache - was NEVER deleted and the AR HUD
# page kept being served from it.
foreach ($root in @($publishRoot, $binRoot)) {
    if (-not (Test-Path $root)) { continue }
    $profiles = @(Get-ChildItem -Path $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*.WebView2' -or $_.Name -eq 'EBWebView' })
    # Deepest paths first: delete children before their parents.
    $profiles = $profiles | Sort-Object { $_.FullName.Length } -Descending
    foreach ($p in $profiles) {
        if (Test-Path -LiteralPath $p.FullName) {
            Write-Host "Remove WebView2 profile (recursive): $($p.FullName)"
            Remove-Item -LiteralPath $p.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
# Stage 1d: JavaScript syntax guard. Fail before build if the Quest page contains
# a syntax error; otherwise WebView2 would silently render a blank/non-functional page.
$questJs = Join-Path $PSScriptRoot 'data\\js\\quests_ui.js'
if (Test-Path $questJs) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        Write-Host "Checking Quest JavaScript syntax: $questJs"
        & $node.Source --check $questJs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Quest JavaScript syntax check FAILED." -ForegroundColor Red
            exit $LASTEXITCODE
        }
        Write-Host "Quest JavaScript syntax check OK." -ForegroundColor Green
    } else {
        Write-Host "Node.js not found; Quest JavaScript syntax check skipped." -ForegroundColor Yellow
    }
}

# Stage 2: build
# WebOverlay is maintained in the sibling repository f:\repo\weboverlay in the
# standard development layout. Build it first so data\bin\WebOverlay.exe used by
# ETS2 Assist always contains the current window-state/default-position fixes.
$webOverlayProject = Join-Path $PSScriptRoot '..\weboverlay\WebOverlay.csproj'
# Worktrees live one level deeper (repo.worktrees\<name>), so the sibling repo is
# at ..\..\weboverlay there. Resolve it explicitly — otherwise the overlay host is
# silently left stale instead of being delivered.
if (-not (Test-Path $webOverlayProject)) {
    $webOverlayProject = Join-Path $PSScriptRoot '..\..\weboverlay\WebOverlay.csproj'
}
$webOverlayPublish = Join-Path $PSScriptRoot 'obj\WebOverlayPublish'
$webOverlayExe = Join-Path $webOverlayPublish 'WebOverlay.exe'
if (Test-Path $webOverlayProject) {
    Write-Host "Building sibling WebOverlay: $webOverlayProject"
    if (Test-Path $webOverlayPublish) {
        Remove-Item -LiteralPath $webOverlayPublish -Recurse -Force -ErrorAction SilentlyContinue
    }
    & dotnet publish $webOverlayProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $webOverlayPublish
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WebOverlay publish failed with exit code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
    $webOverlayTarget = Join-Path $PSScriptRoot 'data\bin\WebOverlay.exe'
    if (-not (Test-Path $webOverlayExe)) {
        Write-Host "WebOverlay publish completed but WebOverlay.exe was not produced: $webOverlayExe" -ForegroundColor Red
        exit 1
    }
    Copy-Item -LiteralPath $webOverlayExe -Destination $webOverlayTarget -Force
    $woVersion = $null
    try { $woVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($webOverlayTarget).ProductVersion } catch { }
    Write-Host "WebOverlay delivered: version=$woVersion path=$webOverlayTarget" -ForegroundColor Cyan
    if ($woVersion -notlike "1.0.40.78*") {
        Write-Host "WebOverlay version CHECK FAILED: expected 1.0.40.77*, got $woVersion" -ForegroundColor Red
        exit 1
    }
    Write-Host "Updated ETS2 Assist data\bin\WebOverlay.exe from sibling WebOverlay build." -ForegroundColor Green
} else {
    Write-Host "Sibling WebOverlay repository not found at $webOverlayProject; keeping existing data\bin\WebOverlay.exe." -ForegroundColor Yellow
}

dotnet clean
dotnet restore
dotnet publish -c Release
# Publish must succeed BEFORE any post-publish cleanup. Otherwise a failed build
# would wipe the WebView2 cache, the diagnostic logs in MemoryAI\LOGS and then
# launch a stale EXE - i.e. destroy exactly the evidence needed to diagnose it.
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet publish failed with exit code $LASTEXITCODE - aborting post-publish cleanup." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Stage 3: post-publish cache wipe (recursive — see 1c for the root cause) and
# verification that the freshly built web content really reached publish\data.
foreach ($root in @($publishRoot, $binRoot)) {
    if (-not (Test-Path $root)) { continue }
    $profiles = @(Get-ChildItem -Path $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*.WebView2' -or $_.Name -eq 'EBWebView' })
    $profiles = $profiles | Sort-Object { $_.FullName.Length } -Descending
    foreach ($p in $profiles) {
        if (Test-Path -LiteralPath $p.FullName) {
            Write-Host "[post-publish] Remove WebView2 profile: $($p.FullName)"
            Remove-Item -LiteralPath $p.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# Stage 3b: DELIVERY CHECK — the source data\ must be delivered into publish\data.
# Guarantee that the built web content is what the overlay actually serves
# (no silent "old code in the build").
# Hashing EVERY file would mean ~564 MB per build (bin\ holds two ~166 MB EXEs),
# so: full MD5 comparison for the WEB CONTENT (html/js/css/json/ico - what the
# overlay loads), size comparison for everything else.
$srcData = Join-Path $PSScriptRoot 'data'
$pubData = Join-Path $publishRoot 'data'
$webExt = @('.html', '.js', '.css', '.json', '.ico', '.png', '.svg')
$mismatch = @()
$hashed = 0
# Reset per build; becomes $true only after a fully verified delivery check.
$deliveryOk = $false
if ((Test-Path $srcData) -and (Test-Path $pubData)) {
    foreach ($src in Get-ChildItem -Path $srcData -Recurse -File -ErrorAction SilentlyContinue) {
        if ($src.FullName -like '*WebOverlay.exe.WebView2*') { continue }   # runtime cache, never published
        $rel = $src.FullName.Substring($srcData.Length).TrimStart('\')
        $dst = Join-Path $pubData $rel
        if (-not (Test-Path $dst)) { $mismatch += "MISSING: $rel"; continue }
        if ((Get-Item $dst).Length -ne $src.Length) { $mismatch += "SIZE: $rel"; continue }
        if ($webExt -contains $src.Extension.ToLowerInvariant()) {
            $hashed++
            if ((Get-FileHash $src.FullName -Algorithm MD5).Hash -ne (Get-FileHash $dst -Algorithm MD5).Hash) {
                $mismatch += "HASH: $rel"
            }
        }
    }
}
if ($mismatch.Count -gt 0) {
    Write-Host "PUBLISH DELIVERY CHECK FAILED ($($mismatch.Count) files):" -ForegroundColor Red
    $mismatch | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "publish\data does NOT match data\ - the overlay would serve stale content." -ForegroundColor Red
    $deliveryOk = $false
} else {
    Write-Host "Publish delivery check OK: web content byte-identical ($hashed files hashed), all sizes match." -ForegroundColor Green
    $deliveryOk = $true
}

# Stage 3c: WebView2 profiles must be gone, otherwise the overlay may reuse cache.
$leftover = @()
foreach ($root in @($publishRoot, $binRoot)) {
    if (-not (Test-Path $root)) { continue }
    $leftover += @(Get-ChildItem -Path $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*.WebView2' -or $_.Name -eq 'EBWebView' })
}
if ($leftover.Count -gt 0) {
    Write-Host "WebView2 cache NOT fully cleared (still present):" -ForegroundColor Yellow
    $leftover | Select-Object -First 10 -ExpandProperty FullName | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
} else {
    Write-Host "WebView2 cache cleared (no *.WebView2 / EBWebView folders remain)." -ForegroundColor Green
}

# Stage 3d: temporary diagnostic logs must not survive a successful build.
# MemoryAI\LOGS is a drop zone for logs the user hands over for analysis; per the
# project rules only the service README.md stays after a successful publish.
$logsDir = Join-Path $PSScriptRoot 'MemoryAI\LOGS'
if (-not $deliveryOk) {
    Write-Host 'MemoryAI\LOGS cleanup SKIPPED: publish delivery check failed, keep the logs for diagnosis.' -ForegroundColor Yellow
} elseif (Test-Path $logsDir) {
    $logsToDelete = @(Get-ChildItem -LiteralPath $logsDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'README.md' })
    # Subfolders (e.g. unpacked archives) go as well - they are never service files.
    $logsDirsToDelete = @(Get-ChildItem -LiteralPath $logsDir -Directory -ErrorAction SilentlyContinue)
    $removedLogs = 0
    foreach ($f in $logsToDelete) {
        Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $f.FullName)) { $removedLogs++ }
    }
    foreach ($d in $logsDirsToDelete) {
        Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path (Join-Path $logsDir 'README.md'))) {
        Write-Host "MemoryAI\LOGS cleanup WARNING: README.md is missing from $logsDir" -ForegroundColor Yellow
    }
    if ($removedLogs -gt 0) {
        Write-Host "MemoryAI\LOGS cleaned: $removedLogs temporary file(s) removed (README.md kept)." -ForegroundColor Green
    } else {
        Write-Host "MemoryAI\LOGS already clean (only README.md)." -ForegroundColor Green
    }
} else {
    Write-Host "MemoryAI\LOGS not found, skipping log cleanup." -ForegroundColor Yellow
}

# Stage 4: launch
Start-Process "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist.exe" -Verb RunAs
