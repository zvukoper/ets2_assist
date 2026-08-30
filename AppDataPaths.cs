using System;
using System.IO;
using System.Linq;

namespace ETS2_Assist_GUI
{
    internal static class AppDataPaths
    {
        public static string StaticDataDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        public static string UserDataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ETS2_Assist");

        public static string SavedTracksDirectory => Path.Combine(UserDataDirectory, "saved_tracks");
        public static string CustomTargetsFile => Path.Combine(UserDataDirectory, "custom_targets.json");
        // Хранилище целей тестовых кнопок: теперь в папке map_overrides (система overrides).
        public static string TestTargetsFile => Path.Combine(UserDataDirectory, "map_overrides", "test_targets.json");
        public static string MapOverridesDirectory => Path.Combine(UserDataDirectory, "map_overrides");
        public static string MapOverridesLoadOrderFile => Path.Combine(UserDataDirectory, "map_overrides", "load_order.txt");
        public static string WebDataFile => Path.Combine(UserDataDirectory, "web_data.json");
        public static string JobStateFile => Path.Combine(UserDataDirectory, "job_state.json");
        public static string ConfigFile => Path.Combine(UserDataDirectory, "config.json");
        public static string TriggerFile => Path.Combine(UserDataDirectory, "save_trail.trigger");

        public static void EnsureUserData()
        {
            Directory.CreateDirectory(UserDataDirectory);
            Directory.CreateDirectory(SavedTracksDirectory);
            MigrateLegacyUserData();
            SeedFile("custom_targets.default.json", CustomTargetsFile);
            SeedFile("web_data.default.json", WebDataFile);
            SeedFile("job_state.default.json", JobStateFile);
            SeedFile("config.json", ConfigFile);
        }

        private static void MigrateLegacyUserData()
        {
            string legacyDataDirectory = StaticDataDirectory;
            foreach (string fileName in new[] { "custom_targets.json", "web_data.json", "job_state.json" })
            {
                string sourcePath = Path.Combine(legacyDataDirectory, fileName);
                string destinationPath = Path.Combine(UserDataDirectory, fileName);
                if (!File.Exists(destinationPath) && File.Exists(sourcePath))
                    File.Copy(sourcePath, destinationPath);
            }

            if (!Directory.EnumerateFileSystemEntries(SavedTracksDirectory).Any())
            {
                string legacyTracksDirectory = Path.Combine(legacyDataDirectory, "saved_tracks");
                if (Directory.Exists(legacyTracksDirectory))
                {
                    foreach (string sourcePath in Directory.EnumerateFiles(legacyTracksDirectory))
                    {
                        string destinationPath = Path.Combine(SavedTracksDirectory, Path.GetFileName(sourcePath));
                        if (!File.Exists(destinationPath))
                            File.Copy(sourcePath, destinationPath);
                    }
                }
            }
        }

        private static void SeedFile(string sourceName, string destinationPath)
        {
            if (File.Exists(destinationPath)) return;

            string sourcePath = Path.Combine(StaticDataDirectory, sourceName);
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, destinationPath);
        }
    }
}
