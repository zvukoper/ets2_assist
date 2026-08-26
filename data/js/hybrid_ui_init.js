// ================================================================
// ЗАГРУЗКА ЯЗЫКА И ИНИЦИАЛИЗАЦИЯ
// ================================================================
async function loadLanguage() {
    try {
        const res = await fetch('lang.json?' + Date.now());
        if (!res.ok) throw new Error('not found');
        hybridState.lang = await res.json();
        applyTranslations();
    } catch (e) {
        hybridState.lang = {
            fuel: 'Fuel', rest: 'Rest in', job: 'Job deadline',
            destination_label: 'DESTINATION',
            steering_sensitivity: 'Arduino steering sensitivity',
            ontime: '✅ On time', hurry: '⚡ Need to hurry', late: '⚠️ You\'re late!',
            speed: 'Speed'
        };
        applyTranslations();
    }
}

function applyTranslations() {
    document.querySelectorAll('[data-i18n]').forEach(el => {
        const key = el.getAttribute('data-i18n');
        if (hybridState.lang[key] !== undefined) el.textContent = hybridState.lang[key];
    });
    if (dom.distanceLabel && hybridState.lang.destination_label) {
        dom.distanceLabel.textContent = hybridState.lang.destination_label;
    }
}

function initHybridUI() {
    loadLanguage().then(() => {
        fetchHttpData();
        hydrateHybridSnapshot();
        setInterval(fetchHttpData, HTTP_UPDATE_INTERVAL);
        setInterval(hydrateHybridSnapshot, 1000);
        setTimeout(() => connectWebSocket(), 2000);
        setTimeout(() => connectCommandWebSocket(), 2500);
    });
}

// Запуск
initHybridUI();