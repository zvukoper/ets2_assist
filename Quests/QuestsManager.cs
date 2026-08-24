using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_PAUSE = 0x13; // не используется, но можно задействовать

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

        private void SendPauseKeyToGame()
        {
            try
            {
                IntPtr gameHandle = IntPtr.Zero;
                var procs = Process.GetProcessesByName("eurotrucks2");
                if (procs.Length > 0 && procs[0].MainWindowHandle != IntPtr.Zero)
                {
                    gameHandle = procs[0].MainWindowHandle;
                }

                if (gameHandle == IntPtr.Zero)
                {
                    AppendLog("[DEBUG] Окно игры не найдено.");
                    return;
                }

                // Активируем окно игры
                SetForegroundWindow(gameHandle);
                System.Threading.Thread.Sleep(50);

                // Отправляем нажатие ESC через keybd_event
                const uint KEYEVENTF_KEYUP = 0x02;
                keybd_event((byte)VK_ESCAPE, 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(50);
                keybd_event((byte)VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                AppendLog("[DEBUG] Клавиша ESC отправлена в окно игры через keybd_event.");
            }
            catch (Exception ex)
            {
                AppendLog($"[DEBUG] Ошибка отправки ESC: {ex.Message}");
            }
        }

        private void ReturnFocusToGame()
        {
            try
            {
                IntPtr gameHandle = IntPtr.Zero;
                var procs = Process.GetProcessesByName("eurotrucks2");
                if (procs.Length > 0 && procs[0].MainWindowHandle != IntPtr.Zero)
                {
                    gameHandle = procs[0].MainWindowHandle;
                }
                if (gameHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(gameHandle);
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
            if (!_randomTargetActive || _randomTargetReached)
            {
                AppendLog("[WS] Цель не активна или уже обработана. Игнорируем.");
                return;
            }

            _randomTargetReached = true;

            // Ставим игру на паузу
            SendPauseKeyToGame();

            this.Invoke((System.Windows.Forms.MethodInvoker)delegate
            {
                // Делаем окно приложения активным и поверх всех
                bool wasTopMost = this.TopMost;
                this.TopMost = true;
                this.Activate();

                var result = MessageBox.Show(
                    this,
                    "Вы достигли случайной цели. Завершить задание?",
                    "Достижение цели",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1,
                    MessageBoxOptions.DefaultDesktopOnly); // Окно поверх

                this.TopMost = wasTopMost;

                // Возвращаем фокус в игру
                ReturnFocusToGame();

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
                            AppendLog("Начислено 3000 денег и 150 опыта (скрыто).");
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"Ошибка ets2c: {ex.Message}");
                        }
                    }

                    SendCommandToMap("remove_random_target");
                    _randomTargetActive = false;
                    _randomTargetReached = false;

                    MessageBox.Show(this, "Задание выполнено!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ReturnFocusToGame(); // ещё раз возвращаем фокус
                }
                else
                {
                    _randomTargetReached = false;
                    SendCommandToMap("reset_target_reached");
                    AppendLog("Пользователь отклонил задание.");
                }
            });
        }
    }
}