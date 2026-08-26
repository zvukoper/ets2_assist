# ETS2 Assist 1.0.22

- Runtime build synchronized to 1.0.22-RUNTIME-CACHE-POI-2026.08.25-1718.
- Replaced Python static server with local HttpListener that sends no-cache headers while keeping stable WebOverlay URLs.
- Overlays.json parsing explicitly treats every top-level array key as a POI category; the runtime logs all categories and per-category counts.
- This patch is designed to eliminate stale 1.0.20 web assets being shown inside a 1.0.21 desktop build.
- Root data folder is intentionally not included.
