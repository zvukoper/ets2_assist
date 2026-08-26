# Data and publish rule

The committed source `data` directory is authoritative. It is copied automatically
by `dotnet publish -c Release` to the runtime directory:

`bin\Release\net10.0-windows\win-x64\publish\data`

The publish directory is generated output and must not be committed. Static web
resources and map data are stored in the repository under `data`.

Mutable user data is stored outside the installation directory at:

`%LOCALAPPDATA%\ETS2_Assist`

This includes settings, custom targets, telemetry cache, trigger files and saved
tracks. The application serves the mutable JSON files through the existing HTTP
URLs, so the web clients do not need a separate path or protocol.

After changing static resources, run:

`dotnet publish -c Release`

The result contains one generated runtime `data` directory next to the EXE.
