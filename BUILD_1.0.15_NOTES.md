# ETS2 Assist 1.0.15-MAPRENDER-2026.08.25-1448

Canonical runtime build. The only authoritative runtime data path is `bin/Release/net10.0-windows/win-x64/publish/data`.

Map render fix: render-key check occurs before canvas clearing; roads are batched and culled.
Target fix: target persistence loads are blocked while queued target saves are pending.
Splash asset: `data/ets2a_logo.png` is a 314x314 RGBA image prepared from the complete 1254x1254 source with transparent margin.
