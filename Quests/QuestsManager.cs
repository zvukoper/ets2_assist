using System;
using System.Diagnostics;
using System.IO;
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

        private void OnClientCommand(JObject data)
        {
            var command = data["command"]?.Value<string>();
            if (string.IsNullOrEmpty(command)) return;
            AppendLog($"[WS Command] {command}");

            switch (command)
            {
                case "target_reached":
                    HandleTargetReached(data);
                    break;
                case "target_created":
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
        // ОТПРАВКА КЛАВИШИ F1 (пауза) через SendInput + PostMessage
        // ============================================================
        private void SendPauseKeyToGame()
        {
            try
            {
                AppendLog("[DEBUG] SendPauseKeyToGame() вызван");

                var procs = Process.GetProcessesByName("eurotrucks2");
                if (procs.Length == 0 || procs[0].MainWindowHandle == IntPtr.Zero)
                {
                    AppendLog("[DEBUG] Окно игры не найдено.");
                    return;
                }

                IntPtr gameHandle = procs[0].MainWindowHandle;

                // Восстанавливаем окно
                ShowWindow(gameHandle, SW_RESTORE);
                SetForegroundWindow(gameHandle);
                System.Threading.Thread.Sleep(100);

                // 1. Отправляем через PostMessage напрямую в окно (быстро, но не всегда работает)
                AppendLog("[DEBUG] Отправка PostMessage WM_KEYDOWN/WM_KEYUP VK_F1");
                PostMessage(gameHandle, WM_KEYDOWN, (IntPtr)VK_F1, IntPtr.Zero);
                System.Threading.Thread.Sleep(30);
                PostMessage(gameHandle, WM_KEYUP, (IntPtr)VK_F1, IntPtr.Zero);

                // 2. Отправляем через SendInput (более надёжно)
                AppendLog("[DEBUG] Отправка SendInput VK_F1");
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].union.keyboard.wVk = (ushort)VK_F1;
                inputs[0].union.keyboard.dwExtraInfo = GetMessageExtraInfo();

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].union.keyboard.wVk = (ushort)VK_F1;
                inputs[1].union.keyboard.dwFlags = KEYEVENTF_KEYUP;
                inputs[1].union.keyboard.dwExtraInfo = GetMessageExtraInfo();

                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));

                AppendLog("[DEBUG] Клавиша F1 отправлена через SendInput.");
            }
            catch (Exception ex)
            {
                AppendLog($"[DEBUG] Ошибка отправки F1: {ex.Message}");
            }
        }

        private void ReturnFocusToGame()
        {
            try
            {
                var procs = Process.GetProcessesByName("eurotrucks2");
                if (procs.Length > 0 && procs[0].MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(procs[0].MainWindowHandle);
                    AppendLog("Фокус возвращён в игру.");
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

            // Ставим паузу
            AppendLog("[DEBUG] Вызов SendPauseKeyToGame() для установки паузы");
            SendPauseKeyToGame();

            // Показываем диалог
            this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
            {
                bool wasTopMost = this.TopMost;
                this.TopMost = true;
                this.Activate();

                AppendLog("[UI] Показываем диалог достижения цели...");

                var result = MessageBox.Show(
                    "Вы достигли случайной цели. Завершить задание?",
                    "Достижение цели",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.DefaultDesktopOnly);

                this.TopMost = wasTopMost;

                // Снимаем паузу
                AppendLog("[DEBUG] Снимаем паузу (отправляем F1 ещё раз)");
                SendPauseKeyToGame();

                if (result == DialogResult.Yes)
                {
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

                    SendCommandToMap("remove_random_target");
                    _randomTargetActive = false;
                    _randomTargetReached = false;
                    MessageBox.Show("Задание выполнено!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _randomTargetReached = false;
                    SendCommandToMap("reset_target_reached");
                    AppendLog("Пользователь отклонил задание.");
                }

                ReturnFocusToGame();
            });
        }
    }
}