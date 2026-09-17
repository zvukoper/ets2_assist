/* ETS2 Assist — shared pause visibility policy for WebOverlay pages. */
(function () {
    'use strict';

    var path = String(window.location.pathname || '').toLowerCase();
    var mode = path.indexOf('web_pause_logo.html') >= 0 ? 'logo'
        : path.indexOf('web_pda_map.html') >= 0 ? 'hide-on-pause'
        : path.indexOf('web_ui_hybrid.html') >= 0 ? 'hide-on-pause'
        : path.indexOf('web_ar_hud.html') >= 0 ? 'hide-on-pause'
        : null;

    if (!mode) return;

    var lastPaused = null;

    var style = document.createElement('style');
    style.textContent =
        'html.ets2-pause-hidden,html.ets2-pause-hidden body{' +
        'visibility:hidden!important;opacity:0!important;pointer-events:none!important;' +
        '}';
    (document.head || document.documentElement).appendChild(style);

    function apply(paused) {
        paused = paused === true;
        if (lastPaused === paused) return;
        lastPaused = paused;

        if (mode === 'logo') {
            var logo = document.getElementById('pauseLogo');
            if (logo) logo.classList.toggle('visible', paused);
            document.documentElement.classList.remove('ets2-pause-hidden');
        } else {
            document.documentElement.classList.toggle('ets2-pause-hidden', paused);
        }
    }

    function connect() {
        try {
            var ws = new WebSocket('ws://localhost:8085/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (d && d.command === 'quest_state') apply(d.paused === true);
                } catch (_) { }
            };
            ws.onclose = function () { setTimeout(connect, 1000); };
            ws.onerror = function () { try { ws.close(); } catch (_) { } };
        } catch (_) {
            setTimeout(connect, 1000);
        }
    }

    if (mode === 'logo') {
        // The logo used to depend on commands from port 8084. Pause state is now
        // authoritative on QuestRuntime's port 8085, so it also appears on the
        // very first pause and disappears on resume without requiring a button.
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function () { apply(false); connect(); });
        } else {
            apply(false);
            connect();
        }
    } else {
        apply(false);
        if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', connect);
        else
            connect();
    }
})();
