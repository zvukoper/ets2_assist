using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // Состояние случайной цели (серверная часть)
        private bool _randomTargetActive = false;
        private bool _randomTargetReached = false;
        private double _randomTargetX = 0;
        private double _randomTargetZ = 0;
        private string _randomTargetName = "Случайная цель";

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

        private void HandleTargetReached(JObject data)
        {
            // Проверяем, активна ли цель и не обработана ли уже
            if (!_randomTargetActive || _randomTargetReached)
            {
                AppendLog("[WS] Цель не активна или уже обработана. Игнорируем.");
                return;
            }

            _randomTargetReached = true;

            // Ставим игру на паузу (если возможно)
            Task.Run(() => PauseGame());

            // Показываем диалог с принудительным фокусом
            this.Invoke((System.Windows.Forms.MethodInvoker)delegate
            {
                // Сохраняем текущее состояние TopMost
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
                    MessageBoxOptions.DefaultDesktopOnly);

                this.TopMost = wasTopMost;

                if (result == DialogResult.Yes)
                {
                    // Начисляем деньги и опыт
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

                    // Удаляем случайную цель
                    SendCommandToMap("remove_random_target");
                    _randomTargetActive = false;
                    _randomTargetReached = false;

                    MessageBox.Show(this, "Задание выполнено!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    // Пользователь отклонил
                    _randomTargetReached = false;
                    SendCommandToMap("reset_target_reached");
                    AppendLog("Пользователь отклонил завершение задания. Ожидаем повторного приближения.");
                }
            });
        }

        // Постановка игры на паузу через TruckTel API
        private async Task PauseGame()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    // Попытка отправить нажатие клавиши Pause
                    var response = await client.PostAsync("http://localhost:8080/api/rest/input/press/pause", null);
                    if (response.IsSuccessStatusCode)
                    {
                        AppendLog("Игра поставлена на паузу через TruckTel.");
                    }
                    else
                    {
                        // Попробуем альтернативный способ: эмуляция клавиши Escape или Pause
                        await client.PostAsync("http://localhost:8080/api/rest/input/press/escape", null);
                        AppendLog("Попытка паузы через Escape.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при постановке игры на паузу: {ex.Message}");
            }
        }
    }
}