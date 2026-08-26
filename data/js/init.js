// ================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ (не вошедшие в другие файлы)
// ================================================================
function loadStepFromStorage() {
    try {
        const saved = localStorage.getItem('ets2_nav_step');
        if (saved) {
            const val = parseInt(saved, 10);
            if (val > 0) {
                stepInput.value = val;
                state.step = val;
                stepDisplay.textContent = val;
            }
        }
    } catch (e) {}
}
function saveStepToStorage() {
    try {
        localStorage.setItem('ets2_nav_step', String(state.step));
    } catch (e) {}
}
function hideLoading() {
    loadingOverlay.classList.add('hidden');
}
function withRnd(url) {
    const sep = url.includes('?') ? '&' : '?';
    return `${url}${sep}rnd=${Date.now()}`;
}
function formatDistance(distMeters) {
    if (distMeters < 1000) return `${Math.round(distMeters)} м`;
    return `${Math.round(distMeters / 1000)} км`;
}
function playSoundViaWS(soundType) {
    if (saveWs && saveWs.readyState === WebSocket.OPEN) {
        saveWs.send(JSON.stringify({ command: 'play_sound', type: soundType }));
    }
}

// ================================================================
// ЗАГРУЗКА ДАННЫХ КАРТЫ
// ================================================================
async function fetchJsonAsset(paths, label) {
    const candidates = Array.isArray(paths) ? paths : [paths];
    let lastError = null;
    const expanded = [];
    for (const path of candidates) {
        expanded.push(path);
        if (location.protocol === 'http:' || location.protocol === 'https:') expanded.push(`${location.protocol}//${location.host}/${String(path).replace(/^\//,'')}`);
        expanded.push(`http://localhost:8082/${String(path).replace(/^\//,'')}`);
    }
    const unique = [...new Set(expanded)];
    for (const path of unique) {
        try {
            const url = /^https?:\/\//.test(path) ? path + (path.includes('?') ? '&' : '?') + '_=' + Date.now() : withRnd(path);
            const res = await fetch(url, { cache: 'no-store' });
            console.log(`[STATIC] ${label}: GET ${url} -> ${res.status}`);
            if (!res.ok) continue;
            return await res.json();
        } catch (e) {
            lastError = e;
            console.warn(`[STATIC] ${label}: ${path} failed:`, e?.message || e);
        }
    }
    if (lastError) console.warn(`[STATIC] ${label}: all candidates failed`);
    return null;
}

function addGeoJsonCities(citiesData, target) {
    for (const f of (citiesData?.features || [])) {
        const c = f?.geometry?.coordinates;
        if (!Array.isArray(c) || c.length < 2) continue;
        target.push({
            x: Number(c[0]),
            z: Number(c[1]),
            name: f?.properties?.name || '?',
            gameName: f?.properties?.name || '?'
        });
    }
}

function addGeoJsonRoads(roadsData, target) {
    const parseCoordinate = (value) => {
        if (Array.isArray(value)) return value;
        if (typeof value === 'string') {
            const parts = value.trim().split(/[\s,]+/).map(Number);
            return parts.length >= 2 && parts.every(Number.isFinite) ? parts : null;
        }
        return null;
    };
    const addLine = (coords, roadType) => {
        if (!Array.isArray(coords)) return;
        for (let i = 0; i < coords.length - 1; i++) {
            const a = parseCoordinate(coords[i]), b = parseCoordinate(coords[i + 1]);
            if (!Array.isArray(a) || !Array.isArray(b) || a.length < 2 || b.length < 2) continue;
            target.push({ x1:Number(a[0]), z1:Number(a[1]), x2:Number(b[0]), z2:Number(b[1]), roadType });
        }
    };
    for (const feature of (roadsData?.features || [])) {
        const geom = feature?.geometry;
        const rt = feature?.properties?.roadType?.String || feature?.properties?.roadType || 'default';
        if (geom?.type === 'LineString') addLine(geom.coordinates, rt);
        else if (geom?.type === 'MultiLineString') for (const line of (geom.coordinates || [])) addLine(line, rt);
    }
}

async function loadData() {
    console.log('[INIT] loadData() вызвана');
    try {
        const localizationMap = {};
        for (const path of LOCALIZATION_FILES) {
            const data = await fetchJsonAsset([path, '/' + path], `localization:${path}`);
            if (data?.citiesList) {
                for (const c of data.citiesList) {
                    const key = c.gameName;
                    if (!localizationMap[key]) localizationMap[key] = { realName:c.realName || c.gameName, x:Number(c.x), z:Number(c.z) };
                }
            }
        }

        const citiesData = await fetchJsonAsset(['GeoJson/cities.geojson', '/GeoJson/cities.geojson'], 'cities.geojson');
        const geoCities = [];
        addGeoJsonCities(citiesData, geoCities);
        const geoCityMap = Object.fromEntries(geoCities.map(c => [c.gameName, {x:c.x,z:c.z}]));

        if (Object.keys(localizationMap).length) {
            state.cities = Object.entries(localizationMap).map(([key, loc]) => {
                const coord = geoCityMap[key] || {x:loc.x,z:loc.z};
                return {x:Number(coord.x), z:Number(coord.z), name:loc.realName, gameName:key};
            });
        } else {
            state.cities = geoCities;
        }
        console.log(`[STATIC] cities loaded: ${state.cities.length}`);

        const roadsData = await fetchJsonAsset(['GeoJson/roads.geojson', '/GeoJson/roads.geojson'], 'roads.geojson');
        state.roads = [];
        addGeoJsonRoads(roadsData, state.roads);
        console.log(`[STATIC] roads loaded: ${state.roads.length}`);

        const overlaysData = await fetchJsonAsset(['Overlays.json', '/Overlays.json'], 'Overlays.json');
        state.pois = [];
        state.poiCategories = [];
        state.poiCategoryCounts = {};
        if (overlaysData && typeof overlaysData === 'object') {
            const categories = Object.keys(overlaysData);
            state.poiCategories = categories;
            for (const category of categories) {
                const items = Array.isArray(overlaysData[category]) ? overlaysData[category] : [];
                state.poiCategoryCounts[category] = items.length;
                for (const item of items) {
                    if (!item || item.x === undefined || item.z === undefined) continue;
                    state.pois.push({
                        x: Number(item.x),
                        z: Number(item.z),
                        type: category,
                        name: item.realName || item.name || item.gameName || category,
                        uid: item.uid || ''
                    });
                }
            }
        }
        console.log(`[STATIC] POI loaded: ${state.pois.length}; categories: ${state.poiCategories.length}`);
        console.log('[STATIC] categoryCounts:', state.poiCategoryCounts);

        await loadCustomTargets();
        if (typeof updateRuntimeDebugOverlay === 'function') updateRuntimeDebugOverlay();
        if (typeof drawMinimap === 'function') drawMinimap();
    } catch (e) {
        console.error('[STATIC] loadData fatal error:', e);
    } finally {
        hideLoading();
    }
}

// ================================================================
// ОБНОВЛЕНИЕ ВСЕХ ДАННЫХ
// ================================================================
function updateAll(speed) {
    const currentSpeed = speed || 0;
    if (state._lastSpeed > 10 && currentSpeed === 0) {
        state._speedHoldCount++;
        if (state._speedHoldCount < 2) {
            const stableSpeed = state._lastSpeed;
            processUpdate(stableSpeed);
            return;
        }
    } else {
        state._speedHoldCount = 0;
        state._lastSpeed = currentSpeed;
    }
    processUpdate(currentSpeed);
}

function processUpdate(speed) {
    const tX = Number(state.truck.x) || 0;
    const tY = Number(state.truck.y) || 0;
    const tZ = Number(state.truck.z) || 0;
    const tgX = Number(state.target.x) || 0;
    const tgY = Number(state.target.y) || 0;
    const tgZ = Number(state.target.z) || 0;
    if (truckX) truckX.value = tX.toFixed(2);
    if (truckY) truckY.value = tY.toFixed(2);
    if (truckZ) truckZ.value = tZ.toFixed(2);

    state.speed = Number(speed) || 0;
    updateTrail(state.speed, { x: tX, z: tZ }, state.engineOn, state.fuel, state.damage, state.gameTime, state.truck.heading, state.truck.pitch, state.truck.roll);
    const nav = computeNavigation(state.truck, state.target);
    state.distance = nav.distance;
    state.relativeAngle = nav.relativeAngle;
    state.absoluteAngle = nav.absoluteAngle;
    const distText = formatDistance(nav.distance);
    if (document.getElementById('distValue')) document.getElementById('distValue').textContent = distText;
    if (targetDistLabel) targetDistLabel.textContent = `дист. ${distText}`;

    const citiesWithDist = state.cities.map(c => {
        const dx = c.x - tX; const dz = c.z - tZ;
        return { ...c, distSq: dx*dx + dz*dz };
    });
    citiesWithDist.sort((a,b)=>a.distSq-b.distSq);
    state.nearbyCities = citiesWithDist.slice(0, NEARBY_CITIES_COUNT);
    drawMinimap();
}

function computeNavigation(truck, target) {
    const dx = target.x - truck.x;
    const dy = target.y - truck.y;
    const dz = target.z - truck.z;
    const dist = Math.sqrt(dx*dx + dy*dy + dz*dz);
    const horiz = Math.sqrt(dx*dx + dz*dz);
    const absoluteAngle = Math.atan2(dx, dz);
    const headingRad = truck.heading;
    let relativeAngle = absoluteAngle - headingRad;
    relativeAngle = ((relativeAngle % (2*Math.PI)) + 3*Math.PI) % (2*Math.PI) - Math.PI;
    return { distance: dist, relativeAngle, absoluteAngle };
}

// ================================================================
// ПЕРИОДИЧЕСКАЯ ПРОВЕРКА ТРИГГЕР-ФАЙЛА
// ================================================================
setInterval(() => {
    console.log('[TRIGGER] Checking trigger...');
    fetch('http://localhost:8083/check_trigger?file=save_trail.trigger', {
        mode: 'cors'
    })
        .then(res => {
            console.log('[TRIGGER] Response status:', res.status);
            if (!res.ok) throw new Error('HTTP error ' + res.status);
            return res.json();
        })
        .then(data => {
            console.log('[TRIGGER] Response data:', data);
            if (data.exists) {
                console.log('[TRIGGER] Файл найден, вызываем saveTrail()');
                saveTrail()
                    .then(() => {
                        console.log('[TRIGGER] saveTrail() успешно завершён, удаляем файл');
                        fetch('http://localhost:8083/delete_trigger?file=save_trail.trigger', {
                            mode: 'cors'
                        }).catch(err => console.warn('[TRIGGER] Ошибка удаления:', err));
                    })
                    .catch(err => {
                        console.error('[TRIGGER] saveTrail() завершился с ошибкой:', err);
                    });
            } else {
                console.log('[TRIGGER] Файл не найден');
            }
        })
        .catch(err => {
            console.warn('[TRIGGER] Ошибка проверки:', err.message);
        });
}, 3000);

// ================================================================
// ИНИЦИАЛИЗАЦИЯ
// ================================================================
(function init() {
    console.log('[INIT] Запуск инициализации');
    loadStepFromStorage();
    loadData();
    setInterval(() => {
        if ((state.roads.length + state.cities.length + state.pois.length) === 0 && !window.__staticLoadRetryInFlight) {
            window.__staticLoadRetryInFlight = true;
            loadData().finally(() => { window.__staticLoadRetryInFlight = false; });
        }
    }, 5000);

    fetchHttpData();
    setInterval(fetchHttpData, 1000);
    setTimeout(() => connectWebSocket(), 2000);

    connectSaveWebSocket();

    window.addEventListener('resize', () => {
        if (wsConnected) drawMinimap();
    });

    if (zoomLabel) zoomLabel.textContent = '×1.0';
    if (stepDisplay) stepDisplay.textContent = state.step;
    if (stepInput) stepInput.value = state.step;

    setupUI();

    console.log('[INIT] Карта загружена');
})();