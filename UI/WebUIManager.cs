using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        private bool _debugMode = false;
        private bool? _lastUiVisible;
        private bool? _lastPauseLogoVisible;

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
            bool gameFocused = IsGameFocused();
            bool uiVisible = !paused && gameFocused;
            if (uiVisible != _lastUiVisible)
            {
                _lastUiVisible = uiVisible;
                _lastPauseState = paused;
                if (!uiVisible)
                {
                    SendCommandToMap("hide_ui");
                    AppendLog("[UI] Отправлена команда hide_ui (пауза или потеря фокуса)");
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

            bool showPauseLogo = paused && gameFocused;
            if (showPauseLogo != _lastPauseLogoVisible)
            {
                _lastPauseLogoVisible = showPauseLogo;
                SendCommandToMap(showPauseLogo ? "show_pause_logo" : "hide_pause_logo");
            }
        }

        private static bool IsGameFocused()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            foreach (var process in Process.GetProcessesByName("eurotrucks2"))
            {
                try
                {
                    if (process.MainWindowHandle == foreground) return true;
                }
                catch { }
                finally { process.Dispose(); }
            }
            return false;
        }

        private async Task<bool> IsGamePausedAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(1);
                    var response = await client.GetAsync("http://localhost:8080/api/rest/single/frame/paused");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = (await response.Content.ReadAsStringAsync()).Trim();
                        if (bool.TryParse(json, out bool paused)) return paused;
                        var token = JToken.Parse(json);
                        if (token.Type == JTokenType.Boolean) return token.Value<bool>();
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