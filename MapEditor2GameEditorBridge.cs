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
        private static readonly Regex CoordinateRegex = new(
            @"\[\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*,\s*([-+]?\d+(?:\.\d+)?)\s*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ManualResetEventSlim StopEvent = new(false);

        private static bool _initialized;
        private static bool _shutdown;
        private static Thread? _monitorThread;
        private static volatile bool _editorRunning;
        private static EditorPosition? _lastPosition;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

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

            // v39.90: фоновый мониторинг игрового редактора. Раз в 5 сек проверяем, запущен ли
            // процесс игрового редактора (окно "Map editor" процесса eurotrucks2.exe). Если
            // запущен — собираем координаты из координатного поля каждые 250 мс в кэш.
            _monitorThread = new Thread(MonitorLoop)
            {
                IsBackground = true,
                Name = "MapEditor2.GameEditorMonitor"
            };
            _monitorThread.Start();
        }

        private static void OnApplicationExit(object? sender, EventArgs e) => Shutdown();

        // v39.90: фоновый цикл мониторинга. Не запущен редактор — проверка раз в 5 сек;
        // запущен — сбор координат каждые 250 мс.
        private static void MonitorLoop()
        {
            while (!StopEvent.IsSet)
            {
                bool running = IsEditorProcessRunning();
                if (running)
                {
                    Volatile.Write(ref _editorRunning, true);
                    var pos = ReadEditorPosition();
                    lock (Sync) _lastPosition = pos;
                    StopEvent.Wait(TimeSpan.FromMilliseconds(100));
                }
                else
                {
                    Volatile.Write(ref _editorRunning, false);
                    lock (Sync) _lastPosition = null;
                    StopEvent.Wait(TimeSpan.FromSeconds(5));
                }
            }
        }

        // v39.90: запущен ли игровой редактор (окно "Map editor" процесса eurotrucks2.exe).
        internal static bool IsEditorRunning() => Volatile.Read(ref _editorRunning);

        // v39.90: последние собранные координаты (кэш, обновляется каждые 250 мс).
        internal static (double X, double Y, double Z)? GetLastPosition()
        {
            lock (Sync)
            {
                if (_lastPosition == null) return null;
                return (_lastPosition.X, _lastPosition.Y, _lastPosition.Z);
            }
        }

        // v39.90: проверка наличия окна "Map editor" процесса eurotrucks2.exe (без UI Automation).
        private static bool IsEditorProcessRunning()
        {
            var processIds = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("eurotrucks2"))
            {
                try { processIds.Add((uint)process.Id); }
                catch { }
                finally { process.Dispose(); }
            }
            if (processIds.Count == 0) return false;

            bool found = false;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!processIds.Contains(pid)) return true;
                string title = GetWindowTitle(hWnd);
                if (title.IndexOf("Map editor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static EditorPosition? ReadEditorPosition()
        {
            var processIds = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("eurotrucks2"))
            {
                try { processIds.Add((uint)process.Id); }
                catch { }
                finally { process.Dispose(); }
            }
            if (processIds.Count == 0) return null;

            IntPtr editorWindow = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!processIds.Contains(pid)) return true;
                string title = GetWindowTitle(hWnd);
                if (title.IndexOf("Map editor", StringComparison.OrdinalIgnoreCase) < 0) return true;
                editorWindow = hWnd;
                return false;
            }, IntPtr.Zero);
            if (editorWindow == IntPtr.Zero) return null;

            try
            {
                var root = AutomationElement.FromHandle(editorWindow);
                if (root == null) return null;

                var paneCondition = new AndCondition(
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBar.Pane2", PropertyConditionFlags.IgnoreCase),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                var pane = root.FindFirst(TreeScope.Descendants, paneCondition);
                if (pane != null)
                {
                    string text = GetAutomationText(pane);
                    var parsed = ParsePosition(text);
                    if (parsed != null) return parsed;
                }

                var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                foreach (AutomationElement candidate in root.FindAll(TreeScope.Descendants, editCondition))
                {
                    string text = GetAutomationText(candidate);
                    var parsed = ParsePosition(text);
                    if (parsed == null) continue;
                    return parsed;
                }
                return null;
            }
            catch (Exception ex)
            {
                Log($"Поиск координат: ошибка UI Automation: {ex.Message}");
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

        internal static void Shutdown()
        {
            lock (Sync)
            {
                if (!_initialized || _shutdown) return;
                _shutdown = true;
            }
            try { Application.ApplicationExit -= OnApplicationExit; } catch { }
            try { StopEvent.Set(); } catch { }
            Log("Map Editor bridge: остановка");
        }

        private static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try { Console.WriteLine($"[MapEditor2GameEditor] {message}"); } catch { }
            try { Debug.WriteLine($"[MapEditor2GameEditor] {message}"); } catch { }
            try { Logger.Current?.Workflow($"[MapEditor2GameEditor] {message}"); } catch { }
        }
    }
}
