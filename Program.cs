using System;
using System.Windows.Forms;
using System.IO;

namespace ETS2_Assist_GUI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Лог запуска
            File.AppendAllText("startup.log", $"{DateTime.Now}: Application started\n");

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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Показываем сплеш-экран с логотипом
            using (var splash = new SplashForm())
            {
                splash.ShowDialog();
            }

            try
            {
                Application.Run(new MainForm());
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