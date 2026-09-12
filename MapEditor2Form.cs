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
        // v1.0.40.20: телеметрия фуры (грузовик + конус обзора на карте).
        // v1.0.40.25: System.Windows.Forms.Timer, созданный внутри async InitializeAsync,
        // НЕ тикал (heartbeat — 0 записей за всю историю), поэтому метка обновлялась один
        // раз при загрузке страницы и дальше стояла. Заменён на System.Threading.Timer
        // (не зависит от очереди сообщений WinForms) + BeginInvoke на UI-поток для WebView2.
        private System.Threading.Timer? _truckTimer;
        private int _truckPushTick;
        private bool _truckPushQueued;
        private long _lastTruckRevision = -1;
        private bool _lastTruckLive;
        private bool _truckSent;
        private bool _lastTruckLiveLogged;   // для лога переходов live->offline (без спама)

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
            Load += async (_, _) => await InitializeAsync();
            FormClosed += (_, _) =>
            {
                try { _truckTimer?.Dispose(); _truckTimer = null; } catch { }
                try { _editorStatusTimer?.Stop(); _editorStatusTimer?.Dispose(); _editorStatusTimer = null; } catch { }
                // Фид телеметрии не должен висеть после закрытия редактора.
                try { TruckTelemetry.Stop(); } catch { }
                try { _webView.Dispose(); } catch { }
            };
        }

        private async Task InitializeAsync()
        {
            AppDataPaths.EnsureUserData();
            _targetSnapshotJson = BuildTargetSnapshotJson();
            await _webView.EnsureCoreWebView2Async();
            AttachEditingBridge();
            AttachOverridesBridge();            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ets2assist-map.local",
                AppDataPaths.StaticDataDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri("https://ets2assist-map.local/map_editor2/index.html");
            _editorStatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _editorStatusTimer.Tick += (_, _) => UpdateEditorStatusIndicator();
            _editorStatusTimer.Start();
            // v1.0.40.20: фид телеметрии фуры живёт, пока открыт редактор.
            TruckTelemetry.Start();
            // v1.0.40.26: интервал берётся из настроек (задаётся полем в редакторе).
            _truckIntervalMs = Math.Clamp(AppSettings.TruckIntervalMs, 100, 60000);
            _truckTimer = new System.Threading.Timer(_ => QueueTruckPush(), null, _truckIntervalMs, _truckIntervalMs);
            Logger.Current?.Data($"[TRUCK] редактор 2: таймер телеметрии запущен ({_truckIntervalMs} мс, System.Threading.Timer)");
        }

        // Планирует отправку на UI-поток (WebView2 вызываем только из него).
        // Флаг не даёт скапливать очередь, если UI занят.
        private void QueueTruckPush()
        {
            try
            {
                if (_truckPushQueued || IsDisposed || !IsHandleCreated) return;
                _truckPushQueued = true;
                BeginInvoke(new Action(() =>
                {
                    _truckPushQueued = false;
                    PushTruckTelemetry();
                }));
            }
            catch { _truckPushQueued = false; }
        }

        // Отправляет снимок телеметрии в страницу РАЗ В СЕКУНДУ (без гейта по revision —
        // именно его отсутствие давало «метка стоит на месте»).
        private void PushTruckTelemetry()
        {
            try
            {
                _truckPushTick++;
                // Диагностика: первые тики + каждые 30 с — видно, что таймер живёт.
                if (_truckPushTick <= 3 || _truckPushTick % 30 == 0)
                {
                    Logger.Current?.Data($"[TRUCK] редактор 2: тик #{_truckPushTick} " +
                        $"webView={(!_webView.IsDisposed && _webView.CoreWebView2 != null)} " +
                        $"pageReady={_pageReady} live={TruckTelemetry.IsLive}");
                }
                if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
                if (!TruckTelemetry.TryGetSnapshot(out var snap, out _, out bool haveSample) || !haveSample)
                    return;   // валидных данных ещё не было — отправлять нечего

                if (!_truckSent || snap.Live != _lastTruckLiveLogged)
                {
                    _lastTruckLiveLogged = snap.Live;
                    _truckSent = true;
                    Logger.Current?.Data(
                        $"[TRUCK] -> карта: x={snap.X:F1} z={snap.Z:F1} h={snap.Heading:F3} live={snap.Live}");
                }
                _lastTruckRevision = 0;
                _lastTruckLive = snap.Live;

                string s = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{{x:{0:R},y:{1:R},z:{2:R},heading:{3:R},headYaw:{4:R},headPitch:{5:R},live:{6}}}",
                    snap.X, snap.Y, snap.Z, snap.Heading, snap.HeadYaw, snap.HeadPitch,
                    snap.Live ? "true" : "false");

                _ = _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SetTruck && window.MapEditor2SetTruck({s});");
            }
            catch (Exception ex)
            {
                Logger.Current?.Data("[TRUCK] редактор 2: ошибка отправки: " + ex.Message);
            }
        }

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
                Logger.Current?.Data("[TRUCK] редактор 2: страница готова (map2-ready) — сбрасываю счётчик отправки");
                // Страница перезагрузилась: снимок нужно отправить заново, даже если
                // revision не менялся (иначе метка не появится до следующего изменения).
                _lastTruckRevision = -1;
                _truckSent = false;
                PushTruckTelemetry();
                _lastEditorRunning = !MapEditor2GameEditorBridge.IsEditorRunning();
                UpdateEditorStatusIndicator();
                await ApplyMapEditor2UiOverridesAsync();
                await SendBuildVersionAsync();
                await SendEditorHistoryAsync();
                await SendStaticPointFilesAsync();
                await SendTargetsAsync();
                await SendOverridesToEditorAsync();
                await SendTruckIntervalAsync();
                return;
            }
            if (string.Equals(message, "map2-generate-terrain", StringComparison.Ordinal))
            {
                await GenerateTerrainAsync();
                return;
            }
            if (string.Equals(message, "map2-data-ready", StringComparison.Ordinal))
            {
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
                    var cmdType = (string?)cmd["type"];
                    if (string.Equals(cmdType, "map2-create-point", StringComparison.Ordinal))
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
                    else if (string.Equals(cmdType, "map2-truck-interval", StringComparison.Ordinal))
                    {
                        // v1.0.40.26: интервал обновления метки задаётся из UI редактора.
                        int ms = cmd["ms"]?.Value<int>() ?? DefaultTruckIntervalMs;
                        SetTruckInterval(ms, persist: true);
                    }
                }
                catch { }
            }
        }

        // v1.0.40.26: интервал обновления метки грузовика (мс).
        // Ограничение 100..60000 защищает от нуля/мусора из поля ввода.
        private const int DefaultTruckIntervalMs = 1000;
        private int _truckIntervalMs = DefaultTruckIntervalMs;

        internal void SetTruckInterval(int ms, bool persist)
        {
            int clamped = Math.Clamp(ms, 100, 60000);
            if (clamped == _truckIntervalMs && _truckTimer != null) return;
            _truckIntervalMs = clamped;
            try
            {
                _truckTimer?.Change(clamped, clamped);
                Logger.Current?.Data($"[TRUCK] редактор 2: интервал обновления = {clamped} мс");
            }
            catch (Exception ex)
            {
                Logger.Current?.Data("[TRUCK] редактор 2: ошибка смены интервала: " + ex.Message);
            }
            if (persist)
            {
                try { AppSettings.TruckIntervalMs = clamped; AppSettings.Save(); } catch { }
            }
        }

        // Применяет сохранённое значение и отправляет его на страницу, чтобы поле ввода
        // показывало актуальное число (а не дефолт).
        private async Task SendTruckIntervalAsync()
        {
            try
            {
                SetTruckInterval(AppSettings.TruckIntervalMs, persist: false);
                if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
                var ms = _truckIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2SetTruckInterval && window.MapEditor2SetTruckInterval({ms});");
            }
            catch { }
        }

        private async Task ApplyMapEditor2UiOverridesAsync()
        {
            if (_webView.CoreWebView2 == null) return;
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
            try { await _webView.CoreWebView2.ExecuteScriptAsync(script); } catch { }
        }

        private async Task SendBuildVersionAsync()
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            try
            {
                var json = JsonConvert.ToString(BuildInfo.Version);
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2SetBuildVersion && window.MapEditor2SetBuildVersion(JSON.parse({json}));");
            }
            catch { }
        }

        private async Task SendEditorHistoryAsync()
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            try
            {
                AppDataPaths.EnsureUserData();
                var path = AppDataPaths.GameEditorPointsHistoryFile;
                if (!File.Exists(path))
                    File.WriteAllText(path, "{\n  \"points\": []\n}\n", new System.Text.UTF8Encoding(false));

                JArray points;
                try
                {
                    var token = JToken.Parse(File.ReadAllText(path));
                    points = token as JArray ?? token["points"] as JArray ?? new JArray();
                }
                catch
                {
                    points = new JArray();
                }

                foreach (var item in points.OfType<JObject>())
                {
                    var gn = ((string?)item["gameName"] ?? "").Trim();
                    var rn = ((string?)item["realName"] ?? "").Trim();
                    if (!string.IsNullOrEmpty(gn) && string.IsNullOrEmpty(rn))
                        item["realName"] = gn;
                }

                var json = JsonConvert.ToString(points.ToString(Newtonsoft.Json.Formatting.None));
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2SetEditorHistory && window.MapEditor2SetEditorHistory(JSON.parse({json}));");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2HISTORY] Ошибка загрузки истории: " + ex.Message);
            }
        }

        private void OpenTerrainFileInExplorer()
        {
            try
            {
                string path = TerrainPngPath;
                string directory = Path.GetDirectoryName(path) ?? AppDataPaths.StaticDataDirectory;
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
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
