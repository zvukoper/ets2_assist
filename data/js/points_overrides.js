// ================================================================
// ПРИЁМ ПОЛНОГО ПАКЕТА OVERRIDES (dumb-receiver)
// Приложение (C#) само собирает эффективное состояние точек:
// статика городов/POI + delta-merge overrides (map_overrides\*.json
// по load_order) + цели из map_overrides\test_targets.json.
// Миникарта НИЧЕГО не считает: принимает пакет map_overrides_data,
// ЗАМЕНЯЕТ свои списки точек, перерисовывается. Статические geojson
// (roads/дороги) остаются локальными — их пакет не заменяет.
// ================================================================

// null = пакет ещё не приходил (рисуем статическую базу из GeoJson/Overlays).
let _mapOverrides = null;
let _mapOverridesSeq = 0;

// Буферизация гонки: если websocket.js получил пакет ДО загрузки этого файла,
// пакет лежит в window.__pendingMapOverrides — применяем сразу после определения.
if (typeof window !== 'undefined' && window.__pendingMapOverrides) {
    const pending = window.__pendingMapOverrides;
    window.__pendingMapOverrides = null;
    setTimeout(() => { console.log('[OVR] применён буферизованный пакет после догрузки'); try { storeMapOverrides(pending); } catch (e) { console.warn('[OVR] буфер apply:', e); } }, 0);
}

function storeMapOverrides(data) {
    try {
        _mapOverrides = {
            seq: Number(data && data.seq) || (_mapOverridesSeq + 1),
            reason: (data && data.reason) || '',
            cities: Array.isArray(data && data.cities) ? data.cities : [],
            pois: Array.isArray(data && data.pois) ? data.pois : [],
            targets: Array.isArray(data && data.targets) ? data.targets : []
        };
        _mapOverridesSeq = _mapOverrides.seq;
        const ovrPois = _mapOverrides.pois.filter(p => p.overridden === true).length;
        const customPois = _mapOverrides.pois.filter(p => (p.category || '') === 'custom').length;
        console.log(`[OVR] map_overrides_data #${_mapOverrides.seq} (${_mapOverrides.reason}): cities=${_mapOverrides.cities.length}, pois=${_mapOverrides.pois.length} (ovr=${ovrPois}, custom=${customPois}), targets=${_mapOverrides.targets.length}`);
        applyMapOverrides();
        // КВИТИРОВАНИЕ: сообщаем приложению, что пакет дошёл и применён (для отладки
        // доставки: C# пишет ack в лог → сразу видно, где рвётся цепочка).
        try {
            if (typeof saveWs !== 'undefined' && saveWs && saveWs.readyState === WebSocket.OPEN) {
                saveWs.send(JSON.stringify({
                    command: 'map_overrides_ack',
                    seq: _mapOverrides.seq,
                    reason: _mapOverrides.reason,
                    cities: state.cities.length,
                    pois: state.pois.length,
                    custom: state.pois.filter(p => p.type === 'custom').length,
                    targets: state.randomTargets.length
                }));
            }
        } catch (e2) { console.warn('[OVR] ack send fail:', e2); }
        // Статусная строка: визуальный индикатор применения (seq + счётчики).
        if (window.mapStatusSetOperation) {
            mapStatusSetOperation(`overrides #${_mapOverrides.seq}: ${state.cities.length} гор, ${state.pois.length} точек (${customPois} своих), ${state.randomTargets.length} целей`, false);
        }
    } catch (e) { console.warn('[OVR] storeMapOverrides:', e); }
}

// Применяет пакет: заменяет state.cities/state.pois/state.customTargets +
// пересобирает randomTargets. ТРИГГЕРЫ целей переинициируются (inZone/armed сброс).
function applyMapOverrides() {
    if (!_mapOverrides) return;
    const ov = _mapOverrides;

    // ГОРОДА: пакет содержит ПОЛНЫЙ список (статика + overrides, hidden отфильтрован C#).
    state.cities = ov.cities.map(c => ({
        x: Number(c.x) || 0,
        y: Number(c.y) || 0,
        z: Number(c.z) || 0,
        name: c.realName || c.gameName || '',
        gameName: c.gameName || '',
        hidden: !!c.hidden
    })).filter(c => !c.hidden && (c.x || c.z));

    // POI: полный список + пользовательские точки (category='custom' рисуются
    // особым цветом из poi.color). SDO-точки несут icon (meta.json, png 50x50 в
    // editor_static_data\icons) — миникарта рисует иконку вместо кружка.
    state.pois = ov.pois.map(p => ({
        x: Number(p.x) || 0,
        z: Number(p.z) || 0,
        y: 0,
        uid: p.uid || p.gameName || '',
        type: p.category || 'custom',
        name: p.name || p.realName || p.uid || 'poi',
        color: p.color || undefined,
        icon: p.icon || undefined,
        hidden: !!p.hidden
    })).filter(p => !p.hidden && (p.x || p.z));

    // ЦЕЛИ (из map_overrides\test_targets.json) — тот же normalizeTarget, что
    // использовал targets_data. Кулдаун (cooldownUntil в будущем) скрывает точку.
    state.customTargets = ov.targets.map(normalizeTarget);
    if (!state.randomTargets) state.randomTargets = [];
    const nowMs = Date.now();
    state.randomTargets = state.customTargets.filter(t => {
        if (!t.isRandom) return false;
        if (t.status === 'inactive' && t.cooldownUntil) {
            const until = Date.parse(t.cooldownUntil);
            if (!isNaN(until) && until > nowMs) return false; // ещё на кулдауне
        }
        return true;
    });
    state.randomTargets.forEach(t => { t.inZone = false; t.armed = false; });

    // Основная цель = последняя (для targetX/Y/Z + указателя).
    if (state.randomTargets.length) {
        const last = state.randomTargets[state.randomTargets.length - 1];
        randomTarget = last;
        targetX.value = last.x.toFixed(2);
        targetY.value = last.y.toFixed(2);
        targetZ.value = last.z.toFixed(2);
        state.target.x = last.x; state.target.y = last.y; state.target.z = last.z;
        focusTargetOnMap(last.x, last.z);
    } else {
        randomTarget = null;
        targetX.value = '0.00'; targetY.value = '0.00'; targetZ.value = '0.00';
        state.target.x = 0; state.target.y = 0; state.target.z = 0;
    }

    if (typeof updateAll === 'function') updateAll();
    if (typeof drawMinimap === 'function') drawMinimap();
    console.log(`[OVR] применено: cities=${state.cities.length}, pois=${state.pois.length}, targets=${state.randomTargets.length}`);

    // Статусная строка: операция завершена.
    if (window.mapStatusSetOperation) mapStatusSetOperation(`overrides обновлены (#${_mapOverridesSeq})`, false);
}

// ==== СОВМЕСТИМОСТЬ (старый API) ====
// map_draw.js вызывает getEffectiveCityList/getEffectivePoiList — теперь они
// возвращают state-списки напрямую (пакет уже применён: state = effective).
function getEffectiveCityList() { return state.cities; }
function getEffectivePoiList() { return state.pois; }

// Старая команда points_overrides_data: переводим на тот же dumb-приёмник.
function storePointsOverrides(data) {
    // Нормализуем старый формат (cities/pois/userPoints) в новый.
    const userPoints = Array.isArray(data && data.userPoints) ? data.userPoints : [];
    storeMapOverrides({
        seq: data && data.seq,
        cities: (data && data.cities) || [],
        pois: ((data && data.pois) || []).concat(userPoints.map(u => ({
            uid: u.gameName, category: 'custom', name: u.realName || u.gameName,
            x: u.x, z: u.z, color: u.color, hidden: !!u.hidden
        }))),
        targets: []
    });
}