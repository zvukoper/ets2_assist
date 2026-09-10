using System;
using Microsoft.Web.WebView2.Core;

namespace ETS2_Assist_GUI
{
    // Дополнительный защитный слой для Map Editor 2.
    // Исправляет четыре краевых случая:
    // 1) клик по заголовку файла в «Сохранённых»;
    // 2) сохранение новой точки в выбранный override-файл;
    // 3) сохранение статичной точки без потери RealName;
    // 4) отображение/сохранение поля «Категория».
    internal sealed partial class MapEditor2Form
    {
        private bool _map2BugfixNavigationHooked;
        private bool _map2BugfixFirstNavigation = true;
        private bool _map2BugfixAllowNextNavigation;

        // Вызывается из конструктора основной части Form, до InitializeAsync.
        private void RegisterMap2BugfixLoadHook()
        {
            Load -= OnMap2BugfixLoad;
            Load += OnMap2BugfixLoad;
        }

        private void OnMap2BugfixLoad(object? sender, EventArgs e)
        {
            if (_map2BugfixNavigationHooked || _webView.IsDisposed) return;
            _map2BugfixNavigationHooked = true;
            _webView.NavigationStarting += OnMap2BugfixNavigationStarting;
        }

        // Ставим preload ДО запуска page scripts: первую навигацию отменяем,
        // регистрируем document-created script и повторяем ту же навигацию.
        private async void OnMap2BugfixNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_map2BugfixAllowNextNavigation)
            {
                _map2BugfixAllowNextNavigation = false;
                return;
            }

            if (!_map2BugfixFirstNavigation) return;
            _map2BugfixFirstNavigation = false;

            var uri = e.Uri;
            e.Cancel = true;

            try
            {
                if (_webView.CoreWebView2 != null)
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Map2BugfixPreloadScript);

                _map2BugfixAllowNextNavigation = true;
                _webView.Source = new Uri(uri);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2FIX] Не удалось установить preload: " + ex.Message);
                _map2BugfixAllowNextNavigation = true;
                try { _webView.Source = new Uri(uri); } catch { }
            }
        }

        // C# raw string literal: внутри JS не требуется экранировать кавычки.
        private const string Map2BugfixPreloadScript = """
(() => {
    'use strict';
    try {
        if (window.__ets2AssistMap2BugfixInstalled) return;
        window.__ets2AssistMap2BugfixInstalled = true;

        // ================================================================
        // 1. Заголовок файла в «Сохранённых» становится кликабельным.
        // C# уже обрабатывает команду map2-open-override-file.
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
        // В текущем renderEditPanel select создаётся, но не добавляется в DOM.
        // Важно подключить именно этот select: closure save-handler уже держит
        // его в Map controls.
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
                    if (wrap.querySelector('select[data-field-key="Category"]')) continue;

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
                }
            } catch (_) { }
        };

        const observer = new MutationObserver(() => {
            try { attachDetachedCategorySelect(); } catch (_) { }
        });
        if (document.documentElement)
            observer.observe(document.documentElement, { childList: true, subtree: true });

        setTimeout(attachDetachedCategorySelect, 0);
        setTimeout(attachDetachedCategorySelect, 50);
        setTimeout(attachDetachedCategorySelect, 250);

        // ================================================================
        // 3. Нормализация команды map2-override-save.
        // Гарантируем, что при сохранении заполненные GameName / RealName /
        // Category / координаты реально уходят в существующий C# bridge.
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
