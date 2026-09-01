@echo off
REM ============================================================
REM  ollama_usage.bat v4 - poluchaet tekushchiy % "Session usage"
REM  so stranitsy https://ollama.com/settings
REM  TRABUET RAZOVOGO VHODA: zapusti ollama_login.bat (vydelennyy
REM  profil %LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile).
REM  Realizaciya: data\ollama_usage.ps1 (headless Edge + Start-Process).
REM  Vyhod: SESSION: NN.N | RESET: <vremya do sbrosa> | UNKNOWN | NO_BROWSER
REM  RESET - vremya do sbrosa Session usage (napr. "48 minutes").
REM  Agent: pervaya zadacha v TODO; posle publikacii - progresbar.
REM  PROGNOZ: uchityvat' RESET - esli do sbrosa malo vremeni, a zadacha
REM  bol'shaya, mozhet byt' vygodnee dozhdat'sya sbrosa (limity obnulyatsya).
REM ============================================================
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0data\ollama_usage.ps1"
endlocal