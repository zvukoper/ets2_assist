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
            Load += async (_, _) => await InitializeAsync();
            FormClosed += (_, _) => { try { _webView.Dispose(); } catch { } };
        }

        private async Task InitializeAsync()
        {
            AppDataPaths.EnsureUserData();
            _targetSnapshotJson = BuildTargetSnapshotJson();
            await _webView.EnsureCoreWebView2Async();
            AttachEditingBridge();
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ets2assist-map.local",
                AppDataPaths.StaticDataDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri("https://ets2assist-map.local/map_editor2/index.html");
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            if (string.Equals(message, "map2-ready", StringComparison.Ordinal))
            {
                _pageReady = true;
                await ApplyMapEditor2UiOverridesAsync();
                await SendStaticPointFilesAsync();
                await SendTargetsAsync();
                return;
            }
            if (string.Equals(message, "map2-generate-terrain", StringComparison.Ordinal))
            {
                await GenerateTerrainAsync();
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

            const getScale = () => {
                const n = Number.parseFloat(
                    getComputedStyle(document.documentElement).getPropertyValue('--font-scale')
                );
                return Number.isFinite(n) ? n : 1.15;
            };

            const normalizeLabelFont = (ctx) => {
                if (!ctx || !ctx.canvas || ctx.canvas.id !== 'labels') return null;
                const current = String(ctx.font || '');
                const match = current.match(/^\\s*(\\d+(?:\\.\\d+)?)px\\s+(.+)$/i);
                if (!match) return null;
                const oldPx = Number.parseFloat(match[1]);
                if (!Number.isFinite(oldPx)) return null;

                const scale = getScale();
                const basePx = 11 * scale;
                const oldNormalPx = 13 * scale;
                // drawLabel() uses 13px for normal/selected labels and 13*1.2px for cities.
                // Any larger label is a city label.
                const isCity = oldPx > oldNormalPx * 1.08;
                const targetWeight = isCity ? 600 : 400;
                const targetPx = basePx * (isCity ? 1.20 : 1.0);
                return `${targetWeight} ${targetPx.toFixed(3)}px Roboto, Arial, sans-serif`;
            };

            CanvasRenderingContext2D.prototype.fillText = function(text, x, y, maxWidth) {
                const old = this.font;
                const next = normalizeLabelFont(this);
                if (next && next !== old) this.font = next;
                try {
                    return maxWidth === undefined
                        ? originalFillText.call(this, text, x, y)
                        : originalFillText.call(this, text, x, y, maxWidth);
                }
                finally {
                    if (next && next !== old) this.font = old;
                }
            };

            CanvasRenderingContext2D.prototype.strokeText = function(text, x, y, maxWidth) {
                const old = this.font;
                const next = normalizeLabelFont(this);
                if (next && next !== old) this.font = next;
                try {
                    return maxWidth === undefined
                        ? originalStrokeText.call(this, text, x, y)
                        : originalStrokeText.call(this, text, x, y, maxWidth);
                }
                finally {
                    if (next && next !== old) this.font = old;
                }
            };

            window.__ets2AssistPointFontPatchInstalled = true;
        }
    } catch (err) {
        console.warn('Map Editor 2 point font override failed', err);
    }
})();";
            try { await _webView.CoreWebView2.ExecuteScriptAsync(script); } catch { }
        }

        private void OpenTerrainFileInExplorer()
        {
            try
            {
                string path = TerrainPngPath;
                string directory = Path.GetDirectoryName(path) ?? AppDataPaths.StaticDataDirectory;
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
                }
                else
                {
                    Directory.CreateDirectory(directory);
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + directory + "\"", UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть расположение карты высот.\n\n" + ex.Message, "Карта высот", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task SendStaticPointFilesAsync()
        {
            if (!_pageReady || _webView.CoreWebView2 == null) return;
            var files = BuildStaticPointFileList();
            var json = JsonConvert.SerializeObject(files, Formatting.None);
            await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SetStaticPointFiles({json});");
        }

        private IReadOnlyList<string> BuildStaticPointFileList()
        {
            try
            {
                string root = Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data");
                if (!Directory.Exists(root)) return Array.Empty<string>();
                return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .Where(path => !string.Equals(Path.GetFileName(path), "meta.json", StringComparison.OrdinalIgnoreCase))
                    .Select(path => Path.GetRelativePath(AppDataPaths.StaticDataDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private async Task SendTargetsAsync()
        {
            if (!_pageReady || _webView.CoreWebView2 == null) return;
            var escaped = JsonConvert.ToString(_targetSnapshotJson);
            await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SetTargets(JSON.parse({escaped}));");
        }

        private string BuildTargetSnapshotJson()
        {
            try
            {
                var path = AppDataPaths.CustomTargetsFile;
                if (!File.Exists(path)) return "[]";
                var root = JToken.Parse(File.ReadAllText(path));
                var array = root is JArray ja ? ja : root["customTargets"] as JArray;
                if (array == null) return "[]";
                var result = new List<object>();
                foreach (var t in array)
                {
                    if (t.Type != JTokenType.Object) continue;
                    if (!double.TryParse((string?)t["x"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x))
                        x = t["x"]?.Value<double?>() ?? double.NaN;
                    if (!double.TryParse((string?)t["z"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                        z = t["z"]?.Value<double?>() ?? double.NaN;
                    if (double.IsNaN(x) || double.IsNaN(z)) continue;
                    var id = (string?)t["gameName"] ?? (string?)t["id"] ?? "";
                    var name = (string?)t["realName"] ?? (string?)t["name"] ?? id;
                    var color = (string?)t["color"] ?? "default";
                    result.Add(new { id, name, x, z, color });
                }
                return JsonConvert.SerializeObject(result, Formatting.None);
            }
            catch { return "[]"; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
