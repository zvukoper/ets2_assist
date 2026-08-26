# ETS2 Assist 1.0.32

Build: `1.0.32-MAP-DATA-APPDATA-2026.08.26-1150`

- Static web and map resources are now committed under the project-root `data` directory and copied automatically during `dotnet publish -c Release`.
- User settings, custom targets, telemetry cache, trigger files and saved tracks are stored in `%LOCALAPPDATA%\ETS2_Assist`.
- Existing web URLs for `custom_targets.json` and `web_data.json` remain compatible through the local HTTP server.
- The hybrid fuel value is displayed as a rounded integer percentage.
- Road GeoJSON coordinates in string format are supported by the minimap loader.
- POI labels and category colors are handled more reliably.
