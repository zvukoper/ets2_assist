@echo off
echo Creating folders and files for ETS2 Assist refactoring...

mkdir data\css 2>nul
mkdir data\js 2>nul
mkdir Quests 2>nul

echo. > data\css\map.css
echo. > data\js\config.js
echo. > data\js\state.js
echo. > data\js\map.js
echo. > data\js\trail.js
echo. > data\js\save.js
echo. > data\js\targets.js
echo. > data\js\websocket.js
echo. > data\js\ui.js
echo. > data\js\init.js
echo. > Quests\QuestsManager.cs

echo Folders and files created successfully.
pause