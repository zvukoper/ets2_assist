using System;
using System.Diagnostics;
using System.IO;

namespace ETS2_Assist_GUI.Services
{
    /// <summary>
    /// Запускает Python HTTP-сервер для раздачи статических файлов (порт 8082).
    /// Использует встроенный модуль http.server.
    /// </summary>
    public class PythonServerStarter
    {
        private readonly Logger _logger;
        private Process _process;
        private bool _isRunning = false;
        private readonly string _dataDirectory;

        public PythonServerStarter(Logger logger = null)
        {
            _logger = logger;
            _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                var pythonCmd = "python";
                // Проверяем наличие python или python3
                if (!CheckPythonExists(pythonCmd))
                {
                    pythonCmd = "python3";
                    if (!CheckPythonExists(pythonCmd))
                    {
                        _logger?.Log("Python не найден. Убедитесь, что Python установлен и доступен в PATH.");
                        return;
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = "-m http.server 8082",
                    WorkingDirectory = _dataDirectory,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                _process = Process.Start(startInfo);
                _isRunning = true;
                _logger?.Log($"Python HTTP-сервер запущен на порту 8082 (PID: {_process?.Id})");
            }
            catch (Exception ex)
            {
                _logger?.Log($"Ошибка запуска Python HTTP-сервера: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning || _process == null) return;

            try
            {
                _process.Kill();
                _process.WaitForExit(1000);
                _process.Dispose();
                _isRunning = false;
                _logger?.Log("Python HTTP-сервер остановлен.");
            }
            catch (Exception ex)
            {
                _logger?.Log($"Ошибка остановки Python HTTP-сервера: {ex.Message}");
            }
        }

        private bool CheckPythonExists(string cmd)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = "--version",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(startInfo))
                {
                    proc.WaitForExit(2000);
                    return proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool IsRunning => _isRunning;
    }
}