// ================================================================
// TOAST
// ================================================================
let toastTimeout = null;
function showToast(msg, duration = 4000) {
    if (toastTimeout) clearTimeout(toastTimeout);
    toastMsg.textContent = msg;
    toastMsg.classList.add('show');
    toastTimeout = setTimeout(() => {
        toastMsg.classList.remove('show');
        toastTimeout = null;
    }, duration);
}

// ================================================================
// ОБРАБОТЧИКИ КНОПОК
// ================================================================
function getStep() {
    const val = parseInt(stepInput.value, 10);
    if (isNaN(val) || val < 1) return 200;
    state.step = val;
    stepDisplay.textContent = val;
    saveStepToStorage();
    return val;
}

function moveTarget(direction) {
    const heading = state.truck.heading;
    const step = getStep();
    let dx = 0, dz = 0;
    switch (direction) {
        case 'up':       dx = -Math.sin(heading); dz = -Math.cos(heading); break;
        case 'down':     dx =  Math.sin(heading); dz =  Math.cos(heading); break;
        case 'left':     dx = -Math.cos(heading); dz =  Math.sin(heading); break;
        case 'right':    dx =  Math.cos(heading); dz = -Math.sin(heading); break;
        case 'up-left':  dx = -Math.sin(heading) + Math.cos(heading); dz = -Math.cos(heading) - Math.sin(heading); break;
        case 'up-right': dx = -Math.sin(heading) - Math.cos(heading); dz = -Math.cos(heading) + Math.sin(heading); break;
        case 'down-left': dx =  Math.sin(heading) + Math.cos(heading); dz =  Math.cos(heading) - Math.sin(heading); break;
        case 'down-right':dx =  Math.sin(heading) - Math.cos(heading); dz =  Math.cos(heading) + Math.sin(heading); break;
        default: return;
    }
    const len = Math.sqrt(dx*dx + dz*dz);
    if (len > 0.01) { dx /= len; dz /= len; }
    const curX = parseFloat(targetX.value) || 0;
    const curZ = parseFloat(targetZ.value) || 0;
    targetX.value = (curX + dx * step).toFixed(2);
    targetZ.value = (curZ + dz * step).toFixed(2);
    updateAll();
}

// Назначение обработчиков (вызывается в init)
function setupUI() {
    document.querySelectorAll('.nav-btn[data-dir]').forEach(btn => {
        btn.addEventListener('click', function() {
            const dir = this.dataset.dir;
            if (dir === 'reset') {
                targetX.value = '0.00';
                targetY.value = '0.00';
                targetZ.value = '0.00';
                updateAll();
                return;
            }
            moveTarget(dir);
        });
    });

    document.getElementById('resetTargetBtn').addEventListener('click', function() {
        targetX.value = '0.00';
        targetY.value = '0.00';
        targetZ.value = '0.00';
        updateAll();
    });

    document.getElementById('targetToTruckBtn').addEventListener('click', function() {
        const tx = parseFloat(truckX.value) || 0;
        const ty = parseFloat(truckY.value) || 0;
        const tz = parseFloat(truckZ.value) || 0;
        targetX.value = tx.toFixed(2);
        targetY.value = ty.toFixed(2);
        targetZ.value = tz.toFixed(2);
        updateAll();
    });

    document.getElementById('targetToCityBtn').addEventListener('click', function() {
        if (state.nearbyCities.length === 0) return;
        const city = state.nearbyCities[0];
        targetX.value = city.x.toFixed(2);
        targetY.value = '0.00';
        targetZ.value = city.z.toFixed(2);
        updateAll();
    });

    document.getElementById('targetToPOIBtn').addEventListener('click', function() {
        if (state.pois.length === 0) return;
        const tX = parseFloat(truckX.value) || 0;
        const tZ = parseFloat(truckZ.value) || 0;
        let nearest = null;
        let nearDist = Infinity;
        for (const poi of state.pois) {
            const dx = poi.x - tX;
            const dz = poi.z - tZ;
            const d = dx*dx + dz*dz;
            if (d < nearDist) {
                nearDist = d;
                nearest = poi;
            }
        }
        if (nearest) {
            targetX.value = nearest.x.toFixed(2);
            targetY.value = '0.00';
            targetZ.value = nearest.z.toFixed(2);
            updateAll();
        }
    });

    document.getElementById('zoomInBtn').addEventListener('click', () => {
        state.manualScaleFactor /= 1.2;
        if (state.manualScaleFactor < 0.001) state.manualScaleFactor = 0.001;
        if (zoomLabel) zoomLabel.textContent = `×${state.manualScaleFactor.toFixed(2)}`;
        updateAll();
    });
    document.getElementById('zoomOutBtn').addEventListener('click', () => {
        state.manualScaleFactor *= 1.2;
        if (zoomLabel) zoomLabel.textContent = `×${state.manualScaleFactor.toFixed(2)}`;
        updateAll();
    });
    document.getElementById('zoomResetBtn').addEventListener('click', () => {
        state.manualScaleFactor = 1;
        if (zoomLabel) zoomLabel.textContent = `×1.0`;
        updateAll();
    });

    [targetX, targetY, targetZ].forEach(input => {
        input.addEventListener('input', updateAll);
    });

    stepInput.addEventListener('change', function() {
        const val = parseInt(this.value, 10);
        if (isNaN(val) || val < 1) this.value = 200;
        state.step = parseInt(this.value, 10);
        stepDisplay.textContent = state.step;
        saveStepToStorage();
    });

    document.getElementById('copyTargetBtn').addEventListener('click', function() {
        const x = targetX.value.trim() || '0';
        const y = targetY.value.trim() || '0';
        const z = targetZ.value.trim() || '0';
        const coordStr = `${x}, ${y}, ${z}`;
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(coordStr).then(() => {
                if (telemetryStatus) telemetryStatus.textContent = '✅ скопировано';
                setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 1500);
            }).catch(() => {});
        } else {
            const ta = document.createElement('textarea');
            ta.value = coordStr;
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); } catch(e) {}
            document.body.removeChild(ta);
            if (telemetryStatus) telemetryStatus.textContent = '✅ скопировано';
            setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 1500);
        }
    });

    document.getElementById('pasteTargetBtn').addEventListener('click', function() {
        const paste = (text) => {
            const parts = text.split(',').map(s => s.trim());
            if (parts.length >= 3) {
                const x = parseFloat(parts[0]);
                const y = parseFloat(parts[1]);
                const z = parseFloat(parts[2]);
                if (!isNaN(x) && !isNaN(y) && !isNaN(z)) {
                    targetX.value = x.toFixed(2);
                    targetY.value = y.toFixed(2);
                    targetZ.value = z.toFixed(2);
                    updateAll();
                    if (telemetryStatus) telemetryStatus.textContent = '✅ вставлено';
                    setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 1500);
                    return true;
                }
            }
            return false;
        };
        if (navigator.clipboard && navigator.clipboard.readText) {
            navigator.clipboard.readText().then(text => {
                if (!paste(text)) {
                    if (telemetryStatus) telemetryStatus.textContent = '❌ неверный формат';
                    setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 2000);
                }
            }).catch(() => {
                const ta = document.createElement('textarea');
                document.body.appendChild(ta);
                ta.focus();
                document.execCommand('paste');
                const text = ta.value;
                document.body.removeChild(ta);
                if (!paste(text)) {
                    if (telemetryStatus) telemetryStatus.textContent = '❌ неверный формат';
                    setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 2000);
                }
            });
        } else {
            const ta = document.createElement('textarea');
            document.body.appendChild(ta);
            ta.focus();
            document.execCommand('paste');
            const text = ta.value;
            document.body.removeChild(ta);
            if (!paste(text)) {
                if (telemetryStatus) telemetryStatus.textContent = '❌ неверный формат';
                setTimeout(() => { if (telemetryStatus) telemetryStatus.textContent = '✅ данные получены'; }, 2000);
            }
        }
    });
}