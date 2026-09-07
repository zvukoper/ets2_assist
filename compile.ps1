taskkill /f /im ETS2_Assist.exe
dotnet clean
dotnet restore
dotnet publish -c release
Start-Process ".\bin\Release\net10.0-windows\win-x64\ETS2_Assist.exe" -Verb RunAs