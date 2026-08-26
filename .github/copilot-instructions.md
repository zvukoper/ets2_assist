# Copilot Instructions

## Project Guidelines
- В этом проекте значительная часть web/runtime-ресурсов и статических данных находится в `D:\repo\ets2_assist\bin\Release\net10.0-windows\win-x64\publish\data`. Основные файлы картографических данных: `Overlays.json`, `custom_targets.json`, `localized_cities\cities_sibirmap.json`, `GeoJson\cities.geojson` и `GeoJson\roads.geojson`. При анализе миникарты нужно проверять именно эту publish-папку.
- Финальная сборка проекта выполняется командой `dotnet publish -c Release`. Runtime-папку `bin\Release\net10.0-windows\win-x64\publish\data` нельзя считать исходником, если публикация формирует её заново; статические web-файлы нужно явно сохранять/копировать в publish после публикации или включить в проект.