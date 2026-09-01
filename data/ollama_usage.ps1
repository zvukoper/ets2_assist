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
    # Время до сброса Session usage: первый "Resets in ..." в блоке Session usage
    # (блок Session usage идёт до блока Weekly usage; берём подстроку до него).
    $wi = $c.IndexOf('Weekly usage', $m.Index)
    $end = if ($wi -gt $m.Index) { $wi } else { [Math]::Min($m.Index + 3000, $c.Length) }
    $seg = $c.Substring($m.Index, $end - $m.Index)
    $rm = [regex]::Match($seg, 'Resets in ([^<]+)')
    $reset = if ($rm.Success) { $rm.Groups[1].Value.Trim() } else { '' }
    if ($reset) { Write-Output ('SESSION: ' + $m.Groups[1].Value + ' | RESET: ' + $reset) }
    else { Write-Output ('SESSION: ' + $m.Groups[1].Value) }
} else {
    $m2 = [regex]::Match($c, '([0-9]+(?:[.,][0-9]+)?)\s*%\s*used')
    if ($m2.Success) { Write-Output ('SESSION: ' + $m2.Groups[1].Value) } else { Write-Output 'SESSION: UNKNOWN' }
}