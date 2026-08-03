using System;
using System.Collections.Generic;
using System.IO;

namespace ETS2_Assist_GUI
{
    public class LanguageManager
    {
        private static LanguageManager? instance;
        public static LanguageManager Instance => instance ??= new LanguageManager();

        private readonly Dictionary<string, string> strings = new Dictionary<string, string>();
        public string CurrentLanguage { get; private set; } = "en";

        public event EventHandler? LanguageChanged;

        private LanguageManager()
        {
            LoadLanguage(AppSettings.Language);
        }

        public void LoadLanguage(string langCode)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, "language", langCode + ".csv");
            if (!File.Exists(path))
            {
                path = Path.Combine(baseDir, "data", "language", langCode + ".csv");
                if (!File.Exists(path))
                    return;
            }

            strings.Clear();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                    strings[parts[0].Trim()] = parts[1].Trim();
            }
            CurrentLanguage = langCode;
            AppSettings.Language = langCode;
            AppSettings.Save();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Get(string key) => strings.TryGetValue(key, out var val) ? val : key;
    }
}