using System;
using System.IO;
using System.Windows.Forms;

namespace ETS2_Assist_GUI.UI
{
    /// <summary>
    /// Логирование сообщений в консоль и в RichTextBox (если передан).
    /// </summary>
    public class Logger
    {
        private readonly RichTextBox _logControl;
        private readonly string _logFile;
        private readonly object _lock = new();

        public Logger(RichTextBox logControl = null)
        {
            _logControl = logControl;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logsDir = Path.Combine(baseDir, "Logs");
            if (!Directory.Exists(logsDir))
                Directory.CreateDirectory(logsDir);

            _logFile = Path.Combine(logsDir, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        /// <summary>
        /// Записывает сообщение в лог (в консоль, в RichTextBox и в файл).
        /// </summary>
        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var fullMessage = $"[{timestamp}] {message}";

            lock (_lock)
            {
                // В консоль
                Console.WriteLine(fullMessage);

                // В RichTextBox (если есть)
                if (_logControl != null && !_logControl.IsDisposed)
                {
                    try
                    {
                        if (_logControl.InvokeRequired)
                            _logControl.Invoke(new Action(() => AppendToLog(fullMessage)));
                        else
                            AppendToLog(fullMessage);
                    }
                    catch { /* Подавляем ошибки UI */ }
                }

                // В файл
                try
                {
                    File.AppendAllText(_logFile, fullMessage + Environment.NewLine);
                }
                catch { /* Подавляем ошибки записи */ }
            }
        }

        private void AppendToLog(string message)
        {
            if (_logControl == null || _logControl.IsDisposed) return;

            _logControl.AppendText(message + Environment.NewLine);
            _logControl.ScrollToCaret();
        }

        /// <summary>
        /// Очищает лог-консоль.
        /// </summary>
        public void ClearLog()
        {
            if (_logControl != null && !_logControl.IsDisposed)
            {
                try
                {
                    if (_logControl.InvokeRequired)
                        _logControl.Invoke(new Action(() => _logControl.Clear()));
                    else
                        _logControl.Clear();
                }
                catch { }
            }
        }

        /// <summary>
        /// Открывает папку с лог-файлами.
        /// </summary>
        public void OpenLogFolder()
        {
            var logDir = Path.GetDirectoryName(_logFile);
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        }
    }
}