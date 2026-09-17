// ETS2 Assist — optional AR1 diagnostics.
// These are intentionally OFF by default and are controlled by the application
// through the normal ar_view WebSocket command.
(function () {
    'use strict';

    var state = {
        showDebugStatus: false,
        showGroundDiagnostic: false
    };

    var statusStyle = document.createElement('style');
    statusStyle.id = 'ets2ArDebugViewStyle';
    document.head.appendChild(statusStyle);

    function applyStatusVisibility() {
        statusStyle.textContent = state.showDebugStatus
            ? ''
            : '#arStatus{display:none!important;}';
    }

    function isDiagnosticDistanceText(text) {
        if (typeof text !== 'string') return false;
        var value = text.trim();
        return /^\d+(?:[.,]\d+)?\s*(?:м|км)$/.test(value)
            || /^(?:\d+(?:[.,]\d+)?\s*)?—$/.test(value);
    }

    // The micro distance is drawn directly into the AR canvas by ar_hud.js.
    // We suppress only distance-looking text near screen centre; normal point
    // names and the FOV diagnostics are unaffected.
    var nativeFillText = CanvasRenderingContext2D.prototype.fillText;
    var nativeStrokeText = CanvasRenderingContext2D.prototype.strokeText;

    function isCenterDistance(ctx, text, x, y) {
        if (state.showGroundDiagnostic || !isDiagnosticDistanceText(text)) return false;
        var canvas = ctx && ctx.canvas;
        if (!canvas) return false;
        var w = canvas.clientWidth || 0;
        var h = canvas.clientHeight || 0;
        if (!w || !h) return false;
        var cx = w / 2;
        var cy = h / 2;
        return Math.abs(Number(x) - cx) <= 24 && Math.abs(Number(y) - (cy + 12)) <= 30;
    }

    CanvasRenderingContext2D.prototype.fillText = function (text, x, y) {
        if (isCenterDistance(this, text, x, y)) return;
        return nativeFillText.apply(this, arguments);
    };

    CanvasRenderingContext2D.prototype.strokeText = function (text, x, y) {
        if (isCenterDistance(this, text, x, y)) return;
        return nativeStrokeText.apply(this, arguments);
    };

    // drawDiagCross() uses two short line segments through the exact screen
    // centre. Track the current path and suppress only this small centred cross.
    var nativeBeginPath = CanvasRenderingContext2D.prototype.beginPath;
    var nativeMoveTo = CanvasRenderingContext2D.prototype.moveTo;
    var nativeLineTo = CanvasRenderingContext2D.prototype.lineTo;
    var nativeStroke = CanvasRenderingContext2D.prototype.stroke;
    var pathState = new WeakMap();

    CanvasRenderingContext2D.prototype.beginPath = function () {
        pathState.set(this, { segments: 0, minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity });
        return nativeBeginPath.apply(this, arguments);
    };

    CanvasRenderingContext2D.prototype.moveTo = function (x, y) {
        var p = pathState.get(this);
        if (p) {
            p.minX = Math.min(p.minX, Number(x));
            p.maxX = Math.max(p.maxX, Number(x));
            p.minY = Math.min(p.minY, Number(y));
            p.maxY = Math.max(p.maxY, Number(y));
        }
        return nativeMoveTo.apply(this, arguments);
    };

    CanvasRenderingContext2D.prototype.lineTo = function (x, y) {
        var p = pathState.get(this);
        if (p) {
            p.segments++;
            p.minX = Math.min(p.minX, Number(x));
            p.maxX = Math.max(p.maxX, Number(x));
            p.minY = Math.min(p.minY, Number(y));
            p.maxY = Math.max(p.maxY, Number(y));
        }
        return nativeLineTo.apply(this, arguments);
    };

    CanvasRenderingContext2D.prototype.stroke = function () {
        if (!state.showGroundDiagnostic) {
            var p = pathState.get(this);
            var canvas = this.canvas;
            if (p && canvas && p.segments === 2) {
                var w = canvas.clientWidth || 0;
                var h = canvas.clientHeight || 0;
                var bx = (p.minX + p.maxX) / 2;
                var by = (p.minY + p.maxY) / 2;
                var bw = p.maxX - p.minX;
                var bh = p.maxY - p.minY;
                if (w && h &&
                    Math.abs(bx - w / 2) <= 24 &&
                    Math.abs(by - h / 2) <= 24 &&
                    bw <= 24 && bh <= 24) {
                    pathState.delete(this);
                    return;
                }
            }
        }
        return nativeStroke.apply(this, arguments);
    };

    function apply(data) {
        if (!data) return;
        if (data.showDebugStatus !== undefined)
            state.showDebugStatus = data.showDebugStatus === true;
        if (data.showGroundDiagnostic !== undefined)
            state.showGroundDiagnostic = data.showGroundDiagnostic === true;
        applyStatusVisibility();
    }

    function connect() {
        try {
            var ws = new WebSocket('ws://localhost:8084/');
            ws.onmessage = function (ev) {
                try {
                    var data = JSON.parse(ev.data);
                    if (data && data.command === 'ar_view') apply(data);
                } catch (_) { }
            };
            ws.onclose = function () { setTimeout(connect, 1000); };
            ws.onerror = function () { try { ws.close(); } catch (_) { } };
        } catch (_) {
            setTimeout(connect, 1000);
        }
    }

    applyStatusVisibility();
    connect();
})();
