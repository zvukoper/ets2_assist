// ================================================================
// HTTP-ЗАПРОСЫ (web_data.json)
// ================================================================
async function fetchHttpData() {
    try {
        const res = await fetch('web_data.json?' + Date.now());
        if (!res.ok) throw new Error('HTTP error');
        const data = await res.json();

        // Обновляем состояние
        if (data.fuel !== undefined && !hybridState.wsConnected) hybridState.fuelPercent = Number(data.fuel) || 0;
        if (data.restHours !== undefined && !hybridState._restFromTruckTel) hybridState.restHours = Number(data.restHours) || 0;
        if (data.jobRemaining !== undefined) hybridState.jobRemaining = Number(data.jobRemaining) || 0;
        if (data.hasJob !== undefined) hybridState.hasJob = data.hasJob === true;
        if (data.jobInitial !== undefined) hybridState.jobInitial = Number(data.jobInitial) || 0;
        if (data.estimatedTime) {
            hybridState.estimatedHours = parseEstimatedTime(data.estimatedTime);
        } else if (data.estimatedHours !== undefined) {
            hybridState.estimatedHours = data.estimatedHours;
        }
        hybridState.estimatedDistance = data.estimatedDistance ?? 0;
        hybridState.initialDistance = data.initialDistance ?? 0;
        if (data.parking !== undefined && !hybridState._parkingFromTruckTel) hybridState.parking = data.parking ? 'ON' : 'OFF';
        if (data.engine !== undefined && !hybridState.wsConnected) hybridState.engine = data.engine ? 'ON' : 'OFF';
        hybridState.rangeMin = data.rangeMin ?? 400;
        hybridState.rangeMax = data.rangeMax ?? 623;
        if (data.trailerAttached !== undefined && !hybridState._trailerFromTruckTel) hybridState.trailerAttached = data.trailerAttached === true;
        if (data.lights !== undefined && !hybridState.wsConnected) hybridState.lights = data.lights || 'off';
        hybridState.arduino = data.arduino === true;

        // Сохраняем начальные значения для задания
        if (hybridState.hasJob) {
            if (hybridState._initialDistance === null && hybridState.estimatedDistance > 0) {
                hybridState._initialDistance = hybridState.estimatedDistance;
            }
            if (hybridState._initialJobRemaining === null && hybridState.jobRemaining > 0) {
                hybridState._initialJobRemaining = hybridState.jobRemaining;
            }
        } else {
            hybridState._initialDistance = null;
            hybridState._initialJobRemaining = null;
        }

        // Динамический порт WebSocket
        const newPort = data.wsPort || DEFAULT_WS_PORT;
        if (newPort !== hybridState.wsPort) {
            hybridState.wsPort = newPort;
            hybridState.wsUrl = `ws://localhost:${newPort}/api/ws/delta/flat/?throttle=50`;
            if (hybridState.socket && hybridState.socket.readyState === WebSocket.OPEN) {
                hybridState.socket.close();
            }
        }

        // Если WebSocket не подключен, используем скорость из HTTP
        if (!hybridState.wsConnected) {
            const speedFromHttp = data.speed ?? 0;
            if (speedFromHttp > 0) hybridState.lastSpeed = speedFromHttp;
        }

        // Обновляем UI, если он уже показан
        if (hybridState.dataShown) {
            updateUI();
        }
    } catch (e) {
        // игнорируем ошибки
    }
}

function parseEstimatedTime(estimatedTimeStr) {
    if (!estimatedTimeStr) return 0;
    const regex = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z$/;
    const match = estimatedTimeStr.match(regex);
    if (!match) return 0;
    const day = parseInt(match[3]);
    const hours = parseInt(match[4]);
    const minutes = parseInt(match[5]);
    const seconds = parseInt(match[6]);
    const totalHours = (day - 1) * 24 + hours + minutes / 60 + seconds / 3600;
    return Math.round(totalHours * 100) / 100;
}