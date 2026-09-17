using System;
using System.Linq;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private bool _questEditorOverlayInjected;
        private static readonly System.Windows.Forms.Timer _questEditorOverlayPump = CreateQuestEditorOverlayPump();

        static MapEditor2Form()
        {
            _questEditorOverlayPump.Start();
        }

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
            return timer;
        }

        private void TryInjectQuestEditorOverlay()
        {
            if (_questEditorOverlayInjected || IsDisposed || !_pageReady || _webView.CoreWebView2 == null) return;
            try
            {
                _questEditorOverlayInjected = true;
                _ = _webView.CoreWebView2.ExecuteScriptAsync("(function(){if(document.getElementById('questEditorOverlayScript'))return;var s=document.createElement('script');s.id='questEditorOverlayScript';s.src='js/quest_editor.js';document.body.appendChild(s);})();");
            }
            catch { _questEditorOverlayInjected = false; }
        }
    }
}