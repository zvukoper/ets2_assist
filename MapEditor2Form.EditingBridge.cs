using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private bool _mapEditor2EditingHooked;

        private void AttachEditingBridge()
        {
            if (_mapEditor2EditingHooked || _webView.CoreWebView2 == null) return;
            _mapEditor2EditingHooked = true;
            _webView.CoreWebView2.WebMessageReceived += OnMapEditor2EditingMessage;
        }

        private async void OnMapEditor2EditingMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(message)) return;
            var trimmed = message.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return;

            try
            {
                var cmd = JObject.Parse(message);
                var type = (string?)cmd["type"];
                if (type == "map2-save-point")
                {
                    var point = cmd["point"] as JObject;
                    if (point == null) return;
                    var id = (string?)point["GameName"] ?? (string?)point["gameName"] ?? (string?)point["id"] ?? "";
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        MessageBox.Show(this, "Системное имя точки не может быть пустым.", "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    string path = AppDataPaths.CustomTargetsFile;
                    AppDataPaths.EnsureUserData();
                    JObject root;
                    if (File.Exists(path)) root = JObject.Parse(File.ReadAllText(path)); else root = new JObject();
                    if (root["customTargets"] is not JArray targets) { targets = new JArray(); root["customTargets"] = targets; }

                    var saved = new JObject
                    {
                        ["id"] = id,
                        ["gameName"] = id,
                        ["realName"] = (string?)point["RealName"] ?? (string?)point["name"] ?? id,
                        ["category"] = (string?)point["Category"] ?? "Пользовательское",
                        ["description"] = (string?)point["Description"] ?? "",
                        ["status"] = (point["Enabled"]?.Value<bool>() ?? true) ? "active" : "inactive",
                        ["enabled"] = point["Enabled"]?.Value<bool>() ?? true,
                        ["x"] = point["X"]?.Value<double>() ?? 0d,
                        ["y"] = point["Y"]?.Value<double>() ?? 0d,
                        ["z"] = point["Z"]?.Value<double>() ?? 0d,
                        ["coords"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}, {1:F2}, {2:F2}", point["X"]?.Value<double>() ?? 0d, point["Y"]?.Value<double>() ?? 0d, point["Z"]?.Value<double>() ?? 0d),
                        ["color"] = (string?)point["Color"] ?? "default",
                        ["icon"] = (string?)point["Icon"] ?? "default",
                        ["labelStroke"] = point["LabelStroke"]?.Value<double>() ?? 1d,
                        ["radius"] = point["TriggerRadius"]?.Value<double>() ?? 200d,
                        ["triggerRadius"] = point["TriggerRadius"]?.Value<double>() ?? 200d,
                        ["cooldown"] = point["CooldownMinutes"]?.Value<int>() ?? 0,
                        ["cooldownMinutes"] = point["CooldownMinutes"]?.Value<int>() ?? 0,
                        ["hidden"] = point["Hidden"]?.Value<int>() ?? 0,
                        ["delete_on_complete"] = point["DeleteOnComplete"]?.Value<int>() ?? 0,
                        ["dialogId"] = (string?)point["DialogId"] ?? "",
                        ["action"] = (string?)point["Action"] ?? "",
                        ["caption"] = (string?)point["Caption"] ?? "",
                        ["enterReward"] = point["EnterReward"]?.Value<int>() ?? 0,
                        ["afterReward"] = point["AfterReward"]?.Value<int>() ?? 0,
                        ["enterXp"] = point["EnterXp"]?.Value<int>() ?? 0,
                        ["afterXp"] = point["AfterXp"]?.Value<int>() ?? 0
                    };
                    for (int i = targets.Count - 1; i >= 0; i--)
                        if (string.Equals((string?)targets[i]?["gameName"] ?? (string?)targets[i]?["id"], id, StringComparison.Ordinal)) targets.RemoveAt(i);
                    targets.Add(saved);
                    File.WriteAllText(path, root.ToString(Formatting.Indented));
                    if (_webView.CoreWebView2 != null)
                        await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2Saved && window.MapEditor2Saved({JsonConvert.SerializeObject(saved.ToString(Formatting.None))});");
                    return;
                }

                if (type == "map2-delete-point")
                {
                    var id = (string?)cmd["gameName"] ?? "";
                    if (string.IsNullOrWhiteSpace(id)) return;
                    var path = AppDataPaths.CustomTargetsFile;
                    if (!File.Exists(path)) return;
                    var root = JObject.Parse(File.ReadAllText(path));
                    if (root["customTargets"] is not JArray targets) return;
                    for (int i = targets.Count - 1; i >= 0; i--)
                        if (string.Equals((string?)targets[i]?["gameName"] ?? (string?)targets[i]?["id"], id, StringComparison.Ordinal)) targets.RemoveAt(i);
                    File.WriteAllText(path, root.ToString(Formatting.Indented));
                    if (_webView.CoreWebView2 != null)
                        await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2Deleted && window.MapEditor2Deleted({JsonConvert.SerializeObject(id)});");
                }
            }
            catch (Exception ex)
            {
                try { MessageBox.Show(this, "Ошибка операции с точкой:\n\n" + ex.Message, "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        static MapEditor2Form()
        {
            InitializeMapEditor2Hotkeys();
        }
    }
}
