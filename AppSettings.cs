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
                    GitHubRepoUrl = GitHubRepoUrl
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
        }
    }
}