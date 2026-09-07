# ETS2 Assist

## Map Editor 2: карта высот

Map Editor 2 может использовать заранее сгенерированный raster-рельеф. Источником высот служат точки из `data/editor_static_data/**/*.json` и города из `data/localized_cities/cities_sibirmap.json`. Интерполяция выполняется методом IDW по ближайшим соседям, затем применяется лёгкое Gaussian-сглаживание.

Для подготовки окружения установите Python 3 и зависимости:

```powershell
py -3 -m pip install -r data\map_editor2\tools\requirements-heightmap.txt
```

После этого в Map Editor 2 используйте `Инструменты → Генерировать карту высот`. Генератор создаёт `data\map_editor2\terrain_height.png` и `data\map_editor2\terrain_height_meta.json`. При следующем открытии редактора готовый raster подхватывается автоматически.

По умолчанию используются цвета:

- минимум высоты: `#0f1c06`
- максимум высоты: `#2d4a18`

Параметры генератора хранятся в `%LOCALAPPDATA%\ETS2_Assist\map_editor2_terrain_settings.json`.
