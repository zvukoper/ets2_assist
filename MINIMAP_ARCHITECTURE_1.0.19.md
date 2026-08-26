# ETS2 Assist 1.0.19 — Minimap and playback architecture

- Static roads/cities/POI are compiled once into Path2D caches after GeoJSON/Overlays load.
- Telemetry updates only dynamic state and redraws cached geometry with a camera transform.
- The dynamic layer contains grid, trail, targets, events, truck and head-view cone.
- `truck.world.placement` heading follows TruckTel/SCS: 0=north/forward, 0.25=left, 0.5=backward, 0.75=right.
- `truck.head.offset[3]` is interpreted with the same convention; 0=forward, 0.25=left, 0.75=right.
- Playback HTML defines `drawMap()` before `resize()` and reuses cached road Path2D groups.
- POI count and category count are displayed in the single lower-right diagnostics panel.
