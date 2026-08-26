// ================================================================
// ЗАГРУЗКА ЦЕЛЕЙ ИЗ custom_targets.json
// ================================================================
async function loadCustomTargets() {
    try {
        const res = await fetch(withRnd('/custom_targets.json'), { cache: 'no-store' });
        if (res.ok) {
            const data = await res.json();
            state.targetMapOverview = false;
            state.zoomOnMapTargets = [];
            // Сохраняем случайные цели, созданные в памяти, чтобы перезагрузка
            // файла (или неудачный POST сохранения) их не стёрла из состояния.
            const inMemoryRandoms = state.customTargets.filter(t => t && t.isRandom);
            state.customTargets = [];
            const targetList = Array.isArray(data) ? data : data.customTargets;
            if (Array.isArray(targetList)) {
                state.customTargets = targetList.map(t => {
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
                    }
                    return {
                        x, y, z,
                        name: t.realName || t.gameName || 'Цель',
                        active: t.status === 'active',
                        gameName: t.gameName || '',
                        icon: t.icon || 'default',
                        color: t.color || 'default',
                        zoomOnMap: t.targetMapOverview === true,
                        isRandom: t.isRandom || false
                    };
                });
                const active = state.customTargets.find(t => t.active);
                if (active) {
                    targetX.value = active.x.toFixed(2);
                    targetY.value = active.y.toFixed(2);
                    targetZ.value = active.z.toFixed(2);
                    state.target.x = active.x;
                    state.target.y = active.y;
                    state.target.z = active.z;
                }
                const random = state.customTargets.find(t => t.isRandom);
                if (random) {
                    randomTarget = random;
                    randomTargetReachedSent = false;
                    focusTargetOnMap(random.x, random.z);
                } else if (inMemoryRandoms.length > 0) {
                    // Файл не содержит случайную цель (например, POST сохранения не дошёл) —
                    // возвращаем последнюю созданную в памяти, чтобы она оставалась на карте.
                    const memRandom = inMemoryRandoms[inMemoryRandoms.length - 1];
                    state.customTargets.push(memRandom);
                    randomTarget = memRandom;
                    randomTargetReachedSent = false;
                    focusTargetOnMap(memRandom.x, memRandom.z);
                    targetX.value = memRandom.x.toFixed(2);
                    targetY.value = memRandom.y.toFixed(2);
                    targetZ.value = memRandom.z.toFixed(2);
                    state.target.x = memRandom.x;
                    state.target.y = memRandom.y;
                    state.target.z = memRandom.z;
                }
            }
        }
    } catch (e) {
        console.warn('[TARGETS] Ошибка загрузки custom_targets.json:', e?.message || e);
    }
}

// ================================================================
// СОХРАНЕНИЕ ЦЕЛЕЙ В ФАЙЛ
// ================================================================
async function saveTargetsToFile() {
    try {
        const targets = state.customTargets.map(t => ({
            gameName: t.gameName || t.name,
            realName: t.name,
            coords: `${t.x.toFixed(2)}, ${t.y.toFixed(2)}, ${t.z.toFixed(2)}`,
            status: t.active ? "active" : "",
            icon: t.icon || "default",
            color: t.color || "default",
            targetMapOverview: t.zoomOnMap || false,
            isRandom: t.isRandom || false
        }));
        const response = await fetch('http://localhost:8083/update_targets', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(targets)
        });
        if (!response.ok) {
            console.warn('[saveTargets] Ошибка сохранения:', await response.text());
        } else {
            console.log('[saveTargets] Цели сохранены в custom_targets.json');
        }
    } catch (e) {
        console.warn('[saveTargets] Ошибка:', e);
    }
}

// ================================================================
// ГЕНЕРАЦИЯ СЛУЧАЙНОЙ ЦЕЛИ (улучшенная)
// ================================================================
function focusTargetOnMap(x, z) {
    // Гарантируем, что цель попадёт в поле зрения миникарты (авто-приближение).
    state.targetMapOverview = true;
    state.zoomOnMapTargets = [{ x, z }];
}

function generateRandomTarget(options) {
    removeRandomTarget();

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

    if (!targetPoint) {
        showToast('Не удалось найти подходящее место для случайной цели.', 4000);
        console.warn('[generateRandomTarget] Не найдено подходящей точки.');
        return;
    }

    const newTarget = {
        x: targetPoint.x,
        y: 0,
        z: targetPoint.z,
        name: name,
        active: true,
        color: '#ff0000',
        icon: 'default',
        gameName: 'random',
        zoomOnMap: false,
        isRandom: true
    };

    state.customTargets.forEach(t => t.active = false);
    state.customTargets.push(newTarget);
    randomTarget = newTarget;
    randomTargetReachedSent = false;

    targetX.value = newTarget.x.toFixed(2);
    targetY.value = newTarget.y.toFixed(2);
    targetZ.value = newTarget.z.toFixed(2);
    state.target.x = newTarget.x;
    state.target.y = newTarget.y;
    state.target.z = newTarget.z;

    if (saveWs && saveWs.readyState === WebSocket.OPEN) {
        saveWs.send(JSON.stringify({
            command: 'target_created',
            target: {
                x: newTarget.x,
                z: newTarget.z,
                name: newTarget.name
            }
        }));
        console.log('[TRIGGER] Отправлена команда target_created на сервер');
    }

    focusTargetOnMap(newTarget.x, newTarget.z);
    updateAll();
    showToast(name + ' добавлена', 3000);
    saveTargetsToFile();
}

function removeRandomTarget() {
    if (randomTarget) {
        state.customTargets = state.customTargets.filter(t => t !== randomTarget);
        randomTarget = null;
        randomTargetReachedSent = false;
        const hasActive = state.customTargets.some(t => t.active);
        if (!hasActive) {
            targetX.value = '0.00';
            targetY.value = '0.00';
            targetZ.value = '0.00';
            state.target.x = 0;
            state.target.y = 0;
            state.target.z = 0;
        }
        updateAll();
        showToast('Случайная цель удалена', 3000);
        saveTargetsToFile();
    }
}