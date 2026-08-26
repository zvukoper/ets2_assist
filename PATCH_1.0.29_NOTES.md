# ETS2 Assist 1.0.29

1. Restored the previously verified Splash implementation (runtime log had 314x314 source/display/form, dpi=96, pad=0).
2. Removed the process-wide DPI call introduced later; Splash now establishes its own PerMonitorV2 thread context.
3. Fixed the map telemetry WebSocket callback: 1.0.28 referenced undefined `applyTelemetryDelta()`. It now calls `mergeTelemetryData(data, "delta")`.
4. Hybrid build badge is white with thin black outline and restrained shadow.
5. Build ID is 1.0.29-SPLASH-MAP-2026.08.26-1005.

This patch intentionally does not include GeoJson/Overlays/city data.
