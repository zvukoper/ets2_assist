using System;
using System.IO;

namespace ETS2_Assist_GUI
{
    public class Logger
    {
        private readonly string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private readonly string currentLogFile;

        public event Action<string>? OnLogMessage;

        public Logger()
        {
            Directory.CreateDirectory(logDir);
            currentLogFile = Path.Combine(logDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        }

        public void Info(string msg) => Write("INFO", msg);
        public void Warning(string msg) => Write("WARN", msg);
        public void Error(string msg) => Write("ERROR", msg);
        public void Debug(string msg) => Write("DEBUG", msg);

        private void Write(string level, string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
            File.AppendAllText(currentLogFile, line + Environment.NewLine);
            OnLogMessage?.Invoke(line);
        }
    }
}