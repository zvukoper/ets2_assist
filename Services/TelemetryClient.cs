using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ETS2_Assist_GUI.Models;

namespace ETS2_Assist_GUI.Services
{
    /// <summary>
    /// Клиент для подключения к WebSocket-серверу телеметрии TruckTel (порт 8080).
    /// Получает данные о положении, скорости, топливе, износе и других параметрах.
    /// </summary>
    public class TelemetryClient
    {
        private readonly Logger _logger;
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private bool _isConnected = false;
        private readonly string _url = "ws://localhost:8080/api/ws/delta/flat/?throttle=50";

        public event Action<TelemetryData> OnTelemetryUpdate;

        public TelemetryClient(Logger logger, string url = null)
        {
            _logger = logger;
            if (!string.IsNullOrEmpty(url))
                _url = url;
        }

        public async Task ConnectAsync()
        {
            if (_isConnected) return;

            try
            {
                _ws = new ClientWebSocket();
                _cts = new CancellationTokenSource();
                await _ws.ConnectAsync(new Uri(_url), _cts.Token);
                _isConnected = true;
                _logger.Log("Подключение к Telemetry WebSocket установлено.");

                // Запускаем приём сообщений
                _ = Task.Run(() => ReceiveLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка подключения к Telemetry WebSocket: {ex.Message}");
                throw;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;

            try
            {
                _cts.Cancel();
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
                _ws.Dispose();
                _isConnected = false;
                _logger.Log("Отключение от Telemetry WebSocket.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка отключения от Telemetry WebSocket: {ex.Message}");
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[4096];

            while (_isConnected && !token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                        _isConnected = false;
                        _logger.Log("Telemetry WebSocket закрыт сервером.");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var data = ParseTelemetry(json);
                        if (data != null)
                            OnTelemetryUpdate?.Invoke(data);
                    }
                }
                catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        _logger.Log($"Ошибка приёма телеметрии: {ex.Message}");
                }
            }

            _isConnected = false;
        }

        private TelemetryData ParseTelemetry(string json)
        {
            try
            {
                var obj = JObject.Parse(json);

                var placement = obj["truck"]?["placement"];
                if (placement == null) return null;

                var position = new Position
                {
                    X = placement["x"]?.Value<double>() ?? 0,
                    Y = placement["y"]?.Value<double>() ?? 0,
                    Z = placement["z"]?.Value<double>() ?? 0
                };

                var heading = placement["heading"]?.Value<double>() ?? 0;
                var speed = obj["truck"]?["speed"]?.Value<double>() ?? 0;
                var fuel = obj["truck"]?["fuel"]?.Value<double>() ?? 0;

                // Суммарный износ
                var wear = obj["truck"]?["wear"] ?? new JObject();
                var totalWear = (wear["engine"]?.Value<double>() ?? 0) +
                                (wear["transmission"]?.Value<double>() ?? 0) +
                                (wear["cabin"]?.Value<double>() ?? 0) +
                                (wear["chassis"]?.Value<double>() ?? 0) +
                                (wear["wheels"]?.Value<double>() ?? 0);

                var engineOn = obj["truck"]?["engineOn"]?.Value<bool>() ?? false;
                var gameTime = obj["game"]?["time"]?.Value<string>() ?? "";

                var job = obj["job"];
                var destinationCity = job?["destinationCity"]?.Value<string>() ?? "";
                var estimatedDistance = job?["estimatedDistance"]?.Value<double>() ?? 0;

                return new TelemetryData
                {
                    Position = position,
                    Heading = heading,
                    Speed = speed,
                    Fuel = fuel,
                    Damage = totalWear,
                    EngineOn = engineOn,
                    GameTime = gameTime,
                    DestinationCity = destinationCity,
                    EstimatedDistance = estimatedDistance
                };
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка парсинга телеметрии: {ex.Message}");
                return null;
            }
        }

        public bool IsConnected => _isConnected;
    }

    /// <summary>
    /// Структура данных телеметрии.
    /// </summary>
    public class TelemetryData
    {
        public Position Position { get; set; }
        public double Heading { get; set; }
        public double Speed { get; set; }
        public double Fuel { get; set; }
        public double Damage { get; set; }
        public bool EngineOn { get; set; }
        public string GameTime { get; set; }
        public string DestinationCity { get; set; }
        public double EstimatedDistance { get; set; }
    }

    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}