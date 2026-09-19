# pull.ps1 - update and build BOTH repositories of the project.
#
# The project is split into two sibling repositories:
#   ..\ets2_assist  - this repository (the app)
#   ..\weboverlay   - the overlay host (WebOverlay.exe, shipped in data\bin)
# Both are pulled, otherwise a stale overlay can be built together with a fresh
# app (or the other way round) and a non-existing bug gets hunted for hours.
#
# The build itself is done by compile.ps1: it also builds the sibling WebOverlay
# and copies the fresh WebOverlay.exe into data\bin, so no separate publish of
# the overlay is needed here.
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less scripts as ANSI,
# so non-ASCII characters break parsing (see the file-encoding rule in MemoryAI).

$ErrorActionPreference = 'Stop'

# --- 1. ETS2 Assist (this repository) ---
Write-Host "=== git pull: $PSScriptRoot ===" -ForegroundColor Cyan
& git -C $PSScriptRoot pull
if ($LASTEXITCODE -ne 0) {
    Write-Host "git pull failed in $PSScriptRoot (exit $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

# --- 2. WebOverlay (sibling repository) ---
# Standard development layout: ..\weboverlay
# Worktree layout: the working copy sits one level deeper (repo.worktrees\<name>),
# so the sibling repository is at ..\..\weboverlay there - exactly like compile.ps1.
$overlayRepo = Join-Path $PSScriptRoot '..\weboverlay'
if (-not (Test-Path (Join-Path $overlayRepo '.git'))) {
    $overlayRepo = Join-Path $PSScriptRoot '..\..\weboverlay'
}
if (Test-Path (Join-Path $overlayRepo '.git')) {
    $overlayRepo = (Resolve-Path $overlayRepo).Path
    Write-Host "=== git pull: $overlayRepo ===" -ForegroundColor Cyan
    & git -C $overlayRepo pull
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git pull failed in $overlayRepo (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
} else {
    # Not fatal: compile.ps1 keeps the existing data\bin\WebOverlay.exe in that case.
    Write-Host "WebOverlay repository not found (looked for ..\weboverlay and ..\..\weboverlay) - pull skipped." -ForegroundColor Yellow
}

# --- 3. Build ETS2 Assist (compile.ps1 builds the sibling WebOverlay too) ---
dotnet clean
dotnet restore
& "$PSScriptRoot\compile.ps1"
