﻿# ETS2_Assist_GUI — заметки сессии (Hy3)

> Папка для памяти между сессиями. Обновляется вручную в конце каждой сессии.

## Сессия v81 — ИТОГИ ТЕСТА ПОЛЬЗОВАТЕЛЕЙ (31.08.2026, 18:34–18:36)
- Симптомы: розовый кружок РОВНО В ЦЕНТРЕ экрана (появляется/пропадает);
  иногда — УВЕЛИЧЕННЫЙ кружок «где-то в пространстве».
- Это два РАЗНЫХ бага, оба объяснимы кодом v81:

### Вывод 1 — центр-прицел вместо маркера (главный)
- ProjectMarker возвращал null → рендер рисовал fallback-прицел (r=10px) в
  центре — v81 сделан намеренно («индикатор живости»). Появлялся/пропадал —
  значит цель то проходит fdot-отсечку (`fdot <= 0.5`), то нет.
- ГЛУБИННАЯ ПРИЧИНА null-проекции (подтверждено логами): прицельный луч
  считался по `yaw = YawBase*2π + YawHead`, но fdot/rdot-отсекание «позади»
  не учитывало, что в момент остановки цели менялись при развороте головы
  (h скачет 0.139 → 0.859 в tick-логе → цель за спиной → прицел).
- Задачи v82 (в порядке приоритета):
  1. **Композитный питч** — сейчас учитывается ТОЛЬКО PitchHead; в AR v1
     (ar_hud.js v75) последовательны: кузов (placement[4]) вокруг right →
     голова (head.offset[4]). Питч кузова y=34.5 м (мост!) даёт огромную
     вертикальную ошибку → маркер «не там».
  2. **Y-компонента цели**: ArPoint для poi/target создаётся с y=0, а
     truckY=34.5 (мост) — wy = (0+0.5)−(34.5+1.9) ≈ −35.9 м вниз → при
     близкой цели dist 98–152 м маркер уводит ГЛУБОКО ВНИЗ за экран.
     Город laishevo имел y≠0, а Company-POI 0x63DF0052508005DA — нет.
     Нужно: брать y из overrides-модели (ArPoint.y уже есть в city-ветке),
     для poi с y=0 — clamp/проекция на groundY, как в displayYFor ar_hud.js.
  3. **Fallback-прицел убрать/заменить** после починки 1–2 (сейчас он
     маскирует настоящие проекции; виден как «розовый кружок в центре»).
  4. Fade по дистанции (500→1500м) + радиус по dist (10/500м) — из ar_hud.js.
  5. Города/пин как полноценные элементы (вторые draw-вызовы).
- Цвет «розовый»: в PMain круг красный (1.0,0.45,0.45) — на bitblt+BGR
  свопчейне выглядит розовым; при доработке цветов учесть.

### Вывод 2 — «увеличенный кружок в пространстве»
- Это УЖЕ настоящий маркер (r=33px): цель была в направлении взгляда и
  проекция считалась. Пропадал/появлялся из-за:
  a) смена цели при развороте (laishevo ↔ Company-POI в логах);
  b) fdot-отсечка при поворотах головы;
  c) вертикальная ошибка из-за pitч кузова (см. выше) — цель рисовалась
     выше/ниже реального положения, «в пространстве».
- Вывод: пайплайн данных (bridge → snapshot → проекция → draw) РАБОТАЕТ
  end-to-end. Осталась точность проекции (пункт 1–2 выше).

### Что подтверждено практикой (не трогать!)
- Swap chain: bitblt (Sequential) + Stretch + Flags.None + Present(1) vsync —
  ЕДИНСТВЕННАЯ рабочая комбинация для COLORKEY-окна на Win10 19045 (v76-v80).
- Мост ArBridge: цель пишется в ArUpdateTick при подборе (v81 фикс).
- Паблиш-бейджи: exe=build.txt=manifest синхронно (v81: 1.0.38.81-AR2-
  TARGET-BRIDGE-08.31-1828).

### Задача пользователя на следующую сессию (дословно)
«может сегодня позже попробуем хотя бы привязать новую точку цель к точке
в пространстве» — т.е. привязка новой цели к позиции AR-маркера (пин → точка).

## Сессия v80 (архив) — Scaling.None был вторым INVALID_CALL
- **v80 ОПУБЛИКОВАН:** 1.0.38.80-AR2-BITBLT-STRETCH-08.31-1818 (exe=txt=manifest OK).
- Разбор логов: BUILD_VERSION подтверждал — ошибка 18:14 была уже на v79
  (bitblt + Flags.None). Значит второй INVALID_CALL-параметр остался.
- Найдено: **`Scaling.None` валиден только для flip** (Windows 8+; в Win10 19045
  bitblt-свопчейн с None → DXGI_ERROR_INVALID_CALL). Фикс v80:
  `Scaling.Stretch` (правильно для bitblt) + Sequential + Flags.None.
- ИТОГОВАЯ матрица swap chain для COLORKEY-оверлея (все три подтверждены
  практикой): flip+colorkey=чёрный экран; bitblt+waitable=INVALID_CALL;
  bitblt+ScalingNone=INVALID_CALL; **bitblt+Stretch+без флагов = v80**.

## Сессия v79 (архив)
- **v79 ОПУБЛИКОВАН:** 1.0.38.79-AR2-BITBLT-NOWAIT-08.31-1812 (exe=txt=manifest OK).
- Убран waitable-флаг (валиден только для flip): Flags.None, ResizeBuffers None,
  Present(1) vsync, `WaitForNextFrame` no-op, поля `_swap2`/`_waitable` удалены.
- Остался скрытый INVALID_CALL из-за Scaling.None (исправлен в v80).

## Сессия v78 (архив) — три фикса по фидбеку
- **v78 ОПУБЛИКОВАН:** 1.0.38.78-AR2-BITBLT-PINLOG-DATABIN-08.31-1808 (exe=txt=manifest OK).
- Фидбек: «Вебоверлей не запустился, удалён WebOverlay.exe и вообще папка
  bin пустая. ets2c тоже удалён?» / «При запуске АР 2 появляется чёрный
  экран. Других визуальных эффектов нет. При выключении чёрный экран
  исчезает» / «В new_object_po_selections.txt перед названием города пишем
  дату и время текущие».

### 1. data\bin в publish (регресс v77)
- Причина: v77 исключил ЦЕЛИКОМ `data\bin\**\*` из Content и
  SyncDataToPublish → `WebOverlay.exe` (174 МБ) и `ets2c.exe` (177 МБ)
  НЕ попали в publish («Вебоверлей executable not found» в логе 17:56:20).
- Фикс v78: в csproj Content `data\**\*` с Exclude ТОЛЬКО
  `data\bin\WebOverlay.exe.WebView2\**` (runtime-кэш WebView2 — источник
  MSB3027), а SyncDataToPublish Exclude тот же + `data\bin\` из
  _SourceTopDirs (не удалить папку bin в publish целиком).
- Урок: **исключать из data-копирования надо точечно (WebOverlay.exe.WebView2),
  НЕ весь data\bin** — exe-файлы нужны в рантайме.

### 2. Чёрный экран AR v2 (регресс v76/v77)
- Причина: swap chain `SwapEffect.FlipSequential` НЕ поддерживает
  LWA_COLORKEY слоёного окна — DWM игнорирует colorkey при flip-презентации →
  чёрный clear + маркер видны как чёрный прямоугольник на весь экран.
- Фикс v78: `SwapEffect.Sequential` (bitblt-модель) + `Scaling.None` —
  классическая схема, colorkey работает.
- Логи подтвердили: D3D-ошибок НЕТ (DXGI_ERROR_INVALID_CALL исправлен ещё в
  v77), оверлей стартовал/останавливался штатно — чистая проблема презентации.

### 3. Дата-время в new_object_po_selections.txt
- `MainForm.LogNewPointSelection`: первая строка теперь
  `dd.MM.yyyy HH:mm:ss Город Дистанция` (DateTime.Now, InvariantCulture).
- Пример было: `Чистополь 2532` → станет `31.08.2026 17:55:24 Чистополь 2532`.

### Служебное
- Залоченный data_1 в publish-кэше удалялся только после Stop-Process
  cpptools* (C/C++ extension повторно лочил кэш после каждого build).

## Сессия v77 (архив)
- **v77 ОПУБЛИКОВАН:** 1.0.38.77-AR2-MARKER-08.31-1746 (exe=build.txt=manifest OK).
- Что:
  1. `ArRenderer.Initialize` — DXGI_ERROR_INVALID_CALL исправлен:
     флаг `SwapChainFlags.FrameLatencyWaitableObject` перенос в
     `SwapChainDescription1.Flags` (Vortice 3.8.3: 5-й аргумент
     `CreateSwapChainForHwnd` — `IDXGIOutput restrictToOutput`, не флаги).
  2. **Первый GPU-маркер в AR v2.0**: `CreateMarkerPipeline()` — компиляция
     inline-HLSL (`MarkerHlsl`: VS VMain / PS PMain, круг с чёрной обводкой и
     тёмным центром) через `Vortice.D3DCompiler.Compiler.Compile(src, entry,
     name, profile, out Blob, out Blob)` (ВНИМАНИЕ: сигнатура с `out Blob`,
     НЕ короткая 3-арг). `_vb` = 6 вершин квад `CreateBuffer<Vector2>(...,
     BindFlags.VertexBuffer, ResourceUsage.Default, ...)` (перегрузка
     `CreateBuffer(verts)` БЕЗ описания не существует). `_il` через
     `InputElementDescription`. BlendState задание через поля
     `RenderTargetBlendDescription` (`SourceBlend: Blend.SourceAlpha`; типа
     `BlendOption` не существует). CB запись через `ctx.Map(buffer,
     MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None)` + unsafe-указатель
     (нужен `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` в csproj).
  3. `RenderFrame` — draw-путь: IA/VS/PS/OM/RSSet + `Draw(6,0)`;
     проекция цели (yaw=head, eyeH=1.9, pitch=head, fov 75°, отсечение
     `fdot <= 0.5`, круг r=33 px).
  4. csproj: `<AllowUnsafeBlocks>true`; исключён `data\bin\**\*` из Content и
     `SyncDataToPublish` (DawnWebGPUCache/DawnGraphiteCache лочил C/C++
     extension → MSB3027/MSB3021 ломал publish даже при исключении только
     WebOverlay.exe.WebView2). **ИСПРАВЛЕНО в v78** — исключение было слишком
     широким, сломало рантайм-данные.
- Урок сессии (важно): **для Vortice 3.8.3 сверять API рефлексией** —
  `probe.csproj` (LoadFrom из nuget + reflection по типам) экономит циклы. Имена
  конфликтующих enum (MapFlags в DXGI и D3D11) разрешать полным именем
  `Vortice.Direct3D11.MapFlags`.
- ⚠️ PowerShell-хвост: после `cd` в удалённую папку терминал ломает cwd —
  все команды падают («directory name is invalid»); чинить
  `Set-Location D:\repo\ets2_assist` с явным Write-Host.

---

## Как собрать и опубликовать EXE
- VS MCP (`vs-mcp_LoadSolution`) НЕ умеет грузить `.slnx` (падает с `E_ABORT`).
  Используем dotnet CLI напрямую из `F:\repo\ets2_assist`:
  ```
  dotnet publish ETS2_Assist_GUI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
  ```
- Результат: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`
  (~130 МБ, single-file, self-contained, .NET 10 WinForms).
- Предупреждения компиляции: только `CS0414` (неиспользуемые поля `_autoSave` и т.п.)
  и `CS8600/8602` (nullability) в `MainForm.cs` — не критично.

## Что сделано в этой сессии

### 1. Сплеш-экран (SplashForm.cs, метод AddVersionCaption)
- Было: отрисовка версии через `GraphicsPath` + `DrawPath` толстыми перьями
  (2.5px «тень» + 1px «обводка») на шрифте 7.5pt → артефакты, «съеденный» белый текст.
- Стало: обычный белый `DrawString` (bold) с толстой чёрной обводкой (кольцо радиусом 2px,
  8-направленные смещённые копии) + авто-подгонка размера шрифта по ширине (от 8.5pt до 6pt),
  чтобы длинная версия не обрезалась по краям. Без `DrawPath` (он давал джаггед-артефакты).

### 2. Случайная цель не появляется на миникарте
Путь команды: кнопки `BtnRandomTarget*` (Quests/QuestsManager.cs) →
`SendCommandToMap("add_random_target"[_2|_100")` → broadcast по WebSocket (C#, порт save-сервера)
→ `web_pda_map.html` слушает `data/js/websocket.js` → `generateRandomTarget` (data/js/targets.js)
→ цель кладётся в `state.customTargets` + `targetX/targetZ` → `updateAll()` → `drawMinimap()` (data/js/map_draw.js).
Отрисовка в map_draw.js корректна (рисует точку, если цель на карте, и стрелку+дистанцию, если за пределами).

Найденные и исправленные баги (data/js/targets.js):
- **Баг А (главный):** `generateRandomTarget` для кнопок 1 и 2 требовал `state.roads`/`state.pois`.
  Если статических данных карты нет, поиск не находил точку → функция выводила тост и `return`,
  цель НЕ создавалась (поэтому на карте ничего не появлялось). Добавлен гарантированный
  запасной спавн: случайная точка около фуры без привязки к дорогам/POI.
- **Баг Б:** `loadCustomTargets` (вызывается каждые 2с из `websocket.js: fetchHttpData`)
  в ветке «файл уже содержит random» не восстанавливал `targetX/targetZ` и `state.target`
  (в отличие от ветки in-memory). Добавлено восстановление координат.

Примечание: `loadCustomTargets` каждые 2с сбрасывает `state.targetMapOverview=false` и
`zoomOnMapTargets=[]`, но для random-цели `focusTargetOnMap` внутри восстанавливает обзор.
Если цель всё равно не видна после правок — проверить, что WebSocket save-сервера
доходит до страницы миникарты (saveWs подключён, порт 8084).

## Ключевые файлы
- `SplashForm.cs` — сплеш, отрисовка версии.
- `Quests/QuestsManager.cs` — кнопки случайной цели, пауза ETS2, диалог достижения.
- `data/js/targets.js` — генерация/загрузка/сохранение целей.
- `data/js/map_draw.js` — отрисовка миникарты (функция `drawMinimap`).
- `data/js/websocket.js` — обработка команд + телеметрия (setInterval fetchHttpData 2с).
- `data/js/state.js` — глобальное состояние (`customTargets`, `randomTarget`, `roads`, `pois`).
- `data/GeoJson/roads.geojson`, `cities.geojson` — статические данные карты.

## Открытые вопросы / проверить у пользователя
- Подтвердить, что после правок цель видна на миникарте (точка + стрелка с дистанцией за пределами).
- Если не видно: возможно, команда WebSocket не доходит до `web_pda_map.html` (проверить saveWs).

---

## Сессия 2 (27.08.2026)

### Эмпирика от пользователя (важно!)
- Кнопка №1: цель создаётся и СРАЗУ триггер `target_reached` (деньги/опыт начислены) →
  значит цель реально создаётся, спавнится близко (<50м) и триггер срабатывает.
- Визуально точку НЕ видно ни на одной из кнопок. Значит баг в ОТРИСОВКЕ, не в создании.
- Одна из кнопок (№2) вызвала резкий авто-зум «дальше всей карты» с последующим
  возвратом к фуре (это `focusTargetOnMap` + сброс `targetMapOverview` в `loadCustomTargets` 2с).

### Что сделано
- **Гарантированная отрисовка точки:** в `data/js/map_draw.js` (`drawMinimap`) добавлен
  блок, рисующий `randomTarget` напрямую из глобальной переменной (яркий кружок + перекрестье
  при видимости, стрелка + дистанция при выходе за карту). Это обходит цепочку `state.customTargets`,
  которая, предположительно, теряет активную цель к моменту отрисовки.
- **Кнопка №4 «Ближайшая цель»** (`BtnRandomTarget4_Click` → `add_random_target_near`):
  цель СТРОГО на дороге, радиус 51–60 м от фуры. В `targets.js` добавлен режим `requireRoad`
  + `minDistM/maxDistM` (хелпер `nearestPointOnRoad` для привязки к дороге).
- **Кнопка «Проверка точек»** (`BtnCheckTargets_Click` → `list_targets`): JS собирает все
  точки (`state.customTargets` + `randomTarget`) с координатами и дистанцией до фуры и шлёт
  `targets_list` в C#; C# (`OnClientCommand`) пишет список в лог.
- **Логирование создания:** в `targets.js` `target_created` теперь шлёт `dist` (дистанция до
  фуры); C# `HandleTargetCreated` логирует «Точка создана: x=.., z=.., дистанция=.. м».

### Сборка
- Обычный `dotnet publish` в `publish\` падает на бандлере: запущенный EXE заблокирован
  (пользователь тестирует, PID живой). Поэтому опубликовано во временную папку
  `F:\repo\ets2_assist\publish_test\` (single-file, win-x64, Release). Чтобы обновить основной
  exe — закрыть запущенный экземпляр и пересобрать в `publish\`.
- Правки C# (MainForm.cs, QuestsManager.cs) и JS (targets.js, websocket.js, map_draw.js)
  прошли: `node --check` OK, компиляция C# OK (только CS0414-варнинги).
- **Основной exe пересобран** после закрытия приложения: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe` (130,4 МБ, 27.08.2026 16:36). Временная `publish_test\` больше не нужна.

### Памятки пользователя (зафиксировать в INSTRUCTIONS.md)
- «запомни»/«учти» = кратко в память, без излишеств, понятно для след. сессии.
- Логи рядом с exe (`publish\Logs`); пишем шаги, не спамим значениями (значения — в app_data).
- Отладка телеметрии: `http://localhost:8082/web_telemetry_inspector.html`.
- Обновлять INSTRUCTIONS.md и WORKLOG.md после каждого изменения.

### Что проверить дальше
- Видна ли точка (гарантированная отрисовка + кнопка №4).
- Логи цели (создание + «Проверка точек»).
- Глюк авто-зумa (ограничить макс. дистанцию обзора / не сбрасывать `targetMapOverview`).

---

## Сессия 3 (27.08.2026, продолжение)

### Наблюдение пользователя (ключ к багу)
- Кнопка №3 = кратковременный «обзор целей» (zoom-out) потом возврат к фуре → подтверждает
  глюк авто-зумa (`focusTargetOnMap` + сброс `targetMapOverview` каждые 2с).
- **Ни одна кнопка не добавила видимую точку.**
- «Проверка точек» (`list_targets`) находит ТОЛЬКО цели из файла `custom_targets.json`,
  а созданные кнопками — НЕТ. → значит хранилище в памяти теряет их.
- Гипотеза пользователя (верна): в ранних версиях ВСЕ цели (и на лету) лежали в
  `custom_targets.json`; потом кто-то разделил хранение → появились потери.

### Анализ: куда добавляются случайные цели и что их обнуляет
- `generateRandomTarget` (targets.js): пушит в `state.customTargets` + дублирует в глобальную
  `randomTarget`, и пытается сохранить в файл `custom_targets.json` через `saveTargetsToFile()`
  (POST на :8083). То есть «источник истины» двойной: файл И в память.
- **Обнуляет `loadCustomTargets()`**: каждые 2с делает `state.customTargets = []` и пересобирает
  из файла. Старая логика: если в файле ЕСТЬ `isRandom` — брать ЕГО (даже если в памяти свежее).
  ⇒ устаревшая цель из файла ПЕРЕЗАПИСЫВАЛА только что созданную кнопкой → в `list_targets`
  и на карте попадала не та / старая цель, а свежая терялась. Это и есть «что-то обнуляет».

### Что сделано
- **Исправлен приоритет в `loadCustomTargets`:** свеже-созданная в памяти цель
  (`inMemoryRandoms`) теперь ПРИОРИТЕТНЕЕ файловой `random`. Устаревший файл-рандом
  больше не затирает живую цель. (`data/js/targets.js`)
- **Диагностическая распечатка массива точек сразу после добавления кнопкой:**
  добавлена `reportTargetsSnapshot(reason)` (targets.js) → шлёт `targets_snapshot` в C#;
  вызывается в конце `generateRandomTarget` ПЕРЕД любым 2с-пересбросом. C# (`QuestsManager.cs`,
  `OnClientCommand`, case `targets_snapshot`) пишет в лог: «СНИМОК хранилища (причина: add:Имя)»
  + список `[i] name/x/z/isRandom/active` + «всего в памяти: N». Замысел пользователя:
  нажать 4 кнопки подряд → в логе 1,2,3,4 цели; если массив пуст — его кто-то обнуляет, будем искать.
- Кнопка №4 («Ближайшая цель») уже пишет в `custom_targets` через `saveTargetsToFile()`;
  с фиксом приоритета памяти она теперь гарантированно не теряется.

### Память (по «запомни»/«в планы»)
- **Стоящее правило:** перед КАЖДОЙ задачей/правкой сверяться с папкой логов приложения
  (`publish\Logs`) — там наша ключевая отладочная информация. Записано в INSTRUCTIONS.md.
- **В ПЛАНЫ:** полный рефакторинг принципа создания/хранения/отрисовки точек (вернуть
  единый надёжный источник — файл как истина + консистентная отрисовка). Добавлено в
  раздел «Планы на будущее» INSTRUCTIONS.md. НЕ делать в этой задаче.

### Сборка
- `node --check data/js/targets.js` OK; `dotnet publish` в `publish\` OK (только CS0414).
- EXE пересобран: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.

### Что проверить у пользователя
- Открыть лог (`publish\Logs`), нажать по очереди 4 кнопки случайной цели → ожидаем
  в логе 4 снимка «всего в памяти: 1 / 2 / 3 / 4». Если пусто — массив обнуляется (искать где).
- Видна ли точка на карте теперь (фикс приоритета + гарантированная отрисовка randomTarget).
- Глюк авто-зумa (отдельная задача, пока не правили).

---

## Сессия 4 (27.08.2026) — КОРЕНЬ БАГА НАЙДЕН И ИСПРАВЛЕН

### Наблюдение пользователя
- Ни одна кнопка не добавляла видимую точку; «Проверка точек» видела только цели из файла
  (связной/тайник). Гипотеза пользователя: файл `custom_targets` работает, но созданные
  кнопками цели «теряются». Верно — но причина глубже.
- Просьба: убрать таймеры, что миникарта сама опрашивает файл; обновлять только по команде
  от приложения; при старте (после «анимации show») давать команду перечитать файл.
- Попросил подчистить раскладку кнопок (накладывались друг на друга).

### КОРЕНЬ БАГА (найдено при разборе)
- `loadCustomTargets` (targets.js) читал `withRnd('/custom_targets.json')` → **:8082
  (статический `data/`)**, а `saveTargetsToFile` писал POST `:8083/update_targets` → **:8083
  (AppData `%LOCALAPPDATA%/ETS2_Assist/custom_targets.json`)**. Читали и писали РАЗНЫЕ файлы!
  Кнопочные цели попадали в AppData-файл, миникарта перечитывала статический → не видела их.
  (Совпадает с `АРХИТЕКТУРА ПРОЕКТА.md`: targets_read/write должны быть через :8083.)

### Что сделано (рефакторинг архитектуры целей)
- `loadCustomTargets` теперь читает `http://localhost:8083/custom_targets.json` (тот же
  файл, что пишется). Файл — единый источник истины. (`data/js/targets.js`)
- Убраны ВСЕ периодические вызовы `loadCustomTargets`:
  - `websocket.js` `fetchHttpData` больше НЕ вызывает `loadCustomTargets()` (телеметрия оставлена).
  - `init.js` `loadData` больше НЕ вызывает `loadCustomTargets()` на старте.
- Добавлена команда `reload_custom_targets` (minimap, `websocket.js` → `loadCustomTargets()`).
- Когда шлётся: (1) старт — миникарта при `saveWs.onopen` шлёт `map_ready` → C#
  (`QuestsManager`) отвечает `reload_custom_targets`; (2) после создания точки —
  `generateRandomTarget` после `await saveTargetsToFile()` шлёт `request_reload_custom_targets`
  → C# → `reload_custom_targets`. (`targets.js`, `websocket.js`, `QuestsManager.cs`)
- `saveTargetsToFile` защищён от перезаписи файла пустым массивом: если `state.customTargets`
  пуст — сначала догружает из файла. (Иначе на старте до первой загрузки сохранение могло
  стереть связной/тайник.)
- Все 4 кнопки случайной цели теперь файл-базированы (пишут в AppData-файл + reload).

### Раскладка кнопок (MainForm.cs)
- Было наложение: `btnShowMap` стоял на `topY+360`, пересекаясь с `btnRandomTarget4`(+350)
  и `btnCheckTargets`(+380). Перенесено: `btnShowMap`→+410, `btnShowHybrid`→+440,
  `btnTestPause`→+470, `btnResetRecordingOrigin`→+510. Теперь без пересечений.

### Сборка
- `node --check` (targets/websocket/init) OK; `dotnet publish` в `publish\` OK (только CS0414).
- EXE пересобран: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.

### Что проверить у пользователя
- Запустить, дождаться загрузки миникарты. Видны ли связной/тайник (читаются из :8083)?
- Нажать кнопку №4 «Ближайшая цель» → появилась ли точка на карте? (это главная проверка
  файл-подхода). Затем кнопки 1/2/3.
- «Проверка точек» → в логе теперь должны быть ВСЕ созданные (не только файловые).
- Глюк авто-зумa (кнопка №2) — проверить, остался ли после отвязки таймера.

---

## Сессия 5 (27.08.2026) — версионирование + указатели целей

### Правило версионирования (запомнить)
- Перед КАЖДЫМ билдом повышаем версию: `A.B.CCCC-DESC-DATE-BUILD`
  (A=поколение, B=этап, C=порядковый № изменения, сбрасывается при B+1; DESC=2-3 англ. слова;
  DATE=YYYY.MM.DD; BUILD=№ сборки). Строка версии — в `BuildInfo.cs` (`BuildInfo.Version`).
- Поднял `1.0.34-...-RND3` → **`1.0.35-TARGET-POINTERS-2026.08.27-RND4`** (C: 34→35).
  Записано в `INSTRUCTIONS.md` (раздел «Версионирование»).

### Указатели целей за пределами карты
- Исследование показало: указатели НА ЦЕЛИ УЖЕ ЕСТЬ в `data/js/map_draw.js`:
  - гарантированный блок `randomTarget` (строки ~520-578) рисует точку + перекрестье на карте
    и СТРЕЛКУ С ДИСТАНЦИЕЙ за пределами;
  - циклы «Неактивные/Активные цели» (~347-493) тоже рисуют стрелку на краю, если цель
    вне экрана, — по той же логике, что и 4 ближайших города (~580-636).
- То есть «указателей нет» — ложная гипотеза. Реальная причина, почему точки не появлялись
  после первой: созданная цель СТИРАЛАСЬ перезагрузкой из файла (POST сохранения мог не
  дойти / файл устаревал), и `loadCustomTargets` очищал `randomTarget`/`state.customTargets`.

### Что сделано (реальный фикс)
- В `loadCustomTargets` (targets.js) добавлена защита: запоминаем `prevRandom`/`prevReached`
  ДО сброса; если после пересборки из файла активная цель не найдена, НО она была создана
  кнопкой и ещё не достигнута — восстанавливаем её (`randomTarget`, `state.customTargets`,
  `targetX/Z`, `state.target`) и снова зуммим. Теперь живая цель гарантированно не теряется
  при любом раунде «сохранить→перечитать».
- Это и есть то, что «включает» видимость точек И их указателей за пределами карты.

### Сборка
- `node --check data/js/targets.js` OK; `dotnet publish` в `publish\` OK (только CS0414).
- EXE пересобран: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`
  (версия 1.0.35-TARGET-POINTERS-2026.08.27-RND4).

### Что проверить у пользователя
- Запустить новый EXE (1.0.35). Нажать по очереди кнопки 1/2/3/4 — каждая должна давать
  точку НА карте (близко) и СТРЕЛКУ-УКАЗАТЕЛЬ за пределами (если далеко), как у городов.
- «Проверка точек» → теперь должна видеть ВСЕ созданные (не только файловые).
- В логе (publish\Logs) см. снимки `СНИМОК хранилища точек (причина: add:...)` — должно расти 1/2/3/4.
- Глюк авто-зумa (кнопка №2) — проверить, остался ли.

---

## Сессия 6 (27.08.2026) — рефакторинг: приложение = единственный владелец custom_targets.json

### Контекст от пользователя
- Пользователь вручную добавил «Связной 2» в `C:\Users\nikitinma\AppData\Local\ETS2_Assist\custom_targets.json`
  — третья голубая точка появилась корректно. Авто-зум (обзор целей) — ОК, эту задачу забыли.
- Задача: **убрать ВСЕ остальные механики создания/хранения точек**; на данном этапе
  **вернуться полностью к файлу** `custom_targets.json`. Позже — рефакторинг на «более
  эффективную систему хранения» (добавлено в план/бэклог, см. память #17).
- Ключевое требование: миникарта **НЕ читает и НЕ пишет файл сама** (он вне веб-сервера).
  Приложение читает файл (один раз при старте), пишет при добавлении/удалении цели и
  **шлёт содержимое на миникарту командой `targets_data`**; миникарта только рисует.
- «Проверка точек» → приложение **принудительно перечитывает файл и шлёт на карту** (отладка).

### Что сделано (рефактор)
**Миникарта (data/js):**
- `targets.js`: `loadCustomTargets` (fetch из :8083) → удалён; добавлены `normalizeTarget` +
  `applyTargetsData(targets)` (только приём и отрисовка по `targets_data`).
- Удалены `saveTargetsToFile` (POST в :8083) и `reportTargetsSnapshot` (диагностика).
- `generateRandomTarget`: вместо записи в файл + `request_reload_custom_targets` — шлёт
  `target_created` + `add_target` приложению (приложение пишет файл и шлёт `targets_data`).
- `removeRandomTarget`: стал локально-только (без записи в файл).
- `websocket.js`: `case 'reload_custom_targets'` → `case 'targets_data': applyTargetsData(...)`.
  `map_ready` (с миникарты) по-прежнему шлётся; C# на него отвечает `targets_data`.
- `init.js`: обновлён комментарий (цели приходят через `targets_data`).

**Приложение (C# / QuestsManager.cs, MainForm.cs):**
- Новый владелец файла: `SendTargetsToMap()` (читает файл → шлёт `targets_data`),
  `AddTargetToFile(target)` (дописывает цель, заменяя предыдущую `isRandom`, пишет файл,
  шлёт `targets_data`), `RemoveTargetFromFile(x,z)` (удаляет по координатам),
  `LogCustomTargetsFromFile()` (лог списка для отладки).
- `OnClientCommand`: `map_ready`/`request_reload_custom_targets` → `SendTargetsToMap()`;
  добавлен `case "add_target"` → `AddTargetToFile`; `targets_snapshot` → проигнорирован.
- `BtnCheckTargets_Click` («Проверка точек»): теперь `SendTargetsToMap()` + `LogCustomTargetsFromFile()`
  (принудительное перечитывание файла и отправка на карту).
- `HandleTargetReached` (Да): `RemoveTargetFromFile(reachedX,reachedZ)` + `SendTargetsToMap()`
  — достигнутая цель удаляется из файла приложением.
- `MainForm.cs`: удалены HTTP `GET /custom_targets.json` и `POST /update_targets`
  (миникарта больше не ходит в файл по HTTP); из `ProcessStaticRequest` убран маппинг
  `custom_targets.json` (оставлен только `web_data.json`).

### Версия
- Повышена: `1.0.35-TARGET-POINTERS-2026.08.27-RND4` → **`1.0.36-APP-FILE-BROKER-2026.08.27-RND5`** (`BuildInfo.cs`).

### Сборка
- `node --check` (targets/websocket/init) OK.
- `dotnet publish` в `publish\` — ВНИМАНИЕ: первый раз ПАЛ с `error CS0103` (см. Сессию 7:
  `Formatting` не в области видимости, не хватало `using Newtonsoft.Json;`). Исправлено только в
  Сессии 7. До исправления в `publish\` ЛЕЖАЛ СТАРЫЙ 1.0.35 — пользователь тестировал его, а не 1.0.36.

### Что проверить у пользователя
- Запустить новый EXE. Точка «Связной 2» (и прочие из файла) должна появиться сразу после
  загрузки миникарты (по `map_ready` → `targets_data`).
- Нажать кнопки 1/2/3/4 → цель должна записаться в `custom_targets.json` (проверить файл
  в блокноте) И появиться на карте. В логе приложения: `[TARGETS] Цель добавлена в файл...`
  и `Отправлено точек на миникарту: N`.
- Доехать до цели → в логе `Цель удалена из файла` (файл больше не содержит её), карта чистит.
- «Проверка точек» → лог: `=== Проверка точек (из файла): N ===` со списком имён/координат,
  и карта перерисовывает точки из файла.
- По правилу: при проблемах — прислать выдержку из `publish\Logs`.

---

## Сессия 7 (27.08.2026) — инцидент: 1.0.36 НЕ собиралась (тихая ошибка компиляции)

### Симптом (от пользователя)
- Кнопка №3 показывает двойную случайную цель ~100м, доехал ближе — триггер не сработал.
- Файл `custom_targets.json` НЕ меняется. «Цели добавляются как будто по старой схеме».
- В логе `publish\Logs\app_workflow.log` первая строка сессии: `BUILD_VERSION=1.0.35-TARGET-POINTERS-2026.08.27-RND4`
  и `[WS] Миникарта готова -> reload_custom_targets`.

### КОРЕННАЯ ПРИЧИНА (НЕ инкрементальный кэш!)
- В `Quests/QuestsManager.cs` `AddTargetToFile`/`RemoveTargetFromFile` использовали `Formatting.Indented`,
  но файл имел только `using Newtonsoft.Json.Linq;` → **`error CS0103: The name 'Formatting' does not exist`**
  (строки 335, 373). `dotnet publish` ПАДАЛ, поэтому старый EXE 1.0.35 ОСТАВАЛСЯ в `publish\`.
- Я ошибочно посмотрел ТОЛЬКО на trailing CS0414-варнинги и решил, что сборка успешна.
  УРОК: вывод `dotnet publish` надо грепать на `error CS`, иначе тихий провал компиляции
  оставляет предыдущий бинарь — и юзер тестирует старую версию.
- Итог: пользователь тестировал 1.0.35 (старую схему с POST/`reload_custom_targets`), а не 1.0.36.

### Исправление
- Добавлен `using Newtonsoft.Json;` в `Quests/QuestsManager.cs` (рядом с `using Newtonsoft.Json.Linq;`).
- `dotnet clean` + `dotnet publish` → SUCCESS. EXE `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`
  содержит `APP-FILE-BROKER` (проверено grep unicode), timestamp 18:26:22.
- Развёрнутый `data/js/targets.js` содержит `applyTargetsData`, НЕТ `loadCustomTargets`;
  `websocket.js` обрабатывает `targets_data`.

### Путь запуска (важно)
- «`publish\`» пользователя = `bin\Release\net10.0-windows\win-x64\publish\` (там же `Logs\`).
  Репо-рут `publish\ETS2_Assist_GUI.exe` НЕ существует. Значит, собранный EXE — тот, что запускает юзер.
- `publish_test\` — отдельная папка, НЕ использовать.

### Что проверить у пользователя (после запуска свежего EXE)
- Первая строка лога: `BUILD_VERSION=1.0.36-APP-FILE-BROKER-2026.08.27-RND5`, НЕТ `reload_custom_targets`.
- Кнопка №3 → лог `[TARGETS] Цель добавлена в файл...` и файл `custom_targets.json` меняется.
  - Pending: геометрия кнопок 1/2/4 (road/POI) — только №3 добавляет цель; глюк авто-зумa (кнопка №2).

---

## Сессия 8 (27.08.2026) — синхронизация data при публикации + debug-лог целей + локализация

### Контекст от пользователя
- После очистки publish-папки удалилась папка `language` → интерфейс потерял локализацию.
  Пользователь перенёс `en.csv`/`ru.csv` в `data\language\` (исходники).
- После запуска миникарта БЕСКОНЕЧНО проигрывает анимацию появления (каждые 3-5 сек);
  кнопки случайной цели не дают эффекта. Гипотеза: папка `data` в publish УСТАРЕЛА.
- Просьба: изменить проект так, чтобы после билда/публикации исходная `data` копировалась
  поверх publish (актуальные веб-файлы). Запомнить: «чистый билд» = publish-папка полностью
  удаляется/очищается перед созданием exe.
- Просьба: подробный debug-лог записи `custom_targets.json` (путь + что пишется + перечитка
  содержимого), чтобы понять, почему связной/тайник есть, а случайные цели «улетают в никуда».

### Что сделано
- **Синхронизация data (`ETS2_Assist_GUI.csproj`):** добавлен Target `SyncDataToPublish`
  (`AfterTargets="Publish"`). Он удаляет `publish\data\` целиком и копирует заново
  исходную `data\` (388 файлов). Это устраняет рассинхрон и устаревшие файлы в publish.
  Локализация: добавлена папка `language\**\*` как Content (рядом с `data\**\*`), поэтому
  `BaseDirectory/language` и `BaseDirectory/data/language` обе присутствуют в publish.
  `LanguageManager.cs` уже резолвит оба пути.
- **Debug-лог целей (`Quests/QuestsManager.cs`):** добавлен `LogTargetsFileDump(label,path)` —
  логирует путь файла и ПЕРЕЧИТЫВАЕТ его содержимое. Вызывается после записи/удаления цели
  (`AddTargetToFile`, `RemoveTargetFromFile`) и в `SendTargetsToMap` (путь + число точек).
  В `AddTargetToFile` логируется сам `entry` (JSON) и счётчики до/после добавления.
- **Версия:** `1.0.36-APP-FILE-BROKER-2026.08.27-RND5` → **`1.0.37-TARGET-DEBUG-DATASYNC-2026.08.27-RND6`**
  (`BuildInfo.cs`, `VersionPrefix` csproj → 1.0.37, `data/ets2_assist_build.txt`,
  `data/web_runtime_manifest.json`).

### Сборка
- `dotnet publish` (Release, win-x64, self-contained, single-file) — SUCCESS, БЕЗ `error CS`.
- В выводе: `SyncDataToPublish: скопировано 388 файлов data -> ...publish\data`.
- EXE: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.
- Проверено: `publish\data\language\{en,ru}.csv` и `publish\language\{en,ru}.csv` на месте.

### Что проверить у пользователя
- Запустить новый EXE (1.0.37). Анимация появления миникарты — ОДИН раз (не цикл).
  Интерфейс на русском (локализация восстановлена).
- Нажать кнопки 1/2/3/4 → в логе (`publish\Logs`) искать `[TARGETS][DEBUG] ЗАПИСЬ цели...`
  с путём файла, `entry=...`, и `[TARGETS][DEBUG] ПОСЛЕ ЗАПИСИ ЦЕЛИ: содержимое={...}`.
  Это покажет, реально ли цель попадает в `custom_targets.json`.
- «Проверка точек» → `[TARGETS] Отправлено точек... (путь файла=...)`.
- Если цель в логе пишется, но на карте не видна — значит баг в `applyTargetsData`
  (data/js/targets.js) или отрисовке; если НЕ пишется — баг в доставке команды `add_target`
  от миникарты (websocket.js). Лог это разделит.
- Pending (старые): глюк авто-зумa (кнопка №2); геометрия кнопок 1/2/4.

### ИНЦИДЕНТ (после публикации 1.0.37): потеряна папка data\bin
- **Симптом:** после публикации пропали `WebOverlay.exe` и `ets2c.exe` — нет ни оверлея,
  ни начисления наград. Лог `app_workflow.log` показал старый `BUILD_VERSION=1.0.36` и
  ошибку `Language file 'en.csv' not found!` (arduino.ps1). Причина: таргет `SyncDataToPublish`
  изначально делал `RemoveDir publish\data\` ЦЕЛИКОМ — стёр `data\bin`, которого не было
  в исходниках (бинарники жили только в publish).
- **Фикс:** пользователь скопировал `ets2c.exe`+`WebOverlay.exe` в `data\bin\` (корень
  проекта) — теперь это авторитетный источник. Таргет переписан: НЕ удаляет `publish\data\`
  целиком, а копирует исходную `data\` ПОВЕРХ и удаляет-пересоздаёт только ТОП-папки,
  присутствующие в исходниках (js/css/GeoJson/language/bin/...). Runtime-файлы (web_data.json)
  сохраняются. Повторный `dotnet publish` → `publish\data\bin\{ets2c.exe,WebOverlay.exe}` на месте,
  390 файлов скопировано. Записано в INSTRUCTIONS.md (урок: не RemoveDir всю publish\data).

---

## Сессия 9 (27.08.2026) — РАЗБЛОКИРОВКА СБОРКИ + фикс версионирования

### Симптом
- `dotnet restore`/`dotnet publish` падали мгновенно (66-92 мс) с
  `error MSB4181: The "RestoreTask" task returned false but did not log an error.`
  без каких-либо других ошибок. `obj/project.assets.json` оказался без таргета
  `net10.0-windows` (после неудачного restore).

### КОРЕНЬ (два независимых момента)
1. **`<Version>` содержал НЕвалидный SemVer.** Раньше строка версии
  (`1.0.38-TARGETS-FILE-2026.08.27-2110`) клалась в `<Version>` (пакетный номер NuGet,
  используемый restore). Точки ВНУТРИ prerelease-сегмента (`2026.08.27`) — недопустимы по
  SemVer, и restore падал именно с этим «безымянным» RestoreTask-эррором (очень сбивает:
  ошибка не логируется). Проверено бисекцией: минимальный WinForms-проект восстанавливается
  нормально, мой csproj падал ровно из-за `<Version>$(DescriptiveVersion)</Version>`.
2. **Офлайн-восстановление.** У `dotnet` на сборочной машине НЕТ выхода до NuGet-фида
  (обычный сетевой egress есть, но `dotnet restore`/`workload install` не доходят).
  Все нужные пакеты УЖЕ в глоб. кэше (`%USERPROFILE%\.nuget\packages`). Рабочий обходняк:
  собрать плоский offline-фид из кэша (`Get-ChildItem ...\.nuget\packages -Recurse -Filter *.nupkg
  | Copy-Item` в одну папку) и `dotnet restore ... --source <feed>`. После этого publish с
  `--no-restore`. НЕ пытаться лечить сеть — restore упадёт без лога.
  (Windows Desktop workload при этом НЕ нужен: пак `Microsoft.WindowsDesktop.App.Ref` уже в
  `C:\Program Files\dotnet\packs`, минимальный WinForms восстанавливается/строится офлайн.)

### Исправления (файлы)
- `ETS2_Assist_GUI.csproj`:
  - `<Version>` = `$(VersionPrefix)` (чистый SemVer) — раньше было `$(DescriptiveVersion)`.
  - `<InformationalVersion>` = `$(DescriptiveVersion)` (НОВОЕ свойство) — именно из него SDK
    берёт `AssemblyInformationalVersionAttribute`. `<AssemblyInformationalVersion>` сам по себе
    атрибут НЕ формирует (SDK смотрит на `$(InformationalVersion)`)!
  - `<_EpochMod>` = `$([MSBuild]::Modulo($([System.DateTimeOffset]::Now.ToUnixTimeSeconds()), 65536))`
    — 4-я цифра `AssemblyVersion`/`FileVersion` = секунды UNIX mod 65536.
  - `<AssemblyVersion>`/`<FileVersion>` = `$(VersionPrefix).$(_EpochMod)`.
- `BuildInfo.cs`: добавлен `using System.Reflection;` (иначе `GetCustomAttribute<T>` — extension,
  CS1061; без ImplicitUsings в проекте он не в области видимости). `BuildInfo.Version` читает
  `AssemblyInformationalVersionAttribute` → теперь это описательная строка.

### Правило версионирования (обновлено, 27.08.2026)
- Формат строки: `A.B.CCCC-DESC-YYYY.MM.DD-HHmm` (БЕЗ `RND`; последний блок = дата+время билда).
- `CCCC` — счётчик билда, повышается ВРУЧНУЮ на 1 ПОСЛЕ каждого билда (в `VersionPrefix`).
- 4-я цифра EXE = epoch mod 65536 (уникальна на каждый билд).
- Строка версии автосинхронизирована с csproj через `InformationalVersion` (не дублируем вручную).
- Бейдж-строка дублируется в `data/ets2_assist_build.txt` и `data/web_runtime_manifest.json`.

### Сборка (успех)
- Offline-restore из локального фида + `dotnet publish ... --no-restore` → SUCCESS, БЕЗ `error CS`.
- EXE: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.
- Проверено: `ProductVersion` = `1.0.38-TARGETS-FILE-2026.08.27-2135` (читается `BuildInfo.Version`);
  `FileVersion` = `1.0.38.26470` (4-я цифра = epoch mod 65536); `publish\data\bin\{ets2c.exe,WebOverlay.exe}`
  на месте (390 файлов скопировано таргетом `SyncDataToPublish`).
- ВАЖНО: `obj\` был удалён в процессе отладки restore — после успешного offline-restore он
  валиден; для последующих билдов достаточно `dotnet publish ... --no-restore` (или restore из фида).

### Что проверить у пользователя
- Запустить свежий EXE (1.0.38-TARGETS-FILE-...). В заголовке/логах `BUILD_VERSION` = новая строка.
- Свойства exe (Подробно) → 4-я цифра FileVersion меняется каждый билд (epoch mod 65536).
- Старые pending: глюк авто-зумa (кнопка №2); геометрия кнопок 1/2/4 — НЕ правили в этой сессии.

## Сессия 10 (28.08.2026) — НОВЫЙ ФОРМАТ ВЕРСИИ + МУЛЬТИ-ЦЕЛИ/ТРИГГЕРЫ

### Формат версии (НОВОЕ ПРАВИЛО пользователя)
- Строка: `A.B.C.D-DESC-YY.MM.DD-UNIX`
  - A=1, B=0 (по команде), C=38 (набор задач, по команде), D=итератор (агент бампит при
    завершении задачи и при каждом тест-билде), DESC=что решено в D, YY.MM.DD=дата, UNIX=секунды.
  - `<Version>`=A.B.C (чистый SemVer для restore); D идёт в FileVersion/AssemblyVersion и в строку.
- csproj: `VersionMajor/VersionStage/VersionSet/VersionIter/VersionDesc` + `DescriptiveVersion`.
- Собрано: `1.0.38.2-RND-TARGETS-26.08.28-<unix>` (FileVersion 1.0.38.2). Бейджи обновлены.

### Случайные цели — мульти-цели + триггер (РЕАЛИЗОВАНО, нужна рантайм-проверка)
- Раньше: одна цель за раз, триггер по входу в 50м (`target_reached` → пауза+диалог).
- Теперь НЕСКОЛЬКО целей одновременно, каждая с уникальным `id` (генерит миникарта).
- Поток: кнопка → `add_random_target*` → миникарта `generateRandomTarget` (params уже разные:
  №1 радиус 2км у дороги, №2 у POI, №3 100м, №4 51-60м строго на дороге) → шлёт
  `target_created`+`add_target`(c id) → C# пишет `custom_targets.json` (с id, без удаления
  предыдущих) → шлёт `targets_data` → миникарта рисует ВСЕ цели (`state.randomTargets`).
- ТРИГГЕР (вход→арм, выход→завершение): `trail.js` детектит вход/выход из зоны (radius цели)
  каждого `state.randomTargets`; шлёт `target_zone_enter`/`target_zone_leave`.
  - `target_zone_enter`: C# ставит `InZone/Armed`, паузит игру, показывает диалог
    «Принять задание?». Да → `Accepted=true` (ждём выхода); Нет → `Accepted=false`, цель
    ОСТАЁТСЯ на карте (ре-арм при повторном входе, как просил пользователь).
  - `target_zone_leave`: если `Armed && Accepted` → `CompleteRandomTarget` (удаляет из файла+
    словаря, шлёт `remove_target` миникарте, награда ets2c +3000/-150xp, balloon). Иначе сброс.
- Цвет на карте: красная (не в зоне) / зелёная (в зоне/армирована), подпись `[в зоне]`.
- Удалён старый `HandleTargetReached` (target_reached больше не шлётся). Удалены поля
  `_randomTarget*`, заменены на `Dictionary<string,RandomTargetState> _randomTargets`.

### Что проверить у пользователя (рантайм)
- Несколько кнопок создают НЕСКОЛЬКО точек, все видны на миникарте (не дублируются).
- Вход в зону цели → зеленеет + диалог принятия; выход после принятия → цель исчезает + награда.
- Отказ → цель остаётся; повторный вход снова даёт диалог (не спамит постоянно).
- Старые pending сохраняются: авто-зумa (№2), геометрия кнопок 1/2/4.

### Планы (от пользователя, этапы квестов)
- **Супер-задача:** менеджер квестов управляет одним динамическим квестом «Курьер»
  (успех → повысить B и начать новый цикл C/D).
- **Премиум-задача:** менеджер квестов управляет ещё и квестом «Попутчик».
- Задачи по квестам сформулировать ПОСЛЕ завершения минимальных задач (внесены в план).

## Сессия 10 (дополнение, 28.08.2026) — ФОРМАТ ВЕРСИИ v2 + ФИКСЫ + КВЕСТ-КНОПКИ

### Формат версии ИЗМЕНЁН (по фидбеку)
- Новый: `A.B.C.D-DESC-MM.DD-Hmm`
  - Убрали UNIX-секунды (слишком длинные). Время `Hmm`: час БЕЗ ведущего нуля,
    минуты С ведущим нулём, без разделителей (9:22->922, 13:47->1347).
  - Убрали год. Дата `MM.DD` только.
  - Собрано: `1.0.38.3-QUESTS-FIX-08.28-1015` (FileVersion 1.0.38.3). Бейджи обновлены.
  - csproj: `BuildDate=MM.dd`, `BuildTime=Hmm`, убран `BuildUnix`.

### Фиксы по рантайм-тесту пользователя
- B1 (цвет не сбрасывался на выходе): в `trail.js` при `target_zone_leave` теперь
  сбрасываем `t.armed=false` (раньше только `inZone`). Теперь выход из зоны возвращает
  красный цвет.
- B2 (диалог не показывался): вероятная причина — `SCSController.SetPause` бросал
  исключение ДО показа диалога (SDK недоступен в момент триггера). Теперь пауза в
  `try/catch`, диалог показывается гарантированно. Также завершение теперь по КНОПКЕ
  в диалоге, а не по выходу из зоны.
- B3 (метка за пределами карты приклеивалась к краю): в `map_draw.js` метка случайной
  цели за пределами карты теперь позиционируется у стрелки-указателя (в correct direction),
  а не у `rScreen` (далеко за экраном).
- Пользователь подтвердил: загрузка целей из файла при перезапуске корректна; несколько
  целей создаются; вход/выход триггеры верный. Убрал пункт «глюк авто-зум» из задач
  (это была реакция на неверные цели, не баг).
- Флапинг WS (карта постоянно коннект/дисконнект, анимация рестарт анимации, точки не показывались
  до `map_ready`): НЕ полностью решён — нужна рантайм-проверка. Возможно, стоит убрать
  `showUIWithAnimation` на каждый `map_ready`/коннект.

### Квест-кнопки (реализовано, нужна рантайм-проверка)
- 3 кнопки вместо 4 старых: `Курьер: забор 100м` (синяя), `Тайник: 200м POI` (жёлтая),
  `Перекус: 400м POI` (зелёная), + 4-я кнопка `Обзор целей (вкл/выкл)` (тоггл, шлёт
  `set_overview`). Левая панель кнопок расширена (width 175, консоль сдвинута).
- Карта: команды `quest_courier`/`quest_stash`/`quest_snack` + `quest_courier_dropoff`
  (создаётся C# после принятия Курьера) + `set_overview` + `hide_target`.
- Поток триггера (вход в зону -> диалог, завершение по кнопке):
  - courier_pickup: диалог «Доставить документы. 1200р. Расстояние: X м» (X=400..2000,
    сгенерировано), кнопки «Начать выполнение»/«Отказаться». Любой выбор удаляет точку
    забора. «Начать» -> создаёт фиолетовую courier_dropoff на расстоянии X.
  - courier_dropoff (радиус 35): диалог «Выручить документы?» ДА/НЕТ. ДА -> +1200р/+250xp,
    удалить. НЕТ -> закрыть, ждать реактивации при повторном входе.
  - stash (жёлтая, active:false => нет указателя за пределами, не в обзоре): диалог
    «Вы нашли тайник. +3000р» ОК -> +3000р, удалить.
  - snack (зелёная): диалог «Вы перекусили чем-то вкусным.» ОК -> -450р/+1000xp, точка
    скрывается на 5 мин (`hide_target` durationMs=300000, map пропускает отрисовку/триггер),
    затем появляется снова.
- Обзор целей: управляется ТОЛЬКО приложением (тоггл). `focusTargetOnMap` больше не
  включает overview. В `map_draw` при `targetMapOverview` охватываются ВСЕ активные цели
  (`active!==false`), stash исключается.
- Награды через `ets2c.exe` (-moneygive/-xpgive); для Перекуса money отрицательный (-450).
  Требует проверки, что ets2c принимает отрицательный moneygive.

### Что проверить у пользователя (рантайм)
- Несколько кнопок создают НЕСКОЛЬКО точек, все видны на миникарте (не дублируются).
- Вход в зону цели → зеленеет + диалог принятия; выход после принятия → цель исчезает + награда.
- Отказ → цель остаётся; повторный вход снова даёт диалог (не спамит постоянно).
- Старые pending сохраняются: авто-зумa (№2), геометрия кнопок 1/2/4.

### Планы (от пользователя, этапы квестов)
- **Супер-задача:** менеджер квестов управляет одним динамическим квестом «Курьер»
  (успех → повысить B и начать новый цикл C/D).
- **Премиум-задача:** менеджер квестов управляет ещё и квестом «Попутчик».
- Задачи по квестам сформулировать ПОСЛЕ завершения минимальных задач (внесены в план).

---

## Сессия 10 (продолжение) — точные названия кнопок + конфиг позиций оверлеев
- Build: `1.0.38.5-OVERLAY-CFG-08.28-1047`.
- Пользователь указал ТОЧНЫЕ названия 4 кнопок (старые не совпадали с заданием):
  `Курьер 100 POI дорога т50 а.у.` / `Тайник 2 200м т30` / `Перекус 400` / `Обзор целей`.
  Переименовал (MainForm.cs:332-342), расширил левую панель (width 230 -> consoleLeft 190->240).
- Сохранение позиции/размера окна приложения: УЖЕ реализовано через `AppSettings`
  (WindowX/Y/Width/Height/DeviceName -> `%LocalAppData%\ETS2_Assist\appsettings.json`,
  `ApplySavedWindowBounds`/`SaveWindowBounds` в MainForm.cs:2505/2540). Персистит при перезапусках
  и пересборках, дублировать не стал.
- Детекция экрана игры `GetGameScreen()` (MainForm.cs): ищет окно `eurotrucks2`/`amtrucks2`
  через `Screen.FromHandle(MainWindowHandle)`, иначе `Screen.PrimaryScreen`.
- Создание файлов позиций WebOverlay `EnsureOverlayWindowConfig()` (зовётся в `StartWebOverlay`):
  при первом запуске, если в `%AppData%\Roaming\WebOverlay\config\` нет файлов, создаёт их для
  порт-8082 URL (web_pda_map / web_ui_hybrid / web_pause_logo). Формат (по weboverlay/Program.cs):
  5 строк = X, Y, zoom=1, Width, Height; имя = url с недопустимыми символами -> '_' + '.txt'.
  Геометрия относительно `WorkingArea` экрана игры: миникарта — левый нижний, 6% площади;
  гибрид — центр по горизонтали у нижнего края, 20%; лого паузы — левый верхний, 3%.
- Версия 1.0.38.5 собрана офлайн (restore закэширован), бейджи обновлены.

### Сессия 10 (продолжение 2) — UI-FIX (build 1.0.38.6-UI-FIX-08.28-1056)
- Кнопки НЕ менялись визуально, потому что `ApplyLanguage()` (MainForm.cs:614-617)
  перезаписывал их Text ПОСЛЕ конструктора. Исправил там же: точные названия
  `Курьер 100 POI дорога т50 а.у.` / `Тайник 2 200м т30` / `Перекус 400` / `Обзор целей`.
  ВАЖНО: любые правки Text кнопок btnRandomTarget* делать В ДВУХ местах
  (конструктор ~332 и ApplyLanguage ~614).
- Правая панель индикаторов обрезалась: форма по умолчанию (1100px) уже, чем нужно
  для раскладки (список+действия+индикаторы). Добавил `this.MinimumSize = 1180x480`
  (до ApplySavedWindowBounds, чтобы клампило сохранённый/дефолтный размер), defaultWidth
  1100->1180. Расширил панель индикаторов: indicatorsWidth 180->190, panel 180->190,
  label 170->180, actionsWidth 125->120, пол индикатора listWidth 180->150. Теперь
  панель помещается в ClientSize с запасом ~10px.
- Версия 1.0.38.6 собрана.

### Сессия 10 (продолжение 3) — логика показа карты/гибрида/лого (build 1.0.38.7-MAP-LOGIC-08.28-1113)
- Миникарта: стартовый размер +20% и строго квадрат. `ComputeOverlayGeometry` (MainForm.cs):
  side = round(min(WA.W, WA.H) * sqrt(0.06) * 1.2), квадрат, левый нижний.
- Логика показа (`CheckPauseAndUpdateUI`, UI/WebUIManager.cs):
  - `active = gameRunning && !paused && gameFocused`. Карта+гибрид показываются
    ТОЛЬКО когда active. Во всех остальных случаях (пауза / не в фокусе / игра не
    запущена) карта+гибрид скрыты, а `web_pause_logo.html` (версия) показан.
  - Исправлена ошибка: раньше `showPauseLogo = paused && gameFocused` (лого не
    показывалось при потере фокуса). Теперь `showPauseLogo = !active`.
  - Гистерезис (~1с, 2 устойчивых тика) чтобы кратковременная потеря фокуса не
    мигала оверлеями (была жалоба «миникарта моргает и снова появляется»).
  - Отдельная команда для миникарты `minimap_show`/`minimap_hide` (НЕ show_ui/hide_ui,
    которые управляют гибридом), т.к. broadcast идёт всем окнам. `minimap_auto` {enabled}
    — ручной тоггл. В `websocket.js` (web_pda_map) добавлены эти команды; show_ui/hide_ui
    убраны из миникарты (гибрид их сам обрабатывает в hybrid_ui_websocket.js).
- Кнопка «Показать карту» = тоггл авто-логики миникарты. Выкл → миникарта скрыта
  независимо от состояния игры (`minimap_auto enabled=false` + `minimap_hide`);
  повторно → включает обратно. Текст кнопки меняется ✔/✖.
- Пауз-лого (web_pause_logo.html) уже корректно реагирует на show/hide_pause_logo
  и показывает версию из ets2_assist_build.txt.
- Версия 1.0.38.7 собрана. Осталось рантайм-проверить: показ/скрытие по фокусу/паузе,
  тоггл миникарты, квадратный размер +20%.

---

## Сессия 10 (продолжение 4) — ФИКС ДВОЙНЫХ ЦЕЛЕЙ + кулдаун/hidden/delete_on_complete (build 1.0.38.8-TARGET-FIX-08.28-1204)

### Баг: каждая цель создавалась в ДВОЙНОМ экземпляре (в разных местах)
- Симптом от пользователя: любая кнопка создания цели -> две точки; точка выдачи
  курьера (courier_dropoff) тоже дважды в разных местах.
- КОРЕНЬ: `generateRandomTarget` (data/js/targets.js) создаёт цель локально И просит C#
  записать её в `custom_targets.json` (`add_target`). C# `AddTargetToFile` (QuestsManager.cs)
  дописывал в файл и рассылал `targets_data` (который ЗАМЕНЯЛ список миникарты). Но если
  команда `quest_*` приходила ДВАЖДЫ (флапинг WS — см. Сессию 10 MAP-LOGIC, либо
  повторный бродкаст), генерились ДВА разных `id` -> файл накапливал дубли по одному типу.
- ФИКС (дедуп по `questType`, обе стороны):
  - C# `AddTargetToFile`: перед записью удаляет из файла и из `_randomTargets` любую
    существующую `isRandom`-цель ТОГО ЖЕ `questType` (другой id). Гарантирует <=1 активной
    цели каждого типа независимо от числа `add_target`.
  - JS `generateRandomTarget`: перед пушем удаляет из `state.randomTargets`/`state.customTargets`
    старую цель того же `questType` и шлёт `remove_target` (чтобы C# убрал из файла). Плюс
    `hidden` в объекте цели и в `add_target`-сообщении.
  - Результат: нажатие кнопки -> ровно одна точка нужного типа (старая заменяется новой).

### custom_targets: новые параметры (cooldown / hidden / delete_on_complete)
- Добавлены поля в запись файла (`AddTargetToFile`): `cooldown`, `current_cooldown`,
  `hidden`, `delete_on_complete`. Читаются из `add_target`-сообщения, по умолчанию 0.
- `normalizeTarget` (targets.js) теперь пропускает `hidden/cooldown/currentCooldown/deleteOnComplete`.
- `map_draw.js`: скрытая цель (`rt.hidden`) НЕ рисуется, но триггер зоны в `trail.js`
  остаётся активным (там пропуск только по `hiddenUntil`).
- C# `CompleteTargetById(id)`: при завершении цели применяет параметры:
  - `delete_on_complete==1` -> удалить навсегда (Тайник — разовая цель).
  - `delete_on_complete==2` -> удалить и попросить миникарту сгенерировать новую
    (та применит свой радиус/POI -> в >=радиусе от фуры).
  - `0` (по умолчанию) -> цель остаётся, `current_cooldown=cooldown` (запас 5 мин),
    `status=inactive`; таймер раз в минуту `DecrementCooldowns()` уменьшает
    `current_cooldown` на 1, пишет файл и при достижении 0 ставит `status=active`
    (+рассылка `targets_data` — только при сбросе в 0, без пересылки каждую минуту).
- Запросы генерации теперь несут параметры: `quest_stash` -> `deleteOnComplete:1`,
  `quest_snack` -> `cooldown:5`. Перекус больше НЕ использует `hide_target` 5мин —
  вместо него кулдаун (5 мин скрыта, затем снова активна). Тайник -> удаляется навсегда
  (исправляет «тайник появился снова после кулдауна», который пользователь видел).

### Сборка
- `dotnet publish` (Release, win-x64, self-contained, single-file) — SUCCESS, 0 error
  (только CS0414/CS860x варнинги, как ранее). Версия `1.0.38.8-TARGET-FIX-08.28-1204`.
- Бейджи `data/ets2_assist_build.txt` и `data/web_runtime_manifest.json` обновлены.
- EXE: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.

### Что проверить у пользователя (рантайм)
- Нажать кнопку создания цели -> РОВНО ОДНА точка (не две). Повторное нажатие ->
  старая заменяется новой, не копится.
- Курьер: принять -> фиолетовая точка выдачи ОДНА.
- Тайник: выполнить -> исчезает навсегда (не появляется снова).
- Перекус: выполнить -> точка скрывается ~5 мин, затем появляется снова (кулдаун).
- Прочие пункты из предыдущих сессий (геометрия +30%/+15%, лого версии, тёмная тема,
  награды жирным зелёным) — ЕЩЁ НЕ сделаны, ждут отдельной реализации.

---

## Сессия 10 (продолжение 5) — ГЕОМЕТРИЯ + ЛОГИКА ПОКАЗА + БЕЙДЖИ + ТЕМА + ДИАЛОГИ (build 1.0.38.9-UI-THEME-08.28-1232)

### Геометрия оверлеев (ComputeOverlayGeometry, MainForm.cs)
- Миникарта: +30% (было *1.2 -> *1.3), квадрат (~6% площади).
- Гибрид: опущен на 15% высоты экрана (`y = wa.Bottom - h + (int)(wa.Height*0.15)`);
  край может уйти за пределы экрана (по согласованию с пользователем — видимый
  контент не скрывается).
- Пауз-лого: +15% (было *1.0 -> *1.15), строго квадрат (~3% площади).

### Логика показа оверлеев (CheckPauseAndUpdateUI, UI/WebUIManager.cs)
- НОВОЕ ПОВЕДЕНИЕ: вне фокуса игры СКРЫВАЮТСЯ ВСЕ оверлеи (включая пауз-лого).
- В фокусе и НЕ на паузе -> карта + гибрид показаны, пауз-лого скрыт.
- В фокусе и на паузе -> ТОЛЬКО пауз-лого; карта+гибрид скрыты.
- Гистерезис (~1с, 2 тика) теперь по ФОКУСУ. `showMapHybrid = focused && !paused`;
  `showPause = focused && paused`.

### Динамические бейджи версии (устранили «отставание на версию»)
- `web_pda_map.html` (#mapBuildBadge) и `web_ui_hybrid.html` (#ets2AssistBuildBadge):
  fetch `ets2_assist_build.txt?_=<cache-bust>` + `setInterval(upd,5000)`.
  Гибридный бейдж перенесён в НИЗ ПО ЦЕНТРУ (`left:50%;bottom:8px;translateX(-50%)`).
- `web_pause_logo.html`: версия ПОЛНАЯ (`v.<вся строка>`, раньше только края) и
  ЦЕНТРИРУЕТСЯ под лого; добавлен `setInterval(upd,5000)` + обновление при показе
  лого -> больше не отстаёт на версию после пересборки.

### Тёмная/светлая тема + кнопка (MainForm.cs)
- Кнопка `btnTheme` «Тема: светлая/тёмная» в правом верхнем углу.
- `ApplyTheme()` + `SetControlTheme`:
  - Тёмная — фон `#2b2b2b`, шрифты `#e8e8e8`,
    контролы `#3c3c3c`, поля `#1e1e1e`; светлая — системные. Рекурсивно по всем контролам.
  - Без сохранения в настройках — пока в памяти.)

### Диалоги: награды жирным зелёным (QuestDialogForm.cs)
- `message` через `RichTextBox`; строки с наградой (`руб`/`опыта`/`опыт`/`Награда`/
  шаблон `[+-]\d`) — жирный зелёный `#009628`: `+3000р`, `-450р/+1000xp`, `1200р`.

### Сборка
- `dotnet publish` (Release, win-x64, self-contained, single-file) — SUCCESS, 0 error
  (только CS0414). Версия `1.0.38.9-UI-THEME-08.28-1232`. Бейджи обновлены.
- EXE: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe`.
- Для НОВОЙ ГЕОМЕТРИИ: удалить файлы позиций WebOverlay
  (`%AppData%\Roaming\WebOverlay\config\*web_pda_map*.txt` и т.д.), иначе оверлей
  использует закэшированные координаты.

### Что проверить (рантайм)
- Миникарта +30% квадрат; гибрид ниже; лого паузы крупнее.
- Вне фокуса -> все оверлеи скрыты; в фокусе на паузе -> только лого; в игре -> карта+гибрид.
- Бейджи показывают ТЕКУЩУЮ сборку и сами обновляются.
- «Тема» переключает тёмную/светлую; диалоги — награды зелёным жирным.

### Редактор карты (MapEditorForm.cs, MainForm.cs) — 1.0.38.10-MAP-EDITOR
- НОВАЯ форма `MapEditorForm` (WinForms, GDI+, БЕЗ weboverlay): рисует дороги
  (кэш `GraphicsPath` в мировых координатах + матричная трансформация), города и цели из
  `custom_targets.json`. Север вверх, колесо — зум к курсору, правая кнопка — панорама.
- Позиция фуры — через WebSocket-клиент (`WebSocketSharp.WebSocket`) к
  `ws://localhost:8080/api/ws/delta/flat/?throttle=50`, поле `truck.world.placement`;
  авто-переподключение таймером.
- Клик ЛКМ: у цели — копирует `gameName` (id) + открывает `custom_targets.json`;
  у города — координаты `x,y,z`; в пустоте — координаты под курсором (в буфер).
- Кнопки: «найти грузовик», «показать всё», «обновить цели». Масштаб/центр сохраняются
  в `map_editor_state.json`. Кнопка «Редактор карты» добавлена в MainForm (конструктор+ApplyLanguage).

### ИНЦИДЕНТ: пустая publish\data + залоченный exe (28.08.2026)
- Симптом: в `publish\data` осталась только `bin\weboverlay`, остальное (js/css/GeoJson/
  language/localized_cities) пропало. Причина: пересборка 1.0.38.10 прервалась на `GenerateBundle`
  — exe был ЗАПУЩЕН (пользователь тестировал) → `UnauthorizedAccessException`, цель
  `SyncDataToPublish` не доработала, data не скопировалась.
- Фикс: добавлено ПРАВИЛО (INSTRUCTIONS.md) — перед публикацией проверять блокировку exe и
  ПРИ ЗАЛОЧЕННОМ процессе ВЫДАВАТЬ ПОЛЬЗОВАТЕЛЮ ЗАПРОС закрыть exe, затем продолжить.
  Пересобрано в стандартный путь: `SyncDataToPublish: скопировано 390 файлов data`
  (все папки + `language/ru.csv`,`en.csv`, `localized_cities/cities_sibirmap.json`).
- Версия `build.txt`/`manifest` синхронизированы с exe: `1.0.38.10-MAP-EDITOR-08.28-1446`.

### ФИКС фриза редактора карты (MapEditorForm.cs) — 1.0.38.11-MAP-EDITOR-FIX
- Причина фриза: (а) каждый кадр перерисовывал гигантский `GraphicsPath` ВСЕХ дорог
  (~98k фич roads.geojson, 45 МБ) — GDI+ рендерил миллионы сегментов 20 раз/сек из-за
  телеметрии; (б) после закрытия формы фоновая задача загрузки и WebSocket-клиент
  телеметрии НЕ останавливались → продолжали жрать CPU.
- Решение:
  - Слой дорог рендерится ОДИН раз в offscreen-битмап (`RenderRoadLayer`) при смене вида,
    с отсечением невидимых сегментов (culling по видимому миру); перерисовка debounce-таймером
    (70 мс). `OnPaint` теперь только блитит битмап + рисует города/цели/фуру — дёшево.
  - Индикатор загрузки по этапам: «Этап 1/2: разбор файла (45 МБ)…» → «Этап 2/2: дороги N из total,
    сегментов S» → «Готово: дорог N, сегментов S». Прогресс через BeginInvoke (троттлинг 120 мс).
  - `CancellationTokenSource` для загрузки; `OnFormClosing` отменяет загрузку, останавливает
    `_wsReconnectTimer`/`_renderTimer`, закрывает `_ws` (Close, Dispose недоступен в этой версии
    WebSocketSharp), освобождает битмап, ставит `_disposed` (защита BeginInvoke после закрытия).
  - Телеметрия больше не форсирует тяжёлую перерисовку — только дешёвый Invalidate поверх кэша.
- Собрано `1.0.0.38.11-MAP-EDITOR-FIX-08.28-1500`, data (390 файлов) синхронизирована.

### ПЛАН/БЭКЛОГ: расшифровка типов сегментов дорог (roads.geojson `properties.roadType.String`)
- В `data/GeoJson/roads.geojson` каждая LineString-дорога имеет `properties.roadType.String`
  (напр. `"balt11"`, `"balt11"` и т.п.) — это тип/класс дороги. Пока все дороги рисуются
  одним цветом/толщиной.
- ЗАДАЧА: выяснить, чему соответствуют эти строковые коды (таблица SCS road types / профили
  дорог ETS2), и назначить каждому типу свою визуальную форму (цвет, толщину, пунктир,
  оверлей-иконку) — чтобы дороги стали информативнее. Вероятно есть свой набор значений
  (highway/local/urban/второстепенные и т.д.), возможно привязаны к `roadType.Value` (числовой хэш).
- Источники: документация SCS SDK / форумы ETS2 modding; в `data/` поискать `roadType`,
  `balt`, `road_profile`. После расшифровки — добавить парсинг `roadType.String` в `LoadRoads`
  и раскраску по типу в `BuildRoadsPath` (несколько `GraphicsPath` по типам или цвет пера
  по сегменту).

### Состояние редактора карты (актуально на 1.0.38.20)
- Города: жёлтые круги (alpha 0.8) с чёрной обводкой 2px, подписи жёлтые с чёрной обводкой
  (шрифт 9.5). Сайдбар `TreeView`: Цели → Города → POI по категориям (Company, Fuel, Garage,
  Parking, Service, BusStop, Ferry, Recruitment, Train, TruckDealer, WeightStation, Overlay).
- POI из `Overlays.json` наложены цветом по категории (та же мировая СК, что у дорог; кажутся
  «не на месте» только из-за клипа дорог x≥111805.88 && z≤-36536.58 — вне клип-региона
  дорог нет, а POI есть → висят на пустом месте). Макс. масштаб (зум-аут) увеличен до 600 м/px.
- Наведение на точку: курсор `Hand`, тултип = латинская переменная (city.gameName / target.id),
  для POI — категория.   Названия целей на карте берутся из `realName`.

---

## Сессия 10 (продолжение 21) — КЛЮЧЕВОЕ РЕЗУЛЬТАТ: система координат POI = дороги = игра
- Доказано эмпирикой пользователя. Точка `0x63DF00527280076D`:
  - `Overlays.json`: `x=119602.05, z=-53518.26`
  - В игре (телепорт в ту точку): `X=119567, Z=-53536.6`
  - Расхождение ~35 м по X / ~18 м по Z — погрешность парковки фуры. **ОДИН масштаб.**
- Соседний город из `cities_sibirmap.json` ≈ `119554, -53560` — тоже тот же масштаб.
- ВЫВОД: никакого умножения/смещения POI не нужно. Любое «×100» СЛОМАЕТ наложение
  (станет 11960205, что не совпадает ни с игрой, ни с городом).

### Почему POI казались «гигантскими и со смещением» (до фикс)
- Дороги КЛИПУЮТСЯ регионом `x≥111805.88 && z≤-36536.58` (северо-восток; +z = юг,
  поэтому здесь «ниже» = z>-36536.58 отсекается, оставляем z≤ClipZMin). Города НЕ клипуются.
- POI раньше рисовались ПО ВСЕЙ карте (вплоть до x=1082, z=+5195) → вне клип-региона
  дорог нет, POI висят на пустом месте, а при «показать всё» облако POI огромно и смещено
  относительно крошечной полоски дорог. Это и давало иллюзию «другой СК / гигантского масштаба».

### Что сделано (commits-привязка по VersionIter)
- **1.0.38.21** «финальные штрихи»: тёмные кнопки (BackColor 40,48,62 / LightGray / Flat),
  max zoom-out 600, шрифт городов 9.5, круглые города с чёрной обводкой, тултип+Hand на
  наведении (city.gameName / target.id / POI=категория), названия целей из `realName`.
- **1.0.38.22** (НЕ билдился сразу — см. ниже): добавлены временные кнопки тулбара
  «инвертировать v POI» / «инвертировать h POI» (тогглят `_invertV`/`_invertH`, вызывают
  `ApplyPoiTransform()`+`PopulateSidebar()`+`RequestRender()`). Это была ГИПОТЕЗА, что POI
  отзеркалены — гипотеза ОПРОВЕРГНУТА телеметрией (СК совпадает). Кнопки можно удалить.
- **1.0.38.22 (реальный билд)** ТЕЛЕМЕТРИЯ: порт WS ДИНАМИЧЕСКИЙ. Редактор читает `wsPort`
  из `web_data.json` (`AppDataPaths.WebDataFile`, пишет TruckTel-бридж) → `GetTelemetryWsUrl()`
  = `ws://localhost:{wsPort}/api/ws/delta/flat/?throttle=50`, фолбэк 8080. Фикс «фура не
  найдена / игра не запущена» (редактор раньше хардкодил 8080). Диагностическая страница
  `web_telemetry_inspector.html` тоже переведена на чтение `web_data.json` для порта.
- **1.0.38.23** «показать всё» переписан: охватывает 4 крайние точки по roads+cities+targets+pois
  с отступом 2000; ограничение зума 600 СНЯТО (`MaxScale=8000`), чтобы вписать всё. ПЛЮС
  **клип POI** тем же регионом, что и дороги (`if (x<ClipXMin||z>ClipZMin) continue;` в
  `ApplyPoiTransform`) → POI теперь ложатся точно на видимые дороги в одном регионе.
- **1.0.38.24** диагностика: в тултип POI добавлены реальные координаты, которые хранит
  редактор (`poi.category + "  " + x + ", 0, " + z`). Нужно, чтобы разрешить парадокс ниже.

### ОТКРЫТЫЙ ПАРАДОКС (главная недоделка — разрешить в новой сессии)
- Пользователь скопировал из РЕДАКТОРА координату POI = `11960205.00, 0, -5351822.00`
  (ровно ×100 от файловых `119602.05`). Предложил «умножим Overlay на 100».
- В коде ×100 НЕТ (проверено: парсинг `LoadOverlays` — `TryParse(xv.ToString())`; проекция
  `WorldToScreen`/`ScreenToWorld`; отрисовка POI — все без масштаба). Оба файла
  (`data/Overlays.json` и `publish/data/Overlays.json`) содержат `119602.05`.
- Поэтому редактор ДОЛЖЕН показывать `119602.05`. Значит `11960205`, который видит
  пользователь, — артефакт СТАРОЙ/другой сборки (не 1.0.38.24) либо кэша.
- **СЛЕДУЮЩИЙ ШАГ (новая сессия):** запустить 1.0.38.24, навести на POI `0x63DF00527280076D`,
  прочитать тултип.
  - Если тултип `119602.05, 0, -53518.26` → редактор корректен, POI ложатся на дороги
    (просто перепроверить, что запущена именно сборка 24; кнопки инверсии можно удалить).
  - Если тултип `11960205.00, ...` → ×100 реально есть в рантайм
  (не умножать на 100 — это усугубит).
- Текстовое поле «копировать» (OnMouseClick → anyCoord) и тултип — единственные места,
  откуда пользователь берёт координаты; hit-test POI в OnMouseClick пока отсутствует
  (клик по POI даёт anyCoord мира под курсором, а не поле POI).

### Технические детали (для продолжения)
- Поля: `_poisRaw` (список сырых `(category,uid,x,z)`), `_invertV`,`_invertH`;
  `ApplyPoiTransform()` наполняет `_pois` с учётом инверсии и клипа.
- `LoadOverlays()` читает `AppDataPaths.StaticDataDirectory/Overlays.json` (JObject, 12 категорий).
- Клип константы: `ClipXMin=111805.88`, `ClipZMin=-36536.58` (поля формы).
- Текущая последняя собранная: **1.0.38.24-MAP-EDITOR-FIX-08.28-1708** (390 файлов data).

### КЛЮЧЕВАЯ ЗАДАЧА ДЛЯ СЛЕДУЮЩЕЙ СЕССИИ (НОВЫЙ ПК) — 28.08.2026
- Запустить **1.0.38.24** на другом ПК, навести на POI `0x63DF00527280076D`, прочитать тултип.
  - `119602.05, 0, -53518.26` → редактор корректен, POI ложатся на дороги
    (просто перепроверить, что запущена именно сборка 24; кнопки инверсии можно удалить).
  - `11960205.00, ...` → ×100 реально есть в рантайм
  (не умножать на 100 — это усугубит).
- СК POI = дороги = игра (погрешность парковки ~35м/X, ~18м/Z). Масштаб один.

### Бэклог редактора / проекта (не сделано)
1. **Расшифровка `roadType.String`** (roads.geojson) — назначить цвет/толщину/стиль по
   типу дороги (highway/local/urban…). См. план выше (строки ~767). Не начато.
2. Удалить временные кнопки «инвертировать v/h POI» (после подтверждения СК).
3. Добавить hit-test POI в `OnMouseClick`, чтобы клик копировал поле POI (как у городов/целей).
4. Общий бэклог приложения (вне редактора, с приоритетом после редактора): перекрытие
   иконок событий, иконка «сервис», мини-лого регрессия, кнопка темы (ресайз/перенос),
   миникарта ×2, переделка логики паузы, «Кафе У Дороги», инвентарь. (Из предыдущих планов.)
5. Флапинг WS миникарты при старте (коннект/дисконнект, рестарт анимации) — не до конца
   решён (см. Сессию 10). Проверить, убрать `showUIWithAnimation` на каждый `map_ready`.

---

## Сессия 11 (28.08.2026) — РАЗРЕШЕНИЕ ПАРАДОКСА ×100 (POI)

### Эмпирика пользователя (новый ПК, запущен 1.0.38.24)
- Тултип POI `0x63DF00527280076D` = `11960205.00, 0, -5351824.00` — ровно ×100 от
  файловых `119602.05 / -53518.26`. Парадокс ВОСПРОИЗВЁЛСЯ на свежей 1.0.38.24 →
  НЕ был «старой сборкой/кэшем», как гипотеза ранее.
- Центр по умолчанию при открытии редактора: `163268.00, 163.57, -75657.80` (scale 132.5 —
  это fit-to-all). Это НОРМА: дороги/города в том же масштабе (город `akyar` x=159839),
  значит ×100 был ТОЛЬКО в оверлеях, не в дорогах/городах.

### Анализ
- Код `LoadOverlays` (MapEditorForm.cs) НЕ умножает (парсит `x/z` напрямую), `ApplyPoiTransform`
  только инверсия. `Overlays.json` в репозитории/публикации корректен (`119602.05`, ×100 нет).
- Проверено на этой машине по пути `publish\data\Overlays.json`: содержит `119602.05`,
  `11960205` отсутствует. ⇒ на ПК пользователя читается ИНОЙ `Overlays.json` (с ×100) —
  StaticDataDirectory = `BaseDirectory/data`, но файл там, видимо, другой версии/генерации.

### Фикс (защитная нормализация, не «умножение»)
- `MapEditorForm.LoadOverlays`: после парсинга `x/z`, если ОБЕ оси > 1 000 000
  (реальные координаты карты ≤ ~200 000) — делим на 100 и `Debug.WriteLine` предупреждение.
  Корректный файл (119602.05 < 1 000 000) НЕ затрагивается; ×100-файл авто-исправляется
  и POI ложатся на дороги (в клип-регион).
- Это решает парадокс независимо от того, какой `Overlays.json` на диске: редактор
  всегда приводит POI к единой СК с дорогами/городами.

### Версия / сборка
- `24`→`25`, `VersionDesc`=`POI-FIX`. Собрано **`1.0.38.25-POI-FIX-08.28-2006`**
  (offline publish, 0 error CS; SyncDataToPublish скопировала data; `Overlays.json` корректен).
- Бейджи `data/ets2_assist_build.txt` и `data/web_runtime_manifest.json` обновлены до
  `1.0.38.25-POI-FIX-08.28-2006` (синхронны с exe).
- Версия 1.0.38.24 (пред. публикация) помечена устаревшей; актуальна 1.0.38.25.

### Что проверить у пользователя (рантайм, новый ПК)
- Запустить 1.0.38.25, открыть редактор, навести на `0x63DF00527280076D` → тултип
  должен показать `119602.05, 0, -53518.26` (нормализовано). Если файл на диске был с
  ×100 — редактор сам делит; если корректный — оставляет как есть.
- POI теперь лежат НА дорогах (в клип-регионе), не «висят в пустоте».
- Кнопки «инвертировать v/h POI» (временные, гипотеза отзеркалки) можно удалить — СК
  подтверждена единой (оставлены пока для наглядности, но уже не нужны).

### Бэклог (обновление)
- Удалить кнопки «инвертировать v/h POI» в MapEditorForm (конструктор + handlers) — гипотеза
  отвергнута, кнопки лишние.
- (прочее из предыдущего бэклога сохраняется: roadType.String, hit-test POI, флапинг WS.)

---

## Сессия 12 (28.08.2026) — POI НА МИНИКАРТЕ + ГРУЗОВИК В РЕДАКТОРЕ

### POI на миникарте (`data/js/`)
- **×100 (как в редакторе):** `init.js` при `state.pois.push` теперь нормализует
  `x/z`: если ОБЕ оси > 1 000 000 — делит на 100. Корректный файл (`119602.05`) не трогается.
- **Точка ×2 + обводка 2px:** `map_draw.js` POI-блок — `size = min(12, poiPointSize*2)`,
  после заливки `ctx.lineWidth = 2; ctx.strokeStyle = '#000'; ctx.stroke();`.
- **Название:** `poi.name || poi.type || 'poi'` (в `init.js` name = realName||name||
  gameName||category → при отсутствии всех трёх это категория, как просил пользователь).
- **Метки сверху по центру:** `greedy.js` `createLabelElement` теперь ВСЕГДА
  `el.style.transform = 'translate(-50%, -50%)'` (раньше только в SHOW_BBOX). Раньше левый
  край метки ставился в точку → текст уходил ВПРАВО. Теперь центрируется (greedyPlacement
  и так считает x/y центром). Якоря меток уже над точкой (`y-20/-12/-10`).
- **Порядок отрисовки:** переставлено — POI рисуются ПЕРВЫМИ, затем города, затем цели
  (точки целей рисуются в greedy-секции ПОСЛЕ городов/POI). Итог: города накрывают POI,
  цели накрывают города (и их названия/точки). Метки городов/целей — DOM-слой (поверх canvas-POI).

### Редактор карты — удаление кнопок инверсии
- Удалены кнопки «инвертировать v/h POI» (конструктор + handlers), поля `_invertV/_invertH`,
  их использование в `ApplyPoiTransform` (теперь просто `x=p.x; z=p.z`). Гипотеза отзеркалки
  окончательно отвергнута (СК едина).

### Грузовик в редакторе — «нет данных от фуры» (ГЛАВНАЯ проблема)
- **Причина:** `MapEditorForm.EnsureTelemetry` брал порт ТОЛЬКО из `web_data.json`
  (`json["wsPort"]`); если файл устарел/неверен — подключение шло не туда. Плюс парсинг
  `placement[0]` через `(string?)` падал, т.к. TruckTel шлёт координаты ЧИСЛАМИ (JValue number
  → `(string?)` = null → TryParse fail → `_truckKnown` никогда не true).
- **Фикс:**
  - `GetTelemetryWsUrl` → `GetCandidatePorts()`: сначала порт из `web_data.json`, затем
    гарантированный **`8080`** (именно на нём работает `web_telemetry_inspector.html`,
    подтверждено пользователем). При ошибке/закрытии — цикл на следующий порт.
  - Парсинг `placement`: `xTok.ToString()` (работает и для чисел, и для строк) +
    `SelectToken("truck.world.placement")` + вложенный `truck/world/placement` (на случай,
    если Newtonsoft трактует точечный ключ как путь).
  - **Диагностика в строке статуса:** `OnOpen` → «телеметрия: подключено (порт X)»;
    `OnError` → «ошибка (порт X)»; пришли координаты → «телеметрия: фура X=.. Z=..».
    Все апдейты статуса через `BeginInvoke` (без cross-thread).

### Версии / сборки
- `26` POI-MAP-TRUCK (первый заход: POI-метки/порядок/кнопки) → `27` TRUCK-FIX
  (перебор портов + парсинг чисел + диагностика). Собрано **`1.0.38.27-TRUCK-FIX-08.28-2043`**
  (offline publish, 0 error CS; data синхронизирована; бейджи синхронны с exe).
- `node --check` init.js/map_draw.js/greedy.js — OK.

### Что проверить у пользователя (рантайм)
- Миникарта: POI видны (×2 точки, 2px обводка), названия КАТЕГОРИЙ сверху по центру;
  при наложении города перекрывают POI, цели перекрывают города.
- Редактор: строка статуса должна показать «телеметрия: подключено (порт 8080)» и
  «фура X=.. Z=..» (вместо «нет данных от фуры»). Если покажет «ошибка (порт …)» —
  сообщить, какой порт, — значит TruckTel на другом.

---

## Сессия 13 (28.08.2026) — РЕДАКТОР: МЕТКИ НАД ТОЧКОЙ + СТАТУС-БАР ВНИЗ + ЛОГ

### ВАЖНОЕ УТОЧНЕНИЕ ПОЛЬЗОВАТЕЛЯ
- Всё предыдущее (×100, центрирование greedy.js, POI на миникарте) относилось к ВЕБ-МИНИКАРТЕ
  (`web_pda_map.html` / `data/js/*`). Но пользователь имел в виду РЕДАКТОР КАРТЫ
  (`MapEditorForm.cs`, C#/GDI на `_mapPanel`) — там метки рисуются СВОИМ C#-кодом,
  а НЕ JS. Поэтому JS-правки не влияли на редактор. Исправлено в C#.

### Редактор: подписи над точкой по центру (MapEditorForm.OnPaint)
- БЫЛО: города/цели рисовались `p.X + 7` (текст слева-сверху → визуально СПРАВА от точки);
  ПОИ вообще НЕ имели подписи (только точка). Грузовик — `p.X + 8` (справа).
- СТАЛО: новый метод `DrawLabelAbove(g, text, cx, cy, color, bold)` — измеряет ширину
  текста и рисует его ПО ЦЕНТРУ НАД точкой (`x = cx - w/2`, `y = cy - h - gap`), с
  чёрной обводкой (4 направления) для читаемости.
- Города: `DrawLabelAbove(..., жёлтый, bold)`. Цели: `DrawLabelAbove(..., белый, bold)`.
  Грузовик: `DrawLabelAbove("Грузовик", ..., красный, bold)`.
- ПОИ: `DrawLabelAbove(poi.category, ..., CategoryColor)` — т.к. в `Overlays.json` у POI
  НЕТ поля имени (только `uid/x/z`), по договорённости пишем КАТЕГОРИЮ (родительский ключ,
  напр. `Company`, `Viewpoint`). Старый `DrawLabelWithOutline` удалён (заменён на `DrawLabelAbove`).

### Редактор: статус-бар вниз во всю ширину
- `_statusLabel.Dock` из `Top` → `Bottom` (высота 24). Тулбар остался `Bottom` (ниже
  статус-бара). Итог: статус-бар — ПОД картой, во всю ширину, между картой и тулбаром.
  Туда же выводятся сообщения телеметрии («подключено (порт …)», «фура X=.. Z=..», «ошибка»).

### Редактор: логирование всех шагов (Logger.Current -> Logs/app_workflow.log)
- `Logger.Current` (статическое) теперь устанавливается в `MainForm.InitializeProcessManager`
  (`Logger.Current = logger;`), поэтому редактор пишет в ТОТ ЖЕ `app_workflow.log`.
- Хелпер `LogEditor(msg)` → `Logger.Current?.Info("[EDITOR] " + msg)`.
- Логируется:
  - Открытие редактора + сводка (городов/целей/POI/дорог).
  - `LoadOverlays`: сколько POI загружено (raw/applied) из `Overlays.json`.
  - `GetCandidatePorts`: существует ли `web_data.json` и список кандидат-портов.
  - `EnsureTelemetry`: «попытка подключения к порту X» (каждый перебор), `OnOpen` (подключено),
    `OnError` (ОШИБКА + текст ошибки), `OnClose` (закрыто, переключение на след. порт),
    исключение при `ConnectAsync`, и пришедшие координаты фуры (X/Z) либо причина,
    почему placement не распознан (нет ключей / координаты не числа).
- Агент ПРОЧИТАЕТ `Logs/app_workflow.log` после прогона пользователя для диагностики.

### Версия / сборка
- `28` EDITOR-LABELS-LOG. Собрано **`1.0.38.28-EDITOR-LABELS-LOG-08.28-2058`**
  (offline publish, 0 error CS; бейджи синхронны с exe).

### Что проверить у пользователя (рантайм)
- В редакторе: подписи городов/целей/ПОИ/грузовика — НАД точками, по центру (а не справа);
  у ПОИ подпись = категория (имени в Overlays.json нет).
- Внизу под картой — статус-бар (полная ширина) с сообщениями телеметрии.
- Запустить приложение + редактор; агент прочитает `Logs/app_workflow.log` (теги `[EDITOR]`),
  чтобы увидеть, в какие порты ходил WS и подключился ли.

---

## Сессия 15 (28.08.2026) — ПРАВКИ: масштаб координат фуры, спам, цвет наград

### Телеметрия фуры: формат координат (editor)
- Пользователь: координаты приходят «слитно» (без точки). Счёт: X нужно делить на `1e9`,
  Z — на `1e12`, чтобы попасть в диапазон карты (X≈119822, Z≈−53603). Именно так фура
  оказывается ВНУТРИ карты (единый масштаб по обеим осям в метрах соблюдается).
- `MapEditorForm.ProcessTelemetry`: `_truckX = tx / TruckCoordScaleX; _truckZ = tz / TruckCoordScaleZ;`
  (константы `TruckCoordScaleX = 1e9`, `TruckCoordScaleZ = 1e12`).

### Убран спам координатами
- Статус-бар редактора больше не пишет `X=.. Z=..` каждый кадр — только
  «телеметрия: фура (данные получены)».
- Из лога убрана строка «получена телеметрия фуры X=.. Z=..». Оставлены только
  диагностические: «ПОДКЛЮЧЕНО к порту», «ОШИБКА подключения», «placement не распознан».

### Диалог квестов: цвет наград (QuestDialogForm.ColorRewardLines / IsRewardLine)
- БАГ: строка красилась зелёной, если `IsRewardLine` возвращало true по регулярке
  `[+\-]\s*\d` — из-за этого любой текст с «-53603» / «X-Y» / отрицательным числом
  красился (зелёным было не награда, а фрагмент текста).
- ФИКС: `IsRewardLine` теперь строгий — строка награда, если содержит «Награда/награда»
  ЛИБО `[+\-]\s*число` с ОБЯЗАТЕЛЬНОЙ единицей награды справа (`р`, `р.`, `руб`,
  `опыта`, `опыт`, `xp`, `монет`). Обычный текст с «-/число» (координаты и т.п.)
  больше не красится.
- `ColorRewardLines` теперь СНАЧАЛА сбрасывает весь текст к обычному цвету/шрифту, затем
  красит ТОЛЬКО строки наград (жирный зелёный `Color.FromArgb(0,150,40)`). Остальной
  текст — обычным шрифтом. Награды в сообщениях квестов и так идут отдельной строкой
  ниже описания («Награда: 1200р.», «+3000р», «+250xp»).

### Версия / сборка / БАГ БЕЙДЖЕЙ
- `29` POI-WS → `30` TRUCK-SCALE (масштаб координат + убран спам) → `31` QUEST-REWARD.
- **ВАЖНО (дефект синхронизации бейджей выявлен):** при сборках 30 и 31 правки бейджей
  (`data/ets2_assist_build.txt` и `data/web_runtime_manifest.json`) в какой-то момент НЕ
  применились (oldString не совпал — manifest оставался `1.0.38.29`). В 31 всё выправлено
  и перепроверено: EXE / build.txt / manifest = **`1.0.38.31-QUEST-REWARD-08.28-2143`**.
- НАПОМИНАНИЕ агенту: после каждой смены версии ОБЯЗАТЕЛЬНО перечитать оба бейдж-файла
  и сверить с `ProductVersion` exe (не полагаться на «успех» edit-вызова — проверять
  фактическое содержимое). Правило совпадает с INSTRUCTIONS.md.

### Что проверить у пользователя (рантайм)
- В редакторе фура рисуется на карте в правильном месте (X≈119822, Z≈−53603), без спама
  координатами в статусе/логах.
- Диалог квеста: зелёным и жирным — ТОЛЬКО строки наград («Награда: 1200р.», «+3000р»,
  «+250xp»); весь прочий текст — обычным шрифтом/цветом (координаты и прочее не зелёные).

---

## Сессия 16 (28.08.2026) — ПРАВКИ: масштаб фуры, пауза квестов, POI, дистанция, кнопки

### Масштаб координат фуры в редакторе (MapEditorForm)
- Пользователь: в игре фура ~`119824, -53601`; приложение показывало `11982289, -503603`.
  Явная инструкция: «раздели 11982289 на 10 чтобы было 119822,89» → фактически ЗНАЧИТ
  поделить отображаемое на 100 (11982289/100 = 119822,89; /10 дало бы 1198228,9 — неверно).
- Значит эффективный масштаб увеличить в 100 раз: `TruckCoordScaleX 1e9 → 1e11`,
  `TruckCoordScaleZ 1e12 → 1e14`. Теперь отображаемое X≈119822,89 (совпадает с игрой 119824).
- ⚠ НЕОПРЕДЕЛЁННОСТЬ ПО Z: в игре Z≈−53601, но после /100 отображаемое Z будет ≈−5036
  (на момент чтения). Скорее всего фура сместилась между замерами, либо Z нужен свой
  коэффициент. Единый масштаб обязателен для геометрии карты, поэтому применил одинаковый
  ×100. НУЖНА ПРОВЕРКА Z у пользователя; если Z «не там» — уточнить.

### Отрисовка фуры без спама + статус-индикатор (MapEditorForm)
- `ProcessTelemetry`: координаты больше НЕ пишутся в лог и не в статус. `InvalidateMap`
  теперь не чаще 1 раза в 500 мс (поле `_lastTruckDraw`); первая порция рисуется сразу.
- Статус-бар: зелёная точка «● Координаты грузовика онлайн» (ForeColor зелёный) при
  `_truckKnown`, красная точка «● Нет данных от грузовика» при отсутствии. Цвет через
  `ForeColor` (● = U+25CF). Подключение WS в статус больше не пишется (только в лог).
- Добавлен `_truckWatchdog` (таймер 1 с): если телеметрии нет >3 с — `_truckKnown=false`,
  красная точка + перерисовка.

### ЛОГИКА ПАУЗЫ КВЕСТОВ (КРИТИЧНО, QuestsManager)
- БАГ: раньше пауза отправлялась УЖЕ ПОСЛЕ захвата фокуса диалогом, и в finally снималась
  пауза (`SetGamePause(false)`) → игра не вставала на паузе при диалоге, а при закрытии
  снималась (авария/неудобно).
- Новый `TriggerQuestDialog(st)` (async):
  1) строгая проверка `await IsGamePausedAsync()` — если игра УЖЕ на паузе, пауза НЕ
     отправляем (иначе игрок не успеет взять управление → авария);
  2) иначе `SetGamePause(true)` и ПОЛЛИНГ до подтверждения паузы (20×250 мс = 5 с);
  3) задержка `await Task.Delay(2000)`;
  4) только потом `Show()`/`Activate()` + `HandleQuestEnter` (фокус на диалог).
- В `finally`: `_questHandling=false` и `ReturnFocusToGame()` — НО `SetGamePause(false)`
  БОЛЬШЕ НЕ ВЫЗЫВАЕТСЯ НИКОГДА. Игра остаётся на паузе; игрок сам её снимает.
- Защита `_questHandling` от повторного входа в зону пока диалог активен.

### Курьер: точки у POI + награда (QuestsManager + data/js)
- `data/js/targets.js`: добавлен режим `atPoi` (до ветки distanceM) — цель СТРОГО НА POI
  (здание), в радиусе `poiMinDistM..poiMaxDistM`, либо ближайшая к `poiTargetDistM`;
  учитывается близость дороги (`maxDistanceToPOI`).
- `data/js/websocket.js`: `quest_courier` → `atPoi` (POI в 40..350 м от фуры);
  `quest_courier_dropoff` → `atPoi` с `poiTargetDistM = distanceM` (POI ≈ в distanceM от
  фуры). Теперь забор и доставка документов — у зданий POI, а не на трассе.
- Награда курьера: `reward = 1000 + round(30 * dist / 150)` (база 1000р + 30р/150м),
  `dist = Random(400..2000)`. Хранится в `_courierReward` при принятии и начисляется при
  выдаче (`GiveReward(reward, 250)`). Текст диалога показывает вычисленную награду.

### Формат дистанции на миникарте (data/js/init.js formatDistance)
- <1000 м → «N метров» (напр. «900 метров»);
- 1000..9999 м (км<10) → «X,Y км» с одной цифрой после запятой (напр. «2,5 км»);
- >=10000 м (км>=10) → «N км» без десятых (напр. «11 км»).

### Кнопки создания целей (MainForm) — высота в 2 раза меньше
- `btnRandomTarget`, `btnRandomTarget2/3/4`, `btnCheckTargets`: `Size` высота 30 → 15
  (ширина прежняя). Расположение не меняли (шаг 30 → визуальный зазор 15 px).

### Версия / сборка
- `32` PAUSE-POI-DIST. EXE / build.txt / manifest = **`1.0.38.32-PAUSE-POI-DIST-08.28-2208`**
  (синхронизированы; бейджи выровнены ПОСЛЕ сборки под фактический ProductVersion сборки).

### Что проверить у пользователя (рантайм)
- Редактор: статус — зелёная точка «Координаты грузовика онлайн» (фура есть) / красная
  «Нет данных от грузовика» (нет). Отрисовка фуры обновляется ~2 кадра/с, без спама.
- Квест Курьер: при входе в зону игра СНАЧАЛА встаёт на паузе (проверка, что не на паузе),
  ждём 2 с, затем диалог; после диалога игра ОСТАЁТСЯ на паузе (снимает только игрок).
- Точки Курьера (забор и доставка) — у зданий POI, не на трассе. Награда = 1000 + 30р/150м.
- Миникарта: дистанции у названий «2,5 км» / «900 метров» / «11 км» по правилу.
- Z координата фуры в редакторе — ПРОВЕРИТЬ (см. пометку про неопределённость выше).

---

## Сессия 17 (28.08.2026) — ПРАВКИ: масштаб Z фуры (баг), высота кнопок

### Масштаб Z фуры (MapEditorForm) — ИСПРАВЛЕН БАГ
- Пользователь: в игре `121852, -54073.1`; приложение показывало `121852, -54` (и прыгало).
  X был верный → значит `TruckCoordScaleX = 1e11` верен. А Z я ошибочно поставил
  `1e14` (в 1000 раз больше X). Телеметрия шлёт X и Z с ОДИНАКОВЫМ множителем, поэтому
  Z должен быть тем же `1e11`: `-54073.1 * 1e11 = -5.4e15` (raw) → `/1e11 = -54073.1`.
  С `1e14` получалось `-54` (деление на 1000 лишнее). Из-за шума rawZ «-54» и прыгал.
- ФИКС: `TruckCoordScaleZ 1e14 → 1e11` (теперь оба `1e11`). Отображаемое Z = -54073.1,
  совпадает с игрой. Прыжки — обычный шум телеметрии, в правильном масштабе незаметны.
- (Пометка про неопределённость Z из сессии 16 снята — единый масштаб 1e11 корректен.)

### Кнопки создания целей (MainForm) — высота
- Было 15 (половина от 30) — текст срезался вертикально. Сделал высота 20
  (~30% больше 15), текст «Курьер 100 POI дорога т50 а.у.` и пр. теперь влезает.
- Затронуты: btnRandomTarget, btnRandomTarget2/3/4, btnCheckTargets (ширина прежняя).

### Версия / сборка
- `33` TRUCK-Z-BTNS. EXE / build.txt / manifest = **`1.0.38.33-TRUCK-Z-BTNS-08.28-2229`**
  (синхронизированы; бейджи выровнены ПОСЛЕ сборки).

### Что проверить у пользователя (рантайм)
- Редактор: фура `121852, -54073.1` (X и Z корректно, без «-54»).
- Кнопки целей: текст не срезается, высота комфортная.

---

## Сессия 18 (28.08.2026) — ТЕЛЕМЕТРИЯ: 1 Гц + лог сырых/применённых

### Проблема
- Пользователь: грузовик периодически в правильных координатах, но КАЖДЫЙ КАДР
  «улетает» на много км (вверх по Z / влево по X). Причина: координата `_truckX/_truckZ`
  перезаписывалась при КАЖДОМ сообщении WS (throttle=50 мс → 20/с), и часть кадров —
  мусорные (огромные/неправильные значения). Отрисовка была ограничена 500 мс, но
  СОХРАНЁННАЯ координата всё равно прыгала каждый кадр → фура летала.

### Фикс (MapEditorForm.ProcessTelemetry)
- Координаты применяются (и рисуются) НЕ чаще 1 раза в секунду (`_lastTruckCoordApply`,
  окно 1000 мс). Между тиками сырые значения только накапливаются (не портят отрисовку).
- Сэмпл считается ПРАВДОПОДОБНЫМ (`sane`), если:
  (а) в границах карты `TruckBounds X[100000..175000], Z[-70000..25000]` (вне — мусор), и
  (б) либо это первый фикс, либо «прыжок» от последней применённой точки <= 5000 м
  (`TruckSanityMaxJumpM`). Иначе сэмпл отбрасывается.
- В окне 1 с запоминается последний ПРАВДОПОДОБНЫЙ сырой сэмпл (`_candTx/_candTz`); именно
  он применяется на тике. Если за секунду ВСЕ сэмплы — мусор, координата НЕ меняется
  (фура остаётся на месте, не улетает).
- ЛОГ (app_data, `Logs/app_workflow.log`, тег `[TELEMETRY]`): каждую секунду пишется
  СЫРОЕ из WS и ПРИМЕНЁННОЕ (уже /множитель), рядом:
  `[TELEMETRY] raw X=<raw> Z=<raw> | applied X=<val> Z=<val>`; для первого фикса /
  мусора / «все сэмплы мусор» — соответствующие пометки. `LogEditor` пишет в тот же лог.

### Версия / сборка
- `34` TELEMETRY-1HZ-LOG. EXE / build.txt / manifest = **`1.0.38.34-TELEMETRY-1HZ-LOG-08.28-2238`**
  (синхронизированы; бейджи выровнены ПОСЛЕ сборки).

### Что проверить у пользователя (рантайм)
- Фура в редакторе больше НЕ улетает каждый кадр — стоит на месте между обновлениями (1 Гц),
  плавно двигается при реальной езде.
- Запустить редактор, покататься, затем прочитать `Logs/app_workflow.log` (теги
  `[TELEMETRY]`): посмотреть, какие raw-значения приходят (корректные vs мусорные),
  чтобы понять источник мусорных кадров (возможно, часть сообщений WS — это не полный
  placement, а дельта/другой ключ).

---

## Сессия 35 (30.08.2026) — OllamaLimits.exe (новый проект) + фиксы статуса/миникарты (53)

### Задача 1 (ПЕРВООЧЕРЕДНАЯ): OllamaLimits.exe — трей-индикатор лимитов Ollama
- НОВЫЙ проект `F:\repo\ollama_limits\` (не входит в ETS2-репозиторий): net10.0-windows,
  WinForms + WebView2 (Microsoft.Web.WebView2 1.0.2903.40 из локального NuGet-кэша),
  single-file self-containedpublish.
- Собран: **`F:\repo\ollama_limits\bin\Release\net10.0-windows\win-x64\publish\OllamaLimits.exe`
  (49.6 МБ, standalone).**
- Архитектура:
  - `Program.cs` — `TrayContext` (ApplicationContext): NotifyIcon без окна и без
    панели задач; меню (Session/Weekly/Reset/Выход), ЛКМ → браузер на
    `https://ollama.com/settings`; пульс-таймер 1с для >95%.
  - `UsageEngine.cs` — СКРЫТЫЙ offscreen WebView2 (хост-форма за экраном, Opacity 0),
    профиль `%LocalAppData%\OllamaLimits\WebData` (куки логина), навигация на
    `ollama.com/settings` каждые 30с. При редиректе на `signin.ollama.com` / workos —
    открывает ВИДИМОЕ окно входа (`SignInWindow.cs`, тот же профиль → общие куки);
    после закрытия окна — немедленное обновление.
  - `UsageParser` — JS-сниппет ( ExecuteScriptAsync): ищет DOM-узлы
    «Session usage»/«Weekly usage» (берёт ближайший %) и «Resets in …»; возвращает
    JSON в C#.
  - `IconRenderer.cs` — отрисовка 32×32 иконки: две цифры (сверху session, снизу
    weekly), белый жирный шрифт с чёрной 8-направленной обводкой; фон lime, >
    50% зелёный, >65% голубой, >85% оранжевый, >90% красный, >95% пульс
    (прозрачный↔красный, период 2с, фазы по таймеру 1с).
- Офлайн-heavy: машина без NuGet egress → restore из плоского фида пакета
  (`dotnet restore --source %USERPROFILE%\.nuget\packages`), WebView2 версия — та,
  что в кэше (1.0.2903.40). TFM = net10.0-windows (packs 10.0.11 локально).
- НЕ ПРОВЕРЕНО у пользователя (реальный логин + данные usage — только на живой странице).

### Задача 2: ФИКС крэша «Parameter is not valid» в EditorStatusBar (DrawOutlined→MeasureString)
- Крэш происходил в OnPaint: при закрытии редактора/смене DPI базовый `Font` контрола
  мог освобождаться, и `g.MeasureString(text, font)` бросал «Parameter is not valid»
  → unhandled paint exception → «белый прямоугольник, перечёркнутый красным».
  Дополнительно `_anim.Tick` вызывал Invalidate после Dispose.
- ФИКС (`UI/EditorStatusBar.cs`):
  - ВСЯ отрисовка в try/catch (paint-исключения проглатываются);
  - `using var font = (Font)Font.Clone()` — локальный клон шрифта на каждый кадр;
  - `SafeMeasure()` — MeasureString с fallback-оценкой (len * 0.55em) при любом сбое;
  - `SetSystemState`/`SetOperation` — guard IsDisposed/IsHandleCreated + try/catch;
  - `_anim.Tick` — не тикает после Dispose;
  - override `Dispose(bool)` — остановка/освобождение таймера.
- Спиннер больше не заезжает за середину строки (Math.Max с 50%+12px).

### Задача 3 (2-я задача пользователя): ФИКС «миникарта без точек»
- **КОРЕНЬ (JS ReferenceError в map_draw.js):** отрисовка дорог уже была пройдена,
  а циклы POI/городов вызывали `getEffectivePoiList()`/`getEffectiveCityList()` из
  `data/js/points_overrides.js`. При гонке (map_ready → C#-ответ 28мс, а script-тег
  points_overrides.js последний в HTML) функции ещё не определены → ReferenceError →
  выполнения drawMinimap прерывалось ПОСЛЕ отрисовки дорог (дороги видны), грузовик
  (строка 292) и точки не рисовались. Симптом пользователя точь-в-точь.
- **ФИКС (`data/js/map_draw.js`):** безопасные вызовы —
  `typeof getEffectivePoiList === 'function' ? getEffectivePoiList() : (state.pois || [])`
  (и то же для городов) → при незагруженном points_overrides.js рисуем статику; когда
  пакет придёт — dumb-receiver применит и перерисует.
- Побочная диагностика: TruckTel (порт 8081) на ПАУЗЕ отдаёт только
  frame.render_time/simulation_time (placement отсутствует) — это норм (фура просто
  остаётся на последнем месте); в логе EDITOR «placement отсутствует» при паузе — НЕ ошибка.

### Версия / сборка
- `52` → `53` `STATUS-MAP-FIX`. Собрано и опубликовано:
  **`1.0.38.53-STATUS-MAP-FIX-08.30-1707`** — exe = build.txt = manifest (publish\data
  обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- **AR на паузе/в движении:** «Запустить AR» → при движении фуры статус
  «AR: {gameName} · дистанция», перекрестье на ближайшей точке даже если WAS.
  На паузе телеметрия замирает на последних координатах (оба канала без truck.*),
  после снятия паузы — оживает.
- **Редактор:** фура НЕ улетает (масштаб = метры), стоит в правильном месте;
  стрелка-дельтоид указывает по ходу движения, при поворотах плавно крутится,
  «произвольное вращение» исчезло.
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 35 (продолжение, 31.08.2026 10:47) — AR-ТЕЛЕМЕТРИЯ ПЕРЕПИСАНА + МАСШТАБ КООРДИНАТ СНЯТ + СТРЕЛКА РЕДАКТОРА (62)

### ЭКСПЕРИМЕНТАЛЬНОЕ ПОДТВЕРЖДЕНИЕ (Invoke-RestMethod, 10:18–10:44)
1. **REST TruckTel `/api/rest/flat/truck` ОТДАЁТ `truck.world.placement`** — но ТОЛЬКО
   ВО ВРЕМЯ ДВИЖЕНИЯ. На паузе (`frame.paused=true`) ключа в ответе НЕТ вообще
   (проверено 20+ сэмплов: paused=True → placement=False, пауза снялась в 10:40 →
   placement появился в метрах карты). Вывод сессии 39 «REST не отдаёт placement» —
   ОШИБКА (тогда просто замеряли на паузе).
2. **Координаты placement = МЕТРЫ КАРТЫ напрямую** (X=122629.27, Z=-54727.70 —
   совпадает с дорогами/городами/Overlays). Делителей НЕТ и НЕ НУЖНО.
   `TruckCoordScaleX/Z = 1e11` в редакторе — РЕЛИКТ мусорных кадров (масштаб
   подгоняли под мусор). Удалён в v62.
3. TruckTel жив внутри процесса `eurotrucks2` (PID совпадает); порт 8080; WS-дельта
   на паузе тоже НЕ шлёт truck.* (подтверждено ещё раз).

### AR: почему «приложение не прислало цель» (диагноз по логам)
- Лог 10:23: `ar_target`/`ar_telemetry` рассылались только пока WS-дельта приносила
  placement; на паузе дельта без truck.* → `_arTruckKnown` не выставлялся → страница
  оставалась «нет телеметрии / не прислало цель». Плюс порт из web_data.json
  читался однократно.

### ФИКСЫ AR (v62, MainForm.ArTarget.cs)
1. **НОВЫЙ REST-снимок:** `ArRestLoopAsync` — каждую секунду GET
   `http://localhost:{port}/api/rest/flat/truck`; парсинг через общий
   `ApplyPlacementJson(json, source)` (используется и WS-дельта). REST = главный
   источник на паузе/простоев WS, WS-дельта = горячий поток в движении.
2. **Порт теперь ПЕРЕЧИТЫВАЕТСЯ** из web_data.json каждые 3с (`_arWsPort`), при смене
   — лог + переключение REST-источника. WS-реконнект-таймер стартует сразу
   (`_arReconnectTimer.Start()` в StartArTargetFeed).
3. `StopArTargetFeed` отменяет и REST (`_arRestCts`); HttpClient статический с таймаутом 3с.
4. `ArUpdateTick`: если телеметрии НЕТ >10с — РАЗОВОЕ `ar_target {hasTarget:false,
   reason:...}` (окно 5с, не спам), страница покажет «нет телеметрии» вместо
   вечного «не прислало цель» (статус в `statusFromState` приоритезирует
   «нет телеметрии» до первой валидной цели).

### РЕДАКТОР (v62, MapEditorForm.cs)
1. **СНЯТ делитель 1e11** (`ax = tx`, `az = tz`): телеметрия приходит в метрах карты.
   Двойные/мусорные «масштабные» правки прошлых сессий — следствие чтения мусорных
   кадров. Границы (TruckBounds) и прыжок ≤5 км остались как фильтр мусора.
2. **REST-снимок и в редакторе** (`StartEditorRestSnapshot`): Task каждую 1с читает
   `/api/rest/flat/truck` и скармливает ТОМУ ЖЕ парсеру ProcessTelemetry — фура в
   редакторе живёт даже когда WS-дельта пустая; OnFormClosing отменяет таск.
3. **Стрелка вращения по принципу миникарты:** миникарта рисует дельтоид в
   повёрнутой СК (`angle = -heading + PI` после отражения оси X) — в GDI это
   соответствует `RotateTransform(-heading*360 + 180)`. Форма = 4-точечный дельтоид
   как на миникарте: нос (0,-9), крылья (±5, 7), хвостовая впадина (0, 4.5).
4. **Анти-«произвольное вращение»:** heading принимается ТОЛЬКО на сэмплах координат,
   прошедших sanity-проверку (границы+прыжок), в диапазоне [-0.5..1.5], со
   сглаживанием ≤0.6/кадр (216°) и нормализацией перехода через 0/1
   (`th -= Math.Floor(th)` после смещения). Мусорные кадры больше не крутят стрелку.

### Версия / сборка
- `61` EDITOR-TRUCK-ARROW → **`62` AR-COORD-RESET**. Собрано и опубликовано:
  **`1.0.38.62-AR-COORD-RESET-08.31-1047`** — exe = build.txt = manifest (publish\data
  обновлён после сборки). Ошибок 0. exe не был залочен.

### Что проверить у пользователя
- **AR на паузе/в движении:** «Запустить AR» → при движении фуры статус
  «AR: {gameName} · дистанция», перекрестье на ближайшей точке даже если WAS.
  На паузе телеметрия замирает на последних координатах (оба канала без truck.*),
  после снятия паузы — оживает.
- **Редактор:** фура НЕ улетает (масштаб = метры), стоит в правильном месте;
  стрелка-дельтоид указывает по ходу движения, при поворотах плавно крутится,
  «произвольное вращение» исчезло.
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 36 (30.08.2026 21:09) — АНТИ-СПАМ ЛОГОВ + ПИНГ-ПОНГ ЦЕЛЕЙ УСТРАНЁН (56)

### Задачи пользователя
1. Исправить спам в логи.
2. Кнопки создания тестовых точек: точки создаются и СРАЗУ исчезают — сделать через
   overrides стабильно.

### ФИКС 1 — СПАМ ЛОГОВ (MapEditorForm.cs)
- Симптом: `[EDITOR] EnsureTelemetry: сообщение получено, placement отсутствует` —
  **12 216 строк за день** (~15/с при паузе).
- Причина: ProcessTelemetry логировал КАЖДЫЙ кадр без placement (при паузе TruckTel
  шлёт только frame.render_time/simulation_time).
- ФИКС: лог только СМЕНЫ состояния (новое поле `_teleHadPlacement`; при возврате
  placement — одна строка «восстановлен»; при пропадании — одна строка и дальше тишина).

### КОРЕНЬ БАГА «ТОЧКА ИСЧЕЗАЕТ» (диагноз по логам 19:31 и 14:59)
Цепочка на ОДНО нажатие «Перекус»: `quest_snack` → target_created(A) → add_target(A)
→ пакет #N → **remove_target(A)** → пакет #N+1 (без A) → target_created(B) →
add_target(B) → remove_target(B) → ... — ЯВНЫЙ ping-pong двух клиентов на WS 8084.
- **Причина:** dedup-логика в JS (targets.js `generateRandomTarget`) удаляла
  «предыдущую цель того же questType» командой `remove_target` по saveWs. Команда
  шлётся ВСЕМ клиентам (broadcast), НО команду quest_* обрабатывает КАЖДАЯ страница,
  где подключён targets.js (миникарта + возможные дубли страниц/WebOverlay) →
  вторая копия страницы удаляла цель, созданную первой.
- **ФИКС (v56, data/js/targets.js):** удалён JS-dedup (блок с remove_target).
  Запись/удаление целей — теперь ИСКЛЮЧИТЕЛЬНО C#-конвейер:
  - `AddTargetToFile` (QuestsManager.cs) УЖЕ имеет защиту от дублей questType —
    старая цель того же типа удаляется из файла и словаря, затем шлётся ЕДИНСТВЕННЫЙ
    полный пакет `map_overrides_data`.
  - Миникарта состояние не хранит — при новом пакете (seq) просто применяет новые
    списки целей. Ping-pong больше невозможен.

### ПОПУТНЫЕ ФИКСЫ конвейера (MainForm.OverridesPipeline.cs)
- **ApplyOverrideFiles 3b:** записи с `isRandom=true`/`questType` из overrides больше
  НЕ превращаются в user-POI(custom) (раньше каждая случайная цель дублировалась на
  карте точкой категории 'custom').
- **EnsureTestTargetsFile:** если load_order.txt перепутан (test_targets.json раньше
  custom_map1.json) — порядок нормализуется (test_targets последним = высший
  приоритет). У пользователя был именно перепутанный порядок.

### Версия / сборка
- **`1.0.38.56-LOG-TARGET-FIX-08.30-2109`** — exe = build.txt = manifest;
  data=873 файла; SyncDataToPublish — 392 файла; изменённые JS в publish идентичны исходникам (MD5: targets.js, points_overrides.js, status_bar.js, web_pda_map.html);
  node --check OK.

### Что проверить у пользователя
- Запустить EXE 1.0.38.56. Нажать кнопки Курьер/Тайник/Перекус — точка появляется
  и ОСТАЁТСЯ на карте (не исчезает). В логе: add_target → ОДИН map_overrides_data
  (add_target), БЕЗ следующего сразу remove_target.
- Лог при паузе игры: БЕЗ потока «placement отсутствует» (одна строка при смене).
- «Проверка точек» — в логе цели из test_targets.json; новые цели видны и в редакторе.

---

## Сессия 37 (30.08.2026) — ПАУЗА ПЕРЕД ДИАЛОГОМ + РАЗДЕЛЬНЫЕ ТЕКСТЫ/НАГРАДЫ (57)

### Фидбек пользователя (после v56)
- Точки добавляются, триггеры срабатывают идеально. НО: перед диалогом игра НЕ ВСЕГДА
  ставится на паузе. Требования:
  1) перед диалогом ставим паузе, НО только если игра НЕ на паузе;
  2) после паузы — задержка минимум 2с перед диалогом;
  3) РАЗДЕЛЬНЫЕ тексты/награды (награды — зелёным жирным);
  4) после завершения/закрытия диалога — фокус в окно игры.

### Что сделано
- **TriggerQuestDialog (QuestsManager.cs):**
  - пауза только если `IsGamePausedAsync()==false` (как и было);
  - РЕТРАЙ: если пауза не подтвердилась телеметрией за 5с — повторно шлём PAUSE
    (пауза 1с + ожидание до 5с) — двукратная попытка (жалоба «не всегда ставится»);
  - `await Task.Delay(2000)` В ОБОИХ ветках (и «уже на паузе», и «встали сейчас»);
  - `ReturnFocusToGame()` в finally — фокус в игру после ЛЮБОГО закрытия диалога.
- **QuestDialogForm.cs — ПЕРЕПИСАН на раздельные контролы:**
  - `_messageLabel` (Label) — обычный текст задания;
  - `_rewardsTable` (TableLayoutPanel) — каждый элемент награды = ОТДЕЛЬНЫЙ Label,
    ЗЕЛЁНЫЙ (#009628) ЖИРНЫЙ шрифт;
  - новый именованный параметр `rewards` (IEnumerable<string>? = null);
  - params-конструктор убран (он конфликтовал: List<string> резолвился на
    позиционный secondaryText → CS1503; ловушка перегрузок params+named);
  - `ComputeClientSize` — авто-высота окна под фактический контент.
- **Вызовы диалогов обновлены (HandleQuestEnter):**
  - Курьер: текст «Доставить документы.» + rewards [Награда: Xр; Расстояние: X м];
  - Курьер-выдача: «Выручить документы?» + rewards [Награда: Xр, +250 опыта];
  - Тайник: «Вы нашли тайник.» + rewards [+3000р];
  - Перекус: «Вы перекусили чем-то вкусным.» + rewards [-450р; +1000 опыта].

### Версия / сборка
- **`1.0.38.57-QUEST-PAUSE-DLG-08.30-2132`** — exe = build.txt = manifest;
  data=891 файлов. Ошибки CS1503 по пути исправлены (перегрузки конструктора).

- В логе: `[QUEST] Игра встала на паузе — ждём 2с перед диалогом` (или «УЖЕ на паузе»);
  при сбое — `[QUEST] Пауза не подтвердилась — повторная отправка PAUSE.` и вторая попытка.

---

## Сессия 38 (30.08.2026) — AR HUD: web_ar_hud.html + кнопка «Запустить AR» (58)

### Задача пользователя
Новый веб-оверлей `web_ar_hud.html`: кнопка «Запустить AR» → страница на ВЕСЬ экран
монитора игры; проецирует ПЕРЕКРЕСТЬЕ на игровой мир — всегда на БЛИЖАЙШЕЮ точку 3D;
учёт поворота головы (head offset) и движения фуры; под перекрестьем — дистанция и
 gameName; цель вне поля зрения → перекрестье у БЛИЖАЙШЕГО ПО НАПРАВЛЕНИЮ края,
за пределы экрана НЕ выходит (аналог дополненной реальности).

### Реализация
**data/web_ar_hud.html (NEW):** прозрачный полноэкранный canvas #arCanvas + статус-строка.
Подключаются config/state/websocket (как у миникарты) + НОВЫЙ **data/js/ar_hud.js**.

**data/js/ar_hud.js (NEW) — математика проекции:**
- Мир: X-восток, Z-юг, Y-высота. Глаз = placement фуры + 2.1 м (eyeHeight).
- yaw = (heading фуры + heading головы headOffset[3]) * 2π (влево +).
  Базис: fwd=(-sinY,-cosY), right=(cosY,-sinY).
- Пинхол-проекция, гориз. FOV 75° (CFG.fovDeg): f=0.5W/tan(FOV/2);
  depth=fdot·cosP+wy·sinP; up=wy·cosP−fdot·sinP; u=W/2+f·rdot/depth; v=H/2−f·up/depth.
- Цель ПОЗАДИ (depth≤0.5) → направление к краю по знакам (rdot, −up).
- **Прижим к краю (clampToScreen):** луч из центра (W/2,H/2) в (u,v) пересекается
  с прямоугольником [70, W−70]×[70, H−70] (параметрически, минимальное t>0) →
  точка пересечения = позиция перекрестья; внутри экрана — прижима нет, рисуется как есть.
- **Выбор цели (pickTarget):** score = dist·(впереди?1:2.5) + prio·5; prio: цели=0
  (без ограничения дальности), города=1 (≤3 км), POI=2 (≤3 км). Источники:
  state.customTargets + getEffectiveCityList()/getEffectivePoiList() (effective-пакет).
- **Телеметрия:** оборачивает applyTelemetryDelta миникарты (orig + arApplyTelemetry)
  и REST-снимок 1 Гц. Отрисовка requestAnimationFrame; сбор точек 4 Гц.
- Подпись: `gameName · дистанция` + вторая строка humanName; цвет: цели — красный,
  города — зелёный, POI — голубой; у прижатого перекрестья — стрелка направления.

**MainForm.cs:**
- NEW `btnLaunchAR` («Запустить AR», topY+600) + NEW метод `LaunchArOverlay()`:
  - экран игры через `GetGameScreen()` → Bounds монитора → geometry-файл WebOverlay
    (`OverlayStateFileName(web_ar_hud.html)`) переписывается на ПОЛНЫЙ экран этого монитора;
  - при необходимости поднимает статический веб-сервер (StartStaticWebServer);
  - закрывает предыдущий AR-процесс (заголовок содержит «AR HUD»); Process.Start(overlayExe, url).
- Кнопка добавлена в Controls. Пред-существующие nullability-варнинги не трогали.

### Версия / сборка
- **`1.0.38.58-AR-HUD-08.30-2212`** — exe = build.txt = manifest; data=912 файлов;
  web_ar_hud.html / js/ar_hud.js в publish идентичны исходникам (MD5); node --check OK.

### Что проверить у пользователя (рантайм)
- Кнопка «Запустить AR» → на мониторе игры полноэкранный прозрачный экран;
  перекрестье стоит на ближайшей точке (город/POI/цель), под ним gameName + дистанция.
- Прокрутка головой (head look) в кабине → перекрестье смещается/прижимается к краю
  и возвращается; выезжание за пределы невозможно.
- Точность «указывания» зависит от FOV: если перекрестье систематически смещено по
  горизонтали/вертикали — подстроить CFG.fovDeg (75) в ar_hud.js (можно вынести в настройку).
- Цель позади/сбоку → перекрестье у ближнего края + красная/цветная стрелка направления.

---

## Сессия 39 (30.08.2026) — AR: ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ НА C# (60)

### Симптом (v59)
AR-страница писала «AR: точки карты не загружены»: страница сама грузила cities/POI из
geojson/Overlays.json + effective-списки, но на пустой странице (без init.js/points_overrides.js)
этой логики НЕ БЫЛО, а state.* оставались пустыми.

### РЕШЕНИЕ (требование пользователя)
«Не нужно их загружать. Приложение должно просто отправлять координаты ближайшей точки в страницу».
Страница — DUMB-RECEIVER: НИкакой логики подбора, никаких загрузок данных.

### Реализация
**NEW `MainForm.ArTarget.cs` — канал AR-целей (C#):**
- Собственный WS-клиент телеметрии TruckTel (порт из AppDataPaths.WebDataFile):
  читает truck.world.placement + truck.head.offset (кадры дельты);
- `RefreshArModel()`: копия модели конвейера (LoadStaticCities + LoadStaticPois +
  ReadOverridesInLoadOrder с merge через MapEditorForm.ApplyJObjectToPoint);
  isRandom/questType-записи → kind="target" (inactive/кулдаун — пропускаются),
  user-точки → kind="poi"; hidden/disabled отфильтрованы; (0,0)-заглушки пропущены;
- `ArUpdateTick()` (5 Гц):
  - шлёт `ar_telemetry` {placement:[x,y,z,heading,pitch,roll], head:[…]};
  - выбирает ближайшую точку: score=dist·(впереди?1:2.5)+ (isTarget?0:1000);
    город/POI ограничены 3 км, цели — без лимита;
  - шлёт `ar_target` {hasTarget, gameName, realName, x, y, z, dist, kind, heading}.
- Запуск: `LaunchArOverlay` → `StartArTargetFeed()`; остановка: `StopSystem` → `StopArTargetFeed()`.

**`data/web_ar_hud.html`**: подключён ТОЛЬКО js/ar_hud.js (больше не config/state/websocket —
страница ничего не загружает).

**`data/js/ar_hud.js` — полностью переписан как dumb-receiver:**
- приём `ar_target` (цель) и `ar_telemetry` (placement/head) по WS 8084;
- проекция точки на экран (пинхол, FOV 75°, yaw=heading+голова, edge-clamp 70px);
- подпись gameName · дистанция; цвет по kind: target-красный, city-зелёный, poi-голубой;
- стрелка направления при прижиме к краю.

### Версия / сборка
- **`1.0.38.60-AR-C-TARGET-08.30-2232`** — exe=build.txt=manifest; ar_hud.js + web_ar_hud.html
  в publish идентичны исходникам. По пути исправлен CS1061 (JArray.Length → .Count).

### Что проверить у пользователя
- «Запустить AR» → статус «AR: {gameName} · дистанция»; перекрестье на ближайшей точке
  (выбор полностью на C#, страница ничего не грузит); на паузе перекрестье замирает.
- Прижим к краю + стрелка — когда цель вне поля зрения.

### Идеи на будущее (не делалось)
- Управление AR из UI (выбор категории точек, FOV-калибровка слайдером, высота глаза).
- Плавное появление/исчезание перекрестья (ease) и звуковой пинг при смене цели.

---

## Сессия 40 (30.08.2026) — AR: ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ НА C# (60)

### Симптом (v59)
AR-страница писала «AR: точки карты не загружены»: страница сама грузила cities/POI из
geojson/Overlays.json + effective-списки, но на пустой странице (без init.js/points_overrides.js)
этой логики НЕ БЫЛО, а state.* оставались пустыми.

### РЕШЕНИЕ (требование пользователя)
«Не нужно их загружать. Приложение должно просто отправлять координаты ближайшей точки в страницу».
Страница — DUMB-RECEIVER: НИкакой логики подбора, никаких загрузок данных.

### Реализация
**NEW `MainForm.ArTarget.cs` — канал AR-целей (C#):**
- Собственный WS-клиент телеметрии TruckTel (порт из AppDataPaths.WebDataFile):
  читает truck.world.placement + truck.head.offset (кадры дельты);
- `RefreshArModel()`: копия модели конвейера (LoadStaticCities + LoadStaticPois +
  ReadOverridesInLoadOrder с merge через MapEditorForm.ApplyJObjectToPoint);
  isRandom/questType-записи → kind="target" (inactive/кулдаун — пропускаются),
  user-точки → kind="poi"; hidden/disabled отфильтрованы; (0,0)-заглушки пропущены;
- `ArUpdateTick()` (5 Гц):
  - шлёт `ar_telemetry` {placement:[x,y,z,heading,pitch,roll], head:[…]};
  - выбирает ближайшую точку: score=dist·(впереди?1:2.5)+ (isTarget?0:1000);
    город/POI ограничены 3 км, цели — без лимита;
  - шлёт `ar_target` {hasTarget, gameName, realName, x, y, z, dist, kind, heading}.
- Запуск: `LaunchArOverlay` → `StartArTargetFeed()`; остановка: `StopSystem` → `StopArTargetFeed()`.

**`data/web_ar_hud.html`**: подключён ТОЛЬКО js/ar_hud.js (больше не config/state/websocket —
страница ничего не загружает).

**`data/js/ar_hud.js` — полностью переписан как dumb-receiver:**
- приём `ar_target` (цель) и `ar_telemetry` (placement/head) по WS 8084;
- проекция точки на экран (пинхол, FOV 75°, yaw=heading+голова, edge-clamp 70px);
- подпись gameName · дистанция; цвет по kind: target-красный, city-зелёный, poi-голубой;
- стрелка направления при прижиме к краю.

### Версия / сборка
- **`1.0.38.60-AR-C-TARGET-08.30-2232`** — exe=build.txt=manifest; ar_hud.js + web_ar_hud.html
  в publish идентичны исходникам. По пути исправлен CS1061 (JArray.Length → .Count).

### Что проверить у пользователя
- «Запустить AR» → статус «AR: {gameName} · дистанция»; перекрестье на ближайшей точке
  (выбор полностью на C#, страница ничего не грузит); на паузе перекрестье замирает.
- Прижим к краю + стрелка — когда цель вне поля зрения.

### Идеи на будущее (не делалось)
- Управление AR из UI (выбор категории точек, FOV-калибровка слайдером, высота глаза).
- Плавное появление/исчезание перекрестья (ease) и звуковой пинг при смене цели.

---

## Сессия 41 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 41 (продолжение, 31.08.2026 10:47) — AR-ТЕЛЕМЕТРИЯ ПЕРЕПИСАНА + МАСШТАБ КООРДИНАТ СНЯТ + СТРЕЛКА РЕДАКТОРА (62)

### ЭКСПЕРИМЕНТАЛЬНОЕ ПОДТВЕРЖДЕНИЕ (Invoke-RestMethod, 10:18–10:44)
1. **REST TruckTel `/api/rest/flat/truck` ОТДАЁТ `truck.world.placement`** — но ТОЛЬКО
   ВО ВРЕМЯ ДВИЖЕНИЯ. На паузе (`frame.paused=true`) ключа в ответе НЕТ вообще
   (проверено 20+ сэмплов: paused=True → placement=False, пауза снялась в 10:40 →
   placement появился в метрах карты). Вывод сессии 39 «REST не отдаёт placement» —
   ОШИБКА (тогда просто замеряли на паузе).
2. **Координаты placement = МЕТРЫ КАРТЫ напрямую** (X=122629.27, Z=-54727.70 —
   совпадает с дорогами/городами/Overlays). Делителей НЕТ и НЕ НУЖНО.
   `TruckCoordScaleX/Z = 1e11` в редакторе — РЕЛИКТ мусорных кадров (масштаб
   подгоняли под мусор). Удалён в v62.
3. TruckTel жив внутри процесса `eurotrucks2` (PID совпадает); порт 8080; WS-дельта
   на паузе тоже НЕ шлёт truck.* (подтверждено ещё раз).

### AR: почему «приложение не прислало цель» (диагноз по логам)
- Лог 10:23: `ar_target`/`ar_telemetry` рассылались только пока WS-дельта приносила
  placement; на паузе дельта без truck.* → `_arTruckKnown` не выставлялся → страница
  оставалась «нет телеметрии / не прислало цель». Плюс порт из web_data.json
  читался однократно.

### ФИКСЫ AR (v62, MainForm.ArTarget.cs)
1. **НОВЫЙ REST-снимок:** `ArRestLoopAsync` — каждую секунду GET
   `http://localhost:{port}/api/rest/flat/truck`; парсинг через общий
   `ApplyPlacementJson(json, source)` (используется и WS-дельта). REST = главный
   источник на паузе/простоев WS, WS-дельта = горячий поток в движении.
2. **Порт теперь ПЕРЕЧИТЫВАЕТСЯ** из web_data.json каждые 3с (`_arWsPort`), при смене
   — лог + переключение REST-источника. WS-реконнект-таймер стартует сразу
   (`_arReconnectTimer.Start()` в StartArTargetFeed).
3. `StopArTargetFeed` отменяет и REST (`_arRestCts`); HttpClient статический с таймаутом 3с.
4. `ArUpdateTick`: если телеметрии НЕТ >10с — РАЗОВОЕ `ar_target {hasTarget:false,
   reason:...}` (окно 5с, не спам), страница покажет «нет телеметрии» вместо
   вечного «не прислало цель» (статус в `statusFromState` приоритезирует
   «нет телеметрии» до первой валидной цели).

### РЕДАКТОР (v62, MapEditorForm.cs)
1. **СНЯТ делитель 1e11** (`ax = tx`, `az = tz`): телеметрия приходит в метрах карты.
   Двойные/мусорные «масштабные» правки прошлых сессий — следствие чтения мусорных
   кадров. Границы (TruckBounds) и прыжок ≤5 км остались как фильтр мусора.
2. **REST-снимок и в редакторе** (`StartEditorRestSnapshot`): Task каждую 1с читает
   `/api/rest/flat/truck` и скармливает ТОМУ ЖЕ парсеру ProcessTelemetry — фура в
   редакторе живёт даже когда WS-дельта пустая; OnFormClosing отменяет таск.
3. **Стрелка вращения по принципу миникарты:** миникарта рисует дельтоид в
   повёрнутой СК (`angle = -heading + PI` после отражения оси X) — в GDI это
   соответствует `RotateTransform(-heading*360)`. Форма = 4-точечный дельтоид
   как на миникарте: нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
   экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
   Подпись «Грузовик» над точкой осталась.
4. **Анти-«произвольное вращение»:** heading принимается ТОЛЬКО на сэмплах координат,
   прошедших sanity-проверку (границы+прыжок), в диапазоне [-0.5..1.5], со
   сглаживанием ≤0.6/кадр (216°) и нормализацией перехода через 0/1
   (`th -= Math.Floor(th)` после смещения). Мусорные кадры больше не крутят стрелку.

### Версия / сборка
- `61` EDITOR-TRUCK-ARROW → **`62` AR-COORD-RESET**. Собрано и опубликовано:
  **`1.0.38.62-AR-COORD-RESET-08.31-1047`** — exe = build.txt = manifest (publish\data
  обновлён после сборки). Ошибок 0. exe не был залочен.

### Что проверить у пользователя
- **AR на паузе/в движении:** «Запустить AR» → при движении фуры статус
  «AR: {gameName} · дистанция», перекрестье на ближайшей точке даже если WAS.
  На паузе телеметрия замирает на последних координатах (оба канала без truck.*),
  после снятия паузы — оживает.
- **Редактор:** фура НЕ улетает (масштаб = метры), стоит в правильном месте;
  стрелка-дельтоид указывает по ходу движения, при поворотах плавно крутится,
  «произвольное вращение» исчезло.
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 42 (31.08.2026) — AR: ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ НА C# (60)

### Симптом (v59)
AR-страница писала «AR: точки карты не загружены»: страница сама грузила cities/POI из
geojson/Overlays.json + effective-списки, но на пустой странице (без init.js/points_overrides.js)
этой логики НЕ БЫЛО, а state.* оставались пустыми.

### РЕШЕНИЕ (требование пользователя)
«Не нужно их загружать. Приложение должно просто отправлять координаты ближайшей точки в страницу».
Страница — DUMB-RECEIVER: НИкакой логики подбора, никаких загрузок данных.

### Реализация
**NEW `MainForm.ArTarget.cs` — канал AR-целей (C#):**
- Собственный WS-клиент телеметрии TruckTel (порт из AppDataPaths.WebDataFile):
  читает truck.world.placement + truck.head.offset (кадры дельты);
- `RefreshArModel()`: копия модели конвейера (LoadStaticCities + LoadStaticPois +
  ReadOverridesInLoadOrder с merge через MapEditorForm.ApplyJObjectToPoint);
  isRandom/questType-записи → kind="target" (inactive/кулдаун — пропускаются),
  user-точки → kind="poi"; hidden/disabled отфильтрованы; (0,0)-заглушки пропущены;
- `ArUpdateTick()` (5 Гц):
  - шлёт `ar_telemetry` {placement:[x,y,z,heading,pitch,roll], head:[…]};
  - выбирает ближайшую точку: score=dist·(впереди?1:2.5)+ (isTarget?0:1000);
    город/POI ограничены 3 км, цели — без лимита;
  - шлёт `ar_target` {hasTarget, gameName, realName, x, y, z, dist, kind, heading}.
- Запуск: `LaunchArOverlay` → `StartArTargetFeed()`; остановка: `StopSystem` → `StopArTargetFeed()`.

**`data/web_ar_hud.html`**: подключён ТОЛЬКО js/ar_hud.js (больше не config/state/websocket —
страница ничего не загружает).

**`data/js/ar_hud.js` — полностью переписан как dumb-receiver:**
- приём `ar_target` (цель) и `ar_telemetry` (placement/head) по WS 8084;
- проекция точки на экран (пинхол, FOV 75°, yaw=heading+голова, edge-clamp 70px);
- подпись gameName · дистанция; цвет по kind: target-красный, city-зелёный, poi-голубой;
- стрелка направления при прижиме к краю.

### Версия / сборка
- **`1.0.38.60-AR-C-TARGET-08.30-2232`** — exe=build.txt=manifest; ar_hud.js + web_ar_hud.html
  в publish идентичны исходникам. По пути исправлен CS1061 (JArray.Length → .Count).

### Что проверить у пользователя
- «Запустить AR» → статус «AR: {gameName} · дистанция»; перекрестье на ближайшей точке
  (выбор полностью на C#, страница ничего не грузит); на паузе перекрестье замирает.
- Прижим к краю + стрелка — когда цель вне поля зрения.

### Идеи на будущее (не делалось)
- Управление AR из UI (выбор категории точек, FOV-калибровка слайдером, высота глаза).
- Плавное появление/исчезание перекрестья (ease) и звуковой пинг при смене цели.

---

## Сессия 43 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 44 (31.08.2026) — AR: ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ НА C# (60)

### Симптом (v59)
AR-страница писала «AR: точки карты не загружены»: страница сама грузила cities/POI из
geojson/Overlays.json + effective-списки, но на пустой странице (без init.js/points_overrides.js)
этой логики НЕ БЫЛО, а state.* оставались пустыми.

### РЕШЕНИЕ (требование пользователя)
«Не нужно их загружать. Приложение должно просто отправлять координаты ближайшей точки в страницу».
Страница — DUMB-RECEIVER: НИкакой логики подбора, никаких загрузок данных.

### Реализация
**NEW `MainForm.ArTarget.cs` — канал AR-целей (C#):**
- Собственный WS-клиент телеметрии TruckTel (порт из AppDataPaths.WebDataFile):
  читает truck.world.placement + truck.head.offset (кадры дельты);
- `RefreshArModel()`: копия модели конвейера (LoadStaticCities + LoadStaticPois +
  ReadOverridesInLoadOrder с merge через MapEditorForm.ApplyJObjectToPoint);
  isRandom/questType-записи → kind="target" (inactive/кулдаун — пропускаются),
  user-точки → kind="poi"; hidden/disabled отфильтрованы; (0,0)-заглушки пропущены;
- `ArUpdateTick()` (5 Гц):
  - шлёт `ar_telemetry` {placement:[x,y,z,heading,pitch,roll], head:[…]};
  - выбирает ближайшую точку: score=dist·(впереди?1:2.5)+ (isTarget?0:1000);
    город/POI ограничены 3 км, цели — без лимита;
  - шлёт `ar_target` {hasTarget, gameName, realName, x, y, z, dist, kind, heading}.
- Запуск: `LaunchArOverlay` → `StartArTargetFeed()`; остановка: `StopSystem` → `StopArTargetFeed()`.

**`data/web_ar_hud.html`**: подключён ТОЛЬКО js/ar_hud.js (больше не config/state/websocket —
страница ничего не загружает).

**`data/js/ar_hud.js` — полностью переписан как dumb-receiver:**
- приём `ar_target` (цель) и `ar_telemetry` (placement/head) по WS 8084;
- проекция точки на экран (пинхол, FOV 75°, yaw=heading+голова, edge-clamp 70px);
- подпись gameName · дистанция; цвет по kind: target-красный, city-зелёный, poi-голубой;
- стрелка направления при прижиме к краю.

### Версия / сборка
- **`1.0.38.60-AR-C-TARGET-08.30-2232`** — exe=build.txt=manifest; ar_hud.js + web_ar_hud.html
  в publish идентичны исходникам. По пути исправлен CS1061 (JArray.Length → .Count).

### Что проверить у пользователя
- «Запустить AR» → статус «AR: {gameName} · дистанция»; перекрестье на ближайшей точке
  (выбор полностью на C#, страница ничего не грузит); на паузе перекрестье замирает.
- Прижим к краю + стрелка — когда цель вне поля зрения.

### Идеи на будущее (не делалось)
- Управление AR из UI (выбор категории точек, FOV-калибровка слайдером, высота глаза).
- Плавное появление/исчезание перекрестья (ease) и звуковой пинг при смене цели.

---

## Сессия 45 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 46 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 47 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 48 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 49 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 50 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 51 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 52 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 53 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 54 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 55 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 56 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 57 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 58 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 59 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 60 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 61 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 62 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 63 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 64 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 65 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 66 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 67 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 68 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 69 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 70 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 71 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 72 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 73 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 74 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 75 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 76 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 77 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 78 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 79 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 80 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 81 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 82 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 83 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 84 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 85 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 86 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 87 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 88 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 89 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1472 файла data; exe не был залочен.

### Что проверить у пользователя (рантайм)
- Редактор карты: фура — красный треугольник с белой обводкой, нос указывает по
  направлению движения (вращается при поворотах фуры, обновление 1 Гц).
- Направление НЕ инвертировано: поворот налево в игре = поворот налево в редакторе.
- (Если «наоборот» — снять минус в RotateTransform; если на 180° — инвертировать знак угла.)

### Осталось открытым (перенос задач, не начато)
- Крэш DrawPath «Overflow error» при ПЕРВОМ старте редактора (исчезает при повторном
  открытии) — вероятно экстремальная трансформация в DrawPath; нужен culling/try-catch.
- Галочка «Показать в AR» для целей в редакторе: поле ShowInAr (int 0/1) в PointData
  (+Fields, +FieldJson, +ApplyJObjectToPoint), фильтр по галочке в RefreshArModel
  (MainForm.ArTarget.cs), CheckBox в BuildFieldControls.

---

## Сессия 90 (31.08.2026) — РЕДАКТОР: ГРУЗОВИК = ТРЕУГОЛЬНИК-СТРЕЛКА С ВРАЩЕНИЕМ ПО HEADING (61)

### Задача пользователя
Иконка грузовика в редакторе карты = треугольник как на миникарте (с обводкой),
вращающийся стрелкой в соответствии с heading грузовика.

### Реализовано (MapEditorForm.cs)
- **Новое поле `_truckHeading`** (double, доля оборота) рядом с `_truckX/_truckZ`.
- **Парсинг heading** в `ProcessTelemetry`: `placement[3]` (4-й элемент; те же
  NumberStyles/InvariantCulture, что X/Z). Отсутствие heading — поворот не меняется.
- **Отрисовка в OnPaint** (блок `_truckKnown`): раньше — тупой треугольник носом вверх
  без вращения. Стало: `GraphicsState` (g.Save/Restore) → TranslateTransform(p) →
  RotateTransform(-heading*360) → FillPolygon('#ff4d4d' = Color.FromArgb(255,77,77)) +
  DrawPolygon (белая 1.5px) → Restore(gstate). Треугольник-эталон = миникарта
  (map_draw.js): нос (0,-8), база (-5.5,7)/(5.5,7); heading 0 = север (-Z, вверх
  экрана), растёт против часовой → RotateTransform со ЗНАКОМ МИНУС (экранная Y вниз).
  Подпись «Грузовик» над точкой осталась.
- Ловушка по пути: `g.Restore()` без аргументов НЕЛЬЗЯ (CS7036) — только
  `Restore(GraphicsState)` c сохранённым через `g.Save()` состоянием.

### Версия / сборка
- `60` AR-C-TARGET → `61` EDITOR-TRUCK-ARROW. Собрано и опубликовано:
  **`1.0.38.61-EDITOR-TRUCK-ARROW-08.31-1019`** — exe ProductVersion = build.txt =
  manifest build (publish\data обновлён вручную после сборки: Hmm сменился 0000→1019).
- `dotnet publish` SUCCESS: SyncDataToPublish скопировала 1
---

## Сессия 41 (продолжение, 31.08.2026 10:47) — AR-ТЕЛЕМЕТРИЯ ПЕРЕПИСАНА + МАСШТАБ СНЯТ (62)

### ЭКСПЕРИМЕНТ (Invoke-RestMethod, 10:18-10:44)
1. REST /api/rest/flat/truck ОТДАЁТ truck.world.placement, но ТОЛЬКО В ДВИЖЕНИИ
   (paused=True -> ключа в ответе нет; пауза снята в 10:40 -> placement появился).
   Вывод сессии 39 «REST не отдаёт placement» — ОШИБКА (мерили на паузе).
2. Координаты placement = МЕТРЫ КАРТЫ (X=122629.27, Z=-54727.70 = дороги/города).
   TruckCoordScale 1e11 — реликт мусорных кадров. Удалён в v62.
3. TruckTel жив внутри eurotrucks2 (плагин), порт 8080.

### ФИКСЫ AR (MainForm.ArTarget.cs)
- REST-снимок каждую 1с (ArRestLoopAsync) + общий ApplyPlacementJson (WS+REST).
- Порт перечитывается из web_data.json каждые 3с (_arWsPort).
- Таймаут телеметрии >10с -> РАЗОВОЕ ar_target{hasTarget:false, reason} (окно 5с).

### ФИКСЫ РЕДАКТОРА (MapEditorForm.cs)
- СНЯТ делитель 1e11 (ax=tx; az=tz) — телеметрия в метрах карты.
- REST-снимок и в редакторе (StartEditorRestSnapshot, 1с) — фура на месте даже без WS.
- Стрелка: RotateTransform(-heading*360 + 180) + дельтоид миникарты
  (нос (0,-9), крылья (±5,7), впадина (0,4.5)).
- Анти-произвольное вращение: heading только на sane-сэмплах, диапазон [-0.5..1.5],
  сглаживание <=0.6/кадр, нормализация перехода 0/1.

### Версия / сборка
- **1.0.38.62-AR-COORD-RESET-08.31-1047** — exe = build.txt = manifest; 0 ошибок.

### Что проверить у пользователя
- AR: перекрестье на ближайшей точке в движении; на паузе замирает, после снятия — оживает.
- Редактор: фура в правильном месте (метры), стрелка крутится по ходу движения,
  «произвольное вращение» исчезло.
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».
---

## Сессия 41 (продолжение 2, 31.08.2026 11:03) — ДИАГНОСТИКА ПО ЛОГАМ + ПОДРОБНЫЕ ЛОГИ В app_data (63)

### Эмпирика из workflow-лога (сессия v62, 10:48-10:49)
1. «первый сэмпл отброшен: вне границ карты» — 34 строки за минуту: в этот момент
   фура стояла ВНЕ диапазона карты (TruckBounds) — TruckTel отдаёт placement в меню/
   гараже, фильтр прав. Фура не встала => AR-подбор: известная фура вне карты =
   0 точек в 3км -> hasTarget=false -> «приложение не прислало цель» у страницы.
2. Мой REST-таск редактора СПАМИЛ GetCandidatePorts в workflow каждую секунду
   (нарушение правила «данные не в workflow»).
3. ar_target/ar_telemetry рассылались исправно, но на координатах вне карты
   ближайшей точки не находилось.

### ФИКСЫ (v63, 1.0.38.63-AR-LOG-DATA-08.31-1103)
- **РАЗДЕЛЕНИЕ ЛОГОВ (по правилу пользователя):**
  MapEditorForm.LogEditorData() -> Logger.Data() = app_data.log. В workflow
  остались ТОЛЬКО смены состояния: первый валидный сэмпл применён / REST
  запущен-остановлен / порт сменился. Спам «за секунду все отброшены» и
  «первый сэмпл отброшен» удалён из workflow; теперь в app_data
  ([TELEMETRY][DROP] с raw-координатами, 1/с).
- **GetCandidatePorts кэширован в REST-таске** (перечитывается раз в 10с) —
  спам workflow устранён.
- **AR (MainForm.ArTarget.cs):** данные placement/подбора — в app_data:
  [AR] placement применён из 'rest|ws' (1/5с), [AR] placement отсутствует в
  источнике (1/5с), [AR] tick: truck=... points=... near3km=... hasTarget=... 
  (1/5с) — сразу видно, почему «нет цели»: нет телеметрии / фура вне карты /
  точек рядом нет.
- Кандидат координат теперь хранит ПРИМЕНЁННЫЕ метры (ax/az), а не raw.

### Что проверить у пользователя
- Открыть редактор → фура-треугольник должен появиться в границах карты (в гараже/
  меню фильтр её прячет — это норм; выехать на дорогу → появится).
- AR в движении: перекрестье на ближайшей точке. Диагностика: Logs/app_data.log
  ([AR]/[EDITOR][TELEMETRY][DROP]), workflow — чистый, без данных.
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».
---

## Сессия 41 (продолжение 3, 31.08.2026 11:11) — КОРЕНЬ ВСЕХ БАГОВ: ЛОКАЛЬ-ПАРСИНГ ЧИСЕЛ (64)

### Симптом v63 НЕ ИСЧЕЗ (фура нет / AR «не прислало цель»)
app_data.log наконец показал ДАННЫЕ:
- [AR] truck=(12169557691955566,0,393734130859375,0,...) h=4431975483894348,000
- [AR] placement применён из 'rest': x=12169557752990724,0 ...
Прямой REST-замер одновременно: x=121657.32 (метры!). Приложение увидело ×1e11.

### КОРЕНЬ — классический баг локали (ru-RU Windows)
placement[0] = JSON-число (JValue). JValue.ToString() в ru-RU-культуре даёт
«121657,32» (ЗАПЯТАЯ). double.TryParse(«121657,32», InvariantCulture, Any) читает
запятую как РАЗДЕЛИТЕЛЬ ТЫСЯЧ -> 12165732... (гигантское число). Дальше весь
конвейер «работал верно»: границы卡车 -> вон, AR 3км -> пусто, hasTarget=false.
Это же объясняет ВСЕ прошлые странности: «×100/×1e11 масштабы», «мусорные кадры»
(часть кадров WS шла в другой культуре? нет — просто в этих полях запятая),
«произвольное вращение» (h тоже ×1e8), и, вероятно, даже «парадокс ×100 POI»
на старом ПК (другая локаль!).

### ФИКС (v64, 1.0.38.64-LOCALE-PARSE-FIX-08.31-1111)
ВЕЗДЕ, где координаты читаются из JValue-ЧИСЕЛ, заменён текстовый раунд-трип
(ToString()+TryParse) на прямое Value<double>() (Newtonsoft — культуро-независимо):
- MainForm.ArTarget.cs: ApplyPlacementJson — ВСЕ 6 элементов placement (Value<double>).
- MapEditorForm.cs ProcessTelemetry: tx/tz/th через Value<double>(), NaN/Inf -> return
  (метод, не цикл!) + [TELEMETRY][DROP] в app_data.
- MapEditorForm.cs LoadOverlays (POI) — Value<double>().
- MainForm.OverridesPipeline.cs: LoadStaticPois (Value<double>) + ApplyJObjectToPoint
  merge x/z (если JValue-число — напрямую; строки — как было, в files точка).
- LoadStaticCities НЕ тронут: там в JSON координаты СТРОКИ с точкой («1436.4»).

### Правило (запомнить!)
НИКОГДА не парси числовые JValue через ToString()+TryParse на машине с ru-RU:
запятая не проходит Invariant. Только Value<double>() / (double?)t[...]  / строковые
поля из файлов (там точка). ПРОВЕРКА-ПАТТЕРН: grep «ToString(), NumberStyles».

### Что проверить у пользователя (главное!)
- Редактор: фура-дельтоид появляется на карте в границах, стрелка по ходу движения.
- AR в движении: перекрестье на ближайшей точке (в app_data tick: truck=(121657...,...
  near3km>=1, hasTarget=True).
- Открыто: крэш DrawPath при первом старте; галочка «Показать в AR».
---

## Сессия 41 (продолжение 4, 31.08.2026, v65 AR-MIRROR-SEND-ONCE-08.31-1144)

**Реализовано (публикация v65):**
1. **ЗЕРКАЛО ГЕОМЕТРИИ (фидбек: «указатель инвертирован»)**:
   - Редактор: стрелка грузовика `RotateTransform(-heading*360)` — «+180°» УБРАН (вращение совпадает с миникартой).
   - AR JS: `projectPoint` зеркало инверсии: `fwd=(sin,cos)`, `right=(-cos,sin)`.
2. **КОНУС ОБЗОРА в редакторе** (OnPaint, между grid и точками): полупрозрачный сектор ±35° от фуры, направление `heading + _headYaw` (yaw головы из `truck.head.offset[3]`), радиус = min(panelW,panelH)*scale. Верифицировать на живом редакторе.
3. **ar_target — РАЗОВАЯ рассылка** (главное требование сессии): точки статичны; `ArUpdateTick` шлёт ar_target ТОЛЬКО при смене `_arLastSentGameName` (или после `hasTarget:false`). Форс-ресет `_arLastSentGameName=null`: StartArTargetFeed, RefreshArModel (модель изменилась), SendMapOverridesToMap (map_overrides_data → AR). Телеметрия `ar_telemetry` осталась потоковой, таймер 200мс → **33мс (30 Гц)** для плавности.
4. **Модель точек AR**: кортеж → `record ArPoint(gameName, realName, x, y, z, kind, isTarget, category, color)`. Города → category «Город» (жёлтые в JS), POI → Category оверлея (палитра как в редакторе `_poiPalette`), цели → Category=«Цель», Color из entry. Payload ar_target + `category`, `color`.
5. **ar_hud.js v65** (переписан ранее в сессии): размеры 30→96px (10м↔500м), fade до 1.5км, rAF ~60FPS, метром-гладкость `_markerSize*0.15`, за спиной → стрелка на НИЖНЕМ краю, crosshair только когда цель на экране; цвета категорий = редактор. `applyArTarget` хранит category/color, `ar.targetAt`.

**Что проверить пользователю:**
- Редактор: стрелка «налево в игре = налево», конус обзора крутится с головой (head tracking).
- AR: метка правильного цвета категории, плавная (без рывков), уменьшается к 500м, прозрачность растёт к 1.5км; цель сзади → стрелка снизу экрана; за пределами экрана — только стрелка+текст, перекрестье исчезло.

**Пороги в ar_hud.js (CFG):** minSize=30, maxSize=96, sizeNearDist=10, sizeFarDist=500, fadeDist=1500, fovDeg=75, edgeMargin=70.

**ПРЕДЫДУЩИЕ ЗАДАЧИ (НЕ потерять):**
- [ ] Краш редактора при первом старте: `Overflow error` в `g.DrawPath(_roadsPath)` (OnPaint). План: клиппинг/фейс-клиш в BuildRoadsPath + try/catch вокруг DrawPath.
- [ ] Чекбокс «Показать в AR» (ShowInAr: PointData field, Fields, FieldJson, ApplyJObjectToPoint, фильтр в RefreshArModel).
---

## Сессия 41 (продолжение 5, 31.08.2026, v66 + v67)

**v66 AR-VERTICAL-SMOOTH-08.31-1213:**
1. ОТКАТ зеркала проекции v65 (КОРЕНЬ «метка сзади при цели впереди»): эталон = миникарта: `fwd=(-sin,-cos)`, `right=(cos,-sin)`; v65 инвертировал оба — метка показывала назад.
2. ВЕРТИКАЛЬ: камера = глаз `placement[1] + eyeHeight(1.9м)`; метка «стоит на земле» `groundY = target.y + 0.5м` (ground-якорь, фикс «приклеена к камере по Y»); pitch кузова+головы вращает луч (offset[4]*2π).
3. Интерполяция: экстраполяция камеры между пакетами (30Гц→60fps, лимит 0.5с) + exp lerp экранной позиции (0.25/кадр); при смене цели — мгновенный прыжок (без перелёта).
4. Редактор: конус вдвое меньше (17.5°, радиус 0.5×); при потере телеметрии стрелка+конус НЕ удаляются — становятся СЕРЫМИ, подпись «Грузовик (нет данных)» (последняя позиция всегда на карте).

**v67 HEAD-PITCH-FIX-08.31-1230:**
1. headPitchSign инвертирован (-1 → +1): фидбек «метка движется ВМЕСТЕ с головой» = двойная инверсия. Теперь: голова вверх → камера вверх → мир уходит вниз экрана (метка остаётся в мире).
2. Телеметрия головы у TruckTel: head.offset = [x,y,z, yaw, pitch, roll]; пример пользователя: [-0.0456,-0.0365,0.0585, 0.9695, -0.0357, 0] — yaw=0.9695 (348°), pitch=-0.0357 (-12.9°), ДОЛЯ ОБОРОТА.
3. На миникарту в runtimeDebug добавлено HeadPitch (signed + нормализованный): `HeadPitch: -12.9° (347.1°n)`.
4. Статус AR показывает pitch головы (докалибровать по нему, если знак снова не тот).

**Проверить:** метка ведёт себя как объект в мире при наклонах головы; runtimeDebug HeadPitch меняется при вертикальном движении головы; знак — если метка снова «прилипает» (движется противоположно ожиданию), сменить CFG.headPitchSign в ar_hud.js на противоположный.
---

## Сессия 41 (продолжение 6, 31.08.2026, v68 AR-ROUND-DIM-08.31-1234)

**Правки AR по фидбеку:**
1. МЕТКА КРУГЛАЯ (была ромб): `drawMarkerToward` рисует arc; тёмный центр — маленький круг; чёрная обводка сохранена.
2. РАЗМЕРЫ −30%: minSize 30→21, maxSize 96→67 px. Перекрестье масштабируется как метка: `drawCrosshair(u,v,color, k)` где k=(sa.size/67)*0.7 — тоже до 30% меньше на максимуме.
3. ПРОЗРАЧНОСТЬ ТЕКСТА: `drawOutlinedText(text,x,y,font,color,alpha)` — обводка rgba(0,0,0,0.85*alpha) теперь прозрачнеет ВМЕСТЕ с заливкой (раньше stroke фиксировано 0.85 — текст «чернел»).
4. ПИТЧ КУЗОВА: УЖЕ учитывался (v66): pitchRad = truck.pitch(placement[4])*2π + headPitch. Знак кузова оставлен положительным (как в TrailSaver, PITCH_CALIBRATION_FACTOR=1.0, *360): голова вверх при подъёме носа — оба компенсируются в сумме. Если на подъёме метка уедет не туда — знак truck.pitch в projectPoint инвертировать.
5. Пороги сохранены: ≤10м max/непрозрачно; ≥500м min; 500..1500м — линейное затухание до 0 (в JS alpha=0 рисуется минимум 0.12, метка никогда не исчезает полностью — если нужно полное исчезновение, сменить минимум globalAlpha).

**Не забыть:** проверить v68 в игре; старые TODO: DrawPath-краш первого старта, чекбокс «Показать в AR».
---

## Сессия 41 (продолжение 7, 31.08.2026, v69 LOG-SPAM-FIX)

**Фидбек:** «хватит спамить» — `Command 'ar_telemetry' sent to map` писался в workflow 30 строк/с.
**Правка:** MainForm.SendCommandToMap — потоковые команды (IsStreamingCommand: ar_telemetry) теперь в app_data.log (Logger.Data), workflow чист. Разовые (ar_target, map_overrides_data…) — по-прежнему в workflow.
**Правило сохранено:** /memories/logging_rules.md + Hy3/INSTRUCTIONS «Машинное правило логов».
---

## Сессия 41 (продолжение 8, 31.08.2026, v70 + v71)

**v70 AR-PIN-HEIGHT-08.31-1331 (опубликован, протестирован частично):**
- Подписи AR: realName сверху / gameName снизу; высота точек Y=0 по ближайшему городу (350/50м переход+фикс, cities в ar_telemetry до 5км Y!=0); частота ×2; метка круг ÷2 (15/33px); стрелка не инвертирована; вне экрана только стрелка+текст; серая пометка ar_pin.
- Кнопка «Поставить в АР»; OpenCreateAt(pre-fill Y=truckY); клик по пустому месту = пометка; «отменить» снимает (NotifyArPinCancelled).
- MainForm.Current (static instance).

**v71 PIN-CROSS-VIEWCONE-08.31-1351 (опубликован):**
- Фидбек: пометка в АР = ПРОСТОЙ крестик (X) + подпись «Новая точка · дистанция»; кнопка «Пометить в АР» удалена, функционал = хоткей Shift+Ctrl+X (бывш. stop recording); hotkey-лог обновлён.
- ОТЛОЖЕННОЕ Применено: CFG.cityYCorrection=-40 (компенсация высоты городов, ar_hud.js); конус обзора на миникарте VIEW_CONE_LEN_M=350/полуугол 17.5° (map_draw.js, переменные); [OVR][DEBUG] спам → app_data (Logger.Data), в workflow только «load_order отсутствует»; редактор в ОДНОМ экземпляре (MainForm кнопка + AR pin закрывают старое окно).
- Урок: exe лочится при НЕПолном выходе (окно скрывается в трей) — всегда проверять Get-Process и требовать Exit из трея.
---

## Сессия 41 (продолжение 9, 31.08.2026, v72 AR-CURSOR-FIX-08.31-1437)

**Фидбек v71 исправлен:**
1. Shift+Ctrl+X БОЛЬШЕ НЕ закрывает редактор: переиспользуем открытое окно (Activate + OpenCreateAt). Кнопка «Редактор карты» — как было (закрыть+новое, осознанное).
2. Пометка в АР = иконка КАК В РЕДАКТОРЕ: серый кружок + перекрестье (верт./гориз. полосы).
3. Прицельный курсор: полупрозрачная точка 1x1 в центре экрана AR (под ней создаётся метка).
4. КРАШ МИНИКАРТЫ (v71): truckScreen объявлялся НИЖЕ блока конуса (TDZ ReferenceError) — из-за него отпадали все точки+фура. Фикс: позиция считается ДО конуса + guard isFinite.
5. РЫВОК ВЫСОТЫ раз в секунду: убран контур lerp-сам-на-себя; displayYFor = чистая функция(дистанция) + НЧ-фильтр 0.08; при <50м тянется к truckY. Приближение теперь ТЯНЕТ высоту точки к высоте грузовика (k: 350->50м: cityY->truckY).
6. cityYCorrection: -40 → -44 (поправка ниже на 4 м).
7. Клик по фуре в редакторе: копирует координаты (с высотой placement[1] — _truckY сохраняется), статус-строка-тултип.

**Session usage (ollama):** страница требует login (Invoke-WebRequest/headless отдают «Sign in»). Создан ollama_usage_v2.bat (headless Edge w/ профиль); правило «% в TODO первой задачей + прогрессбар в резюме» — в /memories/session_usage.md + Hy3. Надёжный источник пока — сообщение пользователя.
---

## Сессия 41 (продолжение 10, 31.08.2026, v73 AR-RADIUS-ROADS-08.31-1541 ОПУБЛИКОВАН)

**Первостепенные (полный список в INSTRUCTIONS [v73]):** дороги ×2 (min 3.5=шлейф); КОРЕНЬ рывка/спама = RefreshArModel сбрасывал _arLastSentGameName раз/с → сигнатура _arModelSig; радиус AR 1500м единый; fade = globalAlpha на весь блок (метка/крест/стрелка/текст), <0.03 не рисуем; плашка «нет точек в радиусе 1.5 км»; pin cap 1500м (взгляд выше горизонта → 1500); города AR компенсация; конус 15/5/40м по head.offset[4]; Shift+Ctrl+X = только AR + ar_pin_map (миникарта, state.arPinMap); GetCandidatePorts → app_data; Logs\new_object_po_selections.txt (LogNewPointSelection: город+дист/JSON/пустая строка — вызовы: клик-создание редактора + pin из АР); правило >90% квоты.

**Session usage:** до 87.0% / после 88.1%. При >90% — спрашивать подтверждение у пользователя.
**НЕ забыть:** v73 у пользователя: проверить дороги (толще), отсутствие рывка высоты, отсутствие спама в workflow, pin до 1500м, конус 15/40/5, ar_pin_map на миникарте, new_object_po_selections.txt в Logs.
---

## Сессия 41 (продолжение 11, 31.08.2026, v74 AR-EVENT-DRIVEN-08.31-1629 ОПУБЛИКОВАН + диагностика fuel)

**v74 (все из запроса):** AR событийная (telemetry/cities/targets только по изменению); pin dirY=sin(pitch) (+вверх/-вниз, кузов отключен); pin+курсор рисуются независимо от hasTarget; иконки событий миникарты выкл; дороги ×2.5; конус редактора ≤1.5км+экран; конус миникарты в % (30/90/10, пороги 8/20/35°); cities в C# payload −44 (JS cityYCorrection=0).

**ДИАГНОСТИКА Топливо на гибриде (замеры 31.08):
- WS-дельты TruckTel: speed меняется в ~290/12с кадрах, а truck.fuel.amount приходит только 1–2 раза за 12с, rest.stop — 1 раз. TruckTel шлёт fuel ТОЛЬКО при заметном изменении значения (дельта-механика), изменения копеечные (1428.12 → 1428.09 → 1426.43 за минуты) — UI «замирает» на 0–1% округлениях. ЭТО НЕ НАШ БАГ: данные доходят (REST живой, fuel.amount реально уменьшается).
- Гибрид-UI обновляет fuelPercent ВСЯКИЙ кадр WS (mergeHybridTelemetry), НО процент round(fuel/capacity*100) меняется только раз в ~0.35% ёмкости (≈5 л из 1465 л!) — при бачке 1465 л и расходе ~0.05 л/с значение в % меняется раз в ~10-20 с; между сменами UI статичен. Возможное улучшение (НЕ делано, ждать решения): показывать долю с десятыми (0.1%) либо количество литров.

**Машинное правило:** топливо на гибриде НЕ баг приложения/JS — это granularity процентов при баке 1465 л; REST/WS живые.
---

## Сессия 41 (продолжение 12, 31.08.2026, v75 AR-VIEW-PITCH-FUEL-L-08.31-1656 ОПУБЛИКОВАН)

**КРИТИЧНО (точки исчезли):** в v74 подбор цели был привязан к _arTruckChanged (сбрасывается после telemetry) — при неизменной позиции подбор вообще не выполнялся, поэтому «нет точек в радиусе 1.5 км» при куче точек. Фикс: подбор на КАЖДОМ тике (локальная математика, БЕЗ сети), ar_target по-прежнему ТОЛЬКО при смене лучшей точки (рассылка событийная, требование соблюдено).

**Композитный питч (v75):** projectPoint = 3D-повороты луча: (fdot,up) поворот на питч КУЗОВА вокруг right, затем на питч ГОЛОВЫ. Это и есть «складывание» питчей через композицию поворотов — эффект кузова автоматически ослабевает на борту (90°) и инвертируется при взгляде назад, полноценно по оси. Пользователь ошибочно видел «инверсию» из-за 2D-деки знака в v74 — сейчас операция = последовательное вращение (ответ на «определи сам»). Знаки: head.offset[4]>0=вверх (v67-эмпирика), dirY=+sin.
**Pin в C#:** аналогично только питч ГОЛОВЫ (dirY=sin(pitchRad)), кузов не влияет — pin считаем от взгляда.

**Fuel вар. (а) литры:** hybridState.fuelLiters из truck.fuel.amount; fuelValue = «1424 л» (или %, если liters нет). Диагностика v74 подтвердила: TruckTel шлёт fuel только при изменении; литры живее процентов.

**Session usage:** 26.4% в начале, после ~31%. Порог >90% не задет.
---

## Сессия 41 (продолжение 13, 31.08.2026, v76 AR2-D3D11-08.31-1726 ОПУБЛИКОВАН)

**AR v2.0 (нативный рендер, по документу «новая технология AR HUD»):**
- Папка AR\: ArGameState.cs (immutable-снимок), LatestBuffer.cs (latest-wins, без очереди поз, метрика skipped), ArBridge.cs (мост: публикация по событиям канала ArTarget, НЕ регулярка), ArOverlayWindow.cs (Win32-окно borderless/COLORKEY-транспарент/click-through/no-activate/TOPMOST), ArRenderer.cs (D3D11 + DXGI FLIP_DISCARD + waitable swap chain SetMaximumFrameLatency(1), RenderThread без Sleep/Timer).
- Vortice.Windows 3.8.3 подключен (PackageReference Vortice.Direct3D11; тянет D3D11+DXGI+Math).
- Кнопка «AR v2.0 (D3D)» в MainForm (под «Запустить AR», тоггл). StopArTargetFeed останавливает и v2.0.
- Мост данных: PublishArV2Snapshot() — по событиям ApplyPlacementJson (камера/голова/pin/города−44/цель). WS — только источник данных, каданс задаёт swap chain.
- v1 JS сохранён как reference implementation (не удалён).

**Csproj fix:** data\bin\WebOverlay.exe.WebView2\**\* исключён из Content/None (runtime-кэш WebView2 лочил publish: MSB3027/MSB3021 DawnWebGPUCache\data_1).

**Session usage:** 34.6% → 48.4% → 54.7% (контроль активен, <90 — ок).
**Проверка пользователю:** кнопка AR v2.0 (окно прозрачное поверх игры — пока БЕЗ рисования маркера, только pipeline/swap chain), лог [ARv2].