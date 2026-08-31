// ================================================================
// ETS2 ASSIST — AR HUD (дополненная реальность) v65
// ================================================================
// ПРИНЦИП (уточнение пользователя 31.08.2026): точки СТАТИЧНЫ. Приложение
// рассылает ar_target ОДИН РАЗ — в тот же момент, когда шлёт map_overrides_data
// на миникарту (и повторно только по команде: сохранение/перемещение/новая цель).
// Оверлей ЗАПОМИНАЕТ координаты; проекция и отрисовка — на стороне оверлея (JS),
// плавно ~60 кадров/с (requestAnimationFrame).
//
// ПРОЕКЦИЯ (v66): ОРИЕНТАЦИЯ = КАМЕРА (голова водителя), НЕ кузов фуры:
//     yaw  = (heading + headYaw) * 2π (влево +, как на миникарте);
//     pitch = truck.pitch (тангаж кузова), + головной pitch (head.offset[4])
//     если TruckTel его отдаёт (иначе 0). ИЗМЕНЕНИЕ v65→v66: fwd/right вернулись
//     к знакам миникарты: fwd=(-sin,-cos), right=(cos,-sin) — в v65 знаки были
//     ошибочно инвертированы (метка показывала «сзади» при цели строго впереди),
//     а НЕ «инверсией миникарты», как предполагалось.
// ВЫСОТА: ВЕРТИКАЛЬНАЯ ПЛОСКОСТЬ (требование 31.08.2026 #2): метка «стоит» на
//     земле: groundY = target.y − targetGroundOffset (по умолчанию 0.5 м);
//     камера смотрит из placement[1] + eye (высота глаз над точкой placement).
//     При подъёме/опускании головы (pitch) метка уезжает по экрану, а не
//     «приклеена» к центру; при взгляде в сторону — остаётся на своей высоте.
// ЧВЕРТИ-ПОРЯДКА ГОЛОВЫ: head.offset[3]=yaw (доля) и head.offset[4]=pitch (доля),
//     оба * 2π (миникарта трактует так же, см. websocket.js/MINIMAP_ARCHITECTURE_1.0.19).
// ЦВЕТ: как в редакторе карт (по category / color, fallback по kind).
// РАЗМЕР/ПРОЗРАЧНОСТЬ (v68, пороги в CFG ниже; размеры -30% по требованию):
//     <= sizeNearDist (10 м)   : максимальный размер (maxSize=67×0.7≈47)
//     >= sizeFarDist (500 м)   : минимальный размер (minSize), метка видна
//     > sizeFarDist..fadeDist  : прозрачность растёт до 100% на fadeDist (1.5 км)
//     Перекрестье масштабируется тем же sa.size.
// МЕТКА (v68): КРУГЛАЯ (не ромб) — по требованию 31.08.2026.
// ТЕКСТ (v68): обводка тоже прозрачнеет (rgba умножает альфу txtAlpha) —
//     раньше stroke оставался чёрным при прозрачной заливке («текст чернеет»).
// ЦЕЛЬ ВНЕ ЭКРАНА (v70): точка и перекрестье НЕ рисуются — только указатель-стрелка
//     и название. Указатель инвертирован по горизонтали (фикс v70 — показывал в
//     обратную сторону).
// ЦЕЛЬ ПОЗАДИ: стрелка на НИЖНЕМ крае экрана.
// ПОДПИСИ (v70): верхний текст = ОТОБРАЖАЕМОЕ имя (realName), нижний мелкий =
//     системное имя (gameName).
// ВЫСОТА ТОЧЕК (v70): Y=0 = «координата не извлечена» (не уровень моря):
//     высота берётся у ближайшего города; 350..50 м — переход к высоте фуры;
//     <50 м — фиксация; при удалении — обратный переход (см. CFG.hCityDist/hLockDist).
// ПОМЕТКА В АР (v70): команда ar_pin (кнопка миникарты) — в редакторе создаётся
//     новая точка на пересечении взгляда с плоскостью высоты фуры; в АР — СЕРЫЙ
//     КРЕСТИК в этой точке независимо от остальных индикаторов.
// ПЛАВНОСТЬ (v66): экспоненциальная интерполяция экранной позиции/размера/
//     альфы ~0.25/кадр (~60fps) ПЛЮС экстраполяция угла камеры между пакетами
//     телеметрии (30 Гц → 60 fps рендера без «ступенек»).

(function () {
    'use strict';

    const canvas = document.getElementById('arCanvas');
    const ctx = canvas.getContext('2d');
    const statusEl = document.getElementById('arStatus');
    let W = 0, H = 0, dpr = 1;

    // ---------- НАСТРАИВАЕМЫЕ ПОРОГИ (отдельные переменные — по требованию) ----------
    const CFG = {
        fovDeg: 75,             // горизонтальный FOV (калибруется по фидбеку)
        edgeMargin: 70,         // отступ от края экрана, px
        labelDy: 46,            // подпись под перекрестьем, px
        wsUrl: 'ws://localhost:8084/',

        // РАЗМЕР/ПРОЗРАЧНОСТЬ МЕТКИ (v70: базовый размер вдвое меньше прежнего):
        minSize: 15,            // минимальный размер метки, px (всегда) [было 21]
        maxSize: 33,            // максимальный размер (вплотную), px [было 67 - вдвое]
        crosshairScale: 0.7,    // перекрестье масштабируется от sa.size (было 100%)
        sizeNearDist: 10,       // дистанция «вплотную» (size=maxSize), м
        sizeFarDist: 500,       // дальше — размер минимальный, м
        fadeDist: 1500,         // дальше — прозрачность растёт до 100% (м)

        // ВЫСОТА КАМЕРЫ/МЕТКИ (v66…v70):
        eyeHeight: 1.9,         // глаза над точкой placement, м (приближение кабины)
        groundOffset: 0.5,      // метка «стоит» на 0.5 м над указанной высотой цели
        smooth: 0.25,           // коэф. экспон. интерполяции позиции (плавность, 60fps)
        headPitchSign: 1,       // знак pitch головы (v67 эмпирика)

        // ВЫСОТА ТОЧКИ ПО ГОРОДУ (v70): Y=0 = «координаты нет».
        hCityDist: 350,
        hLockDist: 50,
        fps: 120,               // частота расчёта/перерисовки v70 (×2 от ~60)
        showFPS: true,
        // v74: компенсация высоты городов ПЕРЕНЕСЕНА на C# (в payload приходят уже
        // готовые города с поправкой −44 м — «приложение передаёт в АР уже
        // скомпенсированную высоту»). Оставляем 0 как калибровочную ручку.
        cityYCorrection: 0      // м, прибавляется к высоте всех городов (отриц. = ниже)
    };

    // Цвета как в РЕДАКТОРЕ КАРТ (MapEditorForm._poiPalette), по категории POI.
    const CATEGORY_COLORS = {
        'Company': '#ff78c8', 'BusStop': '#78dcff', 'Ferry': '#78ffb4',
        'Fuel': '#ffc850', 'Garage': '#b4a0ff', 'Overlay': '#c8c8c8',
        'Parking': '#ffa05a', 'Recruitment': '#ff7878', 'Service': '#78ffff',
        'Train': '#a0c8ff', 'TruckDealer': '#ffdc78', 'WeightStation': '#dcb4ff',
        'custom': '#ffffff'
    };
    // Города в редакторе жёлтые; случайные цели — цветом цели.
    const CITY_COLOR = '#ffff5c';
    const KIND_FALLBACK = { 'target': '#ff3b30', 'city': CITY_COLOR, 'poi': '#70d1fe' };

    function colorFor(kind, colorField, category) {
        if (typeof colorField === 'string' && colorField.startsWith('#')) return colorField; // #rrggbb
        if (category && CATEGORY_COLORS[category]) return CATEGORY_COLORS[category];
        if (typeof colorField === 'string' && CATEGORY_COLORS[colorField]) return CATEGORY_COLORS[colorField];
        if (kind === 'target') return KIND_FALLBACK.target;
        if (kind === 'city') return CITY_COLOR;
        return KIND_FALLBACK.poi;
    }

    // Состояние AR (консоль: window.__arHud)
    const ar = {
        camX: 0, camY: 0, camZ: 0,   // глаз (мир)
        yawBase: 0,                   // heading фуры (доля оборота)
        yawHead: 0,                   // yaw головы (рад, head.offset[3]*2π)
        pitchHead: 0,                 // pitch головы (рад, head.offset[4]*2π*sign)
        headPitchRaw: 0,              // raw head.offset[4] (доля) — для отладки/статуса
        pitch: 0, roll: 0,
        haveTruck: false,
        haveHead: false,
        haveHeadPitch: false,
        lastTelemetryAt: 0,
        telePrevAt: 0, teleDt: 33,
        target: null,                 // последняя ar_target {…, groundY}
        pin: null,                    // пометка «Пометить в АР» {x,y,z} (серый крестик)
        cities: [],                   // [{x,y,z}] — высоты для фиксации Y=0 точек.
                                      // Наполняется из ar_telemetry.cities (C#).
        sel: null                     // текущая экранная позиция перекрестья
    };
    window.__arHud = ar;

    // ================================================================
    // СТАТУС-СТРОКА
    // ================================================================
    function setStatus(kind, text) {
        statusEl.classList.remove('ok', 'warn', 'err');
        if (kind) statusEl.classList.add(kind);
        statusEl.textContent = text;
    }

    function statusFromState() {
        if (!ar.haveTruck) { setStatus('warn', 'AR: нет телеметрии от приложения'); return; }
        if (!ar.target) {
            // v73: если цель сброшена приложением с reason «нет точек в радиусе 1.5 км» —
            // показываем эту формулировку (не рисуем точку/дистанцию).
            setStatus('warn', 'AR: нет точек в радиусе 1.5 км');
            return;
        }
        const age = performance.now() - ar.lastTelemetryAt;
        const stale = age > 5000 ? ' (телеметрия ' + Math.round(age / 1000) + 'с назад — пауза?)' : '';
        setStatus('ok', 'AR: ' + (ar.target.realName || ar.target.gameName) + ' · ' +
            fmtDist(ar.target.dist) + ' | ' + (_fpsVal || '…') + ' fps | голова: ' +
            (ar.haveHead ? 'да' : 'нет') +
            ' (pitch ' + (ar.headPitchRaw * 360).toFixed(1) + '°)' + stale);
    }
    setInterval(statusFromState, 1000);

    function fmtDist(d) {
        if (!Number.isFinite(d)) return '—';
        return d < 1000 ? Math.round(d) + ' м' : (d / 1000).toFixed(2) + ' км';
    }

    // ================================================================
    // КОМАНДНЫЙ WS (8084) — приём пакетов от приложения
    // ================================================================
    let ws = null;
    function connect() {
        if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
        try {
            ws = new WebSocket(CFG.wsUrl);
            ws.onopen = function () { setStatus('ok', 'AR: подключено к приложению'); };
            ws.onmessage = function (ev) {
                let data = null;
                try { data = JSON.parse(ev.data); } catch (e) { return; }
                if (!data || !data.command) return;
                if (data.command === 'ar_target') applyArTarget(data);
                else if (data.command === 'ar_telemetry') applyArTelemetry(data);
                else if (data.command === 'ar_pin') applyArPin(data);
                else if (data.command === 'ar_pin') applyArPin(data);
            };
            ws.onclose = function () { ws = null; setTimeout(connect, 2000); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) {
            setTimeout(connect, 2000);
        }
    }

    // ---- Приём: цель (приложение шлёт РАЗОВО при смене выбранной точки) ----
    function applyArTarget(data) {
        if (data.hasTarget === true) {
            const ty = Number(data.y) || 0;
            ar.target = {
                gameName: String(data.gameName || 'target'),
                realName: String(data.realName || ''),
                x: Number(data.x) || 0,
                y: ty,
                // Ground-якорь: POI/города приходят с y=0, но «стоят» на земле:
                // считаем от неё, не от нуля мира (фикс «приклеена к камере по Y»).
                groundY: ty + CFG.groundOffset,
                z: Number(data.z) || 0,
                dist: Number(data.dist) || 0,
                kind: String(data.kind || 'poi'),
                category: data.category ? String(data.category) : '',
                color: data.color ? String(data.color) : ''
            };
            ar.targetAt = performance.now();
        } else {
            ar.target = null; // приложение сказало: точек в радиусе нет / нет телеметрии
        }
        statusFromState();
    }

    // ---- Приём: телеметрия (placement + голова) от приложения ----
    // КАМЕРА = глаз над точкой placement (не «низ фуры»): метка тогда корректно
    // уходит вниз экрана, когда подъезжаем, и стоит на земле при подъёме головы.
    function applyArTelemetry(data) {
        const p = data.placement;
        if (Array.isArray(p) && p.length >= 6) {
            ar.camX = Number(p[0]) || 0;
            ar.camY = (Number(p[1]) || 0) + CFG.eyeHeight;  // глаз, не колёса
            ar.camZ = Number(p[2]) || 0;
            ar.yawBase = Number(p[3]) || 0;
            ar.pitch = Number(p[4]) || 0;
            ar.roll = Number(p[5]) || 0;
            ar.haveTruck = true;
            ar.telePrev = {                          // для экстраполяции в рендере
                camX: ar.camX, camY: ar.camY, camZ: ar.camZ, yawBase: ar.yawBase
            };
            ar.lastTelemetryAt = performance.now();
            if (ar.telePrevAt) ar.teleDt = Math.max(1, Math.min(500, ar.lastTelemetryAt - ar.telePrevAt));
            ar.telePrevAt = ar.lastTelemetryAt;
        }
        const h = data.head;
        if (Array.isArray(h) && h.length >= 4) {
            ar.yawHead = (Number(h[3]) || 0) * Math.PI * 2;
            if (h.length >= 5) {
                // Вертикальный наклон головы: head.offset[4], доля оборота
                // (как yaw: 0.25 = 90°; пример пользователя: -0.0357 ≈ -12.9°).
                ar.headPitchRaw = Number(h[4]) || 0;
                ar.pitchHead = ar.headPitchRaw * Math.PI * 2 * CFG.headPitchSign;
                ar.haveHeadPitch = true;
            }
            ar.haveHead = true;
        }
        // Высоты ближайших городов (v70 — фиксация Y=0 точек):
        if (Array.isArray(data.cities)) {
            ar.cities = data.cities.map(c => ({
                x: Number(c.x) || 0,
                y: Number(c.y) || 0,
                z: Number(c.z) || 0
            }));
        }
    }

    // ---- Приём: пометка «Пометить в АР» (серый крестик + точка в редакторе) ----
    function applyArPin(data) {
        if (data.active === true && data.x !== undefined) {
            ar.pin = {
                x: Number(data.x) || 0,
                y: Number(data.y) || 0,      // высота = высота грузовика (считает C#)
                z: Number(data.z) || 0
            };
        } else {
            ar.pin = null;                   // ОТМЕНА в редакторе — крестик снят
        }
        statusFromState();
    }

    connect();

    // ================================================================
    // ПРОЕКЦИЯ ТОЧКИ (v66): камера = голова; знаки — как на миникарте
    // ================================================================
    // Система координат миникарты (эталон, ui.js/map_draw.js):
    //   «вперёд»  = (-sin y, -cos y), «вправо» = ( cos y, -sin y), y = yaw (рад).
    //   Экран: u вправо (+right), v вниз; heading растёт ПРОТИВ часовой (влево +).
    // Вертикаль: camY = placement[1] + eyeHeight (глаза); метка стоит на groundY.
    //   pitch (кузов + голова) вращает ЛУЧ ВЗГЛЯДА вокруг оси right — при взгляде
    //   вверх/вниз метка уезжает по экрану (не «приклеена» к камере).
    // Высота отображения точки (v70): Y=0 = «не извлечена».
    // >hCityDist (350м): высота ближайшего города; hCityDist..hLockDist: лерп
    // к высоте грузовика; <hLockDist (50м): высота ЗАПОМИНАЕТСЯ (lock) и не меняется,
    // при удалении — плавный обратный переход к высоте города.
    // Высота отображения точки (v70/v72): Y=0 = «не извлечена».
    //   >hCityDist (350м): высота ближайшего города (с поправкой cityYCorrection);
    //   hCityDist..hLockDist — плавный переход К ВЫСОТЕ ГРУЗОВИКА (фура ниже/выше —
    //   метка тянется к ней, требование 31.08.2026);
    //   <hLockDist (50м) — высота ЗАФИКСИРОВАНА (не скачет), при удалении — обратно.
    // v72 ФИКС РЫВКА: убран замкнутый контур (lerp к самому себе) — теперь высота
    //   это ЧИСТАЯ ФУНКЦИЯ дистанции + низкочастотный сглаживающий фильтр (lerp 0.08)
    //   на выходе; при захвате <50м фильтр тянется к текущему значению (truckY),
    //   без прыжков от обновления списков.
    const _ySmooth = new Map();             // gameName -> сглаженная высота
    function nearestCityY(x, z) {
        let best = null, bd = Infinity;
        for (const c of ar.cities) {
            const d2 = (c.x - x) * (c.x - x) + (c.z - z) * (c.z - z);
            if (d2 < bd) { bd = d2; best = c; }
        }
        // cityYCorrection (м): города систематически выше реальной земли.
        return best ? { y: best.y + CFG.cityYCorrection, dist: Math.sqrt(bd) } : null;
    }
    function displayYFor(t, dist2d, truckY) {
        // Явная ненулевая высота — используется как есть (город/цель с реальной Y).
        if (t.y && Math.abs(t.y) > 0.001) return t.y + CFG.groundOffset;
        // Y=0 — компенсация: высота ближайшего города (с поправкой).
        // НАЗНАЧЕНИЕ kind==='city' (v73 фидбек): высота выбранного ГОРОДА тоже
        // компенсируется — он рисуется на cityY (та же логика, что и у всех точек).
        const cy = nearestCityY(t.x, t.z);
        const cityY = cy ? cy.y : truckY;
        // Целевая высота по дистанции: город → переход → грузовик.
        let targetY;
        if (dist2d >= CFG.hCityDist) {
            targetY = cityY;                                             // далеко: город
        } else {
            const k = Math.min(1, Math.max(0,
                1 - (dist2d - CFG.hLockDist) / (CFG.hCityDist - CFG.hLockDist))); // 0..1
            // При приближении высота тянется к ВЫСОТЕ ГРУЗОВИКА (k → 1) —
            // как требует пользователь; дальше hLockDist не меняется (см. ниже).
            targetY = cityY + (truckY - cityY) * k;
        }
        // Низкочастотный фильтр на выходе (нет рывка от обновления телеметрии).
        const prev = _ySmooth.get(t.gameName);
        let y = prev === undefined ? targetY : prev + (targetY - prev) * 0.08;
        // ЗАХВАТ (<hLockDist): «запоминаем» и дальше не меняем (пока рядом).
        if (dist2d < CFG.hLockDist) {
            if (prev === undefined) _ySmooth.set(t.gameName, y);
            else { y = prev + (truckY - prev) * 0.08; _ySmooth.set(t.gameName, y); }
        } else {
            _ySmooth.set(t.gameName, y);
        }
        return y;
    }

    function projectPoint(pt, cam) {
        const c = cam || ar;
        // ВЕРТИКАЛЬ v75 (требование 31.08.2026): КОМПОЗИТНЫЙ ПИТЧ.
        //   Питч кузова применяется к ЛУЧУ (fdot, up-компоненты) вокруг «right»,
        //   затем сверху добавляется питч головы (тот же приём): это 3D-повороты —
        //   эффект кузова автоматически ослабевает при взгляде на борт и
        //   ИНВЕРТИРУЕТСЯ при взгляде назад (>90°), как просил пользователь.
        const wy = (pt.dispY !== undefined ? pt.dispY : pt.y) - c.camY;
        const dist = Math.sqrt((pt.x - c.camX) ** 2 + wy * wy + (pt.z - c.camZ) ** 2);

        const yaw = c.yawBase * Math.PI * 2 + c.yawHead;
        const sinY = Math.sin(yaw), cosY = Math.cos(yaw);
        const fwdX = -sinY,  fwdZ = -cosY;   // как на миникарте (v66)
        const rightX = cosY, rightZ = -sinY;

        const fdot0 = (pt.x - c.camX) * fwdX + (pt.z - c.camZ) * fwdZ;
        const rdot = (pt.x - c.camX) * rightX + (pt.z - c.camZ) * rightZ;

        // 1) ПИТЧ КУЗОВА (поворот луча вокруг right):
        const bodyPitch = (c.pitch || 0) * Math.PI * 2;
        const cosB = Math.cos(bodyPitch), sinB = Math.sin(bodyPitch);
        let fwd1 = fdot0 * cosB + wy * sinB;
        let   up1 = wy * cosB - fdot0 * sinB;

        // 2) ПИТЧ ГОЛОВЫ (добавляется к кузову, та же ось right):
        const headPitch = c.pitchHead || 0;
        const cosH = Math.cos(headPitch), sinH = Math.sin(headPitch);
        const depth = fwd1 * cosH + up1 * sinH;
        const up    = up1 * cosH - fwd1 * sinH;

        const halfTan = Math.tan((CFG.fovDeg * Math.PI / 180) / 2);
        const f = (W * 0.5) / halfTan;

        let u, v;
        if (depth > 0.5) {
            u = W / 2 + f * (rdot / depth);
            v = H / 2 - f * (up / depth);
        } else {
            // Точка позади: направление к краю по знакам компонент.
            u = (rdot >= 0) ? Infinity : -Infinity;
            v = (up >= 0) ? -Infinity : Infinity;
        }
        return { dist, u, v, inFront: fdot0 > 0, depth };
    }

    // Кратчайшая разница углов в долях оборота (для экстраполяции yawBase).
    function shortAngleDiff(a, b) {
        let d = a - b;
        if (d > 0.5) d -= 1;
        if (d < -0.5) d += 1;
        return d;
    }

    // Прижим к рамке [m..W-m]×[m..H-m] вдоль луча из центра.
    // ЦЕЛЬ ПОЗАДИ (behind=true) — стрелка уходит на НИЖНИЙ край (требование 31.08.2026),
    // x-позиция — по горизонтали направления (влево/вправо/центр).
    function clampToScreen(u, v, behind) {
        const m = CFG.edgeMargin;
        const cx = W / 2, cy = H / 2;

        if (behind) {
            const su = Number.isFinite(u) ? Math.sign(u - cx || 1) : (u > 0 ? 1 : -1);
            return { u: cx + su * (W / 2 - m), v: H - m, clamped: true, bottom: true };
        }

        const du = u - cx, dv = v - cy;
        if (Number.isFinite(u) && Number.isFinite(v)) {
            if (u >= m && u <= W - m && v >= m && v <= H - m) {
                return { u, v, clamped: false, bottom: false };
            }
        }
        let t = Infinity;
        if (du > 0) t = Math.min(t, (W - m - cx) / du);
        else if (du < 0) t = Math.min(t, (m - cx) / du);
        if (dv > 0) t = Math.min(t, (H - m - cy) / dv);
        else if (dv < 0) t = Math.min(t, (m - cy) / dv);
        if (!Number.isFinite(t) || t <= 0) {
            const cu = Math.min(Math.max(u, m), W - m);
            const cv = Math.min(Math.max(v, m), H - m);
            return { u: cu, v: cv, clamped: true, bottom: cv >= (H - m) };
        }
        const ru = cx + du * t, rv = cy + dv * t;
        return { u: ru, v: rv, clamped: true, bottom: rv >= (H - m - 2) };
    }

    // ================================================================
    // РАЗМЕР / ПРОЗРАЧНОСТЬ ПО ДИСТАНЦИИ (пороги — в CFG)
    // ================================================================
    function sizeAlphaFor(dist) {
        const d = Math.max(0, dist);
        // Размер: <=sizeNearDist -> maxSize; >=sizeFarDist -> minSize; линейно между.
        let size;
        if (d <= CFG.sizeNearDist) size = CFG.maxSize;
        else if (d >= CFG.sizeFarDist) size = CFG.minSize;
        else {
            size = CFG.maxSize + (CFG.minSize - CFG.maxSize) *
                   ((d - CFG.sizeNearDist) / (CFG.sizeFarDist - CFG.sizeNearDist));
        }
        // Прозрачность: в пределах sizeFarDist — непрозрачно; затем до 0 на fadeDist.
        let alpha = 1;
        if (d > CFG.sizeFarDist) {
            const t = Math.min(1, (d - CFG.sizeFarDist) / Math.max(1, CFG.fadeDist - CFG.sizeFarDist));
            alpha = 1 - t;   // 1 → 0 (0 = полностью прозрачна)
        }
        return { size, alpha };
    }

    // ================================================================
    // ОТРИСОВКА
    // ================================================================
    function resize() {
        dpr = window.devicePixelRatio || 1;
        W = window.innerWidth; H = window.innerHeight;
        canvas.width = Math.max(1, Math.round(W * dpr));
        canvas.height = Math.max(1, Math.round(H * dpr));
        canvas.style.width = W + 'px';
        canvas.style.height = H + 'px';
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
    window.addEventListener('resize', resize);
    resize();

    function drawOutlinedText(text, x, y, font, color, alpha) {
        // v68: alpha применяем И к обводке (раньше stroke был фиксированно чёрный
        // 0.85 — текст «чернел» при прозрачной заливке).
        const a = (typeof alpha === 'number') ? Math.max(0, Math.min(1, alpha)) : 1;
        ctx.font = font;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(0,0,0,' + (0.85 * a).toFixed(3) + ')';
        ctx.strokeText(text, x, y);
        ctx.fillStyle = color || '#ffffff';
        ctx.fillText(text, x, y);
    }

    function drawCrosshair(u, v, color, scale) {
        // v68: перекрестье масштабируется тем же коэффициентом, что и метка
        // (максимум на 30% меньше прежнего по требованию).
        const k = (typeof scale === 'number') ? Math.max(0.4, Math.min(1.5, scale)) : 1;
        const S = 26 * k, G = 7 * k;
        ctx.save();
        for (const pair of [[6, 'rgba(0,0,0,0.85)'], [3, color]]) {
            ctx.lineWidth = pair[0];
            ctx.strokeStyle = pair[1];
            ctx.beginPath();
            ctx.moveTo(u - G - S, v); ctx.lineTo(u - G, v);
            ctx.moveTo(u + G, v); ctx.lineTo(u + G + S, v);
            ctx.moveTo(u, v - G - S); ctx.lineTo(u, v - G);
            ctx.moveTo(u, v + G); ctx.lineTo(u, v + G + S);
            ctx.stroke();
        }
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(u, v, 2.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    // Метка цели — КРУГ (v68, требование 31.08.2026), размер/прозрачность по дистанции.
    let _markerSize = CFG.minSize;
    function drawMarkerToward(u, v, color, targetSize, alpha) {
        _markerSize += (targetSize - _markerSize) * 0.15;
        const size = Math.max(CFG.minSize, _markerSize);
        const r = size / 2;
        ctx.save();
        ctx.globalAlpha = Math.max(0.12, Math.min(1, alpha));  // не исчезает полностью
        ctx.beginPath();
        ctx.arc(u, v, r, 0, Math.PI * 2);
        ctx.fillStyle = color;
        ctx.shadowColor = 'rgba(0,0,0,0.8)';
        ctx.shadowBlur = 6;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.lineWidth = 2;
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(u, v, Math.max(2, size * 0.1), 0, Math.PI * 2);
        ctx.fillStyle = '#000000';
        ctx.fill();
        ctx.restore();
    }

    function drawEdgeArrow(u, v, color, fromBottom) {
        const cx = W / 2, cy = H / 2;
        // Стрелка УКАЗЫВАЕТ в СТОРОНУ ЦЕЛИ (v70: инверсия по горизонтали исправлена —
        // раньше показывала в обратную сторону).
        const ang = fromBottom ? (-Math.PI / 2) : Math.atan2(v - cy, -(u - cx));
        const ax = u, ay = fromBottom ? v - 34 : v - 26;
        ctx.save();
        ctx.translate(ax, ay);
        ctx.rotate(ang + Math.PI);
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(10, 0); ctx.lineTo(-6, -7); ctx.lineTo(-6, 7);
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = 'rgba(0,0,0,0.85)';
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.restore();
    }

    // ================================================================
    // ГЛАВНЫЙ ЦИКЛ (v70: 120 расчётов/с — rAF с двойным шагом) — ИНТЕРПОЛЯЦИЯ
    // ================================================================
    // requestAnimationFrame синхронизирован с монитором (обычно 60 Гц), поэтому
    // частоту РАСЧЁТА удваиваем: на каждый rAF выполняем два шага сглаживания
    // с полшагом (эффективно ~120 Гц лерпа) + экстраполяция камеры остаётся.
    let _sm = null;                       // сглаженное состояние
    let _fpsCnt = 0, _fpsAt = performance.now(), _fpsVal = 0;

    function render() {
        requestAnimationFrame(render);
        if (W !== window.innerWidth || H !== window.innerHeight) resize();
        ctx.clearRect(0, 0, W, H);
        // v73: ПРИЦЕЛЬНЫЙ КУРСОР — рисуем ВСЕГДА (не зависит от телеметрии/цели):
        // «Даже если ближайших точек нет, мы всё равно отрисовываем метки новых точек».
        ctx.fillStyle = 'rgba(255,255,255,0.45)';
        ctx.fillRect(W / 2 - 1, H / 2 - 1, 1.5, 1.5);
        _fpsCnt++;
        const fNow = performance.now();
        if (fNow - _fpsAt >= 1000) { _fpsVal = _fpsCnt; _fpsCnt = 0; _fpsAt = fNow; }

        // ---- Экстраполяция КАМЕРЫ (позиция + yaw) между пакетами телеметрии ----
        const nowT = performance.now();
        const age = Math.min(nowT - (ar.telePrevAt || nowT), 500);
        const dt = ar.teleDt || 33;
        let exCam = ar;
        if (age > 2 && ar.telePrev) {
            const k = age / dt;              // доля «прошедшего» интервала
            const kC = Math.min(k, 1.5);
            exCam = {
                camX: ar.camX + (ar.camX - ar.telePrev.camX) * kC,
                camY: ar.camY + (ar.camY - ar.telePrev.camY) * kC,
                camZ: ar.camZ + (ar.camZ - ar.telePrev.camZ) * kC,
                yawBase: ar.yawBase + shortAngleDiff(ar.yawBase, ar.telePrev.yawBase) * kC,
                pitch: ar.pitch, roll: ar.roll,
                yawHead: ar.yawHead, pitchHead: ar.pitchHead
            };
        }

        // v73 фидбек: ПОМЕТКА (pin) — НЕЗАВИСИМО от цели/радиуса 1.5 км: рисуем всегда,
        // когда есть телеметрия и pin установлен.
        if (ar.haveTruck && ar.pin) {
            const pPr = projectPoint({ x: ar.pin.x, y: ar.pin.y, z: ar.pin.z }, exCam);
            if (pPr.inFront) {
                let pu = Number.isFinite(pPr.u) ? pPr.u : (pPr.u > 0 ? W - CFG.edgeMargin : CFG.edgeMargin);
                let pv = Number.isFinite(pPr.v) ? pPr.v : (pPr.v > 0 ? H - CFG.edgeMargin : CFG.edgeMargin);
                pu = Math.min(Math.max(pu, CFG.edgeMargin), W - CFG.edgeMargin);
                pv = Math.min(Math.max(pv, CFG.edgeMargin), H - CFG.edgeMargin);
                ctx.save();
                ctx.strokeStyle = 'rgba(225,225,225,0.95)';
                ctx.fillStyle = 'rgba(160,160,160,0.95)';
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.arc(pu, pv, 6, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();
                ctx.beginPath();
                ctx.moveTo(pu - 13, pv); ctx.lineTo(pu + 13, pv);
                ctx.moveTo(pu, pv - 13); ctx.lineTo(pu, pv + 13);
                ctx.stroke();
                ctx.restore();
                const pinDist = fmtDist(pPr.dist);
                drawOutlinedText('Новая точка  \u00B7  ' + pinDist,
                    pu, pv + 18, '600 13px "Segoe UI", Arial',
                    'rgba(220,220,220,0.95)', 0.95);
            }
        }

        if (!ar.haveTruck || !ar.target) return;   // цель нет — дальше рисовать нечего

        const pr = projectPoint(ar.target, exCam);
        // Infinity — точка ровно сзади/сбоку: фиксируем направление к крайним значениям.
        if (!Number.isFinite(pr.u)) pr.u = pr.u > 0 ? (W - CFG.edgeMargin) : CFG.edgeMargin;
        if (!Number.isFinite(pr.v)) pr.v = pr.v > 0 ? (H - CFG.edgeMargin) : CFG.edgeMargin;
        const cl = clampToScreen(pr.u, pr.v, !pr.inFront);

        // ---- ВЫСОТА ПО ПОРЯДКУ v70: город → переход → фиксация ----
        const dist2d = Math.sqrt((ar.target.x - exCam.camX) ** 2 + (ar.target.z - exCam.camZ) ** 2);
        ar.target.dispY = displayYFor(ar.target, dist2d, ar.camY - CFG.eyeHeight);

        // ---- Экспоненциальное сглаживание экранной позиции (главный фикс «ряби») ----
        // При смене ЦЕЛИ (identity) — прыжок мгновенно, без «перелёта» через экран.
        // v70: частота ×2 — двойной шаг сглаживания за rAF с полшагом.
        const ident = ar.target.gameName + '|' + ar.target.x.toFixed(1) + ',' + ar.target.z.toFixed(1);
        if (!_sm || _sm.ident !== ident) {
            _sm = { ident, u: cl.u, v: cl.v, clamped: cl.clamped, bottom: cl.bottom };
        } else {
            const s = 1 - Math.pow(1 - CFG.smooth, 2);   // эффективный шаг за 2 подшага
            if (!_sm.clamped && !cl.clamped) {
                _sm.u += (cl.u - _sm.u) * s;
                _sm.v += (cl.v - _sm.v) * s;
            } else {
                // У края/за спиной: позиция определяется направлением — без накопления лага.
                _sm.u = cl.u; _sm.v = cl.v;
            }
            _sm.clamped = cl.clamped; _sm.bottom = cl.bottom;
        }
        ar.sel = { u: _sm.u, v: _sm.v, clamped: _sm.clamped, inFront: pr.inFront };

        // Цвет как в редакторе (category/color/kind), размер/альфа по дистанции.
        const color = colorFor(ar.target.kind, ar.target.color, ar.target.category);
        const sa = sizeAlphaFor(pr.dist);
        const txtAlpha = Math.max(0, Math.min(1, sa.alpha));

        // v73: ЗАТУХАНИЕ КРИСТИКА/СТРЕЛКИ — globalAlpha всей отрисовки точки/указателя
        // по дистанции (требование: прозрачность действует и на стрелку за экраном).
        // При alpha ≤ 0.03 ничего не рисуем (дистанция ~fadeDist).
        if (txtAlpha <= 0.03) return;
        ctx.save();
        ctx.globalAlpha = txtAlpha;

        // ПОДПИСИ (v70): верхний текст = ОТОБРАЖАЕМОЕ имя (realName),
        // нижний мелкий = системное имя (gameName). Текст рисуется и когда цель
        // вне экрана (рядом со стрелкой), и когда внутри — под точкой.
        const distText = fmtDist(pr.dist);
        if (_sm.clamped) {
            // ЦЕЛЬ ВНЕ ЭКРАНА: точку и перекрестье НЕ рисуем — только указатель+текст.
            const lu = _sm.u, lv = _sm.bottom ? (_sm.v - 58) : (_sm.v + CFG.labelDy);
            drawOutlinedText((ar.target.realName || ar.target.gameName) + '  \u00B7  ' + distText,
                lu, lv, '600 14px "Segoe UI", Arial',
                'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
            if (ar.target.gameName && ar.target.gameName !== (ar.target.realName || ar.target.gameName)) {
                drawOutlinedText(ar.target.gameName,
                    lu, lv + 19, '12px "Segoe UI", Arial',
                    'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
            }
            drawEdgeArrow(_sm.u, _sm.v, color, _sm.bottom === true);
        } else {
            drawMarkerToward(_sm.u, _sm.v, color, sa.size, txtAlpha);
            drawOutlinedText((ar.target.realName || ar.target.gameName) + '  \u00B7  ' + distText,
                _sm.u, _sm.v + CFG.labelDy, '600 14px "Segoe UI", Arial',
                'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
            if (ar.target.gameName !== (ar.target.realName || ar.target.gameName)) {
                drawOutlinedText(ar.target.gameName,
                    _sm.u, _sm.v + CFG.labelDy + 19, '12px "Segoe UI", Arial',
                    'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
            }
            drawCrosshair(_sm.u, _sm.v, color, (sa.size / 33) * CFG.crosshairScale);
        }
        ctx.restore();   // конец блока globalAlpha=txtAlpha (для метки/стрелки/крестика)
        // (v74: pin и прицельный курсор перенесены ВЫШЕ — рисуются ДО цели и
        //  не зависят от наличия ar.target.)
    }
    requestAnimationFrame(render);
})();