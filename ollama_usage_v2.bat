@echo off
REM ============================================================
REM  ollama_usage.bat v3 - poluchaet tekushchiy % "Session usage"
REM  so stranitsy https://ollama.com/settings
REM  TRABUET RAZOVOGO VHODA: zapusti ollama_login.bat (vydelennyy
REM  profil %LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile).
REM  Realizaciya: data\ollama_usage.ps1 (headless Edge + Start-Process).
REM  Vyhod: SESSION: NN.N | UNKNOWN | NO_BROWSER
REM  Agent: pervaya zadacha v TODO; posle publikacii - progresbar.
REM ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0data\ollama_usage.ps1"
endlocal