1.0.30-TRACKMAP-SPLASH-2026.08.26-1015

Fixes:
- Restore verified SplashForm DPI/physical-size implementation from the previously working revision.
- Playback HTML now contains drawMap() and can render without ReferenceError.
- Saved tracks no longer embed the huge roads/cities arrays in the save payload; playback loads static map resources from /GeoJson and /Overlays.json.
- Runtime web map uses a single debug panel.
- Runtime telemetry hydration remains via REST snapshot + delta WebSocket.
- Hybrid build badge is white with a thin black outline.
- Static map diagnostics use explicit absolute resource paths.
