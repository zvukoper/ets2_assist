using System;
using System.IO;
using System.Text.Json;

namespace ETS2_Assist_GUI
{
    public static class AppSettings
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static bool DebugMode { get; set; } = false;
        public static string Language { get; set; } = "en";
        public static bool AutoStartSystem { get; set; } = false;
        public static bool StartMinimized { get; set; } = false;
        public static bool CheckUpdatesOnStart { get; set; } = true;
        public static string GitHubRepoUrl { get; set; } = "https://api.github.com/repos/zvukoper/ets2_assist/releases/latest";

        // ===== НОВЫЕ НАСТРОЙКИ ЗАПИСИ =====
        public static string RecordMode { get; set; } = "Auto";      // Off, Auto, Manual, TrailOnly
        public static int RecordDurationMinutes { get; set; } = 60;
        public static bool AutoSaveEnabled { get; set; } = true;
        public static string SaveFormat { get; set; } = "ThreeFiles"; // OneFile, TwoFiles, ThreeFiles
        public static string DefaultSuffix { get; set; } = "";
        public static string DefaultDescription { get; set; } = "";

        static AppSettings() => Load();

        public static void Load()
        {
            if (!File.Exists(SettingsFile)) return;
            try
            {
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                if (settings != null)
                {
                    DebugMode = settings.DebugMode;
                    Language = settings.Language ?? "en";
                    AutoStartSystem = settings.AutoStartSystem;
                    StartMinimized = settings.StartMinimized;
                    CheckUpdatesOnStart = settings.CheckUpdatesOnStart;
                    GitHubRepoUrl = settings.GitHubRepoUrl ?? "https://api.github.com/repos/zvukoper/ets2_assist/releases/latest";
                    RecordMode = settings.RecordMode ?? "Auto";
                    RecordDurationMinutes = settings.RecordDurationMinutes;
                    AutoSaveEnabled = settings.AutoSaveEnabled;
                    SaveFormat = settings.SaveFormat ?? "ThreeFiles";
                    DefaultSuffix = settings.DefaultSuffix ?? "";
                    DefaultDescription = settings.DefaultDescription ?? "";
                }
            }
            catch { /* ignore errors */ }
        }

        public static void Save()
        {
            try
            {
                var settings = new SettingsData
                {
                    DebugMode = DebugMode,
                    Language = Language,
                    AutoStartSystem = AutoStartSystem,
                    StartMinimized = StartMinimized,
                    CheckUpdatesOnStart = CheckUpdatesOnStart,
                    GitHubRepoUrl = GitHubRepoUrl,
                    RecordMode = RecordMode,
                    RecordDurationMinutes = RecordDurationMinutes,
                    AutoSaveEnabled = AutoSaveEnabled,
                    SaveFormat = SaveFormat,
                    DefaultSuffix = DefaultSuffix,
                    DefaultDescription = DefaultDescription
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { /* ignore */ }
        }

        private class SettingsData
        {
            public bool DebugMode { get; set; }
            public string Language { get; set; } = string.Empty;
            public bool AutoStartSystem { get; set; }
            public bool StartMinimized { get; set; }
            public bool CheckUpdatesOnStart { get; set; }
            public string GitHubRepoUrl { get; set; } = string.Empty;
            public string RecordMode { get; set; } = "Auto";
            public int RecordDurationMinutes { get; set; } = 60;
            public bool AutoSaveEnabled { get; set; } = true;
            public string SaveFormat { get; set; } = "ThreeFiles";
            public string DefaultSuffix { get; set; } = string.Empty;
            public string DefaultDescription { get; set; } = string.Empty;
        }
    }
}