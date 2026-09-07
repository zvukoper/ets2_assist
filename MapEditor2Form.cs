using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
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
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ets2assist-map.local",
                AppDataPaths.StaticDataDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            _webView.CoreWebView2.AddWebResourceRequestedFilter(
                "https://ets2assist-map.local/map_editor2/index.html",
                CoreWebView2WebResourceContext.Document);
            _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri("https://ets2assist-map.local/map_editor2/index.html");
        }

        private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var stream = new MemoryStream();
                string htmlPath = Path.Combine(AppDataPaths.StaticDataDirectory, "map_editor2", "index.html");
                string html = PrepareMapEditor2Html(File.ReadAllText(htmlPath));
                byte[] bytes = Encoding.UTF8.GetBytes(html);
                stream.Write(bytes, 0, bytes.Length);
                stream.Position = 0;
                e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
            }
            catch { }
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            if (string.Equals(message, "map2-ready", StringComparison.Ordinal))
            {
                _pageReady = true;
                await InstallDebugGridAsync();
                await SendStaticPointFilesAsync();
                await SendTargetsAsync();
                return;
            }
            if (string.Equals(message, "map2-generate-terrain", StringComparison.Ordinal))
            {
                await GenerateTerrainAsync();
                return;
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                try
                {
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
