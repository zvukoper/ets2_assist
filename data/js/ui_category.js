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
 * Отладочный форс (debugShow) ОТМЕНЯЕТ ТОЛЬКО СКРЫТИЕ ПО ФОКУСУ
 * (класс ets2-hide-all). Логика ПАУЗЫ / БЕЗ ПАУЗЫ — то есть выбор
 * взаимоисключающей категории — продолжает работать КАК ОБЫЧНО: иначе
 * отладка перестала бы показывать реальное поведение страниц.
 */
(function () {
    'use strict';

    var STYLE_ID = 'ets2CategoryStyle';

    function ensureStyle() {
        if (document.getElementById(STYLE_ID)) return;
        var st = document.createElement('style');
        st.id = STYLE_ID;
        st.textContent =
            // Взаимоисключение категорий (ПАУЗА -> interactive, игра -> game)
            // работает ВСЕГДА, в том числе под debugShow.
            'html.ets2-cat-game [data-category~="interactive"],' +
            'html.ets2-cat-interactive [data-category~="game"],' +
            // Скрытие при фокусе вне игры — ЕДИНСТВЕННОЕ, что отменяет
            // debugShow (это и есть «не исчезают без фокуса»).
            'html:not([data-debug-show="1"]).ets2-hide-all [data-category]' +
            '{visibility:hidden!important;opacity:0!important;pointer-events:none!important}';
        (document.head || document.documentElement).appendChild(st);
    }

    /* Полное скрытие: фокус ушёл на стороннее окно — наложение запрещено.
     * Дополнительно к CSS отключаем pointer-events на корне, чтобы по
     * невидимым элементам нельзя было случайно кликнуть.
     * Под debugShow и класс, и отключение кликов ИГНОРИРУЮТСЯ. */
    function applyHideAll(hidden) {
        var root = document.documentElement;
        if (window.__debugShow === true) hidden = false;
        root.classList.toggle('ets2-hide-all', !!hidden);
        root.style.pointerEvents = hidden ? 'none' : '';
    }

    var category = null;
    var hideAllWanted = false;

    /* Единая точка применения обоих правил. Вызывается и по WS-командам,
     * и повторно при переключении debugShow — CSS-правило меняется по атрибуту
     * на <html>, но класс ets2-hide-all мог быть выставлен ДО включения
     * debugShow, поэтому его надо пересмотреть (снять). */
    function reapplyVisibility() {
        applyHideAll(hideAllWanted);
    }
    window.ets2ReapplyVisibility = reapplyVisibility;

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
                    if (d.command === 'set_overlay_hidden') { hideAllWanted = d.hidden === true; reapplyVisibility(); }
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
