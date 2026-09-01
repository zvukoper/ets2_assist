// ================================================================
// ПОЛУЧЕНИЕ DOM-ЭЛЕМЕНТОВ (глобальные)
// ================================================================
const minimapCanvas = document.getElementById('minimapCanvas');
const ctx = minimapCanvas.getContext('2d');
const minimapContainer = document.getElementById('minimapContainer');
const labelLayer = document.getElementById('labelLayer');
const toastMsg = document.getElementById('toastMsg');

const truckX = document.getElementById('truckX');
const truckY = document.getElementById('truckY');
const truckZ = document.getElementById('truckZ');
const targetX = document.getElementById('targetX');
const targetY = document.getElementById('targetY');
const targetZ = document.getElementById('targetZ');
const headingDisplay = document.getElementById('headingDisplay');
const telemetryStatus = document.getElementById('telemetryStatus');
const telemetryDot = document.getElementById('telemetryDot');
const heightIndicatorUp = document.getElementById('heightIndicatorUp');
const heightIndicatorDown = document.getElementById('heightIndicatorDown');
const zoomLabel = document.getElementById('zoomLabel');
const rawHeadingValue = document.getElementById('rawHeadingValue');
const stepInput = document.getElementById('stepInput');
const stepDisplay = document.getElementById('stepDisplay');
const targetDistLabel = document.getElementById('targetDistLabel');

// ================================================================
// ПЕРЕМЕННЫЕ КАРТЫ
// ================================================================
let W, H;
let lastCanvasCssW = 0, lastCanvasCssH = 0, lastCanvasDpr = 1;
let centerX = 0, centerZ = 0, scale = 1;
let dragStartX = 0, dragStartY = 0, dragStartCX = 0, dragStartCZ = 0, isDragging = false;
let targetCenterX = 0, targetCenterZ = 0;

// ================================================================
// ЦВЕТА ДЛЯ КАТЕГОРИЙ POI
// ================================================================
const POI_COLORS = {
    'city': '#ffdd88',
    'village': '#ddcc88',
    'gas_station': '#ff8800',
    'rest_stop': '#44aaff',
    'service': '#88ddff',
    'parking': '#aaddaa',
    'viewpoint': '#ffaa88',
    'industry': '#cc88ff',
    'custom': '#ffffff',
    'default': '#66bbff'
};

// ================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ (зависят от текущего состояния)
// ================================================================
function worldToScreen(wx, wz) {
    const dx = (wx - centerX) * scale;
    const dz = (wz - centerZ) * scale;
    return { x: W/2 + dx, y: H/2 - dz };
}

function fitMap() {
    if (trailPoints.length < 2) return;
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of trailPoints) {
        if (p.x < minX) minX = p.x;
        if (p.x > maxX) maxX = p.x;
        if (p.z < minZ) minZ = p.z;
        if (p.z > maxZ) maxZ = p.z;
    }
    centerX = (minX + maxX) / 2;
    centerZ = (minZ + maxZ) / 2;
    targetCenterX = centerX;
    targetCenterZ = centerZ;
    const range = Math.max(maxX - minX, maxZ - minZ, 1);
    scale = (Math.min(W, H) * 0.85) / (range * 1.15);
}

function resize() {
    drawMinimap();
}

// ================================================================
// ОСНОВНАЯ ФУНКЦИЯ ОТРИСОВКИ
// ================================================================
function drawMinimap() {
    const rect = minimapContainer.getBoundingClientRect();
    const w = rect.width, h = rect.height;
    if (w === 0 || h === 0) return;
    const dpr = window.devicePixelRatio || 1;
    if (w !== lastCanvasCssW || h !== lastCanvasCssH || dpr !== lastCanvasDpr) {
        minimapCanvas.width = Math.max(1, Math.round(w * dpr));
        minimapCanvas.height = Math.max(1, Math.round(h * dpr));
        minimapCanvas.style.width = w + 'px';
        minimapCanvas.style.height = h + 'px';
        lastCanvasCssW = w;
        lastCanvasCssH = h;
        lastCanvasDpr = dpr;
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    labelLayer.innerHTML = '';

    const truckPos = { x: parseFloat(truckX.value) || 0, z: parseFloat(truckZ.value) || 0 };
    const targetPos = { x: parseFloat(targetX.value) || 0, z: parseFloat(targetZ.value) || 0 };
    const heading = state.truck.heading || 0;

    const isTargetActive = !(Math.abs(targetPos.x) < 0.01 && Math.abs(targetPos.z) < 0.01);
    state.targetActive = isTargetActive;

    const centerX2 = truckPos.x;
    const centerZ2 = truckPos.z;

    let targetScale = 1;
    const useOverview = state.targetMapOverview === true;

    if (useOverview) {
        // Обзор охватывает ВСЕ активные цели (stash с active:false исключается).
        state.zoomOnMapTargets = state.customTargets.concat(state.randomTargets)
            .filter(t => t.active !== false)
            .map(t => ({ x: t.x, z: t.z }));
        const zoomTargets = state.zoomOnMapTargets;
        if (zoomTargets.length > 0) {
            let maxDist = 0;
            for (const t of zoomTargets) {
                const dx = Math.abs(t.x - truckPos.x);
                const dz = Math.abs(t.z - truckPos.z);
                const dist = Math.sqrt(dx*dx + dz*dz);
                if (dist > maxDist) maxDist = dist;
            }
            const visibleSize = Math.min(w, h) * 0.8;
            targetScale = maxDist / (visibleSize * 0.4);
        } else {
            const autoScale = computeAutoScale(state.speed);
            state.autoScale = autoScale;
            targetScale = autoScale * state.manualScaleFactor;
        }
    } else {
        const autoScale = computeAutoScale(state.speed);
        state.autoScale = autoScale;
        targetScale = autoScale * state.manualScaleFactor;
    }

    const smoothFactor = 0.12;
    state.currentScale += (targetScale - state.currentScale) * smoothFactor;
    if (state.currentScale < 0.001) state.currentScale = 0.001;
    if (state.currentScale > 1000) state.currentScale = 1000;

    const scale2 = state.currentScale;
    state.scale = scale2;

    // Переопределяем worldToScreen для локального использования
    function worldToScreen2(wx, wz) {
        let dx = (wx - centerX2) / scale2;
        let dz = (wz - centerZ2) / scale2;
        dx = -dx;
        const angle = -heading + Math.PI;
        const cos = Math.cos(angle);
        const sin = Math.sin(angle);
        const rx = dx * cos - dz * sin;
        const ry = dx * sin + dz * cos;
        return { x: w/2 + rx, y: h/2 - ry };
    }

    // --- Фон (статический, без смены по времени суток) ---
    ctx.fillStyle = '#0f1217';
    ctx.fillRect(0, 0, w, h);

    // Сетка
    const gridStep = getGridStep(scale2, Math.min(w, h));
    ctx.strokeStyle = '#3a4a5a';
    ctx.lineWidth = 0.5;
    ctx.setLineDash([4, 6]);

    const halfSize = (Math.min(w, h) / 2) * scale2;
    const worldMinX = centerX2 - halfSize;
    const worldMaxX = centerX2 + halfSize;
    const worldMinZ = centerZ2 - halfSize;
    const worldMaxZ = centerZ2 + halfSize;

    let startX = Math.ceil(worldMinX / gridStep) * gridStep;
    for (let x = startX; x <= worldMaxX; x += gridStep) {
        const p1 = worldToScreen2(x, worldMinZ);
        const p2 = worldToScreen2(x, worldMaxZ);
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.stroke();
    }
    let startZ = Math.ceil(worldMinZ / gridStep) * gridStep;
    for (let z = startZ; z <= worldMaxZ; z += gridStep) {
        const p1 = worldToScreen2(worldMinX, z);
        const p2 = worldToScreen2(worldMaxX, z);
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.stroke();
    }
    ctx.setLineDash([]);

    ctx.font = '12px "Segoe UI", sans-serif';
    ctx.fillStyle = '#8fa0b9';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'bottom';
    ctx.fillText(`1 клетка = ${gridStep} м`, 8, h-8);
    ctx.textBaseline = 'top';
    ctx.fillText(`Масштаб: ${scale2.toFixed(1)} м/px`, 8, 8);

    // Дороги
    for (const seg of state.roads) {
        const p1 = worldToScreen2(seg.x1, seg.z1);
        const p2 = worldToScreen2(seg.x2, seg.z2);
        const width = roadWidthStyles[seg.roadType] || roadWidthStyles['default'];
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.strokeStyle = '#5a7a8a';
        ctx.lineWidth = width;
        ctx.globalAlpha = 0.8;
        ctx.stroke();
        ctx.globalAlpha = 1.0;
    }

    // Города
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    // POI (с цветами по категориям) — РИСУЕМ ПЕРВЫМИ (города и цели рисуются поверх)
    // Безопасный доступ: если points_overrides.js не загрузился (гонка map_ready),
    // используем state напрямую — иначе ReferenceError убивал всю отрисовку ПОСЛЕ дорог.
    const poiList = (typeof getEffectivePoiList === 'function') ? getEffectivePoiList() : (state.pois || []);
    const cityList = (typeof getEffectiveCityList === 'function') ? getEffectiveCityList() : (state.cities || []);
    const showPoiLabels = scale2 <= 5.0;
    const poiPointSize = Math.max(2, 6 / (scale2 / 10));
    for (const poi of poiList) {
        const p = worldToScreen2(poi.x, poi.z);
        if (p.x < -10 || p.x > w+10 || p.y < -10 || p.y > h+10) continue;
        const size = Math.min(6, poiPointSize);
        ctx.beginPath();
        ctx.arc(p.x, p.y, size, 0, 2*Math.PI);
        // Выбираем цвет по категории; у пользовательских точек — свой цвет из редактора
        const category = poi.type || 'default';
        const color = (category === 'custom' && poi.color) ? poi.color : (POI_COLORS[category.toLowerCase()] || POI_COLORS['default']);
        ctx.fillStyle = color;
        ctx.shadowColor = color + '44';
        ctx.shadowBlur = 6;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.lineWidth = 2;
        ctx.strokeStyle = '#000000';
        ctx.stroke();
        if (showPoiLabels) {
            ctx.font = '9px "Segoe UI", sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';
            ctx.fillStyle = '#aabbcc';
            ctx.shadowColor = 'rgba(0,0,0,0.7)';
            ctx.shadowBlur = 4;
            ctx.fillText(poi.name || poi.type || 'poi', p.x, p.y - size - 2);
            ctx.shadowBlur = 0;
        }
    }

    // Города (поверх POI)
    for (const city of cityList) {
        const p = worldToScreen2(city.x, city.z);
        if (p.x < -10 || p.x > w+10 || p.y < -10 || p.y > h+10) continue;
        ctx.beginPath();
        ctx.arc(p.x, p.y, 4, 0, 2*Math.PI);
        ctx.fillStyle = '#ffdd88';
        ctx.shadowColor = '#ffdd8844';
        ctx.shadowBlur = 6;
        ctx.fill();
        ctx.shadowBlur = 0;
    }

    // Шлейф
    if (state.trail.length > 1) {
        for (let i = 1; i < state.trail.length; i++) {
            const parts1 = state.trail[i-1].p.split(' ');
            const parts2 = state.trail[i].p.split(' ');
            const p1 = worldToScreen2(parseFloat(parts1[0]), parseFloat(parts1[1]));
            const p2 = worldToScreen2(parseFloat(parts2[0]), parseFloat(parts2[1]));
            const speed = parseFloat(state.trail[i].s) || 0;
            const color = getTrailColor(speed);
            ctx.beginPath();
            ctx.moveTo(p1.x, p1.y);
            ctx.lineTo(p2.x, p2.y);
            ctx.strokeStyle = color;
            ctx.lineWidth = 3.5;
            ctx.shadowColor = 'rgba(0,0,0,0.5)';
            ctx.shadowBlur = 4;
            ctx.stroke();
            ctx.shadowBlur = 0;
        }
    }

    // КОНУС ОБЗОРА на миникарте (v74): длина в ПРОЦЕНТАХ размера миникарты
    // (НЕ зависит от зума/метров). 100% = от фуры до края по направлению.
    //   питч головы: |deg| <= 8°  → стандарт 30%;
    //                > 20° вверх → до 90%;
    //                > 35° вниз  → минимум 10%.
    // (между порогами — линейная интерполяция; положит. offset[4] = вверх, v67-эмпирика)
    // v102: ПОЛУУГОЛ конуса = |питч взгляда| (формула из ar_head_ground.csv:
    //   pitchDeg = atan(eyeH/dist), eyeH≈1.9м). Чем ближе земля — тем шире конус.
    const VIEW_CONE_STD_PCT  = 30;          // стандартная длина, % радиуса экрана
    const VIEW_CONE_UP_PCT   = 90;          // макс при взгляде вверх (>20°)
    const VIEW_CONE_DOWN_PCT = 10;          // минимум при взгляде вниз (>35°)
    const VIEW_CONE_FILL = 'rgba(255,210,90,0.16)';
    const VIEW_CONE_STROKE = 'rgba(255,210,90,0.45)';
    const truckScreen = worldToScreen2(truckPos.x, truckPos.z); // (v72: ДО конуса)
    if (state.truck.heading !== undefined && (Math.abs(truckPos.x) + Math.abs(truckPos.z)) > 0.01) {
        const headPitchDeg = (Array.isArray(state.headOffset) ? (Number(state.headOffset[4]) || 0) : 0) * 360;
        // Длина в % экранного радиуса (расстояние фура→край по направлению = 100%).
        let pct = VIEW_CONE_STD_PCT;
        if (headPitchDeg > 8) {
            const k = Math.min(1, (headPitchDeg - 8) / Math.max(1, 20 - 8));   // 8..20°
            pct = VIEW_CONE_STD_PCT + (VIEW_CONE_UP_PCT - VIEW_CONE_STD_PCT) * k;
        } else if (headPitchDeg < -8) {
            const k = Math.min(1, (-headPitchDeg - 8) / Math.max(1, 35 - 8));  // -8..-35°
            pct = VIEW_CONE_STD_PCT + (VIEW_CONE_DOWN_PCT - VIEW_CONE_STD_PCT) * k;
        }
        const dirAngle = state.truck.heading +
            (Array.isArray(state.headOffset) ? (Number(state.headOffset[3]) || 0) * Math.PI * 2 : 0);
        const cxT = truckScreen.x, cyT = truckScreen.y;
        // Экранный вектор «вперёд» (в экранных координатах) — через probe-точку.
        const probeDX = -Math.sin(dirAngle), probeDZ = -Math.cos(dirAngle);  // вперёд (мир)
        const probeScr = worldToScreen2(truckPos.x + probeDX * 100, truckPos.z + probeDZ * 100);
        const ux = probeScr.x - cxT, uy = probeScr.y - cyT;
        const ulen = Math.sqrt(ux * ux + uy * uy) || 1;
        // lenPx = pct% х расстояния до края миникарты (min(w,h)/2).
        const lenPx = (Math.min(w, h) / 2) * (pct / 100);
        const baseAng = Math.atan2(uy, ux);
        // v102: полуугол = |питч взгляда| (формула atan(eyeH/dist)), clamp 5..45°.
        const halfDeg = Math.min(45, Math.max(5, Math.abs(headPitchDeg)));
        const halfA = halfDeg * Math.PI / 180;
        if (Number.isFinite(baseAng) && Number.isFinite(lenPx) && lenPx > 1) {
            ctx.save();
            ctx.beginPath();
            ctx.moveTo(cxT, cyT);
            ctx.arc(cxT, cyT, lenPx, baseAng - halfA, baseAng + halfA);
            ctx.closePath();
            ctx.fillStyle = VIEW_CONE_FILL;
            ctx.fill();
            ctx.strokeStyle = VIEW_CONE_STROKE;
            ctx.lineWidth = 1;
            ctx.stroke();
            ctx.restore();
        }
    }

    // Грузовик
    ctx.save();
    ctx.translate(truckScreen.x, truckScreen.y);
    ctx.beginPath();
    ctx.moveTo(0, -18);
    ctx.lineTo(-12, 12);
    ctx.lineTo(0, 4);
    ctx.lineTo(12, 12);
    ctx.closePath();
    ctx.fillStyle = '#ff4d4d';
    ctx.shadowColor = '#ff4d4d88';
    ctx.shadowBlur = 12;
    ctx.fill();
    ctx.shadowBlur = 0;
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.restore();

    // ПОМЕТКА «Пометить в АР» на миникарте (v73): серый кружок + перекрестье,
    // как в редакторе карт — рисуется по state.arPinMap (команда ar_pin_map).
    if (state.arPinMap) {
        const pPin = worldToScreen2(state.arPinMap.x, state.arPinMap.z);
        ctx.save();
        ctx.strokeStyle = 'rgba(225,225,225,0.95)';
        ctx.fillStyle = 'rgba(160,160,160,0.95)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(pPin.x, pPin.y, 6, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(pPin.x - 13, pPin.y); ctx.lineTo(pPin.x + 13, pPin.y);
        ctx.moveTo(pPin.x, pPin.y - 13); ctx.lineTo(pPin.x, pPin.y + 13);
        ctx.stroke();
        ctx.restore();
    }

    // ---- Метки greedyPlacement (города, цели) ----
    const cx = w/2, cy = h/2;
    const margin = 30;
    const radius = Math.min(w, h)/2 - margin;

    const allTargets = state.customTargets.length > 0 ? state.customTargets : [];
    const currentTarget = {
        x: parseFloat(targetX.value) || 0,
        z: parseFloat(targetZ.value) || 0,
        name: 'Цель',
        active: true,
        color: '#ffc857',
    };
    const exists = allTargets.some(t => Math.abs(t.x - currentTarget.x) < 0.1 && Math.abs(t.z - currentTarget.z) < 0.1);
    if (!exists && (Math.abs(currentTarget.x) > 0.01 || Math.abs(currentTarget.z) > 0.01)) {
        allTargets.push(currentTarget);
    }

    const labelsData = [];
    // Города
    for (const city of state.cities) {
        const cityScreen = worldToScreen2(city.x, city.z);
        const visible = Math.abs(cityScreen.x - cx) < w/2 - 20 && Math.abs(cityScreen.y - cy) < h/2 - 20;
        if (visible) {
            const distReal = Math.sqrt((city.x - truckPos.x)**2 + (city.z - truckPos.z)**2);
            const distText = formatDistance(distReal);
            const label = `${city.name} (${distText})`;
            const charWidth = 11 * 0.6;
            const wLabel = Math.min(label.length * charWidth + 16, 180);
            const hLabel = 18;
            labelsData.push({
                x: cityScreen.x,
                y: cityScreen.y - 20,
                text: label,
                color: '#c8ddee',
                isActive: false,
                isCity: true,
                w: wLabel,
                h: hLabel,
                priority: 0,
            });
        }
    }
    // Неактивные цели
    for (const target of allTargets) {
        if (state.randomTargets && state.randomTargets.includes(target)) continue;
        if (target.active) continue;
        const tPos = { x: target.x, z: target.z };
        const distReal = Math.sqrt((tPos.x - truckPos.x)**2 + (tPos.z - truckPos.z)**2);
        const distText = formatDistance(distReal);
        const tScreen = worldToScreen2(tPos.x, tPos.z);
        const visible = Math.abs(tScreen.x - cx) < w/2 - 20 && Math.abs(tScreen.y - cy) < h/2 - 20;
        let color = '#88aadd';
        if (target.color && target.color !== 'default') {
            color = target.color;
        }
        if (visible) {
            ctx.beginPath();
            ctx.arc(tScreen.x, tScreen.y, 4, 0, 2*Math.PI);
            ctx.fillStyle = color;
            ctx.shadowColor = color + '88';
            ctx.shadowBlur = 8;
            ctx.fill();
            ctx.shadowBlur = 0;
            const label = `${target.name} (${distText})`;
            const charWidth = 10 * 0.65;
            const wLabel = Math.min(label.length * charWidth + 20, 200);
            const hLabel = 18;
            labelsData.push({
                x: tScreen.x,
                y: tScreen.y - 10,
                text: label,
                color: color,
                isActive: false,
                isCity: false,
                w: wLabel,
                h: hLabel,
                priority: 1,
            });
        } else {
            const dx = tScreen.x - cx;
            const dy = tScreen.y - cy;
            const len = Math.sqrt(dx*dx + dy*dy);
            if (len < 0.01) continue;
            const nx = dx / len;
            const ny = dy / len;
            const arrowX = cx + nx * radius;
            const arrowY = cy + ny * radius;
            const angle = Math.atan2(ny, nx);
            ctx.save();
            ctx.translate(arrowX, arrowY);
            ctx.rotate(angle);
            ctx.beginPath();
            ctx.moveTo(8, 0);
            ctx.lineTo(-5, -5);
            ctx.lineTo(-5, 5);
            ctx.closePath();
            ctx.fillStyle = color;
            ctx.shadowColor = 'rgba(0,0,0,0.6)';
            ctx.shadowBlur = 4;
            ctx.fill();
            ctx.strokeStyle = '#000';
            ctx.lineWidth = 1;
            ctx.stroke();
            ctx.shadowBlur = 0;
            ctx.restore();
            let labelX = arrowX;
            let labelY = (ny > 0) ? arrowY - 14 : arrowY + 18;
            if (labelX < 50) labelX = 50;
            if (labelX > w - 50) labelX = w - 50;
            if (labelY < 20) labelY = 20;
            if (labelY > h - 20) labelY = h - 20;
            const label = `${target.name} (${distText})`;
            const charWidth = 10 * 0.65;
            const wLabel = Math.min(label.length * charWidth + 20, 200);
            const hLabel = 18;
            labelsData.push({
                x: labelX,
                y: labelY,
                text: label,
                color: color,
                isActive: false,
                isCity: false,
                w: wLabel,
                h: hLabel,
                priority: 1,
            });
        }
    }
    // Активные цели
    for (const target of allTargets) {
        if (state.randomTargets && state.randomTargets.includes(target)) continue;
        if (!target.active) continue;
        const tPos = { x: target.x, z: target.z };
        const distReal = Math.sqrt((tPos.x - truckPos.x)**2 + (tPos.z - truckPos.z)**2);
        const distText = formatDistance(distReal);
        const tScreen = worldToScreen2(tPos.x, tPos.z);
        const visible = Math.abs(tScreen.x - cx) < w/2 - 20 && Math.abs(tScreen.y - cy) < h/2 - 20;
        let color = '#ffc857';
        if (target.color && target.color !== 'default') {
            color = target.color;
        }
        if (visible) {
            ctx.beginPath();
            ctx.arc(tScreen.x, tScreen.y, 6, 0, 2*Math.PI);
            ctx.fillStyle = color;
            ctx.shadowColor = color + '88';
            ctx.shadowBlur = 16;
            ctx.fill();
            ctx.shadowBlur = 0;
            ctx.strokeStyle = '#ffffff';
            ctx.lineWidth = 1.5;
            ctx.stroke();
            const label = `${target.name} (${distText})`;
            const charWidth = 13 * 0.65;
            const wLabel = Math.min(label.length * charWidth + 20, 200);
            const hLabel = 20;
            labelsData.push({
                x: tScreen.x,
                y: tScreen.y - 12,
                text: label,
                color: color,
                isActive: true,
                isCity: false,
                w: wLabel,
                h: hLabel,
                priority: 3,
            });
        } else {
            const dx = tScreen.x - cx;
            const dy = tScreen.y - cy;
            const len = Math.sqrt(dx*dx + dy*dy);
            if (len < 0.01) continue;
            const nx = dx / len;
            const ny = dy / len;
            const arrowX = cx + nx * radius;
            const arrowY = cy + ny * radius;
            const angle = Math.atan2(ny, nx);
            ctx.save();
            ctx.translate(arrowX, arrowY);
            ctx.rotate(angle);
            ctx.beginPath();
            ctx.moveTo(10, 0);
            ctx.lineTo(-6, -6);
            ctx.lineTo(-6, 6);
            ctx.closePath();
            ctx.fillStyle = color;
            ctx.shadowColor = 'rgba(0,0,0,0.6)';
            ctx.shadowBlur = 4;
            ctx.fill();
            ctx.strokeStyle = '#fff';
            ctx.lineWidth = 1.5;
            ctx.stroke();
            ctx.shadowBlur = 0;
            ctx.restore();
            let labelX = arrowX;
            let labelY = (ny > 0) ? arrowY - 16 : arrowY + 22;
            if (labelX < 50) labelX = 50;
            if (labelX > w - 50) labelX = w - 50;
            if (labelY < 20) labelY = 20;
            if (labelY > h - 20) labelY = h - 20;
            const label = `${target.name} (${distText})`;
            const charWidth = 13 * 0.65;
            const wLabel = Math.min(label.length * charWidth + 20, 200);
            const hLabel = 20;
            labelsData.push({
                x: labelX,
                y: labelY,
                text: label,
                color: color,
                isActive: true,
                isCity: false,
                w: wLabel,
                h: hLabel,
                priority: 3,
            });
        }
    }

    // ---- ГАРАНТИРОВАННАЯ отрисовка ВСЕХ случайных целей (state.randomTargets) ----
    // Рисуем напрямую из массива, минуя цепочку customTargets, чтобы точки всегда были видны.
    if (state.randomTargets && state.randomTargets.length) {
        for (const rt of state.randomTargets) {
            // Перекус скрыт на 5 мин после активации — не рисуем и не показываем указатель.
            if (rt.hiddenUntil && Date.now() < rt.hiddenUntil) continue;
            // Скрытая цель (hidden=1) — не рисуем, но триггер зоны в trail.js остаётся активным.
            if (rt.hidden) continue;
            const rp = { x: rt.x, z: rt.z };
            const rDist = Math.sqrt((rp.x - truckPos.x) ** 2 + (rp.z - truckPos.z) ** 2);
            const rScreen = worldToScreen2(rp.x, rp.z);
            const rVisible = Math.abs(rScreen.x - cx) < w / 2 - 8 && Math.abs(rScreen.y - cy) < h / 2 - 8;
            // Метка: рядом с точкой, если видима; у стрелки-указателя, если за пределами карты.
            let labelX = rScreen.x, labelY = rScreen.y - 16;
            // Тайник (active=false) не имеет указателя за пределами карты — вне экрана не рисуем.
            if (!rVisible && rt.active === false) continue;
            // Цель «армирована» (игрок в зоне) — зелёная, иначе красная.
            const rColor = rt.armed ? '#22dd55' : ((rt.color && rt.color !== 'default') ? rt.color : '#ff2d2d');
            ctx.save();
            if (rVisible) {
                ctx.beginPath();
                ctx.arc(rScreen.x, rScreen.y, 7, 0, 2 * Math.PI);
                ctx.fillStyle = rColor;
                ctx.shadowColor = rColor;
                ctx.shadowBlur = 14;
                ctx.fill();
                ctx.shadowBlur = 0;
                ctx.lineWidth = 2;
                ctx.strokeStyle = '#ffffff';
                ctx.stroke();
                ctx.beginPath();
                ctx.moveTo(rScreen.x - 13, rScreen.y);
                ctx.lineTo(rScreen.x + 13, rScreen.y);
                ctx.moveTo(rScreen.x, rScreen.y - 13);
                ctx.lineTo(rScreen.x, rScreen.y + 13);
                ctx.strokeStyle = '#ffffff';
                ctx.lineWidth = 1;
                ctx.stroke();
            } else {
                const dx = rScreen.x - cx, dy = rScreen.y - cy;
                const len = Math.sqrt(dx * dx + dy * dy) || 1;
                const nx = dx / len, ny = dy / len;
                const ax = cx + nx * (radius - 6), ay = cy + ny * (radius - 6);
                ctx.translate(ax, ay);
                ctx.rotate(Math.atan2(ny, nx));
                ctx.beginPath();
                ctx.moveTo(11, 0);
                ctx.lineTo(-7, -7);
                ctx.lineTo(-7, 7);
                ctx.closePath();
                ctx.fillStyle = rColor;
                ctx.shadowColor = 'rgba(0,0,0,0.6)';
                ctx.shadowBlur = 5;
                ctx.fill();
                ctx.shadowBlur = 0;
                ctx.strokeStyle = '#ffffff';
                ctx.lineWidth = 1.5;
                ctx.stroke();
                ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
                // Метка у стрелки-указателя (в правильном направлении от центра карты).
                labelX = ax; labelY = ay - 16;
            }
            ctx.restore();
            const rLabel = `${rt.name || 'Цель'}${rt.armed ? ' [в зоне]' : ''}: ${formatDistance(rDist)}`;
            labelsData.push({
                x: labelX, y: labelY,
                text: rLabel, color: rColor, isActive: true, isCity: false,
                w: Math.min(rLabel.length * 7 + 22, 230), h: 18, priority: 6
            });
        }
    }

    // Города за пределами карты (ближайшие 4)
    if (state.nearbyCities.length > 0) {
        for (const city of state.nearbyCities) {
            const cityScreen = worldToScreen2(city.x, city.z);
            const visible = Math.abs(cityScreen.x - cx) < w/2 - 20 && Math.abs(cityScreen.y - cy) < h/2 - 20;
            if (visible) continue;
            const distReal = Math.sqrt(city.distSq);
            const distText = formatDistance(distReal);
            const label = `${city.name} (${distText})`;
            const charWidth = 11 * 0.6;
            const wLabel = Math.min(label.length * charWidth + 16, 180);
            const hLabel = 18;
            const dx = cityScreen.x - cx;
            const dy = cityScreen.y - cy;
            const len = Math.sqrt(dx*dx + dy*dy);
            if (len < 0.01) continue;
            const nx = dx / len;
            const ny = dy / len;
            const arrowX = cx + nx * radius;
            const arrowY = cy + ny * radius;
            const angle = Math.atan2(ny, nx);
            ctx.save();
            ctx.translate(arrowX, arrowY);
            ctx.rotate(angle);
            ctx.beginPath();
            ctx.moveTo(8, 0);
            ctx.lineTo(-5, -5);
            ctx.lineTo(-5, 5);
            ctx.closePath();
            ctx.fillStyle = '#aabbcc';
            ctx.shadowColor = 'rgba(0,0,0,0.6)';
            ctx.shadowBlur = 4;
            ctx.fill();
            ctx.strokeStyle = '#000';
            ctx.lineWidth = 1;
            ctx.stroke();
            ctx.shadowBlur = 0;
            ctx.restore();
            let labelX = arrowX;
            let labelY = (ny > 0) ? arrowY - 14 : arrowY + 18;
            if (labelX < 50) labelX = 50;
            if (labelX > w - 50) labelX = w - 50;
            if (labelY < 20) labelY = 20;
            if (labelY > h - 20) labelY = h - 20;
            labelsData.push({
                x: labelX,
                y: labelY,
                text: label,
                color: '#c8ddee',
                isActive: false,
                isCity: true,
                w: wLabel,
                h: hLabel,
                priority: 0,
            });
        }
    }

    // Жадная расстановка меток
    if (labelsData.length > 0) {
        const placedLabels = greedyPlacement(labelsData, w, h);
        for (const label of placedLabels) {
            let px = label.x;
            let py = label.y;
            const halfW = label.w / 2;
            const halfH = label.h / 2;
            if (px - halfW < 5) px = 5 + halfW;
            if (px + halfW > w - 5) px = w - 5 - halfW;
            if (py - halfH < 5) py = 5 + halfH;
            if (py + halfH > h - 5) py = h - 5 - halfH;
            const el = createLabelElement(label.text, px, py, label.color, label.isActive, label.isCity);
            labelLayer.appendChild(el);
        }
    }

    // Отрисовка маркеров событий
    // v74 (фидбек 31.08.2026): ВРЕМЕННО ОТКЛЮЧЕНЫ — иконки остановки/событий сильно
    // загрязняют шлейф трека. Блок сохранён: включить = убрать `false &&`.
    for (const marker of (false ? state.eventMarkers : [])) {
        const p = worldToScreen2(marker.x, marker.z);
        if (p.x < -20 || p.x > w+20 || p.y < -20 || p.y > h+20) continue;

        if (marker.textOnly) {
            ctx.save();
            ctx.font = '10px "Segoe UI", sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.shadowColor = 'rgba(0,0,0,0.8)';
            ctx.shadowBlur = 4;
            ctx.fillStyle = '#ffffff';
            ctx.strokeStyle = '#000000';
            ctx.lineWidth = 2;
            ctx.strokeText(marker.label, p.x, p.y);
            ctx.fillText(marker.label, p.x, p.y);
            ctx.restore();
            continue;
        }

        const size = 12;
        const isSquare = marker.square === true;
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.shadowColor = 'rgba(0,0,0,0.5)';
        ctx.shadowBlur = 8;
        ctx.beginPath();
        if (isSquare) {
            const r = 4;
            ctx.moveTo(-size + r, -size);
            ctx.lineTo(size - r, -size);
            ctx.quadraticCurveTo(size, -size, size, -size + r);
            ctx.lineTo(size, size - r);
            ctx.quadraticCurveTo(size, size, size - r, size);
            ctx.lineTo(-size + r, size);
            ctx.quadraticCurveTo(-size, size, -size, size - r);
            ctx.lineTo(-size, -size + r);
            ctx.quadraticCurveTo(-size, -size, -size + r, -size);
            ctx.closePath();
        } else {
            ctx.arc(0, 0, size, 0, 2*Math.PI);
        }
        ctx.fillStyle = marker.color || '#ffffff';
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 2;
        ctx.stroke();

        ctx.fillStyle = '#ffffff';
        ctx.font = 'bold 11px "Segoe UI", sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(marker.label || '?', 0, -2);

        if (marker.subtext) {
            ctx.fillStyle = '#ffffff';
            ctx.shadowColor = 'rgba(0,0,0,0.8)';
            ctx.shadowBlur = 4;
            ctx.font = '8px "Segoe UI", sans-serif';
            ctx.textBaseline = 'top';
            ctx.fillText(marker.subtext, 0, size + 2);
            ctx.shadowBlur = 0;
        }
        ctx.restore();
    }

    // Плашка режима обзора
    if (useOverview && state.zoomOnMapTargets.length > 0) {
        ctx.save();
        const text = 'Режим: обзор целей';
        ctx.font = '10px "Segoe UI", sans-serif';
        const metrics = ctx.measureText(text);
        const padding = 4;
        const rectW = metrics.width + padding * 2;
        const rectH = 16;
        const x = w - 4;
        const y = 4;
        const radiusRect = 4;
        ctx.shadowColor = 'rgba(0,0,0,0.5)';
        ctx.shadowBlur = 6;
        ctx.fillStyle = 'rgba(255, 200, 87, 0.92)';
        ctx.beginPath();
        ctx.moveTo(x - rectW + radiusRect, y);
        ctx.arcTo(x - rectW, y, x - rectW, y + rectH, radiusRect);
        ctx.arcTo(x - rectW, y + rectH, x - rectW + radiusRect, y + rectH, radiusRect);
        ctx.arcTo(x, y + rectH, x, y + rectH - radiusRect, radiusRect);
        ctx.arcTo(x, y, x - radiusRect, y, radiusRect);
        ctx.closePath();
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.fillStyle = '#1a1a1a';
        ctx.textAlign = 'right';
        ctx.textBaseline = 'middle';
        ctx.fillText(text, x - padding, y + rectH/2);
        ctx.restore();
    }

    // Индикаторы высоты
    const truckYVal = parseFloat(truckY.value) || 0;
    const targetYVal = parseFloat(targetY.value) || 0;
    const heightDiff = targetYVal - truckYVal;
    const threshold = 20;
    if (heightDiff > threshold) {
        heightIndicatorUp.style.opacity = '1';
        heightIndicatorDown.style.opacity = '0';
    } else if (heightDiff < -threshold) {
        heightIndicatorUp.style.opacity = '0';
        heightIndicatorDown.style.opacity = '1';
    } else {
        heightIndicatorUp.style.opacity = '0';
        heightIndicatorDown.style.opacity = '0';
    }

    rawHeadingValue.textContent = state.rawHeading.toFixed(6);
}