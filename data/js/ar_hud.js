// ================================================================
// ETS2 ASSIST — AR HUD (дополненная реальность) v60
// ================================================================
// ПРИНЦИП (требование пользователя 30.08.2026): страница НИЧЕГО не загружает.
// Приложение САМО находит ближайшую точку к фуре и присылает её:
//   ar_target     {hasTarget, gameName, realName, x, y, z, dist, kind, heading}
//   ar_telemetry  {placement:[x,y,z,heading,pitch,roll], head:[x,y,z,heading,...]}
// Оба сообщения приходят по командному WS 8084 (тот же сервер, что у миникарты;
// web_pda_map.html подключается к нему же через websocket.js). Страница —
// dumb-receiver: принимает пакет, проецирует точку, рисует перекрестье.
//
// Проекция (математика v58 сохранена):
//   Мировые оси: X-восток, Z-юг, Y-высота. Глаз = placement + eyeHeight.
//   yaw = (heading фуры + heading головы) * 2π (влево +, SCS-конвенция).
//   Пинхол-проекция, гориз. FOV: f=0.5W/tan(FOV/2);
//   depth=fdot·cosP+wy·sinP; up=wy·cosP−fdot·sinP;
//   u=W/2+f·rdot/depth; v=H/2−f·up/depth.
//   Точка позади/вне экрана → прижим к рамке [margin..W/H−margin] вдоль луча
//   из центра экрана (ближайший край по направлению).

(function () {
    'use strict';

    const canvas = document.getElementById('arCanvas');
    const ctx = canvas.getContext('2d');
    const statusEl = document.getElementById('arStatus');
    let W = 0, H = 0, dpr = 1;

    const CFG = {
        fovDeg: 75,            // горизонтальный FOV (калибруется по фидбеку)
        eyeHeight: 2.1,        // высота глаза над опорной точкой фуры, м
        edgeMargin: 70,        // отступ перекрестья от края экрана (px)
        labelDy: 46,           // подпись под перекрестьем (px)
        wsUrl: 'ws://localhost:8084/'   // командный сервер приложения
    };

    // Состояние AR (консоль: window.__arHud)
    const ar = {
        camX: 0, camY: 0, camZ: 0,   // глаз (мир)
        yawBase: 0,                   // heading фуры (доля оборота)
        yawHead: 0,                   // yaw головы (рад)
        pitch: 0, roll: 0,
        haveTruck: false,
        haveHead: false,
        lastTelemetryAt: 0,
        target: null,                 // последняя ar_target {gameName, realName, x, y, z, dist, kind}
        sel: null                     // текущая экранная позиция перекрестья
    };
    window.__arHud = ar;

    // ================================================================
    // СТАТУС-СТРОКА
    // ================================================================
    function setStatus(kind, text) {
        statusEl.classList.remove('ok', 'warn', 'err');
        if (kind) statusEl.classList.add(kind);
        statusEl.textContent = text;
    }

    function statusFromState() {
        if (!ar.haveTruck) { setStatus('warn', 'AR: нет телеметрии от приложения'); return; }
        if (!ar.target) { setStatus('warn', 'AR: приложение не прислало цель'); return; }
        const age = Date.now() - ar.lastTelemetryAt;
        const stale = age > 5000 ? ' (телеметрия ' + Math.round(age / 1000) + 'с назад — пауза?)' : '';
        setStatus('ok', 'AR: ' + ar.target.gameName + ' · ' +
            fmtDist(ar.target.dist) + ' | голова: ' + (ar.haveHead ? 'да' : 'нет') + stale);
    }
    setInterval(statusFromState, 1000);

    function fmtDist(d) {
        if (!Number.isFinite(d)) return '—';
        return d < 1000 ? Math.round(d) + ' м' : (d / 1000).toFixed(2) + ' км';
    }

    // ================================================================
    // КОМАНДНЫЙ WS (8084) — приём пакетов от приложения
    // ================================================================
    let ws = null;
    function connect() {
        if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
        try {
            ws = new WebSocket(CFG.wsUrl);
            ws.onopen = function () { setStatus('ok', 'AR: подключено к приложению'); };
            ws.onmessage = function (ev) {
                let data = null;
                try { data = JSON.parse(ev.data); } catch (e) { return; }
                if (!data || !data.command) return;
                if (data.command === 'ar_target') applyArTarget(data);
                else if (data.command === 'ar_telemetry') applyArTelemetry(data);
            };
            ws.onclose = function () { ws = null; setTimeout(connect, 2000); };
            ws.onerror = function () { try { ws.close(); } catch (e) {} };
        } catch (e) {
            setTimeout(connect, 2000);
        }
    }

    // ---- Приём: цель (уже выбрана приложением как ближайшая) ----
    function applyArTarget(data) {
        if (data.hasTarget === true) {
            ar.target = {
                gameName: String(data.gameName || 'target'),
                realName: String(data.realName || ''),
                x: Number(data.x) || 0,
                y: Number(data.y) || 0,
                z: Number(data.z) || 0,
                dist: Number(data.dist) || 0,
                kind: String(data.kind || 'poi')
            };
        } else {
            ar.target = null; // приложение сказало: точек в радиусе нет
        }
        statusFromState();
    }

    // ---- Приём: телеметрия (placement + голова) от приложения ----
    function applyArTelemetry(data) {
        const p = data.placement;
        if (Array.isArray(p) && p.length >= 6) {
            ar.camX = Number(p[0]) || 0;
            ar.camY = (Number(p[1]) || 0) + CFG.eyeHeight;
            ar.camZ = Number(p[2]) || 0;
            ar.yawBase = Number(p[3]) || 0;
            ar.pitch = Number(p[4]) || 0;
            ar.roll = Number(p[5]) || 0;
            ar.haveTruck = true;
            ar.lastTelemetryAt = Date.now();
        }
        const h = data.head;
        if (Array.isArray(h) && h.length >= 4) {
            ar.yawHead = (Number(h[3]) || 0) * Math.PI * 2;
            ar.haveHead = true;
        }
    }

    connect();

    // ================================================================
    // ПРОЕКЦИЯ ТОЧКИ (математика v58)
    // ================================================================
    function projectPoint(pt) {
        const wx = pt.x - ar.camX;
        const wy = pt.y - ar.camY;
        const wz = pt.z - ar.camZ;
        const dist = Math.sqrt(wx * wx + wy * wy + wz * wz);

        const yaw = ar.yawBase * Math.PI * 2 + ar.yawHead;
        const sinY = Math.sin(yaw), cosY = Math.cos(yaw);
        const fwdX = -sinY, fwdZ = -cosY;     // вперёд (yaw=0 → север: -Z)
        const rightX = cosY, rightZ = -sinY;  // вправо (yaw=0 → восток: +X)

        const fdot = wx * fwdX + wz * fwdZ;
        const rdot = wx * rightX + wz * rightZ;
        const pitchRad = ar.pitch || 0;
        const cosP = Math.cos(pitchRad), sinP = Math.sin(pitchRad);

        const depth = fdot * cosP + wy * sinP;
        const up = wy * cosP - fdot * sinP;

        const halfTan = Math.tan((CFG.fovDeg * Math.PI / 180) / 2);
        const f = (W * 0.5) / halfTan;

        let u, v;
        if (depth > 0.5) {
            u = W / 2 + f * (rdot / depth);
            v = H / 2 - f * (up / depth);
        } else {
            // Точка позади: направление к краю по знакам компонент.
            u = (rdot >= 0) ? Infinity : -Infinity;
            v = (up >= 0) ? -Infinity : Infinity;
        }
        return { dist, u, v, inFront: fdot > 0, depth };
    }

    // Прижим к рамке [m..W-m]×[m..H-m] вдоль луча из центра экрана.
    function clampToScreen(u, v) {
        const m = CFG.edgeMargin;
        const cx = W / 2, cy = H / 2;
        const du = u - cx, dv = v - cy;

        if (Number.isFinite(u) && Number.isFinite(v)) {
            if (u >= m && u <= W - m && v >= m && v <= H - m) {
                return { u, v, clamped: false };
            }
        } else {
            const sU = Number.isFinite(u) ? 0 : (u > 0 ? 1 : -1);
            const sV = Number.isFinite(v) ? 0 : (v > 0 ? 1 : -1);
            if (sU !== 0 && sV === 0) return { u: sU > 0 ? W - m : m, v: cy, clamped: true };
            if (sV !== 0 && sU === 0) return { u: cx, v: sV > 0 ? H - m : m, clamped: true };
            return { u: sU > 0 ? W - m : m, v: sV > 0 ? H - m : m, clamped: true };
        }

        let t = Infinity;
        if (du > 0) t = Math.min(t, (W - m - cx) / du);
        else if (du < 0) t = Math.min(t, (m - cx) / du);
        if (dv > 0) t = Math.min(t, (H - m - cy) / dv);
        else if (dv < 0) t = Math.min(t, (m - cy) / dv);
        if (!Number.isFinite(t) || t <= 0) {
            return {
                u: Math.min(Math.max(u, m), W - m),
                v: Math.min(Math.max(v, m), H - m),
                clamped: true
            };
        }
        return { u: cx + du * t, v: cy + dv * t, clamped: true };
    }

    // ================================================================
    // ОТРИСОВКА
    // ================================================================
    function resize() {
        dpr = window.devicePixelRatio || 1;
        W = window.innerWidth; H = window.innerHeight;
        canvas.width = Math.max(1, Math.round(W * dpr));
        canvas.height = Math.max(1, Math.round(H * dpr));
        canvas.style.width = W + 'px';
        canvas.style.height = H + 'px';
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
    window.addEventListener('resize', resize);
    resize();

    function drawOutlinedText(text, x, y, font, color) {
        ctx.font = font;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(0,0,0,0.85)';
        ctx.strokeText(text, x, y);
        ctx.fillStyle = color || '#ffffff';
        ctx.fillText(text, x, y);
    }

    function drawCrosshair(u, v, color) {
        const S = 26, G = 7;
        ctx.save();
        for (const pair of [[6, 'rgba(0,0,0,0.85)'], [3, color]]) {
            ctx.lineWidth = pair[0];
            ctx.strokeStyle = pair[1];
            ctx.beginPath();
            ctx.moveTo(u - G - S, v); ctx.lineTo(u - G, v);
            ctx.moveTo(u + G, v); ctx.lineTo(u + G + S, v);
            ctx.moveTo(u, v - G - S); ctx.lineTo(u, v - G);
            ctx.moveTo(u, v + G); ctx.lineTo(u, v + G + S);
            ctx.stroke();
        }
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(u, v, 2.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    function drawEdgeArrow(u, v, color) {
        const cx = W / 2, cy = H / 2;
        const ang = Math.atan2(v - cy, u - cx);
        const ax = u - Math.cos(ang) * 34;
        const ay = v - Math.sin(ang) * 26;
        ctx.save();
        ctx.translate(ax, ay);
        ctx.rotate(ang + Math.PI);
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(10, 0); ctx.lineTo(-6, -7); ctx.lineTo(-6, 7);
        ctx.closePath();
        ctx.fill();
        ctx.restore();
    }

    function colorFor(kind) {
        if (kind === 'target') return '#ff3b30';
        if (kind === 'city') return '#7bed9f';
        return '#70d1fe';
    }

    function render() {
        requestAnimationFrame(render);
        if (W !== window.innerWidth || H !== window.innerHeight) resize();
        ctx.clearRect(0, 0, W, H);
        if (!ar.haveTruck || !ar.target) return;

        const pr = projectPoint(ar.target);
        const cl = clampToScreen(pr.u, pr.v);
        ar.sel = { u: cl.u, v: cl.v, clamped: cl.clamped, inFront: pr.inFront };

        const color = colorFor(ar.target.kind);
        drawCrosshair(cl.u, cl.v, color);

        const distText = fmtDist(pr.dist);
        drawOutlinedText(ar.target.gameName + '  \u00B7  ' + distText,
            cl.u, cl.v + CFG.labelDy, '600 14px "Segoe UI", Arial', '#ffffff');
        if (ar.target.realName && ar.target.realName !== ar.target.gameName) {
            drawOutlinedText(ar.target.realName,
                cl.u, cl.v + CFG.labelDy + 19, '12px "Segoe UI", Arial',
                'rgba(255,255,255,0.85)');
        }
        if (cl.clamped) drawEdgeArrow(cl.u, cl.v, color);
    }
    requestAnimationFrame(render);
})();