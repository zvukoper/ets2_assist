using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
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
        private struct INPUT
        {
            public uint type;
            public INPUTUNION union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mouse;

            [FieldOffset(0)]
            public KEYBDINPUT keyboard;

            [FieldOffset(0)]
            public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // Состояние КАЖДОЙ активной случайной цели (поддержка нескольких одновременно).
        // Ключ — уникальный id цели (генерится в миникарте).
        private class RandomTargetState
        {
            public string Id = "";
            public double X = 0;
            public double Z = 0;
            public string Name = "Случайная цель";
            public double Radius = 50;
            public string QuestType = ""; // courier_pickup / courier_dropoff / stash / snack
            public string Color = "#ff0000";
            public bool Active = true;    // участвует в обзоре целей / имеет указатель за пределами
            public bool InZone = false;   // игрок находится в зоне цели
            public bool Armed = false;    // вошёл в зону -> триггер взведён, ждём выхода
            // КУЛДАУН на РЕАЛЬНОМ СИСТЕМНОМ ВРЕМЕНИ (UTC). Пока now < CooldownUntil —
            // цель неактивна (скрыта, триггер зоны отключён), сохраняется между перезапусками.
            public DateTime CooldownUntil = DateTime.MinValue;
        }
        private Dictionary<string, RandomTargetState> _randomTargets = new Dictionary<string, RandomTargetState>();
        private bool _overviewOn = false;

        private void BtnRandomTarget_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("quest_courier");
            AppendLog("Отправлена команда: Курьер (синяя точка, 100м у POI на дороге).");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Курьер: точка создана.", ToolTipIcon.Info);
        }

        private void BtnRandomTarget2_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("quest_stash");
            AppendLog("Отправлена команда: Тайник (жёлтая точка, 200м на POI у дороги, неактивна).");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Тайник: точка создана.", ToolTipIcon.Info);
        }

        private void BtnRandomTarget3_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно создать цель.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SendCommandToMap("quest_snack");
            AppendLog("Отправлена команда: Перекус (зелёная точка, 400м на POI у дороги).");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Перекус: точка создана.", ToolTipIcon.Info);
        }

        private void BtnRandomTarget4_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно переключить обзор.");
                MessageBox.Show("WebSocket сервер не запущен. Запустите систему.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _overviewOn = !_overviewOn;
            SendCommandToMap("set_overview", new JObject { ["enabled"] = _overviewOn });
            AppendLog($"Обзор целей {( _overviewOn ? "ВКЛ" : "ВЫКЛ")} (охват всех активных точек).");
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", $"Обзор целей: {(_overviewOn ? "вкл" : "выкл")}", ToolTipIcon.Info);
        }

        private void BtnCheckTargets_Click(object sender, EventArgs e)
        {
            if (!_wsSaveRunning || _wsSaveServer == null)
            {
                AppendLog("WebSocket сервер не запущен. Невозможно проверить точки.");
                return;
            }

            // Отладка: принудительно пересобираем пакет overrides (статика + overrides +
            // test_targets.json) и шлём на миникарту.
            SendTargetsOverridesData("check");
            LogCustomTargetsFromFile();
            AppendLog("Проверка точек: пакет overrides принудительно пересобран и отправлен на карту.");
        }

        // «Пометить в АР» (v70): рассчитываем точку пересечения центрального луча
        // взгляда с плоскостью высоты грузовика, ставим пометку AR (серый крестик)
        // и открываем её как НОВУЮ точку в редакторе карты (EnterCreateMode).
        private void BtnArPin_Click(object sender, EventArgs e)
        {
            AppendLog("[AR] Кнопка «Пометить в АР» нажата.");
            PlacePinFromArAndOpenEditor();
        }

        // «Пометить в АР» (v73 фидбек): ТОЛЬКО пометка в AR-оверлее. Редактор карты
        // НЕ открываем и НЕ трогаем (Shift+Ctrl+X не должен перекл. фокус с игры).
        // Точка на миникарте будет видна как pin (отдельная команда ar_pin_map).
        private void PlacePinFromArAndOpenEditor()
        {
            try
            {
                ArPlacePinFromViewCenter();
                var pin = GetArPin();
                if (pin == null) return;
                // v73: журнал выбора новой точки (Logs\new_object_po_selections.txt).
                LogNewPointSelection(pin.Value.x, pin.Value.y, pin.Value.z);
                // Пометка на миникарте — той же иконкой (кружок+крест).
                SendCommandToMap("ar_pin_map", new JObject
                {
                    ["active"] = true,
                    ["x"] = pin.Value.x, ["y"] = pin.Value.y, ["z"] = pin.Value.z
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Ошибка пометки из АР: {ex.Message}");
            }
        }

        private void OnClientCommand(JObject data)
        {
            var command = data["command"]?.Value<string>();
            if (string.IsNullOrEmpty(command)) return;
            AppendLog($"[WS Command] {command}");

            switch (command)
            {
                case "target_reached":
                    // УСТАРЕЛО: миникарта больше не шлёт target_reached (теперь
                    // target_zone_enter / target_zone_leave). Оставлено для совместимости.
                    AppendLog("[WS] target_reached проигнорирован (устарело, используется target_zone_*).");
                    break;
                case "target_created":
                    var createdTarget = data["target"];
                    AppendDataLog($"target_created x={createdTarget?["x"]} z={createdTarget?["z"]}");
                    RegisterTargetFromMap(data["target"]);
                    break;
                case "targets_data_ack":
                    // Совместимость: старые подтверждения миникарты не нужны.
                    break;
                case "target_zone_enter":
                    {
                        var zt = data["id"]?.Value<string>();
                        if (zt != null && _randomTargets.TryGetValue(zt, out var st))
                        {
                            // КУЛДАУН (реальное системное время): пока не истёк — игнорируем вход.
                            if (st.CooldownUntil > DateTime.UtcNow)
                            {
                                AppendLog($"[TRIGGER] Цель {zt} ({st.Name}) на кулдауне до {st.CooldownUntil.ToLocalTime():HH:mm:ss} — вход проигнорирован.");
                                break;
                            }
                            st.InZone = true;
                            st.Armed = true;
                            AppendLog($"[TRIGGER] Вход в зону цели {zt} ({st.Name}, тип={st.QuestType}) — диалог задания.");
                            this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate { TriggerQuestDialog(st); });
                        }
                        break;
                    }
                case "target_zone_leave":
                    {
                        var zt = data["id"]?.Value<string>();
                        if (zt != null && _randomTargets.TryGetValue(zt, out var st))
                        {
                            // Завершение — по кнопке в диалоге, а не по выходу. Выход лишь
                            // сбрасывает арм, чтобы триггер реактивировался при повторном входе
                            // (Курьер-выдача: «НЕТ» -> ждём повторного входа).
                            st.InZone = false;
                            st.Armed = false;
                        }
                        break;
                    }
                case "remove_target":
                    {
                        var rt = data["id"]?.Value<string>();
                        if (rt != null)
                        {
                            AppendLog($"[WS] remove_target {rt} — удаляем из файла/словаря.");
                            RemoveTargetById(rt);
                        }
                        break;
                    }
                case "targets_list":
                    AppendLog("=== Список созданных точек ===");
                    if (data["targets"] is JArray list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var t = list[i];
                            AppendLog($"  [{i + 1}] name={t["name"]} x={t["x"]} z={t["z"]} дист. до фуры={t["dist"]}м");
                        }
                        AppendLog($"=== всего точек: {list.Count} ===");
                    }
                    else
                    {
                        AppendLog("  список точек пуст или не получен.");
                    }
                    break;
                case "targets_snapshot":
                    // Устаревшая диагностика: миникарта больше не шлёт снимки хранилища.
                    // Отладка теперь ведётся командой «Проверка точек» (чтение файла).
                    AppendLog("[WS] targets_snapshot проигнорирован (устарело).");
                    break;
                case "map_overrides_ack":
                    // КВИТАНЦИЯ от миникарты: пакет map_overrides_data Дошёл и ПРИМЕНЁН.
                    // Сверяем счётчики: если на миникарте нет custom-точек, а в отправке были —
                    // обрыв на отрисовке; если ack нет вовсе — обрыв доставки WS.
                    AppendLog($"[OVR] ACK от миникарты: seq={data["seq"]} ({data["reason"]}) — применено cities={data["cities"]}, pois={data["pois"]} (custom={data["custom"]}), targets={data["targets"]}");
                    break;
                case "map_ready":
                    // Миникарта готова — приложение шлёт полный пакет overrides (города/POI
                    // + цели из test_targets.json) командой map_overrides_data.
                    AppendLog("[WS] Миникарта готова -> отправляем overrides+цели");
                    MainForm.EnsureTestTargetsFileStatic();
                    SendTargetsOverridesData("map_ready");
                    break;
                case "request_reload_custom_targets":
                    // На всякий случай (совместимость) — полная пересылка пакета overrides.
                    SendTargetsOverridesData("request_reload");
                    break;
                case "add_target":
                    // Миникарта сгенерировала случайную цель и просит приложение записать её
                    // в test_targets.json (система overrides), затем прислать пакет заново.
                    AppendLog($"[WS] add_target x={data["target"]?["x"]} z={data["target"]?["z"]}");
                    AddTargetToFile(data["target"]);
                    break;
                case "ar_pin_set":
                    // «Пометить в АР» (кнопка миникарты): создаём точку на пересечении
                    // центрального луча взгляда с плоскостью высоты грузовика, открываем
                    // её как НОВУЮ точку в редакторе карты (v70).
                    AppendLog("[WS] ar_pin_set — пометка в АР (создание точки из взгляда).");
                    PlacePinFromArAndOpenEditor();
                    break;
                default:
                    AppendLog($"[WS Command] Неизвестная команда: {command}");
                    break;
            }
        }

        // Регистрируем цель в словаре активных (по id). Миникарта присылает
        // target_created сразу после генерации — здесь мы начинаем отслеживать цель.
        private void RegisterTargetFromMap(JToken target)
        {
            if (target == null) return;
            string id = target["id"]?.Value<string>() ?? "";
            if (string.IsNullOrEmpty(id)) return;
            var st = new RandomTargetState
            {
                Id = id,
                X = target["x"]?.Value<double>() ?? 0,
                Z = target["z"]?.Value<double>() ?? 0,
                Name = target["name"]?.Value<string>() ?? "Случайная цель",
                Radius = target["radius"]?.Value<double>() ?? 50,
                QuestType = target["questType"]?.Value<string>() ?? "",
                Color = target["color"]?.Value<string>() ?? "#ff0000",
                Active = target["active"]?.Value<bool>() ?? true,
                InZone = false,
                Armed = false
            };
            _randomTargets[id] = st;
            if (_randomTargets.TryGetValue(id, out var existing))
            {
                // Обновляем тип/цвет/активность, если цель уже зарегистрирована
                // (target_created может прийти раньше add_target с полными полями).
                existing.QuestType = st.QuestType;
                existing.Color = st.Color;
                existing.Active = st.Active;
                existing.X = st.X; existing.Z = st.Z; existing.Name = st.Name; existing.Radius = st.Radius;
            }
            var dist = target["dist"]?.Value<double>();
            var distStr = dist.HasValue ? $", дистанция до фуры = {Math.Round(dist.Value)} м" : "";
            AppendLog($"[WS] Точка создана/зарегистрирована: id={id}, x={st.X:F1}, z={st.Z:F1}{distStr} (всего активных: {_randomTargets.Count})");
            AppendDataLog($"target_created_logged id={id} x={st.X} z={st.Z} dist={dist}");
        }

        // ============================================================
        // РАБОТА С ФАЙЛОМ ЦЕЛЕЙ — map_overrides\test_targets.json
        // ============================================================
        // Цели тестовых кнопок живут в системе overrides (как все точки).
        // Приложение — единственный владелец файла; миникарта файл не трогает:
        // получает ГОТОВЫЙ пакет map_overrides_data и только рисует.

        // Полная пересборка и отправка пакета overrides на миникарту.
        internal void SendTargetsOverridesData(string reason) => SendMapOverridesToMap(reason);

        // Статическая обёртка для вызова из MainForm до подписок (инициализация).
        internal static void EnsureTestTargetsFileStatic()
        {
            try
            {
                var main = System.Windows.Forms.Application.OpenForms.OfType<MainForm>().FirstOrDefault();
                main?.EnsureTestTargetsFile();
            }
            catch { }
        }

        // Подробная отладка записи в файл целей: путь + содержимое файла после
        // операции (перечитываем то, что реально лежит на диске).
        private void LogTargetsFileDump(string label, string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    AppendLog($"[TARGETS][DEBUG] {label}: файл НЕ существует: {path}");
                    return;
                }
                string content = File.ReadAllText(path);
                AppendLog($"[TARGETS][DEBUG] {label}: путь={path}");
                AppendLog($"[TARGETS][DEBUG] {label}: содержимое={content}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS][DEBUG] {label}: ошибка чтения файла {path}: {ex.Message}");
            }
        }

        private void SendTargetsToMap()
        {
            // Совместимость: старое имя метода теперь отправляет полный пакет overrides.
            SendMapOverridesToMap("targets");
        }

        private void AddTargetToFile(JToken target)
        {
            try
            {
                if (target == null) return;
                double x = target["x"]?.Value<double>() ?? 0;
                double z = target["z"]?.Value<double>() ?? 0;
                string name = target["name"]?.Value<string>() ?? target["realName"]?.Value<string>() ?? "Случайная цель";
                string color = target["color"]?.Value<string>() ?? "default";
                string id = target["id"]?.Value<string>() ?? "";
                string questType = target["questType"]?.Value<string>() ?? "";
                bool active = target["active"]?.Value<bool>() ?? true;
                double radius = target["radius"]?.Value<double>() ?? 50;

                EnsureTestTargetsFile();
                string filePath = AppDataPaths.TestTargetsFile;
                JObject root;
                string existing = File.Exists(filePath) ? File.ReadAllText(filePath) : "";
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    try { root = JObject.Parse(existing); }
                    catch { root = new JObject(); }
                }
                else
                {
                    root = new JObject();
                }
                var arr = root["customTargets"] as JArray;
                if (arr == null) { arr = new JArray(); root["customTargets"] = arr; }

                // ЗАЩИТА ОТ ДУБЛЕЙ: каждый тип случайной цели (questType) может быть
                // в файле ТОЛЬКО В ОДНОМ ЭКЗЕМПЛЯРЕ. Если уже есть isRandom-цель того же
                // типа (другой id — например, при флаппинге WS или повторном нажатии),
                // удаляем старую из файла и из словаря активных, чтобы не плодились копии.
                if (!string.IsNullOrEmpty(questType) && target["isRandom"]?.Value<bool>() == true)
                {
                    var dupes = arr.OfType<JObject>()
                        .Where(i => (i["isRandom"]?.Value<bool>() == true)
                                 && (i["questType"]?.Value<string>() ?? "") == questType
                                 && (i["id"]?.Value<string>() ?? "") != id)
                        .ToList();
                    foreach (var d in dupes)
                    {
                        string did = d["id"]?.Value<string>() ?? "";
                        if (!string.IsNullOrEmpty(did)) _randomTargets.Remove(did);
                        arr.Remove(d);
                        AppendLog($"[TARGETS] Удалён дубликат типа {questType} (id={did}) перед записью новой цели.");
                    }
                }

                // Если цель с таким id уже есть — обновляем её (защита от повторной записи).
                JObject entry = null;
                if (!string.IsNullOrEmpty(id))
                {
                    entry = arr.OfType<JObject>().FirstOrDefault(i => (i["id"]?.Value<string>() ?? "") == id);
                }
                if (entry == null)
                {
                    entry = new JObject();
                    arr.Add(entry);
                }
                entry["id"] = id;
                entry["gameName"] = target["gameName"]?.Value<string>() ?? "random";
                entry["realName"] = name;
                entry["coords"] = $"{x.ToString("F2", CultureInfo.InvariantCulture)}, 0.00, {z.ToString("F2", CultureInfo.InvariantCulture)}";
                entry["status"] = active ? "active" : "inactive";
                entry["icon"] = target["icon"]?.Value<string>() ?? "default";
                entry["color"] = color;
                entry["questType"] = questType;
                entry["targetMapOverview"] = false;
                entry["isRandom"] = true;
                entry["radius"] = radius;

                // Доп. параметры (кулдаун/скрытие/удаление) — как в системе overrides.
                int cooldown = target["cooldown"]?.Value<int>() ?? 0;
                int hidden = target["hidden"]?.Value<int>() ?? 0;
                int deleteOnComplete = target["delete_on_complete"]?.Value<int>() ?? 0;
                entry["cooldown"] = cooldown;
                entry["hidden"] = hidden;
                entry["delete_on_complete"] = deleteOnComplete;

                // Синхронизируем словарь активных целей (id может прийти только в add_target).
                if (!string.IsNullOrEmpty(id) && !_randomTargets.ContainsKey(id))
                {
                    _randomTargets[id] = new RandomTargetState
                    {
                        Id = id, X = x, Z = z, Name = name, Radius = radius,
                        QuestType = questType, Color = color, Active = active
                    };
                }

                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                AppendLog($"[TARGETS] Цель добавлена в overrides ({Path.GetFileName(filePath)}): {name} ({x:F1}, {z:F1}) [id={id}]");
                LogTargetsFileDump("ПОСЛЕ ЗАПИСИ ЦЕЛИ", filePath);
                EnsureCooldownTimer();
                // Пересобираем и шлём ПОЛНЫЙ пакет overrides (цели + города/POI).
                SendMapOverridesToMap("add_target");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка записи цели в overrides: {ex.Message}");
            }
        }

        // ============================================================
        // КУЛДАУН / СКРЫТИЕ / УДАЛЕНИЕ ЦЕЛЕЙ (параметры custom_targets)
        // ============================================================
        // cooldown (мин) — после выполнения цель неактивна N минут, затем
        //   снова активируется (current_cooldown уменьшается на 1 каждую минуту
        //   и пишется в файл; рассылка targets_data — только при сбросе в 0).
        // hidden (0/1) — невидима на карте, но триггер зоны активен (в JS).
        // delete_on_complete (0 — оставить, 1 — удалить навсегда, 2 — пересоздать
        //   в >=3 км от старой точки той же логикой).
        private System.Timers.Timer _cooldownTimer = null;
        private void EnsureCooldownTimer()
        {
            if (_cooldownTimer != null) return;
            try
            {
                _cooldownTimer = new System.Timers.Timer(60000) { AutoReset = true };
                _cooldownTimer.Elapsed += (s, e) => DecrementCooldowns();
                _cooldownTimer.Start();
                AppendLog("[TARGETS] Таймер кулдауна целей запущен (1/мин).");
            }
            catch (Exception ex) { AppendLog($"[TARGETS] Ошибка запуска таймера кулдауна: {ex.Message}"); }
        }

        // КУЛДАУН на РЕАЛЬНОМ СИСТЕМНОМ ВРЕМЕНИ (UTC). Таймер раз в минуту проверяет
        // cooldown_until; когда время пришло — цель снова active, кулдаун сбрасывается.
        // Это переживает перезапуск приложения/игры: метка времени хранится в файле.
        private void DecrementCooldowns()
        {
            try
            {
                string filePath = AppDataPaths.TestTargetsFile;
                if (!File.Exists(filePath)) return;
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null || arr.Count == 0) return;
                var now = DateTime.UtcNow;
                bool changed = false;
                foreach (var item in arr.OfType<JObject>())
                {
                    var cu = item["cooldown_until"]?.Value<string>();
                    if (string.IsNullOrEmpty(cu)) continue;
                    if (!DateTime.TryParse(cu, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until)) continue;
                    if (until <= now)
                    {
                        item["status"] = "active";
                        item["cooldown_until"] = (string?)null; // снимаем метку
                        string id = item["id"]?.Value<string>() ?? "";
                        if (!string.IsNullOrEmpty(id) && _randomTargets.TryGetValue(id, out var st))
                            st.CooldownUntil = DateTime.MinValue;
                        AppendLog($"[TARGETS] Кулдаун цели '{item["realName"]}' истёк — цель снова активна.");
                        changed = true;
                    }
                }
                if (changed)
                {
                    File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                    SendMapOverridesToMap("cooldown-reset"); // рассылка — только когда что-то изменилось
                }
            }
            catch (Exception ex) { AppendLog($"[TARGETS] Ошибка декремента кулдауна: {ex.Message}"); }
        }

        // Применяет параметры завершения цели: кулдаун / скрытие / удаление.
        private void CompleteTargetById(string id)
        {
            try
            {
                string filePath = AppDataPaths.TestTargetsFile;
                if (!File.Exists(filePath)) { RemoveTargetById(id); return; }
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null) { RemoveTargetById(id); return; }
                var entry = arr.OfType<JObject>().FirstOrDefault(i => (i["id"]?.Value<string>() ?? "") == id);
                if (entry == null) { RemoveTargetById(id); return; }

                int doc = entry["delete_on_complete"]?.Value<int>() ?? 0;
                int cooldown = entry["cooldown"]?.Value<int>() ?? 0;
                string qType = entry["questType"]?.Value<string>() ?? "";

                if (doc == 1)
                {
                    // Удалить навсегда (например, Тайник — разовая цель).
                    RemoveTargetById(id);
                    AppendLog($"[TARGETS] Цель '{entry["realName"]}' удалена навсегда (delete_on_complete=1).");
                    return;
                }
                if (doc == 2)
                {
                    // Пересоздать: удаляем старую и просим миникарту сгенерировать новую
                    // (та применит свой радиус/POI — будет в >=радиусе от фуры, иначе в новом месте).
                    RemoveTargetById(id);
                    if (!string.IsNullOrEmpty(qType))
                    {
                        string cmd = qType == "snack" ? "quest_snack"
                                   : qType == "stash" ? "quest_stash"
                                   : qType == "courier_dropoff" ? "quest_courier_dropoff"
                                   : qType == "courier_pickup" ? "quest_courier"
                                   : "";
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            SendCommandToMap(cmd);
                            AppendLog($"[TARGETS] Цель '{entry["realName"]}' пересоздана (delete_on_complete=2).");
                        }
                    }
                    return;
                }

                // delete_on_complete=0 (по умолчанию): оставляем цель, применяем кулдаун.
                // Кулдаун — РЕАЛЬНОЕ СИСТЕМНОЕ ВРЕМЯ (UTC), метка cooldown_until пишется в файл
                // и переживает перезапуск приложения/игры. Точка скрыта и триггер отключён, пока
                // now < cooldown_until (см. target_zone_enter + DecrementCooldowns).
                int cd = cooldown > 0 ? cooldown : 5; // запасной 5 мин, если кулдаун не задан
                entry["status"] = "inactive";
                entry["cooldown_until"] = DateTime.UtcNow.AddMinutes(cd).ToString("o");
                if (_randomTargets.TryGetValue(id, out var st))
                    st.CooldownUntil = DateTime.UtcNow.AddMinutes(cd);
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                EnsureCooldownTimer();
                SendMapOverridesToMap("cooldown-set");
                AppendLog($"[TARGETS] Цель '{entry["realName"]}' скрыта на кулдаун {cd} мин (до {DateTime.UtcNow.AddMinutes(cd).ToLocalTime():HH:mm:ss}).");
            }
            catch (Exception ex) { AppendLog($"[TARGETS] Ошибка завершения цели: {ex.Message}"); }
        }

        private void RemoveTargetFromFile(double x, double z)
        {
            try
            {
                string filePath = AppDataPaths.TestTargetsFile;
                if (!File.Exists(filePath)) return;
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null) return;
                var toRemove = new List<JToken>();
                foreach (var item in arr)
                {
                    double ix = 0, iz = 0;
                    var coords = item["coords"]?.Value<string>() ?? "";
                    var parts = coords.Split(',');
                    if (parts.Length >= 3)
                    {
                        double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out ix);
                        double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out iz);
                    }
                    else
                    {
                        ix = item["x"]?.Value<double>() ?? 0;
                        iz = item["z"]?.Value<double>() ?? 0;
                    }
                    if (Math.Abs(ix - x) < 0.5 && Math.Abs(iz - z) < 0.5) toRemove.Add(item);
                }
                foreach (var r in toRemove) arr.Remove(r);
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                AppendLog($"[TARGETS] Цель удалена из overrides: ({x:F1}, {z:F1}), осталось {arr.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка удаления цели из overrides: {ex.Message}");
            }
        }

        // Удаление цели из файла + словаря по id, с уведомлением миникарты.
        private void RemoveTargetById(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                _randomTargets.Remove(id);
                string filePath = AppDataPaths.TestTargetsFile;
                if (!File.Exists(filePath)) { SendCommandToMap("remove_target", new JObject { ["id"] = id }); return; }
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null) { SendCommandToMap("remove_target", new JObject { ["id"] = id }); return; }
                var toRemove = arr.OfType<JObject>().Where(i => (i["id"]?.Value<string>() ?? "") == id).ToList();
                foreach (var r in toRemove) arr.Remove(r);
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                AppendLog($"[TARGETS] Цель [id={id}] удалена из overrides, осталось {arr.Count}");
                SendCommandToMap("remove_target", new JObject { ["id"] = id });
                // Полный пакет: миникарта получает новые списки и перерисовывается сама.
                SendMapOverridesToMap("remove_target");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка удаления цели по id: {ex.Message}");
            }
        }

        // Завершение цели: игрок вошёл в зону (armed) и вышел из неё.
        // Убираем цель, начисляем награду, уведомляем миникарту.
        private void CompleteRandomTarget(string id)
        {
            if (string.IsNullOrEmpty(id) || !_randomTargets.TryGetValue(id, out var st)) return;
            AppendLog($"[QUEST] Цель завершена: id={id} ({st.Name}). Начисляем награду.");
            RemoveTargetById(id);
            try
            {
                string ets2cPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "ets2c.exe");
                if (File.Exists(ets2cPath))
                {
                    var si = new ProcessStartInfo
                    {
                        FileName = ets2cPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    si.Arguments = "-moneygive 3000"; Process.Start(si);
                    si.Arguments = "-xpgive 150"; Process.Start(si);
                    AppendLog("Начислено 3000 денег и 150 опыта.");
                }
                else AppendLog("ets2c.exe не найден — награда не начислена.");
            }
            catch (Exception ex) { AppendLog($"Ошибка ets2c: {ex.Message}"); }
            trayIcon.ShowBalloonTip(2000, "ETS2 Assist", $"Цель выполнена: {st.Name}", ToolTipIcon.Info);
        }

        // Начисление награды через ets2c.exe. money может быть отрицательным (Перекус: -450р).
        private void GiveReward(int money, int xp)
        {
            try
            {
                string ets2cPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "ets2c.exe");
                if (File.Exists(ets2cPath))
                {
                    var si = new ProcessStartInfo
                    {
                        FileName = ets2cPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    if (money > 0) { si.Arguments = $"-moneygive {money}"; Process.Start(si); }
                    else if (money < 0) { si.Arguments = $"-moneytake {Math.Abs(money)}"; Process.Start(si); }
                    if (xp != 0) { si.Arguments = $"-xpgive {xp}"; Process.Start(si); }
                    AppendLog($"Начислено: денег {money}, опыта {xp}.");
                }
                else AppendLog("ets2c.exe не найден — награда не начислена.");
            }
            catch (Exception ex) { AppendLog($"Ошибка ets2c: {ex.Message}"); }
        }

        // Обработка входа в зону в зависимости от типа квеста. Вызывается из UI-потока (BeginInvoke).
        private void HandleQuestEnter(RandomTargetState st)
        {
            switch (st.QuestType)
            {
                    case "courier_pickup":
                        {
                            int dist = new Random().Next(400, 2001); // 400..2000 м
                            int reward = 1000 + (int)Math.Round(30.0 * dist / 150.0); // 1000р база + 30р/150м
                            using var dlg = new QuestDialogForm("Курьер",
                                "Доставить документы.",
                                isSuccess: false, primaryText: "Начать выполнение", secondaryText: "Отказаться",
                                rewards: new System.Collections.Generic.List<string>
                                {
                                    $"Награда: {reward}р",
                                    $"Расстояние доставки: {dist} м"
                                });
                            dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                            var res = dlg.ShowDialog(this);
                            st.Armed = false; // после диалога сбрасываем арм (повторный вход -> снова диалог)
                            RemoveTargetById(st.Id); // любой выбор удаляет точку забора
                            if (res == DialogResult.Yes)
                            {
                                _courierReward = reward;
                                SendCommandToMap("quest_courier_dropoff", new JObject { ["distanceM"] = dist });
                                AppendLog($"[QUEST] Курьер принят. Награда {reward}р, точка выдачи создана на {dist} м.");
                            }
                            else
                            {
                                AppendLog("[QUEST] Курьер отклонён — точка забора удалена.");
                            }
                            break;
                        }
                case "courier_dropoff":
                    {
                        int rewardNow = _courierReward;
                        using var dlg = new QuestDialogForm("Курьер", "Выручить документы?",
                            isSuccess: false, primaryText: "ДА", secondaryText: "НЕТ",
                            rewards: new System.Collections.Generic.List<string> { $"Награда: {rewardNow}р, +250 опыта" });
                        dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                        var res = dlg.ShowDialog(this);
                        st.Armed = false;
                            if (res == DialogResult.Yes)
                            {
                                int reward = _courierReward;
                                _courierReward = 0;
                                GiveReward(reward, 250);
                                RemoveTargetById(st.Id);
                                AppendLog($"[QUEST] Курьер выполнен: +{reward}р, +250xp.");
                            }
                        else
                        {
                            AppendLog("[QUEST] Курьер: выдача отложена, ждём повторного входа.");
                        }
                        break;
                    }
                case "stash":
                    {
                        using var dlg = new QuestDialogForm("Тайник", "Вы нашли тайник.",
                            isSuccess: true, primaryText: "ОК",
                            rewards: new System.Collections.Generic.List<string> { "+3000р" });
                        dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                        var res = dlg.ShowDialog(this);
                        st.Armed = false;
                        if (res == DialogResult.OK)
                        {
                            GiveReward(3000, 0);
                            RemoveTargetById(st.Id);
                            AppendLog("[QUEST] Тайник: +3000р.");
                        }
                        break;
                    }
                case "snack":
                    {
                        using var dlg = new QuestDialogForm("Перекус", "Вы перекусили чем-то вкусным.",
                            isSuccess: true, primaryText: "ОК",
                            rewards: new System.Collections.Generic.List<string> { "-450р", "+1000 опыта" });
                        dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                        var res = dlg.ShowDialog(this);
                        st.Armed = false;
                        if (res == DialogResult.OK)
                        {
                            // -450р, +1000xp; цель уходит на кулдаун (current_cooldown), затем
                            // снова появляется (логика в DecrementCooldowns).
                            GiveReward(-450, 1000);
                            CompleteTargetById(st.Id);
                            AppendLog("[QUEST] Перекус: -450р, +1000xp. Скрыто на время кулдауна.");
                        }
                        break;
                    }
                default:
                    AppendLog($"[TRIGGER] Неизвестный тип цели {st.QuestType} — игнорируем.");
                    st.Armed = false;
                    break;
            }
        }

        private void LogCustomTargetsFromFile()
        {
            try
            {
                string filePath = AppDataPaths.TestTargetsFile;
                if (!File.Exists(filePath)) { AppendLog($"[TARGETS] Файл целей отсутствует: {filePath}"); return; }
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray ?? new JArray();
                AppendLog($"=== Проверка точек (test_targets.json): {arr.Count} ===");
                for (int i = 0; i < arr.Count; i++)
                {
                    var t = arr[i];
                    AppendLog($"  [{i + 1}] name={t["realName"] ?? t["gameName"]} coords={t["coords"]} isRandom={t["isRandom"]} status={t["status"]}");
                }
                AppendLog("=== конец проверки точек ===");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка чтения файла целей: {ex.Message}");
            }
        }

        // ============================================================
        // ПАУЗА ETS2 ЧЕРЕЗ ets2_assist_input.dll (Named Pipe)
        // ============================================================
        private int _courierReward = 0;
        private bool _questHandling = false;

        // Показ диалога квеста СОБЛЮДАЯ СТРОГУЮ ЛОГИКУ ПАУЗЫ (уточнение пользователя
        // 30.08.2026: «перед появлением диалога пауза, НО только если НЕ на паузе;
        // после паузы диалог c задержкой 2с»):
        // 1) пауза отправляется ТОЛЬКО если игра НЕ на паузе;
        // 2) ждём подтверждение паузы по телеметрии (до 5с); если НЕ подтвердилось —
        //    ретраим команду PAUSE ещё раз (1с) и ждём ещё 3с (двойная попытка —
        //    была жалоба «не всегда ставится на паузу»);
        // 3) после паузы (или если УЖЕ была на паузе) задержка 2с, затем диалог;
        // 4) после закрытия диалога фокус возвращаем в окно игры.
        private async void TriggerQuestDialog(RandomTargetState st)
        {
            if (_questHandling) { AppendLog("[QUEST] диалог уже активен — повторный вход проигнорирован."); return; }
            _questHandling = true;
            try
            {
                bool alreadyPaused = await IsGamePausedAsync();
                if (alreadyPaused)
                {
                    AppendLog("[QUEST] Игра УЖЕ на паузе — паузу не отправляем.");
                    // Уточнение пользователя: задержка 2с обязательна ПЕРЕД диалогом —
                    // и в ветке уже-на-паузе тоже.
                    await Task.Delay(2000);
                }
                else
                {
                    bool paused = false;
                    for (int attempt = 0; attempt < 2 && !paused; attempt++)
                    {
                        if (attempt > 0)
                        {
                            AppendLog("[QUEST] Пауза не подтвердилась — повторная отправка PAUSE.");
                            await Task.Delay(1000);
                            SetGamePause(true);
                        }
                        else
                        {
                            SetGamePause(true);
                        }
                        for (int i = 0; i < 20 && !paused; i++)
                        {
                            await Task.Delay(250);
                            if (await IsGamePausedAsync()) paused = true;
                        }
                    }
                    if (!paused)
                        AppendLog("[QUEST] ВНИМАНИЕ: пауза не подтвердилась за 2 попытки — показываем диалог без гарантии паузы.");
                    else
                        AppendLog("[QUEST] Игра встала на паузу — ждём 2с перед диалогом.");
                    // задержка 2с после паузы во всех ветках, перед диалогом.
                    await Task.Delay(2000);
                }
                this.TopMost = true;
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
                HandleQuestEnter(st);
            }
            catch (Exception ex)
            {
                AppendLog($"[QUEST] ошибка показа диалога: {ex.Message}");
            }
            finally
            {
                this.TopMost = false;
                _questHandling = false;
                // Фокус ВСЕГДА возвращаем в окно игры (просьба пользователя) —
                // паузу не снимаем: игрок сам выходит из паузы.
                ReturnFocusToGame();
            }
        }

        private bool SetGamePause(bool enabled)
        {
            AppendLog($"[SCS] SetGamePause({enabled})");
            // Фиксируем намерение приложения — используется как запасной детектор паузы,
            // если телеметрия не отдаёт корректный статус paused.
            this._pausedIntent = enabled;
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
                SetForegroundWindow(hWnd);
                SetActiveWindow(hWnd);
                SetFocus(hWnd);

                return GetForegroundWindow() == hWnd;
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

        // NOTE: HandleTargetReached удалён. Завершение цели теперь обрабатывается
        // через события target_zone_enter / target_zone_leave (см. CompleteRandomTarget):
        // вход в зону взводит триггер, выход из зоны после входа завершает цель.
    }
}