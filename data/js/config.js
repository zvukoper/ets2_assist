// ================================================================
// НАСТРОЙКИ
// ================================================================
const NEARBY_CITIES_COUNT = 4;
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 4.8;
const MAX_SPEED_KMH = 115;
const MIN_SPEED_KMH = 0;
const LOCALIZATION_FILES = ["localized_cities/cities_sibirmap.json"];
const CORNER_RADIUS = 18;
const LABEL_PADDING = -5;
const TRAIL_INTERVAL = 3;
const DATA_INTERVAL = 25;

// ================================================================
// КАЛИБРОВКА PITCH/ROLL ПО ДОКУМЕНТАЦИИ TRUCK TEL
// ================================================================
// heading: 0..1 → 0..360
// pitch: -0.25..0.25 → -90..90 (умножаем на 360)
// roll:  -0.5..0.5  → -180..180 (умножаем на 360)
const PITCH_SCALE = 360;
const ROLL_SCALE = 360;
const HEADING_SCALE = 360;

// Параметры страницы
const urlParams = new URLSearchParams(window.location.search);
const DEBUG_MODE = urlParams.get('debug') === 'true';
const SHOW_BBOX = urlParams.get('bbox') === 'true';

// Применяем дебаг-режим
if (!DEBUG_MODE) {
    document.body.classList.add('hide-ui');
}

// ================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ДЛЯ КАРТЫ
// ================================================================
function computeAutoScale(speed) {
    const clampedSpeed = Math.max(0, Math.min(speed, MAX_SPEED_KMH));
    const t = clampedSpeed / MAX_SPEED_KMH;
    return ZOOM_MIN + t * (ZOOM_MAX - ZOOM_MIN);
}

function getGridStep(scale, canvasSize) {
    const targetLines = 6;
    let step = scale * (canvasSize / targetLines);
    const steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000];
    let best = steps[0];
    for (const s of steps) {
        if (Math.abs(s - step) < Math.abs(best - step)) best = s;
    }
    return best;
}