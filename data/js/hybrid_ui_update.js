// ================================================================
// ОБНОВЛЕНИЕ UI
// ================================================================
// v75: хелпер литров (fallback — если liters нет, 0).
function fuelLit(st) { return Number(st.fuelLiters) || 0; }

function updateUI() {
    const speed = hybridState.lastSpeed || 0;
    const engineOn = (hybridState.engine === 'ON');
    const hasJob = hybridState.hasJob || false;
    const arduino = hybridState.arduino || false;

    // Показываем/скрываем range bar
    dom.rangeBarWrap.style.display = arduino ? 'block' : 'none';

    // Speed
    let modeText = getSpeedRange(speed);
    dom.speedValue.textContent = Number.isFinite(speed) ? Math.round(speed) : '--';
    const sensitivityText = (hybridState.lang.steering_sensitivity || 'Arduino steering sensitivity') + ': MODE ' + modeText + ' km/h';
    dom.rangeLabel.textContent = sensitivityText;

    // Speed highlighting
    const isAccelerating = speed > hybridState.lastSpeedPrev;
    const isBraking = speed < hybridState.lastSpeedPrev;
    const inGreenZone = (speed >= 50 && speed <= 70) || (speed >= 80 && speed <= 100);
    const inOrangeZone = (speed >= 100 && speed <= 110);

    if (inGreenZone || inOrangeZone) {
        if (hybridState.speedZoneState === null) {
            hybridState.speedZoneState = isAccelerating ? 'accelerating' : 'decelerating';
        }
    } else {
        hybridState.speedZoneState = null;
    }

    dom.speedCard.classList.remove('warning', 'safe');
    if (inOrangeZone && hybridState.speedZoneState === 'accelerating') {
        dom.speedCard.classList.add('warning');
    } else if (inGreenZone && hybridState.speedZoneState === 'decelerating') {
        dom.speedCard.classList.add('safe');
    }

    // Engine icon
    dom.engineIcon.className = 'speed-icon engine-icon ' + (engineOn ? 'engine-active' : 'engine-inactive');

    // Parking icon
    const parkingOn = (hybridState.parking === 'ON');
    dom.parkingIcon.className = 'speed-icon parking-icon ' + (parkingOn ? 'parking-active' : 'parking-inactive');

    // Trailer & Lights
    dom.trailerIndicator.className = 'indicator trailer' + (hybridState.trailerAttached ? ' active' : '');
    const lights = hybridState.lights || 'off';
    if (lights === 'high') {
        dom.lightsIndicator.className = 'indicator lights highbeam';
    } else if (lights === 'low' || lights === 'parking') {
        dom.lightsIndicator.className = 'indicator lights active';
    } else {
        dom.lightsIndicator.className = 'indicator lights';
    }

    if (dom.brakeIndicator) {
        const brakeOn = (Number(hybridState.brake) > 0.02) || hybridState.brakeLight === true;
        dom.brakeIndicator.className = 'indicator brake' + (brakeOn ? ' active' : '');
    }

    // Fuel (v75, вар. «а»: литры + % — см. диагностику v74: TruckTel шлёт fuel
    // только при изменении, круглый % при баке 1465 л замирает на 10-20 с.
    // Показ ЛИТРОВ рядом с % даёт живое значение (fuel.amount меняется непрерывно).
    const fuel = Math.round(Number(hybridState.fuelPercent) || 0);
    const lit = fuelLit(hybridState);
    if (fuel <= 0 && lit <= 0) {
        dom.fuelValue.textContent = '0';
        dom.fuelProgress.style.width = '0%';
        dom.fuelProgress.style.backgroundColor = 'transparent';
        dom.fuelCard.classList.remove('warning', 'critical');
    } else {
        dom.fuelValue.textContent = (lit ? (lit.toFixed(0) + ' л') : String(fuel));
        dom.fuelProgress.style.width = fuel + '%';
        dom.fuelProgress.style.backgroundColor = getProgressColor(fuel);
        dom.fuelCard.classList.remove('warning', 'critical');
        if (fuel < 10) dom.fuelCard.classList.add('critical');
        else if (fuel < 15) dom.fuelCard.classList.add('warning');
    }

    // Rest
    const restHours = hybridState.restHours || 0;
    if (restHours <= 0) {
        dom.restValue.textContent = '0:00';
        dom.restProgress.style.width = '0%';
        dom.restProgress.style.backgroundColor = 'transparent';
        dom.restCard.classList.remove('warning', 'critical');
    } else {
        dom.restValue.textContent = formatHours(restHours);
        const restPercent = Math.min(100, (restHours / 11) * 100);
        dom.restProgress.style.width = restPercent + '%';
        dom.restProgress.style.backgroundColor = getProgressColor(restPercent);
        dom.restCard.classList.remove('warning', 'critical');
        if (restHours < 1) dom.restCard.classList.add('critical');
        else if (restHours < 2) dom.restCard.classList.add('warning');
    }

    // Job deadline
    const jobRemaining = hybridState.jobRemaining || 0;
    const estimatedHours = hybridState.estimatedHours || 0;
    if (hasJob && jobRemaining > 0) {
        dom.jobCard.style.display = 'flex';
        const initialJob = hybridState._initialJobRemaining || jobRemaining;
        const jobPercent = Math.min(100, (jobRemaining / initialJob) * 100);
        dom.jobValue.textContent = formatHours(jobRemaining);
        dom.jobProgress.style.width = jobPercent + '%';
        dom.jobProgress.style.backgroundColor = getProgressColor(jobPercent);
        dom.jobCard.classList.remove('warning', 'critical', 'blink');

        let bgColor = '';
        let blink = false;
        if (estimatedHours > 0 && jobRemaining > 0) {
            const buffer = (jobRemaining - estimatedHours) / jobRemaining;
            if (buffer < 0.05) { bgColor = '#c62828'; blink = true; }
            else if (buffer < 0.15) { bgColor = '#c62828'; }
            else if (buffer < 0.20) { bgColor = '#e65100'; }
            else if (buffer < 0.30) { bgColor = '#1565c0'; }
            else { bgColor = '#2e7d32'; }
        } else { bgColor = '#2e7d32'; }
        if (bgColor) {
            dom.jobCard.style.backgroundColor = bgColor;
            dom.jobProgress.style.backgroundColor = bgColor;
        }
        if (blink) dom.jobCard.classList.add('blink');
    } else {
        dom.jobCard.style.display = 'none';
    }

    // Sensitivity bar
    if (engineOn && arduino) {
        const progress = getSensitivityProgress(speed);
        dom.rangeFill.style.width = Math.min(100, Math.max(0, progress)) + '%';
        dom.rangeFill.style.backgroundColor = getRangeColor(progress);
    } else {
        dom.rangeFill.style.width = '0%';
        dom.rangeFill.style.backgroundColor = 'transparent';
    }

    // Distance progress
    const initialDist = hybridState._initialDistance || 0;
    const estimatedDist = hybridState.estimatedDistance || 0;
    if (engineOn && hasJob) {
        dom.distanceCard.style.display = 'flex';
        let progressDist = 0;
        let timeStr = '--';
        if (initialDist > 0 && estimatedDist >= 0) {
            progressDist = (1 - (estimatedDist / initialDist)) * 100;
            progressDist = Math.min(100, Math.max(0, progressDist));
            timeStr = (estimatedHours > 0) ? formatHours(estimatedHours) : '--';
        }
        dom.distanceProgress.style.width = progressDist + '%';
        dom.distanceTime.textContent = timeStr;

        let statusText = '', statusClass = '';
        if (estimatedHours > 0 && jobRemaining > 0) {
            const ratio = estimatedHours / jobRemaining;
            if (ratio >= 1.0) {
                statusText = hybridState.lang.late || '⚠️ You\'re late!';
                statusClass = 'late';
            } else if (ratio >= 0.85) {
                statusText = hybridState.lang.hurry || '⚡ Need to hurry';
                statusClass = 'hurry';
            } else {
                statusText = hybridState.lang.ontime || '✅ On time';
                statusClass = 'ontime';
            }
        } else {
            statusText = hybridState.lang.ontime || '✅ On time';
            statusClass = 'ontime';
        }
        dom.distanceStatus.textContent = statusText;
        dom.distanceStatus.className = 'distance-status ' + statusClass;
    } else {
        dom.distanceCard.style.display = 'none';
    }

    hybridState.lastSpeedPrev = speed;
}

// ================================================================
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
// ================================================================
function formatHours(h) {
    if (!h || h <= 0) return '--';
    const totalMin = Math.round(h * 60);
    const hrs = Math.floor(totalMin / 60);
    const min = totalMin % 60;
    return hrs + ':' + min.toString().padStart(2, '0');
}

function getProgressColor(percent) {
    if (percent >= 40) return '#2e7d32';
    else if (percent >= 30) return '#1565c0';
    else {
        const p = percent / 30;
        const r = 255;
        const g = Math.round(80 * p);
        const b = 0;
        return `rgb(${r}, ${g}, ${b})`;
    }
}

function getRangeColor(percent) {
    const p = percent / 100;
    const r = Math.round(46 * (1 - p) + 2 * p);
    const g = Math.round(125 * (1 - p) + 119 * p);
    const b = Math.round(50 * (1 - p) + 189 * p);
    return `rgb(${r}, ${g}, ${b})`;
}

function getSensitivityProgress(speed) {
    let min, max;
    if (speed < 1) { min = 282; max = 742; }
    else if (speed < 30) { min = 256; max = 768; }
    else if (speed < 60) { min = 179; max = 844; }
    else if (speed < 80) { min = 128; max = 896; }
    else if (speed < 100) { min = 77; max = 947; }
    else if (speed < 140) { min = 38; max = 986; }
    else { min = 0; max = 1023; }
    const width = max - min;
    const maxWidth = 1023;
    const minWidth = 460;
    return ((maxWidth - width) / (maxWidth - minWidth)) * 100;
}

function getSpeedRange(speed) {
    if (speed < 1) return '0-1';
    else if (speed < 30) return '0-30';
    else if (speed < 60) return '30-60';
    else if (speed < 80) return '60-80';
    else if (speed < 100) return '80-100';
    else if (speed < 140) return '100-140';
    else return '140+';
}