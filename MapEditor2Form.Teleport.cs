using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private static readonly MapEditor2HotkeyFilter _hotkeyFilter = new();
        private static bool _hotkeyFilterInitialized;
        private static bool _hotkeyFilterInstalled;

        private static void InitializeMapEditor2Hotkeys()
        {
            if (_hotkeyFilterInitialized) return;
            _hotkeyFilterInitialized = true;
            if (!_hotkeyFilterInstalled)
            {
                Application.AddMessageFilter(_hotkeyFilter);
                _hotkeyFilterInstalled = true;
            }
        }

        private async Task HandleMapEditor2HotkeyAsync(bool editorTeleport)
        {
            try
            {
                if (_webView.CoreWebView2 == null) return;
                var raw = await _webView.CoreWebView2.ExecuteScriptAsync("window.MapEditor2GetTeleportTarget ? window.MapEditor2GetTeleportTarget() : null;");
                var json = JsonConvert.DeserializeObject<string>(raw);
                if (string.IsNullOrWhiteSpace(json)) return;
                var target = JObject.Parse(json);
                var x = target.Value<double?>("x") ?? 0d;
                var y = target.Value<double?>("y") ?? 0d;
                var z = target.Value<double?>("z") ?? 0d;

                // v1.0.40.17: телепорт в РЕДАКТОРЕ — камера ставится с юга от цели
                // (дистанция 7 м, высота +2 м), а не в саму координату точки.
                if (editorTeleport)
                    MainForm.MapEditor2TeleportEditor(
                        x,
                        y + MainForm.EditorCamHeightM,
                        z + MainForm.EditorCamDistanceM);
                else
                    await MainForm.MapEditor2TeleportGameAsync(x, y, z);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MapEditor2 teleport: " + ex.Message);
            }
        }

        private sealed class MapEditor2HotkeyFilter : IMessageFilter
        {
            private const int WM_HOTKEY = 0x0312;
            private const int HOTKEY_TELEPORT = 9010;
            private const int HOTKEY_TELEPORT_EDITOR = 9011;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_HOTKEY) return false;
                var id = m.WParam.ToInt32();
                if (id != HOTKEY_TELEPORT && id != HOTKEY_TELEPORT_EDITOR)
                    return false;

                foreach (Form form in Application.OpenForms)
                {
                    if (form is MapEditor2Form editor && !editor.IsDisposed)
                    {
                        _ = editor.HandleMapEditor2HotkeyAsync(id == HOTKEY_TELEPORT_EDITOR);
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
