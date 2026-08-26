# ETS2 Assist 1.0.20

- Delta telemetry updates dynamic state on every TruckTel frame.
- processUpdate uses state.truck instead of stale DOM fields.
- Roads are compiled once into cached Path2D groups.
- Map orientation follows SCS/TruckTel heading.
- Shift+Ctrl+S sends direct `save_trail` to the connected map while paused, with trigger fallback.
- Recording includes steering, throttle, brake, input axes, lights, headOffset and cargo damage.
- Single bottom-right telemetry panel; POI count/category count.
- Removed thick inset map border.
- Distance markers explicitly have filled backgrounds.
- Build: 1.0.20-DYNAMIC-MAP-2026.08.25-1635

- Removed periodic custom_targets reload that overwrote live random targets.
- Record a data frame immediately when steering/throttle/brake/lights/head changes, not only every 25 m.
- Event icons parking/distance/stop force a filled background.
