Реализовать в `zvukoper/ets2_assist` реальную плоскость дороги по данным колёс TruckTel.

Источник истины:

`truck.world.placement`  
`truck.wheel.position[]`  
`truck.wheel.radius[]`  
`truck.wheel.on_ground[]`  
`truck.wheel.suspension.deflection[]`

Создать единый объект `AR.ArGroundPlane`, который:

- вычисляет мировые точки контакта всех колёс, находящихся на земле;
- строит по ним наклонную 3D-плоскость;
- хранит нормаль плоскости;
- хранит две ортонормальные оси сетки внутри плоскости;
- хранит среднюю высоту контактов;
- хранит высоту плоскости непосредственно под reference point грузовика;
- хранит все данные каждого колеса для диагностики;
- хранит остаток ошибки каждого контакта относительно рассчитанной плоскости.

Расчёт контакта выполнять итерационно:

1. перевести `wheel.position` из vehicle space в world space через уже существующий `ScsCameraPose.Rotate`;
2. начальный контакт = центр колеса − world up × radius;
3. построить `Y = aX + bZ + c`;
4. получить нормаль `(-a, 1, -b)`;
5. пересчитать контакт как `wheelCenter − normal * radius`;
6. повторить несколько итераций;
7. выполнить финальный fit.

Колёса с `on_ground=false` не должны участвовать в fit, но должны сохраняться в диагностическом массиве.

Создать файл:

`AR/ArGroundPlane.cs`

с реализацией из приложенного Markdown-задания.

В `AR/ArGameState.cs` добавить:

```csharp
public ArGroundPlane? GroundPlane;
```

`GroundY` оставить только ради обратной совместимости.

В `MainForm.ArTarget.cs` добавить кэш:

```csharp
private readonly List<Vector3> _arWheelPositions = new();
private readonly List<double> _arWheelRadii = new();
private readonly List<bool> _arWheelOnGround = new();
private readonly List<double> _arWheelSuspensionDeflection = new();

private int _arWheelCount;
private bool _arWheelDataKnown;
private AR.ArGroundPlane? _arGroundPlane;
```

Добавить чтение полных массивов TruckTel и обработку indexed WebSocket keys.

После обновления placement + wheel telemetry пересчитывать:

```csharp
AR.ArGroundPlane.TryBuild(
    _arTruckX,
    _arTruckY,
    _arTruckZ,
    new AR.ScsEuler(_arHeading, _arPitch, _arRoll),
    _arWheelPositions,
    _arWheelRadii,
    _arWheelOnGround,
    _arWheelSuspensionDeflection,
    out var groundPlane)
```

При успешном расчёте:

```csharp
_arGroundPlane = groundPlane;
```

В `PublishArV2Snapshot()` передавать:

```csharp
GroundPlane = _arGroundPlane,
```

В `ar_telemetry` добавить:

```json
"groundPlane": {
  "valid": true,
  "origin": [x,y,z],
  "normal": [nx,ny,nz],
  "axisU": [ux,uy,uz],
  "axisV": [vx,vy,vz],
  "averageWheelHeight": 0,
  "referenceHeight": 0,
  "maxResidual": 0,
  "wheels": []
}
```

Не удалять `wheels[]`: он нужен для дальнейшей диагностики и построения реальной геометрии.

В `data/js/ar_hud.js` добавить состояние и обработчик `groundPlane`.

Полностью отказаться для новой AR1-геометрии от:

```javascript
groundY
groundOffset
PlaneOffsetM
Math.Round(worldX)
Math.Round(worldZ)
```

Новая сетка должна жить непосредственно на `groundPlane`.

Параметры:

```javascript
const GROUND_GRID_RADIUS_M = 150;
const GROUND_GRID_FADE_START_M = 125;
const GROUND_GRID_FADE_END_M = 150;
```

Сетка:

- каждая 1 м — тонкая белая;
- каждая 10 м — толстая оранжевая;
- каждая 50 м — красная;
- 50 м имеет приоритет над 10 м;
- до 125 м alpha постоянная;
- от 125 до 150 м линейный fade;
- после 150 м линия не рисуется.

Центр сетки:

```text
truck.world.placement X/Z
```

но Y центра должен лежать непосредственно на рассчитанной `GroundPlane`.

Оси сетки брать из `GroundPlane.AxisU` и `GroundPlane.AxisV`, а не из мировых X/Z напрямую.

Окружность радиуса 150 м строить как реальную мировую окружность на той же наклонной плоскости, а не как экранный круг.

При создании новой точки:

```text
Camera position
        ↓
Camera Forward
        ↓
Ray
        ↓
intersection with GroundPlane
        ↓
GroundPlane → (u,v)
        ↓
round u/v to integer meters
        ↓
GroundPlane.FromGrid(u,v)
        ↓
final world XYZ
```

Таким образом новая точка всегда лежит:

1. на реальной рассчитанной плоскости;
2. в пересечении метровых линий сетки.

В `ArPlacePinFromViewCenter()` убрать старый snap по `worldX/worldZ`.

Использовать луч:

```csharp
var origin = new Vector3(
    (float)_arCameraPose.X,
    (float)_arCameraPose.Y,
    (float)_arCameraPose.Z);

var dir = _arCameraPose.Forward;

float denom = Vector3.Dot(dir, plane.Normal);

float t = Vector3.Dot(
    plane.Origin - origin,
    plane.Normal) / denom;

var hit = origin + dir * t;
```

Затем:

```csharp
plane.ProjectToGridCoordinates(hit, out double u, out double v);

u = Math.Round(u, MidpointRounding.AwayFromZero);
v = Math.Round(v, MidpointRounding.AwayFromZero);

var snapped = plane.FromGrid(u, v);
```

Использовать `snapped.X/Y/Z` как итоговые координаты точки.

`ArPlacePinAtWorld(x,z)` также перевести на `GroundPlane`: существующий X/Z должен сначала проецироваться на plane, затем snap в `(u,v)` и обратно в world XYZ.

Не использовать:

```csharp
_arTruckY + AR.ArBridge.PlaneOffsetM
```

для высоты новой точки.

Старую `DrawPlaneGrid()` в `AR/ArRenderer.cs`, которая сейчас использует:

```csharp
double planeY = s.GroundY + s.PlaneOffsetM;
```

перевести на `s.GroundPlane`.

Новая плоскость должна быть общей для:

```text
Wheel telemetry
      ↓
ArGroundPlane
      ↓
ArGameState.GroundPlane
      ├── AR1 grid
      ├── point snap
      ├── будущие ray/plane intersections
      └── будущая 3D world-object placement
```

Добавить тесты минимум на:

- горизонтальную плоскость;
- продольный уклон;
- поперечный уклон;
- диагональный уклон;
- поворот грузовика;
- поднятое колесо;
- snap по наклонной плоскости.

Финальная проверка репозитория:

```powershell
.\compile.ps1
```

Не использовать `dotnet publish` напрямую как финальную сборку.