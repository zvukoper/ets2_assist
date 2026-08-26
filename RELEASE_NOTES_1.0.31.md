# ETS2 Assist 1.0.31

- Playback HTML uses only embedded static snapshot from `.map.json`/HTML; no GeoJson/Overlays fetch.
- Track playback opens as local HTML file from `saved_tracks`.
- Recorded static snapshot includes cities, roads, POI, POI categories/counts and custom targets.
- Save hotkey pauses ETS2 through SCS SDK, waits for confirmed pause, then requests save. It does not require the game to already be paused and does not unpause automatically.
- Added GUI button `Сбросить начало записи трека`; saving no longer resets the accumulated recording.
- Live telemetry normalization uses TruckTel flat keys for speed, engine, fuel, rest, steering, throttle, brake, odometer, head offset, wear, trailer and lights.
- Hybrid HTTP polling no longer overwrites valid TruckTel WebSocket values with zero/default values.
- Added brake indicator to Hybrid; trailer and lights indicators remain at lower-left/lower-right.
- Added head-view cone to live minimap and playback.
- Playback truck marker is vertically mirrored.
- Static map loader has absolute localhost fallback and retry when static data did not load.
- Splash runtime uses 314x314 full RGBA logo and the verified DPI configuration.
