// ================================================================
// WEBSOCKET ДЛЯ СОХРАНЕНИЯ ТРЕКОВ (порт 8084)
// ================================================================
let saveWs = null;
let saveWsConnected = false;
let saveInProgress = false;
let saveStartedAt = 0;
let minimapAutoOff = false;   // true = ручной тоггл выключил авто-показ миникарты
let minimapAlwaysOn = false;  // тоггл «Показать карту»: true = карта ВСЕГДА видна (hide игнорируется)
let minimapShownOnce = false; // первый показ с анимацией — только один раз

function connectSaveWebSocket() {
    try {
        saveWs = new WebSocket('ws://localhost:8084/');
        saveWs.onopen = () => {
            saveWsConnected = true;
            console.log('[WS] Connected to save server');
            // Сообщаем приложению, что миникарта готова — оно даст команду
            // перечитать цели из файла (миникарта сама файл не опрашивает).
            if (saveWs.readyState === WebSocket.OPEN) {
                saveWs.send(JSON.stringify({ command: 'map_ready' }));
            }
        };
        saveWs.onmessage = (e) => {
            try {
                const data = JSON.parse(e.data);
                if (data.command) {
                    console.log('[WS] Received command:', data.command);
                    switch (data.command) {
                        case 'quest_courier':
                            // Курьер: синяя точка СТРОГО НА POI рядом с фурой (<=350м, >=40м).
                            generateRandomTarget({ atPoi: true, poiMaxDistM: 350, poiMinDistM: 40, questType: 'courier_pickup', color: '#2d7dff', active: true, name: 'Курьер: забрать документы', radius: 50 });
                            break;
                        case 'quest_stash':
                            // Тайник: жёлтая точка, 200м, на POI рядом с дорогой, НЕ активна (без указателя за пределами).
                            // Разовая цель: удаляется навсегда после выполнения (delete_on_complete=1).
                            generateRandomTarget({ nearTruck: true, distanceM: 200, requirePoi: true, requireRoad: true, questType: 'stash', color: '#ffd400', active: false, name: 'Тайник', radius: 30, deleteOnComplete: 1 });
                            break;
                        case 'quest_snack':
                            // Перекус: зелёная точка, 400м, на POI у дороги. После выполнения
                            // уходит на кулдаун (2 мин, реальное системное время) и снова появляется.
                            generateRandomTarget({ nearTruck: true, distanceM: 400, requirePoi: true, requireRoad: true, questType: 'snack', color: '#22dd55', active: true, name: 'Перекус', radius: 50, cooldown: 2 });
                            break;
                        case 'quest_courier_dropoff':
                            // Промежуточная точка квеста Курьер: СТРОГО НА POI примерно в
                            // distanceM от фуры (чтобы доставка была у здания, а не на трассе).
                            {
                                const dm = data.distanceM ? Number(data.distanceM) : 1000;
                                generateRandomTarget({ atPoi: true, poiTargetDistM: dm, poiMaxDistM: dm * 1.5 + 300, poiMinDistM: 60, questType: 'courier_dropoff', color: '#a64dff', active: true, name: 'Доставить документы', radius: 35 });
                            }
                            break;
                        case 'list_targets':
                            listTargets();
                            break;
                        case 'save_trail':
                            console.log('[WS] save_trail command received');
                            if (saveInProgress) { console.warn('[WS] save_trail ignored: save already in progress'); break; }
                            saveInProgress = true;
                            saveStartedAt = Date.now();
                            Promise.resolve(saveTrail(data.requestId || '')).finally(() => { saveInProgress = false; });
                            break;
                        case 'reset_recording_origin':
                            resetTrail();
                            break;
                        case 'start_recording':
                            resetTrail();
                            break;
                        case 'stop_recording':
                            console.log('[REC] stop_recording acknowledged. Existing data preserved until reset.');
                            break;
                        case 'remove_random_target':
                            removeRandomTarget(data.id);
                            break;
                        case 'remove_target':
                            // Приложение завершило цель (вышли из зоны после входа) —
                            // убираем её из миникарты.
                            removeRandomTarget(data.id);
                            break;
                        case 'set_overview':
                            // Обзор целей управляется ТОЛЬКО приложением (тоггл).
                            state.targetMapOverview = data.enabled === true;
                            console.log('[WS] Обзор целей ' + (state.targetMapOverview ? 'ВКЛ' : 'ВЫКЛ'));
                            break;
                        case 'hide_target':
                            // Скрыть цель на время (Перекус: 5 мин невидимости).
                            if (data.id) {
                                const t = state.randomTargets && state.randomTargets.find(r => r.id === data.id);
                                if (t) { t.hiddenUntil = Date.now() + (Number(data.durationMs) || 300000); console.log('[WS] Цель скрыта до ' + t.hiddenUntil); }
                            }
                            break;
                        case 'reset_target_reached':
                            randomTargetReachedSent = false;
                            console.log('[WS] Сброшен флаг достижения цели');
                            break;
                        case 'targets_data':
                            // УСТАРЕЛО (оставлено для совместимости): полный пакет теперь
                            // приходит как map_overrides_data, но цели из этого пакета тоже
                            // применяются (normalizeTarget тот же).
                            applyTargetsData(data.targets);
                            break;
                        case 'map_overrides_data':
                            // ПОЛНЫЙ пакет от приложения: города/POI/цели (статика + overrides
                            // + test_targets). Миникарта ничего не merge — просто применяет.
                            // typeof-guard: если points_overrides.js ещё не успел загрузиться
                            // (гонка при старте), буферизуем пакет — он применится при загрузке.
                            if (typeof storeMapOverrides === 'function') {
                                storeMapOverrides(data);
                            } else {
                                console.warn('[WS] storeMapOverrides ещё не определена — пакет буферизован');
                                window.__pendingMapOverrides = data;
                            }
                            break;
                        case 'points_overrides_data':
                            // Переопределённые города/POI и пользовательские точки
                            // из редактора карты (delta поверх статических баз).
                            storePointsOverrides(data);
                            break;
                        case 'ar_pin_map':
                            // v73: пометка «Пометить в АР» на МИНИКАРТЕ (кружок+крест,
                            // серый, как в редакторе). Рисует map_draw.js по state.arPinMap.
                            state.arPinMap = (data.active === true) ?
                                { x: Number(data.x) || 0, y: Number(data.y) || 0, z: Number(data.z) || 0 } : null;
                            console.log('[WS] ar_pin_map ' + (state.arPinMap ? 'установлена' : 'снята'));
                            break;
                        case 'minimap_show':
                            // Миникарта показывается только когда авто-логика включена.
                            if (!minimapAutoOff) {
                                if (!minimapShownOnce) { showUIWithAnimation(); minimapShownOnce = true; }
                                else showUIFast();
                            }
                            break;
                        case 'minimap_hide':
                            // При включённом тогл-режиме (minimapAutoOff=false => показ ВСЕГДА)
                            // hide-команды игнорируются: карта никогда не исчезает.
                            if (!minimapAlwaysOn) hideUIFast();
                            break;
                        case 'minimap_auto':
                            // Тоггл «Показать карту»: enabled=true — карта ВСЕГДА на экране
                            // (minimap_hide игнорируется); false — обычная авто-логика.
                            if (data.enabled === true) {
                                minimapAlwaysOn = true;
                                minimapAutoOff = false;
                                if (!minimapShownOnce) { showUIWithAnimation(); minimapShownOnce = true; }
                                else showUIFast();
                            } else {
                                minimapAlwaysOn = false;
                            }
                            break;
                        case 'show_pause_logo':
                        case 'hide_pause_logo':
                            break;
                        default:
                            console.log('[WS] Unknown command:', data.command);
                    }
                    return;
                }
                if (data.status === 'ok') {
                    console.log('[WS] Server confirmed save');
                    showToast('Трек сохранён!', 3000);
                    playSoundViaWS('success');
                }
            } catch (e) { console.log('[WS] Error parsing message', e); }
        };
        saveWs.onclose = () => {
            saveWsConnected = false;
            console.log('[WS] Disconnected from save server');
            setTimeout(connectSaveWebSocket, 3000);
        };
        saveWs.onerror = () => {
            console.log('[WS] Error connecting to save server');
        };
    } catch (e) {
        console.log('[WS] Exception connecting to save server');
        setTimeout(connectSaveWebSocket, 3000);
    }
}

function showUIWithAnimation() {
    const container = document.querySelector('.minimap-container');
    if (!container) {
        console.warn('[UI] .minimap-container not found, retrying in 100ms');
        setTimeout(showUIWithAnimation, 100);
        return;
    }
    console.log('[UI] Applying animation to map');
    container.style.opacity = '0';
    container.style.transform = 'scale(0.45) translateY(0)';
    container.style.transition = 'none';
    container.offsetHeight;
    container.style.transition = 'transform 2s cubic-bezier(0.25, 0.46, 0.45, 0.94), opacity 0.3s ease-in';
    container.style.transform = 'scale(1) translateY(0)';
    container.style.opacity = '1';
    const badge = document.getElementById('mapBuildBadge');
    if (badge) badge.style.opacity = '0.9';
}

function showUIFast() {
    const container = document.querySelector('.minimap-container');
    if (!container) return;
    container.style.transition = 'opacity 0.15s ease-in';
    container.style.opacity = '1';
    container.style.transform = 'scale(1) translateY(0)';
    const badge = document.getElementById('mapBuildBadge');
    if (badge) badge.style.opacity = '0.9';
}

function hideUIFast() {
    const container = document.querySelector('.minimap-container');
    if (!container) return;
    container.style.transition = 'opacity 0.15s ease-out';
    container.style.opacity = '0';
    const badge = document.getElementById('mapBuildBadge');
    if (badge) badge.style.opacity = '0';
}

// ================================================================
// WEBSOCKET ТЕЛЕМЕТРИИ (TruckTel)
// ================================================================
let wsConnected = false;
let wsPort = 8080;
let wsUrl = null;
let socket = null, reconnectTimer = null, wsTimeoutId = null;
let telemetryFrames = 0;

async function hydrateTelemetrySnapshot() {
    const base = `http://localhost:${wsPort || 8080}`;
    const paths = [
        '/api/rest/flat/truck',
        '/api/rest/flat/local',
        '/api/rest/flat/game',
        '/api/rest/flat/job'
    ];
    for (const path of paths) {
        try {
            const res = await fetch(base + path, { cache: 'no-store' });
            if (!res.ok) continue;
            const snap = await res.json();
            if (typeof applyTelemetryDelta === 'function') applyTelemetryDelta(snap);
            else console.warn('[WS] applyTelemetryDelta not defined during snapshot:', path);
        } catch (e) {
            console.debug('[WS] REST snapshot unavailable:', path, e?.message || e);
        }
    }
    updateRuntimeDebugOverlay();
}

async function fetchHttpData() {
    try {
        const res = await fetch(withRnd('web_data.json'));
        if (!res.ok) throw new Error('HTTP error');
        const data = await res.json();
        const newPort = data.wsPort || 8080;
        if (newPort !== wsPort) {
            wsPort = newPort;
            wsUrl = `ws://localhost:${wsPort}/api/ws/delta/flat/?throttle=50`;
            if (socket && socket.readyState === WebSocket.OPEN) socket.close();
        }
        state.jobDestination = data.job?.destinationCity || '';
        state.estimatedDistance = data.job?.estimatedDistance || 0;
        await hydrateTelemetrySnapshot();
        // Миникарта не опрашивает файл целей сама — приложение шлёт targets_data
        // по готовности (map_ready) и при каждом изменении файла.
    } catch (e) { console.warn('[WS] web_data fetch:', e); }
}

function removeLegacyDebugPanels() {
    document.querySelectorAll('#extraInfo, .extra-info, .legacy-extra-info').forEach(el => el.remove());
}

function updateRuntimeDebugOverlay() {
    const el = document.getElementById('runtimeDebug');
    if (!el) return;
    const head = Array.isArray(state.headOffset) ? ((((Number(state.headOffset[3]) || 0) % 1 + 1) % 1) * 360) : 0;
    // Вертикальный угол головы (v67, по требованию 31.08.2026): head.offset[4],
    // доля оборота → градусы (пример: -0.0357 → -12.9°). Рядом с горизонтальным.
    const headPitch = Array.isArray(state.headOffset) ? ((((Number(state.headOffset[4]) || 0) % 1 + 1) % 1) * 360) : 0;
    const pitchSigned = (((Number(state.headOffset?.[4]) || 0) % 1) * 360);
    const scale = Number(state.localScale) || 0;
    const speed = Number(state.speed) || 0;
    const poi = Array.isArray(state.pois) ? state.pois.length : 0;
    const cat = Array.isArray(state.poiCategories) ? state.poiCategories.length : 0;
    const roads = Array.isArray(state.roads) ? state.roads.length : 0;
    const cities = Array.isArray(state.cities) ? state.cities.length : 0;
    const staticOk = roads > 0 || cities > 0 || poi > 0;
    const fuel = Number(state.fuel) || 0;
    const brake = Number(state.brake) || 0;
    const trailer = state.trailerAttached ? 'ON' : 'OFF';
    const lights = state.lights ? Object.entries(state.lights).filter(([,v])=>v).map(([k])=>k).join(',') || 'off' : 'off';
    el.textContent = `Head: ${head.toFixed(1)}° | HeadPitch: ${pitchSigned.toFixed(1)}° (${headPitch.toFixed(1)}°n) | Scale: ${scale.toFixed(2)}× | Speed: ${speed.toFixed(0)} km/h | Fuel: ${fuel.toFixed(0)}% | Brake: ${(brake*100).toFixed(0)}% | Trailer:${trailer} | Lights:${lights} | POI:${poi}/${cat} | Cities:${cities} Roads:${roads} | WS:${telemetryFrames} | STATIC:${staticOk ? 'OK' : 'MISSING'}`;
}

function normalizeHeadOffset(value) {
    let values = [];
    if (Array.isArray(value)) {
        values = value;
    } else if (typeof value === 'string') {
        values = value.split(/[\s,;]+/);
    } else if (value && typeof value === 'object') {
        values = Object.values(value);
    }

    const normalized = values.slice(0, 6).map(Number);
    while (normalized.length < 6) normalized.push(0);
    return normalized.map(value => Number.isFinite(value) ? value : 0);
}

function applyTelemetryDelta(data) {
    const placement = data['truck.world.placement'];
    if (Array.isArray(placement) && placement.length >= 6) {
        const tx = Number(placement[0]) || 0;
        const ty = Number(placement[1]) || 0;
        const tz = Number(placement[2]) || 0;
        const rawHeading = Number(placement[3]) || 0;
        state.rawHeading = rawHeading;
        state.truck.x = tx; state.truck.y = ty; state.truck.z = tz;
        state.truck.heading = rawHeading * 2 * Math.PI;
        state.truck.pitch = Number(placement[4]) || 0;
        state.truck.roll = Number(placement[5]) || 0;
        if (truckX) truckX.value = tx.toFixed(2);
        if (truckY) truckY.value = ty.toFixed(2);
        if (truckZ) truckZ.value = tz.toFixed(2);
        if (headingDisplay) headingDisplay.textContent = `heading: ${(rawHeading * 360).toFixed(1)}°`;
    }

    if (data['truck.speed'] !== undefined) state.speed = Math.abs(Number(data['truck.speed']) || 0) * 3.6;
    if (data['truck.engine.enabled'] !== undefined) state.engineOn = Boolean(data['truck.engine.enabled']);
    else if (data['truck.engine.on'] !== undefined) state.engineOn = Boolean(data['truck.engine.on']);

    const fuelAmount = data['truck.fuel.amount'];
    const fuelCapacity = data['truck.fuel.capacity'];
    if (fuelAmount !== undefined && fuelCapacity !== undefined && Number(fuelCapacity) > 0)
        state.fuel = Math.max(0, Math.min(100, Number(fuelAmount) / Number(fuelCapacity) * 100));
    else if (data['truck.fuel'] !== undefined) state.fuel = Number(data['truck.fuel']) || 0;

    if (data['game.time'] !== undefined) state.gameTimeMinutes = Number(data['game.time']) || 0;
    if (data['local.scale'] !== undefined) state.localScale = Number(data['local.scale']) || 1;
    if (data['truck.effective.steering'] !== undefined) state.steering = Number(data['truck.effective.steering']) || 0;
    if (data['truck.effective.throttle'] !== undefined) state.throttle = Number(data['truck.effective.throttle']) || 0;
    if (data['truck.effective.brake'] !== undefined) state.brake = Number(data['truck.effective.brake']) || 0;
    if (data['truck.odometer'] !== undefined) state.odometer = Number(data['truck.odometer']) || 0;
    if (data['truck.head.offset'] !== undefined) state.headOffset = normalizeHeadOffset(data['truck.head.offset']);

    let wearSum = 0;
    let wearCount = 0;
    for (const [key, value] of Object.entries(data)) {
        if (key.startsWith('truck.wear.') || key.startsWith('trailer.') && key.includes('.wear.')) {
            const n = Number(value);
            if (Number.isFinite(n)) { wearSum += n; wearCount++; }
        }
    }
    if (wearCount > 0) state.damage = wearSum;
    if (data['trailer.0.cargo.damage'] !== undefined) state.cargoDamage = Number(data['trailer.0.cargo.damage']) || 0;

    for (const [key, value] of Object.entries(data)) {
        if (key.startsWith('trailer.') && key.endsWith('.connected') && value === true) state.trailerAttached = true;
    }

    const lightMap = {
        beamHigh: 'truck.light.beam.high', beamLow: 'truck.light.beam.low',
        parking: 'truck.light.parking', beacon: 'truck.light.beacon',
        brake: 'truck.light.brake', reverse: 'truck.light.reverse',
        leftBlinker: 'truck.light.lblinker', rightBlinker: 'truck.light.rblinker'
    };
    for (const [dst, src] of Object.entries(lightMap)) {
        if (data[src] !== undefined) state.lights[dst] = Boolean(data[src]);
    }
    if (data['truck.light.aux.front'] !== undefined) state.lights.aux = Number(data['truck.light.aux.front']) || 0;

    state.wsDataReceived = true;
    telemetryFrames++;
    if (window.mapStatusSetTelemetry) mapStatusSetTelemetry(true);
    updateAll(state.speed);
    updateRuntimeDebugOverlay();
}

removeLegacyDebugPanels();

function connectWebSocket() {
    if (!wsUrl) wsUrl = `ws://localhost:8080/api/ws/delta/flat/?throttle=50`;
    if (socket && socket.readyState === WebSocket.OPEN) return;
    socket = new WebSocket(wsUrl);
    socket.onopen = function() {
        wsConnected = true;
        if (wsTimeoutId) clearTimeout(wsTimeoutId);
        if (telemetryDot) telemetryDot.style.background = '#3dd68c';
        if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены';
        console.log(`[WS] Telemetry connected: ${wsUrl}`);
    };
    socket.onmessage = function(event) {
        try {
            const data = JSON.parse(event.data);
            applyTelemetryDelta(data);
        } catch (e) {
            console.warn('[WS] Ошибка обработки telemetry delta:', e);
        }
    };
    socket.onerror = function() {
        wsConnected = false;
        if (telemetryDot) telemetryDot.style.background = '#e45c5c';
        if (telemetryStatus) telemetryStatus.textContent = '⚠️ ошибка';
    };
    socket.onclose = function() {
        wsConnected = false;
        if (telemetryDot) telemetryDot.style.background = '#f5b342';
        if (telemetryStatus) telemetryStatus.textContent = '⏳ переподключение...';
        if (window.mapStatusSetTelemetry) mapStatusSetTelemetry(false);
        if (reconnectTimer) clearTimeout(reconnectTimer);
        reconnectTimer = setTimeout(connectWebSocket, 1500);
    };
    wsTimeoutId = setTimeout(() => {
        if (!wsConnected) {
            try { socket?.close(); } catch {}
        }
    }, 5000);
}

setInterval(fetchHttpData, 2000);
fetchHttpData();
connectSaveWebSocket();
connectWebSocket();
