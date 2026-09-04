using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    // ЕДИНЫЙ КОНВЕЙЕР OVERRIDES ДЛЯ МИНИКАРТЫ (принцип редактора карты):
    // приложение САМО собирает эффективное состояние точек (статика городов/POI +
    // delta-merge overrides из map_overrides\*.json по load_order.txt + цели из
    // test_targets.json) и рассылает ГОТОВЫЙ пакет командой map_overrides_data.
    // Миникарта НЕ считает merged сама — только принимает пакет, заменяет свои
    // списки точек и перерисовывает (dumb-receiver).
    //
    // Формат payload (map_overrides_data):
    // {
    //   seq: N,                       // монотонный номер пакета (для отладки)
    //   cities: [{gameName,x,y,z,realName,hidden,color}],
    //   pois:   [{uid,category,name,x,z,hidden,color}],
    //   targets:[{id,gameName,realName,x,y,z,color,radius,active,questType,isRandom,
    //             hidden,cooldown,cooldownUntil,deleteOnComplete,delete_on_complete}]
    // }
    public partial class MainForm
    {
        private long _overridesSeq = 0;

        // ==== ПУБЛИЧНЫЕ ТОЧКИ ОТПРАВКИ ====

        // Полная пересборка и отправка (старт, map_ready, «Проверка точек», ручное обновление).
        // НИКАКИХ таймеров/циклов: вызывается ТОЛЬКО по событиям (map_ready, сохранение/
        // удаление/добавление точки, кулдаун-таймер точек и т.п.).
        internal void SendMapOverridesToMap(string reason = "manual")
        {
            try
            {
                var payload = BuildMapOverridesPayload();
                payload["seq"] = ++_overridesSeq;
                payload["reason"] = reason;
                SendCommandToMap("map_overrides_data", payload);

                // Данные карты изменились → AR-цель переотправляется разово (следующий
                // тик AR): модель точек/координаты могли смениться, страница должна
                // получить новую точку в той же рассылке, что и миникарта.
                _arLastSentGameName = null;

                // ДЕТАЛЬНЫЙ ЛОГ payload (для отладки overrides на миникарте): считаем
                // переопределённые города/POI (те, что есть в overrides) отдельно.
                int ovrCities = 0, ovrPois = 0, customPois = 0;
                foreach (var c in payload["cities"] as JArray ?? new JArray())
                    if (c["overridden"]?.Value<bool>() == true) ovrCities++;
                foreach (var p in payload["pois"] as JArray ?? new JArray())
                {
                    if (p["overridden"]?.Value<bool>() == true) ovrPois++;
                    if ((p["category"]?.Value<string>() ?? "") == "custom") customPois++;
                }
                AppendLog($"[OVR] map_overrides_data #{_overridesSeq} ({reason}): cities={payload["cities"]!.Count()} (переопр={ovrCities}), pois={payload["pois"]!.Count()} (переопр={ovrPois}, custom={customPois}), targets={payload["targets"]!.Count()}");
            }
            catch (Exception ex)
            {
                AppendLog($"[OVR] Ошибка сборки/отправки map_overrides_data ({reason}): {ex.Message}");
            }
        }

        // ==== СБОРКА ПАКЕТА ====

        internal JObject BuildMapOverridesPayload()
        {
            // 1) Статика городов (cities_sibirmap.json), ключ = gameName.
            var cities = LoadStaticCities();

            // 2) Статика POI (Overlays.json), ключ = uid.
            var pois = LoadStaticPois();

            // 2.5) SDO (Static Data Objects, выгрузка редактора игры) — тоже в pois:
            // единый dumb-receiver миникарты, category = читабельное имя из meta.json,
            // name = meta.name + имя объекта (uid / easter-имя), color из meta.color.
            // uid уникальны (0x…), коллизии с Overlays.json практически исключены —
            // SDO поверх POI (в словаре это уже так: «последний побеждает»).
            LoadSdoPointsInto(pois);

            // 3) Цели из test_targets.json (заранее очищаем от устаревших кулдаунов).
            var targets = LoadTestTargets();

            // 4) Delta-merge ВСЕХ overrides по load_order (снизу вверх, последний побеждает).
            ApplyOverrideFiles(cities, pois, targets);

            return new JObject
            {
                ["cities"] = CitiesToJArray(cities.Values),
                ["pois"] = PoisToJArray(pois.Values),
                ["targets"] = TargetsToJArray(targets)
            };
        }

        // ==== СТАТИКА ====

        private Dictionary<string, PointData> LoadStaticCities()
        {
            var cities = new Dictionary<string, PointData>();
            try
            {
                var path = Path.Combine(AppDataPaths.StaticDataDirectory, "localized_cities", "cities_sibirmap.json");
                if (File.Exists(path))
                {
                    var list = (JObject.Parse(File.ReadAllText(path))["citiesList"] as JArray) ?? new JArray();
                    foreach (var c in list.OfType<JObject>())
                    {
                        var id = (string?)c["gameName"];
                        if (string.IsNullOrEmpty(id) || cities.ContainsKey(id)) continue;
                        if (!double.TryParse((string?)c["x"], NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) continue;
                        if (!double.TryParse((string?)c["z"], NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) continue;
                        double.TryParse((string?)c["y"], NumberStyles.Any, CultureInfo.InvariantCulture, out var y);
                        var nm = (string?)c["realName"];
                        cities[id] = new PointData
                        {
                            GameName = id,
                            RealName = string.IsNullOrEmpty(nm) ? id : nm,
                            X = x, Y = y, Z = z,
                            IsCity = true
                        };
                    }
                }
            }
            catch (Exception ex) { AppendLog($"[OVR] Города (статика): {ex.Message}"); }
            return cities;
        }

        // ==== SDO (Static Data Objects, выгрузка редактора игры) ====

        // Добавляет SDO в словарь POI (ключ = uid). Категория = читабельное имя из
        // meta.json; color — из meta.color; координаты уже в игровой СК.
        private static void LoadSdoPointsInto(Dictionary<string, PointData> pois)
        {
            try
            {
                foreach (var p in SdoLoader.LoadAll())
                {
                    if (string.IsNullOrEmpty(p.GameName)) continue;
                    pois[p.GameName] = new PointData
                    {
                        GameName = p.GameName,
                        RealName = p.RealName,
                        Category = p.Category,
                        Enabled = true,
                        X = p.X, Y = p.Y, Z = p.Z,
                        Color = SdoMeta.ColorHexOf(p.Category),
                        IsPoi = true,
                        IsSdo = true
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[OVR] SDO (статика): " + ex.Message);
                Logger.Current?.Data("[OVR] SDO (статика): " + ex.Message);
            }
        }

        private Dictionary<string, PointData> LoadStaticPois()
        {
            var pois = new Dictionary<string, PointData>();
            try
            {
                var path = Path.Combine(AppDataPaths.StaticDataDirectory, "Overlays.json");
                if (File.Exists(path))
                {
                    foreach (var prop in JObject.Parse(File.ReadAllText(path)).Properties())
                    {
                        if (prop.Value is not JArray arr) continue;
                        foreach (var item in arr.OfType<JObject>())
                        {
                            var uid = (string?)item["uid"] ?? prop.Name;
                            if (string.IsNullOrEmpty(uid) || pois.ContainsKey(uid)) continue;
                            if (item["x"] == null || item["z"] == null) continue;
                            // ФИКС v64: Value<double>() напрямую (ru-RU ToString даёт «,» —
                            // Invariant TryParse читал её как разделитель тысяч = ×1e8 мусор).
                            double x = item["x"].Value<double>();
                            double z = item["z"].Value<double>();
                            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsInfinity(z)) continue;
                            if (Math.Abs(x) > 1_000_000 && Math.Abs(z) > 1_000_000) { x /= 100; z /= 100; }
                            var nm = (string?)item["realName"] ?? (string?)item["name"] ?? (string?)item["gameName"];
                            pois[uid] = new PointData
                            {
                                GameName = uid,
                                RealName = string.IsNullOrEmpty(nm) ? prop.Name : nm,
                                Category = prop.Name,
                                X = x, Z = z,
                                IsPoi = true
                            };
                        }
                    }
                }
            }
            catch (Exception ex) { AppendLog($"[OVR] POI (статика): {ex.Message}"); }
            return pois;
        }

        // ==== TEST_TARGETS (цели тестовых кнопок) ====

        // Гарантирует наличие test_targets.json в папке overrides + запись в load_order.
        internal void EnsureTestTargetsFile()
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                var order = File.Exists(AppDataPaths.MapOverridesLoadOrderFile)
                    ? File.ReadAllLines(AppDataPaths.MapOverridesLoadOrderFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
                    : new List<string>();

                bool changed = false;
                // ФИКС (30.08.2026): у пользователя был ОБРАТНЫЙ порядок
                // [test_targets.json, custom_map1.json] — custom_map1 (last=высший приоритет)
                // перекрывал статус целей. Нормализуем порядок:
                // custom_map1 ниже, test_targets.json последним (высший приоритет).
                int ttIdx = order.FindIndex(f => f.Equals("test_targets.json", StringComparison.OrdinalIgnoreCase));
                int cmIdx = order.FindIndex(f => f.Equals("custom_map1.json", StringComparison.OrdinalIgnoreCase));
                if (ttIdx >= 0 && cmIdx >= 0 && ttIdx < cmIdx)
                {
                    string tt = order[ttIdx];
                    order.RemoveAt(ttIdx);
                    order.Add(tt);
                    changed = true;
                    AppendLog("[OVR] load_order.txt исправлен: test_targets.json переставлен ПОСЛЕДНИМ (высший приоритет).");
                }
                if (!order.Contains("custom_map1.json", StringComparer.OrdinalIgnoreCase))
                {
                    // ВСТАВЛЯЕМ ПЕРЕД test_targets.json: priority custom_map1 ниже (раньше),
                    // test_targets остаётся последним (высший).
                    int idx = order.FindIndex(f => f.Equals("test_targets.json", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) order.Insert(idx, "custom_map1.json"); else order.Add("custom_map1.json");
                    changed = true;
                    AppendLog("[OVR] custom_map1.json добавлен в load_order.txt (был отсутствовал — его записи не читались!).");
                }
                if (!order.Contains("test_targets.json", StringComparer.OrdinalIgnoreCase))
                {
                    order.Add("test_targets.json"); // высший приоритет — последним
                    changed = true;
                    AppendLog("[OVR] test_targets.json добавлен в load_order.txt.");
                }
                if (changed)
                    File.WriteAllLines(AppDataPaths.MapOverridesLoadOrderFile, order);

                var path = AppDataPaths.TestTargetsFile;
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, new JObject { ["customTargets"] = new JArray() }.ToString(Formatting.Indented));
                    AppendLog($"[OVR] Создан файл тестовых целей: {path}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[OVR] Ошибка инициализации test_targets.json: {ex.Message}");
            }
        }

        // Читает цели из test_targets.json и подготавливает к отправке:
        // сбрасывает status=active, если кулдаун (cooldown_until) уже истёк.
        private List<JObject> LoadTestTargets()
        {
            var result = new List<JObject>();
            try
            {
                var path = AppDataPaths.TestTargetsFile;
                if (!File.Exists(path)) return result;
                var arr = (JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray) ?? new JArray();
                var now = DateTime.UtcNow;
                foreach (var t in arr.OfType<JObject>())
                {
                    // Автопробуждение: кулдаун истёк -> снова active (метка остаётся до перезаписи).
                    var cu = (string?)t["cooldown_until"];
                    if (!string.IsNullOrEmpty(cu) &&
                        DateTime.TryParse(cu, null, DateTimeStyles.RoundtripKind, out var until) &&
                        until <= now)
                    {
                        t["status"] = "active";
                        t["cooldown_until"] = (string?)null;
                    }
                    result.Add(t);
                }
            }
            catch (Exception ex) { AppendLog($"[OVR] Чтение test_targets.json: {ex.Message}"); }
            return result;
        }

        // Записывает список целей в test_targets.json (формат как custom_targets.json).
        internal void SaveTestTargets(JArray targets, string reason)
        {
            try
            {
                EnsureTestTargetsFile();
                var path = AppDataPaths.TestTargetsFile;
                File.WriteAllText(path, new JObject { ["customTargets"] = targets }.ToString(Formatting.Indented));
                AppendLog($"[OVR] test_targets.json записан ({reason}): целей={targets.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[OVR] Ошибка записи test_targets.json: {ex.Message}");
            }
        }

        // ==== НАЛОЖЕНИЕ OVERRIDES (delta-merge, как в редакторе) ====

        private void ApplyOverrideFiles(
            Dictionary<string, PointData> cities,
            Dictionary<string, PointData> pois,
            List<JObject> targets)
        {
            int mergedCities = 0, mergedPois = 0, mergedTargets = 0, addedUser = 0;
            foreach (var (file, entry) in ReadOverridesInLoadOrder())
            {
                var key = (string?)entry["gameName"] ?? (string?)entry["id"];
                if (string.IsNullOrEmpty(key)) continue;

                // 1) Переопределение города.
                if (cities.TryGetValue(key, out var city))
                {
                    MapEditorForm.ApplyJObjectToPoint(city, entry);
                    city.IsOverride = true;
                    city.SourceFile = file;
                    mergedCities++;
                    Logger.Current?.Data($"[OVR] merge CITY '{key}' ({file})");
                    continue;
                }
                // 2) Переопределение POI.
                if (pois.TryGetValue(key, out var poi))
                {
                    MapEditorForm.ApplyJObjectToPoint(poi, entry);
                    poi.IsOverride = true;
                    poi.SourceFile = file;
                    mergedPois++;
                    Logger.Current?.Data($"[OVR] merge POI '{key}' ({file})");
                    continue;
                }
                // 3) Цель из test_targets по id.
                if (targets.FirstOrDefault(it => ((string?)it["id"] ?? (string?)it["gameName"]) == key) is { } tgt)
                {
                    JObjectTargetMergeExtensions.ApplyToTargetEntry(tgt, entry);
                    mergedTargets++;
                    Logger.Current?.Data($"[OVR] merge TARGET '{key}' ({file})");
                    continue;
                }
                // 3b) ФИКС (30.08.2026): случайные/квестовые цели (isRandom) из
                // test_targets.json НЕ должны доходить до ветки user-точек — иначе
                // каждая цель ДУБЛИРУЕТСЯ на карте как POI 'custom' (сейчас в payload
                // custom=5 при 2 пользовательских точках: +courier_pickup,
                // +courier_dropoff, +random). Цели отрисовываются как targets.
                if ((entry["isRandom"]?.Value<bool>() ?? false) ||
                    !string.IsNullOrEmpty(entry["questType"]?.Value<string>()))
                {
                    continue;
                }
                // 4) Пользовательская точка (только в overrides).
                var up = new PointData { GameName = key };
                MapEditorForm.ApplyJObjectToPoint(up, entry);
                up.SourceFile = file;
                up.IsOverride = true;
                up.Category = string.IsNullOrEmpty(up.Category) ? "Пользовательское" : up.Category;

                // Пользовательская точка может «сидеть» на статике (IsCity/IsPoi уже false) —
                // уходит в общий список POI с категорией (миникарта рисует её особым цветом 'custom').
                up.IsPoi = true;
                up.Category = "custom";
                pois[key] = up;
                addedUser++;
                Logger.Current?.Data($"[OVR] новая USER-точка '{key}' ({file}) -> pois[custom], coords=({up.X:F1}, {up.Z:F1})");
            }
            Logger.Current?.Data($"[OVR] merge итог: cities+{mergedCities}, pois+{mergedPois}, targets+{mergedTargets}, user+{addedUser}");
        }

        // ==== ЧТЕНИЕ ФАЙЛОВ OVERRIDES ====

        // load_order.txt: индекс 0 = НИЗШИЙ приоритет, последний = ВЫСШИЙ.
        private IEnumerable<(string file, JObject entry)> ReadOverridesInLoadOrder()
        {
            var dir = AppDataPaths.MapOverridesDirectory;
            var orderFile = AppDataPaths.MapOverridesLoadOrderFile;
            if (!File.Exists(orderFile))
            {
                AppendLog("[OVR] load_order.txt ОТСУТСТВУЕТ — overrides не читаются вообще!");
                yield break;
            }
            var orderLines = File.ReadAllLines(orderFile)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Logger.Current?.Data($"[OVR] load_order.txt: [{string.Join(", ", orderLines)}] (снизу=низший приоритет)");
            foreach (var f in orderLines)
            {
                var path = Path.Combine(dir, f);
                if (!File.Exists(path))
                {
                    Logger.Current?.Data($"[OVR] файл из load_order не найден: {f} (пропущен)");
                    continue;
                }
                JArray? list = null;
                try
                {
                    list = JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray;
                    Logger.Current?.Data($"[OVR] прочитан {f}: записей={list?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Logger.Current?.Data($"[OVR] ОШИБКА чтения {f}: {ex.Message}");
                }
                if (list == null) continue;
                foreach (var t in list.OfType<JObject>())
                {
                    var k = (string?)t["gameName"] ?? (string?)t["id"] ?? "?";
                    var crd = (string?)t["coords"] ?? $"{t["x"]},{t["z"]}";
                    Logger.Current?.Data($"[OVR]   {f} -> {k}: {crd}");
                    yield return (f, t);
                }
            }
        }

        // ==== СЕРИАЛИЗАЦИЯ ПАКЕТА ====

        private static JArray CitiesToJArray(IEnumerable<PointData> cities)
        {
            var arr = new JArray();
            foreach (var c in cities)
            {
                arr.Add(new JObject
                {
                    ["gameName"] = c.GameName,
                    ["realName"] = c.RealName,
                    ["category"] = c.Category,
                    ["x"] = c.X, ["y"] = c.Y, ["z"] = c.Z,
                    ["color"] = c.Color,
                    ["hidden"] = c.Hidden == 1 || !c.Enabled,
                    ["overridden"] = c.IsOverride
                });
            }
            return arr;
        }

        private static JArray PoisToJArray(IEnumerable<PointData> pois)
        {
            var arr = new JArray();
            foreach (var p in pois)
            {
                // SDO: иконка категории (meta.json, png 50x50 в editor_static_data\icons).
                // Миникарта рисует иконку вместо цветного кружка; без иконки — кружок.
                string? icon = null;
                float opacity = 1f;
                int layer = 100;
                bool displayOnMap = true;
                if (p.IsSdo)
                {
                    var meta = SdoMeta.Categories.TryGetValue(p.Category, out var metaItem) ? metaItem : null;
                    if (meta != null)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Icon)) icon = meta.Icon;
                        opacity = SdoMeta.OpacityOf(p.Category);
                        layer = SdoMeta.LayerOf(p.Category);
                        displayOnMap = SdoMeta.DisplayOnMapOf(p.Category);
                    }
                }
                arr.Add(new JObject
                {
                    ["uid"] = p.GameName,
                    ["category"] = p.Category,
                    ["name"] = p.RealName,
                    ["x"] = p.X, ["z"] = p.Z,
                    ["color"] = p.Color,
                    ["icon"] = icon,
                    ["opacity"] = opacity,
                    ["layer"] = layer,
                    ["display_on_map"] = displayOnMap,
                    ["hidden"] = p.Hidden == 1 || !p.Enabled,
                    ["overridden"] = p.IsOverride
                });
            }
            return arr;
        }

        private static JArray TargetsToJArray(List<JObject> targets)
        {
            var arr = new JArray();
            foreach (var t in targets)
            {
                var status = (string?)t["status"];
                arr.Add(new JObject
                {
                    ["id"] = (string?)t["id"] ?? (string?)t["gameName"] ?? "",
                    ["gameName"] = (string?)t["gameName"] ?? "",
                    ["realName"] = (string?)t["realName"] ?? (string?)t["name"] ?? "Цель",
                    ["x"] = ExtractTargetX(t),
                    ["y"] = 0,
                    ["z"] = ExtractTargetZ(t),
                    ["color"] = (string?)t["color"] ?? "default",
                    ["icon"] = (string?)t["icon"] ?? "default",
                    ["radius"] = t["radius"]?.Value<double>() ?? t["triggerRadius"]?.Value<double>() ?? 50,
                    ["active"] = (string?)t["status"] != "inactive",
                    ["questType"] = (string?)t["questType"] ?? null,
                    ["isRandom"] = t["isRandom"]?.Value<bool?>() ?? false,
                    ["hidden"] = t["hidden"]?.Value<int?>() ?? 0,
                    ["cooldown"] = t["cooldown"]?.Value<int?>() ?? 0,
                    ["cooldownUntil"] = (string?)t["cooldown_until"] ?? null,
                    ["deleteOnComplete"] = t["delete_on_complete"]?.Value<int?>() ?? 0
                });
            }
            return arr;
        }

        private static double ExtractTargetX(JObject t)
        {
            var coords = (string?)t["coords"];
            if (!string.IsNullOrEmpty(coords))
            {
                var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) return x;
            }
            return t["x"]?.Value<double>() ?? 0;
        }

        private static double ExtractTargetZ(JObject t)
        {
            var coords = (string?)t["coords"];
            if (!string.IsNullOrEmpty(coords))
            {
                var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                    if (double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) return z;
            }
            return t["z"]?.Value<double>() ?? 0;
        }
    }

    // Хелпер: delta-merge для ЗАПИСИ цели (JObject)->JObject — применяет только
    // присутствующие поля оверрайда к JSON-цели (не к PointData). Выделено сюда,
    // чтобы не менять MapEditorForm.ApplyJObjectToPoint (он для PointData).
    public static class JObjectTargetMergeExtensions
    {
        public static void ApplyToTargetEntry(JObject target, JObject ovr)
        {
            if (ovr.ContainsKey("realName")) target["realName"] = (string?)ovr["realName"] ?? target["realName"];
            if (ovr.ContainsKey("color")) target["color"] = (string?)ovr["color"] ?? target["color"];
            if (ovr.ContainsKey("radius")) target["radius"] = ovr["radius"]?.Value<double>() ?? target["radius"]?.Value<double>() ?? 50;
            else if (ovr.ContainsKey("triggerRadius")) target["radius"] = ovr["triggerRadius"]?.Value<double>() ?? target["radius"]?.Value<double>() ?? 50;
            if (ovr.ContainsKey("status")) target["status"] = (string?)ovr["status"] ?? "active";
            if (ovr.ContainsKey("hidden")) target["hidden"] = ovr["hidden"]?.Value<int>() ?? 0;
            if (ovr.ContainsKey("cooldown")) target["cooldown"] = ovr["cooldown"]?.Value<int>() ?? 0;
            if (ovr.ContainsKey("delete_on_complete")) target["delete_on_complete"] = ovr["delete_on_complete"]?.Value<int>() ?? 0;
            if (ovr.ContainsKey("questType")) target["questType"] = (string?)ovr["questType"] ?? target["questType"];
            if (ovr.ContainsKey("cooldown_until")) target["cooldown_until"] = (string?)ovr["cooldown_until"];
            double x, z;
            var coords = (string?)ovr["coords"];
            if (!string.IsNullOrEmpty(coords))
            {
                var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                double.TryParse(parts.Length > 0 ? parts[0] : "", NumberStyles.Any, CultureInfo.InvariantCulture, out x);
                double.TryParse(parts.Length > 2 ? parts[2] : "", NumberStyles.Any, CultureInfo.InvariantCulture, out z);
            }
            else
            {
                // ФИКС v64: строки-координаты в files пишутся с точкой (Invariant) — здесь
                // строки, TryParse Invariant корректен. Но если x — JValue-число, ToString()
                // может дать запятую (ru-RU) → читаем через Value<double>() напрямую.
                x = ovr["x"]?.Type == JTokenType.Integer || ovr["x"]?.Type == JTokenType.Float
                    ? ovr["x"].Value<double>() : ExtractStatic(target, 0);
                z = ovr["z"]?.Type == JTokenType.Integer || ovr["z"]?.Type == JTokenType.Float
                    ? ovr["z"].Value<double>() : ExtractStatic(target, 2);
            }
            target["x"] = x; target["z"] = z;
            target["coords"] = $"{x.ToString("F2", CultureInfo.InvariantCulture)}, 0.00, {z.ToString("F2", CultureInfo.InvariantCulture)}";

            static double ExtractStatic(JObject t, int idx)
            {
                var c = (string?)t["coords"];
                if (!string.IsNullOrEmpty(c))
                {
                    var p = c.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3 && double.TryParse(p[idx], NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
                }
                return t[idx == 0 ? "x" : "z"]?.Value<double>() ?? 0;
            }
        }
    }
}