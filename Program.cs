using System;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace ETS2_Assist_GUI
{
    internal static class Program
    {
        private const string InstanceMutexName = "Local\\ETS2_Assist_MainInstance";
        private const string InstanceSignalName = "Local\\ETS2_Assist_MainWindowSignal";
        internal const string ShutdownSignalName = "Local\\ETS2_Assist_GracefulShutdownSignal";
        // Внешний запуск оверлеев без мыши: `ETS2_Assist.exe --start`.
        // Вместо мыши этот сигнал поднимает систему ТОЙ ЖЕ кнопкой Start
        // (MainForm.StartSystem), поэтому поведение полностью совпадает.
        internal const string StartSignalName = "Local\\ETS2_Assist_StartSignal";
        private static Mutex? _instanceMutex;
        internal static EventWaitHandle? InstanceSignal { get; private set; }
        internal static EventWaitHandle? StartSignal { get; private set; }

        /// <summary>
        /// Приложение запущено с `--start`: систему нужно поднять сразу, без мыши.
        /// </summary>
        internal static bool StartSystemRequested { get; private set; }

        internal static void SignalStartSystem()
        {            try
            {
                using var signal = EventWaitHandle.OpenExisting(StartSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (Exception)
            {
            }
        }

        internal static void SignalExistingInstance()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(InstanceSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }

        private static bool HasArg(string[]? args, string name)
            => args != null && Array.Exists(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        internal static void SignalGracefulShutdown()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShutdownSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (Exception)
            {
            }
        }

        // ================================================================
        // ГАРАНТИРОВАННОЕ ЗАВЕРШЕНИЕ (v1.0.40.24)
        // Проблема: --shutdown только посылал событие и сразу выходил. Если приложение
        // висело (или событие никто не слушал), процесс оставался в памяти; а обработчик
        // сигнала вызывал Application.Exit() В ОБХОД StopSystem() — поэтому
        // KillChildProcesses() не выполнялся и процессы WebOverlay/pano
        // оставались висеть фоном.
        // Решение: сначала даём штатный шанс, затем принудительно зачищаем остатки.
        // ================================================================
        private const int GracefulWaitMs = 6000;

        internal static void EnsureEverythingStopped()
        {
            try
            {
                int self = Environment.ProcessId;
                var deadline = DateTime.Now.AddMilliseconds(GracefulWaitMs);
                while (DateTime.Now < deadline && CountOtherProcesses("ETS2_Assist", self) > 0)
                    Thread.Sleep(200);

                // Остатки главного процесса (себя НЕ убиваем — мы и есть ETS2_Assist в режиме --shutdown).
                KillProcesses("ETS2_Assist", self);
                // Оверлеи убиваем ВСЕГДА — включая случаи, когда главный процесс уже умер.
                KillProcesses("WebOverlay", self);
                KillProcesses("pano", self);
                // Зависшие WebView2 ТОЛЬКО от наших оверлеев — освобождают файлы и GPU-ресурсы.
                KillOurWebView2();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("shutdown.log", $"{DateTime.Now}: EnsureEverythingStopped: {ex.Message}\n"); }
                catch { }
            }
        }

        // Убивает зависшие msedgewebview2 ТОЛЬКО от наших окон (WebOverlay / ETS2_Assist).
        // ВАЖНО: `msedgewebview2` используют и сторонние приложения (Teams/Outlook и т.п.) —
        // убивать все процессы нельзя, фильтруем по командной строке через WMI.
        internal static void KillOurWebView2()
        {
            try
            {
                int self = Environment.ProcessId;
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedgewebview2.exe'");
                foreach (var obj in searcher.Get())
                {
                    try
                    {
                        string cmd = obj["CommandLine"]?.ToString() ?? string.Empty;
                        bool ours = cmd.IndexOf("WebOverlay", StringComparison.OrdinalIgnoreCase) >= 0
                                 || cmd.IndexOf("ETS2_Assist", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!ours) continue;
                        int pid = Convert.ToInt32(obj["ProcessId"]);
                        if (pid == self) continue;
                        using var p = Process.GetProcessById(pid);
                        p.Kill();
                        p.WaitForExit(2000);
                        TryLogShutdown($"Убит msedgewebview2 (PID {pid}, наш)");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TryLogShutdown("KillOurWebView2: " + ex.Message);
            }
        }

        private static int CountOtherProcesses(string name, int selfPid)
        {
            try
            {
                int n = 0;
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { if (p.Id != selfPid) n++; }
                    finally { p.Dispose(); }
                }
                return n;
            }
            catch { return 0; }
        }

        // Убивает процессы по имени (кроме себя) с ожиданием завершения и повтором:
        // WebOverlay может запускать дочерние окна, первая попытка не всегда достаточна.
        private static void KillProcesses(string name, int selfPid)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool any = false;
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.Id == selfPid) continue;
                        any = true;
                        p.Kill();
                        p.WaitForExit(3000);
                        TryLogShutdown($"Убит {name} (PID {p.Id})");
                    }
                    catch (Exception ex)
                    {
                        TryLogShutdown($"Неудачно убить {name}: {ex.Message}");
                    }
                    finally { p.Dispose(); }
                }
                if (!any) return;
                if (attempt == 0) Thread.Sleep(300);
            }
        }

        private static void TryLogShutdown(string msg)
        {
            try { File.AppendAllText("shutdown.log", $"{DateTime.Now}: {msg}\n"); } catch { }
        }

        [STAThread]
        static void Main(string[]? args)
        {
            if (args != null && Array.Exists(args, a => string.Equals(a, "--shutdown", StringComparison.OrdinalIgnoreCase)))
            {
                // 1) Штатный сигнал; 2) ожидание; 3) принудительная зачистка остатков.
                SignalGracefulShutdown();
                EnsureEverythingStopped();
                return;
            }

            // `--start` — запуск оверлеев без мыши. Если приложение уже работает,
            // ему уходит сигнал запуска; иначе флаг доходит до MainForm и система
            // поднимается сама сразу после старта окна.
            bool startSystem = HasArg(args, "--start");
            StartSystemRequested = startSystem;
            if (startSystem && HasArg(args, "--start-only"))
            {
                // Только сигнал уже запущенному приложению, без старта копии.
                SignalStartSystem();
                return;
            }

            _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                SignalExistingInstance();
                if (startSystem) SignalStartSystem();
                return;
            }

            InstanceSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceSignalName);
            StartSignal = new EventWaitHandle(false, EventResetMode.AutoReset, StartSignalName);
            using var shutdownSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownSignalName);

            File.AppendAllText("startup.log", $"{DateTime.Now}: Application started BUILD={BuildInfo.Version}\n");

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                string msg = $"Thread exception: {e.Exception.Message}\n{e.Exception.StackTrace}";
                MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.AppendAllText("crash.log", $"{DateTime.Now}: {msg}\n");
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                string msg = $"Unhandled exception: {ex?.Message}\n{ex?.StackTrace}";
                MessageBox.Show(msg, "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.AppendAllText("crash.log", $"{DateTime.Now}: {msg}\n");
            };

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var splash = new SplashForm())
            {
                splash.ShowDialog();
            }

            try
            {
                var mainForm = new MainForm();
                Quests.QuestRuntime.Attach(mainForm);
                _ = ThreadPool.RegisterWaitForSingleObject(
                    shutdownSignal,
                    static (state, _) =>
                    {
                        if (state is not Form form || form.IsDisposed)
                            return;
                        try
                        {
                            // v1.0.40.24: раньше здесь был Application.Exit() — он НЕ вызывал
                            // StopSystem(), поэтому дочерние процессы (WebOverlay/pano) оставались
                            // висеть. Теперь идём штатным путём остановки системы.
                            if (form is MainForm mf)
                            {
                                if (mf.InvokeRequired)
                                    mf.BeginInvoke(new Action(mf.ShutdownFromSignal));
                                else
                                    mf.ShutdownFromSignal();
                                return;
                            }
                            if (form.InvokeRequired)
                                form.BeginInvoke(new Action(Application.Exit));
                            else
                                Application.Exit();
                        }
                        catch
                        {
                        }
                    },
                    mainForm,
                    -1,
                    executeOnlyOnce: false);

                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                string msg = $"Startup exception: {ex.Message}\n{ex.StackTrace}";
                MessageBox.Show(msg, "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                File.AppendAllText("crash.log", $"{DateTime.Now}: {msg}\n");
            }
        }
    }
}