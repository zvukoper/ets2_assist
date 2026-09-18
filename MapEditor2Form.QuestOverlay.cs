using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private bool _questEditorOverlayInjected;
        private bool _questEditorBridgeHooked;
        private static readonly System.Windows.Forms.Timer _questEditorOverlayPump = CreateQuestEditorOverlayPump();

        private static System.Windows.Forms.Timer CreateQuestEditorOverlayPump()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 600 };
            timer.Tick += (_, _) =>
            {
                try
                {
                    foreach (var form in Application.OpenForms.OfType<MapEditor2Form>())
                        form.TryInjectQuestEditorOverlay();
                }
                catch { }
            };
            timer.Start();
            return timer;
        }

        private void AttachQuestEditorBridgeHook()
        {
            if (_questEditorBridgeHooked || _webView.CoreWebView2 == null) return;
            _questEditorBridgeHooked = true;
            _webView.CoreWebView2.WebMessageReceived += OnQuestEditorBridgeMessage;
        }

        private async void OnQuestEditorBridgeMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.TryGetWebMessageAsString(), "map2-quest-bridge-ready", StringComparison.Ordinal)) return;
            await SendQuestEditorPointsAsync();
        }

        private async Task SendQuestEditorPointsAsync()
        {
            try
            {
                if (!_pageReady || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
                var runtime = Quests.QuestRuntime.Current;
                var questPoints = runtime?.GetQuestPointsForEditor() ?? Array.Empty<Quests.QuestPointSnapshot>();
                var baseJsonEscaped = JsonConvert.ToString(_targetSnapshotJson);
                var questJson = JsonConvert.SerializeObject(questPoints, Formatting.None);
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2InstallQuestPoints && window.MapEditor2InstallQuestPoints(JSON.parse({baseJsonEscaped}), {questJson});");
            }
            catch (Exception ex)
            {
                Logger.Current?.Data("[MAP2][QUEST] Не удалось передать квестовые точки в редактор: " + ex.Message);
            }
        }

        private void TryInjectQuestEditorOverlay()
        {
            if (IsDisposed || !_pageReady || _webView.CoreWebView2 == null) return;
            try
            {
                AttachQuestEditorBridgeHook();
                // v1.0.40.57: САМОЛЕЧЕНИЕ. Единственной точкой входа был флаг
                // `_questEditorOverlayInjected`; после перезагрузки страницы он
                // оставался true → скрипт НЕ подключался заново и категория
                // «Квестовые» исчезала из сайдбара. Теперь решение принимает САМА
                // страница (проверяет, есть ли уже элемент script), поэтому вызов
                // идемпотентен и повторяется насосом (600 мс), пока страница жива.
                // Стоимость — один ExecuteScriptAsync раз в 600 мс.
                _questEditorOverlayInjected = true;
                _ = _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){if(document.getElementById('questEditorOverlayScript'))return;" +
                    "var s=document.createElement('script');s.id='questEditorOverlayScript';s.src='../js/quest_editor.js';" +
                    "document.body.appendChild(s);})();");
            }
            catch { _questEditorOverlayInjected = false; }
        }
    }
}
