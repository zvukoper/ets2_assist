using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form : Form
    {
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
        private bool _pageReady;
        private string _targetSnapshotJson = "[]";
        private System.Windows.Forms.Timer? _editorStatusTimer;
        private bool _lastEditorRunning;

        // v39.89: создание точки по координатам из игрового редактора (Ctrl+Shift+X).
        // Вызывается из MainForm, когда MapEditor2Form открыт. Маршалит на UI-поток
        // и вызывает JS MapEditor2CreatePointFromEditor (аналог ЛКМ-клика в режиме
        // «Добавить»), с системным именем MapEditor<координаты>, зумом к точке и
        // фокусом на поле «Отображаемое имя».
        internal void CreatePointFromEditor(double x, double y, double z)
        {
            try
            {
                if (IsDisposed || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
                string sx = x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                string sy = y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                string sz = z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                _ = _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2CreatePointFromEditor?.({sx},{sy},{sz});");
            }
            catch { }
        }

        public MapEditor2Form()
        {
            Text = "Редактор карты 2";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ControlBox = true;
            BackColor = Color.FromArgb(15, 18, 23);
            Controls.Add(_webView);

            // Bugfix preload должен быть подключён раньше InitializeAsync,
            // чтобы NavigationStarting поймал первую навигацию и установил
            // document-created script до выполнения page scripts.
            RegisterMap2BugfixLoadHook();
            Load += async (_, _) => await InitializeAsync();
            FormClosed += (_, _) => { try { _webView.Dispose(); } catch { } };
        }

        private async Task InitializeAsync()
        {
            AppDataPaths.EnsureUserData();
            _targetSnapshotJson = BuildTargetSnapshotJson();
            await _webView.EnsureCoreWebView2Async();
            AttachEditingBridge();
            AttachOverridesBridge();
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ets2assist-map.local",
                AppDataPaths.StaticDataDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri("https://ets2assist-map.local/map_editor2/index.html");

            // v39.90: таймер обновления индикатора «Редактор запущен» в статусбаре.
            _editorStatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _editorStatusTimer.Tick += (_, _) => UpdateEditorStatusIndicator();
            _editorStatusTimer.Start();
        }

        // v39.90: обновляет индикатор «Редактор запущен» (lime) в статусбаре под картой.
        private void UpdateEditorStatusIndicator()
        {
            try
            {
                bool running = MapEditor2GameEditorBridge.IsEditorRunning();
                if (running == _lastEditorRunning) return;
                _lastEditorRunning = running;
                if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
                _ = _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SetEditorRunning?.({(running ? "true" : "false")});");
            }
            catch { }
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            if (string.Equals(message, "map2-ready", StringComparison.Ordinal))
            {
                _pageReady = true;
                // v39.91: при загрузке страницы принудительно обновляем индикатор
                // «Редактор запущен» (иначе он мог остаться скрытым, если CoreWebView2
                // не был готов на момент первого тика таймера).
                _lastEditorRunning = !MapEditor2GameEditorBridge.IsEditorRunning();
                UpdateEditorStatusIndicator();
                await ApplyMapEditor2UiOverridesAsync();
                await SendStaticPointFilesAsync();
                await SendTargetsAsync();
                // Секция «Сохранение точек»: список файлов + применение overrides к точкам.
                await SendOverridesToEditorAsync();
                return;
            }
            if (string.Equals(message, "map2-generate-terrain", StringComparison.Ordinal))
            {
                await GenerateTerrainAsync();
                return;
            }
            if (string.Equals(message, "map2-data-ready", StringComparison.Ordinal))
            {
                // Статические точки/категории полностью загружены — ПЕРЕД отрисовкой
                // применяем переопределения из map_overrides (первая строка
                // load_order.txt = высший приоритет).
                await SendOverridesToEditorAsync();
                return;
            }
            if (string.Equals(message, "map2-open-terrain-file", StringComparison.Ordinal))
            {
                OpenTerrainFileInExplorer();
                return;
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                try
                {
                    var trimmed = message.TrimStart();
                    if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return;
                    var cmd = JObject.Parse(message);
                    if (string.Equals((string?)cmd["type"], "map2-create-point", StringComparison.Ordinal))
                    {
                        var x = cmd["x"]?.Value<double>() ?? 0d;
                        var y = cmd["y"]?.Value<double>() ?? 0d;
                        var z = cmd["z"]?.Value<double>() ?? 0d;
                        var coords = $"{x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, {y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, {z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";
                        try { MainForm.LogNewPointSelection(x, y, z); } catch { }
                        try { Clipboard.SetText(coords); } catch { }
                        if (_webView.CoreWebView2 != null)
                            await _webView.CoreWebView2.ExecuteScriptAsync(
                                $"window.MapEditor2ShowNewPoint({x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{z.ToString(System.Globalization.CultureInfo.InvariantCulture)});");
                    }
                }
                catch { }
            }
        }

        private async Task ApplyMapEditor2UiOverridesAsync()
        {
            if (_webView.CoreWebView2 == null) return;
            // Базовая типографика Map Editor 2: ВСЕ названия точек и категории —
            // одинаковый Roboto Regular. Города на карте — Roboto SemiBold и +20%.
            // Canvas-надписи нельзя надёжно переопределить обычным CSS, поэтому здесь
            // нормализуем и DOM, и canvas #labels.
            const string script = @"
(() => {
    try {
        const root = document.documentElement;
        root.dataset.ets2AssistMap2UiOverrides = '1';
        const styleId = 'ets2-assist-map2-ui-overrides';
        if (!document.getElementById(styleId)) {
            const style = document.createElement('style');
            style.id = styleId;
            style.textContent = `
.categoryHead,
.pointButton,
.pointName {
    font-family: Roboto, Arial, sans-serif !important;
    font-weight: 400 !important;
}
`;
            document.head.appendChild(style);
        }

        if (!window.__ets2AssistPointFontPatchInstalled) {
            const originalFillText = CanvasRenderingContext2D.prototype.fillText;
            const originalStrokeText = CanvasRenderingContext2D.prototype.strokeText;

            CanvasRenderingContext2D.prototype.fillText = function(text, x, y, maxWidth) {
                try {
                    const root = document.documentElement;
                    if (root?.dataset?.ets2AssistMap2PointLabel === '1') {
                        const oldFont = this.font;
                        const m = /^(.*?)(\d+(?:\.\d+)?)px\s+(.*)$/.exec(oldFont);
                        if (m) this.font = `${m[1]}${(parseFloat(m[2]) || 12).toFixed(2)}px Roboto, Arial, sans-serif`;
                        const result = maxWidth == null ? originalFillText.call(this, text, x, y) : originalFillText.call(this, text, x, y, maxWidth);
                        this.font = oldFont;
                        return result;
                    }
                } catch (_) { }
                return maxWidth == null ? originalFillText.call(this, text, x, y) : originalFillText.call(this, text, x, y, maxWidth);
            };

            CanvasRenderingContext2D.prototype.strokeText = function(text, x, y, maxWidth) {
                try {
                    const root = document.documentElement;
                    if (root?.dataset?.ets2AssistMap2PointLabel === '1') {
                        const oldFont = this.font;
                        const m = /^(.*?)(\d+(?:\.\d+)?)px\s+(.*)$/.exec(oldFont);
                        if (m) this.font = `${m[1]}${(parseFloat(m[2]) || 12).toFixed(2)}px Roboto, Arial, sans-serif`;
                        const result = maxWidth == null ? originalStrokeText.call(this, text, x, y) : originalStrokeText.call(this, text, x, y, maxWidth);
                        this.font = oldFont;
                        return result;
                    }
                } catch (_) { }
                return maxWidth == null ? originalStrokeText.call(this, text, x, y) : originalStrokeText.call(this, text, x, y, maxWidth);
            };

            window.__ets2AssistPointFontPatchInstalled = true;
        }
    } catch (_) { }
})();";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }
}
