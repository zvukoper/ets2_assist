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
function generateRandomTarget() {
    removeRandomTarget();

    const truckX = state.truck.x;
    const truckZ = state.truck.z;
    const radiusM = 2000;
    const maxAttempts = 300;
    const maxDistanceToRoad = 10;
    const maxDistanceToPOI = 50;

    let targetPoint = null;

    // 1. Найти ближайший POI
    function findNearestPOI(x, z) {
        let minDist = Infinity;
        let nearest = null;
        for (const city of state.cities) {
            const d = Math.hypot(city.x - x, city.z - z);
            if (d < minDist) { minDist = d; nearest = city; }
        }
        for (const poi of state.pois) {
            const d = Math.hypot(poi.x - x, poi.z - z);
            if (d < minDist) { minDist = d; nearest = poi; }
        }
        for (const t of state.customTargets) {
            if (t.isRandom) continue;
            const d = Math.hypot(t.x - x, t.z - z);
            if (d < minDist) { minDist = d; nearest = t; }
        }
        return nearest;
    }

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

    // Стратегия 1: ищем точку около POI, на дороге, в радиусе 2 км
    if (state.roads.length > 0 && state.pois.length > 0) {
        for (let attempt = 0; attempt < maxAttempts; attempt++) {
            const poiIdx = Math.floor(Math.random() * state.pois.length);
            const poi = state.pois[poiIdx];
            const angle = Math.random() * 2 * Math.PI;
            const dist = Math.random() * maxDistanceToPOI;
            const x = poi.x + Math.cos(angle) * dist;
            const z = poi.z + Math.sin(angle) * dist;
            const distToTruck = Math.hypot(x - truckX, z - truckZ);
            if (distToTruck > radiusM) continue;
            const distToRoad = findNearestRoadDistance(x, z);
            if (distToRoad <= maxDistanceToRoad) {
                targetPoint = { x, z };
                break;
            }
        }
    }

    // Стратегия 2: ищем на любой дороге в радиусе 2 км и отодвигаем на 10 м
    if (!targetPoint && state.roads.length > 0) {
        for (let attempt = 0; attempt < maxAttempts; attempt++) {
            const roadIdx = Math.floor(Math.random() * state.roads.length);
            const road = state.roads[roadIdx];
            const t = Math.random();
            const x = road.x1 + (road.x2 - road.x1) * t;
            const z = road.z1 + (road.z2 - road.z1) * t;
            const distToTruck = Math.hypot(x - truckX, z - truckZ);
            if (distToTruck > radiusM) continue;
            const distToRoad = findNearestRoadDistance(x, z);
            if (distToRoad <= maxDistanceToRoad) {
                // Смещаем на 10 м в сторону от дороги
                const dx = road.x2 - road.x1;
                const dz = road.z2 - road.z1;
                const len = Math.hypot(dx, dz);
                if (len > 0) {
                    const nx = -dz / len;
                    const nz = dx / len;
                    const side = Math.random() > 0.5 ? 1 : -1;
                    targetPoint = { x: x + nx * side * 10, z: z + nz * side * 10 };
                    break;
                }
            }
        }
    }

    // Стратегия 3: последняя попытка – случайная дорога + 10 м
    if (!targetPoint && state.roads.length > 0) {
        const road = state.roads[Math.floor(Math.random() * state.roads.length)];
        const t = Math.random();
        const x = road.x1 + (road.x2 - road.x1) * t;
        const z = road.z1 + (road.z2 - road.z1) * t;
        const dx = road.x2 - road.x1;
        const dz = road.z2 - road.z1;
        const len = Math.hypot(dx, dz);
        if (len > 0) {
            const nx = -dz / len;
            const nz = dx / len;
            const side = Math.random() > 0.5 ? 1 : -1;
            targetPoint = { x: x + nx * side * 10, z: z + nz * side * 10 };
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
        name: 'Случайная цель',
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

    updateAll();
    showToast('Случайная цель добавлена', 3000);
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