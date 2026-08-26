# ETS2 Assist 1.0.23

- Overlay window URLs remain stable so WebOverlay can preserve X/Y/size settings.
- A single Unix epoch-seconds cache token is generated once per application start.
- All local JS/CSS references inside served HTML are rewritten to `?t=<epoch>`. Existing `?v=` values are replaced.
- Static server sends no-cache headers plus `X-ETS2-Assist-Build` and `X-ETS2-Assist-Cache-Epoch`.
- Startup logs now fingerprint `web_pda_map.html` and `web_ui_hybrid.html` from the actual publish data directory, making stale runtime assets immediately visible.
- Root `data` folder is intentionally not included.
