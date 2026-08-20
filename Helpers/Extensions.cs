using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ETS2_Assist_GUI.Helpers
{
    /// <summary>
    /// Класс с методами-расширениями для упрощения работы с типами данных.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Безопасное преобразование строки в double с инвариантной культурой.
        /// </summary>
        public static double ToDoubleInvariant(this string value, double defaultValue = 0)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Безопасное преобразование строки в int.
        /// </summary>
        public static int ToInt(this string value, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            if (int.TryParse(value, out var result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Форматирует расстояние в метрах в читаемый вид (м или км).
        /// </summary>
        public static string ToDistanceString(this double meters)
        {
            if (meters < 1000)
                return $"{Math.Round(meters)} м";
            return $"{Math.Round(meters / 1000)} км";
        }

        /// <summary>
        /// Форматирует время в секундах в формат ЧЧ:ММ:СС.
        /// </summary>
        public static string ToTimeString(this double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// Получает подстроку между двумя маркерами.
        /// </summary>
        public static string SubstringBetween(this string source, string start, string end)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;

            int startIndex = source.IndexOf(start);
            if (startIndex == -1)
                return string.Empty;

            startIndex += start.Length;
            int endIndex = source.IndexOf(end, startIndex);
            if (endIndex == -1)
                return string.Empty;

            return source.Substring(startIndex, endIndex - startIndex);
        }

        /// <summary>
        /// Обрезает строку до указанной длины и добавляет многоточие.
        /// </summary>
        public static string Truncate(this string value, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength - suffix.Length) + suffix;
        }

        /// <summary>
        /// Проверяет, является ли значение в допустимом диапазоне.
        /// </summary>
        public static double Clamp(this double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Проверяет, является ли значение в допустимом диапазоне.
        /// </summary>
        public static int Clamp(this int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Объединяет коллекцию строк с разделителем, пропуская пустые.
        /// </summary>
        public static string JoinNonEmpty(this IEnumerable<string> values, string separator)
        {
            return string.Join(separator, values.Where(v => !string.IsNullOrEmpty(v)));
        }

        /// <summary>
        /// Форматирует значение с фиксированным количеством десятичных знаков.
        /// </summary>
        public static string ToFixed(this double value, int decimals = 2)
        {
            return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Парсит строку с разделителями в массив double.
        /// </summary>
        public static double[] ParseDoubles(this string value, char separator)
        {
            if (string.IsNullOrEmpty(value))
                return Array.Empty<double>();

            var parts = value.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<double>();
            foreach (var part in parts)
            {
                if (part.ToDoubleInvariant() is double d)
                    result.Add(d);
            }
            return result.ToArray();
        }
    }
}