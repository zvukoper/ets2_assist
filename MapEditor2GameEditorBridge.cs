using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Reads the camera coordinates from the ETS2 "Map editor" window through UI Automation.
    /// The reader is intentionally independent from MainForm/MapEditor2Form so Map Editor 2
    /// can consume the live coordinates without coupling the editor UI to the game process.
    /// </summary>
    internal static class MapEditor2GameEditorBridge
    {
        private sealed record EditorPosition(double X, double Y, double Z);

        private static readonly object Sync = new();
        private static readonly List<WeakReference<WebView2>> WebViews = new();
        private static readonly Regex CoordinateRegex = new(
            @"\[\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static EditorPosition? _latest;
        private static bool _initialized;
        private static int _scriptSequence;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [ModuleInitializer]
        internal static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            Application.Idle += OnApplicationIdle;

            var thread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "MapEditor2.GameEditorCoordinateReader"
            };
            thread.Start();
        }

        private static void OnApplicationIdle(object? sender, EventArgs e)
        {
            try
            {
                foreach (Form form in Application.OpenForms)
                {
                    if (!string.Equals(form.GetType().Name, "MapEditor2Form", StringComparison.Ordinal))
                        continue;

                    var webView = FindWebView(form);
                    if (webView == null || webView.IsDisposed)
                        continue;

                    RegisterWebView(form, webView);
                }
            }
            catch
            {
                // The bridge is auxiliary UI functionality. Never let it affect the main app loop.
            }
        }

        private static WebView2? FindWebView(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is WebView2 webView)
                    return webView;

                var nested = FindWebView(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static void RegisterWebView(Form form, WebView2 webView)
        {
            lock (Sync)
            {
                for (int i = WebViews.Count - 1; i >= 0; i--)
                {
                    if (!WebViews[i].TryGetTarget(out var existing) || existing.IsDisposed)
                        WebViews.RemoveAt(i);
                }

                foreach (var reference in WebViews)
                {
                    if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, webView))
                        return;
                }

                WebViews.Add(new WeakReference<WebView2>(webView));
            }

            if (webView.CoreWebView2 != null)
            {
                InstallPageBridge(form, webView);
            }
            else
            {
                EventHandler<CoreWebView2InitializationCompletedEventArgs>? handler = null;
                handler = (_, _) =>
                {
                    try
                    {
                        webView.CoreWebView2InitializationCompleted -= handler;
                        InstallPageBridge(form, webView);
                    }
                    catch
                    {
                    }
                };
                webView.CoreWebView2InitializationCompleted += handler;
            }
        }

        private static void InstallPageBridge(Form form, WebView2 webView)
        {
            if (webView.CoreWebView2 == null || webView.IsDisposed)
                return;

            int sequence = Interlocked.Increment(ref _scriptSequence);
            string script = BuildPageBridgeScript(sequence);

            try
            {
                _ = webView.CoreWebView2.ExecuteScriptAsync(script);

                // Push the already-known position immediately after bridge installation.
                var latest = Volatile.Read(ref _latest);
                if (latest != null)
                    PushPosition(form, webView, latest);
            }
            catch
            {
            }
        }

        private static string BuildPageBridgeScript(int sequence)
        {
            return $$"""
(() => {
    try {
        const markerId = 'ets2assist-game-editor-marker';
        const infoId = 'ets2assist-game-editor-info';
        const sepId = 'ets2assist-game-editor-sep';
        const btnId = 'ets2assist-game-editor-center';

        window.__ets2AssistGameEditorBridgeInstalled = true;
        window.__ets2AssistGameEditorBridgeLatest = null;

        const map = document.getElementById('map');
        const interaction = document.getElementById('interaction');
        const status = document.getElementById('status');
        const cursorInfo = document.getElementById('cursorInfo');
        if (!map || !interaction || !status || !cursorInfo) return;

        let marker = document.getElementById(markerId);
        if (!marker) {
            marker = document.createElement('div');
            marker.id = markerId;
            marker.style.cssText = 'position:absolute;z-index:20;display:none;width:20px;height:20px;transform:translate(-50%,-50%);pointer-events:auto;cursor:pointer;';
            marker.innerHTML = '<div style="position:absolute;left:1px;right:1px;top:9px;height:2px;background:#fff;box-shadow:0 0 3px #000"></div>' +
                               '<div style="position:absolute;top:1px;bottom:1px;left:9px;width:2px;background:#fff;box-shadow:0 0 3px #000"></div>' +
                               '<div style="position:absolute;left:0;top:-22px;transform:translateX(-50%);white-space:nowrap;color:#fff;font:400 11px Roboto,Arial,sans-serif;text-shadow:0 0 4px #000;">Новая точка</div>';
            marker.addEventListener('pointerdown', e => {
                e.preventDefault();
                e.stopPropagation();
            }, true);
            marker.addEventListener('click', e => {
                e.preventDefault();
                e.stopPropagation();
                const p = window.__ets2AssistGameEditorBridgeLatest;
                if (!p) return;

                // Reuse the editor's normal "Добавить" click path so the right-hand
                // edit panel is populated with exactly the same fields/handlers as a
                // regular empty-map click.
                const addButton = document.querySelector('[data-tool="add"]');
                if (!addButton) return;
                const oldToolButton = document.querySelector('[data-tool].active');
                const oldTool = oldToolButton?.getAttribute('data-tool') || 'select';
                addButton.click();

                const r = map.getBoundingClientRect();
                const tr = getTransform();
                if (!tr) return;
                const p0 = probe(tr.cx, tr.cy);
                restoreMouse();
                if (!p0) return;
                const sx = tr.cx + (p.x - p0.x) / tr.mppX;
                const sy = tr.cy + (p.z - p0.z) / tr.mppZ;
                const down = new PointerEvent('pointerdown', {bubbles:true,cancelable:true,isPrimary:true,pointerId:7781,pointerType:'mouse',button:0,buttons:1,clientX:r.left+sx,clientY:r.top+sy});
                interaction.dispatchEvent(down);
                const up = new PointerEvent('pointerup', {bubbles:true,cancelable:true,isPrimary:true,pointerId:7781,pointerType:'mouse',button:0,buttons:0,clientX:r.left+sx,clientY:r.top+sy});
                interaction.dispatchEvent(up);

                // Restore the visual tool selection. A second select click would toggle
                // multi-select, so restore only the active class instead of invoking its handler.
                document.querySelectorAll('[data-tool]').forEach(b => b.classList.toggle('active', b.getAttribute('data-tool') === oldTool));
            });
            map.appendChild(marker);
        }

        let info = document.getElementById(infoId);
        if (!info) {
            info = document.createElement('span');
            info.id = infoId;
            info.style.cssText = 'display:none;color:#b7ff46;white-space:nowrap;font-variant-numeric:tabular-nums;';
            status.insertBefore(info, cursorInfo);
        }

        let sep = document.getElementById(sepId);
        if (!sep) {
            sep = document.createElement('span');
            sep.id = sepId;
            sep.textContent = '|';
            sep.style.cssText = 'display:none;margin:0 8px;color:#66717f;';
            status.insertBefore(sep, cursorInfo);
        }

        let centerButton = document.getElementById(btnId);
        if (!centerButton) {
            centerButton = document.createElement('button');
            centerButton.id = btnId;
            centerButton.type = 'button';
            centerButton.textContent = '⌖';
            centerButton.title = 'Перейти к координатам редактора ETS2';
            centerButton.style.cssText = 'display:none;width:24px;height:22px;margin:0 5px 0 4px;padding:0;border:1px solid rgba(150,165,185,.25);border-radius:3px;background:#202732;color:#b7ff46;cursor:pointer;font:400 16px Segoe UI,Arial,sans-serif;line-height:18px;';
            status.insertBefore(centerButton, cursorInfo);
            centerButton.addEventListener('click', e => {
                e.preventDefault();
                e.stopPropagation();
                window.__ets2AssistGameEditorCenter?.();
            });
        }

        let lastMouse = null;
        let mouseButtons = 0;
        interaction.addEventListener('pointermove', e => {
            if (e.isTrusted) {
                lastMouse = {x:e.clientX, y:e.clientY};
                mouseButtons = e.buttons || 0;
            }
        }, true);
        interaction.addEventListener('pointerdown', e => { if (e.isTrusted) mouseButtons = e.buttons || 1; }, true);
        interaction.addEventListener('pointerup', e => { if (e.isTrusted) mouseButtons = e.buttons || 0; }, true);
        window.addEventListener('pointerup', e => { if (e.isTrusted) mouseButtons = 0; }, true);

        const parseCursor = text => {
            const m = String(text || '').match(/X=\s*([-+]?\d+(?:\.\d+)?)\s+Y=\s*([-+]?\d+(?:\.\d+)?)\s+Z=\s*([-+]?\d+(?:\.\d+)?)/);
            return m ? {x:Number(m[1]), y:Number(m[2]), z:Number(m[3])} : null;
        };

        const probe = (x, y) => {
            const ev = new PointerEvent('pointermove', {
                bubbles:true,
                cancelable:true,
                pointerId:9991,
                pointerType:'mouse',
                clientX:x,
                clientY:y,
                buttons:0
            });
            interaction.dispatchEvent(ev);
            return parseCursor(cursorInfo.textContent);
        };

        const restoreMouse = () => {
            if (!lastMouse) return;
            try {
                const ev = new PointerEvent('pointermove', {
                    bubbles:true,
                    cancelable:true,
                    pointerId:9992,
                    pointerType:'mouse',
                    clientX:lastMouse.x,
                    clientY:lastMouse.y,
                    buttons:mouseButtons
                });
                interaction.dispatchEvent(ev);
            } catch {}
        };

        const getTransform = () => {
            if (mouseButtons) return null;
            const r = map.getBoundingClientRect();
            const cx = r.width / 2;
            const cy = r.height / 2;
            const p0 = probe(cx, cy);
            const px = probe(Math.min(r.width - 2, cx + 100), cy);
            const py = probe(cx, Math.min(r.height - 2, cy + 100));
            restoreMouse();
            if (!p0 || !px || !py) return null;
            const dx = px.x - p0.x;
            const dz = py.z - p0.z;
            if (!Number.isFinite(dx) || !Number.isFinite(dz) || Math.abs(dx) < 0.000001 || Math.abs(dz) < 0.000001) return null;
            return {cx,cy,mppX:dx/100,mppZ:dz/100};
        };

        const render = () => {
            const p = window.__ets2AssistGameEditorBridgeLatest;
            if (!p) {
                info.style.display = 'none';
                sep.style.display = 'none';
                centerButton.style.display = 'none';
                marker.style.display = 'none';
                return;
            }

            info.textContent = `Редактор: X=${p.x.toFixed(2)} Y=${p.y.toFixed(2)} Z=${p.z.toFixed(2)}`;
            info.style.display = 'inline';
            sep.style.display = 'inline';
            centerButton.style.display = 'inline-block';

            const tr = getTransform();
            if (!tr) return;
            const sr = map.getBoundingClientRect();
            const p0 = probe(tr.cx, tr.cy);
            restoreMouse();
            if (!p0) return;
            const x = tr.cx + (p.x - p0.x) / tr.mppX;
            const y = tr.cy + (p.z - p0.z) / tr.mppZ;
            marker.style.left = `${x}px`;
            marker.style.top = `${y}px`;
            marker.style.display = (x >= -24 && x <= sr.width + 24 && y >= -24 && y <= sr.height + 24) ? 'block' : 'none';
        };

        window.__ets2AssistGameEditorSet = (x, y, z) => {
            window.__ets2AssistGameEditorLatest = {x:Number(x),y:Number(y),z:Number(z)};
            window.__ets2AssistGameEditorBridgeLatest = window.__ets2AssistGameEditorLatest;
            render();
        };

        window.__ets2AssistGameEditorClear = () => {
            window.__ets2AssistGameEditorLatest = null;
            window.__ets2AssistGameEditorBridgeLatest = null;
            info.style.display = 'none';
            sep.style.display = 'none';
            centerButton.style.display = 'none';
            marker.style.display = 'none';
        };

        window.__ets2AssistGameEditorCenter = () => {
            const p = window.__ets2AssistGameEditorLatest;
            if (!p || mouseButtons) return;
            const tr = getTransform();
            if (!tr) return;
            const p0 = probe(tr.cx, tr.cy);
            restoreMouse();
            if (!p0) return;
            const targetX = tr.cx + (p.x - p0.x) / tr.mppX;
            const targetY = tr.cy + (p.z - p0.z) / tr.mppZ;
            const dx = tr.cx - targetX;
            const dy = tr.cy - targetY;
            const down = new PointerEvent('pointerdown', {bubbles:true,cancelable:true,pointerId:7771,pointerType:'mouse',button:2,buttons:2,clientX:tr.cx,clientY:tr.cy});
            interaction.dispatchEvent(down);
            const move = new PointerEvent('pointermove', {bubbles:true,cancelable:true,pointerId:7771,pointerType:'mouse',button:2,buttons:2,clientX:tr.cx+dx,clientY:tr.cy+dy});
            interaction.dispatchEvent(move);
            const up = new PointerEvent('pointerup', {bubbles:true,cancelable:true,pointerId:7771,pointerType:'mouse',button:2,buttons:0,clientX:tr.cx+dx,clientY:tr.cy+dy});
            interaction.dispatchEvent(up);
            render();
        };

        if (!window.__ets2AssistGameEditorTimer) {
            window.__ets2AssistGameEditorTimer = setInterval(() => {
                if (window.__ets2AssistGameEditorBridgeLatest) render();
            }, 1000);
        }
    } catch (e) {
        console.warn('Map Editor 2 game editor bridge failed', e);
    }
})();
""";
        }

        private static void PollLoop()
        {
            while (true)
            {
                try
                {
                    var position = ReadEditorPosition();
                    Volatile.Write(ref _latest, position);
                    PushPositionToWebViews(position);
                }
                catch
                {
                    Volatile.Write(ref _latest, null);
                    PushPositionToWebViews(null);
                }

                Thread.Sleep(1000);
            }
        }

        private static EditorPosition? ReadEditorPosition()
        {
            var processIds = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("eurotrucks2"))
            {
                try { processIds.Add((uint)process.Id); } catch { }
                process.Dispose();
            }

            if (processIds.Count == 0)
                return null;

            IntPtr editorWindow = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!processIds.Contains(pid))
                    return true;

                string title = GetWindowTitle(hWnd);
                if (title.IndexOf("Map editor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    editorWindow = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (editorWindow == IntPtr.Zero)
                return null;

            try
            {
                var root = AutomationElement.FromHandle(editorWindow);
                if (root == null)
                    return null;

                var paneCondition = new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBar.Pane2", PropertyConditionFlags.IgnoreCase),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

                var pane = root.FindFirst(TreeScope.Descendants, paneCondition);
                if (pane == null)
                {
                    // Fallback: search by control name/value containing the coordinate brackets.
                    var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                    foreach (AutomationElement candidate in root.FindAll(TreeScope.Descendants, editCondition))
                    {
                        string text = GetAutomationText(candidate);
                        if (CoordinateRegex.IsMatch(text))
                            return ParsePosition(text);
                    }

                    return null;
                }

                return ParsePosition(GetAutomationText(pane));
            }
            catch
            {
                return null;
            }
        }

        private static string GetAutomationText(AutomationElement element)
        {
            try
            {
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject) && valuePatternObject is ValuePattern valuePattern)
                    return valuePattern.Current.Value ?? element.Current.Name ?? string.Empty;
            }
            catch
            {
            }

            try { return element.Current.Name ?? string.Empty; } catch { return string.Empty; }
        }

        private static EditorPosition? ParsePosition(string text)
        {
            var match = CoordinateRegex.Match(text ?? string.Empty);
            if (!match.Success)
                return null;

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                return null;

            return new EditorPosition(x, y, z);
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLengthSafe(hWnd);
                if (length <= 0)
                    return string.Empty;

                var buffer = new char[length + 1];
                int count = GetWindowText(hWnd, buffer, buffer.Length);
                return count > 0 ? new string(buffer, 0, count) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private static int GetWindowTextLengthSafe(IntPtr hWnd)
        {
            try { return GetWindowTextLength(hWnd); } catch { return 0; }
        }

        private static void PushPositionToWebViews(EditorPosition? position)
        {
            WeakReference<WebView2>[] views;
            lock (Sync)
                views = WebViews.ToArray();

            foreach (var reference in views)
            {
                if (!reference.TryGetTarget(out var webView) || webView.IsDisposed || webView.CoreWebView2 == null)
                    continue;

                if (webView.FindForm() is not Form form || form.IsDisposed)
                    continue;

                PushPosition(form, webView, position);
            }
        }

        private static void PushPosition(Form form, WebView2 webView, EditorPosition? position)
        {
            try
            {
                if (!form.IsDisposed && form.IsHandleCreated)
                {
                    form.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (webView.IsDisposed || webView.CoreWebView2 == null)
                                return;

                            if (position == null)
                            {
                                await webView.CoreWebView2.ExecuteScriptAsync(
                                    "window.__ets2AssistGameEditorClear?.();");
                            }
                            else
                            {
                                string x = position.X.ToString("R", CultureInfo.InvariantCulture);
                                string y = position.Y.ToString("R", CultureInfo.InvariantCulture);
                                string z = position.Z.ToString("R", CultureInfo.InvariantCulture);
                                await webView.CoreWebView2.ExecuteScriptAsync(
                                    $"window.__ets2AssistGameEditorSet?.({x},{y},{z});");
                            }
                        }
                        catch
                        {
                        }
                    }));
                }
            }
            catch
            {
            }
        }
    }
}
