using System.Configuration;

namespace ETS2_Assist_GUI
{
    public static class Settings
    {
        private static Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public static bool DebugMode
        {
            get => GetBool("DebugMode", false);
            set => Set("DebugMode", value);
        }

        public static string Language
        {
            get => GetString("Language", "en");
            set => Set("Language", value);
        }

        public static bool AutoStartSystem
        {
            get => GetBool("AutoStartSystem", false);
            set => Set("AutoStartSystem", value);
        }

        public static bool StartMinimized
        {
            get => GetBool("StartMinimized", false);
            set => Set("StartMinimized", value);
        }

        public static bool CheckUpdatesOnStart
        {
            get => GetBool("CheckUpdatesOnStart", true);
            set => Set("CheckUpdatesOnStart", value);
        }

        public static string GitHubRepoUrl
        {
            get => GetString("GitHubRepoUrl", "https://api.github.com/repos/yourname/ETS2_Assist/releases/latest");
            set => Set("GitHubRepoUrl", value);
        }

        private static string GetString(string key, string defaultValue)
        {
            return ConfigurationManager.AppSettings[key] ?? defaultValue;
        }

        private static bool GetBool(string key, bool defaultValue)
        {
            return bool.TryParse(ConfigurationManager.AppSettings[key], out var result) ? result : defaultValue;
        }

        private static void Set(string key, object value)
        {
            config.AppSettings.Settings.Remove(key);
            config.AppSettings.Settings.Add(key, value.ToString());
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}