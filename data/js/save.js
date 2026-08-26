// ================================================================
// ГЕНЕРАЦИЯ КОМПАКТНОГО ТРЕКА (с включением всех новых полей)
// ================================================================
function generateCompactTrail() {
    console.log('[SAVE] generateCompactTrail() вызвана');
    if (state.trail.length < 2) {
        console.warn('[SAVE] Трек слишком короткий, длина:', state.trail.length);
        showToast('Трек слишком короткий для сохранения.', 3000);
        return null;
    }

    const now = new Date();
    const startLabel = trackTitleSuffix || `Запись ${now.toLocaleDateString()} ${now.toLocaleTimeString()}`;
    const desc = trackDescription || '';

    const meta = {
        version: 2,
        title: startLabel,
        description: desc,
        startTime: state.trailStartTime ? new Date(state.trailStartTime).toISOString() : now.toISOString(),
        finishTime: now.toISOString(),
        durationMs: state.elapsedSeconds * 1000,
        trailInterval: TRAIL_INTERVAL,
        dataInterval: DATA_INTERVAL,
        minSpeed: MIN_SPEED_KMH,
        maxSpeed: MAX_SPEED_KMH,
        totalDistance: state.totalRealDistance,
        eventTypes: EVENT_TYPES,
    };

    const lines = [];
    lines.push(JSON.stringify(meta));

    for (const tp of state.trail) {
        const parts = tp.p.split(' ');
        lines.push(`${tp.t};${parts[0]};${parts[1]};${parts[2]};${tp.s}`);
    }

    for (const dp of state.dataPoints) {
        const parts = dp.p.split(' ');
        const lights = dp.lights || '{}';
        const headOffset = dp.headOffset || '0,0,0,0,0,0';
        lines.push(`D;${dp.t};${parts[0]};${parts[1]};${parts[2]};${dp.fuel};${dp.damage};${dp.pitch || '0'};${dp.roll || '0'};${lights};${dp.gameTime || 0};${dp.localScale || 1};${dp.steering || 0};${dp.throttle || 0};${dp.brake || 0};${dp.odometer || 0};${headOffset}`);
    }

    for (const e of state.eventMarkers) {
        const typeCode = EVENT_TYPES[e.type] || 0;
        lines.push(`E;${e.t || state.elapsedSeconds};${e.x.toFixed(2)};${e.z.toFixed(2)};${typeCode};${e.label || ''};${e.color || ''};${e.subtext || ''}`);
    }

    const result = lines.join('\n');
    console.log('[SAVE] Компактный трек сгенерирован, длина:', result.length);
    return result;
}

// ================================================================
// СОХРАНЕНИЕ ШЛЕЙФА (с возвратом Promise)
// ================================================================
async function saveTrail(requestId = "") {
    console.log('[SAVE] saveTrail() вызвана');
    console.log('[SAVE] saveWsConnected =', saveWsConnected);

    if (!saveWsConnected) {
        console.warn('[SAVE] Сервер сохранения не доступен');
        showToast('Сервер сохранения не доступен. Проверьте GUI.', 3000);
        return Promise.reject(new Error('Сервер сохранения не доступен'));
    }

    const compactData = generateCompactTrail();
    if (!compactData) {
        console.warn('[SAVE] Нет данных для сохранения');
        return Promise.reject(new Error('Нет данных для сохранения'));
    }

    const mapData = {
        version: 1,
        capturedAt: new Date().toISOString(),
        cities: Array.isArray(state.cities) ? state.cities : [],
        roads: Array.isArray(state.roads) ? state.roads : [],
        pois: Array.isArray(state.pois) ? state.pois : [],
        poiCategories: Array.isArray(state.poiCategories) ? state.poiCategories : [],
        poiCategoryCounts: state.poiCategoryCounts || {},
        customTargets: Array.isArray(state.customTargets) ? state.customTargets : []
    };

    const payload = {
        format: 'compact_v2',
        data: compactData,
        meta: {
            title: trackTitleSuffix || 'Без названия',
            description: trackDescription || '',
            startTime: state.trailStartTime ? new Date(state.trailStartTime).toISOString() : new Date().toISOString(),
            durationMs: state.elapsedSeconds * 1000,
            totalDistance: state.totalRealDistance,
            trailInterval: TRAIL_INTERVAL,
            dataInterval: DATA_INTERVAL,
            minSpeed: MIN_SPEED_KMH,
            maxSpeed: MAX_SPEED_KMH,
        },
        mapData: mapData,
        customTargets: state.customTargets,
        requestId: requestId || "-"
    };

    console.log('[SAVE] Отправка payload, размер:', JSON.stringify(payload).length);

    try {
        const response = await new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                console.warn('[SAVE] Таймаут ожидания ответа от сервера');
                saveWs.removeEventListener('message', handler);
                reject(new Error('Таймаут'));
            }, 10000);

            const handler = (e) => {
                try {
                    const data = JSON.parse(e.data);
                    console.log('[SAVE] Получен ответ от сервера:', data);
                    if (data.status !== 'ok' && data.status !== 'error') return;
                    saveWs.removeEventListener('message', handler);
                    if (data.status === 'ok') {
                        clearTimeout(timeout);
                        resolve(data);
                    } else {
                        clearTimeout(timeout);
                        reject(new Error(data.message || 'Ошибка сохранения'));
                    }
                } catch (err) {
                    clearTimeout(timeout);
                    reject(err);
                }
            };
            saveWs.addEventListener('message', handler);
            try {
                saveWs.send(JSON.stringify(payload));
                console.log('[SAVE] Payload отправлен, ожидаем ответ...');
            } catch (sendError) {
                saveWs.removeEventListener('message', handler);
                clearTimeout(timeout);
                reject(sendError);
            }
        });

        console.log('[SAVE] Сохранение успешно завершено, ответ:', response);
        showToast('Трек сохранён!', 3000);
        playSoundViaWS('success');
        console.log('[SAVE] Сохранение завершено. Трек НЕ сбрасывается автоматически; используйте «Сбросить начало записи трека».');
        return response;
    } catch (e) {
        console.error('[SAVE] Ошибка сохранения:', e.message);
        showToast(`Ошибка сохранения: ${e.message}`, 4000);
        throw e;
    }
}

function resetTrail() {
    console.log('[SAVE] resetTrail() вызвана');
    const lastPos = state.trail.length > 0 ? state.trail[state.trail.length-1] : { p: `${parseFloat(truckX.value).toFixed(2)} ${parseFloat(truckZ.value).toFixed(2)} 0` };
    state.trail = [];
    state.dataPoints = [];
    state.eventMarkers = [];
    state.totalRealDistance = 0;
    state.lastDistanceMarker = 0;
    state.firstMovementDetected = false;
    state.isParking = false;
    state.isStopped = false;
    state.parkingStartGameTime = null;
    state.parkingStartRealTime = null;
    state.parkingStartPos = null;
    state.stopStartRealTime = null;
    state.stopPos = null;
    state.stopTextMarker = null;
    state._lastDataDist = 0;
    state.elapsedSeconds = 0;
    state.trailStartTime = Date.now();
    state.wsDataReceived = true;

    const nowDate = new Date();
    const label = `Старт ${nowDate.toLocaleDateString()} ${nowDate.toLocaleTimeString()}`;
    const parts = lastPos.p.split(' ');
    const lx = parseFloat(parts[0]);
    const lz = parseFloat(parts[1]);
    state.eventMarkers.push({
        x: lx,
        z: lz,
        type: 'start',
        label: '▶',
        color: '#44ff88',
        subtext: label
    });
    state.trail.push({ p: lastPos.p, s: '0.00', t: '0.0' });
    if (typeof updateRuntimeDebugOverlay === 'function') updateRuntimeDebugOverlay();
    if (typeof drawMinimap === 'function') drawMinimap();
    showToast('Начало записи трека сброшено. Старый пройденный путь забыт.', 2500);
}
