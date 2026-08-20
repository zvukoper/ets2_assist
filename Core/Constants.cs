 
namespace ETS2_Assist_GUI.Core
{
    /// <summary>
    /// Глобальные константы приложения.
    /// Все настраиваемые параметры вынесены сюда для централизованного изменения.
    /// </summary>
    public static class Constants
    {
        // ===== СЕТЕВЫЕ ПОРТЫ =====
        public const int TelemetryPort = 8080;       // Порт WebSocket-сервера телеметрии (TruckTel)
        public const int HttpStaticPort = 8082;      // Порт HTTP-сервера статики (Python)
        public const int HttpControlPort = 8083;     // Порт HTTP-сервера управления (триггер, список треков)
        public const int WebSocketControlPort = 8084; // Порт WebSocket-сервера управления (сохранение треков)

        // ===== ПУТИ =====
        public const string DataFolder = "data";
        public const string SavedTracksFolder = "saved_tracks";
        public const string LogsFolder = "Logs";
        public const string SoundsFolder = "sounds";
        public const string ConfigFile = "config.json";
        public const string TriggerFile = "save_trail.trigger";

        // ===== НАСТРОЙКИ ПО УМОЛЧАНИЮ =====
        public const int DefaultTrailInterval = 3;      // метров между точками шлейфа
        public const int DefaultDataInterval = 25;       // метров между обновлением данных (топливо, урон)
        public const int DefaultNearbyCitiesCount = 4;   // количество ближайших городов
        public const double DefaultMinSpeed = 0;         // минимальная скорость для цветовой шкалы
        public const double DefaultMaxSpeed = 115;       // максимальная скорость для цветовой шкалы

        // ===== ЛИМИТЫ =====
        public const int MaxTrailFileSizeMB = 10;        // максимальный размер файла трека в МБ
        public const int DefaultRecordingDurationMinutes = 60; // длительность записи по умолчанию (мин)

        // ===== ПОЛЯ ДЛЯ КОМПАКТНОГО ФОРМАТА =====
        public const char DataSeparator = ';';           // разделитель полей в компактном формате
        public const int DefaultDecimalPlaces = 2;       // количество знаков после запятой для координат/скорости

        // ===== URL-АДРЕСА =====
        public const string TelemetryWebSocketUrl = "ws://localhost:8080/api/ws/delta/flat/?throttle=50";
        public const string SaveWebSocketUrl = "ws://localhost:8084/";
        public const string TriggerCheckUrl = "http://localhost:8083/check_trigger";
        public const string TriggerDeleteUrl = "http://localhost:8083/delete_trigger";
        public const string TrackListUrl = "http://localhost:8083/list_tracks";
        public const string TrackGetUrl = "http://localhost:8083/get_track/";
        public const string StaticBaseUrl = "http://localhost:8082/";
        public const string PlayerBaseUrl = "http://localhost:8082/saved_tracks/";
    }
}