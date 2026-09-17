(function () {
    'use strict';
    var ws = null;
    var questState = null;
    var bases = new Map();
    var lastDraw = 0;

    var NON_INTERACTIVE_HINTS = ['кафе','магазин','пятёр','пятер','сувенир','отель','гостиниц','мотел','сервис','сто','шиномонтаж','шины','полиц'];
    var INTERACTIVE_CATS = new Set(['5ka','cafe','small_cafe','letn_cafe','producti','souvenir','souvenir_shop','shop','minimarket','hotel','motel','hostel','service','service_station','mechanic','garage','tire_repairs','shinomontaz','police','police_man','police_car','fuel_l','refuel_zone','restaurant','refuel','parking_l','paid_parking']);

    function isInteractiveBase(p) {
        var cat = String(p.type || p.category || '').toLowerCase();
        if (INTERACTIVE_CATS.has(cat)) return true;
        var name = String(p.name || '').toLowerCase();
        return NON_INTERACTIVE_HINTS.some(function (x) { return name.indexOf(x) >= 0 || cat.indexOf(x) >= 0; });
    }

    function iconPath(marker) {
        if (marker === 'yellow_exclamation') return 'editor_static_data/icons/quest_exclamation_yellow.svg';
        if (marker === 'yellow_question') return 'editor_static_data/icons/quest_question_yellow.svg';
        if (marker === 'gray_question') return 'editor_static_data/icons/quest_question_gray.svg';
        return '';
    }

    function ensureBases() {
        if (!window.state || !Array.isArray(window.state.pois)) return;
        window.state.pois.forEach(function (p) {
            var uid = String(p.uid || '');
            if (!uid || bases.has(uid)) return;
            bases.set(uid, { displayOnMap: p.displayOnMap, name: p.name, icon: p.icon, type: p.type, opacity: p.opacity });
            p.__questBaseInteractive = isInteractiveBase(p);
            p.__questBaseName = p.name;
            p.__questBaseIcon = p.icon;
            p.__questBaseType = p.type;
        });
    }

    function distance(a, b) {
        var dx = a.x - b.x, dy = (a.y || 0) - (b.y || 0), dz = a.z - b.z;
        return Math.sqrt(dx * dx + dy * dy + dz * dz);
    }

    function applyQuestState(data) {
        questState = data;
        ensureBases();
        var byUid = new Map();
        (data.points || []).forEach(function (q) { byUid.set(String(q.Uid || q.uid), q); });

        if (window.state && Array.isArray(window.state.pois)) {
            window.state.pois.forEach(function (p) {
                var uid = String(p.uid || '');
                var base = bases.get(uid);
                var q = byUid.get(uid);
                if (q) {
                    p.displayOnMap = !!q.MinimapVisible;
                    p.name = q.Name || p.name;
                    if (q.Marker && q.Marker !== 'none') {
                        p.icon = iconPath(q.Marker);
                        p.type = 'quest';
                        p.opacity = 1;
                    } else {
                        p.icon = base ? base.icon : p.__questBaseIcon;
                        p.type = base ? base.type : p.__questBaseType;
                        p.opacity = 1;
                    }
                } else {
                    var showDebug = !!(data.settings && data.settings.DebugShowAllPoints);
                    var debugRadius = Number((data.settings && data.settings.DebugRadiusM) || 50);
                    var near = true;
                    if (showDebug && window.state.truck) near = distance(p, window.state.truck) <= debugRadius;
                    p.displayOnMap = showDebug ? near : !!p.__questBaseInteractive;
                    if (!q && p.__questPermanentName) p.displayOnMap = true;
                    if (base) { p.icon = base.icon; p.type = base.type; p.name = base.name; p.opacity = base.opacity; }
                }
            });
        }
        ensureNotificationElement();
        if (typeof window.drawMinimap === 'function') window.drawMinimap();
        renderQuestNotificationState();
        lastDraw = Date.now();
    }

    function ensureNotificationElement() {
        if (document.getElementById('questNotification')) return;
        var el = document.createElement('div');
        el.id = 'questNotification';
        el.innerHTML = '<div class="qIcon"></div><div class="qText"><div class="qTitle"></div><div class="qBody"></div></div>';
        document.body.appendChild(el);
        var st = document.createElement('style');
        st.textContent = '#questNotification{position:fixed;top:1.2%;left:50%;transform:translateX(-50%);width:32.4074vw;min-height:3.7037vh;box-sizing:border-box;display:none;align-items:center;gap:10px;padding:7px 12px;background:rgba(13,17,22,.94);border:1px solid rgba(255,210,55,.55);border-radius:6px;z-index:200;font-family:Segoe UI,Arial,sans-serif;color:#fff;box-shadow:0 5px 22px rgba(0,0,0,.55)}#questNotification.show{display:flex}#questNotification .qIcon{width:28px;height:28px;flex:0 0 28px;background-position:center;background-repeat:no-repeat;background-size:contain}#questNotification .qTitle{font-weight:700;font-size:15px;line-height:1.15}#questNotification .qBody{font-size:12px;line-height:1.25;margin-top:3px;white-space:pre-line;color:#cbd3de}.quest-reward{color:#b7ff46}';
        document.head.appendChild(st);
    }

    var hideTimer = null;
    function showNotification(n) {
        ensureNotificationElement();
        var el = document.getElementById('questNotification');
        var icon = el.querySelector('.qIcon'), title = el.querySelector('.qTitle'), body = el.querySelector('.qBody');
        var settings = questState && questState.settings || {};
        el.style.width = Number(settings.NotificationWidthPercent || 32.4074) + 'vw';
        el.style.minHeight = Number(settings.NotificationHeightPercent || 3.7037) + 'vh';
        var iconName = iconPath(n.icon || '');
        icon.style.backgroundImage = iconName ? 'url("' + iconName + '")' : 'none';
        title.textContent = n.title || '';
        body.textContent = n.text || '';
        title.style.display = n.title ? 'block' : 'none';
        el.classList.add('show');
        if (hideTimer) clearTimeout(hideTimer);
        hideTimer = setTimeout(function () { el.classList.remove('show'); }, 6500);
    }

    function renderQuestNotificationState() {
        ensureNotificationElement();
    }

    function connect() {
        try {
            ws = new WebSocket('ws://localhost:8085/');
            ws.onmessage = function (ev) {
                try {
                    var d = JSON.parse(ev.data);
                    if (d.command === 'quest_state') applyQuestState(d);
                    if (d.command === 'quest_notify') showNotification(d.notification || {});
                } catch (e) { console.warn('[QUEST] packet', e); }
            };
            ws.onclose = function () { setTimeout(connect, 1500); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) { setTimeout(connect, 1500); }
    }

    setInterval(function () {
        if (!questState) return;
        ensureBases();
        if (questState.settings && questState.settings.DebugShowAllPoints && typeof window.drawMinimap === 'function') {
            var now = Date.now();
            if (now - lastDraw > 250) { applyQuestState(questState); }
        }
    }, 250);

    document.addEventListener('DOMContentLoaded', function () { ensureNotificationElement(); connect(); });
})();