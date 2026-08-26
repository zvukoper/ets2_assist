// ================================================================
// УПРАВЛЕНИЕ КАРТОЙ (события)
// ================================================================
// zoom
minimapCanvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    scale *= delta;
    if (scale < 0.001) scale = 0.001;
    if (scale > 1000) scale = 1000;
    drawMinimap();
}, { passive: false });

// drag
minimapCanvas.addEventListener('mousedown', (e) => {
    if (e.button === 0) {
        isDragging = true;
        dragStartX = e.clientX;
        dragStartY = e.clientY;
        dragStartCX = centerX;
        dragStartCZ = centerZ;
        minimapCanvas.style.cursor = 'grabbing';
    }
});

window.addEventListener('mousemove', (e) => {
    if (isDragging) {
        const dx = (e.clientX - dragStartX) / scale;
        const dy = (dragStartY - e.clientY) / scale;
        centerX = dragStartCX - dx;
        centerZ = dragStartCZ - dy;
        targetCenterX = centerX;
        targetCenterZ = centerZ;
        drawMinimap();
    }
    // Координаты под курсором
    const rect = minimapCanvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx >= 0 && mx <= W && my >= 0 && my <= H) {
        const wx = centerX + (mx - W/2) / scale;
        const wz = centerZ - (my - H/2) / scale;
        document.getElementById('cursorCoords').textContent = `📍 ${wx.toFixed(1)}, ${wz.toFixed(1)}`;
    }
});

window.addEventListener('mouseup', () => {
    if (isDragging) {
        isDragging = false;
        minimapCanvas.style.cursor = 'grab';
    }
});

// click – копирование координат
let notes = [];
minimapCanvas.addEventListener('click', (e) => {
    const rect = minimapCanvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx < 0 || mx > W || my < 0 || my > H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    const coordStr = `${wx.toFixed(2)}, ${wz.toFixed(2)}`;
    let objName = '';
    for (const c of state.cities) {
        const dx = c.x - wx;
        const dz = c.z - wz;
        if (dx*dx + dz*dz < 100) { objName = c.name; break; }
    }
    if (!objName) {
        for (const t of state.customTargets) {
            const dx = t.x - wx;
            const dz = t.z - wz;
            if (dx*dx + dz*dz < 100) { objName = t.name; break; }
        }
    }
    if (!objName) {
        for (const e of state.eventMarkers) {
            const dx = e.x - wx;
            const dz = e.z - wz;
            if (dx*dx + dz*dz < 100) { objName = e.label || ''; break; }
        }
    }
    const note = objName ? `${coordStr} – ${objName}` : coordStr;
    notes.push(note);
    navigator.clipboard?.writeText(coordStr);
    const toast = document.createElement('div');
    toast.style.cssText = 'position:fixed;bottom:170px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,0.8);color:#fff;padding:4px 12px;border-radius:4px;font-size:12px;z-index:999;pointer-events:none;';
    toast.textContent = `Скопировано: ${coordStr}`;
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 2000);
});

// dblclick – измерение расстояния
let measureMode = false;
let measureStart = null;
minimapCanvas.addEventListener('dblclick', (e) => {
    const rect = minimapCanvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx < 0 || mx > W || my < 0 || my > H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    if (!measureMode) {
        measureMode = true;
        measureStart = { x: wx, z: wz };
        document.getElementById('measureTool').classList.add('active');
        document.getElementById('measureDist').textContent = '0.0';
        return;
    }
    const dx = wx - measureStart.x;
    const dz = wz - measureStart.z;
    const dist = Math.sqrt(dx*dx + dz*dz);
    document.getElementById('measureDist').textContent = dist.toFixed(1);
    measureMode = false;
    measureStart = null;
    setTimeout(() => document.getElementById('measureTool').classList.remove('active'), 5000);
});