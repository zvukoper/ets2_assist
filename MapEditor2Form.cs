using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Completely isolated map renderer prototype.
    /// No editor state, selection, sidebar, telemetry, drag/drop or WPF renderer.
    /// The browser canvas owns all pan/zoom/render input; WinForms only hosts WebView2
    /// and supplies the current target snapshot.
    /// </summary>
    internal sealed class MapEditor2Form : Form
    {
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
        private bool _pageReady;
        private string _targetSnapshotJson = "[]";

        public MapEditor2Form()
        {
            Text = "Редактор карты 2";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(15, 18, 23);
            Controls.Add(_webView);

            Load += async (_, _) => await InitializeAsync();
            FormClosed += (_, _) =>
            {
                try { _webView.Dispose(); } catch { }
            };
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

            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri("https://ets2assist-map.local/map_editor2/index.html");
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.TryGetWebMessageAsString(), "map2-ready", StringComparison.Ordinal))
                return;

            _pageReady = true;
            await SendTargetsAsync();
        }

        private async Task SendTargetsAsync()
        {
            if (!_pageReady || _webView.CoreWebView2 == null)
                return;

            var escaped = JsonConvert.ToString(_targetSnapshotJson);
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.MapEditor2SetTargets(JSON.parse({escaped}));");
        }

        private string BuildTargetSnapshotJson()
        {
            try
            {
                var path = AppDataPaths.CustomTargetsFile;
                if (!File.Exists(path))
                    return "[]";

                var root = JToken.Parse(File.ReadAllText(path));
                var array = root is JArray ja ? ja : root["customTargets"] as JArray;
                if (array == null)
                    return "[]";

                var result = new List<object>();
                foreach (var t in array)
                {
                    if (t.Type != JTokenType.Object)
                        continue;

                    if (!double.TryParse((string?)t["x"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x))
                        x = t["x"]?.Value<double?>() ?? double.NaN;
                    if (!double.TryParse((string?)t["z"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                        z = t["z"]?.Value<double?>() ?? double.NaN;
                    if (double.IsNaN(x) || double.IsNaN(z))
                        continue;

                    var id = (string?)t["gameName"] ?? (string?)t["id"] ?? "";
                    var name = (string?)t["realName"] ?? (string?)t["name"] ?? id;
                    var color = (string?)t["color"] ?? "default";
                    result.Add(new { id, name, x, z, color });
                }

                return JsonConvert.SerializeObject(result, Formatting.None);
            }
            catch
            {
                return "[]";
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
