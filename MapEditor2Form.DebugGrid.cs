using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private async Task InstallDebugGridAsync()
        {
            if (_webView.CoreWebView2 == null)
                return;

            const string script = @"
(() => {
    if (window.__mapEditor2GridInstalled) return;
    window.__mapEditor2GridInstalled = true;

    const source = document.getElementById('gl');
    const host = document.getElementById('map');
    if (!source || !host) return;

    const grid = document.getElementById('diagnostic-grid') || document.createElement('canvas');
    if (!grid.parentElement) {
        grid.id = 'diagnostic-grid';
        grid.style.position = 'absolute';
        grid.style.inset = '0';
        grid.style.width = '100%';
        grid.style.height = '100%';
        grid.style.pointerEvents = 'none';
        grid.style.zIndex = '1';
        host.insertBefore(grid, source.nextSibling);
    }

    const ctx = grid.getContext('2d');
    const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
    let offsetX = 0;
    let offsetY = 0;
    let scale = 1;
    const baseSpacing = 80;

    function resize() {
        const w = Math.max(1, Math.floor(innerWidth * dpr));
        const h = Math.max(1, Math.floor(innerHeight * dpr));
        grid.width = w;
        grid.height = h;
        grid.style.width = innerWidth + 'px';
        grid.style.height = innerHeight + 'px';
        draw();
    }

    function niceStep(px) {
        const target = Math.max(48, Math.min(140, px));
        const p = Math.pow(10, Math.floor(Math.log10(target)));
        const n = target / p;
        const m = n < 1.5 ? 1 : n < 3.5 ? 2 : n < 7.5 ? 5 : 10;
        return m * p;
    }

    function draw() {
        const w = innerWidth;
        const h = innerHeight;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, w, h);

        const step = niceStep(baseSpacing * scale);
        const startX = ((offsetX % step) + step) % step;
        const startY = ((offsetY % step) + step) % step;

        ctx.lineWidth = 1;
        ctx.strokeStyle = 'rgba(120, 135, 155, 0.26)';
        ctx.beginPath();
        for (let x = startX; x < w; x += step) {
            ctx.moveTo(Math.round(x) + 0.5, 0);
            ctx.lineTo(Math.round(x) + 0.5, h);
        }
        for (let y = startY; y < h; y += step) {
            ctx.moveTo(0, Math.round(y) + 0.5);
            ctx.lineTo(w, Math.round(y) + 0.5);
        }
        ctx.stroke();

        const major = step * 5;
        const majorStartX = ((offsetX % major) + major) % major;
        const majorStartY = ((offsetY % major) + major) % major;
        ctx.strokeStyle = 'rgba(170, 185, 205, 0.42)';
        ctx.beginPath();
        for (let x = majorStartX; x < w; x += major) {
            ctx.moveTo(Math.round(x) + 0.5, 0);
            ctx.lineTo(Math.round(x) + 0.5, h);
        }
        for (let y = majorStartY; y < h; y += major) {
            ctx.moveTo(0, Math.round(y) + 0.5);
            ctx.lineTo(w, Math.round(y) + 0.5);
        }
        ctx.stroke();

        ctx.fillStyle = 'rgba(210, 220, 232, 0.80)';
        ctx.font = '11px Segoe UI, Arial, sans-serif';
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        ctx.fillText('GRID · ' + Math.round(step) + ' px', 12, 34);
    }

    source.addEventListener('pointermove', e => {
        if (!source.hasPointerCapture(e.pointerId)) return;
        offsetX += e.movementX;
        offsetY += e.movementY;
        draw();
    });

    source.addEventListener('wheel', e => {
        const factor = Math.pow(1.13, e.deltaY / 100);
        const rect = source.getBoundingClientRect();
        const px = e.clientX - rect.left;
        const py = e.clientY - rect.top;
        const oldScale = scale;
        scale /= factor;
        scale = Math.max(0.08, Math.min(24, scale));
        const applied = scale / oldScale;
        offsetX = px - (px - offsetX) * applied;
        offsetY = py - (py - offsetY) * applied;
        draw();
    }, { passive: true });

    addEventListener('resize', resize);
    resize();
})();";

            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }
}
