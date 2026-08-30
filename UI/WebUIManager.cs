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
                    SendCommandToMap("minimap_show");
                    AppendLog("[UI] Отправлена команда show_ui_first (debug mode)");
                }
                return;
            }

            bool gameRunning = IsGameRunning();
            bool paused = await IsGamePausedAsync();
            bool gameFocused = IsGameFocused();

            // В фокусе = игра запущена И окно игры активно. Только в фокусе показываем
            // что-либо; ВНЕ фокуса СКРЫВАЕМ ВСЕ оверлеи (включая пауз-лого).
            bool inFocusNow = gameRunning && gameFocused;

            // Гистерезис по фокусу: фиксируем смену только после 2 устойчивых тиков (~1с),
            // чтобы кратковременная потеря фокуса не мигала оверлеями.
            if (inFocusNow != _committedActive)
            {
                _activeMismatch++;
                if (_activeMismatch < 2) return;
                _committedActive = inFocusNow;
                _activeMismatch = 0;
            }
            else
            {
                _activeMismatch = 0;
            }

            bool focused = _committedActive;
            // В фокусе и НЕ на паузе -> карта + гибрид. В фокусе и на паузе -> только пауз-лого.
            bool showMapHybrid = focused && !paused;
            bool showPause = focused && paused;

            // Гибрид: показываем только когда в фокусе и не на паузе.
            if (showMapHybrid != _lastUiVisible)
            {
                _lastUiVisible = showMapHybrid;
                _lastPauseState = paused;
                if (!showMapHybrid)
                {
                    SendCommandToMap("hide_ui");
                    AppendLog("[UI] Отправлена команда hide_ui (пауза / потеря фокуса / игра не запущена)");
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

            // Пауз-лого: показываем ТОЛЬКО в фокусе и на паузе. Вне фокуса — скрываем.
            // Когда «Показать карту» включён (тоггл), карта не зависит от паузы/фокуса.
            if (showPause != _lastPauseLogoVisible)
            {
                _lastPauseLogoVisible = showPause;
                SendCommandToMap(showPause ? "show_pause_logo" : "hide_pause_logo");
                AppendLog(showPause ? "[UI] Пауз-лого показан (в фокусе, пауза)" : "[UI] Пауз-лого скрыт (в фокусе и не на паузе / вне фокуса)");
            }

            // Миникарта:
            //  - тоггл «Показать карту» ВКЛ -> карта ВСЕГДА видна (не зависит от паузы/фокуса);
            //  - тоггл ВЫКЛ -> обычная логика (как гибрид: фокус + не пауза).
            bool minimapVisible;
            if (_minimapAutoLogic)
            {
                minimapVisible = true;
                // Один раз показываем; гибридом и пауз-лого управляет обычная логика выше.
                if (_lastMinimapVisible != true)
                {
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = true });
                    SendCommandToMap("minimap_show");
                }
            }
            else
            {
                minimapVisible = showMapHybrid;
            }
            if (minimapVisible != _lastMinimapVisible)
            {
                _lastMinimapVisible = minimapVisible;
                if (!_minimapAutoLogic)
                    SendCommandToMap(minimapVisible ? "minimap_show" : "minimap_hide");
            }
            if (_minimapAutoLogic != _lastMinimapAuto)
            {
                _lastMinimapAuto = _minimapAutoLogic;
                if (!_minimapAutoLogic)
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = false });
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
                        // TruckTel может вернуть булево в разных видах ("true", true,
                        // {"paused":true}...). ParsePausedResponse разбирает всё; при
                        // неудаче разбора доверяем намерению приложения (_pausedIntent).
                        var parsed = ParsePausedResponse(json);
                        if (parsed.HasValue) return parsed.Value;
                        return _pausedIntent;
                    }
                }
            }
            catch
            {
                // ignored
            }
            // Телеметрия недоступна — используем последнее известное намерение приложения.
            return _pausedIntent;
        }
    }
}