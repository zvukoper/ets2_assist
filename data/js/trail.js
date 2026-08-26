// ================================================================
// ЦВЕТ ШЛЕЙФА
// ================================================================
function getTrailColor(speed) {
    const minS = Math.max(0, MIN_SPEED_KMH);
    const maxS = Math.max(minS, MAX_SPEED_KMH);
    const s = Math.max(minS, Math.min(maxS, speed));
    const t = (s - minS) / (maxS - minS);
    let r,g,b;
    if (t <= 0.2) {
        const u = t / 0.2;
        r = 0; g = u * 255; b = 255;
    } else if (t <= 0.4) {
        const u = (t - 0.2) / 0.2;
        r = 0; g = 255; b = 255 - u * 255;
    } else if (t <= 0.6) {
        const u = (t - 0.4) / 0.2;
        r = u * 255; g = 255; b = 0;
    } else if (t <= 0.8) {
        const u = (t - 0.6) / 0.2;
        r = 255; g = 255 - u * (255 - 165); b = 0;
    } else {
        const u = (t - 0.8) / 0.2;
        r = 255 - u * (255 - 128); g = 165 - u * 165; b = u * 255;
    }
    return `rgb(${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)})`;
}

// ================================================================
// ОБНОВЛЕНИЕ ШЛЕЙФА
// ================================================================
let recordingMode = 'auto';
let recordingActive = true;
let trackTitleSuffix = '';
let trackDescription = '';
let targetReachedSending = false; // защита от дублей

function updateTrail(speed, pos, engineOn, fuel, damage, gameTime, heading, pitch, roll) {
    if (!recordingActive) return;

    const tX = pos.x, tZ = pos.z;
    const currentSpeed = speed || 0;
    const currentHeading = heading || state.truck.heading || 0;
    const now = Date.now();

    if (state.trailStartTime) {
        state.elapsedSeconds = (now - state.trailStartTime) / 1000;
    }

    // Телепорт
    let isTeleport = false;
    if (state.prevPos) {
        const dx = tX - state.prevPos.x;
        const dz = tZ - state.prevPos.z;
        const dist = Math.sqrt(dx*dx + dz*dz);
        const speedChange = Math.abs(currentSpeed - state.prevSpeed);
        if (dist > 500 || speedChange > 150) {
            isTeleport = true;
        }
    }
    state.prevPos = { x: tX, z: tZ };
    state.prevSpeed = currentSpeed;

    if (isTeleport) {
        if (state.trail.length > 0) {
            const last = state.trail[state.trail.length - 1];
            if (!isNearExistingMarker(last.x, last.z)) {
                state.eventMarkers.push({
                    x: last.x,
                    z: last.z,
                    type: 'service',
                    label: 'S',
                    color: '#2266cc',
                    subtext: 'сервис'
                });
                playSoundViaWS('icon');
            }
        }
        if (!isNearExistingMarker(tX, tZ)) {
            state.eventMarkers.push({
                x: tX,
                z: tZ,
                type: 'service',
                label: 'S',
                color: '#2266cc',
                subtext: 'сервис'
            });
            playSoundViaWS('icon');
        }
        state.trail = [];
        state.dataPoints = [];
        state.isParking = false;
        state.isStopped = false;
        state.parkingStartGameTime = null;
        state.parkingStartRealTime = null;
        state.stopStartRealTime = null;
        state.stopPos = null;
        state.stopTextMarker = null;
        state._lastDataDist = 0;
        if (state.damageTimer) {
            clearTimeout(state.damageTimer);
            state.damageTimer = null;
        }
        return;
    }

    // Урон
    if (damage !== undefined && damage !== null) {
        const damageChange = damage - state.lastDamage;
        if (damageChange > 0.001) {
            if (!state.damageTimer) {
                state.damageAccumulator = damageChange;
                state.damageStartPos = { x: tX, z: tZ };
                state.damageTimer = setTimeout(() => {
                    if (state.damageStartPos && state.damageAccumulator > 0) {
                        if (!isNearExistingMarker(state.damageStartPos.x, state.damageStartPos.z)) {
                            state.eventMarkers.push({
                                x: state.damageStartPos.x,
                                z: state.damageStartPos.z,
                                type: 'damage',
                                label: '!',
                                color: '#ff4444',
                                subtext: `${(state.damageAccumulator * 100).toFixed(0)}%`
                            });
                            playSoundViaWS('icon');
                        }
                    }
                    state.damageTimer = null;
                    state.damageAccumulator = 0;
                    state.damageStartPos = null;
                }, 3000);
            } else {
                state.damageAccumulator += damageChange;
                clearTimeout(state.damageTimer);
                state.damageTimer = setTimeout(() => {
                    if (state.damageStartPos && state.damageAccumulator > 0) {
                        if (!isNearExistingMarker(state.damageStartPos.x, state.damageStartPos.z)) {
                            state.eventMarkers.push({
                                x: state.damageStartPos.x,
                                z: state.damageStartPos.z,
                                type: 'damage',
                                label: '!',
                                color: '#ff4444',
                                subtext: `${(state.damageAccumulator * 100).toFixed(0)}%`
                            });
                            playSoundViaWS('icon');
                        }
                    }
                    state.damageTimer = null;
                    state.damageAccumulator = 0;
                    state.damageStartPos = null;
                }, 3000);
            }
        }
        state.lastDamage = damage;
    }

    // Логика остановки/парковки (сокращена для читаемости)
    const isEngineOn = engineOn === true;
    const gameTimeStr = gameTime || null;

    if (currentSpeed < 1) {
        if (!state.isParking && !state.isStopped && state.parkingStartRealTime === null) {
            state.parkingStartRealTime = now;
            state.parkingStartGameTime = gameTimeStr;
            state.parkingStartPos = { x: tX, z: tZ };
        }
        if (!isEngineOn && !state.isParking && state.parkingStartRealTime !== null) {
            if (now - state.parkingStartRealTime > 10000) {
                state.isParking = true;
                state.isStopped = false;
            }
        }
        if (isEngineOn && !state.isStopped && state.parkingStartRealTime !== null) {
            if (now - state.parkingStartRealTime > 10000) {
                state.isStopped = true;
                state.isParking = false;
                state.stopStartRealTime = state.parkingStartRealTime;
                state.stopPos = { x: tX, z: tZ };
            }
        }
    } else {
        if (state.isParking) {
            const startGame = state.parkingStartGameTime;
            let durationStr = '0:00';
            if (startGame) {
                try {
                    const start = new Date(startGame);
                    const nowGame = new Date(gameTimeStr || startGame);
                    const diffMs = nowGame - start;
                    if (diffMs > 0) {
                        const totalMin = Math.floor(diffMs / 60000);
                        const sec = Math.floor((diffMs % 60000) / 1000);
                        durationStr = `${totalMin}:${sec.toString().padStart(2, '0')}`;
                    }
                } catch (e) {}
            }
            if (state.parkingStartPos) {
                if (!isNearExistingMarker(state.parkingStartPos.x, state.parkingStartPos.z)) {
                    state.eventMarkers.push({
                        x: state.parkingStartPos.x,
                        z: state.parkingStartPos.z,
                        type: 'parking',
                        label: 'P',
                        color: '#4488ff',
                        subtext: durationStr,
                        square: true
                    });
                    playSoundViaWS('icon');
                }
            }
            state.isParking = false;
            state.parkingStartGameTime = null;
            state.parkingStartRealTime = null;
            state.parkingStartPos = null;
        }
        if (state.isStopped) {
            const durationMs = now - state.stopStartRealTime;
            const totalSec = Math.floor(durationMs / 1000);
            const hours = Math.floor(totalSec / 3600);
            const minutes = Math.floor((totalSec % 3600) / 60);
            let timeStr = '';
            if (hours > 0) timeStr = `${hours}ч `;
            timeStr += `${minutes}мин`;
            if (state.stopPos) {
                state.eventMarkers.push({
                    x: state.stopPos.x,
                    z: state.stopPos.z,
                    type: 'stop',
                    label: '⏸',
                    color: '#ffaa00',
                    subtext: timeStr,
                    textOnly: false
                });
                playSoundViaWS('icon');
            }
            state.isStopped = false;
            state.stopStartRealTime = null;
            state.stopPos = null;
            state.stopTextMarker = null;
        }
        if (state.parkingStartRealTime !== null && !state.isParking && !state.isStopped) {
            state.parkingStartRealTime = null;
            state.parkingStartGameTime = null;
            state.parkingStartPos = null;
        }
    }

    // Старт
    if (!state.firstMovementDetected && currentSpeed > 1) {
        state.firstMovementDetected = true;
        const nowDate = new Date();
        const label = `Старт ${nowDate.toLocaleDateString()} ${nowDate.toLocaleTimeString()}`;
        if (!isNearExistingMarker(tX, tZ)) {
            state.eventMarkers.push({
                x: tX,
                z: tZ,
                type: 'start',
                label: '▶',
                color: '#44ff88',
                subtext: label
            });
            playSoundViaWS('icon');
        }
    }

    // Добавление точки в шлейф
    if (state.trail.length > 0) {
        const last = state.trail[state.trail.length - 1];
        const parts = last.p.split(' ');
        const lx = parseFloat(parts[0]);
        const lz = parseFloat(parts[1]);
        const dx = tX - lx;
        const dz = tZ - lz;
        const dist = Math.sqrt(dx*dx + dz*dz);
        if (dist >= TRAIL_INTERVAL) {
            const pStr = `${tX.toFixed(2)} ${tZ.toFixed(2)} ${currentHeading.toFixed(4)}`;
            const elapsed = state.elapsedSeconds;
            state.trail.push({ p: pStr, s: currentSpeed.toFixed(2), t: elapsed.toFixed(1) });
            state.totalRealDistance += dist;
            state._lastDataDist += dist;
            if (state._lastDataDist >= DATA_INTERVAL || state.eventMarkers.length > 0) {
                const wearTotal = (damage || 0) * 100;
                const pitchVal = pitch !== undefined ? pitch : 0;
                const rollVal = roll !== undefined ? roll : 0;
                const headOffset = Array.isArray(state.headOffset) ? state.headOffset : [0, 0, 0, 0, 0, 0];
                const extraData = {
                    lights: state.lights,
                    gameTime: state.gameTimeMinutes,
                    localScale: state.localScale,
                    steering: state.steering,
                    throttle: state.throttle,
                    brake: state.brake,
                    odometer: state.odometer,
                    headOffset
                };
                state.dataPoints.push({
                    p: pStr,
                    fuel: fuel !== undefined ? fuel.toFixed(1) : '0',
                    damage: wearTotal.toFixed(1),
                    t: elapsed.toFixed(1),
                    pitch: pitchVal.toFixed(6),
                    roll: rollVal.toFixed(6),
                    lights: JSON.stringify(extraData.lights),
                    gameTime: extraData.gameTime,
                    localScale: extraData.localScale,
                    steering: extraData.steering,
                    throttle: extraData.throttle,
                    brake: extraData.brake,
                    odometer: extraData.odometer,
                    headOffset: extraData.headOffset.join(',')
                });
                state._lastDataDist = 0;
            }
        }
    } else {
        const pStr = `${tX.toFixed(2)} ${tZ.toFixed(2)} ${currentHeading.toFixed(4)}`;
        const elapsed = state.elapsedSeconds;
        state.trail.push({ p: pStr, s: currentSpeed.toFixed(2), t: elapsed.toFixed(1) });
        const wearTotal = (damage || 0) * 100;
        const pitchVal = pitch !== undefined ? pitch : 0;
        const rollVal = roll !== undefined ? roll : 0;
        const extraData = {
            lights: state.lights,
            gameTime: state.gameTimeMinutes,
            localScale: state.localScale,
            steering: state.steering,
            throttle: state.throttle,
            brake: state.brake,
            odometer: state.odometer,
            headOffset: Array.isArray(state.headOffset) ? state.headOffset : [0, 0, 0, 0, 0, 0]
        };
        state.dataPoints.push({
            p: pStr,
            fuel: fuel !== undefined ? fuel.toFixed(1) : '0',
            damage: wearTotal.toFixed(1),
            t: elapsed.toFixed(1),
            pitch: pitchVal.toFixed(6),
            roll: rollVal.toFixed(6),
            lights: JSON.stringify(extraData.lights),
            gameTime: extraData.gameTime,
            localScale: extraData.localScale,
            steering: extraData.steering,
            throttle: extraData.throttle,
            brake: extraData.brake,
            odometer: extraData.odometer,
            headOffset: extraData.headOffset.join(',')
        });
    }

    if (TRAIL_LENGTH > 0 && state.trail.length > TRAIL_LENGTH) {
        state.trail = state.trail.slice(-TRAIL_LENGTH);
    }

    // Отметки дистанции каждые 2 км
    if (state.totalRealDistance - state.lastDistanceMarker >= state.distanceMarkerThreshold) {
        const hasJob = state.jobDestination && state.estimatedDistance > 0;
        let label = '';
        if (hasJob) {
            const distKm = state.estimatedDistance / 1000;
            label = `${state.jobDestination} ${Math.round(distKm)} км`;
        } else {
            const distKm = state.totalRealDistance / 1000;
            label = `Пройдено ${distKm.toFixed(1)} км`;
        }
        if (!isNearExistingMarker(tX, tZ)) {
            state.eventMarkers.push({
                x: tX,
                z: tZ,
                type: 'distance',
                label: label,
                color: '#3366cc',
                subtext: '',
                distanceMarker: true
            });
            playSoundViaWS('icon');
            state.lastDistanceMarker = state.totalRealDistance;
        }
    }

    // ---- ОТСЛЕЖИВАНИЕ ДОСТИЖЕНИЯ СЛУЧАЙНОЙ ЦЕЛИ (с защитой от дублей) ----
    if (randomTarget && !targetReachedSending) {
        const distToTarget = Math.sqrt((tX - randomTarget.x)**2 + (tZ - randomTarget.z)**2);
        if (distToTarget < 50) {
            // Блокируем повторные отправки
            targetReachedSending = true;
            if (saveWs && saveWs.readyState === WebSocket.OPEN) {
                saveWs.send(JSON.stringify({
                    command: 'target_reached',
                    target: {
                        x: randomTarget.x,
                        z: randomTarget.z,
                        name: randomTarget.name
                    }
                }));
                console.log('[TRIGGER] Отправлена команда target_reached (флаг установлен)');
            } else {
                // Если WebSocket не открыт, снимаем блокировку через 2 секунды
                setTimeout(() => {
                    targetReachedSending = false;
                }, 2000);
            }
        }
    }
}

// ================================================================
// ПРОВЕРКА БЛИЗОСТИ ИКОНОК
// ================================================================
function isNearExistingMarker(x, z, excludeType = null) {
    const RADIUS = 20;
    for (const m of state.eventMarkers) {
        if (excludeType && m.type === excludeType) continue;
        const dx = m.x - x;
        const dz = m.z - z;
        if (dx*dx + dz*dz < RADIUS*RADIUS) {
            return true;
        }
    }
    return false;
}