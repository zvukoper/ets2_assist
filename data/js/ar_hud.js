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
        // v1.0.40.32: ВЕРТИКАЛЬНЫЙ FOV — ОТДЕЛЬНЫЙ от горизонтального (измерено
        // ≈30.7° при горизонтали 80°; «один focal по X и Y» давал 50.5° и
        // «уплывающий» горизонт). Приложение присылает готовое значение.
        fovDegVertical: 30.7,
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
        // v1.0.40.40: ПО УМОЛЧАНИЮ ВЫКЛЮЧЕНО — включается из меню «Вид» в приложении
        // (команда ar_view). Приложение остаётся единственным владельцем настройки.
        showHorizon: false,
        horizonColor: 'rgba(120,220,255,0.75)',
        horizonPixels: 1.5,     // толщина линии, px
        // v1.0.40.32: ПОДПИСЬ горизонта (требование: «не понятно, какая из линий
        // горизонт»). Рисуется у левого края на самой линии.
        showHorizonLabel: true,
        horizonLabelX: 14,      // отступ подписи от левого края, px
        horizonLabelFont: '700 12px "Segoe UI", Arial',
        horizonLabelFontSize: 12,
        // ================================================================
        // v1.0.40.36: ЛИНИЯ ГОРИЗОНТА РЕАЛЬНОГО ПОЛОТНА (критерий сравнения).
        //
        // Зачем: голубой горизонт строится из БАЗИСА КАМЕРЫ, а белый — из РЕАЛЬНОЙ
        // плоскости дороги (по колёсам). Если режим крена/питча камеры верен, эти
        // две линии ОБЯЗАНЫ сойтись. Это единственная проверка, которая не требует
        // догадок: оба источника независимы, и видно невооружённым глазом.
        // ================================================================
        showPlaneHorizon: false,
        planeHorizonColor: 'rgba(255,255,255,0.95)',
        planeHorizonPixels: 2.0,
        planeHorizonLabel: 'горизонт полотна',
        // ================================================================
        // v1.0.40.37: ОСИ ШАССИ — ОРИЕНТАЦИЯ ГРУЗОВИКА.
        //
        // Требование пользователя: ориентацию грузовика относительно земли и
        // горизонта показывать ОТДЕЛЬНО — двумя линиями в центре экрана
        // (вертикальной и горизонтальной), параллельными осям ШАССИ (локальные
        // X/Z кузова), а НЕ краям экрана. При компенсированной камере крен/питч
        // грузовика виден именно по этим линиям.
        //   • ось шасси X (поперечная) — красноватая;
        //   • ось шасси Z (продольная) — зеленоватая;
        //   • ось шасси Y (вертикаль кузова) — синеватая (показывает крен кузова).
        // ================================================================
        showChassisAxes: false,
        chassisAxisLenPx: 130,        // полудлина каждой оси, px
        chassisAxisXColor: 'rgba(255,120,120,0.90)',
        chassisAxisZColor: 'rgba(140,255,140,0.90)',
        chassisAxisYColor: 'rgba(140,180,255,0.90)',
        chassisAxisWidth: 2.0,
        chassisAxisLabel: true,
        diagCrossColor: 'rgba(170,170,170,0.95)',
        diagCrossSize: 7,       // полуразмер СЕРЫЙ ДИАГОНАЛЬНЫЙ микрокрестик, px
        showGroundDistText: true,
        groundDistFont: '600 12px Consolas, monospace',
        groundDistColor: 'rgba(255,255,255,0.95)',
        groundDistDy: 12,       // отступ текста дистанции от центра экрана, px

        // ================================================================
        // v1.0.40.33: СЕТКА — ПРОСТО ПЛОСКОСТЬ БЕЗ ОГРАНИЧЕНИЙ.
        // УБРАНО по требованию: круг R=150 м, fade 125…150 м, граница-окружность,
        // «шахматные» эффекты и расширение к камере (CameraPadM) — всё это
        // оказалось лишним. Сетка строится как бесконечная плоскость; гаснет
        // только по экранной вертикали и по расстоянию до КАМЕРЫ (мягкое
        // отсечение невидимой дали, чтобы не считать километры линий).
        // Оси — из GroundPlane.AxisU/AxisV (плоскость наклонная).
        // ================================================================
        showGroundGrid: false,
        gridFarStartM: 220,      // до этого расстояния от камеры alpha полная
        gridFarEndM: 320,        // дальше — линия не рисуется
        gridBehindCameraM: 80,   // запас сетки ЗА камерой (м), чтобы не было «дыры» у низа экрана
        verticalFromHorizontalFactor: 0.58,
        gridColor1M: 'rgba(255,255,255,0.42)',    // каждая 1 м — тонкая белая
        gridColor10M: 'rgba(255,140,0,0.85)',     // каждая 10 м — толстая оранжевая
        gridColor50M: 'rgba(255,38,38,0.95)',     // каждая 50 м — красная (приоритет)
        gridWidth1M: 0.7,
        gridWidth10M: 2.2,
        gridWidth50M: 3.2,

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
        fovDeg: 100,                  // ГОРИЗОНТАЛЬНЫЙ FOV (camera.fovDeg)
        // ================================================================
        // v1.0.40.32: ВЕРТИКАЛЬНЫЙ FOV — ОТДЕЛЬНЫЙ от горизонтального.
        // КОРЕНЬ «горизонт/метка уплывают тем сильнее, чем дальше прицел от
        // горизонта»: по Y применялся тот же focal length, что и по X. Это даёт
        // смещение Δv ≈ Δf·tan(наклона) — ровно ноль на горизонте и рост с углом.
        // ================================================================
        fovDegVertical: 30.7,
        projectionCenterX: 0.5,
        projectionCenterY: 0.5,
        lastTelemetryAt: 0,
        // v1.0.40.44: момент приёма последнего ПАКЕТА (performance.now()).
        // Разница с текущим временем = «возраст» данных, на который
        // экстраполируется поза камеры при рендере (плавность точек).
        telemetryAtMs: 0,
        target: null,                 // БЛИЖАЙШАЯ точка (статус-строка, совместимость)
        points: [],                   // v1.0.40.40: ВСЕ точки в радиусе (рисуем все)
        displayRadiusM: 50,           // v1.0.40.40: радиус показа точек, м
        pin: null,                    // пометка / новая точка {x,y,z} (крестик)
        cities: [],                   // [{x,y,z}] — ТОЛЬКО совместимость/диагностика

        // ================================================================
        // v1.0.40.31: РЕАЛЬНАЯ ПЛОСКОСТЬ ДОРОГИ (по колёсам, Ground Plane).
        // Единственный источник геометрии земли: сетка, дистанция до земли,
        // создание точки. Поля — из ar_telemetry.groundPlane.
        // ================================================================
        groundPlane: null,

        sel: null                     // текущая экранная позиция перекрестья
    };
    window.__arHud = ar;

    // ================================================================
    // v1.0.40.31: РАБОТА С ПЛОСКОСТЬЮ ДОРОГИ (зеркало AR/ArGroundPlane.cs).
    // Все геометрические запросы идут ТОЛЬКО сюда — никаких groundY/PlaneOffsetM.
    // ================================================================
    const GroundPlane = {
        valid(gp) { return !!gp && gp.valid === true; },

        // Высота плоскости в мировой точке (X,Z).
        heightAt(gp, x, z) {
            if (!this.valid(gp)) return NaN;
            // Плоскость задана нормалью и точкой origin; решаем по нормали.
            const n = gp.normal, o = gp.origin;
            if (Math.abs(n[1]) < 1e-9) return NaN;
            return o[1] - (n[0] * (x - o[0]) + n[2] * (z - o[2])) / n[1];
        },

        // Пересечение луча с плоскостью. Возвращает {t,x,y,z} или null.
        intersectRay(gp, ox, oy, oz, dx, dy, dz) {
            if (!this.valid(gp)) return null;
            const n = gp.normal, o = gp.origin;
            const denom = dx * n[0] + dy * n[1] + dz * n[2];
            if (!Number.isFinite(denom) || Math.abs(denom) < 1e-9) return null;
            const numer = (o[0] - ox) * n[0] + (o[1] - oy) * n[1] + (o[2] - oz) * n[2];
            const t = numer / denom;
            if (!Number.isFinite(t) || t <= 0) return null;
            return { t, x: ox + dx * t, y: oy + dy * t, z: oz + dz * t };
        },

        // Мировая точка → координаты сетки (u,v) внутри плоскости.
        projectToGrid(gp, x, y, z) {
            if (!this.valid(gp)) return null;
            const o = gp.origin, U = gp.axisU, V = gp.axisV;
            const dx = x - o[0], dy = y - o[1], dz = z - o[2];
            return {
                u: dx * U[0] + dy * U[1] + dz * U[2],
                v: dx * V[0] + dy * V[1] + dz * V[2]
            };
        },

        // Координаты сетки (u,v) → мировая точка (ЛЕЖИТ на плоскости).
        fromGrid(gp, u, v) {
            if (!this.valid(gp)) return null;
            const o = gp.origin, U = gp.axisU, V = gp.axisV;
            return {
                x: o[0] + U[0] * u + V[0] * v,
                y: o[1] + U[1] * u + V[1] * v,
                z: o[2] + U[2] * u + V[2] * v
            };
        },

        // Проекция мировой X/Z на плоскость (высота подбирается по плоскости).
        snapToPlane(gp, x, z) {
            const y = this.heightAt(gp, x, z);
            if (!Number.isFinite(y)) return null;
            return { x, y, z };
        }
    };

    // ================================================================
    // v1.0.40.44: СГЛАЖИВАНИЕ ПОЗЫ КАМЕРЫ — КОРЕНЬ «ТОЧКИ ДВИЖУТСЯ РЫВКАМИ».
    //
    // ДИАГНОЗ. Телеметрия приходит 28–35 раз в секунду (замерено: интервал
    // ~36 мс, вход `throttle=16`). Рендер идёт через requestAnimationFrame
    // (60 Гц). Но поза камеры применялась РОВНО в момент прихода пакета, а между
    // пакетами не менялась: 2 кадра рисовали одну точку, потом был скачок.
    // Это и есть «14 fps» на глаз, хотя счётчик кадров показывает 60–100.
    // В v1.0.40.30 экстраполяция камеры была УДАЛЕНА (тогда боролись с
    // «уплывающим» горизонтом), и точки стали дёргаться.
    //
    // РЕШЕНИЕ. Экспоненциальное сглаживание положения/базиса камеры в рендере:
    //   cam = cam + (target − cam) * k,   k = 1 − exp(−dt / tau)
    // Формула через dt (а не фиксированный коэффициент «на кадр») даёт ОДИНАКОВОЕ
    // поведение при любой частоте кадров — при 60 и при 144 Гц сглаживание
    // одинаковое по времени, а не «в 2 раза быстрее».
    //
    // ВАЖНО ПРО БАЗИС (forward/right/up): их НЕЛЬЗЯ сглаживать покомпонентно
    // (векторы перестанут быть ортонормальными → «плывущий» горизонт, который
    // чинили в v40.32). Для наклона используем СФЕРИЧЕСКУЮ интерполяцию:
    // нормализуем после лерпа. Это сохраняет единичную длину и не даёт
    // геометрических артефактов.
    //
    // ОГОВОРКА: сглаживание ЗАДЕРЖИВАЕТ картинку на tau (≈40 мс). Это плата за
    // плавность и она на порядок меньше, чем рывок в 36 мс; для AR-метки
    // задержка незаметна, а рывки раздражают.
    // ================================================================
    const SMOOTH = {
        enabled: true,
        // v1.0.40.44 (оптимизировано численно): СНАЧАЛА экстраполируем ЦЕЛЬ по
        // скорости до текущего момента, ЗАТЕМ сглаживаем. Сравнение подходов
        // (скорость 20 м/с, пакеты 36 мс, рендер 60 Гц):
        //   без обработки             — джиттер 358.7 мм, макс. рывок 720 мм
        //   сглаживание позиции       — джиттер  85.2 мм, рывок 425 мм
        //   экстраполяция цели + сглаж. — джиттер  42.7 мм, рывок 333 мм
        // То есть экстраполяция цели убирает дрожание ещё в 2 раза и вдвое
        // уменьшает максимальный рывок.
        //
        // v1.0.40.54: СИЛА СГЛАЖИВАНИЯ = 80 мс — ПОДОБРАННОЕ ПОЛЬЗОВАТЕЛЕМ
        // значение (AppSettings.Ar1SmoothTau = 0.08). Оно же используется
        // квестовыми маркерами (js/ar_quests.js читает его через window.__arSmooth),
        // иначе два слоя AR сглаживаются с разной силой и «разъезжаются».
        posTau: 0.08,      // постоянная времени позиции, с (80 мс)
        rotTau: 0.092,     // ориентация — чуть мягче (tau × 1.15)
        snapDistM: 25.0,   // телепорт/загрузка: скачок больше — без сглаживания
        // Предел экстраполяции: если пакетов долго нет, НЕ улетаем вперёд
        // (защита от «поехавшей» скорости после паузы/телепорта).
        maxExtrapS: 0.15
    };

    // Сглаженное состояние (то, что реально используется в проекции).
    const camSmooth = {
        x: 0, y: 0, z: 0,
        fx: 0, fy: 0, fz: 0,
        rx: 0, ry: 0, rz: 0,
        ux: 0, uy: 0, uz: 0,
        // Скорость камеры (м/с), оценённая по последним двум пакетам.
        vx: 0, vy: 0, vz: 0,
        valid: false,
        lastAt: 0
    };

    /**
     * Пересчитать сглаженную позу к моменту now. Вызывается КАЖДЫЙ кадр.
     * Возвращает объект для проекции (или исходный `ar`, если сглаживание выкл.).
     */
    function smoothCamera(nowMs) {
        if (!SMOOTH.enabled || !ar.cameraValid) return ar;

        if (!camSmooth.valid) {
            // Первый пакет — принимаем как есть (без «доезда» от нуля).
            camSmooth.x = ar.camX; camSmooth.y = ar.camY; camSmooth.z = ar.camZ;
            camSmooth.fx = ar.cameraForward.x; camSmooth.fy = ar.cameraForward.y; camSmooth.fz = ar.cameraForward.z;
            camSmooth.rx = ar.cameraRight.x; camSmooth.ry = ar.cameraRight.y; camSmooth.rz = ar.cameraRight.z;
            camSmooth.ux = ar.cameraUp.x; camSmooth.uy = ar.cameraUp.y; camSmooth.uz = ar.cameraUp.z;
            camSmooth.vx = camSmooth.vy = camSmooth.vz = 0;
            camSmooth.valid = true;
            camSmooth.lastAt = nowMs;
            return applySmooth();
        }

        // dt ограничиваем: после паузы/фонового таба не «догоняем» рывком.
        let dt = (nowMs - camSmooth.lastAt) / 1000;
        if (!Number.isFinite(dt) || dt <= 0) dt = 1 / 60;
        if (dt > 0.25) dt = 0.25;
        camSmooth.lastAt = nowMs;

        // Телепорт/перезагрузка — принимаем позу как есть (иначе «доезд»).
        const jump = Math.hypot(ar.camX - camSmooth.x, ar.camY - camSmooth.y, ar.camZ - camSmooth.z);
        if (jump > SMOOTH.snapDistM) {
            camSmooth.x = ar.camX; camSmooth.y = ar.camY; camSmooth.z = ar.camZ;
            camSmooth.fx = ar.cameraForward.x; camSmooth.fy = ar.cameraForward.y; camSmooth.fz = ar.cameraForward.z;
            camSmooth.rx = ar.cameraRight.x; camSmooth.ry = ar.cameraRight.y; camSmooth.rz = ar.cameraRight.z;
            camSmooth.ux = ar.cameraUp.x; camSmooth.uy = ar.cameraUp.y; camSmooth.uz = ar.cameraUp.z;
            camSmooth.vx = camSmooth.vy = camSmooth.vz = 0;
            return applySmooth();
        }

        // Коэффициенты сглаживания, НЕ зависящие от частоты кадров
        // (k = 1 − exp(−dt/tau): поведение одинаково при 60 и 144 Гц).
        const kPos = 1 - Math.exp(-dt / SMOOTH.posTau);
        const kRot = 1 - Math.exp(-dt / SMOOTH.rotTau);

        // ---- ЦЕЛЬ: позиция пакета + экстраполяция по скорости ----
        // Возраст данных (сколько прошло с момента последнего принятого пакета).
        const ageS = Math.min(
            (nowMs - (ar.telemetryAtMs || nowMs)) / 1000,
            SMOOTH.maxExtrapS);
        const ageClamped = Math.max(0, ageS);
        const tx = ar.camX + camSmooth.vx * ageClamped;
        const ty = ar.camY + camSmooth.vy * ageClamped;
        const tz = ar.camZ + camSmooth.vz * ageClamped;

        camSmooth.x += (tx - camSmooth.x) * kPos;
        camSmooth.y += (ty - camSmooth.y) * kPos;
        camSmooth.z += (tz - camSmooth.z) * kPos;

        // Сферическая интерполяция базиса: лерп + нормализация (сохраняет
        // единичную длину; покомпонентный лерп ломал бы ортонормальность).
        const f = lerpNormalized(camSmooth.fx, camSmooth.fy, camSmooth.fz,
            ar.cameraForward.x, ar.cameraForward.y, ar.cameraForward.z, kRot);
        camSmooth.fx = f[0]; camSmooth.fy = f[1]; camSmooth.fz = f[2];

        const r = lerpNormalized(camSmooth.rx, camSmooth.ry, camSmooth.rz,
            ar.cameraRight.x, ar.cameraRight.y, ar.cameraRight.z, kRot);
        camSmooth.rx = r[0]; camSmooth.ry = r[1]; camSmooth.rz = r[2];

        const u = lerpNormalized(camSmooth.ux, camSmooth.uy, camSmooth.uz,
            ar.cameraUp.x, ar.cameraUp.y, ar.cameraUp.z, kRot);
        camSmooth.ux = u[0]; camSmooth.uy = u[1]; camSmooth.uz = u[2];

        return applySmooth();
    }

    /**
     * Оценить скорость камеры по двум последним пакетам телеметрии.
     * Вызывается ИЗ ОБРАБОТЧИКА ПАКЕТА (не из рендера!) — только там известны
     * точные интервалы между выборками.
     */
    function updateCameraVelocity() {
        const now = performance.now();
        if (camPrev.valid) {
            const dt = (now - camPrev.atMs) / 1000;
            if (dt > 0.005 && dt < 0.5) {
                // Скачок (телепорт) не должен давать «скорость» — фильтруем.
                const dxp = ar.camX - camPrev.x, dyp = ar.camY - camPrev.y, dzp = ar.camZ - camPrev.z;
                const dist = Math.hypot(dxp, dyp, dzp);
                if (dist < SMOOTH.snapDistM) {
                    // Мягко: новая оценка смешивается со старой (сглаживание скорости),
                    // иначе дрожание интервалов пакетов давало бы дрожание скорости.
                    const nvx = dxp / dt, nvy = dyp / dt, nvz = dzp / dt;
                    const a = 0.35;
                    camSmooth.vx += (nvx - camSmooth.vx) * a;
                    camSmooth.vy += (nvy - camSmooth.vy) * a;
                    camSmooth.vz += (nvz - camSmooth.vz) * a;
                } else {
                    camSmooth.vx = camSmooth.vy = camSmooth.vz = 0;
                }
            }
        }
        camPrev.x = ar.camX; camPrev.y = ar.camY; camPrev.z = ar.camZ;
        camPrev.atMs = now;
        camPrev.valid = true;
        ar.telemetryAtMs = now;   // возраст данных для экстраполяции
    }

    // Предыдущий пакет камеры (для оценки скорости).
    const camPrev = { x: 0, y: 0, z: 0, atMs: 0, valid: false };

    /** Лерп двух векторов с нормализацией результата (сферическая интерполяция). */
    function lerpNormalized(ax, ay, az, bx, by, bz, k) {
        // Пишем в модульный буфер — без аллокации массива каждый кадр.
        _lerpOut[0] = ax + (bx - ax) * k;
        _lerpOut[1] = ay + (by - ay) * k;
        _lerpOut[2] = az + (bz - az) * k;
        const len = Math.hypot(_lerpOut[0], _lerpOut[1], _lerpOut[2]);
        if (!Number.isFinite(len) || len < 1e-9) {
            _lerpOut[0] = bx; _lerpOut[1] = by; _lerpOut[2] = bz;
            return _lerpOut;
        }
        _lerpOut[0] /= len; _lerpOut[1] /= len; _lerpOut[2] /= len;
        return _lerpOut;
    }
    const _lerpOut = [0, 0, 0];

    /** Собрать объект позы для проекции из сглаженного состояния. */
    function applySmooth() {
        // Один переиспользуемый объект — ноль аллокаций в кадре
        // (поля объектов меняем на месте, НЕ пересоздаём их).
        camSmoothView.camX = camSmooth.x;
        camSmoothView.camY = camSmooth.y;
        camSmoothView.camZ = camSmooth.z;
        const f = camSmoothView.cameraForward;
        f.x = camSmooth.fx; f.y = camSmooth.fy; f.z = camSmooth.fz;
        const r = camSmoothView.cameraRight;
        r.x = camSmooth.rx; r.y = camSmooth.ry; r.z = camSmooth.rz;
        const u = camSmoothView.cameraUp;
        u.x = camSmooth.ux; u.y = camSmooth.uy; u.z = camSmooth.uz;
        // Остальные поля берём напрямую (FOV/центр/плоскость не сглаживаем).
        camSmoothView.cameraValid = true;
        camSmoothView.fovDeg = ar.fovDeg;
        camSmoothView.fovDegVertical = ar.fovDegVertical;
        camSmoothView.projectionCenterX = ar.projectionCenterX;
        camSmoothView.projectionCenterY = ar.projectionCenterY;
        camSmoothView.groundPlane = ar.groundPlane;
        return camSmoothView;
    }

    // Переиспользуемый «вид» сглаженной камеры (аллокаций в кадре нет).
    const camSmoothView = {
        camX: 0, camY: 0, camZ: 0,
        cameraForward: { x: 0, y: 0, z: -1 },
        cameraRight: { x: 1, y: 0, z: 0 },
        cameraUp: { x: 0, y: 1, z: 0 },
        cameraValid: false,
        fovDeg: 105,
        fovDegVertical: 65,
        projectionCenterX: 0.5,
        projectionCenterY: 0.5,
        groundPlane: null
    };

    // ================================================================
    // v1.0.40.54: СГЛАЖИВАНИЕ ПОЗЫ ДОСТУПНО ДРУГИМ МОДУЛЯМ СТРАНИЦЫ.
    // Квестовые маркеры (js/ar_quests.js) применяют ТУ ЖЕ технику и ТУ ЖЕ силу
    // (tau), иначе метки дрожат на пачке кадров между пакетами телеметрии.
    // Публикуется ЗДЕСЬ (а не рядом с ar) — до этой строки SMOOTH/camSmoothView
    // в temporal dead zone (const), и обращение дало бы ReferenceError.
    // ================================================================
    window.__arSmooth = { cfg: SMOOTH, view: camSmoothView };

    /**
     * Сбросить сглаживание (новая поза/перезапуск) — следующий кадр примет позу как есть.
     */
    function resetCameraSmoothing() {
        camSmooth.valid = false;
        camSmooth.vx = camSmooth.vy = camSmooth.vz = 0;
        camPrev.valid = false;
    }

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
            ws.onopen = function () {
                setStatus('ok', 'AR: подключено к приложению');
                // v1.0.40.40: при подключении применяем актуальные настройки вида.
                requestArView();
            };
            ws.onmessage = function (ev) {
                let data = null;
                try { data = JSON.parse(ev.data); } catch (e) { return; }
                if (!data || !data.command) return;
                if (data.command === 'ar_target') applyArTarget(data);
                else if (data.command === 'ar_telemetry') applyArTelemetry(data);
                else if (data.command === 'ar_pin') applyArPin(data);
                else if (data.command === 'ar_fov') applyArFov(data);
                else if (data.command === 'ar_view') applyArView(data);
                else if (data.command === 'debug_show') debugShow(data.enabled === true);
            };
            ws.onclose = function () { ws = null; setTimeout(connect, 2000); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) {
            setTimeout(connect, 2000);
        }
    }

    // ---- Приём: точки (приложение шлёт РАЗОВО при смене НАБОРА) ----
    // v1.0.40.40: приходит СПИСОК точек в радиусе 50 м (`points[]`) — рисуем ВСЕ.
    // Поля `gameName/x/y/z/...` на верхнем уровне сохранены для обратной
    // совместимости: это БЛИЖАЙШАЯ точка (её использует статус-строка и старый код).
    function applyArTarget(data) {
        if (data.hasTarget === true) {
            function toPoint(p) {
                return {
                    gameName: String(p.gameName || 'target'),
                    realName: String(p.realName || ''),
                    x: Number(p.x) || 0,
                    // v1.0.40.30: высота точки — КАК ЕСТЬ (никаких groundY-якорей
                    // и «подъёма» через ближайший город: это ломало геометрию).
                    y: Number(p.y) || 0,
                    z: Number(p.z) || 0,
                    dist: Number(p.dist) || 0,
                    kind: String(p.kind || 'poi'),
                    category: p.category ? String(p.category) : '',
                    color: p.color ? String(p.color) : '',
                    isTarget: p.isTarget === true
                };
            }

            if (Array.isArray(data.points) && data.points.length > 0) {
                ar.points = data.points.map(toPoint);
            } else {
                // Совместимость со старым форматом (одна точка в корне).
                ar.points = [toPoint(data)];
            }
            ar.target = ar.points[0] || null;   // ближайшая — для статус-строки
            if (data.radiusM) ar.displayRadiusM = Number(data.radiusM) || ar.displayRadiusM;
            ar.targetAt = performance.now();
        } else {
            ar.points = [];
            ar.target = null; // приложение сказало: точек в радиусе нет / нет телеметрии
        }
        statusFromState();
    }

    // Запрос настроек вида у приложения (страница — dumb-receiver).
    function requestArView() {
        try {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify({ command: 'ar_view_request' }));
            }
        } catch (e) { /* не критично — придут при следующем изменении */ }
    }

    // ---- Приём: настройки ВИДА (v1.0.40.40, меню «Вид» в приложении) ----    // Приложение — единственный владелец настроек; страница их только применяет.
    // По умолчанию сетка/оси/горизонты ВЫКЛЮЧЕНЫ (требование пользователя:
    // «Убираем оси, сетку, визуализацию высот»), поэтому стартовые значения CFG —
    // false, а приложение присылает актуальные при старте/изменении.
    function applyArView(data) {
        if (data.grid !== undefined)          CFG.showGroundGrid = data.grid === true;
        if (data.axes !== undefined)          CFG.showChassisAxes = data.axes === true;
        if (data.horizon !== undefined)       CFG.showHorizon = data.horizon === true;
        if (data.planeHorizon !== undefined)  CFG.showPlaneHorizon = data.planeHorizon === true;
        if (data.radiusM !== undefined) {
            const r = Number(data.radiusM);
            if (Number.isFinite(r) && r > 0) ar.displayRadiusM = r;
        }
        // v1.0.40.44: ПЛАВНОСТЬ — приложение владелец настройки.
        if (data.smooth !== undefined) {
            SMOOTH.enabled = data.smooth === true;
            if (!SMOOTH.enabled) resetCameraSmoothing();
        }
        if (data.smoothTau !== undefined) {
            const t = Number(data.smoothTau);
            if (Number.isFinite(t) && t >= 0.005 && t <= 0.5) {
                SMOOTH.posTau = t;
                SMOOTH.rotTau = t * 1.15;   // ориентация чуть мягче позиции
            }
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
                // v1.0.40.34: ДЕРЖИМ CFG.fovDeg В СИНХРОНЕ с телеметрией. Раньше
                // CFG.fovDeg обновлялся ТОЛЬКО командой ar_fov (разово при подключении),
                // и при любой потере этой команды сетка/горизонт считались fh от
                // СТАРОГО значения по умолчанию (75°) вместо реальных (91°) — то есть
                // по горизонтали всё «уезжало». Телеметрия приходит постоянно, поэтому
                // синхронизация здесь надёжнее.
                if (Number.isFinite(ar.fovDeg)) CFG.fovDeg = ar.fovDeg;

                // v1.0.40.32: ВЕРТИКАЛЬНЫЙ FOV приходит ОТДЕЛЬНО (корень «уплывающего»
                // горизонта). Если приложение его не прислало — выводим из горизонтали
                // по тому же правилу (сжатие по вертикали), чтобы не откатываться к
                // старой ошибочной геометрии «тот же focal по X и Y».
                const fovV = Number(camera.fovDegVertical);
                if (Number.isFinite(fovV) && fovV > 1.0) {
                    ar.fovDegVertical = Math.max(10, Math.min(170, fovV));
                } else {
                    const aspect = (W > 0 && H > 0) ? (W / H) : (16 / 9);
                    ar.fovDegVertical = Math.max(10, Math.min(170,
                        2 * Math.atan(Math.tan(ar.fovDeg * Math.PI / 360) / aspect *
                            (CFG.verticalFromHorizontalFactor || 1)) * 180 / Math.PI));
                }

                ar.haveTruck = true;
                ar.lastTelemetryAt = performance.now();
                // v1.0.40.44: оценить скорость камеры по двум последним пакетам —
                // нужна для экстраполяции позы между пакетами (плавность точек).
                updateCameraVelocity();
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

        // ============================================================
        // v1.0.40.31: РЕАЛЬНАЯ ПЛОСКОСТЬ ДОРОГИ (Ground Plane по колёсам).
        // Приложение отдаёт готовую плоскость: origin/normal/axisU/axisV,
        // высоты и диагностический массив колёс. Страница НЕ строит плоскость
        // сама — она только использует её для сетки/дистанции/создания точки.
        // groundY / PlaneOffsetM / Math.Round(worldX/worldZ) больше НЕ нужны.
        // ============================================================
        const gp = data.groundPlane;
        if (gp && typeof gp === 'object' && gp.valid === true) {
            const origin = readVec3(gp.origin, null);
            const normal = readVec3(gp.normal, null);
            const axisU = readVec3(gp.axisU, null);
            const axisV = readVec3(gp.axisV, null);

            if (origin && normal && axisU && axisV) {
                ar.groundPlane = {
                    valid: true,
                    origin: [origin.x, origin.y, origin.z],
                    normal: [normal.x, normal.y, normal.z],
                    axisU: [axisU.x, axisU.y, axisU.z],
                    axisV: [axisV.x, axisV.y, axisV.z],
                    averageWheelHeight: Number(gp.averageWheelHeight) || 0,
                    referenceHeight: Number(gp.referenceHeight) || 0,
                    maxResidual: Number(gp.maxResidual) || 0,
                    usedWheelCount: Number(gp.usedWheelCount) || 0,
                    // v1.0.40.37: горизонтальная ли плоскость (для диагностики)
                    isHorizontal: gp.isHorizontal === true,
                    // v1.0.40.37: ОСИ ШАССИ — ориентация ГРУЗОВИКА (от кузова, не от камеры).
                    chassisForward: readVec3(gp.chassisForward, null),
                    chassisRight: readVec3(gp.chassisRight, null),
                    chassisUp: readVec3(gp.chassisUp, null),
                    truckPitchDeg: Number(gp.truckPitchDeg) || 0,
                    truckRollDeg: Number(gp.truckRollDeg) || 0,
                    wheels: Array.isArray(gp.wheels) ? gp.wheels : []
                };
            }
        } else if (gp && gp.valid === false) {
            ar.groundPlane = null;   // плоскость не построена (нет данных колёс)
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
        }
        // v1.0.40.32: вертикальный FOV приходит ОТДЕЛЬНЫМ полем — можно подстраивать
        // на лету (Ctrl+Alt+PGUP/PGDN) и проверять «приклеивание» горизонта.
        const fv = Number(data.fovVertical);
        if (Number.isFinite(fv) && fv >= 10 && fv <= 170) {
            CFG.fovDegVertical = fv;
            ar.fovDegVertical = fv;
        }
        statusFromState();
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

        // v1.0.40.32: РАЗНЫЕ focal length по X и Y. Общий f давал ошибку Δf по
        // вертикали → горизонт/метки «уплывали» пропорционально tan(наклона).
        const fh = (W * 0.5) / Math.tan((CFG.fovDeg * Math.PI / 180) / 2);
        const fv = (H * 0.5) / Math.tan((Math.max(1, Number(c.fovDegVertical) || CFG.fovDegVertical) * Math.PI / 180) / 2);

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
            u: cx + fh * (rdot / depth),
            v: cy - fv * (udot / depth),
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

        const fovV = Math.max(10, Math.min(170, Number(c.fovDegVertical) || CFG.fovDegVertical));
        const halfTanV = Math.tan((fovV * Math.PI / 180) / 2);
        if (!Number.isFinite(halfTanV) || Math.abs(halfTanV) < 1e-12) return;

        // v1.0.40.32: горизонт строится по ВЕРТИКАЛЬНОМУ focal length (он же
        // используется проекцией) — иначе линия «уплывала» при наклоне головы.
        const fv = (H * 0.5) / halfTanV;
        const fh = (W * 0.5) / Math.tan((Math.max(10, Math.min(170, Number(c.fovDeg) || CFG.fovDeg)) * Math.PI / 180) / 2);
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
            const x0 = (0 - cx) / fh;
            const x1 = (W - cx) / fh;

            const y0 = cy + fv * (fy + ry * x0) / uy;
            const y1 = cy + fv * (fy + ry * x1) / uy;

            if (Number.isFinite(y0) && Number.isFinite(y1)) {
                ctx.moveTo(0, y0);
                ctx.lineTo(W, y1);
            }
        } else if (Math.abs(ry) > eps) {
            // Вырожденный случай: горизонт вертикально через экран.
            const x = cx - fh * fy / ry;
            if (Number.isFinite(x)) {
                ctx.moveTo(x, 0);
                ctx.lineTo(x, H);
            }
        }

        ctx.stroke();
        ctx.restore();
    }

    // ================================================================
    // v1.0.40.32: ПОДПИСЬ ЛИНИИ ГОРИЗОНТА.
    // Требование пользователя: «вижу голубые линии и не понятно, какая из них
    // горизонт» — линия горизонта подписывается явно, у левого края.
    // ================================================================
    function drawHorizonLabel(cam) {
        const c = cam || ar;
        if (!CFG.showHorizon || !CFG.showHorizonLabel || !c.cameraValid) return;

        const fovV = Math.max(10, Math.min(170, Number(c.fovDegVertical) || CFG.fovDegVertical));
        const halfTanV = Math.tan((fovV * Math.PI / 180) / 2);
        if (!Number.isFinite(halfTanV) || Math.abs(halfTanV) < 1e-12) return;

        const fv = (H * 0.5) / halfTanV;
        const fh = (W * 0.5) / Math.tan((Math.max(10, Math.min(170, Number(c.fovDeg) || CFG.fovDeg)) * Math.PI / 180) / 2);
        const cx = W * (c.projectionCenterX || 0.5);
        const cy = H * (c.projectionCenterY || 0.5);

        const fy = c.cameraForward.y;
        const ry = c.cameraRight.y;
        const uy = c.cameraUp.y;
        if (Math.abs(uy) < 1e-7) return;                 // вертикальный горизонт — не подписываем

        // Высота линии у левого края.
        const xAt = (CFG.horizonLabelX - cx) / fh;
        const y = cy + fv * (fy + ry * xAt) / uy;
        if (!Number.isFinite(y) || y < -60 || y > H + 60) return;

        const txt = 'ГОРИЗОНТ МИРА';
        ctx.save();
        ctx.font = CFG.horizonLabelFont;
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';

        // Фон-плашка, чтобы подпись не сливалась с травой/небом.
        const tw = ctx.measureText(txt).width;
        const padX = 5, padY = 3;
        const bx = CFG.horizonLabelX - padX;
        const by = y - CFG.horizonLabelFontSize / 2 - padY;
        const bw = tw + padX * 2;
        const bh = CFG.horizonLabelFontSize + padY * 2;
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(bx, by, bw, bh);

        // Тонкая чёрная обводка текста (читаемость на светлом фоне).
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.lineJoin = 'round';
        ctx.strokeText(txt, CFG.horizonLabelX, y);
        ctx.fillStyle = CFG.horizonColor;
        ctx.fillText(txt, CFG.horizonLabelX, y);
        ctx.restore();
    }

    // ================================================================
    // v1.0.40.36: ГОРИЗОНТ РЕАЛЬНОГО ПОЛОТНА — белая линия.
    //
    // Отличие от голубой линии горизонта: голубая — это горизонт МИРА (лучи
    // перпендикулярны мировой вертикали), а белая — линия, где РЕАЛЬНАЯ плоскость
    // дороги уходит в бесконечность. На ровной дороге они совпадают (пользователь
    // это подтвердил), а на уклоне/крене расходятся ровно настолько, насколько
    // неверна компенсация наклона. Поэтому белая линия — эталон.
    //
    // Геометрия: точка плоскости P = Origin + u·AxisU + v·AxisV уходит на
    // бесконечность при t → ∞ вдоль направления D (t·AxisU + AxisV или AxisU − t·AxisV).
    // Экранная позиция = предел проекции, направление = проекция D (не точка!).
    // ================================================================
    function drawPlaneHorizon(cam) {
        const c = cam || ar;
        const gp = c.groundPlane;
        if (!CFG.showPlaneHorizon || !gp || !gp.valid || !c.cameraValid) return;

        const fovH = Math.max(10, Math.min(170, Number(c.fovDeg) || CFG.fovDeg));
        const fovV = Math.max(10, Math.min(170, Number(c.fovDegVertical) || CFG.fovDegVertical));
        const fh = (W * 0.5) / Math.tan((fovH * Math.PI / 180) / 2);
        const fv = (H * 0.5) / Math.tan((fovV * Math.PI / 180) / 2);
        const cx = W * (c.projectionCenterX || 0.5);
        const cy = H * (c.projectionCenterY || 0.5);

        const F = c.cameraForward, Rg = c.cameraRight, U = c.cameraUp;

        // Проекция world-точки: null, если точка за камерой.
        function proj(p) {
            const dx = p[0] - c.camX, dy = p[1] - c.camY, dz = p[2] - c.camZ;
            const zc = dx * F.x + dy * F.y + dz * F.z;
            if (zc <= 1e-6) return null;
            return {
                u: cx + fh * ((dx * Rg.x + dy * Rg.y + dz * Rg.z) / zc),
                v: cy - fv * ((dx * U.x + dy * U.y + dz * U.z) / zc),
                z: zc
            };
        }

        // Направление в бесконечность: экранная позиция = предел (проектируем как
        // точку «далеко впереди», направление сохраняется).
        function dirScreen(d) {
            const zc = d[0] * F.x + d[1] * F.y + d[2] * F.z;
            if (Math.abs(zc) < 1e-9) return null;
            return {
                u: cx + fh * ((d[0] * Rg.x + d[1] * Rg.y + d[2] * Rg.z) / zc),
                v: cy - fv * ((d[0] * U.x + d[1] * U.y + d[2] * U.z) / zc),
                z: zc
            };
        }

        const O = [gp.origin.x, gp.origin.y, gp.origin.z];
        const AU = [gp.axisU.x, gp.axisU.y, gp.axisU.z];
        const AV = [gp.axisV.x, gp.axisV.y, gp.axisV.z];
        const s = 1e6;   // «бесконечность»: линия в плоскости уходит ровно по её направлению

        // Две точки далеко впереди вдоль плоскости (в ОБЕ стороны) — их проекции
        // дают направление линии горизонта полотна на экране.
        const P1 = [O[0] + AU[0] * s + AV[0] * s, O[1] + AU[1] * s + AV[1] * s, O[2] + AU[2] * s + AV[2] * s];
        const P2 = [O[0] - AU[0] * s + AV[0] * s, O[1] - AU[1] * s + AV[1] * s, O[2] - AU[2] * s + AV[2] * s];

        const d1 = dirScreen([P1[0] - c.camX, P1[1] - c.camY, P1[2] - c.camZ]);
        const d2 = dirScreen([P2[0] - c.camX, P2[1] - c.camY, P2[2] - c.camZ]);
        if (!d1 || !d2) return;

        // Линия через две точки; проводим её по всей ширине экрана.
        const dxs = d1.u - d2.u, dys = d1.v - d2.v;
        if (Math.abs(dxs) < 1e-9 && Math.abs(dys) < 1e-9) return;

        let yL, yR;
        if (Math.abs(dxs) >= Math.abs(dys)) {
            const k = dys / dxs;
            yL = d1.v + k * (0 - d1.u);
            yR = d1.v + k * (W - d1.u);
        } else {
            // Почти вертикальная — рисуем вертикаль через x первой точки.
            yL = yR = d1.v;
        }
        if (!Number.isFinite(yL) || !Number.isFinite(yR)) return;

        ctx.save();
        ctx.strokeStyle = CFG.planeHorizonColor;
        ctx.lineWidth = CFG.planeHorizonPixels;
        ctx.beginPath();
        ctx.moveTo(0, yL);
        ctx.lineTo(W, yR);
        ctx.stroke();

        // Подпись у правого края — чтобы не путать с подписью голубого горизонта.
        const txt = CFG.planeHorizonLabel;
        ctx.font = CFG.horizonLabelFont;
        ctx.textAlign = 'right';
        ctx.textBaseline = 'middle';
        const tw = ctx.measureText(txt).width;
        ctx.fillStyle = 'rgba(0,0,0,0.55)';
        ctx.fillRect(W - 14 - tw - 5, yR - CFG.horizonLabelFontSize / 2 - 3, tw + 10, CFG.horizonLabelFontSize + 6);
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = 'rgba(0,0,0,0.9)';
        ctx.lineJoin = 'round';
        ctx.strokeText(txt, W - 14, yR);
        ctx.fillStyle = CFG.planeHorizonColor;
        ctx.fillText(txt, W - 14, yR);
        ctx.restore();
    }

    // ================================================================
    // v1.0.40.37: ОСИ ШАССИ — ОРИЕНТАЦИЯ ГРУЗОВИКА ОТНОСИТЕЛЬНО ГОРИЗОНТА.
    //
    // Камера ETS2 компенсирует наклон (голова выравнивается), поэтому по камере
    // крен/питч ГРУЗОВИКА не виден. Оси шасси приходят отдельно (chassisForward/
    // chassisRight/chassisUp ОТ КУЗОВА, не от камеры) и рисуются через центр
    // экрана: их наклон к краям экрана = реальный наклон кузова к горизонту.
    // ================================================================
    function drawChassisAxes(cam) {
        const c = cam || ar;
        const gp = c.groundPlane;
        if (!CFG.showChassisAxes || !c.cameraValid || !gp) return;
        if (!gp.chassisForward || !gp.chassisRight || !gp.chassisUp) return;

        const fovH = Math.max(10, Math.min(170, Number(c.fovDeg) || CFG.fovDeg));
        const fovV = Math.max(10, Math.min(170, Number(c.fovDegVertical) || CFG.fovDegVertical));
        const fh = (W * 0.5) / Math.tan((fovH * Math.PI / 180) / 2);
        const fv = (H * 0.5) / Math.tan((fovV * Math.PI / 180) / 2);
        const cx = W * (c.projectionCenterX || 0.5);
        const cy = H * (c.projectionCenterY || 0.5);
        const F = c.cameraForward, Rg = c.cameraRight, U = c.cameraUp;

        // Проекция НАПРАВЛЕНИЯ в экранный вектор (как смещение от центра).
        function dir(d) {
            const zc = d.x * F.x + d.y * F.y + d.z * F.z;
            if (Math.abs(zc) < 1e-6) return null;
            return {
                x: fh * ((d.x * Rg.x + d.y * Rg.y + d.z * Rg.z) / zc),
                y: -fv * ((d.x * U.x + d.y * U.y + d.z * U.z) / zc)
            };
        }

        function axis(d, color, label) {
            const v = dir(d);
            if (!v) return;
            const len = Math.hypot(v.x, v.y);
            if (!(len > 1e-6)) return;
            const k = CFG.chassisAxisLenPx / len;
            const ex = cx + v.x * k, ey = cy + v.y * k;
            const sx = cx - v.x * k, sy = cy - v.y * k;

            ctx.save();
            ctx.strokeStyle = color;
            ctx.lineWidth = CFG.chassisAxisWidth;
            ctx.beginPath();
            ctx.moveTo(sx, sy);
            ctx.lineTo(ex, ey);
            ctx.stroke();

            if (CFG.chassisAxisLabel) {
                ctx.font = '700 11px Consolas, monospace';
                ctx.textAlign = 'left';
                ctx.textBaseline = 'middle';
                ctx.lineWidth = 2.5;
                ctx.strokeStyle = 'rgba(0,0,0,0.9)';
                ctx.lineJoin = 'round';
                ctx.strokeText(label, ex + 6, ey);
                ctx.fillStyle = color;
                ctx.fillText(label, ex + 6, ey);
            }
            ctx.restore();
        }

        ctx.save();
        // Центр — точка, вокруг которой строятся оси.
        ctx.strokeStyle = 'rgba(255,255,255,0.85)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(cx, cy, 3.5, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();

        axis(gp.chassisRight, CFG.chassisAxisXColor, 'X шасси');
        axis(gp.chassisForward, CFG.chassisAxisZColor, 'Z шасси');
        axis(gp.chassisUp, CFG.chassisAxisYColor, 'Y кузова');
    }

    // ================================================================
    // ДИСТАНЦИЯ ДО ЗЕМЛИ ПОД МИКРОТОЧКОЙ ПРИЦЕЛА
    // ================================================================
    // v1.0.40.31: луч — РЕАЛЬНЫЙ CameraForward, земля — РЕАЛЬНАЯ ПЛОСКОСТЬ
    // ДОРОГИ (по колёсам). Прежний вариант использовал горизонтальную плоскость
    // на высоте опорной точки фуры (truckY) и на уклонах давал ошибку.
    // Возвращает ГОРИЗОНТАЛЬНУЮ дистанцию (м) до точки пересечения.
    function groundDistanceFromCrosshair(cam) {
        const c = cam || ar;
        if (!c.cameraValid) return NaN;

        const gp = c.groundPlane;
        if (!GroundPlane.valid(gp)) return NaN;

        const f = c.cameraForward;
        const hit = GroundPlane.intersectRay(gp, c.camX, c.camY, c.camZ, f.x, f.y, f.z);
        if (!hit) return NaN;                     // взгляд выше горизонта / позади

        return Math.hypot(hit.x - c.camX, hit.z - c.camZ);
    }

    // ================================================================
    // v1.0.40.33: СЕТКА — ПРОСТО ПЛОСКОСТЬ БЕЗ ОГРАНИЧЕНИЙ.
    //
    // УБРАНО по требованию пользователя: круг R=150 м, fade 125…150 м по радиусу,
    // граница-окружность, «шахматные» эффекты, расширение к камере (CameraPadM).
    // Теперь это обычная плоская сетка:
    //   • каждая 1 м   — тонкая белая;
    //   • каждая 10 м  — толстая оранжевая;
    //   • каждая 50 м  — красная (приоритет над 10 м);
    //   • линии гаснут по ЭКРАННОЙ вертикали и по РАССТОЯНИЮ ДО КАМЕРЫ
    //     (gridFarStartM…gridFarEndM) — это мягкое отсечение дали, а не граница.
    // Оси — из GroundPlane.AxisU/AxisV; центр — точка под камерой (камера
    // проецируется на плоскость). Так сетка всегда покрывает низ экрана.
    // ================================================================
    function drawGroundGrid(cam) {
        const c = cam || ar;
        if (!CFG.showGroundGrid || !c.cameraValid) return;

        const gp = c.groundPlane;
        if (!GroundPlane.valid(gp)) return;

        const farStart = Number(CFG.gridFarStartM) || 220;
        const farEnd = Number(CFG.gridFarEndM) || 320;

        // Центр сетки — точка ПОД КАМЕРОЙ (проекция камеры на плоскость) → в (u,v).
        // Раньше центром была опорная точка фуры; камера стоит выше и сзади, поэтому
        // у низа экрана сетки не было. Привязка к камере это устраняет без «кругов».
        const center = GroundPlane.snapToPlane(gp, c.camX, c.camZ);
        if (!center) return;
        const uv0 = GroundPlane.projectToGrid(gp, center.x, center.y, center.z);
        if (!uv0 || !Number.isFinite(uv0.u) || !Number.isFinite(uv0.v)) return;

        const u0 = Math.round(uv0.u);
        const v0 = Math.round(uv0.v);

        // Запас сетки ЗА камерой (м) — чтобы низ экрана был покрыт без «дыры».
        const behind = Number(CFG.gridBehindCameraM) || 80;
        // Насколько далеко вперёд тянуть линии: от центра до farEnd + небольшой запас.
        const ahead = farEnd + 20;

        // ---- ПРОЕКЦИЯ УЗЛА (u,v) ПЛОСКОСТИ В ЭКРАН — БЕЗ АЛЛОКАЦИЙ ----
        // Прямая линия В ПЛОСКОСТИ остаётся прямой на экране (pinhole), поэтому
        // для отрезка достаточно ДВУХ проекций — это и есть основа скорости.
        const gpO = gp.origin, gpU = gp.axisU, gpV = gp.axisV;
        const camF = c.cameraForward, camR = c.cameraRight, camUp = c.cameraUp;
        const focalH = (W * 0.5) / Math.tan((Number(c.fovDeg || CFG.fovDeg) * Math.PI / 180) / 2);
        const focalV = (H * 0.5) / Math.tan((Math.max(1, Number(c.fovDegVertical) || CFG.fovDegVertical) * Math.PI / 180) / 2);
        const scx = W * (c.projectionCenterX || 0.5);
        const scy = H * (c.projectionCenterY || 0.5);
        const camX = c.camX, camY = c.camY, camZ = c.camZ;
        const out = [0, 0];
        const world = [0, 0, 0];
        function worldOf(u, v) {
            world[0] = gpO[0] + gpU[0] * u + gpV[0] * v;
            world[1] = gpO[1] + gpU[1] * u + gpV[1] * v;
            world[2] = gpO[2] + gpU[2] * u + gpV[2] * v;
        }
        function projUV(u, v) {
            worldOf(u, v);
            const dx = world[0] - camX, dy = world[1] - camY, dz = world[2] - camZ;
            const depth = dx * camF.x + dy * camF.y + dz * camF.z;
            if (!(depth > 0.5)) return false;
            const rdot = dx * camR.x + dy * camR.y + dz * camR.z;
            const udot = dx * camUp.x + dy * camUp.y + dz * camUp.z;
            out[0] = scx + focalH * (rdot / depth);
            out[1] = scy - focalV * (udot / depth);
            return Number.isFinite(out[0]) && Number.isFinite(out[1]);
        }

        // Гашение по расстоянию ОТ КАМЕРЫ (не по радиусу вокруг точки!).
        function farFade(dist) {
            if (dist >= farEnd) return 0;
            if (dist <= farStart) return 1;
            return (farEnd - dist) / (farEnd - farStart);
        }

        // Вертикальное затухание: близко к низу экрана ярче, к горизонту гаснет.
        function vertAlpha(vPx) {
            const centerPx = H / 2;
            const below25 = centerPx + H * 0.25;
            if (vPx <= below25) return 1;
            return Math.max(0, Math.min(1, 1 - (vPx - below25) / Math.max(1, H - below25)));
        }

        function styleFor(idx) {
            if (((idx % 50) + 50) % 50 === 0) return { color: CFG.gridColor50M, width: CFG.gridWidth50M, alpha: 0.95 };
            if (((idx % 10) + 10) % 10 === 0) return { color: CFG.gridColor10M, width: CFG.gridWidth10M, alpha: 0.85 };
            return { color: CFG.gridColor1M, width: CFG.gridWidth1M, alpha: 0.42 };
        }

        // Отрезок линии: 2 проекции + обводка. alphaScale передаётся вызывающим
        // (он знает расстояние от камеры до середины).
        function seg(uA, vA, uB, vB, style, farScale) {
            if (farScale <= 0.01) return;
            if (!projUV(uA, vA)) return;
            const x1 = out[0], y1 = out[1];
            if (!projUV(uB, vB)) return;
            const x2 = out[0], y2 = out[1];
            // Клип: отрезок целиком за пределами экрана не рисуем.
            if ((y1 < -8 && y2 < -8) || (y1 > H + 8 && y2 > H + 8)) return;
            if ((x1 < -8 && x2 < -8) || (x1 > W + 8 && x2 > W + 8)) return;
            const va = vertAlpha((y1 + y2) * 0.5);
            const a = style.alpha * farScale * va;
            if (a <= 0.012) return;
            ctx.globalAlpha = a;
            ctx.strokeStyle = style.color;
            ctx.lineWidth = style.width;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.stroke();
        }

        // Расстояние от камеры до середины отрезка (в мировых метрах).
        function midDist(uA, vA, uB, vB) {
            const mu = (uA + uB) * 0.5, mv = (vA + vB) * 0.5;
            worldOf(mu, mv);
            const dx = world[0] - camX, dy = world[1] - camY, dz = world[2] - camZ;
            return Math.sqrt(dx * dx + dy * dy + dz * dz);
        }

        // ---- ЛИНИЯ ВДОЛЬ ОСИ V (постоянное u) ----
        // Линия уходит от −behind до +ahead в обе стороны экрана. Чтобы получить
        // корректное затухание по расстоянию, режем её на несколько отрезков.
        function lineConstantU(iu) {
            const style = styleFor(iu);
            const total = behind + ahead;
            const pieces = 10;                  // ~равные куски по длине
            const stepLen = total / pieces;
            let prevV = -behind;
            for (let k = 1; k <= pieces; k++) {
                const curV = -behind + stepLen * k;
                const d = midDist(iu, prevV, iu, curV);
                seg(iu, prevV, iu, curV, style, farFade(d));
                prevV = curV;
            }
        }

        // ---- ЛИНИЯ ВДОЛЬ ОСИ U (постоянное v) ----
        function lineConstantV(iv) {
            const style = styleFor(iv);
            const total = behind + ahead;
            const pieces = 10;
            const stepLen = total / pieces;
            let prevU = -behind;
            for (let k = 1; k <= pieces; k++) {
                const curU = -behind + stepLen * k;
                const d = midDist(prevU, iv, curU, iv);
                seg(prevU, iv, curU, iv, style, farFade(d));
                prevU = curU;
            }
        }

        // Диапазон индексов: покрываем экран с запасом, но БЕЗ бесконечных циклов.
        // Полоса ahead покрывает даль вперёд, behind — низ экрана под камерой.
        const uFrom = -Math.ceil(behind), uTo = Math.ceil(ahead);
        const vFrom = -Math.ceil(behind), vTo = Math.ceil(ahead);

        ctx.save();
        ctx.lineCap = 'butt';
        for (let iu = u0 + uFrom; iu <= u0 + uTo; iu++) lineConstantU(iu);
        for (let iv = v0 + vFrom; iv <= v0 + vTo; iv++) lineConstantV(iv);
        ctx.restore();
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
        const txt = Number(ar.fovDeg || CFG.fovDeg).toFixed(0) + '° / ' +
            Number(ar.fovDegVertical || CFG.fovDegVertical).toFixed(0) + '°';
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
        const gp = c.groundPlane;
        const planeLine = GroundPlane.valid(gp)
            ? 'PLANE ok N=' + gp.normal.map(fmt).join(',') + ' refH=' + gp.referenceHeight.toFixed(2) +
              ' maxRes=' + gp.maxResidual.toFixed(4)
            : 'PLANE: нет (колёса не приняты)';
        const lines = [
            'CAM: ' + c.camX.toFixed(2) + ' ' + c.camY.toFixed(2) + ' ' + c.camZ.toFixed(2),
            'FWD: ' + fmt(f.x) + ' ' + fmt(f.y) + ' ' + fmt(f.z),
            'UP:  ' + fmt(u2.x) + ' ' + fmt(u2.y) + ' ' + fmt(u2.z),
            // v1.0.40.32: печатаем ОБА FOV — по разнице между ними и ловился
            // «уплывающий» горизонт (один focal по X и Y давал vFov ≈ 50.5°
            // вместо измеренных ≈ 30.7°).
            'FOV: H ' + Number(c.fovDeg).toFixed(1) + '°  V ' + Number(c.fovDegVertical).toFixed(1) + '°',
            planeLine
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
        // ============================================================
        // v1.0.40.44: СГЛАЖЕННАЯ ПОЗА КАМЕРЫ (корень «точки рывками»).
        // Телеметрия приходит ~28–35 Гц, рендер идёт 60+ Гц. Раньше поза
        // применялась ТОЛЬКО в момент пакета → между пакетами точки стояли,
        // затем скачок («как на 14 fps» при счётчике 60–100).
        // Теперь каждый кадр считаем сглаженную позу `view` и используем ЕЁ
        // в проекции. Базис интерполируется с нормализацией (ортонормальность
        // сохраняется — иначе «плыл» бы горизонт).
        // ============================================================
        const view = smoothCamera(fNow);
        const camUsable = !!(view && view.cameraValid);

        let groundDist = NaN;
        if (camUsable) {
            // v1.0.40.31: сетка на РЕАЛЬНОЙ плоскости дороги — рисуется ПЕРВОЙ
            // (под горизонтом, дистанцией, метками). Живёт на groundPlane.
            drawGroundGrid(view);
            drawWorldHorizon(view);
            drawPlaneHorizon(view);
            drawChassisAxes(view);
            drawHorizonLabel(view);
            groundDist = groundDistanceFromCrosshair(view);
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
            if (CFG.showDebugDot && camUsable) drawCameraDebugDot(view);
            drawClipFrame();
        }

        // ============================================================
        // ПОМЕТКА / НОВАЯ ТОЧКА (pin) — проекция на СГЛАЖЕННОЙ позе (плавно).
        // ============================================================
        if (camUsable && ar.pin) {
            const pPr = projectPoint({ x: ar.pin.x, y: ar.pin.y, z: ar.pin.z }, view);
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
                const f = view.cameraForward;
                drawOutlinedText(
                    'cam ' + view.camX.toFixed(1) + ' ' + view.camY.toFixed(1) + ' ' + view.camZ.toFixed(1) +
                    ' · fwd ' + f.x.toFixed(2) + ' ' + f.y.toFixed(2) + ' ' + f.z.toFixed(2),
                    pu, pv + 36, '11px Consolas, monospace',
                    'rgba(255,220,120,0.9)', 0.9);
            }
        }

        if (!camUsable || !ar.points || ar.points.length === 0) return;

        // ============================================================
        // v1.0.40.40: РИСУЕМ ВСЕ ТОЧКИ В РАДИУСЕ (требование пользователя).
        // Список отсортирован приложением: цели первыми, затем по дистанции —
        // порядок стабильный, без «мигания» при перерисовке.
        // Для каждой точки: маркер/перекрестье, подпись и указатель за экраном.
        // Ближайшая (первая) дополнительно даёт ar.sel — точку для отслеживания.
        // ============================================================
        ar.sel = null;
        const drawn = [];
        for (let i = 0; i < ar.points.length; i++) {
            const pt = ar.points[i];
            if (!pt) continue;

            const pr = projectPoint(pt, view);
            // Infinity — точка ровно сзади/сбоку: фиксируем направление к краям.
            if (!Number.isFinite(pr.u)) pr.u = pr.u > 0 ? (W - CFG.edgeMargin) : CFG.edgeMargin;
            if (!Number.isFinite(pr.v)) pr.v = pr.v > 0 ? (H - CFG.edgeMargin) : CFG.edgeMargin;
            const cl = clampToScreen(pr.u, pr.v, !pr.inFront);

            if (i === 0) {
                ar.sel = { u: cl.u, v: cl.v, clamped: cl.clamped, inFront: pr.inFront };
            }

            const color = colorFor(pt.kind, pt.color, pt.category);
            // Размер/прозрачность по дистанции ДО ЭТОЙ точки (не до ближайшей).
            const sa = sizeAlphaFor(pr.dist);
            const txtAlpha = Math.max(0, Math.min(1, sa.alpha));
            if (txtAlpha <= 0.03) continue;      // слишком далеко — не рисуем

            const distText = fmtDist(pr.dist);
            const name = (pt.realName || pt.gameName);

            // Подписи рисуем НЕ ВСЕМ точкам (иначе при плотной застройке экран
            // превращается в текст): полные подписи — только цели и точки,
            // близкие/крупные; остальным — короткая подпись имени.
            const fullLabel = pt.isTarget || pr.dist <= 25;

            ctx.save();
            ctx.globalAlpha = txtAlpha;

            if (cl.clamped) {
                // ВНЕ ЭКРАНА: точку/перекрестье НЕ рисуем — указатель + подпись.
                const lu = cl.u, lv = cl.bottom ? (cl.v - 58) : (cl.v + CFG.labelDy);
                drawOutlinedText(name + '  \u00B7  ' + distText,
                    lu, lv, '600 14px "Segoe UI", Arial',
                    'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
                if (fullLabel && pt.gameName && pt.gameName !== name) {
                    drawOutlinedText(pt.gameName, lu, lv + 19,
                        '12px "Segoe UI", Arial',
                        'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
                }
                drawEdgeArrow(cl.u, cl.v, color, cl.bottom === true);
            } else {
                drawMarkerToward(cl.u, cl.v, color, sa.size, txtAlpha);
                if (fullLabel) {
                    drawOutlinedText(name + '  \u00B7  ' + distText,
                        cl.u, cl.v + CFG.labelDy, '600 14px "Segoe UI", Arial',
                        'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
                    if (pt.gameName && pt.gameName !== name) {
                        drawOutlinedText(pt.gameName, cl.u, cl.v + CFG.labelDy + 19,
                            '12px "Segoe UI", Arial',
                            'rgba(255,255,255,' + (0.85 * txtAlpha).toFixed(2) + ')', txtAlpha);
                    }
                } else {
                    // Компактная подпись: только имя под маркером.
                    drawOutlinedText(name, cl.u, cl.v + CFG.labelDy,
                        '600 12px "Segoe UI", Arial',
                        'rgba(255,255,255,' + txtAlpha.toFixed(2) + ')', txtAlpha);
                }
                drawCrosshair(cl.u, cl.v, color, (sa.size / 33) * CFG.crosshairScale);
            }
            ctx.restore();
            drawn.push(pt.gameName);
        }
    }
    requestAnimationFrame(render);
})();