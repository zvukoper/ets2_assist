# ETS2 Assist 1.0.26 — clean runtime refresh

This refresh replaces project source and dynamic web/runtime files from a single coherent baseline.
Static user data is intentionally preserved: GeoJson, Overlays.json, localized_cities.
User state is preserved: config.json, custom_targets.json, job_state.json, web_data.json, ws_config.json, saved_tracks.

The splash uses the known-good physical-size ARGB layered implementation.
WebOverlay URLs remain stable; the local static server appends a per-run epoch to local JS/CSS assets.
