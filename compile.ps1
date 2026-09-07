& "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\ETS2_Assist.exe" --shutdown
dotnet clean
dotnet restore
dotnet publish -c Release
Start-Process "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist.exe" -Verb RunAs