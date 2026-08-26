// ================================================================
// АЛГОРИТМ ЖАДНОЙ РАССТАНОВКИ МЕТОК
// ================================================================
function greedyPlacement(labelsData, containerW, containerH) {
    if (!labelsData || labelsData.length === 0) return [];
    const sorted = [...labelsData].sort((a, b) => {
        if (a.priority !== b.priority) return b.priority - a.priority;
        return (b.w * b.h) - (a.w * a.h);
    });
    const placed = [];
    const PADDING = LABEL_PADDING;
    const MAX_ATTEMPTS = 150;
    for (const label of sorted) {
        let candidate = { ...label };
        let attempts = 0;
        let collides = true;
        while (collides && attempts < MAX_ATTEMPTS) {
            collides = false;
            for (const p of placed) {
                const aHalfW = candidate.w / 2 + PADDING;
                const aHalfH = candidate.h / 2 + PADDING;
                const bHalfW = p.w / 2 + PADDING;
                const bHalfH = p.h / 2 + PADDING;
                const aLeft = candidate.x - aHalfW;
                const aRight = candidate.x + aHalfW;
                const aTop = candidate.y - aHalfH;
                const aBottom = candidate.y + aHalfH;
                const bLeft = p.x - bHalfW;
                const bRight = p.x + bHalfW;
                const bTop = p.y - bHalfH;
                const bBottom = p.y + bHalfH;
                if (!(aRight < bLeft || aLeft > bRight || aBottom < bTop || aTop > bBottom)) {
                    collides = true;
                    break;
                }
            }
            if (collides) {
                const step = 2 + attempts * 1.5;
                if (attempts % 4 === 0) {
                    candidate.y += step;
                } else if (attempts % 4 === 1) {
                    candidate.y -= step;
                } else if (attempts % 4 === 2) {
                    candidate.x += step;
                } else {
                    candidate.x -= step;
                }
                const halfW = candidate.w / 2 + PADDING;
                const halfH = candidate.h / 2 + PADDING;
                if (candidate.x - halfW < 5) candidate.x = 5 + halfW;
                if (candidate.x + halfW > containerW - 5) candidate.x = containerW - 5 - halfW;
                if (candidate.y - halfH < 5) candidate.y = 5 + halfH;
                if (candidate.y + halfH > containerH - 5) candidate.y = containerH - 5 - halfH;
                attempts++;
            }
        }
        placed.push(candidate);
    }
    return placed;
}

// ================================================================
// ФУНКЦИЯ СОЗДАНИЯ HTML-ЭЛЕМЕНТА ДЛЯ МЕТКИ
// ================================================================
function createLabelElement(text, x, y, color, isActive = false, isCity = false) {
    const el = document.createElement('div');
    el.className = 'label-item';
    if (isActive) el.classList.add('target-active');
    else if (!isCity) el.classList.add('target-inactive');
    if (isCity) el.classList.add('city-label');
    el.textContent = text;
    el.style.left = x + 'px';
    el.style.top = y + 'px';
    el.style.color = color;
    if (SHOW_BBOX) {
        el.style.border = '1px solid red';
        el.style.background = 'rgba(255,0,0,0.1)';
        el.style.padding = '0 4px';
        el.style.transform = 'translate(-50%, -50%)';
    }
    return el;
}