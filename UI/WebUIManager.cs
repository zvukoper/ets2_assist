using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        private bool _debugMode = false;

        private void StartPauseCheck()
        {
            // Проверяем, включен ли debug-режим (передаётся через параметр в URL)
            // В веб-страницах используется ?debug=true, мы можем сохранить это состояние при запуске
            // Для простоты будем проверять наличие файла debug.flag или параметра в конфиге.
            // Я добавлю проверку через AppSettings или просто по наличию аргумента командной строки.
            // Для демонстрации будем считать, что debug включен, если в аргументах есть --debug
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.Equals("--debug", StringComparison.OrdinalIgnoreCase))
                {
                    _debugMode = true;
                    break;
                }
            }

            _pauseCheckTimer = new System.Windows.Forms.Timer();
            _pauseCheckTimer.Interval = 500;
            _pauseCheckTimer.Tick += (s, e) => CheckPauseAndUpdateUI();
            _pauseCheckTimer.Start();
            AppendLog("Pause check timer started.");
        }

        private async void CheckPauseAndUpdateUI()
        {
            // Если debug режим, не скрываем UI
            if (_debugMode)
            {
                // Если UI ещё не показан, показываем его с анимацией один раз
                if (!_uiShown)
                {
                    _uiShown = true;
                    SendCommandToMap("show_ui_first");
                    AppendLog("[UI] Отправлена команда show_ui_first (debug mode)");
                }
                return;
            }

            bool paused = await IsGamePausedAsync();
            if (paused != _lastPauseState)
            {
                _lastPauseState = paused;
                if (paused)
                {
                    SendCommandToMap("hide_ui");
                    AppendLog("[UI] Отправлена команда hide_ui (быстрый фейд-аут)");
                }
                else
                {
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
                // ignored
            }
            return false;
        }
    }
}