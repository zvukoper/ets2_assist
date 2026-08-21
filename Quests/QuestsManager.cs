using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // Состояние случайной цели
        private bool _randomTargetActive = false;
        private bool _randomTargetReached = false;

        // Обработчик кнопки "Случайная цель"
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

        // Обработка команд от клиента (через WebSocket)
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
                default:
                    AppendLog($"[WS Command] Неизвестная команда: {command}");
                    break;
            }
        }

        private void HandleTargetReached(JObject data)
        {
            if (_randomTargetReached) return;
            _randomTargetReached = true;

            this.Invoke((System.Windows.Forms.MethodInvoker)delegate
            {
                var result = MessageBox.Show(
                    "Вы достигли случайной цели. Завершить задание?",
                    "Достижение цели",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    string ets2cPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "ets2c.exe");
                    if (File.Exists(ets2cPath))
                    {
                        try
                        {
                            Process.Start(ets2cPath, "-moneygive 3000");
                            Process.Start(ets2cPath, "-xpgive 150");
                            AppendLog("Начислено 3000 денег и 150 опыта.");
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"Ошибка выполнения ets2c: {ex.Message}");
                        }
                    }
                    else
                    {
                        AppendLog("ets2c.exe не найден. Начисление не выполнено.");
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
                    AppendLog("Пользователь отклонил завершение задания. Ожидаем повторного приближения.");
                }
            });
        }

        // Отправка команды на все подключенные клиенты (карту)
        private void SendCommandToMap(string command, JObject? extra = null)
        {
            if (_wsSaveRunning && _wsSaveServer != null)
            {
                var msg = new JObject();
                msg["command"] = command;
                if (extra != null)
                {
                    foreach (var prop in extra.Properties())
                        msg[prop.Name] = prop.Value;
                }
                _wsSaveServer.WebSocketServices.Broadcast(msg.ToString(Formatting.None));
                AppendLog($"Command '{command}' sent to map.");
            }
            else
            {
                AppendLog($"Cannot send command '{command}': WebSocket save server is not running.");
            }
        }
    }
}