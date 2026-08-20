using System.Collections.Generic;

namespace ETS2_Assist_GUI.Models
{
    /// <summary>
    /// Модель настроек приложения.
    /// Сохраняется в config.json.
    /// </summary>
    public class Settings
    {
        // ===== ОБЩИЕ НАСТРОЙКИ =====
        public string Language { get; set; } = "ru";
        public bool AutoStartSystem { get; set; } = true;
        public bool StartMinimized { get; set; } = false;

        // ===== НАСТРОЙКИ ЗАПИСИ ТРЕКОВ =====
        public RecordMode RecordMode { get; set; } = RecordMode.Auto;
        public int RecordDurationMinutes { get; set; } = 60;
        public bool AutoSaveEnabled { get; set; } = true;
        public SaveFormat SaveFormat { get; set; } = SaveFormat.ThreeFiles;
        public string DefaultSuffix { get; set; } = "";
        public string DefaultDescription { get; set; } = "";

        // ===== НАСТРОЙКИ ШЛЕЙФА =====
        public double TrailInterval { get; set; } = 3;
        public double DataInterval { get; set; } = 25;
        public double MinSpeed { get; set; } = 0;
        public double MaxSpeed { get; set; } = 115;

        // ===== ПОЛЬЗОВАТЕЛЬСКИЕ ХОТКЕИ =====
        public Dictionary<string, string> Hotkeys { get; set; } = new()
        {
            { "SaveTrail", "Shift+Ctrl+S" },
            { "StartRecord", "Shift+Ctrl+R" },
            { "StopRecord", "Shift+Ctrl+X" },
            { "AddMarker", "Shift+Ctrl+N" },
            { "TestWindow", "Shift+Ctrl+T" }
        };

        // ===== ПАРАМЕТРЫ ПЛЕЕРА =====
        public double PlaybackSpeed { get; set; } = 1.0;
        public bool FollowTruck { get; set; } = true;
        public double StepSeconds { get; set; } = 1.0;

        // ===== ПУТИ (вычисляемые) =====
        public string DataFolder => "data";
        public string SavedTracksFolder => System.IO.Path.Combine(DataFolder, "saved_tracks");
        public string ConfigFile => System.IO.Path.Combine(DataFolder, "config.json");
    }

    /// <summary>
    /// Режим записи треков.
    /// </summary>
    public enum RecordMode
    {
        Off = 0,          // Запись выключена
        Auto = 1,         // Автоматическая запись
        Manual = 2,       // Вручную (Start/Stop)
        TrailOnly = 3     // Только визуальный шлейф, без сохранения
    }

    /// <summary>
    /// Формат сохранения трека.
    /// </summary>
    public enum SaveFormat
    {
        OneFile = 1,      // Всё в одном HTML
        TwoFiles = 2,     // HTML + JSON трек
        ThreeFiles = 3    // HTML + JSON трек + JSON карта
    }
}