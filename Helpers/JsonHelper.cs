using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI.Helpers
{
    /// <summary>
    /// Утилиты для работы с JSON (сериализация/десериализация, форматирование).
    /// </summary>
    public static class JsonHelper
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        /// <summary>
        /// Сериализует объект в JSON-строку с настройками по умолчанию.
        /// </summary>
        public static string Serialize(object obj, bool indented = true)
        {
            if (obj == null)
                return "null";

            var formatting = indented ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(obj, formatting, _settings);
        }

        /// <summary>
        /// Десериализует JSON-строку в объект указанного типа.
        /// </summary>
        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                return default;

            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        /// <summary>
        /// Десериализует JSON-строку в JObject для динамического доступа.
        /// </summary>
        public static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Проверяет, является ли строка валидным JSON.
        /// </summary>
        public static bool IsValidJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                JToken.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Объединяет два JSON-объекта (поверхностное слияние).
        /// </summary>
        public static JObject Merge(JObject baseObj, JObject mergeObj)
        {
            if (baseObj == null) return mergeObj;
            if (mergeObj == null) return baseObj;

            var result = new JObject(baseObj);
            foreach (var property in mergeObj.Properties())
            {
                result[property.Name] = property.Value;
            }
            return result;
        }

        /// <summary>
        /// Сериализует объект в компактный JSON (без пробелов и лишних символов).
        /// </summary>
        public static string SerializeCompact(object obj)
        {
            if (obj == null)
                return "null";

            return JsonConvert.SerializeObject(obj, Formatting.None, _settings);
        }

        /// <summary>
        /// Получает значение из JObject по пути, с приведением к типу.
        /// </summary>
        public static T GetValue<T>(JObject obj, string path, T defaultValue = default)
        {
            if (obj == null)
                return defaultValue;

            var token = obj.SelectToken(path);
            if (token == null)
                return defaultValue;

            try
            {
                return token.Value<T>();
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}