using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F1 = 0x70;
        private const int VK_PAUSE = 0x13;
        private const int SW_RESTORE = 9;
        private const uint KEYEVENTF_KEYUP = 0x02;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x01;
        private const uint INPUT_KEYBOARD = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEKEYBDHARDWAREINPUT union;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct MOUSEKEYBDHARDWAREINPUT
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { /* не используется */ }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { /* не используется */ }

        private bool _randomTargetActive = false;
        private bool _randomTargetReached = false;
        private double _randomTargetX = 0;
        private double _randomTargetZ = 0;
        private string _randomTargetName = "Случайная цель";

        private void BtnRandomTarget_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("add_random_target");
            _randomTargetActive = true;
            _randomTargetReached = false;
            AppendLog("Отправлена команда на создание случайной цели.");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Случайная цель создана.", ToolTipIcon.Info);
        }

        private void BtnRandomTarget2_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("add_random_target_2");
            _randomTargetActive = true;
            _randomTargetReached = false;
            AppendLog("Отправлена команда на создание случайной цели 2.");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Случайная цель 2 создана.", ToolTipIcon.Info);
        }

        private void BtnRandomTarget3_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("add_random_target_100");
            _randomTargetActive = true;
            _randomTargetReached = false;
            AppendLog("Отправлена команда на создание случайной цели 100м.");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Случайная цель 100м создана.", ToolTipIcon.Info);
        }

        private void OnClientCommand(JObject data)
        {
            var command = data["command"]?.Value<string>();
            if (string.IsNullOrEmpty(command)) return;
            AppendLog($"[WS Command] {command}");

            switch (command)
            {
                case "target_reached":
                    var reachedTarget = data["target"];
                    AppendDataLog($"target_reached x={reachedTarget?["x"]} z={reachedTarget?["z"]}");
                    // ACK immediately so the map can stop retrying while the
                    // modal quest dialog is open on the GUI thread.
                    SendCommandToMap("target_reached_ack");
                    HandleTargetReached(data);
                    break;
                case "target_created":
                    var createdTarget = data["target"];
                    AppendDataLog($"target_created x={createdTarget?["x"]} z={createdTarget?["z"]}");
                    HandleTargetCreated(data);
                    break;
                default:
                    AppendLog($"[WS Command] Неизвестная команда: {command}");
                    break;
            }
        }

        private void HandleTargetCreated(JObject data)
        {
            var target = data["target"];
            if (target != null)
            {
                _randomTargetX = target["x"]?.Value<double>() ?? 0;
                _randomTargetZ = target["z"]?.Value<double>() ?? 0;
                _randomTargetName = target["name"]?.Value<string>() ?? "Случайная цель";
                _randomTargetActive = true;
                _randomTargetReached = false;
                AppendLog($"[WS] Случайная цель создана на координатах ({_randomTargetX}, {_randomTargetZ})");
            }
        }

        // ============================================================
        // ПАУЗА ETS2 ЧЕРЕЗ ets2_assist_input.dll (Named Pipe)
        // ============================================================
        private bool SetGamePause(bool enabled)
        {
            AppendLog($"[SCS] SetGamePause({enabled})");
            bool ok = SCSController.SetPause(enabled);
            AppendLog(ok
                ? $"[SCS] Игра {(enabled ? "поставлена на паузу" : "снята с паузы")} через SDK."
                : $"[SCS] Не удалось {(enabled ? "поставить игру на паузу" : "снять игру с паузы")} через SDK.");
            return ok;
        }

        private bool ForceForegroundWindow(IntPtr hWnd, string reason)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            uint currentThread = GetCurrentThreadId();
            uint targetThread = GetWindowThreadProcessId(hWnd, IntPtr.Zero);
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = foreground != IntPtr.Zero
                ? GetWindowThreadProcessId(foreground, IntPtr.Zero)
                : 0;

            bool attached = false;
            try
            {
                // Windows can reject SetForegroundWindow when another process owns
                // the foreground queue (ETS2 in our case). Temporarily sharing the
                // input queue removes that restriction for this transition.
                if (foregroundThread != 0 && foregroundThread != currentThread)
                {
                    attached = AttachThreadInput(foregroundThread, currentThread, true);
                }

                if (targetThread != 0 && targetThread != currentThread && !attached)
                {
                    attached = AttachThreadInput(targetThread, currentThread, true);
                }

                ShowWindow(hWnd, SW_RESTORE);
                BringWindowToTop(hWnd);
                bool result = SetForegroundWindow(hWnd);
                SetActiveWindow(hWnd);
                SetFocus(hWnd);

                IntPtr foregroundAfter = GetForegroundWindow();
                bool foregroundConfirmed = foregroundAfter == hWnd;
                AppendLog($"[UI] ForceForegroundWindow({reason}) handle=0x{hWnd.ToInt64():X}, SetForegroundWindow={result}, foregroundAfter=0x{foregroundAfter.ToInt64():X}, confirmed={foregroundConfirmed}");
                return foregroundConfirmed;
            }
            finally
            {
                if (attached)
                {
                    // Detach the same pair we attached.
                    if (foregroundThread != 0 && foregroundThread != currentThread)
                        AttachThreadInput(foregroundThread, currentThread, false);
                    else if (targetThread != 0 && targetThread != currentThread)
                        AttachThreadInput(targetThread, currentThread, false);
                }
            }
        }

        private void ReturnFocusToGame()
        {
            try
            {
                var procs = Process.GetProcessesByName("eurotrucks2");
                var gameHandle = procs.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)?.MainWindowHandle ?? IntPtr.Zero;
                if (gameHandle != IntPtr.Zero && ForceForegroundWindow(gameHandle, "return-to-game"))
                {
                    AppendLog("[UI] Фокус возвращён в игру.");
                }
                else
                {
                    AppendLog("[UI] Не удалось надёжно вернуть фокус в игру.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка возврата фокуса: {ex.Message}");
            }
        }

        private void HandleTargetReached(JObject data)
        {
            AppendLog("[WS] HandleTargetReached вызван");

            if (!_randomTargetActive || _randomTargetReached)
            {
                AppendLog("[WS] Цель не активна или уже обработана. Игнорируем.");
                return;
            }

            _randomTargetReached = true;

            // Критически важно: сначала проверяем фактическую паузу.
            // Команда PAUSE у проверенного SDK-плагина является toggle,
            // поэтому нельзя отправлять её, если игра уже находится на паузе.
            AppendLog("[SCS] Проверяем состояние ETS2 перед показом диалога...");
            bool alreadyPaused = IsGamePaused();
            if (alreadyPaused)
            {
                AppendLog("[SCS] ETS2 уже была на паузе — toggle PAUSE не отправляем.");
            }
            else
            {
                bool sent = SetGamePause(true);
                AppendLog(sent
                    ? "[SCS] Команда PAUSE отправлена. Проверяем фактическое состояние игры."
                    : "[SCS] Не удалось отправить PAUSE через SDK.");
            }

            // Не используем ответ Named Pipe как критерий успеха:
            // plugin уже показал, что команда может выполнить PAUSE без корректного ACK.
            bool paused = false;
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                System.Threading.Thread.Sleep(100);
                if (IsGamePaused())
                {
                    paused = true;
                    AppendLog($"[SCS] Фактическое состояние подтверждено: ETS2 paused=true (попытка {attempt}).");
                    break;
                }
            }

            if (!paused)
            {
                _randomTargetReached = false;
                AppendLog("[SCS] Игра не перешла в состояние paused=true. Диалог завершения задания НЕ показываем.");
                SendCommandToMap("reset_target_reached");
                return;
            }

            this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
            {
                bool wasTopMost = this.TopMost;

                try
                {
                    // Убираем фокус с ETS2 и делаем окно приложения владельцем диалога.
                    this.TopMost = true;
                    this.Show();
                    this.WindowState = FormWindowState.Normal;
                    this.Activate();
                    ForceForegroundWindow(this.Handle, "before-quest-dialog");

                    AppendLog($"[UI] Фокус передан окну ETS2 Assist перед диалогом. OwnerHandle=0x{this.Handle.ToInt64():X}, IsHandleCreated={this.IsHandleCreated}, Visible={this.Visible}, TopMost={this.TopMost}");

                    // Используем собственную модальную форму вместо MessageBox.
                    // У неё есть отдельный HWND, поэтому мы можем надёжно передать
                    // foreground и mouse/keyboard focus от ETS2 к самому диалогу.
                    using var dialog = new QuestDialogForm(
                        "Достижение цели",
                        "Вы достигли случайной цели. Завершить задание?",
                        isSuccess: false);
                    dialog.Shown += (_, _) =>
                    {
                        ForceForegroundWindow(dialog.Handle, "quest-completion-dialog");
                        dialog.BringToFront();
                        dialog.Activate();
                    };
                    var result = dialog.ShowDialog(this);

                    if (result == DialogResult.Yes)
                    {
                        AppendLog("[QUEST] Игрок подтвердил завершение случайного задания.");
                        string ets2cPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "ets2c.exe");
                        if (File.Exists(ets2cPath))
                        {
                            try
                            {
                                var startInfo = new ProcessStartInfo
                                {
                                    FileName = ets2cPath,
                                    Arguments = "-moneygive 3000",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WindowStyle = ProcessWindowStyle.Hidden
                                };
                                Process.Start(startInfo);

                                startInfo.Arguments = "-xpgive 150";
                                Process.Start(startInfo);
                                AppendLog("Начислено 3000 денег и 150 опыта.");
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"Ошибка ets2c: {ex.Message}");
                            }
                        }
                        else
                        {
                            AppendLog("ets2c.exe не найден — награда не начислена.");
                        }

                        SendCommandToMap("remove_random_target");
                        _randomTargetActive = false;
                        _randomTargetReached = false;
                        using var successDialog = new QuestDialogForm(
                            "Успех",
                            "Задание выполнено!",
                            isSuccess: true);
                        successDialog.Shown += (_, _) =>
                        {
                            ForceForegroundWindow(successDialog.Handle, "quest-success-dialog");
                            successDialog.BringToFront();
                            successDialog.Activate();
                        };
                        successDialog.ShowDialog(this);
                    }
                    else
                    {
                        AppendLog("[QUEST] Игрок отказался завершать случайное задание.");
                        _randomTargetReached = false;
                        SendCommandToMap("reset_target_reached");
                        AppendLog("Пользователь отклонил задание.");
                    }
                }
                finally
                {
                    this.TopMost = wasTopMost;

                    // ВАЖНО: после завершения задания НЕ снимаем паузу автоматически.
                    // Игрок сам возвращается в игру и снимает паузу, когда готов.
                    AppendLog("[SCS] Игра оставлена на паузе. Автоматический UNPAUSE отключён.");
                    ReturnFocusToGame();
                }
            });
        }
    }
}