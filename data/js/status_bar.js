// ================================================================
// СТАТУСНАЯ СТРОКА МИНИКАРТЫ (дизайн едина с редактором карты)
// Низкая тёмно-серая полоса над нижним краем миникарты, две части:
//  ЛЕВАЯ — индикатор состояний: окружность (светло-серый фон, 2px тёмно-серая
//    обводка, lime при активности) + текст «данные транспорта» (или «нет …»).
//  ПРАВАЯ — операции: вращающийся индикатор + текст; по завершении — тёмно-зелёная
//    галочка, текст предыдущей операции сохраняется до следующей.
// ================================================================

(function () {
    const STATUS_H = 14;          // высота полосы (низкая)
    const IDLE = '#464e58';       // тускло-серая окружность (нет данных)
    const BORDER = '#737a84';     // тёмно-серая обводка 2px
    const LIME = '#beff5a';       // lime
    const CHECK = '#1e8c3c';      // тёмно-зелёная галочка
    const BG = '#262a30';         // фон строки
    const FG = '#c6cdd6';         // приглушённо белый

    let barRoot = null;
    let stateEl = null;
    let opEl = null;
    let opState = { busy: false, text: '' };
    let telemetryActive = false;
    let busyAnim = 0;
    let animTimer = null;

    function ensureDom() {
        if (barRoot) return true;
        const container = document.querySelector('.minimap-container') || document.body;
        if (!container) return false;
        barRoot = document.createElement('div');
        barRoot.id = 'mapStatusBar';
        barRoot.style.cssText = [
            'position:absolute', 'left:0', 'right:0', 'bottom:22px',
            'height:' + STATUS_H + 'px',
            'background:' + BG,
            'z-index:85',
            'display:flex', 'align-items:center',
            'pointer-events:none',
            'font:9px/1 "Segoe UI",sans-serif',
            'border-top:1px solid rgba(0,0,0,.55)'
        ].join(';');

        stateEl = document.createElement('div');
        stateEl.style.cssText = 'position:absolute;left:4px;top:0;bottom:0;display:flex;align-items:center;gap:4px;';

        opEl = document.createElement('div');
        opEl.style.cssText = 'position:absolute;right:4px;top:0;bottom:0;display:flex;align-items:center;gap:4px;';

        barRoot.appendChild(stateEl);
        barRoot.appendChild(opEl);
        container.appendChild(barRoot);
        return true;
    }

    // Текст с чёрной обводкой через text-shadow (4 направления).
    function outlinedSpan(text) {
        const s = document.createElement('span');
        s.textContent = text;
        s.style.cssText = 'color:' + FG + ';text-shadow:-1px 0 0 #000,1px 0 0 #000,0 -1px 0 #000,0 1px 0 #000;white-space:nowrap;';
        return s;
    }

    // Окружность-индикатор состояния (левой части).
    function stateCircle(active) {
        const d = document.createElement('span');
        d.style.cssText = [
            'width:7px', 'height:7px', 'border-radius:50%',
            'display:inline-block', 'flex:0 0 auto',
            'background:' + (active ? LIME : IDLE),
            'border:2px solid ' + BORDER,
            'box-sizing:content-box'
        ].join(';');
        return d;
    }

    function renderState() {
        if (!stateEl) return;
        stateEl.innerHTML = '';
        stateEl.appendChild(stateCircle(telemetryActive()));
        stateEl.appendChild(outlinedSpan(telemetryActive() ? 'данные транспорта' : 'нет данных транспорта'));
    }

    function telemetryActive() {
        return !!(state && state.wsDataReceived);
    }

    // Вращающийся индикатор операций (CSS-анимация).
    function spinnerEl(animate) {
        const d = document.createElement('span');
        d.style.cssText = [
            'width:9px', 'height:9px', 'border-radius:50%', 'display:inline-block',
            'flex:0 0 auto',
            'border:2px solid transparent',
            'border-top-color:' + (animate ? LIME : CHECK),
            'border-right-color:' + (animate ? LIME : CHECK),
            animate ? 'animation:movSpin 0.9s linear infinite' : ''
        ].join(';');
        return d;
    }

    // Статичный стиль для «галочки»: вращённый бордер не нужен — рисуем псевдогалочку
    // символом ✔ (тёмно-зелёным) в окружном стиле, чтобы не городить SVG.
    function checkEl() {
        const d = document.createElement('span');
        d.textContent = '✔';
        d.style.cssText = 'color:' + CHECK + ';font-size:9px;display:inline-block;flex:0 0 auto;text-shadow:0 0 2px rgba(0,0,0,.9);';
        return d;
    }

    function renderOp() {
        if (!opEl) return;
        opEl.innerHTML = '';
        if (opState.busy) {
            opEl.appendChild(spinnerEl(true));
            opEl.appendChild(outlinedSpan(opState.text || '…'));
        } else {
            opEl.appendChild(checkEl());
            if (opState.text) opEl.appendChild(outlinedSpan(opState.text));
        }
    }

    // Публичный API: обновление состояния телеметрии (вызывает websocket.js).
    window.mapStatusSetTelemetry = function (active) {
        telemetryActive = function () { return !!active; };
        renderState();
    };

    // Публичный API: операция (загрузка точек, обновление overrides и т.п.)
    window.mapStatusSetOperation = function (text, busy) {
        opState = { busy: !!busy, text: text || opState.text };
        renderOp();
        if (busy) {
            clearTimeout(window.__mapStatusResetTimer);
            // защитный сброс: операция не подтверждена за 20с -> снять busy
            window.__mapStatusResetTimer = setTimeout(() => { opState.busy = false; renderOp(); }, 20000);
        }
    };

    // Состояние телеметрии обновляем раз в секунду (по state.wsDataReceived).
    setInterval(() => {
        if (!ensureDom()) return;
        renderState();
    }, 1000);

    // Инициализация по DOMContentLoaded.
    function initOnReady() {
        if (!ensureDom()) return;
        renderState();
        mapStatusSetOperation('готово', false);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initOnReady);
    else initOnReady();

    // Стили вращения.
    const css = document.createElement('style');
    css.textContent = '@keyframes movSpin{to{transform:rotate(360deg)}}';
    document.head.appendChild(css);
})();