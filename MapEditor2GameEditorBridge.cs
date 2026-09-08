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

namespace ETS2_Assist_GUI
{
    internal static class MapEditor2GameEditorBridge
    {
        private sealed record EditorPosition(double X, double Y, double Z);

        private static readonly object Sync = new();
        private static readonly object UiaSync = new();
        private static readonly Regex CoordinateRegex = new(
            @"\[\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ManualResetEventSlim StopEvent = new(false);

        // Горячий цикл намеренно короткий: координата игрового редактора должна попасть
        // в кэш практически на следующем UI-тикe, а горячая клавиша при этом читает
        // текущее значение напрямую из уже найденного UIA-контрола.
        private const int ActivePollMs = 16;
        private const int InactivePollMs = 1000;

        private static bool _initialized;
        private static bool _shutdown;
        private static Thread? _monitorThread;
        private static volatile bool _editorRunning;
        private static EditorPosition? _lastPosition;

        // Кэшируем HWND игрового Map editor и сам контрол координат.
        // Старый код заново перечислял процессы, окна и всё UI Automation-дерево КАЖДЫЕ
        // 100 мс — из-за этого «период в 100 мс» на практике превращался в большой lag.
        private static IntPtr _editorWindow;
        private static uint _editorProcessId;
        private static AutomationElement? _coordinateElement;
        private static ValuePattern? _coordinateValuePattern;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [ModuleInitializer]
        internal static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                _shutdown = false;
            }

            Log("Map Editor bridge: инициализация");
            Application.ApplicationExit += OnApplicationExit;

            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "MapEditor2.GameEditorMonitor"
            };
            _monitorThread.Start();
        }

        private static void OnApplicationExit(object? sender, EventArgs e) => Shutdown();

        private static void MonitorLoop()
        {
            while (!StopEvent.IsSet)
            {
                try
                {
                    if (!EnsureEditorWindow())
                    {
                        Volatile.Write(ref _editorRunning, false);
                        ClearCoordinateElement();
                        ClearLastPosition();
                        StopEvent.Wait(TimeSpan.FromMilliseconds(InactivePollMs));
                        continue;
                    }

                    Volatile.Write(ref _editorRunning, true);

                    // Читаем ТОЛЬКО уже найденный coordinate-control. Никаких повторных
                    // FindAll/FindFirst по дереву UI на каждом тике.
                    var pos = ReadCachedCoordinate();
                    if (pos != null)
                    {
                        lock (Sync) _lastPosition = pos;
                    }

                    StopEvent.Wait(TimeSpan.FromMilliseconds(ActivePollMs));
                }
                catch (Exception ex)
                {
                    LogDebug($"Map Editor bridge: цикл мониторинга: {ex.Message}");
                    StopEvent.Wait(TimeSpan.FromMilliseconds(50));
                }
            }
        }

        internal static bool IsEditorRunning() => Volatile.Read(ref _editorRunning);

        // Возвращает МАКСИМАЛЬНО АКТУАЛЬНУЮ координату.
        // Сначала делаем лёгкое чтение из уже найденного UIA-контрола — поэтому Ctrl+Shift+X
        // не зависит от того, когда именно завершился очередной 16-мс цикл кэша.
        // Если прямое чтение временно недоступно, отдаём последний подтверждённый snapshot.
        internal static (double X, double Y, double Z)? GetLastPosition()
        {
            if (!IsEditorRunning() && !EnsureEditorWindow())
                return null;

            var current = ReadCachedCoordinate();
            if (current != null)
            {
                lock (Sync) _lastPosition = current;
                return (current.X, current.Y, current.Z);
            }

            lock (Sync)
            {
                if (_lastPosition == null) return null;
                return (_lastPosition.X, _lastPosition.Y, _lastPosition.Z);
            }
        }

        private static bool EnsureEditorWindow()
        {
            if (IsWindow(_editorWindow))
            {
                GetWindowThreadProcessId(_editorWindow, out uint currentPid);
                if (currentPid != 0 && currentPid == _editorProcessId)
                {
                    EnsureCoordinateElement();
                    return true;
                }
            }

            IntPtr foundWindow = IntPtr.Zero;
            uint foundPid = 0;

            foreach (var process in Process.GetProcessesByName("eurotrucks2"))
            {
                try
                {
                    uint pid = (uint)process.Id;
                    EnumWindows((hWnd, _) =>
                    {
                        GetWindowThreadProcessId(hWnd, out uint windowPid);
                        if (windowPid != pid) return true;

                        string title = GetWindowTitle(hWnd);
                        if (title.IndexOf("Map editor", StringComparison.OrdinalIgnoreCase) < 0)
                            return true;

                        foundWindow = hWnd;
                        foundPid = pid;
                        return false;
                    }, IntPtr.Zero);

                    if (foundWindow != IntPtr.Zero) break;
                }
                catch { }
                finally { process.Dispose(); }
            }

            if (foundWindow == IntPtr.Zero)
            {
                if (_editorWindow != IntPtr.Zero)
                    ClearEditorWindow();
                return false;
            }

            bool changed = foundWindow != _editorWindow || foundPid != _editorProcessId;
            _editorWindow = foundWindow;
            _editorProcessId = foundPid;

            if (changed)
            {
                ClearCoordinateElement();
                ClearLastPosition();
            }

            EnsureCoordinateElement();
            return true;
        }

        private static void EnsureCoordinateElement()
        {
            if (!IsWindow(_editorWindow)) return;

            lock (UiaSync)
            {
                if (_coordinateElement != null && _coordinateValuePattern != null)
                    return;
            }

            try
            {
                var root = AutomationElement.FromHandle(_editorWindow);
                if (root == null) return;

                // Самый стабильный путь: координатная строка — StatusBar.Pane2.
                var paneCondition = new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBar.Pane2", PropertyConditionFlags.IgnoreCase),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                var pane = root.FindFirst(TreeScope.Descendants, paneCondition);

                // Fallback для версий редактора, где AutomationId отличается.
                if (pane == null)
                {
                    var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                    foreach (AutomationElement candidate in root.FindAll(TreeScope.Descendants, editCondition))
                    {
                        string text = GetAutomationText(candidate);
                        if (ParsePosition(text) == null) continue;
                        pane = candidate;
                        break;
                    }
                }

                if (pane == null) return;
                if (!pane.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObject) || patternObject is not ValuePattern valuePattern)
                    return;

                lock (UiaSync)
                {
                    _coordinateElement = pane;
                    _coordinateValuePattern = valuePattern;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Map Editor bridge: поиск координатного поля: {ex.Message}");
            }
        }

        private static EditorPosition? ReadCachedCoordinate()
        {
            ValuePattern? pattern;
            AutomationElement? element;
            lock (UiaSync)
            {
                pattern = _coordinateValuePattern;
                element = _coordinateElement;
            }

            if (pattern == null || element == null)
            {
                EnsureCoordinateElement();
                lock (UiaSync)
                {
                    pattern = _coordinateValuePattern;
                    element = _coordinateElement;
                }
            }

            if (pattern == null || element == null)
                return null;

            try
            {
                string text = pattern.Current.Value ?? string.Empty;
                var parsed = ParsePosition(text);
                if (parsed != null) return parsed;

                // Некоторые реализации UIA не обновляют ValuePattern, но обновляют Name.
                string name = GetAutomationText(element);
                parsed = ParsePosition(name);
                if (parsed != null) return parsed;

                return null;
            }
            catch
            {
                // Control/window мог быть пересоздан самим игровым редактором. Сбрасываем
                // только UIA-кэш; следующий тик заново найдёт нужный control.
                ClearCoordinateElement();
                return null;
            }
        }

        private static string GetAutomationText(AutomationElement element)
        {
            try
            {
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject) && valuePatternObject is ValuePattern valuePattern)
                {
                    string value = valuePattern.Current.Value ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { }
            try { return element.Current.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static EditorPosition? ParsePosition(string text)
        {
            var match = CoordinateRegex.Match(text ?? string.Empty);
            if (!match.Success) return null;
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
                int length = GetWindowTextLength(hWnd);
                if (length <= 0) return string.Empty;
                var buffer = new char[length + 1];
                int count = GetWindowText(hWnd, buffer, buffer.Length);
                return count > 0 ? new string(buffer, 0, count) : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static void ClearCoordinateElement()
        {
            lock (UiaSync)
            {
                _coordinateElement = null;
                _coordinateValuePattern = null;
            }
        }

        private static void ClearEditorWindow()
        {
            _editorWindow = IntPtr.Zero;
            _editorProcessId = 0;
            ClearCoordinateElement();
        }

        private static void ClearLastPosition()
        {
            lock (Sync) _lastPosition = null;
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (!_initialized || _shutdown) return;
                _shutdown = true;
            }
            try { Application.ApplicationExit -= OnApplicationExit; } catch { }
            try { StopEvent.Set(); } catch { }
            ClearEditorWindow();
            ClearLastPosition();
            Log("Map Editor bridge: остановка");
        }

        private static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try { Console.WriteLine($"[MapEditor2GameEditor] {message}"); } catch { }
            try { Debug.WriteLine($"[MapEditor2GameEditor] {message}"); } catch { }
            try { Logger.Current?.Workflow($"[MapEditor2GameEditor] {message}"); } catch { }
        }

        private static void LogDebug(string message)
        {
            try { Debug.WriteLine($"[MapEditor2GameEditor] {message}"); } catch { }
        }
    }
}