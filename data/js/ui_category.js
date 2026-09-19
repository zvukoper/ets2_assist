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
            // До ПЕРВОЙ явной команды приложения всё содержимое страницы
            // физически не участвует в layout: никаких стартовых вспышек.
            'html:not([data-ets2-ui-ready="1"]) body,' +
            'html:not([data-ets2-ui-ready="1"]) [data-category]' +
            '{display:none!important;visibility:hidden!important;opacity:0!important;pointer-events:none!important}' +
            // Взаимоисключение категорий.
            'html.ets2-cat-game [data-category~="interactive"],' +
            'html.ets2-cat-interactive [data-category~="game"]' +
            '{visibility:hidden!important;opacity:0!important;pointer-events:none!important}' +
            // Игровые слои могут использовать общий fade.
            // Интерактивные окна анимируют transform/opacity сами, поэтому
            // категория НЕ должна перекрывать их transition/opacity.
            'html[data-ets2-ui-ready="1"].ets2-cat-game [data-category~="game"]' +
            '{visibility:visible!important;opacity:1!important;pointer-events:auto!important;' +
            'transition:opacity 150ms ease-out!important}' +
            'html[data-ets2-ui-ready="1"].ets2-cat-interactive [data-category~="interactive"]' +
            '{visibility:visible!important;pointer-events:auto!important}' +
            'html[data-ets2-ui-ready="1"].ets2-ui-reveal-pending.ets2-cat-game [data-category~="game"],' +
            'html[data-ets2-ui-ready="1"].ets2-ui-reveal-pending.ets2-cat-interactive [data-category~="interactive"]' +
            '{opacity:0!important}' +
            // Скрытие при фокусе вне игры имеет последний приоритет:
            // никакой выбранный/отладочный слой не может его переопределить.
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
    var categoryCommandReceived = false;
    var hideAllWanted = false;
    var revealRaf = 0;
    var pendingEts2Commands = [];
    var pendingEts2CommandNames = {
        quest_pause_ui:true,
        set_quest_tab_state:true,
        set_quest_collapsed:true,
        quest_toggle_collapse:true,
        quest_toggle_inventory:true,
        quest_bookmark_beacon:true,
        inventory_bookmark_beacon:true,
        quest_collapse_interfaces:true,
        quest_state:true
    };

    function flushPendingEts2Commands() {
        if (typeof window.onEts2Command !== 'function') return;
        if (!pendingEts2Commands.length) return;
        var queue = pendingEts2Commands.slice(0);
        pendingEts2Commands.length = 0;
        queue.forEach(function (command) {
            try { window.onEts2Command(command); } catch (_) { }
        });
    }

    function revealSelectedCategory() {
        var root = document.documentElement;
        if (!category || !document.body) return;

        root.setAttribute('data-ets2-ui-ready', '1');
        root.classList.add('ets2-ui-reveal-pending');
        // Форсируем отдельный layout pass: выбранная категория уже имеет
        // display:auto/свои стили, но всё ещё opacity=0.
        void root.offsetWidth;

        if (revealRaf) cancelAnimationFrame(revealRaf);
        revealRaf = requestAnimationFrame(function () {
            root.classList.remove('ets2-ui-reveal-pending');
            revealRaf = 0;
        });
    }

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
        var changed = category !== next;
        category = next;
        categoryCommandReceived = true;

        var root = document.documentElement;
        root.classList.toggle('ets2-cat-interactive', next === 'interactive');
        root.classList.toggle('ets2-cat-game', next === 'game');

        try {
            if (typeof window.onEts2Category === 'function') window.onEts2Category(next);
        } catch (_) { }

        // Даже повторная команда той же категории должна уметь завершить
        // начальное состояние, если она пришла до DOMContentLoaded.
        if (changed || root.getAttribute('data-ets2-ui-ready') !== '1') {
            if (document.readyState === 'loading') return;
            revealSelectedCategory();
        }
    }

    window.ets2Category = function () { return category; };
    window.ets2ApplyCategory = apply;

    ensureStyle();

    // До явной команды приложения КАТЕГОРИЯ НЕ ВЫБРАНА.
    // Никакого автоматического game при загрузке: overlay остаётся display:none,
    // пока C# не присылает реальное состояние политики.
    document.addEventListener('DOMContentLoaded', function () {
        // Показываем только если ДО этого действительно пришла команда приложения.
        // Это закрывает гонку command-before-DOM, но не возвращает автопоказ.
        if (categoryCommandReceived) {
            try {
                if (typeof window.onEts2Category === 'function') window.onEts2Category(category);
            } catch (_) { }
            revealSelectedCategory();
        }
        /* quests_ui.js is loaded at the end of web_quests.html. Commands may
           have arrived through this earlier WebSocket before its handler existed;
           replay them in the original order once the DOM/page handler is ready. */
        /* Сначала должны завершиться DOMContentLoaded-инициализаторы
           страниц, в частности quests_ui.js. Иначе накопленный quest_pause_ui
           применяется, а следующий DOMContentLoaded-хендлер страницы сразу
           сбрасывает pagePaused/activeInterface и визуальное состояние. */
        setTimeout(flushPendingEts2Commands, 0);
    });

    function connect() {
        try {
            var ws = new WebSocket('ws://localhost:8084/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (!d || !d.command) return;
                    if (d.command === 'set_overlay_category') apply(d.category);
                    if (d.command === 'set_overlay_hidden') { hideAllWanted = d.hidden === true; reapplyVisibility(); }
                    if (typeof window.onEts2Command === 'function') {
                        window.onEts2Command(d);
                    } else if (pendingEts2CommandNames[d.command]) {
                        pendingEts2Commands.push(d);
                        if (pendingEts2Commands.length > 16) pendingEts2Commands.shift();
                    }
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
