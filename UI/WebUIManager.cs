using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        private uint _gameProcessId = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private bool IsGameWindowFocused()
        {
            if (_gameProcessId == 0) return false;
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            GetWindowThreadProcessId(foreground, out uint pid);
            return pid == _gameProcessId;
        }

        private void StartPauseCheck()
        {
            try
            {
                var processes = Process.GetProcessesByName("eurotrucks2");
                if (processes.Length > 0)
                {
                    _gameProcessId = (uint)processes[0].Id;
                    AppendLog($"Game process found, PID: {_gameProcessId}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error finding game process: {ex.Message}");
            }

            _pauseCheckTimer = new System.Windows.Forms.Timer();
            _pauseCheckTimer.Interval = 500;
            _pauseCheckTimer.Tick += (s, e) => CheckPauseAndUpdateUI();
            _pauseCheckTimer.Start();
            AppendLog("Pause check timer started.");
        }

        private async void CheckPauseAndUpdateUI()
        {
            bool paused = await IsGamePausedAsync();
            bool focused = IsGameWindowFocused();

            bool shouldShow = !paused && focused;

            if (shouldShow != _lastPauseState)
            {
                _lastPauseState = shouldShow;
                if (shouldShow)
                {
                    // Если первый раз показываем – используем анимацию увеличения
                    if (!_uiShown)
                    {
                        _uiShown = true;
                        SendCommandToMap("show_ui_first");
                        AppendLog("[UI] Отправлена команда show_ui_first (первый показ с увеличением)");
                    }
                    else
                    {
                        SendCommandToMap("show_ui");
                        AppendLog("[UI] Отправлена команда show_ui (быстрый фейд-ин)");
                    }
                }
                else
                {
                    SendCommandToMap("hide_ui");
                    AppendLog("[UI] Отправлена команда hide_ui (быстрый фейд-аут)");
                }
            }
        }

        private async Task<bool> IsGamePausedAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(1);
                    var response = await client.GetAsync("http://localhost:25555/api/ets2/telemetry");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        return obj["game"]?["paused"]?.Value<bool>() ?? false;
                    }
                }
            }
            catch
            {
                // Если не удалось получить, считаем не на паузе
            }
            return false;
        }
    }
}