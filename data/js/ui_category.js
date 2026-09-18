/* ETS2 Assist — взаимоисключающие категории веб-оверлеев.
 *
 * Существует ровно две категории интерфейса, одновременно на экране может
 * присутствовать только одна:
 *
 *   interactive — интерфейсы паузы (активный интерактив, мини-лого);
 *   game        — интерфейсы игровой камеры (гибрид, миникарта, уведомления, AR).
 *
 * Приложение (порт 8084) является единственным владельцем решения и рассылает
 * команду set_overlay_category. Страница лишь применяет результат, помечая
 * узлы атрибутом data-category="game" / data-category="interactive".
 *
 * Отладочный форс (debugShow) имеет приоритет над категорией.
 */
(function () {
    'use strict';

    var STYLE_ID = 'ets2CategoryStyle';

    function ensureStyle() {
        if (document.getElementById(STYLE_ID)) return;
        var st = document.createElement('style');
        st.id = STYLE_ID;
        st.textContent =
            'html:not([data-debug-show="1"]).ets2-cat-game [data-category~="interactive"],' +
            'html:not([data-debug-show="1"]).ets2-cat-interactive [data-category~="game"]' +
            '{visibility:hidden!important;opacity:0!important;pointer-events:none!important}';
        (document.head || document.documentElement).appendChild(st);
    }

    var category = null;

    function apply(next) {
        next = next === 'interactive' ? 'interactive' : 'game';
        if (category === next) return;
        category = next;
        var root = document.documentElement;
        root.classList.toggle('ets2-cat-interactive', next === 'interactive');
        root.classList.toggle('ets2-cat-game', next === 'game');
        try {
            if (typeof window.onEts2Category === 'function') window.onEts2Category(next);
        } catch (_) { }
    }

    window.ets2Category = function () { return category; };
    window.ets2ApplyCategory = apply;

    ensureStyle();
    apply('game');

    function connect() {
        try {
            var ws = new WebSocket('ws://localhost:8084/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (!d || !d.command) return;
                    if (d.command === 'set_overlay_category') apply(d.category);
                    if (typeof window.onEts2Command === 'function') window.onEts2Command(d);
                } catch (_) { }
            };
            ws.onclose = function () { setTimeout(connect, 1000); };
            ws.onerror = function () { try { ws.close(); } catch (_) { } };
        } catch (_) {
            setTimeout(connect, 1000);
        }
    }

    connect();
})();
