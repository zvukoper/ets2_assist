using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    // Дополнительный защитный слой для Map Editor 2.
    // Не меняет существующий pipeline данных: исправляет только UI/bridge-краевые случаи,
    // которые нельзя надёжно исправить отдельным DOM-патчем в index.html без риска
    // потерять текущие изменения редактора.
    internal sealed partial class MapEditor2Form
    {
        private bool _map2BugfixNavigationHooked;
        private bool _map2BugfixFirstNavigation = true;
        private bool _map2BugfixAllowNextNavigation;

        // Поле-инициализатор выполняется до тела конструктора, поэтому наш Load-handler
        // регистрируется раньше существующего InitializeAsync из MapEditor2Form.cs.
        // На Load мы ставим NavigationStarting-hook до того, как существующий код задаст Source.
        private readonly bool _map2BugfixLoadHook = RegisterMap2BugfixLoadHook();

        private bool RegisterMap2BugfixLoadHook()
        {
            Load += OnMap2BugfixLoad;
            return true;
        }

        private void OnMap2BugfixLoad(object? sender, EventArgs e)
        {
            if (_map2BugfixNavigationHooked || _webView == null || _webView.IsDisposed) return;
            _map2BugfixNavigationHooked = true;
            _webView.NavigationStarting += OnMap2BugfixNavigationStarting;
        }

        // Первый navigation отменяем, регистрируем document-created script, после чего
        // повторяем ТОТ ЖЕ navigation. Это гарантирует, что preload установлен до запуска
        // page script, а не после появления DOM.
        private async void OnMap2BugfixNavigationStarting(
            object? sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
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
                {
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Map2BugfixPreloadScript);
                }
                _map2BugfixAllowNextNavigation = true;
                _webView.Source = new Uri(uri);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2FIX] Не удалось установить preload: " + ex.Message);
                // Даже если preload не установился, страницу всё равно не оставляем
                // заблокированной отменённой навигацией.
                _map2BugfixAllowNextNavigation = true;
                try { _webView.Source = new Uri(uri); } catch { }
            }
        }

        private const string Map2BugfixPreloadScript = @"
(() => {
    'use strict';
    try {
        if (window.__ets2AssistMap2BugfixInstalled) return;
        window.__ets2AssistMap2BugfixInstalled = true;

        // ================================================================
        // 1. Заголовок файла в «Сохранённых» — реальный clickable target.
        // C# уже имеет обработчик map2-open-override-file; здесь не хватает
        // только DOM-события на .savedFileHead.
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
                        file: file
                    }));
                }
                e.preventDefault();
                e.stopPropagation();
            } catch (_) { }
        }, true);

        // Визуально показываем, что заголовок файла — действие, а не простой текст.
        const headStyle = document.createElement('style');
        headStyle.textContent = `
            .savedFileHead { cursor: pointer; }
            .savedFileHead:hover { filter: brightness(1.18); }
        `;
        (document.head || document.documentElement).appendChild(headStyle);

        // ================================================================
        // 2. Поле «Категория».
        // В текущем index.html select создаётся, получает options и listeners,
        // но из-за пропущенного wrap.appendChild(ctrl) остаётся detached.
        // Нам нужен ИМЕННО этот detached DOM-node, потому что closure save-handler
        // уже держит ссылку на него через controls Map. Просто создать новый select
        // недостаточно — сохранение его не прочитает.
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

                    // Берём последний реально созданный .editInput/select, который
                    // ещё не подключён к DOM. В renderEditPanel это как раз select
                    // категории текущей точки.
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
        if (document.documentElement) {
            observer.observe(document.documentElement, { childList: true, subtree: true });
        }
        setTimeout(attachDetachedCategorySelect, 0);
        setTimeout(attachDetachedCategorySelect, 50);
        setTimeout(attachDetachedCategorySelect, 250);

        // ================================================================
        // 3. Нормализация save-command.
        // Это не заменяет C#-сохранение: передаём в существующий bridge тот же
        // command, но гарантируем, что ключевые значения действительно взяты
        // из текущей формы. Это закрывает edge cases новых точек и одновременно
        // сохраняет реальное отображаемое имя статичной точки.
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

                            // При любом сохранении, когда поле действительно заполнено,
                            // передаём его явно. Поэтому существующее статичное имя
                            // никогда не исчезает только из-за состава dirty-полей.
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
})();";
    }
}
