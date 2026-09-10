& "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\ETS2_Assist.exe" --shutdown

git pull

cd $env:localappdata\ETS2_Assist\map_overrides\ets2_overrides

git pull

cd $PSScriptRoot

dotnet clean
dotnet restore
 .\compile.ps1