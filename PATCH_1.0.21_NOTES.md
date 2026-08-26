# ETS2 Assist 1.0.21 — map/POI/controls fix

- Minimap canvas dimensions are changed only on actual resize; telemetry no longer resets canvas buffers.
- Dedicated 50 ms render heartbeat decouples map repaint from WebSocket delta delivery.
- Static geometry is compiled once into cached Path2D groups; static layer redraw is throttled separately from the dynamic layer.
- POI categories are read from top-level Overlays.json keys, including empty categories. Current file structure: 12 categories, 5 non-empty, 1115 points.
- POI points are colored by their root category and visible points temporarily display category names.
- Only one map debug panel is kept, bottom-right inside the minimap.
- New WebSocket client receives current UI visibility command so Hybrid does not miss the initial show_ui_first broadcast.
- Shift+Ctrl+S sends save_trail directly through the existing 8084 WebSocket, with trigger polling retained only as fallback and protected by a save lock.
- Recording creates data points immediately when steering/throttle/brake/lights change, in addition to distance/event intervals.
- Build identifier: 1.0.22-RUNTIME-CACHE-POI-2026.08.25-1718
