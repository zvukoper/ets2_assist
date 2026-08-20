using System;
using System.IO;
using Newtonsoft.Json;
using ETS2_Assist_GUI.Models;
using ETS2_Assist_GUI.Helpers;

namespace ETS2_Assist_GUI.Storage
{
    /// <summary>
    /// Загружает и сохраняет настройки приложения в config.json.
    /// </summary>
    public class SettingsManager
    {
        private readonly Logger _logger;
        private readonly string _configPath;
        private Settings _settings = new();

        public SettingsManager(Logger logger)
        {
            _logger = logger;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(baseDir, "data", "config.json");
        }

        /// <summary>
        /// Загружает настройки из файла. Если файла нет, создаёт настройки по умолчанию.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(_configPath))
            {
                _logger.Log("config.json не найден, создаются настройки по умолчанию.");
                Save();
                return;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                _settings = JsonHelper.Deserialize<Settings>(json) ?? new Settings();
                _logger.Log("Настройки загружены.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка загрузки настроек: {ex.Message}. Используются настройки по умолчанию.");
                _settings = new Settings();
            }
        }

        /// <summary>
        /// Сохраняет текущие настройки в файл.
        /// </summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonHelper.Serialize(_settings, true);
                File.WriteAllText(_configPath, json);
                _logger.Log("Настройки сохранены.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения настроек: {ex.Message}");
            }
        }

        /// <summary>
        /// Возвращает значение настройки по ключу (тип T).
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            try
            {
                var prop = typeof(Settings).GetProperty(key);
                if (prop == null)
                    return defaultValue;

                var value = prop.GetValue(_settings);
                return value == null ? defaultValue : (T)value;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Устанавливает значение настройки по ключу.
        /// </summary>
        public void Set<T>(string key, T value)
        {
            try
            {
                var prop = typeof(Settings).GetProperty(key);
                if (prop == null) return;

                prop.SetValue(_settings, value);
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка установки настройки {key}: {ex.Message}");
            }
        }

        /// <summary>
        /// Возвращает весь объект настроек.
        /// </summary>
        public Settings GetSettings() => _settings;

        /// <summary>
        /// Обновляет настройки из переданного объекта.
        /// </summary>
        public void UpdateSettings(Settings newSettings)
        {
            _settings = newSettings ?? new Settings();
        }
    }
}