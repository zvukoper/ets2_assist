using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

        // ================================================================
        // ПОЛИТИКА ОВЕРЛЕЕВ: ПОТОКОВЫЙ ТАЙМЕР, НЕ WinForms-ТАЙМЕР.
        //
        // КОРЕНЬ «миникарта/гибрид/квесты живут отдельной жизнью»: политика
        // шла на System.Windows.Forms.Timer, который не тикает при удержании
        // окна игры. ETS2 при старте забирает фокус, политика вызывала
        // ForceForegroundWindow СВОЕГО окна и возвращала фокус приложению;
        // окно игры, лишившись фокуса, перестаёт отдавать WM_TIMER, и
        // CheckPauseAndUpdateUI не вызывался больше НИ РАЗУ (в логе сессии
        // 13:34 — ни одной строки политики за 22 минуты).
        //
        // Тот же дефект уже дважды ловили в этом проекте (MapEditor2Form,
        // AR-тик) и лечили сменой таймера. Повторяем проверенное решение:
        // System.Threading.Timer НЕ зависит от очереди сообщений;
        // работа с UI/командами — через BeginInvoke;
        // Interlocked-флаг не даёт тикам накладываться (тик асинхронный).
        // ================================================================
        private System.Threading.Timer? _pauseCheckTimer;
        private int _pauseCheckBusy;

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

            _pauseCheckTimer?.Dispose();
            _pauseCheckTimer = new System.Threading.Timer(
                _ => QueuePauseCheck(), null, PauseCheckIntervalMs, PauseCheckIntervalMs);
            AppendLog($"[UI] Политика оверлеев запущена (гистерезис 1 с, опрос {PauseCheckIntervalMs} мс, потоковый таймер).");
        }

        private const int PauseCheckIntervalMs = 500;
        private long _pauseCheckTicks;
        private long _pauseCheckLastLogged;

        // Снимок паузы, снятый В ФОНЕ (HTTP-запрос нельзя выполнять в обработчике
        // сообщений: пока он идёт, оконная процедура стоит и WM_TIMER/Tick копятся).
        // -1 = данных ещё нет, 0 = игра идёт, 1 = пауза.
        private int _pauseSnapshot = -1;

        /// <summary>
        /// Тик политики приходит в потоке пула. Сеть опрашиваем ЗДЕСЬ (в фоне),
        /// решение применяем в UI-потоке.
        /// </summary>
        private void QueuePauseCheck()
        {
            if (Interlocked.CompareExchange(ref _pauseCheckBusy, 1, 0) != 0) return;
            bool handed = false;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                int paused = IsGamePaused() ? 1 : 0;
                Volatile.Write(ref _pauseSnapshot, paused);
                BeginInvoke((Action)(() =>
                {
                    try { CheckPauseAndUpdateUI(); }
                    finally { Volatile.Write(ref _pauseCheckBusy, 0); }
                }));
                handed = true;   // флаг снимет сам UI-вызов в finally
            }
            catch { }
            finally
            {
                // ВАЖНО: если вызов НЕ передан в UI-очередь (форма закрывается или
                // окно ещё без handle), флаг надо снять ЗДЕСЬ — иначе он «залипнет»
                // в 1 и политика умрёт навсегда. Та же ловушка, что и в AR-тике.
                if (!handed) Volatile.Write(ref _pauseCheckBusy, 0);
            }
        }

        /// <summary>
        /// Строка «жизни» политики: раз в 30 с в app_data.log. Без неё молчание
        /// политики (ровно этот баг) невозможно отличить от «нечего показывать».
        /// </summary>
        private void LogPauseCheckAlive(bool gameRunning, bool paused, bool gameFocused)
        {
            long ticks = Interlocked.Increment(ref _pauseCheckTicks);
            if (ticks - Interlocked.Read(ref _pauseCheckLastLogged) < 60) return;
            Volatile.Write(ref _pauseCheckLastLogged, ticks);
            AppendDataLog(
                $"[UI] политика жива: тиков={ticks} running={gameRunning} paused={paused} " +
                $"focus={gameFocused} active={_committedActive} gameUI={_lastGameUiVisible} " +
                $"interactive={_lastInteractiveVisible} ({_focusDiagnostics})");
        }

        private void CheckPauseAndUpdateUI()
        {
            // Если debug режим, не скрываем UI (и принудительно показываем окна слоёв)
            if (_debugMode)
            {
                RestoreOverlayLayers();
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
            bool gameFocused = IsGameFocused();
            // Пауза — из снимка, снятого в фоне (если ещё нет, берём намерение).
            int snap = Volatile.Read(ref _pauseSnapshot);
            bool paused = snap < 0 ? _pausedIntent : snap == 1;
            LogPauseCheckAlive(gameRunning, paused, gameFocused);

            // v1.0.40.58: ОТЛАДКА ВЕБ-КОНТЕНТА. При включённом debugShow чекбоксе
            // оверлеи НЕ исчезают при потере фокуса (окно ушло на другой экран/
            // пользователь работает в редакторе), но ЛОГИКА ПАУЗЫ работает как есть:
            // пауза -> интерактивные, игра -> игровые. Отключается РОВНО правило
            // фокуса, и ничего больше.
            bool debugShow = AR.ArBridge.DebugShow;

            // Оверлеи допустимы только когда игра запущена и её окно активно.
            // Фокус на любом СТОРОННЕМ окне (включая основную форму приложения)
            // означает, что игровая картинка не видна — наложение запрещено.
            bool gameVisible = gameRunning && (debugShow || gameFocused);

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

            UpdateOverlayLayerFocus(debugShow || _committedActive);
            ApplyOverlayVisibility(showGameUi, showInteractive, gameFocused);
        }

        /// <summary>
        /// Спецпроверка фокуса. Все веб-оверлеи скрываются, если фокус ушёл на
        /// СТОРОННЕЕ окно (в том числе на основную форму ETS2 Assist): игровая
        /// камера уже не видна, наложение поверх пользовательских окон недопустимо.
        /// Фокус на нашей собственной странице оверлея ничего не меняет.
        /// Скрывается и контент страниц (команда set_overlay_hidden, порт 8084),
        /// и сами окна слоёв (команды hide_all/show_all хосту по каналу).
        /// Гистерезис берётся от общего решения политики (_committedActive),
        /// чтобы стартовый/кратковременный переход фокуса не мигал окнами.
        /// </summary>
        private void UpdateOverlayLayerFocus(bool layersVisible)
        {
            try
            {
                bool wanted = !layersVisible;
                if (wanted == _overlayLayersHidden) return;

                _overlayLayersHidden = wanted;
                SendCommandToMap("set_overlay_hidden", new JObject { ["hidden"] = wanted });
                SendOverlayHostCommand();
                AppendLog(wanted
                    ? "[UI] Фокус вне игры — все веб-оверлеи скрыты (и контент, и окна слоёв)."
                    : "[UI] Фокус вернулся в игру — веб-оверлеи показаны.");
            }
            catch { }
        }

        /// <summary>
        /// Команда хост-процессу оверлеев через именованный канал WebOverlayPipe.
        /// Приложение владеет политикой показа, но окнами владеет хост, поэтому
        /// скрытие окон слоёв выполняются его командами hide_all/show_all.
        /// Канал — единственный безопасный способ: если хост не запущен,
        /// подключение просто не удаётся и НИЧЕГО не запускается (запуск exe с
        /// такой командой поднял бы лишний экземпляр со страницей справки).
        /// </summary>
        private void SendOverlayHostCommand()
        {
            Task.Run(() =>
            {
                lock (_overlayHostPipeLock)
                {
                    try
                    {
                        // Значение читается ПОД блокировкой: если состояние успело
                        // смениться, пока задача ждала, хосту уйдёт актуальное.
                        string command = _overlayLayersHidden ? "hide_all" : "show_all";
                        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "WebOverlayPipe", System.IO.Pipes.PipeDirection.Out);
                        pipe.Connect(400);
                        using var writer = new StreamWriter(pipe) { AutoFlush = true };
                        writer.WriteLine(command);
                    }
                    catch
                    {
                        // Хост оверлеев не запущен — скрывать нечего.
                    }
                }
            });
        }

        private readonly object _overlayHostPipeLock = new object();
        private bool _overlayLayersHidden;

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
                Logger.Current?.Workflow($"[QUEST-DIAG] state source=policy paused={!showGameUi} collapsed={_questCollapsed} interactive={showInteractive} gameUi={showGameUi} focused={gameFocused}");
            }

            // v1.0.40.59: пока на экране интерактивная категория (а это значит, что
            // видна закладка «Квесты» либо само окно), TAB переключает окно — как
            // стрелочка сворачивания. В остальное время TAB не перехватываем.
            SetQuestToggleHotkeyActive(showInteractive);

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
                _questCollapsed = collapsed;
                // Вид окна запоминается между запусками приложения.
                AppSettings.QuestWindowCollapsed = collapsed;
                AppSettings.Save();
                // ДИАГНОСТИКА (временно): фактическое состояние окна, а не предполагаемое.
                Logger.Current?.Workflow($"[QUEST-DIAG] state source=page paused={_lastPauseState} collapsed={collapsed} interactive={_lastInteractiveVisible}");
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
                Logger.Current?.Workflow($"[QUEST-DIAG] state source=restore paused=true collapsed={_questCollapsed}");
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
        /// Загружает сохранённый вид окна квестов. Вызывается один раз при старте
        /// системы, ДО первого показа интерактивной категории, чтобы окно
        /// открылось в том виде, в котором его оставил игрок.
        /// </summary>
        internal void LoadQuestWindowState()
        {
            _questCollapsed = AppSettings.QuestWindowCollapsed;
        }

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
        /// Принудительный показ окон слоёв (например, при остановке системы).
        /// </summary>
        internal void RestoreOverlayLayers()
        {
            if (!_overlayLayersHidden) return;
            _overlayLayersHidden = false;
            SendCommandToMap("set_overlay_hidden", new JObject { ["hidden"] = false });
            SendOverlayHostCommand();
        }


        /// <summary>
        /// Игра считается сфокусированной, если активное окно принадлежит игры/нашему оверлею.
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
        /// <summary>
        /// Асинхронный вариант проверки паузы: для НЕ-UI путей (квесты, телепорт),
        /// где блокировать поток на HTTP-таймауте нельзя. Порт TruckTel определяется
        /// динамически (см. MainForm.IsGamePaused — 8080 в новых сборках МЁРТВ).
        /// </summary>
        private async Task<bool> IsGamePausedAsync()
        {
            await Task.Yield();
            return await Task.Run(IsGamePaused);
        }
    }
}