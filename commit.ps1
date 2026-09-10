# commit.ps1
$tortoiseExe = (Get-Command TortoiseGitProc.exe -ErrorAction SilentlyContinue).Source

if (-not $tortoiseExe) {
    $candidates = @(
        "$env:ProgramFiles\TortoiseGit\bin\TortoiseGitProc.exe",
        "${env:ProgramFiles(x86)}\TortoiseGit\bin\TortoiseGitProc.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            $tortoiseExe = $path
            break
        }
    }
}

if (-not $tortoiseExe) {
    Write-Error "TortoiseGit not found"
    exit 1
}

Start-Process -FilePath $tortoiseExe -ArgumentList "/command:commit /path:."
Write-Host "Commit dialog opened for: $(Get-Location)"

cd $PSScriptRoot