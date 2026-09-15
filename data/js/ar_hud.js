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

        // v1.0.40.30: ВСЕ camera-хаки УДАЛЕНЫ (ETS2_AR_CAMERA_POSE_IMPLEMENTATION).
        // Убрано: eyeHeight, groundOffset, smooth, headPitchSign, pinSmooth, pinLead,
        // cityYCorrection, hCityDist/hLockDist, eyeHeightM, camFallback/headFallback,
        // camForwardM/camRightM, showViewCone (конус в AR1 не рисуется).
        // Камера приходит ГОТОВОЙ 6DoF-позой (position + forward/right/up).

        // ЛИНИЯ МИРОВОГО ГОРИЗОНТА (строится из базиса камеры) + ДИСТАНЦИЯ ДО ЗЕМЛИ.
        showHorizon: true,
        horizonColor: 'rgba(120,220,255,0.75)',
        horizonPixels: 1.5,     // толщина линии, px
        diagCrossColor: 'rgba(170,170,170,0.95)',
        diagCrossSize: 7,       // полуразмер СЕРЫЙ ДИАГОНАЛЬНЫЙ микрокрестик, px
        showGroundDistText: true,
        groundDistFont: '600 12px Consolas, monospace',
        groundDistColor: 'rgba(255,255,255,0.95)',
        groundDistDy: 12,       // отступ текста дистанции от центра экрана, px

        // ВЫВОД ТЕКУЩЕГО FOV в ЛЕВОМ НИЖНЕМ УГЛУ (требование пользователя).
        showFovText: true,
        fovFont: '700 14px Consolas, monospace',
        fovColor: 'rgba(255,255,255,0.95)',
        fovMarginX: 16,         // отступ от левого края, px
        fovMarginY: 14,         // отступ от нижнего края, px
        showDebugDot: true,     // отметка позы камеры в режиме debugShow

        // Рамка отсечения рендера — теперь ТОЛЬКО диагностическая (под debugShow).
        showClipFrame: true,

        fps: 120,               // частота расчёта/перерисовки (×2 от ~60)
        showFPS: true
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
    // v1.0.40.30: камера — ПОЛНАЯ 6DoF-поза из приложения (position + базис).
    // Никаких eyeHeight/pitch-складываний: проекция идёт только через базис.
    const ar = {
        camX: 0, camY: 0, camZ: 0,        // МИРОВЫЕ координаты ГЛАЗА/КАМЕРЫ

        cameraForward: { x: 0, y: 0, z: -1 },
        cameraRight:   { x: 1, y: 0, z: 0 },
        cameraUp:      { x: 0, y: 1, z: 0 },
        cameraValid: false,

        // Legacy-поля (только диагностика/статус-строка).
        truckX: 0, truckY: 0, truckZ: 0,
        yawBase: 0,
        yawHead: 0,
        pitchHead: 0,
        headPitchRaw: 0,
        pitch: 0, roll: 0,
        haveTruck: false,
        haveHead: false,
        haveHeadPitch: false,
        fovDeg: 100,                  // FOV приходит в позе камеры (camera.fovDeg)
        projectionCenterX: 0.5,
        projectionCenterY: 0.5,
        lastTelemetryAt: 0,
        target: null,                 // последняя ar_target {…, groundY}
        pin: null,                    // пометка / новая точка {x,y,z} (крестик)
        cities: [],                   // [{x,y,z}] — ТОЛЬКО совместимость/диагностика
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

    // ================================================================
    // v1.0.40.28: ЕДИНЫЙ DEBUG-РЕЖИМ ДЛЯ ВСЕГО ВЕБ-КОНТЕНТА ОВЕРЛЕЯ.
    // debugShow(true)  — ИГНОРИРОВАТЬ логику показа: контент принудительно виден
    //                    (для отладки и настройки), плюс отладочные отметки.
    // debugShow(false) — вернуть исходную логику (как было).
    // Метод единый по имени во ВСЕХ страницах data/*.html; приложение может
    // вызвать его через debugShow(true)/debugShow(false) в консоли страницы.
    // Реализация НЕ меняет саму логику: ставится «залипающий» флаг, а функции
    // показа/скрытия проверяют его в первую очередь.
    // ================================================================
    const DEBUG = { show: false };
    window.debugShow = function (on) {
        DEBUG.show = (on === true);
        try { document.documentElement.dataset.debugShow = DEBUG.show ? '1' : '0'; } catch (e) {}
        // AR1: принудительный показ статус-строки и отладочной рамки/отметок.
        if (statusEl) statusEl.style.display = '';
        if (DEBUG.show) {
            setStatus('ok', 'AR: DEBUG (принудительный показ) · FOV ' +
                CFG.fovDeg.toFixed(0) + '° · cam ' + ar.camSource);
        } else {
            statusFromState();
        }
        return DEBUG.show;
    };

    function statusFromState() {
        if (!ar.cameraValid) { setStatus('warn', 'AR: нет телеметрии от приложения'); return; }
        if (!ar.target) {
            // Цель сброшена приложением (нет точек в радиусе) — показываем это.
            setStatus('warn', 'AR: нет точек в радиусе 1.5 км');
            return;
        }
        const age = performance.now() - ar.lastTelemetryAt;
        const stale = age > 5000 ? ' (телеметрия ' + Math.round(age / 1000) + 'с назад — пауза?)' : '';
        setStatus('ok', 'AR: ' + (ar.target.realName || ar.target.gameName) + ' · ' +
            fmtDist(ar.target.dist) + ' | ' + (_fpsVal || '…') + ' fps | FOV ' +
            ar.fovDeg.toFixed(0) + '°' + stale);
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
                else if (data.command === 'ar_fov') applyArFov(data);
                else if (data.command === 'debug_show') debugShow(data.enabled === true);
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
                // v1.0.40.30: высота точки — КАК ЕСТЬ (никаких groundY-якорей и
                // «подъёма» через ближайший город: это ломало геометрию).
                y: ty,
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

    // ---- Приём: телеметрия ----
    // v1.0.40.30: камера приходит ГОТОВОЙ 6DoF-позой (camera.position/forward/
    // right/up), посчитанной приложением по SCS hierarchy. Страница НЕ собирает
    // позу из углов — она только применяет её.
    function applyArTelemetry(data) {
        const camera = data.camera;

        if (camera && typeof camera === 'object') {
            const p = readVec3(camera.position, null);
            const fwd = readVec3(camera.forward, null);
            const right = readVec3(camera.right, null);
            const up = readVec3(camera.up, null);

            if (p && fwd && right && up) {
                ar.camX = p.x;
                ar.camY = p.y;
                ar.camZ = p.z;

                ar.cameraForward = fwd;
                ar.cameraRight = right;
                ar.cameraUp = up;
                ar.cameraValid = true;

                const fov = Number(camera.fovDeg);
                if (Number.isFinite(fov)) ar.fovDeg = Math.max(10, Math.min(170, fov));

                ar.haveTruck = true;
                ar.lastTelemetryAt = performance.now();
            }
        }

        // Legacy-поля — ТОЛЬКО для статус-строки/диагностики (проекция их не читает).
        const p = data.placement;
        if (Array.isArray(p) && p.length >= 6) {
            ar.truckX = Number(p[0]) || 0;
            ar.truckY = Number(p[1]) || 0;
            ar.truckZ = Number(p[2]) || 0;
            ar.yawBase = Number(p[3]) || 0;
            ar.pitch = Number(p[4]) || 0;
            ar.roll = Number(p[5]) || 0;
        }

        const h = data.head;
        if (Array.isArray(h) && h.length >= 4) {
            ar.yawHead = (Number(h[3]) || 0) * Math.PI * 2;
            if (h.length >= 5) {
                // head.offset[4] — доля оборота (для статуса: «pitch N°»).
                ar.headPitchRaw = Number(h[4]) || 0;
                ar.pitchHead = ar.headPitchRaw * Math.PI * 2;
                ar.haveHeadPitch = true;
            }
            ar.haveHead = true;
        }

        // Города — только совместимость (в проекции НЕ участвуют).
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

    // ---- Приём: FOV (CTRL+PGUP/PGDN в приложении, шаг 1°) ----
    // v1.0.40.30: FOV — часть ПОЗЫ КАМЕРЫ (приходит в camera.fovDeg и в команде
    // ar_fov одинаковым значением). Храним отдельно в ar.fovDeg/CFG.fovDeg для
    // отрисовки подписи и проекции (оба читают одно число).
    function applyArFov(data) {
        const f = Number(data.fov);
        if (Number.isFinite(f) && f >= 30 && f <= 150) {
            CFG.fovDeg = f;
            ar.fovDeg = f;
            statusFromState();
        }
    }

    connect();

    // ================================================================
    // ПРОЕКЦИЯ ТОЧКИ (v1.0.40.30): ТОЛЬКО через мировую 6DoF-позу камеры.
    // ================================================================
    // Прежняя математика (yaw кузова+головы, складывание питчей, eyeHeight,
    // зеркала и сглаживание) УДАЛЕНА — она давала инверсию по горизонтали и
    // «не держала» горизонт. Теперь: world − camera.position → проекции на
    // Forward/Right/Up (готовый базис из приложения) → pinhole FOV → пиксели.
    // ================================================================
    function readVec3(a, fallback) {
        if (!Array.isArray(a) || a.length < 3) return fallback;
        const x = Number(a[0]), y = Number(a[1]), z = Number(a[2]);
        if (![x, y, z].every(Number.isFinite)) return fallback;
        return { x, y, z };
    }

    function projectPoint(pt, cam) {
        const c = cam || ar;

        if (!c.cameraValid) {
            return { dist: Infinity, u: 0, v: 0, inFront: false, depth: -Infinity };
        }

        // pt.y используется КАК ЕСТЬ — никакой displayYFor-подмены.
        const dx = Number(pt.x) - c.camX;
        const dy = Number(pt.y) - c.camY;
        const dz = Number(pt.z) - c.camZ;

        const fwd = c.cameraForward;
        const right = c.cameraRight;
        const up = c.cameraUp;

        const depth = dx * fwd.x + dy * fwd.y + dz * fwd.z;
        const rdot = dx * right.x + dy * right.y + dz * right.z;
        const udot = dx * up.x + dy * up.y + dz * up.z;

        const dist = Math.hypot(dx, dy, dz);

        if (!Number.isFinite(depth) || !Number.isFinite(rdot) || !Number.isFinite(udot)) {
            return { dist, u: 0, v: 0, inFront: false, depth };
        }

        const halfTan = Math.tan((CFG.fovDeg * Math.PI / 180) / 2);
        const f = (W * 0.5) / halfTan;

        const cx = W * (c.projectionCenterX || 0.5);
        const cy = H * (c.projectionCenterY || 0.5);

        if (depth <= 0.5) {
            // Точка позади камеры: направление к краю по знакам компонент.
            return {
                dist,
                u: rdot >= 0 ? Infinity : -Infinity,
                v: udot >= 0 ? -Infinity : Infinity,
                inFront: false,
                depth
            };
        }

        return {
            dist,
            u: cx + f * (rdot / depth),
            v: cy - f * (udot / depth),
            inFront: true,
            depth
        };
    }

    // Прижим к рамке [m..W-m]×[m..H-m] вдоль луча из центра.

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
    // v1.0.40.30: ЛИНИЯ ГОРИЗОНТА — строится ИЗ БАЗИСА КАМЕРЫ (см. drawWorldHorizon
    // ниже). Прежний horizonScreenLine (проекция двух точек плоскости горизонта со
    // складыванием питчей и ручным roll) УДАЛЁН: он «не держал» реальный горизонт
    // при движении головы вверх/вниз.
    // ================================================================

    // Точка экрана, лежащая НА ЛИНИИ ГОРИЗОНТА — больше не нужна: горизонт
    // строится напрямую из базиса камеры (см. drawWorldHorizon ниже).

    // ================================================================
    // v1.0.40.30: ЛИНИЯ МИРОВОГО ГОРИЗОНТА через РЕАЛЬНУЮ позу камеры.
    // (Заменяет прежний horizonScreenLine/drawHorizonLine со складыванием питчей
    //  и ручным roll — та версия «не держала» горизонт при движении головы.)
    //
    // Горизонт = мировые лучи, ортогональные мировому Up = (0,1,0).
    // Для экранного пикселя: rayWorld = Forward + Right*x + Up*((cy−v)/f).
    // Условие rayWorld.Y = 0 даёт  v = cy + f*(Forward.Y + Right.Y*x)/Up.Y.
    // ================================================================
    function drawWorldHorizon(cam) {
        const c = cam || ar;
        if (!CFG.showHorizon || !c.cameraValid) return;

        const fov = Math.max(10, Math.min(170, Number(c.fovDeg) || 100));
        const halfTan = Math.tan((fov * Math.PI / 180) / 2);
        if (!Number.isFinite(halfTan) || Math.abs(halfTan) < 1e-12) return;

        const f = (W * 0.5) / halfTan;
        const cx = W * (c.projectionCenterX || 0.5);
        const cy = H * (c.projectionCenterY || 0.5);

        const fy = c.cameraForward.y;
        const ry = c.cameraRight.y;
        const uy = c.cameraUp.y;

        const eps = 1e-7;

        ctx.save();
        ctx.strokeStyle = CFG.horizonColor;
        ctx.lineWidth = CFG.horizonPixels;
        ctx.beginPath();

        if (Math.abs(uy) > eps) {
            const x0 = (0 - cx) / f;
            const x1 = (W - cx) / f;

            const y0 = cy + f * (fy + ry * x0) / uy;
            const y1 = cy + f * (fy + ry * x1) / uy;

            if (Number.isFinite(y0) && Number.isFinite(y1)) {
                ctx.moveTo(0, y0);
                ctx.lineTo(W, y1);
            }
        } else if (Math.abs(ry) > eps) {
            // Вырожденный случай: горизонт вертикально через экран.
            const x = cx - f * fy / ry;
            if (Number.isFinite(x)) {
                ctx.moveTo(x, 0);
                ctx.lineTo(x, H);
            }
        }

        ctx.stroke();
        ctx.restore();
    }

    // ================================================================
    // ДИСТАНЦИЯ ДО ЗЕМЛИ ПОД МИКРОТОЧКОЙ ПРИЦЕЛА
    // ================================================================
    // v1.0.40.30: луч — РЕАЛЬНЫЙ CameraForward (без складывания питчей и без
    // eyeHeightM: камера уже на своей высоте). Пересечение с плоскостью земли
    // Y = camY − (camY − groundY)… — землёй считаем горизонтальную плоскость на
    // высоте опорной точки фуры (truckY). Выше горизонта — NaN (нет измерения).
    function groundDistanceFromCrosshair(cam) {
        const c = cam || ar;
        if (!c.cameraValid) return NaN;

        const dirY = c.cameraForward.y;          // + вверх
        if (!(dirY < -1e-6)) return NaN;         // взгляд на горизонт/выше — нет измерения

        const eyeY = c.camY;                     // камера (глаз) — из позы
        const groundY = c.truckY;                // земля под колёсами (опорная точка)
        const dy = groundY - eyeY;               // < 0 (глаза выше земли)
        const t = dy / dirY;                     // > 0
        if (!Number.isFinite(t) || t <= 0) return NaN;

        const gx = c.camX + c.cameraForward.x * t;
        const gz = c.camZ + c.cameraForward.z * t;
        return Math.hypot(gx - c.camX, gz - c.camZ);
    }

    // СЕРЫЙ ДИАГОНАЛЬНЫЙ микрокрестик (пункт 8): рисуется в центре, когда
    // микроточка прицела поднялась ВЫШЕ горизонта и измерение дистанции невозможно.
    function drawDiagCross(u, v, alpha) {
        const g = CFG.diagCrossSize;
        const a = (typeof alpha === 'number') ? Math.max(0, Math.min(1, alpha)) : 1;
        ctx.save();
        ctx.strokeStyle = CFG.diagCrossColor;
        ctx.globalAlpha = a;
        for (const pair of [[4, 'rgba(0,0,0,0.6)'], [2, CFG.diagCrossColor]]) {
            ctx.lineWidth = pair[0];
            ctx.strokeStyle = pair[1];
            ctx.beginPath();
            ctx.moveTo(u - g, v - g); ctx.lineTo(u + g, v + g);
            ctx.moveTo(u + g, v - g); ctx.lineTo(u - g, v + g);
            ctx.stroke();
        }
        ctx.restore();
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
    // v1.0.40.30: КОНУС ОБЗОРА В AR1 УДАЛЁН (решение пользователя).
    // Конус в AR1 совпадает с нашим POV — то есть это границы самого экрана,
    // рисовать его бессмысленно (и он ошибочно строился от микроточки вверх).
    // Конус остаётся только на миникарте и в редакторе карты.
    // ================================================================

    // ТЕКУЩИЙ FOV — ЛЕВЫЙ НИЖНИЙ УГОЛ, ЧЁРНАЯ ОБВОДКА (требование пользователя).
    function drawFovText() {
        const txt = Number(ar.fovDeg || CFG.fovDeg).toFixed(0) + '°';
        const x = CFG.fovMarginX, y = H - CFG.fovMarginY;
        ctx.save();
        ctx.font = CFG.fovFont;
        ctx.textAlign = 'left';
        ctx.textBaseline = 'alphabetic';
        ctx.lineWidth = 3.5;
        ctx.strokeStyle = 'rgba(0,0,0,0.95)';
        ctx.lineJoin = 'round';
        ctx.miterLimit = 2;
        ctx.strokeText(txt, x, y);
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = 'rgba(0,0,0,0.85)';
        ctx.strokeText(txt, x, y);
        ctx.fillStyle = CFG.fovColor;
        ctx.fillText(txt, x, y);
        ctx.restore();
    }

    // Отметка позы камеры (для отладки/настройки через debugShow).
    // v1.0.40.30: печатается ГОТОВАЯ поза (position + forward/up) — по этим
    // числам калибруется вся геометрия (сравнивать именно их, а не углы).
    function drawCameraDebugDot(cam) {
        const c = cam || ar;
        if (!c.cameraValid) return;
        ctx.save();
        ctx.fillStyle = 'rgba(255,80,80,0.95)';
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(W / 2, H / 2 + 40, 4, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();

        const f = c.cameraForward, u2 = c.cameraUp;
        const fmt = v => Number(v).toFixed(3);
        const lines = [
            'CAM: ' + c.camX.toFixed(2) + ' ' + c.camY.toFixed(2) + ' ' + c.camZ.toFixed(2),
            'FWD: ' + fmt(f.x) + ' ' + fmt(f.y) + ' ' + fmt(f.z),
            'UP:  ' + fmt(u2.x) + ' ' + fmt(u2.y) + ' ' + fmt(u2.z),
            'FOV: ' + Number(c.fovDeg).toFixed(1)
        ];
        ctx.font = '600 11px Consolas, monospace';
        ctx.textAlign = 'center';
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.fillStyle = '#ffd166';
        for (let i = 0; i < lines.length; i++) {
            const y = H / 2 + 58 + i * 14;
            ctx.strokeText(lines[i], W / 2, y);
            ctx.fillText(lines[i], W / 2, y);
        }
        ctx.restore();
    }

    // ================================================================
    // ГЛАВНЫЙ ЦИКЛ (v70: 120 расчётов/с — rAF с двойным шагом) — ИНТЕРПОЛЯЦИЯ
    // ================================================================
    // requestAnimationFrame синхронизирован с монитором (обычно 60 Гц), поэтому
    // частоту РАСЧЁТА удваиваем: на каждый rAF выполняем два шага сглаживания
    // с полшагом (эффективно ~120 Гц лерпа) + экстраполяция камеры остаётся.
    let _fpsCnt = 0, _fpsAt = performance.now(), _fpsVal = 0;

    // v82: рамка отсечения рендера — пунктирный прямоугольник на границе
    // [edgeMargin .. W/H − edgeMargin]; внутри неё рисуется всё, за ней — обрезка.
    function drawClipFrame() {
        const m = CFG.edgeMargin;
        ctx.save();
        ctx.strokeStyle = 'rgba(120,220,255,0.35)';
        ctx.lineWidth = 1;
        ctx.setLineDash([6, 6]);
        ctx.strokeRect(m, m, W - 2 * m, H - 2 * m);
        // маленькие уголки-маркеры для наглядности
        ctx.setLineDash([]);
        ctx.strokeStyle = 'rgba(120,220,255,0.6)';
        const L = 14;
        for (const [cx2, cy2, sx, sy] of [[m, m, 1, 1], [W - m, m, -1, 1], [m, H - m, 1, -1], [W - m, H - m, -1, -1]]) {
            ctx.beginPath();
            ctx.moveTo(cx2 + sx * L, cy2); ctx.lineTo(cx2, cy2); ctx.lineTo(cx2, cy2 + sy * L);
            ctx.stroke();
        }
        ctx.restore();
    }

    function render() {
        requestAnimationFrame(render);
        if (W !== window.innerWidth || H !== window.innerHeight) resize();
        ctx.clearRect(0, 0, W, H);
        // v73: ПРИЦЕЛЬНЫЙ КУРСОР — рисуем ВСЕГДА (не зависит от телеметрии/цели):
        // «Даже если ближайших точек нет, мы всё равно отрисовываем метки новых точек».
        ctx.fillStyle = 'rgba(255,255,255,0.45)';
        ctx.fillRect(W / 2 - 1, H / 2 - 1, 1.5, 1.5);
        // v82: рамка отсечения рендера (границы, за которые метки не выходят).
        // v1.0.40.30: в обычном режиме НЕ рисуем (только под debugShow) — это
        // диагностическая рамка, а не элемент интерфейса.
        if (CFG.showClipFrame && DEBUG.show) drawClipFrame();
        _fpsCnt++;
        const fNow = performance.now();
        if (fNow - _fpsAt >= 1000) { _fpsVal = _fpsCnt; _fpsCnt = 0; _fpsAt = fNow; }

        // ============================================================
        // v1.0.40.30: ЭКСТРАПОЛЯЦИЯ КАМЕРЫ УДАЛЕНА.
        // Геометрия должна соответствовать последнему SCS-сэмплу: интерполяция
        // basis (forward/right/up) давала бы расхождение с реальным горизонтом.
        // Temporal sync/prediction — отдельный этап (см. §31 спецификации).
        // ============================================================

        // ============================================================
        // v1.0.40.30: ЛИНИЯ МИРОВОГО ГОРИЗОНТА (из базиса камеры) + ДИСТАНЦИЯ
        // ДО ЗЕМЛИ ПОД ПРИЦЕЛОМ. Экстраполяция камеры УБРАНА: геометрия должна
        // соответствовать последнему SCS-сэмплу (temporal sync — отдельный этап).
        // ============================================================
        let groundDist = NaN;
        if (ar.cameraValid) {
            drawWorldHorizon(ar);
            groundDist = groundDistanceFromCrosshair(ar);
            if (!Number.isFinite(groundDist)) {
                // Микроточка выше горизонта — измерение прекращено, подписи
                // дистанции нет, на точке СЕРЫЙ ДИАГОНАЛЬНЫЙ микрокрестик.
                drawDiagCross(W / 2, H / 2, 1);
            } else if (CFG.showGroundDistText) {
                drawOutlinedText(fmtDist(groundDist), W / 2, H / 2 + CFG.groundDistDy,
                    CFG.groundDistFont, CFG.groundDistColor, 0.95);
            }
        }

        // ТЕКУЩИЙ FOV В ЛЕВОМ НИЖНЕМ УГЛУ (чёрная обводка).
        if (CFG.showFovText) drawFovText();

        // DEBUG-режим (debugShow) — принудительный показ и отметки позы камеры.
        if (DEBUG.show) {
            if (CFG.showDebugDot && ar.cameraValid) drawCameraDebugDot(ar);
            drawClipFrame();
        }

        // ============================================================
        // ПОМЕТКА / НОВАЯ ТОЧКА (pin) — БЕЗ зеркалирования и БЕЗ сглаживания:
        // позиция берётся из свежей геометрической проекции каждый кадр.
        // ============================================================
        if (ar.cameraValid && ar.pin) {
            const pPr = projectPoint({ x: ar.pin.x, y: ar.pin.y, z: ar.pin.z }, ar);
            if (pPr.inFront) {
                let pu = Number.isFinite(pPr.u) ? pPr.u
                    : (pPr.u > 0 ? CFG.edgeMargin : W - CFG.edgeMargin);
                let pv = Number.isFinite(pPr.v) ? pPr.v
                    : (pPr.v > 0 ? H - CFG.edgeMargin : CFG.edgeMargin);
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

                drawOutlinedText('Новая точка  ·  ' + fmtDist(pPr.dist),
                    pu, pv + 18, '600 13px "Segoe UI", Arial',
                    'rgba(220,220,220,0.95)', 0.95);
                // Диагностика: поза камеры (по ней калибруется геометрия).
                const f = ar.cameraForward;
                drawOutlinedText(
                    'cam ' + ar.camX.toFixed(1) + ' ' + ar.camY.toFixed(1) + ' ' + ar.camZ.toFixed(1) +
                    ' · fwd ' + f.x.toFixed(2) + ' ' + f.y.toFixed(2) + ' ' + f.z.toFixed(2),
                    pu, pv + 36, '11px Consolas, monospace',
                    'rgba(255,220,120,0.9)', 0.9);
            }
        }

        if (!ar.cameraValid || !ar.target) return;   // цель нет — дальше рисовать нечего

        const pr = projectPoint(ar.target, ar);
        // Infinity — точка ровно сзади/сбоку: фиксируем направление к крайним значениям.
        if (!Number.isFinite(pr.u)) pr.u = pr.u > 0 ? (W - CFG.edgeMargin) : CFG.edgeMargin;
        if (!Number.isFinite(pr.v)) pr.v = pr.v > 0 ? (H - CFG.edgeMargin) : CFG.edgeMargin;
        const cl = clampToScreen(pr.u, pr.v, !pr.inFront);

        // v1.0.40.30: экранная позиция — БЕЗ сглаживания (_sm удалён) и БЕЗ
        // подмены высоты через displayYFor. Сглаживание допустимо только для
        // размера/прозрачности, но НЕ для геометрической позиции.
        ar.sel = { u: cl.u, v: cl.v, clamped: cl.clamped, inFront: pr.inFront };
        const drawU = cl.u;
        const drawV = cl.v;

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
        if (cl.clamped) {
            // ЦЕЛЬ ВНЕ ЭКРАНА: точку и перекрестье НЕ рисуем — только указатель+текст.
            const lu = drawU, lv = cl.bottom ? (drawV - 58) : (drawV + CFG.labelDy);
            drawOutlinedText((ar.target.realName || ar.target.gameName) + '  \u00B7  ' + distText,
                lu, lv, '600 14px "Segoe UI", Arial',
                'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
            if (ar.target.gameName && ar.target.gameName !== (ar.target.realName || ar.target.gameName)) {
                drawOutlinedText(ar.target.gameName,
                    lu, lv + 19, '12px "Segoe UI", Arial',
                    'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
            }
            drawEdgeArrow(drawU, drawV, color, cl.bottom === true);
        } else {
            drawMarkerToward(drawU, drawV, color, sa.size, txtAlpha);
            drawOutlinedText((ar.target.realName || ar.target.gameName) + '  \u00B7  ' + distText,
                drawU, drawV + CFG.labelDy, '600 14px "Segoe UI", Arial',
                'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
            if (ar.target.gameName !== (ar.target.realName || ar.target.gameName)) {
                drawOutlinedText(ar.target.gameName,
                    drawU, drawV + CFG.labelDy + 19, '12px "Segoe UI", Arial',
                    'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
            }
            drawCrosshair(drawU, drawV, color, (sa.size / 33) * CFG.crosshairScale);
        }
        ctx.restore();   // конец блока globalAlpha=txtAlpha (для метки/стрелки/крестика)
        // (v74: pin и прицельный курсор перенесены ВЫШЕ — рисуются ДО цели и
        //  не зависят от наличия ar.target.)
    }
    requestAnimationFrame(render);
})();