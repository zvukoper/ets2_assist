// ================================================================
// ETS2 ASSIST — ЕДИНЫЙ DEBUG-РЕЖИМ ДЛЯ ВСЕГО ВЕБ-КОНТЕНТА ОВЕРЛЕЯ
// ================================================================
// ТРЕБОВАНИЕ ПОЛЬЗОВАТЕЛЯ (v1.0.40.28):
//   «Во весь веб-контент, который мы отправляем через вебоверлей нужно добавить
//    спецметод debugShow(bool), который будет игнорировать логику показа контента
//    и принудительно делать контент видимым для отладки и настройки.
//    True — игнорирование логики и показ, False — возврат в исходную логику.»
//
// ПОДКЛЮЧЕНИЕ: <script src="js/debug_show.js"></script> ПЕРВЫМ в <head>
// (до остальных скриптов), чтобы флаг успел стать и его увидели функции показа.
//
// РЕАЛИЗАЦИЯ:
//   1. Флаг window.__debugShow (true/false) + <html data-debug-show="1|0">.
//   2. debugShow(true) ОТКЛЮЧАЕТ ТОЛЬКО СКРЫТИЕ ОКОН ВНЕ ФОКУСА (класс
//      ets2-hide-all на <html>, ставится js/ui_category.js по команде
//      приложения set_overlay_hidden). Логика ПАУЗЫ / БЕЗ ПАУЗЫ при этом
//      работает как обычно — она приходит отдельными командами
//      (show_ui/hide_ui, minimap_show/minimap_hide, set_overlay_category).
//   3. Стили, классы и анимации страниц debugShow НЕ переписывает: раньше
//      правило .debug-show-force с display:block/transform:none/max-width:none
//      применялось ко ВСЕМ потомкам и ломало раскладку (гибридное окно
//      «меняло масштабы», блоки искажались), а заморозка classList/инлайн-стилей
//      полностью блокировала логику показа/скрытия. См. v1.0.40.58.
//   4. Метод доступен и как window.debugShow(bool), и командой WS 8084
//      { command: 'debug_show', enabled: true|false } (шлёт приложение).
//
// ВАЖНО: скрипт НИЧЕГО не знает о странице — только флаг режима.
// ================================================================
(function () {
    'use strict';

    var FLAG_ATTR = 'data-debug-show';

    // ================================================================
    // v1.0.40.58: debugShow БОЛЬШЕ НЕ ПЕРЕПИСЫВАЕТ СТИЛИ СТРАНИЦЫ.
    //
    // ЧТО БЫЛО НЕ ТАК (жалоба пользователя): прежняя версия вешала на ВСЕ
    // найденные контейнеры и ИХ ПОТОМКОВ правило .debug-show-force с
    // display:block/transform:none/max-width:none !important, плюс замораживала
    // classList и смену инлайн-стилей. Для гибридного окна (flex-раскладка +
    // анимация scale) это ломало ВСЁ: окно «меняло масштабы», а блоки внутри
    // искажались. Заморозка заодно ОТКЛЮЧАЛА логику паузы (show_ui/hide_ui,
    // minimap_show/minimap_hide переставали действовать).
    //
    // ТРЕБОВАНИЕ: debugShow должен отключать ТОЛЬКО скрытие окон ВНЕ ФОКУСА,
    // а логика ПАУЗЫ / БЕЗ ПАУЗЫ обязана работать как обычно.
    //
    // РЕАЛИЗАЦИЯ: одна пассивная CSS-страховка — под debugShow правило скрытия
    // по фокусу (класс ets2-hide-all на <html>) не применяется. Внутренние
    // стили, классы и анимации страниц НЕ ТРОГАЕМ: никакой заморозки, никаких
    // !important на потомков, никакого MutationObserver и дозатора.
    // (Основной фикс живёт в js/ui_category.js — там правило тоже закрыто
    //  селектором :not([data-debug-show="1"]); здесь дублируется на случай,
    //  если стиль категорий ещё не загрузился.)
    // ================================================================
    function ensureStyle() {
        if (document.getElementById('debugShowStyle')) return;
        var st = document.createElement('style');
        st.id = 'debugShowStyle';
        st.textContent =
            'html[' + FLAG_ATTR + '="1"].ets2-hide-all [data-category] {' +
            '  visibility: visible !important;' +
            '  opacity: 1 !important;' +
            '  pointer-events: auto !important;' +
            '}';
        (document.head || document.documentElement).appendChild(st);
    }

    // ================================================================
    // ПУБЛИЧНЫЙ МЕТОД (требование пользователя): debugShow(bool)
    //   true  — ИГНОРИРОВАТЬ логику показа: контент виден принудительно;
    //   false — ВЕРНУТЬ исходную логику (снятие всех принудительных стилей).
    // ================================================================
    function debugShow(on) {
        var enable = (on === true);
        window.__debugShow = enable;
        try { document.documentElement.setAttribute(FLAG_ATTR, enable ? '1' : '0'); } catch (e) {}
        ensureStyle();
        if (enable) {
            // Снимаем УЖЕ применённое скрытие по фокусу: класс ets2-hide-all и
            // инлайн pointer-events на <html> ставит js/ui_category.js. Логику
            // паузы это не затрагивает — она приедет отдельными командами.
            try {
                document.documentElement.classList.remove('ets2-hide-all');
                document.documentElement.style.pointerEvents = '';
            } catch (e) {}
        }
        // Просим модуль категорий пересмотреть своё состояние: при выключении
        // debugShow ранее поданная команда set_overlay_hidden=true должна снова
        // иметь силу (иначе окна остались бы видимыми без фокуса).
        try { if (typeof window.ets2ReapplyVisibility === 'function') window.ets2ReapplyVisibility(); } catch (e) {}
        // Дополнительный хук страницы (если есть): своя логика отладки.
        try { if (typeof window.onDebugShow === 'function') window.onDebugShow(enable); } catch (e) {}
        console.log('[debugShow] ' + (enable
            ? 'ON (скрытие по фокусу игнорируется, логика паузы работает)'
            : 'OFF (логика страницы)'));
        return enable;
    }

    // Глобально (требование: «спецметод debugShow(bool) во весь веб-контент»).
    window.debugShow = debugShow;
    // v1.0.40.58: базовая реализация доступна страницам, у которых ЕСТЬ свой
    // window.debugShow (ar_hud.js, web_heights.html — они перекрывают метод
    // своими отметками). Иначе флаг + снятие скрытия по фокусу не применились бы.
    window.ets2DebugShowBase = debugShow;

    // Команда от приложения через WS 8084 — единая для всех страниц.
    // (Страницы со своим WS-клиентом обрабатывают её сами; здесь — общий фолбэк
    // для страниц без WS, чтобы метод всё равно был доступен.)
    function bindWs() {
        try {
            var ws = new WebSocket('ws://localhost:8084/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (d && d.command === 'debug_show') debugShow(d.enabled === true);
                } catch (e) {}
            };
            ws.onclose = function () { setTimeout(bindWs, 3000); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) { setTimeout(bindWs, 3000); }
    }

    function start() {
        ensureStyle();
        try { document.documentElement.setAttribute(FLAG_ATTR, '0'); } catch (e) {}
        // Разрешаем включение через URL: ?debugshow=1 (удобно при настройке).
        try {
            var q = new URLSearchParams(window.location.search);
            if (q.get('debugshow') === '1' || q.get('debug') === 'true') debugShow(true);
        } catch (e) {}
        bindWs();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
