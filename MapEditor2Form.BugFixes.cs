using System;
using Microsoft.Web.WebView2.Core;

namespace ETS2_Assist_GUI
{
    // Дополнительный защитный слой для Map Editor 2.
    // Не вмешивается в навигацию WebView2: страница должна загружаться штатно.
    internal sealed partial class MapEditor2Form
    {
        private bool _map2BugfixNavigationHooked;

        private void RegisterMap2BugfixLoadHook()
        {
            Load -= OnMap2BugfixLoad;
            Load += OnMap2BugfixLoad;
        }

        private void OnMap2BugfixLoad(object? sender, EventArgs e)
        {
            if (_map2BugfixNavigationHooked || _webView.IsDisposed) return;
            _map2BugfixNavigationHooked = true;
            _webView.NavigationCompleted += OnMap2BugfixNavigationCompleted;
        }

        private async void OnMap2BugfixNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;

            _webView.NavigationCompleted -= OnMap2BugfixNavigationCompleted;

            if (!e.IsSuccess)
            {
                Logger.Current?.Warning("[MAP2FIX] Map Editor 2 navigation failed.");
                return;
            }

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(Map2BugfixRuntimeScript);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2FIX] Runtime JS fix failed: " + ex.Message);
            }
        }

        // Выполняется после штатной загрузки страницы. Навигация не отменяется и не повторяется.
        private const string Map2BugfixRuntimeScript = """
(() => {
    'use strict';
    try {
        if (window.__ets2AssistMap2BugfixInstalled) return;
        window.__ets2AssistMap2BugfixInstalled = true;

        // ================================================================
        // 1. Заголовок файла в «Сохранённых» становится кликабельным.
        // ================================================================
        document.addEventListener('click', function (e) {
            try {
                const target = e.target instanceof Element
                    ? e.target.closest('.savedFileHead')
                    : null;
                if (!target) return;

                let file = String(target.getAttribute('data-file') || target.textContent || '').trim();
                file = file.replace(/\s+\*\s*$/, '').trim();
                if (!file) return;

                const webview = window.chrome && window.chrome.webview;
                if (webview && typeof webview.postMessage === 'function') {
                    webview.postMessage(JSON.stringify({
                        type: 'map2-open-override-file',
                        file
                    }));
                }
                e.preventDefault();
                e.stopPropagation();
            } catch (_) { }
        }, true);

        const headStyle = document.createElement('style');
        headStyle.textContent = `
            .savedFileHead { cursor: pointer; }
            .savedFileHead:hover { filter: brightness(1.18); }
        `;
        (document.head || document.documentElement).appendChild(headStyle);

        // ================================================================
        // 2. Поле «Категория».
        // Оно создаётся renderEditPanel как detached select.
        // Перехватываем только будущие select и подключаем нужный к строке Category.
        // ================================================================
        const detachedSelects = new Set();
        const originalCreateElement = Document.prototype.createElement;
        Document.prototype.createElement = function(localName, options) {
            const el = originalCreateElement.call(this, localName, options);
            if (String(localName).toLowerCase() === 'select') detachedSelects.add(el);
            return el;
        };

        const attachDetachedCategorySelect = () => {
            try {
                const rows = document.querySelectorAll('.editRow[data-field-key="Category"]');
                for (const row of rows) {
                    const wrap = row.querySelector('.editFieldWrap');
                    if (!wrap) continue;
                    if (wrap.querySelector('select.editInput')) continue;

                    const candidates = Array.from(detachedSelects).filter(el =>
                        el && !el.isConnected &&
                        String(el.tagName).toLowerCase() === 'select' &&
                        el.classList.contains('editInput') &&
                        el.options && el.options.length > 0
                    );
                    if (!candidates.length) continue;

                    const ctrl = candidates[candidates.length - 1];
                    wrap.appendChild(ctrl);
                    detachedSelects.delete(ctrl);
                    break;
                }
            } catch (_) { }
        };

        const observer = new MutationObserver(() => {
            try { attachDetachedCategorySelect(); } catch (_) { }
        });
        if (document.documentElement) {
            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
        setTimeout(attachDetachedCategorySelect, 0);
        setTimeout(attachDetachedCategorySelect, 50);
        setTimeout(attachDetachedCategorySelect, 250);

        // ================================================================
        // 3. Сохранение override.
        // Перед отправкой команды в существующий C# bridge явно сохраняем ключевые значения формы.
        // ================================================================
        const webview = window.chrome && window.chrome.webview;
        if (webview && typeof webview.postMessage === 'function' && !webview.__ets2AssistSaveFixInstalled) {
            webview.__ets2AssistSaveFixInstalled = true;
            const originalPostMessage = webview.postMessage.bind(webview);

            const readField = key => {
                const el = document.querySelector(`[data-field-key="${key}"]`);
                if (!el) return '';
                return String(el.value ?? '');
            };

            const readNumber = key => {
                const n = Number(readField(key));
                return Number.isFinite(n) ? n : null;
            };

            webview.postMessage = function(message) {
                try {
                    if (typeof message === 'string') {
                        const cmd = JSON.parse(message);
                        if (cmd && cmd.type === 'map2-override-save') {
                            cmd.file = String(cmd.file || document.getElementById('saveFileSel')?.value || '').trim();

                            const fields = (cmd.fields && typeof cmd.fields === 'object')
                                ? { ...cmd.fields }
                                : {};

                            const gameName = String(
                                fields.gameName || cmd.gameName || readField('GameName') || ''
                            ).trim();
                            if (gameName) {
                                fields.gameName = gameName;
                                cmd.gameName = gameName;
                            }

                            const realName = readField('RealName').trim();
                            if (realName) fields.realName = realName;

                            const category = readField('Category').trim();
                            if (category) fields.category = category;

                            const x = readNumber('X');
                            const y = readNumber('Y');
                            const z = readNumber('Z');
                            if (x !== null && z !== null) {
                                fields.coords = `${x.toFixed(2)}, ${(y === null ? 0 : y).toFixed(2)}, ${z.toFixed(2)}`;
                            }

                            cmd.fields = fields;
                            message = JSON.stringify(cmd);
                        }
                    }
                } catch (_) { }

                return originalPostMessage(message);
            };
        }
    } catch (_) { }
})();
""";
    }
}
