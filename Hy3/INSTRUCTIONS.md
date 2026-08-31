# INSTRUCTIONS — памятка агента для проекта ETS2_Assist_GUI

> Этот файл — для меня (агента). В НОВОЙ сессии пользователь даст команду прочитать
> `Hy3/INSTRUCTIONS.md` и `Hy3/WORKLOG.md`. Прочитав их, я сразу вспомню контекст
> и продолжаю работу без повторного разбора.
>
> ⚠️ **ОБЯЗАТЕЛЬНОЕ ПРЕДУПРЕЖДЕНИЕ ПЕРЕД ЛЮБОЙ ПРАВКОЙ .md (УРОК СЕССИИ 9, 27.08.2026):**
> файлы памяти — UTF-8 **С BOM**. Read/Edit-инструменты среды читают UTF-8 корректно
> ТОЛЬКО при наличии BOM; **без BOM кириллица показывается как mojibake** (`Ð Ď Ň ...`),
> а Edit падает с «oldString not found». Причина, с которой я споткнулся: PowerShell
> `Set-Content -Encoding utf8` (и `Out-File`) в PS 5.1 пишет UTF-8 **БЕЗ BOM** и ломает файл.
> **КАК НЕ ПОВТОРИТЬ:**
> 1. Правь .md ТОЛЬКО через Edit-инструмент — он сохраняет BOM и кириллицу корректно.
> 2. Если нужна пакетная правка в PowerShell — пиши СТРОГО С BOM:
>    `[System.IO.File]::WriteAllText($p, $t, [System.Text.UTF8Encoding]::new($true))`
>    (НЕ `Set-Content -Encoding utf8` и НЕ `Out-File` по умолчанию).
> 3. Увидел mojibake в read/edit — СТОП. Не дописывай поверх битого. Восстанови
>    `git checkout -- Hy3/INSTRUCTIONS.md` (и/или `Hy3/WORKLOG.md`) и переапликай правки
>    через Edit. Файлы git-tracked, оригинал всегда цел.

## Стоящие команды пользователя («запомни» / «учти»)
- Любая фраза «запомни …» или «учти …» означает: кратко записать факт в память
  (`INSTRUCTIONS.md` и/или `WORKLOG.md`) — БЕЗ излишеств, но так, чтобы в следующей
  сессии всё было понятно. Не нужно переспрашивать, просто зафиксируй.
- После КАЖДОГО изменения: дополнять `WORKLOG.md` и обновлять `INSTRUCTIONS.md`
  (новые задачи, планы, нереализованные прототипы, нерешённые проблемы).
- **Перед КАЖДОЙ задачей/правкой — сверяться с папкой логов приложения**
  (`bin\Release\net10.0-windows\win-x64\publish\Logs`). Там наша ключевая отладочная
  информация, которую мы создали сами для себя. Это главный источник диагностики.
- Если пользователь помечает задачу как **«в планы»** — дописать её в раздел
  «Планы на будущее» этого файла (и, при необходимости, в WORKLOG), как отложенную задачу.
- **Копилка задач (`Hy3\Копилка задач`):** если пришла команда добавить что-то в
  копилку задач — задача добавляется **целиком, без правок**, в папку
  `Hy3\Копилка задач` как отдельный `.md`-файл, в имени файла — краткое название
  задачи (как `Кафе.md`). Содержимое файла = ровно то, что дал пользователь,
  без редактирования/перефразирования.

## Логирование и отладка (правила пользователя)
- Логи пишутся РЯДОМ с exe: `bin\Release\net10.0-windows\win-x64\publish\Logs`
  (при публикации в др. папку — рядом с ней).
- В лог пишем ПОШАГОВУЮ отработку работы приложения для отладки. НЕ спамим
  значениями переменных — только шаги («что сделали»).
- Спамить значениями (для отлова багов) можно в `app_data` (рядом с exe / в AppData),
  если это действительно нужно.
- Живая отладка телеметрии: страница `http://localhost:8082/web_telemetry_inspector.html`
  — в реальном времени видны почти все данные от WebSocket TruckTel.
- **ОТЛАДКА ПО ЛОГАМ — БЕЗ УЧАСТИЯ ПОЛЬЗОВАТЕЛЯ (новое правило):** если для диагностики
  или отладки нужен фрагмент лога — агент САМ находит, куда пишется лог приложения
  (`bin\Release\net10.0-windows\win-x64\publish\Logs\app_workflow.log` или иной путь,
  выясняемый из кода/конфигурации), САМ читает файл лога и САМ ищет в нём нужную информацию
  (grep/чтение). **Пользователь НЕ должен копировать и вставлять фрагменты лога для агента.**
  При запросе у пользователя — просить только воспроизвести действие и (опционально) указать
  примерное время, а лог агент забирает и анализирует самостоятельно.

## Статус памяти между сессиями
Память живёт ТОЛЬКО в папке `F:\repo\ets2_assist\Hy3\` (файлы `WORKLOG.md` и `INSTRUCTIONS.md`).
Системная память (memory-*) может быть неполной — всегда сверяйся с этими файлами первыми.

## Что сделать в начале каждой сессии (по запросу пользователя)
1. Прочитать `Hy3/INSTRUCTIONS.md` (этот файл) — понять контекст, задачи, проблемы.
2. Прочитать `Hy3/WORKLOG.md` — что уже сделано, как собирать, ключевые файлы.
3. Кратко подтвердить пользователю, что контекст восстановлен, и уточнить текущую задачу.

## Как собирать и публиковать EXE
- VS MCP (`vs-mcp_LoadSolution`) НЕ грузит `.slnx` (падает `E_ABORT`). Не трать время на него.
- Использовать dotnet CLI из `F:\repo\ets2_assist`:
  ```
  dotnet publish ETS2_Assist_GUI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
  ```
- Результат: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist.exe` (~130 МБ).
  Имя EXE задано через `<AssemblyName>ETS2_Assist</AssemblyName>` в csproj (namespace проекта
  остаётся `ETS2_Assist_GUI`).
- JS-файлы в `data/` не компилируются — правки копируются при публикации как Content.
  Перед публикацией желательно `node --check` на изменённых `.js`.
- **OFFLINE-БИЛД (машина без egress в NuGet; 27.08.2026, СЕССИЯ 9):**
  `dotnet` на этой машине НЕ имеет доступа к NuGet-фиду → `dotnet restore` падает МОЛЧА:
  `error MSB4181: The "RestoreTask" task returned false but did not log an error.`
  Все нужные пакеты УЖЕ есть в глобальном кэше `%USERPROFILE%\.nuget\packages`.
  Рецепт:
  1. Скопировать все `*.nupkg` из `%USERPROFILE%\.nuget\packages` в плоскую папку
     (напр. `C:\Users\Admin\AppData\Local\Temp\opencode\offlinefeed`):
     `Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Recurse -Filter *.nupkg | Copy-Item -Destination <flatfeed>`
  2. `dotnet restore ETS2_Assist_GUI.csproj --source <flatfeed>`
  3. `dotnet publish ETS2_Assist_GUI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true --no-restore`
  - Windows Desktop workload НЕ нужен: `Microsoft.WindowsDesktop.App.Ref` уже лежит в
    `C:\Program Files\dotnet\packs`, минимальный WinForms восстанавливается/билдится офлайн нормально.
- **ЛОВУШКА SEMVER (27.08.2026, СЕССИЯ 9):** `<Version>` (NuGet-версия, используемая restore)
  ДОЛЖНА быть ЧИСТЫМ SemVer. Если в `<Version>` положить составную пре-версию вроде
  `1.0.38-TARGETS-FILE-2026.08.27-2110` — restore падает ТАК ЖЕ молча (`RestoreTask returned false`).
  Описательную строку класть ТОЛЬКО в `<InformationalVersion>` (SDK генерит
  `AssemblyInformationalVersionAttribute` из `$(InformationalVersion)`; `<AssemblyInformationalVersion>`
  в одиночку атрибут НЕ генерит).    `<Version>` оставлять чистой (`1.0.38`). Это ПРАВИЛО — иначе билд не соберётся.
- **ЛОВУШКА: exe ЗАЛОЧЕН при публикации (урок 28.08.2026, сессия 10, MAP-EDITOR):**
   `dotnet publish` (и `dotnet build`) падает на этапе упаковки с
   `System.UnauthorizedAccessException: Access to the path '...publish\ETS2_Assist.exe' is denied`,
   если этот exe **в данный момент запущен** (пользователь тестирует сборку). При этом цель
   `SyncDataToPublish` (`AfterTargets="Publish"`) НЕ дорабатывает, и папка `publish\data`
   ОСТАЁТСЯ ПУСТОЙ/неполной (пропадают js, css, GeoJson, language, localized_cities и т.д.) —
   приложение потом падает/работает без данных. Признак: файл залочен = процесс жив.
   **ПРАВИЛО (обязательное перед КАЖДЫМ build И publish):**
   1. ПЕРЕД `dotnet build`/`dotnet publish` проверить, запущен ли `ETS2_Assist.exe` (и для
      обратной совместимости `ETS2_Assist_GUI.exe` — старые сборки ещё могут быть запущены) и/или
      залочен ли файл (попытка `[System.IO.File]::Open(...,FileShare.None)`).
   2. Если залочен/запущен — **ВЫДАТЬ ПОЛЬЗОВАТЕЛЮ ЗАПРОС** (question-инструмент) закрыть
      запущенный exe (через Диспетчер задач / просто выйти из приложения) и только ПОСЛЕ
      подтверждения продолжить публикацию. НЕ перезаписывать поверх живого процесса.
   3. После успешной публикации ОБЯЗАТЕЛЬНО убедиться, что `publish\data` содержит ВСЕ папки
      (js, css, GeoJson, language, localized_cities, bin, ...) и файлы локализации
      (`data/language/ru.csv`, `en.csv`). Команда `SyncDataToPublish` логирует
      `скопировано N файлов data` — N должно быть ~390.
   4. Версия в `data/ets2_assist_build.txt` и `data/web_runtime_manifest.json` (`"build"`) ДОЛЖНА
      совпадать с `ProductVersion` собранного exe (читается из
      `bin\Release\...\publish\ETS2_Assist.exe` → VersionInfo.ProductVersion). Так как время
      сборки `Hmm` генерится в момент билда, после публикации сверить и при расхождении
      перезаписать оба файла в `publish\data` актуальной строкой.
   5. После переименования exe (сессия 28.08.2026) в папке `publish\` может остаться СТАРЫЙ
      `ETS2_Assist_GUI.exe` — после успешной публикации удалить его, чтобы не путать пользователя.

## Версионирование (НОВОЕ ПРАВИЛО, сессия 10, 28.08.2026)
- Формат строки версии: **`A.B.C.D-DESC-MM.DD-Hmm`**
  - `A` (1) — поколение проекта. Меняется редко.
  - `B` (0) — ключевой этап. Повышается ПО КОМАНДЕ при внедрении существенного функционала.
  - `C` (38) — номер текущего НАБОРА целей/проблем. НЕ меняется, пока не пофиксим набор и не
    начнём новый; тогда сбрасывается и растёт по команде.
  - `D` — ИТЕРАЦИЯ: счётчик работ по задачам из `C`. **БАМПИТСЯ АВТОМАТИЧЕСКИ (агентом):**
    +1 при завершении работы по задаче И +1 при каждом тест-билде. A,B,C — только по команде.
  - `DESC` — краткое описание проблем, решаемых в текущей `D` (обновляется агентом вместе с `D`,
    напр. `VER-FMT`, `RND-TARGETS`, `QUESTS-FIX`).
  - `MM.DD` — дата сборки (месяц.число, ведущие нули). Год НЕ указываем (не нужен).
  - `Hmm` — время сборки: час БЕЗ ведущего нуля, минуты С ведущим нулём, без разделителей.
    Пример: 9:22 -> `922`, 13:47 -> `1347`. UNIX-секунды убрали (слишком длинные).
- **НЕТ RND** в имени версии.
- `<Version>` = `A.B.C` (ЧИСТЫЙ SemVer) — только для restore. `D` НЕ в `<Version>`
  (ловушка SemVer: составная версия в `<Version>` роняет restore молча). `D` уходит в
  `FileVersion`/`AssemblyVersion` (`A.B.C.D`) и в строку `InformationalVersion`.
- Место строки версии: `BuildInfo.cs` → `BuildInfo.Version` читает `AssemblyInformationalVersion`
  (нужен `using System.Reflection;`). Строка прилетает из csproj `<InformationalVersion>`.
  `BuildInfo.Version` — в заголовке окна, логах, HTTP-заголовке `X-ETS2-Assist-Build`.
- csproj-свойства (рабочий вариант, сессия 10):
  ```
  <VersionMajor>1</VersionMajor>
  <VersionStage>0</VersionStage>
  <VersionSet>38</VersionSet>
  <VersionIter>1</VersionIter>          <!-- D: бампить при задаче/билде -->
  <VersionDesc>VER-FMT</VersionDesc>     <!-- что решаем в D -->
  <BuildDate>$([System.DateTime]::Now.ToString('MM.dd'))</BuildDate>
  <BuildTime>$([System.DateTime]::Now.ToString('Hmm'))</BuildTime>
  <DescriptiveVersion>$(VersionMajor).$(VersionStage).$(VersionSet).$(VersionIter)-$(VersionDesc)-$(BuildDate)-$(BuildTime)</DescriptiveVersion>
  <Version>$(VersionMajor).$(VersionStage).$(VersionSet)</Version>
  <AssemblyVersion>$(VersionMajor).$(VersionStage).$(VersionSet).$(VersionIter)</AssemblyVersion>
  <FileVersion>$(VersionMajor).$(VersionStage).$(VersionSet).$(VersionIter)</FileVersion>
  <AssemblyInformationalVersion>$(DescriptiveVersion)</AssemblyInformationalVersion>
  <InformationalVersion>$(DescriptiveVersion)</InformationalVersion>
  ```
- Бейджи билда (`data/ets2_assist_build.txt`, `data/web_runtime_manifest.json`) выставлять
  в `A.B.C.D-DESC-MM.DD-Hmm` вручную (пример: `1.0.38.3-QUESTS-FIX-08.28-1015`).
- **Свойства exe (Проводник → Свойства → Подробно):** формируются из csproj АВТОМАТИЧЕСКИ при
  каждой сборке — `FileVersion`/`AssemblyVersion` = `A.B.C.D`, `ProductVersion` (и всё поле
  «Версия продукта») = полная строка `A.B.C.D-DESC-MM.DD-Hmm`. Ручных действий НЕТ: достаточно
  бампнуть `VersionIter`/`VersionDesc` в csproj перед билдом. ОБЯЗАТЕЛЬНАЯ проверка после
  каждой сборки/публикации (сверка с build.txt/manifest):
  `(Get-Item <путь\к\ETS2_Assist.exe>).VersionInfo.ProductVersion`

## Обязательные команды (всегда!)
ПОСЛЕ КАЖДОГО изменения в проекте:
- **Дополнять `Hy3/WORKLOG.md`**: что изменил, в каком файле, зачем, результат сборки.
- **Обновлять `Hy3/INSTRUCTIONS.md`** (этот файл): добавлять
  - новые задачи (TODO-список ниже),
  - планы и идеи,
  - нереализованные прототипы / наброски,
  - нерешённые проблемы и гипотезы.
Не оставлять эти файлы устаревшими — они единственный мост памяти между сессиями.

## Архитектура (кратко)
- **Редактор карты: фура = треугольник-стрелка (v61, 31.08.2026).** `_truckHeading`
  парсится из placement[3] (ProcessTelemetry). OnPaint: g.Save→TranslateTransform→
  RotateTransform(-heading*360)→FillPolygon #ff4d4d + белая обводка 1.5px→Restore(gstate).
  Эталон формы = миникарта (map_draw.js): нос (0,-8), база ±(5.5,7). ЛОВУШКА: g.Restore()
  без GraphicsState = CS7036 — только Restore(g.Save()).
- C# WinForms приложение `ETS2_Assist_GUI`, .NET 10, Windows.
- Мини-карта — веб-оверлей (WebView2): страница `data/web_pda_map.html`,
  JS в `data/js/` (init.js, ui.js, websocket.js, targets.js, map_draw.js, trail.js, state.js).
- Поток команд цели: кнопки `BtnRandomTarget*` (Quests/QuestsManager.cs)
  → `SendCommandToMap(...)` (broadcast по WebSocket save-сервера)
  → `web_pda_map.html` слушает `websocket.js` → `generateRandomTarget` (targets.js)
  → `state.customTargets` + `targetX/targetZ` → `updateAll()` → `drawMinimap()` (map_draw.js).
- Телеметрия ETS2: `ws://localhost:8080/api/ws/delta/flat/` (TruckTel).
- `loadCustomTargets()` вызывается каждые 2с из `websocket.js` (`setInterval(fetchHttpData, 2000)`)
  и сбрасывает `state.targetMapOverview=false` / `zoomOnMapTargets=[]` — для random-цели
  это компенсируется вызовом `focusTargetOnMap` внутри.

## Найденные и исправленные проблемы
1. Сплеш: артефакты обводки версии (path+толстые перья). → Переписан на DrawString + кольцо 2px + авто-подгонка размера. Файл `SplashForm.cs`.
2. Случайная цель не появлялась на карте:
   - Баг А: `generateRandomTarget` молча не создавал цель без `state.roads`/`state.pois`
     (кнопки 1 и 2). → Добавлен гарантированный запасной спавн около фуры.
   - Баг Б: `loadCustomTargets` не восстанавливал `targetX/targetZ`/`state.target` в ветке
     «файл содержит random». → Добавлено восстановление координат.
   Файл `data/js/targets.js`.
3. **КОРЕНЬ БАГА «точки не появлялись / list_targets видел только файловые цели» (27.08.2026):**
   `loadCustomTargets` (targets.js) читал файл через `withRnd('/custom_targets.json')` →
   порт **:8082 (статический `data/`)**, а `saveTargetsToFile()` писал через POST
   `:8083/update_targets` → порт **:8083 (mutable-файл `custom_targets.json` в AppData
   `%LOCALAPPDATA%/ETS2_Assist`)**. То есть читали и писали РАЗНЫЕ файлы. Созданные кнопками
   цели попадали в AppData-файл, а миникарта перечитывала статический — и не видела их.
   **Фикс:** `loadCustomTargets` теперь читает `http://localhost:8083/custom_targets.json`
   (тот же файл, что и пишется). Это совпадает с `АРХИТЕКТУРА ПРОЕКТА.md` (targets_read/write
   через :8083). До первой загрузки `saveTargetsToFile` защищён: если `state.customTargets`
   пуст — сначала догружает из файла, чтобы не перезаписать файл пустым массивом.

## Архитектура целей (новая, после рефакторинга 27.08.2026)
- **Единый источник истины — файл `custom_targets.json` в AppData (:8083).** Все создаваемые
  кнопками цели пишутся туда (`saveTargetsToFile` POST :8083) и перечитываются оттуда.
- **Миникарта НЕ опрашивает файл сама.** Убраны все периодические вызовы `loadCustomTargets`:
  - `websocket.js` `fetchHttpData` больше НЕ вызывает `loadCustomTargets()` (оставлена только
    телеметрия `hydrateTelemetrySnapshot`).
  - `init.js` `loadData` больше НЕ вызывает `loadCustomTargets()` на старте.
- **Перезагрузка целей — ТОЛЬКО по команде `reload_custom_targets`** (приходит в миникарту
  через `saveWs`, case в `websocket.js` → `loadCustomTargets()`).
- Когда шлётся `reload_custom_targets`:
  1. **Старт системы:** миникарта при `saveWs.onopen` шлёт `map_ready` → C#
     (`QuestsManager.OnClientCommand`) отвечает `SendCommandToMap("reload_custom_targets")`.
  2. **После создания точки:** `generateRandomTarget` (после `await saveTargetsToFile()`)
     шлёт `request_reload_custom_targets` → C# → `reload_custom_targets`.
- Кнопки случайной цели (1,2,3,4) и «Проверка точек» — как ранее (`BtnRandomTarget*`,
  `BtnCheckTargets` в `MainForm.cs` / `QuestsManager.cs`). Все 4 кнопки теперь файл-базированы.

## Ключевая задача для СЛЕДУЮЩЕЙ сессии (НОВЫЙ ПК) — РЕШЕНО (28.08.2026)
- Парадокс ×100 POI **РАЗРЕШЁН** в сессии 11 (build 1.0.38.25-POI-FIX). Тултип на новом ПК
  показывал `11960205` (×100) при корректном файле `119602.05` — значит на ПК пользователя
  читался иной `Overlays.json` с ×100. В `MapEditorForm.LoadOverlays` добавлена ЗАЩИТНАЯ
  нормализация: если ОБЕ оси POI > 1 000 000 — делим на 100 (корректный файл не трогается).
- СК POI = дороги = игра (единая). Любое «×100» ломает наложение — НЕ умножать.
- Кнопки «инвертировать v/h POI» (MapEditorForm) теперь лишние (гипотеза отвергнута) — кандидаты
  на удаление.

## Нерешённые проблемы / гипотезы
- [x] **Корень бага «точка создаётся и СРАЗУ исчезает» — НАЙДЕН И ИСПРАВЛЕН (30.08.2026,
  сессия 36, build 1.0.38.56-LOG-TARGET-FIX):** JS-dedup в targets.js слал remove_target
  по broadcast при нескольких WS-клиентах → ping-pong add_target/remove_target и цель
  пропадала. Фикс: удалён JS-dedup; запись/удаление целей — ТОЛЬКО C#-конвейер
  (AddTargetToFile c защитой от дублей questType). Это правило — ЖЁЛТИЧНОЕ: JS НИКОГДА
  не шлёт remove_target/add_target сам (кроме add_target при создании кнопкой).
- [x] **Спам логов «placement отсутствует» — ИСПРАВЛЕН (сессия 36, v56):** лог только
  смены состояния (`_teleHadPlacement` в MapEditorForm). Было 12k+ строк/день.
- [ ] **Глюк авто-зумa (подтверждён пользователем):** одна из кнопок (№2, спавн у POI на
  любом расстоянии) вызывает `focusTargetOnMap` → `targetMapOverview=true` → карта
  разом масштабируется «дальше всей карты», затем возврат к фуре. Надо либо не сбрасывать
  обзор для активной цели, либо ограничить макс. дистанцию обзора (чтобы не «больше чем вся карта»).
- [ ] Предупреждения компиляции `CS0414` (неиспользуемые поля ...) и `CS8600/8602`
  (nullability) в `MainForm.cs` — не критично, но можно почистить.

## Список задач (TODO)
- [x] **РЕШЕНО (v64, 1.0.38.64-LOCALE-PARSE-FIX-08.31-1111) — КОРЕНЬ ВСЕХ «МАСШТАБНЫХ»
  БАГОВ:** JValue.ToString() на ru-RU даёт «121657,32» (запятая); Invariant TryParse
  читает «,» как разделитель тысяч → числа ×1e11 мусор («вне границ карты» в редакторе,
  «приложение не прислало цель» в AR = фура «вне карты → 0 точек в 3км»,
  «произвольное вращение» стрелки, вероятно и «парадокс ×100 POI» на старом ПК).
  ПРАВИЛО: числовые JValue читать ТОЛЬКО Value<double>() (культуро-независимо);
  ToString()+TryParse — ТОЛЬКО для строковых полей из файлов (там точка).
  Проверка-паттерн: grep «ToString(), NumberStyles». Исправлено: ArTarget.ApplyPlacementJson
  (все 6 элементов), MapEditorForm.ProcessTelemetry + LoadOverlays, OverridesPipeline
  (LoadStaticPois, merge x/z). LoadStaticCities не тронут — там координаты-строки («1436.4»).
- [ ] **ВЕРИФИЦИРОВАТЬ (v64, 1.0.38.64-LOCALE-PARSE-FIX-08.31-1111):** (а) редактор —
  фура-дельтоид появляется в границах карты, стрелка по ходу движения;
  (б) AR в движении — перекрестье на ближайшей точке (в app_data: near3km>=1,
  hasTarget=True); (в) AR на паузе — замирает, оживает после снятия;
  (г) workflow-лог БЕЗ данных, данные → Logs/app_data.log
  ([AR] placement/tick, [EDITOR][TELEMETRY][DROP]).
- [ ] **Крэш редактора при ПЕРВОМ старте (v61, не сделано):** Overflow error в
  g.DrawPath(_roadsPath) (OnPaint, MapEditorForm ~755) — белый фон, красный крест;
  исчезает при повторном открытии. Вероятно экстремальная матрица/координаты дорог
  при первом построении. План: culling сегментов в BuildRoadsPath, clamp масштаба,
  try/catch вокруг DrawPath (проглатывать paint-исключение, перерисовать после готовности).
- [ ] **Галочка «Показать в AR» в редакторе (v61, не сделано):** PointData.ShowInAr
  (int 0/1) + Fields (Group «AR») + FieldJson + ApplyJObjectToPoint + PointDataToJObject;
  BuildFieldControls уже строит CheckBox для bool — но поле int: использовать ValueType
  bool c конверсией в OnFieldChanged/ReadPanelIntoPoint ИЛИ сразу bool ShowInAr.
  Фильтр в MainForm.ArTarget.RefreshArModel: НЕ показывать точки с ShowInAr==0
  (только отмеченные летят в AR). ar_target остаётся «ближайшая из отмеченных».
- [ ] **AR не показывает цель/«приложение не прислало цель» (сессия 41):** проверить у
  пользователя: wsPort в web_data.json; лог [AR] «Модель точек обновлена: N»; если N=0 —
  причина в статики/overrides; после внедрения «Показать в AR» подбор станет по отмеченным.
- [ ] **ВЕРИФИЦИРОВАТЬ у пользователя (сессия 40, 30.08.2026, сборка 1.0.38.60-AR-C-TARGET):**
  AR — DUMB-RECEIVER (требование пользователя): страница не грузит точки; приложение само
  подбирает ближайшую (MainForm.ArTarget.cs: свой WS-клиент телеметрии, копия модели
  конвейера, рассылка ar_target + ar_telemetry 5 Гц через WS 8084; город/POI ≤3 км,
  цели без лимита). Проверить: перекрестье на ближайшей точке, на паузе замирает,
  прижим к краю + стрелка; точность проекции — CFG.fovDeg (75) в ar_hud.js.
  **ПРАВИЛО ТЕЛЕМЕТРИИ (урок 30.08.2026): placement существует ТОЛЬКО в WS-дельте и в
  окрестности движения (на паузе не шлётся); REST /flat/* его НЕ отдаёт. Порт берём
  из web_data.json (обновляет приложение), НЕ хардкодим 8080.**
- [x] **ВЕРИФИЦИРОВАНО у пользователя (сессия 32, 29.08.2026):** hex-ID загружается в поле
  «Системное имя» без искажений; заглавные в GameName переводятся в строчные (не удаляются);
  санитайзер исправлен, тест-сборка 1.0.38.50-GAMEID-SANITIZE успешна.
- [ ] **ВЕРИФИЦИРОВАТЬ у пользователя (сессия 34, 30.08.2026, сборка 1.0.38.52-OVR-PIPELINE):**
  НОВЫЙ КОНВЕЙЕР overrides. Приложение собирает effective-состояние (статика + overrides +
  test_targets) и шлёт ГОТОВЫЙ пакет `map_overrides_data` (миникарта = dumb-receiver, ничего
  не merge сама). Проверить: статусные строки (редактор+миникарта, клик в редакторе → логи);
  перемещение города/POI в редакторе → сохранение → новая позиция на миникарте; тестовые
  кнопки пишут в `map_overrides\test_targets.json` (custom_targets.json более не пишется);
  тоггл «Показать карту»: ВКЛ = карта всегда видна, ВЫКЛ (по умолчанию) = обычная логика.
  Код: MainForm.OverridesPipeline.cs (NEW), MainForm.PointsOverrides.cs (сокращён),
  QuestsManager.cs (file ops → TestTargetsFile), data/js/points_overrides.js (переписан),
  data/js/status_bar.js (NEW), websocket.js (map_overrides_data + minimapAlwaysOn),
  UI/EditorStatusBar.cs (NEW), WebUIManager.cs, MainForm.cs (тоггл).
- [ ] **ВЕРИФИЦИРОВАТЬ у пользователя (сессия 37, 30.08.2026, сборка 1.0.38.57-QUEST-PAUSE-DLG):**
  (а) пауза перед КАЖДЫМ квест-диалогом (только если НЕ на паузе; при сбое — ретрай PAUSE);
  (б) задержка 2с между паузой и диалогом; (в) награды в диалоге — ОТДЕЛЬНЫЙ блок,
  зелёный жирный (QuestDialogForm: _messageLabel + _rewardsTable c Label на каждый элемент);
  (г) после закрытия диалога фокус в окно игры (ReturnFocusToGame в finally).
- [~] **ВЕРИФИЦИРОВАНО ПОЛЬЗОВАТЕЛЕМ (30.08.2026, по v56):** «Точки добавляются. Даже триггеры
  у них срабатывают идеально» — пинг-понг целей устранён, квест-триггеры работают.
- [ ] **ВЕРИФИЦИРОВАТЬ у пользователя (сессия 36, 30.08.2026, сборка 1.0.38.56-LOG-TARGET-FIX):**
  (а) кнопки Курьер/Тайник/Перекус: точка появляется и ОСТАЁТСЯ на карте (фикс ping-pong:
  JS не шлёт remove_target, удаление только через C#-конвейер); (б) лог при паузе игры —
  без потока «placement отсутствует» (анти-спам в MapEditorForm.ProcessTelemetry,
  лог только смены состояния); (в) цели в редакторе: дубли random-целей как POI «custom»
  больше не создаются (ApplyOverrideFiles 3b); (г) load_order.txt нормализуется если
  перепутан (test_targets.json последним).
- [ ] **Правило пайплайна (закрепить):** JS-страницы НИКОГДА не шлют add_target/
  remove_target в C# сами (add_target — только при создании цели кнопкой). Все удаления
  / кулдауны / изменения файла - исключительная зона C# (MainForm.OverridesPipeline.cs
  + QuestsManager.AddTargetToFile). Нарушает исход "двум страницам» = ping-pong.
- [ ] **ВЕРИФИЦИРОВАТЬ у пользователя (сессия 35, 30.08.2026, сборка 1.0.38.53-STATUS-MAP-FIX):**
  (а) редактор карты НЕ падает при открытии (фикс крэша MeasureString в EditorStatusBar —
  try/catch + клон Font + SafeMeasure); (б) миникарта рисует точки/грузовика СРАЗУ (фикс
  ReferenceError: безопасные typeof-вызовы getEffectivePoiList/getEffectiveCityList в
  map_draw.js — гонка с map_ready); (в) OllamaLimits.exe (НОВЫЙ проект F:\repo\ollama_limits,
  standalone 49.6МБ): трей-иконка Session/Weekly usage с ollama.com/settings, окно входа
  WebView2 при редиректе, пульс >95%, клик → браузер, ПКМ → Выход.
- [ ] **Проверить у пользователя:** видна ли теперь точка случайной цели (гарантированная
  отрисовка `randomTarget` в map_draw.js + кнопка №4 «Ближайшая цель» 51-60м на дороге).
- [ ] **Исправить глюк авто-зумa:** ограничить макс. дистанцию обзора в `focusTargetOnMap`
  (чтобы карта не масштабировалась «дальше всей карты») и не сбрасывать `targetMapOverview`
  для активной цели в `loadCustomTargets` (каждые 2с).
- [ ] Проверить, что логи цели пишутся (создание + дистанция; кнопка «Проверка точек»).
- [ ] (опционально) Почистить неиспользуемые поля/`nullability`-предупреждения в MainForm.cs.
- [ ] (исследовать) Перенести рисование ВСЕХ целей на надёжный источник, а не `state.customTargets`.
- [ ] **ВЕРИФИЦИРОВАТЬ ИСПРАВЛЕНИЯ СЕССИЙ 1-9 В РАНТАЙМЕ (у пользователя):**
  - Пауза/оверлей: карта и гибрид СКРЫВАЮТСЯ на паузе ETS2; `web_pause_logo` показывается;
    хоткей CTRL+SHIFT+S не висит 4с (подтверждение паузы срабатывает). Код готов
    (MainForm `_pausedIntent` + `ParsePausedResponse`, WebUIManager `IsGamePausedAsync`,
    скрытие map/hybrid на паузе), НЕ проверено на практике.
  - Replay-анимация миникарты: `SetUiSync` теперь шлёт `show_ui` (не `show_ui_first`) —
    анимация играет ОДИН раз, не каждые ~3с. НЕ проверено.
  - Двойная случайная цель: `map_draw.js` больше не рисует `randomTarget` дважды
    (пропуск в циклах inactive/active). НЕ проверено.
  - Все 4 кнопки случайной цели идут через file-write (`add_target` → `AddTargetToFile`) —
    подтверждено в коде, НЕ проверено в рантайме.
- [x] **ВЕРСИОНИРОВАНИЕ (новое, сессия 10, уточнено):** строка `A.B.C.D-DESC-MM.DD-Hmm`.
  A,B,C — только ПО КОМАНДЕ (C=38 = текущий набор задач). D (итератор) бампит САМ агент
  при завершении задачи и при каждом тест-билде. UNIX-секунды и год убраны (слишком длинные);
  время `Hmm` (час без нуля, минуты с нулём). Собрано `1.0.38.3-QUESTS-FIX-08.28-1015`.
- [ ] **ВЕРИФИЦИРОВАТЬ КВЕСТ-КНОПКИ/ТРИГГЕРЫ (сессия 10, доп) у пользователя:** 3 кнопки
  (Курьер-забор/Тайник/Перекус) + тоггл Обзор; вход в зону → нужный диалог; завершение по
  кнопке → награда/удаление; Курьер ведёт к фиолетовой точке выдачи; метка вне карты у стрелки;
  выход из зоны → красная. Флапинг WS при старте — воспроизводится ли ещё.
- [ ] **КВЕСТЫ (пользователь, этапы):** менеджер квестов (база реализована в сессии 10, доп):
  - **Супер-задача:** квест «Курьер» (забор→выдача) — реализован; успех → повысить B, новый цикл C/D.
  - **Премиум-задача:** квест «Попутчик» (СВЕРХ реализованного — ещё не сделан).
  - Дальше развивать функционал случайных целей по фидбеку пользователя.

## Нереализованные прототипы / идеи
- (пока нет зафиксированных; добавлять сюда наброски и отложенные правки)

## Планы на будущее (отложенные задачи)
- **[В ПЛАНЫ] Полный отказ от legacy custom_targets.json (сессия 34 — выполнено для целей
  тестовых кнопок).** НОВЫЙ принцип (реализован): единый конвейер
  `MainForm.OverridesPipeline.cs` — приложение единственный владелец всех файлов точек
  (`map_overrides\*.json` по load_order + `test_targets.json`); миникарта и редактор
  получают/строят effective-state через один и тот же merge-код (`ApplyJObjectToPoint`).
  Остаточная legacy: старый `custom_targets.json` в AppData больше не используется
  тестовыми кнопками (остаётся только чтение статических целей редактора — см.
  `MapEditorForm._targetsFile` → при полной чистке перевести и его на test_targets).
- **[В ПЛАНЫ] Чистка статических целей редактора**: `MapEditorForm.LoadTargets` всё ещё
  читает legacy `custom_targets.json` как «статические точки» — перевести на
  test_targets.json/overrides и убрать дифференциацию «статические/ользовательские».
- **[В ПЛАНЫ] Чистка корня проекта от патч-ноутов в формате `.md` (второстепенная задача, 27.08.2026).**
  Изучить содержимое ВСЕХ `*.md` в корне проекта (кроме `АРХИТЕКТУРА ПРОЕКТА.md` и `README.md`,
  а также памяти `Hy3\`), кратко резюмировать проделанную работу: только ПОЛЕЗНОЕ, удачные
  исправления; ОСОБОЕ внимание — сформированные пользователем задачи, которые в итоге были
  забыты/не сделаны; искать успешные реализации задач. Свести всё в ОДИН общий файл
  **`Отработано ChatGPT`** (создать в корне проекта). Кандидаты (корень, исключая
  архитектуру/readme/Hy3): `MINIMAP_ARCHITECTURE_1.0.18.md`, `MINIMAP_ARCHITECTURE_1.0.19.md`,
  `MIGRATION_2026-08-25.md`, `WEB_RUNTIME_SYNC_1.0.24.md`, `VERSION_FIX.md`, `PATCH_INSTRUCTIONS.md`,
  `RELEASE_NOTES_1.0.30.md`, `RELEASE_NOTES_1.0.31.md`, `RELEASE_NOTES_1.0.32.md`,
  `PATCH_1.0.20_NOTES.md`, `PATCH_1.0.21_NOTES.md`, `PATCH_1.0.22_NOTES.md`, `PATCH_1.0.23_NOTES.md`,
  `PATCH_1.0.29_NOTES.md`, `BUILD_1.0.15_NOTES.md`, `SPLASH_REGRESSION_FIX_1.0.29.md`,
  `SPLASH_FOCUS_FIX_2026-08-25.md`, `FULL_SYNC_1.0.25.md`, `FULL_REFRESH_1.0.26.md`,
  `FULL_REFRESH_1.0.27.md`, `PUBLISH_DATA_RULE.md` (плюс `data/MAP_RUNTIME_FIX.md`).

## Заметки по стилю работы с пользователем
- Отвечать на русском языке.
- Пользователь ценит конкретные правки + сборку + краткий отчёт, а не просто теорию.
- Память между сессиями ведётся вручную в `Hy3/`.

### [v65 31.08.2026] AR-MIRROR-SEND-ONCE — опубликовано
- ar_target РАЗОВО (смена цели / форс-ресет: StartArTargetFeed, RefreshArModel, SendMapOverridesToMap). Телеметрия 30 Гц (Timer 33мс). Модель = record ArPoint(...category, color). Payload: category+color.
- Зеркало: редактор RotateTransform(-h*360) без +180; JS fwd=(sin,cos), right=(-cos,sin). Конус обзора ±35° (heading+headYaw).
- ar_hud.js CFG: minSize=30 maxSize=96 sizeNearDist=10 sizeFarDist=500 fadeDist=1500. За спиной → стрелка снизу. rAF ~60FPS.
- МАШИННОЕ ПРАВИЛО (подтверждено v64): числовые JValue — ТОЛЬКО .Value<double>(); ToString+TryParse(InvariantCulture) на ru-RU = запятая → ×1e8 мусор.

### TODO (не завершено)
- [ ] ВЕРИФИЦИРОВАТЬ v65 у пользователя: зеркало стрелки/AR-проекции, конус с головой, цвет/размер/фэйд метки, стрелка снизу для цели сзади, разовая рассылка (в логе ar_target только при смене).
- [ ] Краш первого старта редактора: Overflow error в g.DrawPath(_roadsPath) — план: клиппинг в BuildRoadsPath + try/catch DrawPath.
- [ ] Чекбокс «Показать в AR» (PointData.ShowInAr → Fields/FieldJson/ApplyJObjectToPoint/RefreshArModel filter).
### [v65 31.08.2026] AR-MIRROR-SEND-ONCE — опубликовано
- ar_target РАЗОВО (смена цели / форс-ресет: StartArTargetFeed, RefreshArModel, SendMapOverridesToMap). Телеметрия 30 Гц (Timer 33мс). Модель = record ArPoint(...category, color). Payload: category+color.
- Зеркало: редактор RotateTransform(-h*360) без +180; JS fwd=(sin,cos), right=(-cos,sin). Конус обзора ±35° (heading+headYaw).
- ar_hud.js CFG: minSize=30 maxSize=96 sizeNearDist=10 sizeFarDist=500 fadeDist=1500. За спиной → стрелка снизу. rAF ~60FPS.
- МАШИННОЕ ПРАВИЛО (подтверждено v64): числовые JValue — ТОЛЬКО .Value<double>(); ToString+TryParse(InvariantCulture) на ru-RU = запятая -> x1e8 мусор.

### TODO (не завершено)
- [ ] ВЕРИФИЦИРОВАТЬ v65: зеркало стрелки/AR-проекции, конус с головой, цвет/размер/фэйд метки, стрелка снизу для цели сзади, разовая рассылка.
- [ ] Краш первого старта редактора: Overflow error в g.DrawPath(_roadsPath) - план: клиппинг в BuildRoadsPath + try/catch DrawPath.
- [ ] Чекбокс «Показать в AR» (PointData.ShowInAr -> Fields/FieldJson/ApplyJObjectToPoint/RefreshArModel filter).
### [v66+v67 31.08.2026] AR-VERTICAL-SMOOTH / HEAD-PITCH-FIX — опубликовано
- v66: ОТКАТ зеркала проекции v65 (fwd=(-sin,-cos), right=(cos,-sin) — КАК НА МИНИКАРТЕ; v65 «зеркало» было ошибкой 180°). Камера=глаз (placement[1]+1.9м), метка ground-якорь (target.y+0.5м). Экстраполяция камеры+lerp позиции. Конус редактора ×0.5 (17.5°). Офлайн: стрелка+конус серые, НЕ удаляются.
- v67: headPitchSign=+1 (был -1; фидбек «движется вместе с головой» = двойная инверсия). head.offset[4]=pitch (доля оборота; пример: -0.0357=-12.9°). runtimeDebug: HeadPitch.
- Если знак pitch снова не тот — CFG.headPitchSign в ar_hud.js (±1); калибровка по статус-строке AR «pitch N°».
- TODO с прошлых сессий (актуально): краш DrawPath первого старта редактора; чекбокс «Показать в AR».
### [v68 31.08.2026] AR-ROUND-DIM — опубликовано (exe=txt=manifest 1.0.38.68-AR-ROUND-DIM-08.31-1234)
- AR метка КРУГ; размеры -30% (min 21 / max 67); перекрестье масштабируется как метка (*0.7).
- drawOutlinedText(text,x,y,font,color,alpha): обводка тоже прозрачнеет (фикс «чернеющего» текста).
- Питч кузова учитывается в projectPoint (placement[4]*2π + headPitch); знак кузова = TrailSaver.
- Пороги без изменений: 10м/500м/1.5км. Минимум globalAlpha 0.12 (никогда не исчезает полностью).
### МАШИННОЕ ПРАВИЛО ЛОГОВ (пользователь, 31.08.2026 — «хватит спамить»)
- workflow-лог (AppendLog/LogEditor): ТОЛЬКО шаги и переходы состояния (1 строка на событие).
- ПОТОКОВЫЕ/ВЫСОКОЧАСТОТНЫЕ данные (ar_telemetry 30Гц, координаты, кадры, периодические команды) — ТОЛЬКО app_data (Logger.Data/LogEditorData). См. IsStreamingCommand() в MainForm.SendCommandToMap.
- Проверка перед коммитом: нет ли AppendLog/LogEditor внутри таймеров/циклов/частых тиков.
### [v70+v71 31.08.2026] AR-PIN-HEIGHT / PIN-CROSS-VIEWCONE — опубликовано
- AR: подписи realName/gameName; высоты Y=0 по городам (hCityDist 350 / hLockDist 50, фиксация в _yLock Map); cityYCorrection=-40 (все города выше земли на ~40м — переменная CFG.cityYCorrection в ar_hud.js); fps x2 (~120 расчетов/с, double-step lerp), FPS в статусе; метка круг min15/max33; вне экрана только стрелка+текст.
- «Поставить в АР» = Shift+Ctrl+X (кнопка удалена); OpenCreateAt(x,z,y,fromArPin) в редакторе (один экземпляр: старое окно ЗАКРЫВАЕТСЯ и открывается новое — правило и для кнопки «Редактор карты»); ar_pin {active,x,y,z}; клик по пустому месту тоже помечает.
- Конус обзора на миникарте: VIEW_CONE_LEN_M=350, VIEW_CONE_HALF_ANGLE_DEG=17.5 (переменные в map_draw.js — править здесь).
- ЛОГИ: [OVR][DEBUG] → app_data (Logger.Data). ПРАВИЛО: потоки/данные/файлы — app_data, workflow — только события.
### МАШИННОЕ ПРАВИЛО Session usage (пользователь, 31.08.2026)
- ПЕРЕД любой задачей и ПОСЛЕ: проверить https://ollama.com/settings -> Session usage %.
- Инструмент: ollama_usage_v2.bat (headless Edge под профилем пользователя) -> SESSION: NN.N.
- Процент В TODO первой задачей; после публикации в резюме — графический прогрессбар [████----] NN.N%.
- Страница требует входа: если UNKNOWN — спросить процент у пользователя в конце сессии.
### Session usage (ollama) — РАБОТАЕТ (31.08.2026)
- Команда: `ollama_usage_v2.bat` -> SESSION: NN.N (разовый вход: ollama_login.bat, выделенный профиль).
- Эмпирика: в PS5.1 `& msedge --dump-dom` в переменную даёт ПУСТО — только Start-Process -RedirectStandardOutput в файл + чтение файла.
- Агент: % первой задачей в TODO; после публикации прогрессбар [████░░░] в резюме. Если UNKNOWN — перезапустить ollama_login.bat.
### [v73 31.08.2026] Первостепенные (все применены) + вторичные
1. Дороги миникарты: MIN_ROAD_WIDTH=3.5 (=шлейф) — state.js roadWidthStyles ×2.
2. Рывок высоты AR раз/с КОРЕНЬ: RefreshArModel обнулял _arLastSentGameName каждую секунду → ar_target пересылался → ar.target пересоздавался. Фикс: сигнатура _arModelSig (имя+коорд F1) — ресет ТОЛЬКО при реальном изменении; «Модель точек обновлена» в workflow только при изменении, иначе app_data.
3. ЕДИНЫЙ радиус AR 1500м (было 3км для городов); reason «нет точек в радиусе 1.5 км»; плашка AR показывает этот текст при пустой цели; fade txtAlpha теперь на ВЕСЬ блок (метка+крестик+стрелка+текст, ctx.save/globalAlpha/restore, <0.03 — не рисуем).
4. Pin: PinMaxDistM=1500; взгляд выше горизонта/параллельно → сразу 1500м (не 500).
5. Города при выборе AR-целью: kind='city' тоже проходит displayYFor (компенсация -44).
6. Конус миникарты: LEN 15м базово, вверх → до 40, вниз → до 5 (head.offset[4]: +вверх/-вниз, k=pitch*10).
7. Shift+Ctrl+X: ТОЛЬКО пометка АР + ar_pin_map (кружок+крест на миникарте, state.arPinMap); редактор НЕ открываем/НЕ фокусируем. GetArPin ещё используется.
8. Спам AR: см.п.2; GetCandidatePorts → LogEditorData (app_data).
9. Logs\new_object_po_selections.txt (3 строки: город+дист / JSON {x,y,z} / пустая) — LogNewPointSelection: клик-создание в редакторе + pin из АР.
10. КВОТА: >90% Session usage — СПРАШИВАТЬ подтверждение перед задачей.