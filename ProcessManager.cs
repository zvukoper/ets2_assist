using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    public class ProcessManager
    {
        private Process? scriptProcess;
        private readonly Logger logger;
        private bool isStarting = false;
        private bool hasErrors = false;

        public event EventHandler? StatusChanged;

        public bool IsRunning => scriptProcess != null && !scriptProcess.HasExited;
        public bool IsStarting => isStarting;
        public bool HasErrors => hasErrors;

        public ProcessManager(Logger logger)
        {
            this.logger = logger;
        }

        public async Task StartAsync()
        {
            if (IsRunning || isStarting) return;
            isStarting = true;
            hasErrors = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                string scriptPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "data",
                    "ets2_assist_arduino.ps1"
                );

                if (!System.IO.File.Exists(scriptPath))
                {
                    logger.Error($"Script not found: {scriptPath}");
                    hasErrors = true;
                    isStarting = false;
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                // Добавляем -AutoStartAll для автоматического запуска без запросов
                string args = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -AutoStartAll";
                if (!AppSettings.DebugMode)
                    args += " -WindowStyle Hidden";

                scriptProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    }
                };
                scriptProcess.OutputDataReceived += (s, e) => { if (e.Data != null) logger.Info(e.Data); };
                scriptProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) logger.Error(e.Data); };
                scriptProcess.Exited += (s, e) => OnProcessExited();
                scriptProcess.EnableRaisingEvents = true;

                scriptProcess.Start();
                scriptProcess.BeginOutputReadLine();
                scriptProcess.BeginErrorReadLine();

                logger.Info($"Script started with PID {scriptProcess.Id}");
                isStarting = false;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to start script: {ex.Message}");
                hasErrors = true;
                isStarting = false;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            if (scriptProcess != null && !scriptProcess.HasExited)
            {
                scriptProcess.Kill();
                scriptProcess.WaitForExit(3000);
                scriptProcess.Dispose();
                scriptProcess = null;
            }
            logger.Info("System stopped");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnProcessExited()
        {
            logger.Warning("Script process exited unexpectedly");
            hasErrors = true;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}