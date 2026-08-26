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