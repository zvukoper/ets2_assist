using System;
using System.Collections.Generic;
using System.Linq;
using ETS2_Assist_GUI.Models;
using ETS2_Assist_GUI.Services;

namespace ETS2_Assist_GUI.Recording
{
    /// <summary>
    /// Отвечает за сбор и буферизацию данных телеметрии во время записи трека.
    /// </summary>
    public class TrailRecorder
    {
        private readonly Logger _logger;
        private bool _isRecording = false;
        private DateTime _startTime;
        private double _elapsedSeconds = 0;

        // Буферы
        private List<TrailFrame> _frames = new();
        private List<TrailFrame> _dataFrames = new();
        private TrailFrame _lastFrame = null;
        private double _lastDataDistance = 0;

        // Настройки
        private double _trailInterval = 3;
        private double _dataInterval = 25;

        // Последняя позиция для расчёта дистанции
        private double _lastX = 0;
        private double _lastZ = 0;

        public event Action<TrailData> OnTrailUpdated;

        public TrailRecorder(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Начинает запись.
        /// </summary>
        public void Start()
        {
            if (_isRecording) return;

            _startTime = DateTime.UtcNow;
            _elapsedSeconds = 0;
            _frames.Clear();
            _dataFrames.Clear();
            _lastFrame = null;
            _lastDataDistance = 0;
            _isRecording = true;

            _logger.Log("Запись трека начата.");
        }

        /// <summary>
        /// Останавливает запись и возвращает собранные данные.
        /// </summary>
        public TrailData Stop()
        {
            if (!_isRecording) return null;

            _isRecording = false;
            _logger.Log("Запись трека остановлена.");

            // Формируем итоговый объект
            var trailData = new TrailData();
            var meta = new TrailMetadata
            {
                Name = GenerateDefaultName(),
                Description = GenerateDefaultDescription(),
                StartTime = _startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                DurationMs = (long)(_elapsedSeconds * 1000),
                TrailInterval = _trailInterval,
                DataInterval = _dataInterval,
                MinSpeed = 0,
                MaxSpeed = 115,
                TotalDistance = CalculateTotalDistance(),
                FrameCount = _frames.Count
            };

            // Формируем словарь типов событий
            meta.EventTypes = new Dictionary<int, string>
            {
                { 0, "none" },
                { 1, "stop" },
                { 2, "service" },
                { 3, "parking" },
                { 4, "damage" },
                { 5, "user_marker" }
            };

            trailData.Meta = meta;

            // Конвертируем буферы в компактные строки
            var allFrames = _frames.Concat(_dataFrames)
                .OrderBy(f => f.Time)
                .ToList();

            trailData.Data = allFrames.Select(frame => FrameToCompactString(frame)).ToList();

            OnTrailUpdated?.Invoke(trailData);
            return trailData;
        }

        /// <summary>
        /// Обновляет данные от телеметрии.
        /// </summary>
        public void Update(TelemetryData data)
        {
            if (!_isRecording) return;

            var currentTime = (DateTime.UtcNow - _startTime).TotalSeconds;
            _elapsedSeconds = currentTime;

            var position = data.Position;
            var heading = data.Heading;
            var speed = data.Speed;
            var fuel = data.Fuel;
            var damage = data.Damage;

            // Расчёт дистанции с последней точки
            double dist = 0;
            if (_lastFrame != null)
            {
                dist = Math.Sqrt(Math.Pow(position.X - _lastFrame.X, 2) +
                                 Math.Pow(position.Z - _lastFrame.Z, 2));
            }

            // Добавляем точку шлейфа, если прошло достаточно расстояния
            if (_lastFrame == null || dist >= _trailInterval)
            {
                var frame = new TrailFrame
                {
                    Time = (long)(currentTime * 1000),
                    X = position.X,
                    Z = position.Z,
                    Heading = heading,
                    Speed = speed,
                    EventType = 0,
                    Fuel = fuel,
                    Damage = damage
                };

                _frames.Add(frame);
                _lastFrame = frame;

                // Обновляем позицию для расчёта дистанции
                _lastX = position.X;
                _lastZ = position.Z;
            }

            // Добавляем кадр данных (топливо, урон), если прошло DATA_INTERVAL
            _lastDataDistance += dist;
            if (_lastDataDistance >= _dataInterval || _frames.Count == 1)
            {
                var dataFrame = new TrailFrame
                {
                    Time = (long)(currentTime * 1000),
                    X = position.X,
                    Z = position.Z,
                    Heading = heading,
                    Speed = speed,
                    EventType = 0,
                    Fuel = fuel,
                    Damage = damage
                };

                _dataFrames.Add(dataFrame);
                _lastDataDistance = 0;
            }
        }

        /// <summary>
        /// Добавляет событие в трек.
        /// </summary>
        public void AddEvent(double x, double z, int eventType, string label, string color, string subtext = "")
        {
            if (!_isRecording) return;

            var currentTime = (DateTime.UtcNow - _startTime).TotalSeconds;
            var frame = new TrailFrame
            {
                Time = (long)(currentTime * 1000),
                X = x,
                Z = z,
                Heading = 0,
                Speed = 0,
                EventType = eventType,
                Label = label,
                Color = color,
                Subtext = subtext
            };

            _frames.Add(frame);
        }

        /// <summary>
        /// Генерирует название трека по умолчанию.
        /// </summary>
        private string GenerateDefaultName()
        {
            // В реальности здесь нужно получить ближайший город, но пока просто дата
            return $"Запись {DateTime.Now:dd.MM.yy HH:mm}";
        }

        /// <summary>
        /// Генерирует описание по умолчанию.
        /// </summary>
        private string GenerateDefaultDescription()
        {
            return "";
        }

        /// <summary>
        /// Рассчитывает общее расстояние по точкам шлейфа.
        /// </summary>
        private double CalculateTotalDistance()
        {
            double total = 0;
            for (int i = 1; i < _frames.Count; i++)
            {
                var dx = _frames[i].X - _frames[i - 1].X;
                var dz = _frames[i].Z - _frames[i - 1].Z;
                total += Math.Sqrt(dx * dx + dz * dz);
            }
            return total;
        }

        /// <summary>
        /// Преобразует кадр в компактную строку.
        /// Формат: time;x;z;heading;speed;eventType;label;color;subtext;fuel;damage
        /// </summary>
        private string FrameToCompactString(TrailFrame frame)
        {
            var parts = new List<string>
            {
                frame.Time.ToString(),
                frame.X.ToString("F2"),
                frame.Z.ToString("F2"),
                frame.Heading.ToString("F4"),
                frame.Speed.ToString("F2"),
                frame.EventType.ToString()
            };

            // Добавляем опциональные поля
            parts.Add(string.IsNullOrEmpty(frame.Label) ? "" : frame.Label);
            parts.Add(string.IsNullOrEmpty(frame.Color) ? "" : frame.Color);
            parts.Add(string.IsNullOrEmpty(frame.Subtext) ? "" : frame.Subtext);
            parts.Add(frame.Fuel > 0 ? frame.Fuel.ToString("F1") : "");
            parts.Add(frame.Damage > 0 ? frame.Damage.ToString("F1") : "");

            return string.Join(";", parts);
        }

        public bool IsRecording => _isRecording;
        public int FrameCount => _frames.Count;
    }
}