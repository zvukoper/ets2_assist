// ================================================================
// ПРИЁМ ЦЕЛЕЙ ОТ ПРИЛОЖЕНИЯ (минимапта НЕ читает файл сама)
// ================================================================
// Миникарта больше не обращается к custom_targets.json напрямую — файл
// является собственностью приложения (C#). Приложение читает файл (один раз
// при старте, а также при добавлении/удалении цели и по «Проверке точек»)
// и шлёт содержимое командой targets_data. Здесь мы только принимаем данные
// и перестраиваем состояние отрисовки.
function normalizeTarget(t) {
    let x = 0, y = 0, z = 0;
    if (Array.isArray(t.coords)) {
        x = Number(t.coords[0]) || 0;
        y = Number(t.coords[1]) || 0;
        z = Number(t.coords[2]) || 0;
    } else if (typeof t.coords === 'string') {
        const parts = t.coords.split(',').map(s => s.trim());
        if (parts.length >= 3) {
            x = parseFloat(parts[0]) || 0;
            y = parseFloat(parts[1]) || 0;
            z = parseFloat(parts[2]) || 0;
        }
    } else if (typeof t.x === 'number') {
        x = Number(t.x) || 0;
        y = Number(t.y) || 0;
        z = Number(t.z) || 0;
    }
    return {
        x, y, z,
        name: t.realName || t.gameName || t.name || 'Цель',
        active: t.status === 'active',
        gameName: t.gameName || '',
        icon: t.icon || 'default',
        color: t.color || 'default',
        zoomOnMap: t.targetMapOverview === true,
        isRandom: t.isRandom || false,
        id: t.id || '',
        questType: t.questType || null,
        radius: Number(t.radius) || 50,
        hidden: Number(t.hidden) || 0,
        cooldown: Number(t.cooldown) || 0,
        currentCooldown: Number(t.current_cooldown) || 0,
        deleteOnComplete: Number(t.delete_on_complete) || 0
    };
}

function applyTargetsData(targetArray) {
    try {
        const list = Array.isArray(targetArray) ? targetArray : [];
        state.customTargets = list.map(normalizeTarget);
        // Все случайные цели — в отдельный массив (поддержка нескольких одновременно).
        // state.target фокусируется на последней; inZone/armed сбрасываем (каждый кадр
        // trail.js переопределит по фактической близости).
        if (!state.randomTargets) state.randomTargets = [];
        state.randomTargets = state.customTargets.filter(t => t.isRandom);
        state.randomTargets.forEach(t => { t.inZone = false; t.armed = false; });
        if (state.randomTargets.length) {
            const last = state.randomTargets[state.randomTargets.length - 1];
            randomTarget = last;
            targetX.value = last.x.toFixed(2);
            targetY.value = last.y.toFixed(2);
            targetZ.value = last.z.toFixed(2);
            state.target.x = last.x;
            state.target.y = last.y;
            state.target.z = last.z;
            focusTargetOnMap(last.x, last.z);
        } else {
            randomTarget = null;
            targetX.value = '0.00';
            targetY.value = '0.00';
            targetZ.value = '0.00';
            state.target.x = 0;
            state.target.y = 0;
            state.target.z = 0;
        }
    } catch (e) {
        console.warn('[TARGETS] Ошибка приёма целей от приложения:', e?.message || e);
    }
}

// ================================================================
// СОХРАНЕНИЕ ЦЕЛЕЙ В ФАЙЛ — БОЛЬШЕ НЕ ДЕЛАЕТСЯ ИЗ МИНИКАРТЫ
// ================================================================
// Миникарта не пишет и не читает custom_targets.json. Все изменения
// файла выполняет приложение (C#): оно дописывает созданную цель по
// команде add_target и удаляет по достижении, затем шлёт targets_data.
// Поэтому функция saveTargetsToFile удалена.

// ================================================================
// ГЕНЕРАЦИЯ СЛУЧАЙНОЙ ЦЕЛИ (улучшенная)
// ================================================================
function focusTargetOnMap(x, z) {
    // Обзор целей управляется ТОЛЬКО приложением (тоггл). Здесь только фокус основной цели.
    state.zoomOnMapTargets = [{ x, z }];
}

async function generateRandomTarget(options) {
    const truckX = state.truck.x;
    const truckZ = state.truck.z;
    const opts = options || {};
    const nearTruck = opts.nearTruck !== false;
    const requirePoi = opts.requirePoi === true;
    const radiusM = nearTruck ? (Number(opts.radiusM) || 2000) : Infinity;
    const distanceM = opts.distanceM ? Number(opts.distanceM) : 0;
    const maxDistanceToRoad = 15;
    const maxDistanceToPOI = 60;
    const name = opts.name || 'Случайная цель';

    let targetPoint = null;

    function findNearestRoadDistance(x, z) {
        let minDist = Infinity;
        for (const road of state.roads) {
            const dx = road.x2 - road.x1;
            const dz = road.z2 - road.z1;
            const lenSq = dx*dx + dz*dz;
            if (lenSq === 0) continue;
            let t = ((x - road.x1) * dx + (z - road.z1) * dz) / lenSq;
            t = Math.max(0, Math.min(1, t));
            const projX = road.x1 + t * dx;
            const projZ = road.z1 + t * dz;
            const dist = Math.hypot(x - projX, z - projZ);
            if (dist < minDist) minDist = dist;
        }
        return minDist;
    }

    function offsetFromRoad(road, t) {
        const x = road.x1 + (road.x2 - road.x1) * t;
        const z = road.z1 + (road.z2 - road.z1) * t;
        const dx = road.x2 - road.x1;
        const dz = road.z2 - road.z1;
        const len = Math.hypot(dx, dz);
        if (len <= 0) return { x, z };
        const nx = -dz / len;
        const nz = dx / len;
        const side = Math.random() > 0.5 ? 1 : -1;
        return { x: x + nx * side * 10, z: z + nz * side * 10 };
    }

    function nearestPointOnRoad(x, z) {
        let best = { x, z };
        let bestDist = Infinity;
        for (const road of state.roads) {
            const dx = road.x2 - road.x1, dz = road.z2 - road.z1;
            const lenSq = dx*dx + dz*dz;
            let t = lenSq ? ((x - road.x1) * dx + (z - road.z1) * dz) / lenSq : 0;
            t = Math.max(0, Math.min(1, t));
            const px = road.x1 + dx * t, pz = road.z1 + dz * t;
            const dist = Math.hypot(x - px, z - pz);
            if (dist < bestDist) { bestDist = dist; best = { x: px, z: pz }; }
        }
        return best;
    }

    // Режим: цель СТРОГО НА POI (здание/компания) — ближайшем к фуре в заданном
    // радиусе (poiMinDistM..poiMaxDistM), либо ближайшем к запрошенному расстоянию
    // (poiTargetDistM). Используется для Курьера, чтобы доставка не попадала на трассу.
    if (opts.atPoi) {
        const maxDist = Number(opts.poiMaxDistM) || 3000;
        const minDist = Number(opts.poiMinDistM) || 0;
        const targetDist = Number(opts.poiTargetDistM) || 0;
        const cand = [];
        for (const poi of state.pois) {
            const d = Math.hypot(poi.x - truckX, poi.z - truckZ);
            if (d < minDist || d > maxDist) continue;
            if (findNearestRoadDistance(poi.x, poi.z) > maxDistanceToPOI) continue;
            cand.push({ poi, d });
        }
        if (cand.length) {
            let chosen = cand[0].poi;
            if (targetDist > 0) {
                cand.sort((a, b) => Math.abs(a.d - targetDist) - Math.abs(b.d - targetDist));
                chosen = cand[0].poi;
            } else {
                chosen = cand[Math.floor(Math.random() * cand.length)].poi;
            }
            targetPoint = { x: chosen.x, z: chosen.z };
        }
    }

    // Режим: цель на заданном расстоянии (~distanceM) от фуры — гарантированно в зоне видимости
    if (distanceM > 0) {
        for (let attempt = 0; attempt < 400 && !targetPoint; attempt++) {
            const ang = Math.random() * 2 * Math.PI;
            const dist = distanceM * (0.85 + Math.random() * 0.3);
            const x = truckX + Math.cos(ang) * dist;
            const z = truckZ + Math.sin(ang) * dist;
            if (findNearestRoadDistance(x, z) <= maxDistanceToRoad) targetPoint = { x, z };
        }
        if (!targetPoint) {
            const ang = Math.random() * 2 * Math.PI;
            const dist = distanceM;
            targetPoint = { x: truckX + Math.cos(ang) * dist, z: truckZ + Math.sin(ang) * dist };
        }
    }

    // Режим: строго НА ДОРОГЕ, в радиусе [minDistM, maxDistM] от фуры (кнопка №4)
    if (!targetPoint && requireRoad) {
        const minD = Number(opts.minDistM) || 51;
        const maxD = Number(opts.maxDistM) || 60;
        if (state.roads.length > 0) {
            for (let attempt = 0; attempt < 5000 && !targetPoint; attempt++) {
                const road = state.roads[Math.floor(Math.random() * state.roads.length)];
                const tt = Math.random();
                const x = road.x1 + (road.x2 - road.x1) * tt;
                const z = road.z1 + (road.z2 - road.z1) * tt;
                const d = Math.hypot(x - truckX, z - truckZ);
                if (d >= minD && d <= maxD) targetPoint = { x, z };
            }
        }
        if (!targetPoint) {
            // Запасной вариант: точка в нужном радиусе, привязанная к ближайшей дороге
            const ang = Math.random() * 2 * Math.PI;
            const d = minD + Math.random() * (maxD - minD);
            const px = truckX + Math.cos(ang) * d;
            const pz = truckZ + Math.sin(ang) * d;
            targetPoint = state.roads.length > 0 ? nearestPointOnRoad(px, pz) : { x: px, z: pz };
            console.warn('[generateRandomTarget] requireRoad: точная привязка к радиусу не найдена, использован запасной вариант');
        }
    }

    // Кнопка 1: случайная цель в радиусе фуры (рандомная дистанция до radiusM), рядом с дорогой
    if (!targetPoint && nearTruck) {
        if (state.roads.length > 0) {
            const candidates = [];
            for (const road of state.roads) {
                const dx = road.x2 - road.x1;
                const dz = road.z2 - road.z1;
                const lenSq = dx*dx + dz*dz;
                let t = lenSq ? ((truckX - road.x1) * dx + (truckZ - road.z1) * dz) / lenSq : 0;
                t = Math.max(0, Math.min(1, t));
                const cx = road.x1 + t * dx;
                const cz = road.z1 + t * dz;
                if (Math.hypot(truckX - cx, truckZ - cz) <= radiusM + maxDistanceToRoad) candidates.push(road);
            }
            if (candidates.length > 0) {
                const road = candidates[Math.floor(Math.random() * candidates.length)];
                targetPoint = offsetFromRoad(road, Math.random());
            }
        }
        // Запасной вариант: случайная точка в радиусе рядом с дорогой
        for (let attempt = 0; attempt < 600 && !targetPoint; attempt++) {
            const ang = Math.random() * 2 * Math.PI;
            const dist = Math.random() * radiusM;
            const x = truckX + Math.cos(ang) * dist;
            const z = truckZ + Math.sin(ang) * dist;
            if (findNearestRoadDistance(x, z) <= maxDistanceToRoad) targetPoint = { x, z };
        }
    }

    // Кнопка 2: случайная цель рядом с POI и дорогой, на любом расстоянии
    if (!targetPoint && requirePoi) {
        if (state.pois.length > 0 && state.roads.length > 0) {
            for (let attempt = 0; attempt < 2000 && !targetPoint; attempt++) {
                const poi = state.pois[Math.floor(Math.random() * state.pois.length)];
                if (findNearestRoadDistance(poi.x, poi.z) <= maxDistanceToPOI) {
                    targetPoint = { x: poi.x, z: poi.z };
                }
            }
        }
        if (!targetPoint && state.roads.length > 0) {
            const road = state.roads[Math.floor(Math.random() * state.roads.length)];
            targetPoint = offsetFromRoad(road, Math.random());
        }
    }

    // ГАРАНТИРОВАННЫЙ ЗАПАСНОЙ ВАРИАНТ: случайная точка около фуры без
    // привязки к дорогам/POI. Нужен, если статические данные карты (roads/POI)
    // не загружены — иначе цель просто не создавалась и не появлялась на карте.
    if (!targetPoint) {
        const fallbackDist = distanceM > 0
            ? distanceM
            : (radiusM !== Infinity ? radiusM * (0.4 + Math.random() * 0.6) : 1500);
        const ang = Math.random() * 2 * Math.PI;
        targetPoint = {
            x: truckX + Math.cos(ang) * fallbackDist,
            z: truckZ + Math.sin(ang) * fallbackDist
        };
        console.log('[generateRandomTarget] Использован запасной спавн без дорог/POI, дист=' + Math.round(fallbackDist));
    }

    const id = (opts && opts.id) || ('rt_' + Date.now().toString(36) + '_' + Math.floor(Math.random() * 1e4).toString(36));
    const radius = Number(opts && opts.radius) || 50;

    const newTarget = {
        id: id,
        x: targetPoint.x,
        y: 0,
        z: targetPoint.z,
        name: name,
        active: opts.active !== false,
        color: opts.color || '#ff0000',
        questType: opts.questType || null,
        icon: 'default',
        gameName: 'random',
        zoomOnMap: false,
        isRandom: true,
        radius: radius,
        hidden: opts.hidden ? 1 : 0,
        cooldown: opts.cooldown || 0,
        deleteOnComplete: opts.deleteOnComplete || 0,
        inZone: false,
        armed: false
    };

    // ДЕДУП ПО ТИПУ: случайная цель каждого questType может быть только одна.
    // Удаляем предыдущую цель того же типа (защита от флаппинга WS / повторных
    // нажатий кнопки), чтобы не плодились двойники в разных местах.
    if (opts && opts.questType) {
        const sameType = (state.randomTargets || []).filter(t => t.questType === opts.questType);
        sameType.forEach(old => {
            if (old.id === id) return;
            state.randomTargets = (state.randomTargets || []).filter(t => t !== old);
            state.customTargets = (state.customTargets || []).filter(t => t !== old);
            if (saveWs && saveWs.readyState === WebSocket.OPEN) {
                saveWs.send(JSON.stringify({ command: 'remove_target', id: old.id }));
                console.log('[TARGETS] Удалён дубликат типа ' + opts.questType + ' (id=' + old.id + ')');
            }
        });
    }

    state.customTargets.push(newTarget);
    if (!state.randomTargets) state.randomTargets = [];
    state.randomTargets.push(newTarget);
    randomTarget = newTarget;
    randomTargetReachedSent = false;

    targetX.value = newTarget.x.toFixed(2);
    targetY.value = newTarget.y.toFixed(2);
    targetZ.value = newTarget.z.toFixed(2);
    state.target.x = newTarget.x;
    state.target.y = newTarget.y;
    state.target.z = newTarget.z;

    // Диагностика: снимок хранилища точек сразу после добавления кнопкой
    if (saveWs && saveWs.readyState === WebSocket.OPEN) {
        const distToTruck = Math.round(Math.hypot(newTarget.x - state.truck.x, newTarget.z - state.truck.z));
        // Уведомляем приложение о создании цели (лог/награда)...
        saveWs.send(JSON.stringify({
            command: 'target_created',
            target: {
                id: newTarget.id,
                x: newTarget.x,
                z: newTarget.z,
                name: newTarget.name,
                color: newTarget.color,
                questType: newTarget.questType,
                active: newTarget.active,
                dist: distToTruck
            }
        }));
        // ...и просим приложение записать цель в custom_targets.json и
        // прислать обновлённые данные (targets_data). Сама миникарта файл не трогает.
        saveWs.send(JSON.stringify({
            command: 'add_target',
            target: {
                id: newTarget.id,
                x: newTarget.x,
                z: newTarget.z,
                name: newTarget.name,
                color: newTarget.color,
                questType: newTarget.questType,
                active: newTarget.active,
                radius: newTarget.radius,
                hidden: newTarget.hidden,
                cooldown: newTarget.cooldown,
                delete_on_complete: newTarget.deleteOnComplete,
                isRandom: true
            }
        }));
        console.log('[TRIGGER] Отправлены target_created + add_target, дист. до фуры=' + distToTruck + 'м');
    }

    focusTargetOnMap(newTarget.x, newTarget.z);
    updateAll();
    showToast(name + ' добавлена', 3000);
}

function removeRandomTarget(id) {
    if (!id && randomTarget) id = randomTarget.id;
    if (state.randomTargets && state.randomTargets.length) {
        const t = state.randomTargets.find(r => r.id === id);
        if (t) {
            state.randomTargets = state.randomTargets.filter(r => r !== t);
            state.customTargets = state.customTargets.filter(c => c !== t);
        }
    } else if (randomTarget) {
        state.customTargets = state.customTargets.filter(t => t !== randomTarget);
    }
    if (!state.randomTargets || state.randomTargets.length === 0) {
        randomTarget = null;
        randomTargetReachedSent = false;
        targetX.value = '0.00';
        targetY.value = '0.00';
        targetZ.value = '0.00';
        state.target.x = 0;
        state.target.y = 0;
        state.target.z = 0;
    } else {
        randomTarget = state.randomTargets[state.randomTargets.length - 1];
    }
    updateAll();
    showToast('Случайная цель удалена', 3000);
    // Запись в файл выполняет приложение (команда remove_target ->
    // C# удаляет цель из custom_targets.json и шлёт targets_data).
}

// Список всех созданных точек -> отправка в C# для записи в лог (команда list_targets)
function listTargets() {
    try {
        const items = [];
        const tX = state.truck.x, tZ = state.truck.z;
        for (const t of state.customTargets) {
            const d = Math.round(Math.hypot((t.x || 0) - tX, (t.z || 0) - tZ));
            items.push({ name: t.name || 'Цель', x: (t.x || 0).toFixed(2), z: (t.z || 0).toFixed(2), dist: d });
        }
        if (randomTarget && !items.some(it => Math.abs(parseFloat(it.x) - randomTarget.x) < 0.1 && Math.abs(parseFloat(it.z) - randomTarget.z) < 0.1)) {
            const d = Math.round(Math.hypot(randomTarget.x - tX, randomTarget.z - tZ));
            items.push({ name: randomTarget.name || 'Случайная цель', x: randomTarget.x.toFixed(2), z: randomTarget.z.toFixed(2), dist: d });
        }
        if (saveWs && saveWs.readyState === WebSocket.OPEN) {
            saveWs.send(JSON.stringify({ command: 'targets_list', targets: items }));
            console.log('[TARGETS] Отправлен список точек (' + items.length + ')');
        } else {
            console.warn('[TARGETS] Невозможно отправить список: saveWs не подключён');
        }
        showToast('Список точек отправлен в лог (' + items.length + ')', 2500);
    } catch (e) {
        console.warn('[TARGETS] Ошибка listTargets:', e?.message || e);
    }
}

// Диагностика хранилища точек теперь ведётся на стороне приложения
// (команда «Проверка точек» принудительно перечитывает custom_targets.json
// и шлёт targets_data). Функция reportTargetsSnapshot удалена.