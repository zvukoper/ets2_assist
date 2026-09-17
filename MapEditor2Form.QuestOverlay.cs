using System;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private System.Windows.Forms.Timer? _questEditorOverlayTimer;
        private bool _questEditorOverlayInjected;

        private void InitializeQuestEditorOverlay()
        {
            if (_questEditorOverlayTimer != null) return;
            _questEditorOverlayTimer = new System.Windows.Forms.Timer { Interval = 600 };
            _questEditorOverlayTimer.Tick += (_, _) =>
            {
                if (_questEditorOverlayInjected || IsDisposed || !_pageReady || _webView.CoreWebView2 == null) return;
                try
                {
                    _questEditorOverlayInjected = true;
                    _ = _webView.CoreWebView2.ExecuteScriptAsync("(function(){if(document.getElementById('questEditorOverlayScript'))return;var s=document.createElement('script');s.id='questEditorOverlayScript';s.src='js/quest_editor.js';document.body.appendChild(s);})();");
                    _questEditorOverlayTimer?.Stop();
                }
                catch { _questEditorOverlayInjected = false; }
            };
            FormClosed += (_, _) =>
            {
                try { _questEditorOverlayTimer?.Stop(); _questEditorOverlayTimer?.Dispose(); } catch { }
            };
            _questEditorOverlayTimer.Start();
        }
    }
}