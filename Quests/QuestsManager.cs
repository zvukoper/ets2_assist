using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

            // Отладка: принудительно перечитываем custom_targets.json и шлём на миникарту.
            SendTargetsToMap();
            LogCustomTargetsFromFile();
            AppendLog("Проверка точек: файл принудительно перечитан и отправлен на карту.");
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
                case "target_zone_enter":
                    {
                        var zt = data["id"]?.Value<string>();
                        if (zt != null && _randomTargets.TryGetValue(zt, out var st))
                        {
                            st.InZone = true;
                            st.Armed = true;
                            AppendLog($"[TRIGGER] Вход в зону цели {zt} ({st.Name}, тип={st.QuestType}) — диалог задания.");
                            this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
                            {
                                bool wasTop = this.TopMost;
                                bool pausedByUs = false;
                                try
                                {
                                    this.TopMost = true;
                                    this.Show();
                                    this.WindowState = FormWindowState.Normal;
                                    this.Activate();
                                    try { if (!IsGamePaused()) { SetGamePause(true); pausedByUs = true; } }
                                    catch (Exception exP) { AppendLog($"[SCS] пауза не удалась: {exP.Message}"); }
                                    ForceForegroundWindow(this.Handle, "before-quest-dialog");
                                    HandleQuestEnter(st);
                                }
                                finally
                                {
                                    this.TopMost = wasTop;
                                    try { if (pausedByUs && IsGamePaused()) SetGamePause(false); }
                                    catch { }
                                    ReturnFocusToGame();
                                }
                            });
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
                case "map_ready":
                    // Миникарта готова — приложение читает custom_targets.json (один раз
                    // при старте) и шлёт актуальные цели командой targets_data.
                    AppendLog("[WS] Миникарта готова -> отправляем цели из файла");
                    SendTargetsToMap();
                    break;
                case "request_reload_custom_targets":
                    // На всякий случай (если миникарта ещё шлёт эту команду).
                    SendTargetsToMap();
                    break;
                case "add_target":
                    // Миникарта сгенерировала случайную цель и просит приложение
                    // записать её в custom_targets.json, затем разослать targets_data.
                    AppendLog($"[WS] add_target x={data["target"]?["x"]} z={data["target"]?["z"]}");
                    AddTargetToFile(data["target"]);
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
        // РАБОТА С ФАЙЛОМ ЦЕЛЕЙ (custom_targets.json) — ЕДИНСТВЕННЫЙ ВЛАДЕЛЕЦ
        // ============================================================
        // Приложение — единственный, кто читает и пишет custom_targets.json.
        // Миникарта файл не трогает: получает targets_data и только рисует.

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
            try
            {
                string filePath = AppDataPaths.CustomTargetsFile;
                JArray targets;
                if (File.Exists(filePath))
                {
                    var root = JObject.Parse(File.ReadAllText(filePath));
                    targets = root["customTargets"] as JArray ?? new JArray();
                }
                else
                {
                    targets = new JArray();
                }
                var payload = new JObject { ["targets"] = targets };
                SendCommandToMap("targets_data", payload);
                AppendLog($"[TARGETS] Отправлено точек на миникарту: {targets.Count} (путь файла={filePath})");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка отправки целей на миникарту: {ex.Message}");
            }
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

                string filePath = AppDataPaths.CustomTargetsFile;
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

                // Доп. параметры custom_targets (для кулдауна/скрытия/удаления).
                int cooldown = target["cooldown"]?.Value<int>() ?? 0;
                int currentCooldown = target["current_cooldown"]?.Value<int>() ?? 0;
                int hidden = target["hidden"]?.Value<int>() ?? 0;
                int deleteOnComplete = target["delete_on_complete"]?.Value<int>() ?? 0;
                // Для только что сгенерированных целей (без явного кулдауна) ставим 0.
                if (target["cooldown"] == null) cooldown = 0;
                if (target["current_cooldown"] == null) currentCooldown = 0;
                if (target["hidden"] == null) hidden = 0;
                if (target["delete_on_complete"] == null) deleteOnComplete = 0;
                entry["cooldown"] = cooldown;
                entry["current_cooldown"] = currentCooldown;
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
                AppendLog($"[TARGETS] Цель добавлена в файл: {name} ({x:F1}, {z:F1}) [id={id}]");
                LogTargetsFileDump("ПОСЛЕ ЗАПИСИ ЦЕЛИ", filePath);
                EnsureCooldownTimer();
                SendTargetsToMap();
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка записи цели в файл: {ex.Message}");
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

        private void DecrementCooldowns()
        {
            try
            {
                string filePath = AppDataPaths.CustomTargetsFile;
                if (!File.Exists(filePath)) return;
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null || arr.Count == 0) return;
                bool changed = false;
                foreach (var item in arr.OfType<JObject>())
                {
                    int cur = item["current_cooldown"]?.Value<int>() ?? 0;
                    if (cur > 0)
                    {
                        cur -= 1;
                        item["current_cooldown"] = cur;
                        if (cur <= 0)
                        {
                            item["current_cooldown"] = 0;
                            item["status"] = "active"; // кулдаун истёк — цель снова активна
                            AppendLog($"[TARGETS] Кулдаун цели '{item["realName"]}' истёк — цель снова активна.");
                        }
                        changed = true;
                    }
                }
                if (changed)
                {
                    File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                    SendTargetsToMap(); // рассылка — только когда что-то изменилось (кулдаун сброшен)
                }
            }
            catch (Exception ex) { AppendLog($"[TARGETS] Ошибка декремента кулдауна: {ex.Message}"); }
        }

        // Применяет параметры завершения цели: кулдаун / скрытие / удаление.
        private void CompleteTargetById(string id)
        {
            try
            {
                string filePath = AppDataPaths.CustomTargetsFile;
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
                int cd = cooldown > 0 ? cooldown : 5; // запасной 5 мин, если кулдаун не задан
                entry["current_cooldown"] = cd;
                entry["status"] = "inactive";
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                EnsureCooldownTimer();
                AppendLog($"[TARGETS] Цель '{entry["realName"]}' скрыта на кулдаун {cd} мин.");
            }
            catch (Exception ex) { AppendLog($"[TARGETS] Ошибка завершения цели: {ex.Message}"); }
        }

        private void RemoveTargetFromFile(double x, double z)
        {
            try
            {
                string filePath = AppDataPaths.CustomTargetsFile;
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
                AppendLog($"[TARGETS][DEBUG] УДАЛЕНИЕ цели: путь={filePath}, убрано={toRemove.Count}, осталось={arr.Count}");
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                LogTargetsFileDump("ПОСЛЕ УДАЛЕНИЯ ЦЕЛИ", filePath);
                AppendLog($"[TARGETS] Цель удалена из файла: ({x:F1}, {z:F1}), осталось {arr.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[TARGETS] Ошибка удаления цели из файла: {ex.Message}");
            }
        }

        // Удаление цели из файла + словаря по id, с уведомлением миникарты.
        private void RemoveTargetById(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                _randomTargets.Remove(id);
                string filePath = AppDataPaths.CustomTargetsFile;
                if (!File.Exists(filePath)) { SendCommandToMap("remove_target", new JObject { ["id"] = id }); return; }
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray;
                if (arr == null) { SendCommandToMap("remove_target", new JObject { ["id"] = id }); return; }
                var toRemove = arr.OfType<JObject>().Where(i => (i["id"]?.Value<string>() ?? "") == id).ToList();
                foreach (var r in toRemove) arr.Remove(r);
                File.WriteAllText(filePath, root.ToString(Formatting.Indented));
                LogTargetsFileDump("ПОСЛЕ УДАЛЕНИЯ ЦЕЛИ (по id)", filePath);
                AppendLog($"[TARGETS] Цель [id={id}] удалена из файла, осталось {arr.Count}");
                SendCommandToMap("remove_target", new JObject { ["id"] = id });
                SendTargetsToMap();
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
                        using var dlg = new QuestDialogForm("Курьер",
                            $"Доставить документы.\nНаграда: 1200р.\nРасстояние доставки: {dist} м",
                            isSuccess: false, primaryText: "Начать выполнение", secondaryText: "Отказаться");
                        dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                        var res = dlg.ShowDialog(this);
                        st.Armed = false; // после диалога сбрасываем арм (повторный вход -> снова диалог)
                        RemoveTargetById(st.Id); // любой выбор удаляет точку забора
                        if (res == DialogResult.Yes)
                        {
                            SendCommandToMap("quest_courier_dropoff", new JObject { ["distanceM"] = dist });
                            AppendLog($"[QUEST] Курьер принят. Точка выдачи создана на {dist} м.");
                        }
                        else
                        {
                            AppendLog("[QUEST] Курьер отклонён — точка забора удалена.");
                        }
                        break;
                    }
                case "courier_dropoff":
                    {
                        using var dlg = new QuestDialogForm("Курьер", "Выручить документы?", isSuccess: false, primaryText: "ДА", secondaryText: "НЕТ");
                        dlg.Shown += (_, _) => ForceForegroundWindow(dlg.Handle, "quest-dialog");
                        var res = dlg.ShowDialog(this);
                        st.Armed = false;
                        if (res == DialogResult.Yes)
                        {
                            GiveReward(1200, 250);
                            RemoveTargetById(st.Id);
                            AppendLog("[QUEST] Курьер выполнен: +1200р, +250xp.");
                        }
                        else
                        {
                            AppendLog("[QUEST] Курьер: выдача отложена, ждём повторного входа.");
                        }
                        break;
                    }
                case "stash":
                    {
                        using var dlg = new QuestDialogForm("Тайник", "Вы нашли тайник.\n+3000р", isSuccess: true, primaryText: "ОК");
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
                        using var dlg = new QuestDialogForm("Перекус", "Вы перекусили чем-то вкусным.", isSuccess: true, primaryText: "ОК");
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
                string filePath = AppDataPaths.CustomTargetsFile;
                if (!File.Exists(filePath)) { AppendLog("[TARGETS] Файл целей отсутствует."); return; }
                var root = JObject.Parse(File.ReadAllText(filePath));
                var arr = root["customTargets"] as JArray ?? new JArray();
                AppendLog($"=== Проверка точек (из файла): {arr.Count} ===");
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

        // NOTE: HandleTargetReached удалён. Завершение цели теперь обрабатывается
        // через события target_zone_enter / target_zone_leave (см. CompleteRandomTarget):
        // вход в зону взводит триггер, выход из зоны после входа завершает цель.
    }
}