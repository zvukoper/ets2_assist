using System;
using System.Windows.Forms;
using System.IO;
using System.Threading;

namespace ETS2_Assist_GUI
{
    internal static class Program
    {
        private const string InstanceMutexName = "Local\\ETS2_Assist_MainInstance";
        private const string InstanceSignalName = "Local\\ETS2_Assist_MainWindowSignal";
        internal const string ShutdownSignalName = "Local\\ETS2_Assist_GracefulShutdownSignal";
        private static Mutex? _instanceMutex;
        internal static EventWaitHandle? InstanceSignal { get; private set; }

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
        }

        [STAThread]
        static void Main(string[]? args)
        {
            if (args != null && Array.Exists(args, a => string.Equals(a, "--shutdown", StringComparison.OrdinalIgnoreCase)))
            {
                SignalGracefulShutdown();
                return;
            }

            _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                SignalExistingInstance();
                return;
            }

            InstanceSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceSignalName);
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
                _ = ThreadPool.RegisterWaitForSingleObject(
                    shutdownSignal,
                    static (_, state) =>
                    {
                        if (state is not Form form || form.IsDisposed)
                            return;
                        try
                        {
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