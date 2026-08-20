using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ETS2_Assist_GUI.Models;
using ETS2_Assist_GUI.Helpers;

namespace ETS2_Assist_GUI.Storage
{
    /// <summary>
    /// Управляет загрузкой и сохранением данных: карта, настройки, треки.
    /// </summary>
    public class DataManager
    {
        private readonly Logger _logger;
        private readonly string _baseDirectory;

        public DataManager(Logger logger)
        {
            _logger = logger;
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Загружает города из файла локализации.
        /// </summary>
        public List<MapData.City> LoadCities(string filePath)
        {
            var cities = new List<MapData.City>();
            var fullPath = Path.Combine(_baseDirectory, filePath);

            if (!File.Exists(fullPath))
            {
                _logger.Log($"Файл городов не найден: {fullPath}");
                return cities;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var data = JsonHelper.Parse(json);
                if (data == null) return cities;

                var citiesList = data["citiesList"] as Newtonsoft.Json.Linq.JArray;
                if (citiesList != null)
                {
                    foreach (var c in citiesList)
                    {
                        cities.Add(new MapData.City
                        {
                            X = c["x"]?.Value<double>() ?? 0,
                            Z = c["z"]?.Value<double>() ?? 0,
                            Name = c["realName"]?.Value<string>() ?? c["gameName"]?.Value<string>() ?? "?"
                        });
                    }
                }

                _logger.Log($"Загружено {cities.Count} городов.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки городов: {ex.Message}");
            }

            return cities;
        }

        /// <summary>
        /// Загружает дороги из GeoJSON.
        /// </summary>
        public List<MapData.Road> LoadRoads(string filePath)
        {
            var roads = new List<MapData.Road>();
            var fullPath = Path.Combine(_baseDirectory, filePath);

            if (!File.Exists(fullPath))
            {
                _logger.Log($"Файл дорог не найден: {fullPath}");
                return roads;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var data = JsonHelper.Parse(json);
                if (data == null) return roads;

                var features = data["features"] as Newtonsoft.Json.Linq.JArray;
                if (features != null)
                {
                    foreach (var feature in features)
                    {
                        var coords = feature["geometry"]?["coordinates"] as Newtonsoft.Json.Linq.JArray;
                        if (coords == null || coords.Count < 2) continue;

                        var roadType = feature["properties"]?["roadType"]?["String"]?.Value<string>() ?? "default";
                        for (int i = 0; i < coords.Count - 1; i++)
                        {
                            var p1 = coords[i] as Newtonsoft.Json.Linq.JArray;
                            var p2 = coords[i + 1] as Newtonsoft.Json.Linq.JArray;
                            if (p1 == null || p2 == null || p1.Count < 2 || p2.Count < 2) continue;

                            roads.Add(new MapData.Road
                            {
                                X1 = p1[0].Value<double>(),
                                Z1 = p1[1].Value<double>(),
                                X2 = p2[0].Value<double>(),
                                Z2 = p2[1].Value<double>(),
                                Type = roadType
                            });
                        }
                    }
                }

                _logger.Log($"Загружено {roads.Count} отрезков дорог.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки дорог: {ex.Message}");
            }

            return roads;
        }

        /// <summary>
        /// Загружает пользовательские цели (custom_targets.json).
        /// </summary>
        public List<MapData.CustomTarget> LoadCustomTargets(string filePath)
        {
            var targets = new List<MapData.CustomTarget>();
            var fullPath = Path.Combine(_baseDirectory, filePath);

            if (!File.Exists(fullPath))
            {
                _logger.Log($"Файл целей не найден: {fullPath}");
                return targets;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var data = JsonHelper.Parse(json);
                if (data == null) return targets;

                var targetsList = data["customTargets"] as Newtonsoft.Json.Linq.JArray;
                if (targetsList != null)
                {
                    foreach (var t in targetsList)
                    {
                        var coords = t["coords"]?.Value<string>() ?? "0,0,0";
                        var parts = coords.Split(',');
                        var x = parts.Length > 0 ? double.Parse(parts[0].Trim()) : 0;
                        var y = parts.Length > 1 ? double.Parse(parts[1].Trim()) : 0;
                        var z = parts.Length > 2 ? double.Parse(parts[2].Trim()) : 0;

                        targets.Add(new MapData.CustomTarget
                        {
                            X = x,
                            Y = y,
                            Z = z,
                            Name = t["realName"]?.Value<string>() ?? t["gameName"]?.Value<string>() ?? "Цель",
                            Active = t["status"]?.Value<string>() == "active",
                            Color = t["color"]?.Value<string>() ?? "default",
                            Icon = t["icon"]?.Value<string>() ?? "default",
                            ZoomOnMap = t["targetMapOverview"]?.Value<bool>() ?? false
                        });
                    }
                }

                _logger.Log($"Загружено {targets.Count} целей.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки целей: {ex.Message}");
            }

            return targets;
        }

        /// <summary>
        /// Загружает POI из Overlays.json.
        /// </summary>
        public List<dynamic> LoadPois(string filePath)
        {
            var pois = new List<dynamic>();
            var fullPath = Path.Combine(_baseDirectory, filePath);

            if (!File.Exists(fullPath))
            {
                _logger.Log($"Файл POI не найден: {fullPath}");
                return pois;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var data = JsonHelper.Parse(json);
                if (data == null) return pois;

                foreach (var property in data.Properties())
                {
                    var items = property.Value as Newtonsoft.Json.Linq.JArray;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            pois.Add(new
                            {
                                x = item["x"]?.Value<double>() ?? 0,
                                z = item["z"]?.Value<double>() ?? 0,
                                type = property.Name,
                                uid = item["uid"]?.Value<string>() ?? ""
                            });
                        }
                    }
                }

                _logger.Log($"Загружено {pois.Count} POI.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки POI: {ex.Message}");
            }

            return pois;
        }

        /// <summary>
        /// Сохраняет объект в JSON-файл.
        /// </summary>
        public void SaveJson(string filePath, object data)
        {
            try
            {
                var fullPath = Path.Combine(_baseDirectory, filePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonHelper.Serialize(data, true);
                File.WriteAllText(fullPath, json);
                _logger.Log($"Данные сохранены в {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения JSON: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает JSON-файл и возвращает десериализованный объект.
        /// </summary>
        public T LoadJson<T>(string filePath) where T : class, new()
        {
            var fullPath = Path.Combine(_baseDirectory, filePath);
            if (!File.Exists(fullPath))
                return new T();

            try
            {
                var json = File.ReadAllText(fullPath);
                return JsonHelper.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки JSON: {ex.Message}");
                return new T();
            }
        }
    }
}