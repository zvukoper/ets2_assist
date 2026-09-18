using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        private bool _debugMode = false;
        private bool? _lastUiVisible;
        private bool? _lastPauseLogoVisible;

        // ================================================================
        // СТРОГАЯ ПОЛИТИКА ВИДИМОСТИ ВЕБ-ОВЕРЛЕЕВ
        // ================================================================
        // Существуют две взаимоисключающие категории интерфейса:
        //   * игровые интерфейсы    — гибрид, миникарта, уведомления (игровая камера);
        //   * интерактивные         — минилого и активный интерактив (окно квестов
        //                             либо его закладка «Квесты»).
        // Категория выбирается по паузе игры, но на экране должна быть только
        // одна из них. Дополнительно: если фокус ушёл на СТОРОННЕЕ окно
        // (не игра и не наш собственный веб-оверлей) — скрываем ВСЁ, потому что
        // наложение поверх пользовательских окон недопустимо.
        private bool _lastInteractiveVisible;
        private bool _lastGameUiVisible;
        private bool _lastGameFocused;
        private string _focusDiagnostics = "";

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
            AppendLog("[UI] Политика оверлеев запущена (гистерезис 1 с, опрос 500 мс).");
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

            // Оверлеи допустимы только когда игра запущена и её окно активно.
            // Фокус на любом СТОРОННЕМ окне (включая основную форму приложения)
            // означает, что игровая картинка не видна — наложение запрещено.
            bool gameVisible = gameRunning && gameFocused;

            // Гистерезис: фиксируем смену только после 2 устойчивых тиков (~1 с),
            // чтобы кратковременная потеря фокуса не мигала оверлеями.
            if (gameVisible != _committedActive)
            {
                _activeMismatch++;
                if (_activeMismatch < 2) return;
                _committedActive = gameVisible;
                _activeMismatch = 0;
            }
            else
            {
                _activeMismatch = 0;
            }

            bool visible = _committedActive;
            // Ровно одна категория за раз: пауза -> интерактивные, игра -> игровые.
            // Отладочные кнопки («Показать карту», «Показать hybrid») принудительно
            // включают игровую категорию: взаимоисключение при этом сохраняется.
            bool showInteractive = visible && paused && !_gameUiForce;
            bool showGameUi = _gameUiForce || (visible && !paused);

            ApplyOverlayVisibility(showGameUi, showInteractive, gameFocused);
        }

        /// <summary>
        /// Применяет строгую политику показа. Игровые и интерактивные интерфейсы
        /// взаимоисключающие: одновременно видна только одна категория.
        /// </summary>
        private void ApplyOverlayVisibility(bool showGameUi, bool showInteractive, bool gameFocused)
        {
            if (showGameUi != _lastGameUiVisible)
            {
                _lastGameUiVisible = showGameUi;
                _lastUiVisible = showGameUi;
                _lastPauseState = !showGameUi;

                if (!showGameUi)
                {
                    SendCommandToMap("hide_ui");
                    SendCommandToMap("hide_game_ui");
                    if (!_minimapAutoLogic) SendCommandToMap("minimap_hide");
                    AppendLog("[UI] Игровые интерфейсы скрыты (пауза или фокус вне игры).");
                }
                else
                {
                    SendCommandToMap(_uiShown ? "show_ui" : "show_ui_first");
                    SendCommandToMap("minimap_show");
                    SendCommandToMap("show_game_ui");
                    AppendLog(_uiShown
                        ? "[UI] Игровые интерфейсы показаны: гибрид + миникарта + уведомления."
                        : "[UI] Игровые интерфейсы показаны впервые: гибрид + миникарта + уведомления.");
                    _uiShown = true;
                }
            }

            // Миникарта живёт в игровой категории. Отладочный тоггл «Показать карту»
            // держит её видимой всегда (вне зависимости от паузы и фокуса).
            if (_minimapAutoLogic)
            {
                if (_lastMinimapVisible != true)
                {
                    _lastMinimapVisible = true;
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = true });
                    SendCommandToMap("minimap_show");
                }
            }
            else
            {
                if (_lastMinimapAuto != false)
                {
                    _lastMinimapAuto = false;
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = false });
                }
                if (_lastMinimapVisible != showGameUi)
                {
                    _lastMinimapVisible = showGameUi;
                    SendCommandToMap(showGameUi ? "minimap_show" : "minimap_hide");
                }
            }

            if (showInteractive != _lastInteractiveVisible)
            {
                _lastInteractiveVisible = showInteractive;
                _lastPauseLogoVisible = showInteractive;
                if (showInteractive) _lastPauseState = true;
                SendCommandToMap("set_overlay_category", new JObject
                {
                    ["category"] = showInteractive ? "interactive" : "game"
                });
                if (showInteractive)
                {
                    RestoreQuestWindowState();
                    PushQuestInteractiveSignal();
                }
                AppendLog(showInteractive
                    ? "[UI] Интерактивные интерфейсы показаны: минилого + интерактивы (квесты)."
                    : "[UI] Интерактивные интерфейсы скрыты (игра снята с паузы или фокус вне игры).");
            }

            if (gameFocused != _lastGameFocused)
            {
                _lastGameFocused = gameFocused;
                AppendLog($"[UI] Фокус игры={gameFocused} ({_focusDiagnostics})");
            }
        }

        /// <summary>
        /// Вызывается из QuestRuntime при появлении/исчезновении доступных
        /// интерактивов в радиусе игрока. Пока таких интерактивов нет —
        /// закладка «Квесты» статична; при появлении она пульсирует жёлтым.
        /// </summary>
        internal void SetQuestInteractiveSignal(bool hasInteractive)
        {
            try
            {
                if (_questHasInteractive == hasInteractive) return;
                _questHasInteractive = hasInteractive;
                PushQuestInteractiveSignal();
            }
            catch { }
        }

        /// <summary>
        /// Обратная связь от страницы квестов: игрок свернул/развернул окно.
        /// Приложение запоминает вид, чтобы восстановить его при следующей паузе.
        /// </summary>
        internal void OnQuestWindowCollapsedChanged(bool collapsed)
        {
            try
            {
                if (_questCollapsed == collapsed) return;
                _questCollapsed = collapsed;
                SendCommandToMap("set_quest_collapsed", new JObject { ["collapsed"] = collapsed });
                AppendLog(collapsed
                    ? "[QUEST][UI] Окно квестов свёрнуто в закладку «Квесты»."
                    : "[QUEST][UI] Окно квестов развёрнуто.");
            }
            catch { }
        }

        /// <summary>
        /// Восстанавливает сохранённый вид окна квестов при показе интерактивной
        /// категории. Постановка на паузу сама окно не открывает: если игрок
        /// оставил его свёрнутым, показывается только закладка.
        /// </summary>
        private void RestoreQuestWindowState()
        {
            try
            {
                SendCommandToMap("set_quest_collapsed", new JObject { ["collapsed"] = _questCollapsed });
            }
            catch { }
        }

        /// <summary>
        /// Передаёт в окно квестов признак наличия доступного интерактива рядом.
        /// </summary>
        private void PushQuestInteractiveSignal()
        {
            try
            {
                SendCommandToMap("set_quest_tab_state", new JObject { ["hasInteractive"] = _questHasInteractive });
            }
            catch { }
        }

        private bool _questCollapsed;
        private bool _questHasInteractive;
        private bool _gameUiForce;

        /// <summary>
        /// Сброс кэша предыдущего решения политики: следующий тик применит
        /// состояние заново (используется кнопками управления оверлеями).
        /// </summary>
        internal void ResetOverlayVisibilityCache()
        {
            _lastUiVisible = null;
            _lastMinimapVisible = null;
            _lastMinimapAuto = null;
        }


        /// <summary>
        /// Игра считается сфокусированной, если активное окно принадлежит игре.
        /// Фокус на своём собственном веб-оверлее тоже не считается «потерей игры»
        /// (окна WebOverlay создаются с WS_EX_NOACTIVATE и не активируются, но
        /// проверка оставлена на случай ручного включения кликабельности окна).
        /// </summary>
        private bool IsGameFocused()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) { _focusDiagnostics = "нет активного окна"; return false; }

            foreach (var process in Process.GetProcessesByName("eurotrucks2")
                         .Concat(Process.GetProcessesByName("amtrucks2")))
            {
                try
                {
                    if (process.MainWindowHandle == foreground)
                    {
                        _focusDiagnostics = "фокус на игре";
                        return true;
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }

            // Фокус на нашем собственном оверлее не должен скрывать оверлеи.
            try
            {
                GetWindowThreadProcessId(foreground, out uint pid);
                if (pid != 0)
                {
                    using var owner = Process.GetProcessById((int)pid);
                    string name = owner.ProcessName;
                    if (name.Equals("WebOverlay", StringComparison.OrdinalIgnoreCase))
                    {
                        // Свой оверлей: политику показа не меняем.
                        _focusDiagnostics = $"фокус на оверлее ({name})";
                        return true;
                    }
                    _focusDiagnostics = $"фокус на постороннем окне ('{name}')";
                    return false;
                }
            }
            catch (Exception ex) { _focusDiagnostics = "фокус: " + ex.Message; }

            _focusDiagnostics = "фокус вне игры";
            return false;
        }

        private async Task<bool> IsGamePausedAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMilliseconds(700);
                    int currentPort = TruckTelemetry.Port;
                    int[] ports = currentPort == 8080 ? new[] { 8080, 8081 } : new[] { currentPort, 8080 };
                    foreach (int port in ports.Distinct())
                    {
                        try
                        {
                            var response = await client.GetAsync($"http://localhost:{port}/api/rest/single/frame/paused");
                            if (!response.IsSuccessStatusCode) continue;
                            var json = (await response.Content.ReadAsStringAsync()).Trim();
                            var parsed = ParsePausedResponse(json);
                            if (parsed.HasValue) return parsed.Value;
                        }
                        catch { }
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