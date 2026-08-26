# ETS2 Assist — Minimap architecture 1.0.18

Static world geometry is compiled once after `loadData()` into Path2D caches grouped by road type, city layer and POI category.

Telemetry updates only the dynamic state and dynamic layer: truck, trail, targets, events, head-view cone and debug values.

The minimap camera transform is applied to cached static paths; road/city/POI coordinates are not re-parsed per telemetry frame.

The duplicate `extraInfo` panel was removed. One debug overlay remains inside the minimap bounds.
