using System;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private readonly System.Windows.Forms.Timer _questEditorOverlayTimer = CreateQuestEditorOverlayTimer();
        private bool _questEditorOverlayInjected;

        private System.Windows.Forms.Timer CreateQuestEditorOverlayTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 600 };
            timer.Tick += (_, _) =>
            {
                if (_questEditorOverlayInjected || IsDisposed || !_pageReady || _webView.CoreWebView2 == null) return;
                try
                {
                    _questEditorOverlayInjected = true;
                    _ = _webView.CoreWebView2.ExecuteScriptAsync("(function(){if(document.getElementById('questEditorOverlayScript'))return;var s=document.createElement('script');s.id='questEditorOverlayScript';s.src='https://ets2assist-map.local/js/quest_editor.js';document.body.appendChild(s);})();");
                    timer.Stop();
                }
                catch { _questEditorOverlayInjected = false; }
            };
            FormClosed += (_, _) => { try { timer.Stop(); timer.Dispose(); } catch { } };
            timer.Start();
            return timer;
        }
    }
}