// ================================================================
// WEBSOCKET ДЛЯ ТЕЛЕМЕТРИИ И КОМАНД
// ================================================================
let socket = null;
let reconnectTimer = null;
let wsTimeoutId = null;
let hybridFirstDataReceived = false;
let commandWs = null;
let commandWsConnected = false;

function mergeHybridTelemetry(data) {
    if (data['truck.speed'] !== undefined) hybridState.lastSpeed = Math.abs(Number(data['truck.speed']) || 0) * 3.6;
    if (data['truck.engine.enabled'] !== undefined) hybridState.engine = data['truck.engine.enabled'] ? 'ON' : 'OFF';
    else if (data['truck.engine.on'] !== undefined) hybridState.engine = data['truck.engine.on'] ? 'ON' : 'OFF';

    const fuelAmount = Number(data['truck.fuel.amount']);
    const fuelCapacity = Number(data['truck.fuel.capacity']);
    if (Number.isFinite(fuelAmount) && Number.isFinite(fuelCapacity) && fuelCapacity > 0) {
        hybridState.fuelPercent = Math.max(0, Math.min(100, fuelAmount / fuelCapacity * 100));
        // v75 (вар. «а»): литры — живое значение (fuel.amount меняется непрерывно).
        hybridState.fuelLiters = fuelAmount;
    } else if (data['truck.fuel'] !== undefined) hybridState.fuelPercent = Number(data['truck.fuel']) || 0;

    if (data['rest.stop'] !== undefined) { hybridState.restHours = Math.max(0, Number(data['rest.stop']) / 60); hybridState._restFromTruckTel = true; }
    if (data['local.scale'] !== undefined) hybridState.localScale = Number(data['local.scale']) || 1;
    if (data['truck.light.beam.high'] !== undefined || data['truck.light.beam.low'] !== undefined || data['truck.light.parking'] !== undefined) {
        if (data['truck.light.beam.high']) hybridState.lights = 'high';
        else if (data['truck.light.beam.low']) hybridState.lights = 'low';
        else if (data['truck.light.parking']) hybridState.lights = 'parking';
        else hybridState.lights = 'off';
    }
    if (data['truck.brake.parking'] !== undefined) { hybridState.parking = data['truck.brake.parking'] ? 'ON' : 'OFF'; hybridState._parkingFromTruckTel = true; }
    if (data['truck.effective.brake'] !== undefined) hybridState.brake = Math.max(0, Math.min(1, Number(data['truck.effective.brake']) || 0));
    if (data['truck.light.brake'] !== undefined) hybridState.brakeLight = Boolean(data['truck.light.brake']);
    let trailerSeen = false;
    for (const [key, value] of Object.entries(data)) {
        if (key.startsWith('trailer.') && key.endsWith('.connected')) { trailerSeen = true; if (value === true) hybridState.trailerAttached = true; }
    }
    if (trailerSeen) hybridState._trailerFromTruckTel = true;
    hybridState.dataShown = true;
}

async function hydrateHybridSnapshot() {
    const base = `http://localhost:${hybridState.wsPort || DEFAULT_WS_PORT}`;
    for (const path of ['/api/rest/flat/truck', '/api/rest/flat/rest', '/api/rest/flat/local', '/api/rest/flat/job']) {
        try {
            const res = await fetch(base + path, { cache: 'no-store' });
            if (!res.ok) continue;
            const snap = await res.json();
            mergeHybridTelemetry(snap);
        } catch (e) {
            console.debug('[WS Hybrid] snapshot unavailable:', path, e?.message || e);
        }
    }
    updateUI();
}

function connectWebSocket() {
    if (!hybridState.wsUrl) {
        hybridState.wsUrl = `ws://localhost:${DEFAULT_WS_PORT}/api/ws/delta/flat/?throttle=50`;
    }
    if (socket && socket.readyState === WebSocket.OPEN) return;

    socket = new WebSocket(hybridState.wsUrl);

    socket.onopen = function() {
        hybridState.wsConnected = true;
        hybridState.dataShown = true;
        updateUI();
        if (wsTimeoutId) clearTimeout(wsTimeoutId);
    };

    socket.onmessage = function(event) {
        try {
            const data = JSON.parse(event.data);
            mergeHybridTelemetry(data);
            updateUI();
        } catch (e) { console.warn('[WS Hybrid] telemetry parse error:', e); }
    };

    socket.onerror = function() {
        hybridState.wsConnected = false;
    };

    socket.onclose = function() {
        hybridState.wsConnected = false;
        if (reconnectTimer) clearTimeout(reconnectTimer);
        reconnectTimer = setTimeout(() => connectWebSocket(), WS_RECONNECT_DELAY);
    };

    wsTimeoutId = setTimeout(() => {
        if (!hybridState.wsConnected) {
            hybridState.wsConnected = false;
            if (socket && socket.readyState !== WebSocket.CLOSED) socket.close();
        }
    }, WS_TIMEOUT);
}

// Подключение к командному WebSocket (порт 8084)
function connectCommandWebSocket() {
    if (commandWs && commandWs.readyState === WebSocket.OPEN) return;
    commandWs = new WebSocket('ws://localhost:8084/');
    commandWs.onopen = function() {
        commandWsConnected = true;
        console.log('[WS Hybrid] Connected to command server');
    };
    commandWs.onmessage = function(e) {
        try {
            const data = JSON.parse(e.data);
            console.log('[WS Hybrid] Received command:', data);
            if (data.command === 'show_ui_first') {
                showHybridUIFirst();
            } else if (data.command === 'show_ui') {
                showHybridUIFast();
            } else if (data.command === 'hide_ui') {
                hideHybridUIFast();
            }
        } catch(e) { console.warn('[WS Hybrid] Error parsing command', e); }
    };
    commandWs.onclose = function() {
        commandWsConnected = false;
        console.log('[WS Hybrid] Command WebSocket disconnected');
        setTimeout(connectCommandWebSocket, 3000);
    };
    commandWs.onerror = function(e) {
        console.warn('[WS Hybrid] Command WebSocket error:', e);
    };
}

function showHybridUIFirst() {
    const content = document.querySelector('.dashboard-content');
    if (!content) {
        console.warn('[UI Hybrid] .dashboard-content not found, retrying in 100ms');
        setTimeout(showHybridUIFirst, 100);
        return;
    }
    console.log('[UI Hybrid] Applying first animation to .dashboard-content');
    content.style.display = 'block';
    content.style.transform = 'scale(0.45)';
    content.style.opacity = '0';
    content.style.transition = 'none';
    content.offsetHeight;
    content.style.transition = 'transform 2s cubic-bezier(0.25, 0.46, 0.45, 0.94), opacity 0.3s ease-in';
    content.style.transform = 'scale(1)';
    content.style.opacity = '1';
    const badge = document.getElementById('ets2AssistBuildBadge');
    if (badge) badge.style.opacity = '0.9';
    hybridState.dataShown = true;
}

function showHybridUIFast() {
    const content = document.querySelector('.dashboard-content');
    if (!content) return;
    content.style.display = 'block';
    content.style.transition = 'opacity 0.15s ease-in';
    content.style.opacity = '1';
    content.style.transform = 'scale(1)';
    hybridState.dataShown = true;
    const badge = document.getElementById('ets2AssistBuildBadge');
    if (badge) badge.style.opacity = '0.9';
}

function hideHybridUIFast() {
    const content = document.querySelector('.dashboard-content');
    if (!content) return;
    content.style.transition = 'opacity 0.15s ease-out';
    content.style.opacity = '0';
    const badge = document.getElementById('ets2AssistBuildBadge');
    if (badge) badge.style.opacity = '0';
}