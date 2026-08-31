@echo off
REM ============================================================
REM  ollama_usage.bat - poluchaet tekushchiy % Session usage
REM  so stranitsy https://ollama.com/settings
REM  Vyhod: SESSION: NN.N% | ERROR | UNKNOWN
REM  Agent vyzyvaet pered zadachey i posle publikacii (progresbar).
REM ============================================================
setlocal enabledelayedexpansion
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; try { $r = Invoke-WebRequest -Uri 'https://ollama.com/settings' -UseBasicParsing -TimeoutSec 20 -Headers @{ 'User-Agent'='Mozilla/5.0' }; $c = $r.Content; $m = [regex]::Match($c, 'Session usage[^0-9]*([0-9]+(?:[.,][0-9]+)?)\s*%%'); if ($m.Success) { Write-Output ('SESSION: ' + $m.Groups[1].Value) } else { $m2 = [regex]::Match($c, '([0-9]+(?:[.,][0-9]+)?)\s*%%\s*used'); if ($m2.Success) { Write-Output ('SESSION: ' + $m2.Groups[1].Value) } else { Write-Output 'SESSION: UNKNOWN' } } } catch { Write-Output 'SESSION: ERROR' }"`) do set "RESULT=%%A"
echo %RESULT%
endlocal