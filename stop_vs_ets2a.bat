@echo off
echo ==========================================
echo Committing and pushing memory updates
echo ==========================================

cd /d "%~dp0..\opencode-memory"
git add .
git commit -m "Update memory from ETS2_Assist session (%date% %time%)"
if errorlevel 1 (
    echo [INFO] Nothing to commit or commit failed.
) else (
    echo Commit successful.
    git push
    if errorlevel 1 (
        echo [ERROR] Git push failed. Check connection or permissions.
    ) else (
        echo Memory pushed to remote successfully.
    )
)

cd /d "%~dp0"
echo.
echo Done.
echo Press any key to close this window...
pause > nul