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
//   2. CSS-правило .debug-show-force — принудительный показ ВСЕХ «скрываемых»
//      элементов (display/opacity/visibility/transform снимаются !important).
//      Это работает даже для контента, который скрывает не наш код.
//   3. Инлайн-стиль каждого скрытого элемента запоминается и восстанавливается
//      при debugShow(false) — «возврат в исходную логику» без следов.
//   4. debugShow(true) замораживает попытки скрытия: перехватываются
//      classList.add/remove/toggle на document.body/documentElement и
//      изменения style.display/opacity/visibility через MutationObserver —
//      логика страницы продолжает работать, но визуально ничего не скрывается.
//   5. Метод доступен и как window.debugShow(bool), и командой WS 8084
//      { command: 'debug_show', enabled: true|false } (шлёт приложение).
//
// ВАЖНО: скрипт НИЧЕГО не знает о странице — только принудительная видимость.
// ================================================================
(function () {
    'use strict';

    var FLAG_ATTR = 'data-debug-show';
    // Элементы-«контейнеры», которые скрываются логикой страниц оверлея.
    var CONTAINERS = [
        '.minimap-container', '.dashboard-content', '.app', '.minimap-wrapper',
        '#pauseLogo', '#arCanvas', '#arStatus', '#mapCanvas', '#controls',
        '.container', '.loading-overlay'
    ];

    var saved = [];              // [{el, display, opacity, visibility, transform}]
    var observer = null;
    var freezeTimer = null;
    var originalAdd = null, originalRemove = null, originalToggle = null;

    function isOn() {
        return window.__debugShow === true;
    }

    // Стиль: принудительная видимость (правило вставляется в документ).
    function ensureStyle() {
        if (document.getElementById('debugShowStyle')) return;
        var st = document.createElement('style');
        st.id = 'debugShowStyle';
        st.textContent =
            'html[' + FLAG_ATTR + '="1"] .debug-show-force,' +
            'html[' + FLAG_ATTR + '="1"] .debug-show-force * {' +
            '  display: block !important;' +
            '  visibility: visible !important;' +
            '  opacity: 1 !important;' +
            '  transform: none !important;' +
            '  filter: none !important;' +
            '  pointer-events: auto !important;' +
            '  clip-path: none !important;' +
            '  max-width: none !important;' +
            '  max-height: none !important;' +
            '}' +
            'html[' + FLAG_ATTR + '="1"] body {' +
            '  display: block !important;' +
            '  visibility: visible !important;' +
            '  opacity: 1 !important;' +
            '}';
        (document.head || document.documentElement).appendChild(st);
    }

    // Запоминаем инлайн-стиль и снимаем скрытие со всех контейнеров.
    function forceShow() {
        var nodes = [];
        for (var i = 0; i < CONTAINERS.length; i++) {
            var found = document.querySelectorAll(CONTAINERS[i]);
            for (var j = 0; j < found.length; j++) nodes.push(found[j]);
        }
        // Плюс все элементы, которые сейчас реально скрыты инлайн-стилем.
        var all = document.body ? document.body.querySelectorAll('*') : [];
        for (var k = 0; k < all.length; k++) {
            var s = all[k].style;
            if (s && (s.display === 'none' || s.opacity === '0' || s.visibility === 'hidden')) {
                nodes.push(all[k]);
            }
        }
        for (var n = 0; n < nodes.length; n++) {
            var el = nodes[n];
            if (el.__debugSaved) continue;
            el.__debugSaved = {
                display: el.style.display,
                opacity: el.style.opacity,
                visibility: el.style.visibility,
                transform: el.style.transform
            };
            saved.push(el);
            el.classList.add('debug-show-force');
            el.style.display = '';
            el.style.opacity = '';
            el.style.visibility = '';
            el.style.transform = '';
        }
    }

    // Возврат к исходной логике: убираем принудительные стили.
    function restore() {
        for (var i = 0; i < saved.length; i++) {
            var el = saved[i];
            var sv = el.__debugSaved;
            if (sv) {
                el.style.display = sv.display;
                el.style.opacity = sv.opacity;
                el.style.visibility = sv.visibility;
                el.style.transform = sv.transform;
            }
            el.classList.remove('debug-show-force');
            el.__debugSaved = null;
        }
        saved = [];
    }

    // Заморозка скрытия: пока debugShow=true, classList-скрытия и смена
    // display/opacity/visibility не дают эффекта (логика страницы не ломается).
    function installFreeze() {
        if (originalAdd) return;
        var proto = window.DOMTokenList && window.DOMTokenList.prototype;
        if (proto) {
            originalAdd = proto.add;
            originalRemove = proto.remove;
            originalToggle = proto.toggle;
        }
        var guarded = function (el, cls) {
            if (isOn() && el && (el === document.body || el === document.documentElement) && cls) {
                var list = Array.isArray(cls) ? cls : [cls];
                for (var i = 0; i < list.length; i++) {
                    var c = String(list[i]);
                    if (c === 'hide-ui' || c === 'hidden' || c === 'is-hidden' || c === 'ui-hidden') {
                        return;   // принудительный режим: игнорируем скрывающий класс
                    }
                }
            }
            return null;
        };
        if (originalAdd) {
            proto.add = function () {
                if (guarded(this, Array.prototype.slice.call(arguments)) === null) { /* пропускаем */ }
                else return this;
                return originalAdd.apply(this, arguments);
            };
            proto.remove = function () { return originalRemove.apply(this, arguments); };
            proto.toggle = function () { return originalToggle.apply(this, arguments); };
        }
        // MutationObserver: повторно форсим показ, если страница снова спрятала контейнер.
        if (window.MutationObserver && document.body) {
            observer = new MutationObserver(function () {
                if (!isOn()) return;
                forceShow();
            });
            observer.observe(document.body, { attributes: true, attributeFilter: ['style', 'class'], subtree: true });
        }
        // Периодический «дозатор»: страницы оверлеев меняют стили по своему
        // расписанию (таймеры/анимации), поэтому подстраховываемся раз в 500 мс.
        freezeTimer = setInterval(function () { if (isOn()) forceShow(); }, 500);
    }

    function uninstallFreeze() {
        if (originalAdd && window.DOMTokenList && window.DOMTokenList.prototype) {
            window.DOMTokenList.prototype.add = originalAdd;
            window.DOMTokenList.prototype.remove = originalRemove;
            window.DOMTokenList.prototype.toggle = originalToggle;
        }
        originalAdd = originalRemove = originalToggle = null;
        if (observer) { try { observer.disconnect(); } catch (e) {} observer = null; }
        if (freezeTimer) { clearInterval(freezeTimer); freezeTimer = null; }
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
            installFreeze();
            forceShow();
        } else {
            uninstallFreeze();
            restore();
        }
        // Дополнительный хук страницы (если есть): своя логика отладки.
        try { if (typeof window.onDebugShow === 'function') window.onDebugShow(enable); } catch (e) {}
        console.log('[debugShow] ' + (enable ? 'ON (принудительный показ)' : 'OFF (логика страницы)'));
        return enable;
    }

    // Глобально (требование: «спецметод debugShow(bool) во весь веб-контент»).
    window.debugShow = debugShow;

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
