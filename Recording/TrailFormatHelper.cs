using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETS2_Assist_GUI.Models;
using ETS2_Assist_GUI.Helpers;

namespace ETS2_Assist_GUI.Recording
{
    /// <summary>
    /// Утилиты для конвертации данных трека между форматами.
    /// Поддерживает: старый JSON-формат, новый компактный формат, 
    /// а также разделение на трек и карту.
    /// </summary>
    public static class TrailFormatHelper
    {
        private const char SEPARATOR = ';';
        private const int DECIMAL_PLACES = 2;

        /// <summary>
        /// Преобразует массив кадров в компактные строки.
        /// </summary>
        public static List<string> FramesToCompact(IEnumerable<TrailFrame> frames)
        {
            if (frames == null) return new List<string>();

            return frames.Select(frame =>
            {
                var parts = new List<string>
                {
                    frame.Time.ToString(),
                    frame.X.ToString($"F{DECIMAL_PLACES}", CultureInfo.InvariantCulture),
                    frame.Z.ToString($"F{DECIMAL_PLACES}", CultureInfo.InvariantCulture),
                    frame.Heading.ToString("F4", CultureInfo.InvariantCulture),
                    frame.Speed.ToString($"F{DECIMAL_PLACES}", CultureInfo.InvariantCulture),
                    frame.EventType.ToString()
                };

                // Опциональные поля
                parts.Add(string.IsNullOrEmpty(frame.Label) ? "" : frame.Label);
                parts.Add(string.IsNullOrEmpty(frame.Color) ? "" : frame.Color);
                parts.Add(string.IsNullOrEmpty(frame.Subtext) ? "" : frame.Subtext);
                parts.Add(frame.Fuel > 0 ? frame.Fuel.ToString("F1", CultureInfo.InvariantCulture) : "");
                parts.Add(frame.Damage > 0 ? frame.Damage.ToString("F1", CultureInfo.InvariantCulture) : "");

                return string.Join(SEPARATOR.ToString(), parts);
            }).ToList();
        }

        /// <summary>
        /// Разбирает компактную строку в объект TrailFrame.
        /// </summary>
        public static TrailFrame CompactToFrame(string compactLine)
        {
            if (string.IsNullOrEmpty(compactLine))
                return null;

            var parts = compactLine.Split(SEPARATOR);
            if (parts.Length < 6)
                return null;

            var frame = new TrailFrame
            {
                Time = long.Parse(parts[0]),
                X = parts[1].ToDoubleInvariant(),
                Z = parts[2].ToDoubleInvariant(),
                Heading = parts[3].ToDoubleInvariant(),
                Speed = parts[4].ToDoubleInvariant(),
                EventType = parts[5].ToInt()
            };

            // Опциональные поля (если есть)
            if (parts.Length > 6) frame.Label = parts[6];
            if (parts.Length > 7) frame.Color = parts[7];
            if (parts.Length > 8) frame.Subtext = parts[8];
            if (parts.Length > 9) frame.Fuel = parts[9].ToDoubleInvariant();
            if (parts.Length > 10) frame.Damage = parts[10].ToDoubleInvariant();

            return frame;
        }

        /// <summary>
        /// Конвертирует старый JSON-формат в новый компактный.
        /// </summary>
        public static TrailData ConvertFromLegacy(dynamic legacyData)
        {
            var trailData = new TrailData();
            var meta = new TrailMetadata
            {
                Name = legacyData.name ?? "Трек",
                Description = legacyData.description ?? "",
                StartTime = legacyData.startTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                DurationMs = legacyData.durationMs ?? 0,
                TrailInterval = legacyData.trailInterval ?? 3,
                DataInterval = legacyData.dataInterval ?? 25,
                MinSpeed = legacyData.minSpeed ?? 0,
                MaxSpeed = legacyData.maxSpeed ?? 115,
                TotalDistance = legacyData.totalDistance ?? 0
            };

            // Собираем словарь типов событий
            meta.EventTypes = new Dictionary<int, string>
            {
                { 0, "none" },
                { 1, "stop" },
                { 2, "service" },
                { 3, "parking" },
                { 4, "damage" },
                { 5, "user_marker" }
            };

            trailData.Meta = meta;

            // Конвертируем точки шлейфа
            var frames = new List<TrailFrame>();
            if (legacyData.trail != null)
            {
                foreach (var point in legacyData.trail)
                {
                    var frame = new TrailFrame
                    {
                        Time = point.t ?? 0,
                        X = point.x ?? 0,
                        Z = point.z ?? 0,
                        Heading = point.heading ?? 0,
                        Speed = point.speed ?? 0,
                        EventType = 0,
                        Fuel = point.fuel ?? 0,
                        Damage = point.damage ?? 0
                    };
                    frames.Add(frame);
                }
            }

            // Добавляем события
            if (legacyData.events != null)
            {
                foreach (var evt in legacyData.events)
                {
                    var frame = new TrailFrame
                    {
                        Time = evt.time ?? 0,
                        X = evt.x ?? 0,
                        Z = evt.z ?? 0,
                        EventType = evt.type ?? 0,
                        Label = evt.label ?? "",
                        Color = evt.color ?? "",
                        Subtext = evt.subtext ?? ""
                    };
                    frames.Add(frame);
                }
            }

            trailData.Data = FramesToCompact(frames.OrderBy(f => f.Time));

            // Карта (если есть)
            if (legacyData.mapData != null)
            {
                trailData.MapData = new MapData();
                // Заполняем города, дороги, цели из legacy-данных
                // (здесь нужно адаптировать под структуру)
            }

            return trailData;
        }

        /// <summary>
        /// Разделяет данные трека и карты.
        /// </summary>
        public static void SplitTrailAndMap(TrailData trailData, out TrailData trailOnly, out MapData mapData)
        {
            trailOnly = new TrailData
            {
                Meta = trailData.Meta,
                Data = trailData.Data
            };

            mapData = trailData.MapData ?? new MapData();
        }

        /// <summary>
        /// Объединяет трек и карту в один объект.
        /// </summary>
        public static TrailData MergeTrailAndMap(TrailData trail, MapData map)
        {
            return new TrailData
            {
                Meta = trail.Meta,
                Data = trail.Data,
                MapData = map
            };
        }

        /// <summary>
        /// Проверяет, является ли строка валидным кадром компактного формата.
        /// </summary>
        public static bool IsValidCompactFrame(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var parts = line.Split(SEPARATOR);
            return parts.Length >= 6 && long.TryParse(parts[0], out _);
        }

        /// <summary>
        /// Сериализует TrailData в JSON (с учётом формата).
        /// </summary>
        public static string ToJson(TrailData trailData, bool indented = false)
        {
            var formatting = indented ? Newtonsoft.Json.Formatting.Indented : Newtonsoft.Json.Formatting.None;
            return Newtonsoft.Json.JsonConvert.SerializeObject(trailData, formatting);
        }

        /// <summary>
        /// Десериализует JSON в TrailData.
        /// </summary>
        public static TrailData FromJson(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TrailData>(json);
        }
    }
}