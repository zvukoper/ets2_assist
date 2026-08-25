# ETS2 Assist 1.0.27-FRESH-SYNC-2026.08.25-1820

This refresh updates application and web-runtime files only. Existing static map data are preserved and must remain in `bin\Release\net10.0-windows\win-x64\publish\data`.

Required static files:
- `GeoJson\roads.geojson`
- `GeoJson\cities.geojson`
- `Overlays.json`
- `localized_cities\cities_sibirmap.json`

Runtime fixes:
- process every TruckTel delta frame, not only placement frames;
- direct `save_trail` WebSocket command is handled by the map;
- Hybrid updates speed/fuel/engine on every delta;
- remove legacy duplicate extra-info panel;
- prominent Hybrid build badge;
- preserve Splash 1:1 bitmap dimensions;
- auto-check/recover missing static files from a legacy ancestor `data` directory when possible.
