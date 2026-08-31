@echo off
REM ============================================================
REM  ollama_login.bat - ODNOKRATNY vhod v Edge (vydelennyy profil)
REM  Otkryvaet https://ollama.com/settings v OBYCHNOM (ne headless)
REM  Edge s DE DIKIROVANNYM profileyem ETS2 Assistant.
REM  Posle vhoda sessiya sohranyaetsya v: %APPDATA%\.. - см. PROFILE ниже.
REM  Zapuskat ODIN raz; dalee ollama_usage_v2.bat chitaet % sam.
REM ============================================================
setlocal
set "PROFILE=%LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile"
set "EDGE=C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" set "EDGE=C:\Program Files\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" (
    echo EDGE NOT FOUND
    exit /b 1
)
start "" "%EDGE%" --no-first-run --user-data-dir="%PROFILE%" "https://ollama.com/settings"
echo OK: otkryto okno Edge (vydelennyy profil). Zalogintes, potom zapustite ollama_usage_v2.bat
endlocal