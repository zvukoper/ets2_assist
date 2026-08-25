using System;
using System.IO;
using System.Text;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Диагностическое логирование без per-frame спама.
    /// app_workflow.log — жизненный цикл и важные этапы.
    /// app_data.log — события/пакеты, где полезно видеть обрабатываемые данные.
    /// </summary>
    public sealed class Logger
    {
        private readonly string logDir;
        private readonly string workflowFile;
        private readonly string dataFile;
        private readonly object sync = new();

        public event Action<string>? OnLogMessage;

        public Logger()
        {
            logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            workflowFile = Path.Combine(logDir, "app_workflow.log");
            dataFile = Path.Combine(logDir, "app_data.log");

            // Every launch gets a visually distinct boundary in the workflow log.
            lock (sync)
            {
                File.AppendAllText(
                    workflowFile,
                    Environment.NewLine +
                    $"======= ETS2 Assist started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =======" +
                    Environment.NewLine +
                    $"BUILD_VERSION={BuildInfo.Version}" +
                    Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }

        public void Info(string msg) => Workflow("INFO", msg, true);
        public void Warning(string msg) => Workflow("WARN", msg, true);
        public void Error(string msg) => Workflow("ERROR", msg, true);
        public void Debug(string msg) => Workflow("DEBUG", msg, true);
        public void Data(string msg) => Workflow("DATA", msg, false, dataFile);

        public void Workflow(string msg) => Workflow("INFO", msg, false);

        private void Workflow(string level, string msg, bool notify, string? targetFile = null)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
            string file = targetFile ?? workflowFile;
            lock (sync)
            {
                File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
            }

            if (notify)
                OnLogMessage?.Invoke(line);
        }
    }
}
