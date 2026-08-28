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
- Результат: `bin\Release\net10.0-windows\win-x64\publish\ETS2_Assist_GUI.exe` (~130 МБ).
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
   `dotnet publish` падает на шаге `GenerateBundle` с
   `System.UnauthorizedAccessException: Access to the path '...publish\ETS2_Assist_GUI.exe' is denied`,
   если этот exe **в данный момент запущен** (пользователь тестирует сборку). При этом цель
   `SyncDataToPublish` (`AfterTargets="Publish"`) НЕ дорабатывает, и папка `publish\data`
   ОСТАЁТСЯ ПУСТОЙ/неполной (пропадают js, css, GeoJson, language, localized_cities и т.д.) —
   приложение потом падает/работает без данных. Признак: файл залочен = процесс жив.
   **ПРАВИЛО (обязательное перед КАЖДОЙ публикацией):**
   1. ПЕРЕД `dotnet publish` проверить, запущен ли `ETS2_Assist_GUI.exe` и/или залочен ли файл
      (попытка `[System.IO.File]::Open(...,FileShare.None)`).
   2. Если залочен/запущен — **ВЫДАТЬ ПОЛЬЗОВАТЕЛЮ ЗАПРОС** (question-инструмент) закрыть
      запущенный exe (через Диспетчер задач / просто выйти из приложения) и только ПОСЛЕ
      подтверждения продолжить публикацию. НЕ перезаписывать поверх живого процесса.
   3. После успешной публикации ОБЯЗАТЕЛЬНО убедиться, что `publish\data` содержит ВСЕ папки
      (js, css, GeoJson, language, localized_cities, bin, ...) и файлы локализации
      (`data/language/ru.csv`, `en.csv`). Команда `SyncDataToPublish` логирует
      `скопировано N файлов data` — N должно быть ~390.
   4. Версия в `data/ets2_assist_build.txt` и `data/web_runtime_manifest.json` (`"build"`) ДОЛЖНА
      совпадать с `ProductVersion` собранного exe (читается из
      `bin\Release\...\publish\ETS2_Assist_GUI.exe` → VersionInfo.ProductVersion). Так как время
      сборки `Hmm` генерится в момент билда, после публикации сверить и при расхождении
      перезаписать оба файла в `publish\data` актуальной строкой.

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
- [x] **Корень бага «точки не рисуются / list_targets видел только файловые цели» —
  НАЙДЕН И ИСПРАВЛЕН (27.08.2026):** рассинхрон чтения (:8082 статический) и записи
  (:8083 AppData) `custom_targets.json`. Теперь читаем тот же файл, что пишем (см. раздел
  «Архитектура целей» выше). Нужно у пользователя подтвердить на практике, что все 4 кнопки
  теперь показывают точку и «Проверка точек» видит их.
- [ ] **Глюк авто-зумa (подтверждён пользователем):** одна из кнопок (№2, спавн у POI на
  любом расстоянии) вызывает `focusTargetOnMap` → `targetMapOverview=true` → карта
  разом масштабируется «дальше всей карты», затем возврат к фуре. Надо либо не сбрасывать
  обзор для активной цели, либо ограничить макс. дистанцию обзора (чтобы не «больше чем вся карта»).
  (После отвязки `loadCustomTargets` от таймера этот глюк, возможно, уже не связан с ним —
  проверить у пользователя.)
- [ ] Предупреждения компиляции `CS0414` (неиспользуемые поля ...) и `CS8600/8602`
  (nullability) в `MainForm.cs` — не критично, но можно почистить.

## Список задач (TODO)
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
- **[В ПЛАНЫ] Полный рефакторинг принципа создания/хранения/отрисовки точек целей.**
  Текущая архитектура разделила хранение на «файл `custom_targets.json`» + «в память
  (`state.customTargets` + глобальная `randomTarget`)», и периодическая `loadCustomTargets`
  (каждые 2с) пересобирает массив из файла, что приводит к потере/затиранию целей,
  созданных кнопками. В ранних версиях ВСЕ цели (включая созданные на лету) хранились
  в `custom_targets.json` — и это работало. Задача: вернуть единый надёжный источник
  истины (файл как источник) + консистентная отрисовка всех целей из него, без
  рассинхрона между файлом, `state.customTargets` и `randomTarget`. НЕ делать в текущей
  задаче — это отдельная крупная работа.
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
