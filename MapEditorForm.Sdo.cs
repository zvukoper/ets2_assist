using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    // =====================================================================
    // SDO — Static Data Objects (СДО): статические объекты, выгруженные из
    // редактора игры (data\editor_static_data\*.json + meta.json + icons\).
    // Каждый *.json — категория (имя файла без model_/overlay_; easter_* →
    // «Easter eggs»), объекты с координатами в ЕДИНОЙ игровой СК (та же, что
    // Overlays.json/города/миникарта/АР — конвертация НЕ нужна, проверено
    // парсером: floor(X/4000)=sectorX, floor(Z/4000)=sectorZ на всех файлах).
    // meta.json: читабельное имя категории, цвет (#rrggbb), иконка (png 50x50
    // из icons\; без иконки — цветная точка).
    // Точки SDO регистрируются в _pointModel (IsSdo=true) → единый конвейер
    // редактора: сайдбар, выделение/клик/перетаскивание, FitToAll, отправка на
    // миникарту через overrides-пайплайн (MainForm.OverridesPipeline).
    // =====================================================================
    public static class SdoMeta
    {
        public sealed class CatMeta
        {
            public string Name = "";   // читабельное название (пусто = ключ)
            public string Color = "";  // "#rrggbb" (пусто = дефолт)
            public string Icon = "";   // png 50x50 из icons\ (пусто = цветная точка)
            public float FontSize = 9f;      // размер шрифта подписи (по умолчанию 9)
            public string FontColor = "";   // "#rrggbb" (пусто = белый/серый)
            public string FontWeight = "normal"; // "normal" | "bold"
            public bool DisplayOnMap = true; // показывать категорию на карте по умолчанию (false — скрыта, включается вручную)
            public float Opacity = 1f;       // прозрачность отрисовки точки и названия (0..1)
            public int Layer = 100;          // слой сортировки отрисовки (1..9999; 0 = наивысший, всегда поверх)
        }

        private static Dictionary<string, CatMeta>? _cats;
        private static DateTime _loadedAt = DateTime.MinValue;
        private static readonly object _sync = new();
        // Обратный индекс: читабельное имя (meta.name) -> ключ категории (для поиска
        // по отображаемому имени, т.к. Category точки = display name, а ключ в meta = 5ka).
        private static Dictionary<string, string>? _nameToKey;

        public static string MetaPath =>
            Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data", "meta.json");
        public static string SdoDirectory =>
            Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data");
        public static string IconsDirectory =>
            Path.Combine(SdoDirectory, "icons");

        public static Dictionary<string, CatMeta> Categories
        {
            get
            {
                lock (_sync)
                {
                    // meta.json может правиться вручную между запусками — перечитываем,
                    // если файл новее кэша (или кэша ещё нет). File.GetLastWriteTimeUtc
                    // кэшируется слабо — сравниваем с допуском 1с (файловые системы).
                    try
                    {
                        if (_cats == null) { LoadMeta(); }
                        else if (File.Exists(MetaPath))
                        {
                            var wt = File.GetLastWriteTimeUtc(MetaPath);
                            if (wt - _loadedAt > TimeSpan.FromSeconds(1)) LoadMeta();
                        }
                    }
                    catch { if (_cats == null) LoadMeta(); }
                    return _cats ?? new Dictionary<string, CatMeta>();
                }
            }
        }

        private static void LoadMeta()
        {
            var dict = new Dictionary<string, CatMeta>(StringComparer.OrdinalIgnoreCase);
            var nameIdx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(MetaPath))
                {
                    var json = JObject.Parse(File.ReadAllText(MetaPath));
                    if (json["categories"] is JObject cats)
                        foreach (var prop in cats.Properties())
                        {
                            var o = prop.Value as JObject;
                            if (o == null) continue;
                            var m = new CatMeta
                            {
                                Name = (string?)o["name"] ?? "",
                                Color = (string?)o["color"] ?? "",
                                Icon = (string?)o["icon"] ?? "",
                                FontSize = o["font_size"]?.Value<float?>() ?? 9f,
                                FontColor = (string?)o["font_color"] ?? "",
                                FontWeight = (string?)o["font_weight"] ?? "normal",
                                DisplayOnMap = o["display_on_map"]?.Value<bool?>() ?? true,
                                Opacity = o["opacity"]?.Value<float?>() ?? 1f,
                                Layer = o["layer"]?.Value<int?>() ?? 100
                            };
                            dict[prop.Name] = m;
                            if (!string.IsNullOrWhiteSpace(m.Name))
                                nameIdx[m.Name] = prop.Name; // display name -> ключ
                        }
                    System.Diagnostics.Debug.WriteLine($"SdoMeta: загружено {dict.Count} категорий из {MetaPath}");
                }
                _loadedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SdoMeta.LoadMeta: " + ex.Message);
            }
            _cats = dict;
            _nameToKey = nameIdx;
        }

        // Читабельное имя категории (meta.name, иначе ключ).
        public static string DisplayName(string key)
            => Categories.TryGetValue(key, out var m) && !string.IsNullOrWhiteSpace(m.Name) ? m.Name : key;

        // Ключ категории по её отображаемому имени (Category точки = display name,
        // а ключ в meta может быть иным — напр. «Пятёрочка» vs «5ka»).
        private static string ResolveKey(string displayName)
        {
            if (Categories.ContainsKey(displayName)) return displayName;
            if (_nameToKey != null && _nameToKey.TryGetValue(displayName, out var k)) return k;
            return displayName;
        }

        // Цвет категории (meta.color, иначе серо-голубой дефолт).
        public static Color ColorOf(string displayName)
        {
            var key = ResolveKey(displayName);
            if (Categories.TryGetValue(key, out var m) && !string.IsNullOrWhiteSpace(m.Color))
            {
                try { return ColorTranslator.FromHtml(m.Color); }
                catch { }
            }
            return Color.FromArgb(120, 200, 240);
        }

        // Имя файла иконки категории (пусто = нет иконки, рисуем цветную точку).
        public static string? IconFileOf(string displayName)
        {
            var key = ResolveKey(displayName);
            if (!Categories.TryGetValue(key, out var m) || string.IsNullOrWhiteSpace(m.Icon))
                return null;
            var full = Path.Combine(IconsDirectory, m.Icon);
            return File.Exists(full) ? full : null;
        }

        // Цвет категории в формате "#rrggbb" (для PointData.Color; пусто = дефолт конвейера).
        public static string ColorHexOf(string displayName)
        {
            var key = ResolveKey(displayName);
            if (Categories.TryGetValue(key, out var m) && !string.IsNullOrWhiteSpace(m.Color))
                return m.Color;
            return "#78c8f0"; // дефолтный цвет SDO-категории (серо-голубой)
        }

        // Стиль подписи категории (для отрисовки метки на карте).
        public static float FontSizeOf(string displayName)
        {
            var key = ResolveKey(displayName);
            return Categories.TryGetValue(key, out var m) ? m.FontSize : 9f;
        }
        public static Color FontColorOf(string displayName)
        {
            var key = ResolveKey(displayName);
            if (Categories.TryGetValue(key, out var m) && !string.IsNullOrWhiteSpace(m.FontColor))
            {
                try { return ColorTranslator.FromHtml(m.FontColor); }
                catch { }
            }
            return Color.White;
        }
        public static bool FontBoldOf(string displayName)
        {
            var key = ResolveKey(displayName);
            return Categories.TryGetValue(key, out var m) && m.FontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase);
        }

        // Показывать ли категорию на карте по умолчанию (meta.display_on_map; по умолчанию true).
        public static bool DisplayOnMapOf(string displayName)
        {
            var key = ResolveKey(displayName);
            return !Categories.TryGetValue(key, out var m) || m.DisplayOnMap;
        }

        // Прозрачность отрисовки точки и названия (meta.opacity; 0..1, по умолчанию 1).
        public static float OpacityOf(string displayName)
        {
            var key = ResolveKey(displayName);
            if (Categories.TryGetValue(key, out var m))
            {
                float o = m.Opacity;
                if (o < 0f) o = 0f;
                if (o > 1f) o = 1f;
                return o;
            }
            return 1f;
        }

        // Слой сортировки отрисовки (meta.layer; 1..9999, 0 = наивысший приоритет, всегда поверх).
        public static int LayerOf(string displayName)
        {
            var key = ResolveKey(displayName);
            return Categories.TryGetValue(key, out var m) ? m.Layer : 100;
        }
    }

    // Парсер статических JSON-файлов SDO → список PointData (Category=категория).
    public static class SdoLoader
    {
        public sealed record SdoPoint(string Category, string GameName, string RealName, double X, double Y, double Z);

        public static string SdoDirectory => SdoMeta.SdoDirectory;

        // Читает все *.json (кроме meta.json) и возвращает точки.
        // Формат файла: { category, source, easter, count, objects: [ { uid, sector, x, y, z, name? } ] }.
        public static List<SdoPoint> LoadAll()
        {
            var list = new List<SdoPoint>();
            try
            {
                if (!Directory.Exists(SdoDirectory)) return list;
                foreach (var file in Directory.EnumerateFiles(SdoDirectory, "*.json"))
                {
                    if (Path.GetFileName(file).Equals("meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                    JObject json;
                    try { json = JObject.Parse(File.ReadAllText(file)); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SdoLoader {Path.GetFileName(file)}: {ex.Message}");
                        continue;
                    }
                    var category = (string?)json["category"] ?? Path.GetFileNameWithoutExtension(file);
                    // Читабельное имя категории (meta.name) — для Category точки.
                    var dispCat = SdoMeta.DisplayName(category);
                    // Easter eggs: объекты называются по имени (cottage, garage…), НЕ по категории.
                    bool isEaster = json["easter"]?.Value<bool>() ?? false;
                    if (json["objects"] is not JArray objs) continue;
                    foreach (var item in objs.OfType<JObject>())
                    {
                        // ФИКС-правило v64: числовые JValue — ТОЛЬКО Value<double>() (ru-RU ToString даёт запятую).
                        if (item["x"] == null || item["z"] == null) continue;
                        double x = item["x"].Value<double>();
                        double z = item["z"].Value<double>();
                        double y = item["y"]?.Value<double>() ?? 0;
                        if (!double.IsFinite(x) || !double.IsFinite(z)) continue;
                        var uid = (string?)item["uid"] ?? "";
                        if (string.IsNullOrEmpty(uid)) continue;
                        // Отображаемое имя: для Easter eggs — имя объекта (cottage…);
                        // для остальных — имя КАТЕГОРИИ (5ka → «Пятёрочка» из meta).
                        var realName = isEaster ? ((string?)item["name"] ?? uid) : dispCat;
                        list.Add(new SdoPoint(dispCat, uid, realName, x, y, z));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SdoLoader.LoadAll: " + ex.Message);
            }
            return list;
        }
    }
}