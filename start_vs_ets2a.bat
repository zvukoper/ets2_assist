@echo off
echo ==========================================
echo Pulling latest memory from opencode-memory
echo ==========================================

cd /d "%~dp0..\opencode-memory"
git pull
if errorlevel 1 (
    echo [ERROR] Git pull failed. Check network or repository.
    pause
    exit /b
)
echo Memory repository updated successfully.

cd /d "%~dp0"
echo.
echo Starting ETS2_Assist_GUI.slnx ...
start "" "ETS2_Assist_GUI.slnx"
echo Solution launched.

echo.
echo Press any key to close this window...
pause > nul