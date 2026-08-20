using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI.Services
{
    /// <summary>
    /// WebSocket-сервер для управления записью и воспроизведением треков.
    /// Порт: 8084.
    /// Принимает команды от веб-страницы:
    /// - Сохранение трека (JSON)
    /// - Запрос на звук (play_sound)
    /// - Запрос на добавление заметки (add_marker)
    /// </summary>
    public class WebSocketServer
    {
        private readonly Logger _logger;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;
        private readonly int _port = 8084;

        // События для внешних подписчиков
        public event Action<JObject> OnTrailReceived;
        public event Action<string> OnSoundRequest;
        public event Action<MarkerData> OnMarkerRequest;

        public WebSocketServer(Logger logger, int port = 8084)
        {
            _logger = logger;
            _port = port;
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
                _cts = new CancellationTokenSource();
                _isRunning = true;

                _logger.Log($"WebSocket-сервер управления запущен на порту {_port}");

                Task.Run(() => ListenLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка запуска WebSocket-сервера: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _cts.Cancel();
                _listener.Stop();
                _listener.Close();
                _isRunning = false;
                _logger.Log("WebSocket-сервер управления остановлен.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка остановки WebSocket-сервера: {ex.Message}");
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        // Обрабатываем WebSocket-запрос
                        var wsContext = await context.AcceptWebSocketRequestAsync(null);
                        _ = HandleConnection(wsContext.WebSocket, token);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Операция отменена
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        _logger.Log($"Ошибка в цикле WebSocket-сервера: {ex.Message}");
                }
            }
        }

        private async Task HandleConnection(WebSocket ws, CancellationToken token)
        {
            var buffer = new byte[4096];
            _logger.Log("Клиент WebSocket подключился.");

            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                        _logger.Log("Клиент WebSocket отключился.");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessMessage(message, ws);
                    }
                }
                catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Ошибка обработки сообщения WebSocket: {ex.Message}");
                }
            }

            try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }

        private void ProcessMessage(string message, WebSocket ws)
        {
            try
            {
                var json = JObject.Parse(message);

                // Проверяем команду
                var command = json["command"]?.Value<string>();

                if (command == "play_sound")
                {
                    var soundType = json["type"]?.Value<string>() ?? "beep";
                    OnSoundRequest?.Invoke(soundType);
                    return;
                }

                if (command == "add_marker")
                {
                    var marker = new MarkerData
                    {
                        Name = json["name"]?.Value<string>() ?? "",
                        Description = json["desc"]?.Value<string>() ?? "",
                        X = json["x"]?.Value<double>() ?? 0,
                        Z = json["z"]?.Value<double>() ?? 0
                    };
                    OnMarkerRequest?.Invoke(marker);
                    return;
                }

                // Если не команда, считаем это треком
                OnTrailReceived?.Invoke(json);
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка обработки сообщения WebSocket: {ex.Message}");
            }
        }

        public bool IsRunning => _isRunning;
    }

    /// <summary>
    /// Данные маркера (заметки), отправляемые от карты.
    /// </summary>
    public class MarkerData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public double X { get; set; }
        public double Z { get; set; }
    }
}