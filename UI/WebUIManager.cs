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
        private int _pauseSnapshot = -1;
        private long _pauseCheckTicks;
        private long _pauseCheckLastLogged;

        private enum QuestPauseFlowState
        {
            Running,
            WaitingForPause,
            InteractivePaused,
            WaitingForResume
        }

        private QuestPauseFlowState _questPauseFlow = QuestPauseFlowState.Running;
        private bool _questInteractiveShellVisible;
        private bool _validatedPaused;
        private long _questEscapeStartedAt;
        private int _pauseTrueStreak;
        private int _pauseFalseStreak;

        private const int PauseCheckIntervalMs = 50;

        // Интерактивный shell не ждёт анимацию стандартного ESC-меню ETS2.
        // Наша веб-страница сама делает единственный 150-мс fade; большой
        // 5.5-секундный lead здесь запрещён.
        private const int QuestPauseUiLeadMs = 150;
        private const int QuestPauseUiTimeoutMs = 6500;
        private const int PauseConfirmSamples = 2;

        private void StartPauseCheck()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
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
            AppendLog($"[UI] Политика ESC/паузы запущена: опрос {PauseCheckIntervalMs} мс, ESC — единственный триггер интерактива.");
        }

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
                handed = true;
            }
            catch { }
            finally
            {
                if (!handed) Volatile.Write(ref _pauseCheckBusy, 0);
            }
        }

        private void LogPauseCheckAlive(bool gameRunning, bool paused, bool gameFocused)
        {
            long ticks = Interlocked.Increment(ref _pauseCheckTicks);
            if (ticks - Interlocked.Read(ref _pauseCheckLastLogged) < 300) return;
            Volatile.Write(ref _pauseCheckLastLogged, ticks);
            AppendDataLog(
                $"[UI] политика жива: тиков={ticks} running={gameRunning} paused={paused} " +
                $"focus={gameFocused} flow={_questPauseFlow} shell={_questInteractiveShellVisible} " +
                $"active={_committedActive} ({_focusDiagnostics})");
        }

        private void CheckPauseAndUpdateUI()
        {
            bool gameRunning = IsGameRunning();
            bool gameFocused = IsGameFocused();

            if (_debugMode)
            {
                RestoreOverlayLayers();
                bool debugVisible = gameRunning && gameFocused;
                SetQuestInputHookActive(debugVisible);
                SetQuestToggleHotkeyActive(_questInteractiveShellVisible);
                ApplyOverlayVisibility(debugVisible, _questInteractiveShellVisible, gameFocused);
                return;
            }

            int snap = Volatile.Read(ref _pauseSnapshot);
            bool paused = snap < 0 ? _pausedIntent : snap == 1;
            _validatedPaused = paused;

            try { Quests.QuestRuntime.Current?.SetHostPauseState(paused); } catch { }

            // ESC слушается только когда foreground принадлежит ETS2. Оно никогда
            // не превращается в глобальный перехват для остальных приложений.
            SetQuestInputHookActive(gameRunning && gameFocused);

            bool debugShow = AR.ArBridge.DebugShow;
            bool gameVisible = gameRunning && (debugShow || gameFocused);

            if (gameVisible != _committedActive)
            {
                _activeMismatch++;
                if (_activeMismatch < 2) return;
                _committedActive = gameVisible;
                _activeMismatch = 0;
            }
            else _activeMismatch = 0;

            if (!_committedActive)
            {
                ResetQuestPauseFlowForFocusLoss();
                UpdateOverlayLayerFocus(false);
                ApplyOverlayVisibility(false, false, false);
                return;
            }

            AdvanceQuestPauseFlow(paused);

            bool showInteractive = _questInteractiveShellVisible;
            // Категории строго взаимоисключающие: пока игра на паузе,
            // игровая категория НИКОГДА не может быть принудительно показана
            // ручным переключателем миникарты. Возврат game-категории выполняется
            // только после подтверждённого выхода из паузы.
            bool showGameUi = gameVisible &&
                              !paused &&
                              !_questInteractiveShellVisible &&
                              _questPauseFlow != QuestPauseFlowState.WaitingForResume;

            UpdateOverlayLayerFocus(debugShow || _committedActive);
            ApplyOverlayVisibility(showGameUi, showInteractive, gameFocused);
            LogPauseCheckAlive(gameRunning, paused, gameFocused);
        }

        private void AdvanceQuestPauseFlow(bool paused)
        {
            long now = Environment.TickCount64;

            // Важно: после двух подтверждений паузы состояние уже становится
            // InteractivePaused, поэтому старый код, который ждал QuestPauseUiLeadMs
            // только внутри WaitingForPause, больше никогда не доходил до показа
            // закладки. Отложенный показ проверяем независимо от состояния автомата.
            if (paused &&
                _questEscapeStartedAt > 0 &&
                !_questInteractiveShellVisible &&
                now - _questEscapeStartedAt >= QuestPauseUiLeadMs)
            {
                ShowQuestPauseShell();
            }

            switch (_questPauseFlow)
            {
                case QuestPauseFlowState.Running:
                    _pauseTrueStreak = 0;
                    _pauseFalseStreak = 0;
                    break;

                case QuestPauseFlowState.WaitingForPause:
                    if (paused)
                    {
                        _pauseTrueStreak++;
                        _pauseFalseStreak = 0;
                    }
                    else
                    {
                        _pauseTrueStreak = 0;
                        _pauseFalseStreak++;
                    }

                    // Отложенный показ shell выполняется перед switch для всех пауз.

                    if (paused && _pauseTrueStreak >= PauseConfirmSamples)
                    {
                        _questPauseFlow = QuestPauseFlowState.InteractivePaused;
                        SendCommandToMap("quest_pause_ui", new JObject
                        {
                            ["visible"] = true,
                            ["ready"] = true,
                            ["pulse"] = false,
                            ["hasInteractive"] = _questHasInteractive
                        });
                        try { Quests.QuestRuntime.Current?.SetInteractiveVisible(true, false); } catch { }
                    }
                    else if (!paused &&
                             now - _questEscapeStartedAt >= QuestPauseUiTimeoutMs)
                    {
                        HideQuestPauseShell();
                        _questPauseFlow = QuestPauseFlowState.Running;
                    }
                    break;

                case QuestPauseFlowState.InteractivePaused:
                    if (paused)
                    {
                        _pauseFalseStreak = 0;
                        _pauseTrueStreak++;
                    }
                    else
                    {
                        _pauseTrueStreak = 0;
                        _pauseFalseStreak++;
                    }
                    break;

                case QuestPauseFlowState.WaitingForResume:
                    if (paused)
                    {
                        _pauseFalseStreak = 0;
                        _pauseTrueStreak++;
                    }
                    else
                    {
                        _pauseTrueStreak = 0;
                        _pauseFalseStreak++;
                    }

                    if (!paused && _pauseFalseStreak >= PauseConfirmSamples)
                    {
                        _questPauseFlow = QuestPauseFlowState.Running;
                        _pauseTrueStreak = 0;
                        _pauseFalseStreak = 0;
                        AppendLog("[UI] Подтверждён выход из паузы — игровые интерфейсы возвращаются.");
                    }
                    break;
            }
        }

        private void OnQuestEscapeKeyDetected()
        {
            if (!_committedActive || !IsQuestHookGameForeground()) return;

            AppendLog($"[QUEST] ESC detected flow={_questPauseFlow} validatedPaused={_validatedPaused} nonEscMenuGuard={_questNonEscMenuGuard}");

            /* ESC после F1-F12/PAUSE относится к открытому игровому окну.
               Первый ESC только закрывает его; наш интерактив запускается
               следующим отдельным ESC уже на главном экране. */
            if (_questNonEscMenuGuard)
            {
                _questNonEscMenuGuard = false;
                _questNonEscMenuGuardVk = 0;
                if (_questPauseFlow == QuestPauseFlowState.WaitingForPause)
                {
                    HideQuestPauseShell();
                    _questPauseFlow = QuestPauseFlowState.Running;
                }
                AppendLog("[QUEST] ESC ignored: открыт режим F1-F12/PAUSE.");
                return;
            }

            if (_questPauseFlow == QuestPauseFlowState.InteractivePaused)
            {
                BeginQuestResumeWait("ESC while interactive paused");
                return;
            }

            if (_questPauseFlow == QuestPauseFlowState.WaitingForPause)
            {
                // Второй ESC после валидации паузы = закрытие нашего интерактива.
                if (_validatedPaused)
                    BeginQuestResumeWait("second ESC while pause validated");
                return;
            }

            // F1-F12/PAUSE могли открыть внутриигровое меню. Их ESC здесь не
            // превращается в наш триггер: если пауза уже подтверждена, ничего не делаем.
            if (_validatedPaused) return;

            if (_questPauseFlow == QuestPauseFlowState.Running)
            {
                _questEscapeStartedAt = Environment.TickCount64;
                _pauseTrueStreak = 0;
                _pauseFalseStreak = 0;
                _questPauseFlow = QuestPauseFlowState.WaitingForPause;
                AppendLog($"[QUEST] ESC -> ожидание главной паузы; shell через {QuestPauseUiLeadMs} мс.");
            }
        }

        private void ShowQuestPauseShell()
        {
            if (_questInteractiveShellVisible) return;

            _questInteractiveShellVisible = true;
            SendCommandToMap("hide_ui");
            SendCommandToMap("hide_game_ui");
            // Пауза всегда имеет приоритет над ручным режимом «Показать карту»:
            // сначала гасим auto-force, затем непосредственно карту.
            SendCommandToMap("minimap_auto", new JObject { ["enabled"] = false });
            SendCommandToMap("minimap_hide");
            SendCommandToMap("set_overlay_category", new JObject { ["category"] = "interactive" });
            SendCommandToMap("set_quest_collapsed", new JObject { ["collapsed"] = true });
            SendCommandToMap("quest_pause_ui", new JObject
            {
                ["visible"] = true,
                ["ready"] = false,
                ["pulse"] = !_questHasInteractive,
                ["hasInteractive"] = _questHasInteractive
            });
            try { Quests.QuestRuntime.Current?.SetInteractiveVisible(true, true); } catch { }
            PushQuestInteractiveSignal();
            AppendLog("[QUEST] Главный ESC: закладка квестов выдвигается.");
        }

        private void HideQuestPauseShell()
        {
            bool changed = _questInteractiveShellVisible;
            _questInteractiveShellVisible = false;
            SendCommandToMap("quest_pause_ui", new JObject
            {
                ["visible"] = false,
                ["ready"] = false,
                ["pulse"] = false
            });
            try { Quests.QuestRuntime.Current?.SetInteractiveVisible(false, false); } catch { }
            if (changed) AppendLog("[QUEST] Интерактивный shell скрыт.");
        }

        private void BeginQuestResumeWait(string reason)
        {
            _questPauseFlow = QuestPauseFlowState.WaitingForResume;
            _pauseTrueStreak = 0;
            _pauseFalseStreak = 0;
            HideQuestPauseShell();
            AppendLog($"[QUEST] {reason}: ждём подтверждения выхода из паузы.");
        }

        private void ResetQuestPauseFlowForFocusLoss()
        {
            if (_questPauseFlow != QuestPauseFlowState.Running || _questInteractiveShellVisible)
                HideQuestPauseShell();

            _questPauseFlow = QuestPauseFlowState.Running;
            _pauseTrueStreak = 0;
            _pauseFalseStreak = 0;
            _questEscapeStartedAt = 0;
            _validatedPaused = false;
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
            bool gameUiChanged = showGameUi != _lastGameUiVisible;
            bool interactiveChanged = showInteractive != _lastInteractiveVisible;

            if (gameUiChanged)
            {
                _lastGameUiVisible = showGameUi;
                _lastUiVisible = showGameUi;
                _lastPauseState = _validatedPaused;

                if (!showGameUi)
                {
                    SendCommandToMap("hide_ui");
                    SendCommandToMap("hide_game_ui");
                    // Критично: ручной режим миникарты не должен переживать паузу.
                    // Авто-режим будет восстановлен только после выхода из неё.
                    if (_lastMinimapAuto != false)
                    {
                        _lastMinimapAuto = false;
                        SendCommandToMap("minimap_auto", new JObject { ["enabled"] = false });
                    }
                    if (_lastMinimapVisible != false)
                    {
                        _lastMinimapVisible = false;
                        SendCommandToMap("minimap_hide");
                    }
                    AppendLog("[UI] Игровые интерфейсы скрыты.");
                }
                else
                {
                    SendCommandToMap(_uiShown ? "show_ui" : "show_ui_first");
                    SendCommandToMap("show_game_ui");
                    AppendLog(_uiShown
                        ? "[UI] Игровые интерфейсы показаны: гибрид + миникарта + уведомления."
                        : "[UI] Игровые интерфейсы показаны впервые: гибрид + миникарта + уведомления.");
                    _uiShown = true;
                }
            }

            // Миникарта подчиняется только текущей игровой категории.
            // Во время паузы/WaitingForResume она ВСЕГДА выключена, даже если
            // пользователь оставил ручной режим «Показать карту» включённым.
            if (!showGameUi)
            {
                if (_lastMinimapAuto != false)
                {
                    _lastMinimapAuto = false;
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = false });
                }
                if (_lastMinimapVisible != false)
                {
                    _lastMinimapVisible = false;
                    SendCommandToMap("minimap_hide");
                }
            }
            else if (_minimapAutoLogic)
            {
                if (_lastMinimapAuto != true)
                {
                    _lastMinimapAuto = true;
                    SendCommandToMap("minimap_auto", new JObject { ["enabled"] = true });
                }
                if (_lastMinimapVisible != true)
                {
                    _lastMinimapVisible = true;
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
                if (_lastMinimapVisible != true)
                {
                    _lastMinimapVisible = true;
                    SendCommandToMap("minimap_show");
                }
            }

            // Категория отправляется при ЛЮБОМ переходе состояния.
            // Раньше первый переход false->true мог показать game UI, но не
            // отправить set_overlay_category, потому что interactive оставался false.
            if (gameUiChanged || interactiveChanged)
            {
                if (showInteractive)
                {
                    SendCommandToMap("set_overlay_category",
                        new JObject { ["category"] = "interactive" });
                }
                else if (showGameUi)
                {
                    SendCommandToMap("set_overlay_category",
                        new JObject { ["category"] = "game" });
                }
            }

            if (interactiveChanged)
            {
                _lastInteractiveVisible = showInteractive;
                _lastPauseLogoVisible = showInteractive;

                if (showInteractive)
                {
                    SendCommandToMap("set_quest_collapsed", new JObject { ["collapsed"] = true });
                    PushQuestInteractiveSignal();
                }

                Logger.Current?.Workflow(
                    $"[QUEST-DIAG] state source=ESC-flow paused={_validatedPaused} " +
                    $"collapsed={_questCollapsed} interactive={showInteractive} " +
                    $"gameUi={showGameUi} focused={gameFocused}");
            }

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