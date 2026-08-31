$ProgressPreference='SilentlyContinue'
$exe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $exe)) { $exe = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
if (-not (Test-Path $exe)) { Write-Output 'SESSION: NO_BROWSER'; exit }
$prof = Join-Path $env:LOCALAPPDATA 'ETS2_Assist\ollama-edge-profile'
$dst = Join-Path $env:TEMP 'ollama_dump.html'
$err = Join-Path $env:TEMP 'ollama_err.txt'
$p = Start-Process -FilePath $exe -ArgumentList @('--headless=new','--disable-gpu','--no-first-run',("--user-data-dir=`"$prof`""),'--virtual-time-budget=15000','--dump-dom','https://ollama.com/settings') -RedirectStandardOutput $dst -RedirectStandardError $err -PassThru -Wait
$c = Get-Content $dst -Raw -ErrorAction SilentlyContinue
if (-not $c) { $c = '' }
$m = [regex]::Match($c, 'Session usage[^0-9%]*([0-9]+(?:[.,][0-9]+)?)')
if ($m.Success) {
    Write-Output ('SESSION: ' + $m.Groups[1].Value)
} else {
    $m2 = [regex]::Match($c, '([0-9]+(?:[.,][0-9]+)?)\s*%\s*used')
    if ($m2.Success) { Write-Output ('SESSION: ' + $m2.Groups[1].Value) } else { Write-Output 'SESSION: UNKNOWN' }
}