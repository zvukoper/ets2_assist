using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    // Наложение map_overrides на миникарту: те же delta-merge правила, что в редакторе
    // (MapEditorForm.ApplyOverridesToModel → ApplyJObjectToPoint). Приложение строит
    // «эффективные» города/POI/пользовательские точки и рассылает их на веб-оверлей
    // командой points_overrides_data (веб-страница сама грузит только статику из data\).
    public partial class MainForm
    {
        // Редактор вызывает после успешного сохранения/удаления/добавления точки.
        public static event Action? PointsOverridesChanged;
        internal static void NotifyPointsOverridesChanged() => PointsOverridesChanged?.Invoke();

        private System.Windows.Forms.Timer? _pointsOverridesDebounce;

        private void HookPointsOverridesChanged(bool subscribe)
        {
            if (subscribe) PointsOverridesChanged += OnPointsOverridesChanged;
            else PointsOverridesChanged -= OnPointsOverridesChanged;
        }

        private void OnPointsOverridesChanged()
        {
            // Debounce: серия сохранений (например, перетаскивание) — одна рассылка.
            if (_pointsOverridesDebounce == null)
            {
                _pointsOverridesDebounce = new System.Windows.Forms.Timer { Interval = 400 };
                _pointsOverridesDebounce.Tick += (s, e) => { _pointsOverridesDebounce!.Stop(); SendPointsOverridesToMap(); };
            }
            _pointsOverridesDebounce.Stop();
            _pointsOverridesDebounce.Start();
        }

        internal void SendPointsOverridesToMap()
        {
            try
            {
                // 1) Статика городов (cities_sibirmap.json), ключ = gameName.
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
                catch (Exception ex) { AppendLog($"[OVR-MAP] Города: {ex.Message}"); }

                // 2) Статика POI (Overlays.json), ключ = uid (или имя категории, как в редакторе).
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
                                if (!double.TryParse(item["x"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) continue;
                                if (!double.TryParse(item["z"].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) continue;
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
                catch (Exception ex) { AppendLog($"[OVR-MAP] POI: {ex.Message}"); }

                // 3) Статические цели (custom_targets.json) пропускаем — их отдаёт targets_data.
                var staticTargets = new HashSet<string>();
                try
                {
                    var path = AppDataPaths.CustomTargetsFile;
                    if (File.Exists(path))
                    {
                        var list = (JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray) ?? new JArray();
                        foreach (var t in list.OfType<JObject>())
                        {
                            var id = (string?)t["gameName"];
                            if (!string.IsNullOrEmpty(id)) staticTargets.Add(id);
                        }
                    }
                }
                catch (Exception ex) { AppendLog($"[OVR-MAP] Цели: {ex.Message}"); }

                // 4) Delta-merge overrides поверх статики (load_order: индекс 0 — низший приоритет).
                var users = new Dictionary<string, PointData>();
                foreach (var (file, jo) in ReadOverridesInLoadOrder())
                {
                    var key = (string?)jo["gameName"];
                    if (string.IsNullOrEmpty(key)) continue;
                    PointData target;
                    if (cities.TryGetValue(key, out var city)) target = city;
                    else if (pois.TryGetValue(key, out var poi)) target = poi;
                    else
                    {
                        if (staticTargets.Contains(key)) continue;
                        if (!users.TryGetValue(key, out var up)) { up = new PointData { GameName = key }; users[key] = up; }
                        target = up;
                    }
                    MapEditorForm.ApplyJObjectToPoint(target, jo);
                    target.SourceFile = file;
                    target.IsOverride = true;
                }

                // 5) Payload: ТОЛЬКО переопределённые города/POI и пользовательские точки.
                var jCities = new JArray();
                foreach (var c in cities.Values)
                {
                    if (!c.IsOverride) continue;
                    jCities.Add(new JObject
                    {
                        ["gameName"] = c.GameName,
                        ["realName"] = c.RealName,
                        ["x"] = c.X,
                        ["z"] = c.Z,
                        ["hidden"] = c.Hidden == 1 || !c.Enabled
                    });
                }
                var jPois = new JArray();
                foreach (var p in pois.Values)
                {
                    if (!p.IsOverride) continue;
                    jPois.Add(new JObject
                    {
                        ["uid"] = p.GameName,
                        ["category"] = p.Category,
                        ["name"] = p.RealName,
                        ["x"] = p.X,
                        ["z"] = p.Z,
                        ["hidden"] = p.Hidden == 1 || !p.Enabled
                    });
                }
                var jUsers = new JArray();
                foreach (var pd in users.Values)
                {
                    jUsers.Add(new JObject
                    {
                        ["gameName"] = pd.GameName,
                        ["realName"] = pd.RealName,
                        ["color"] = pd.Color,
                        ["x"] = pd.X,
                        ["z"] = pd.Z,
                        ["hidden"] = pd.Hidden == 1 || !pd.Enabled
                    });
                }

                var payload = new JObject { ["cities"] = jCities, ["pois"] = jPois, ["userPoints"] = jUsers };
                SendCommandToMap("points_overrides_data", payload);
                AppendLog($"[OVR-MAP] Отправлено: cities={jCities.Count}, pois={jPois.Count}, userPoints={jUsers.Count}");
            }
            catch (Exception ex)
            {
                AppendLog($"[OVR-MAP] Ошибка подготовки данных для миникарты: {ex.Message}");
            }
        }

        private static IEnumerable<(string file, JObject entry)> ReadOverridesInLoadOrder()
        {
            var dir = Path.Combine(AppDataPaths.UserDataDirectory, "map_overrides");
            var orderFile = Path.Combine(dir, "load_order.txt");
            if (!File.Exists(orderFile)) yield break;
            foreach (var raw in File.ReadAllLines(orderFile))
            {
                var f = raw.Trim();
                if (f.Length == 0) continue;
                var path = Path.Combine(dir, f);
                if (!File.Exists(path)) continue;
                var list = TryParseCustomTargets(path);
                if (list == null) continue;
                foreach (var t in list.OfType<JObject>()) yield return (f, t);
            }
        }

        private static JArray? TryParseCustomTargets(string path)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OVR-MAP] {path}: {ex.Message}");
                return null;
            }
        }
    }
}