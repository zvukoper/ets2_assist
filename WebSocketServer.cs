using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ETS2_Assist_GUI
{
    public class TrailBehavior : WebSocketBehavior
    {
        private static Action<string>? _log;
        private static Action<JObject>? _onTrail;

        public static void SetLog(Action<string> log) => _log = log;
        public static void SetOnTrail(Action<JObject> action) => _onTrail = action;

        protected override void OnOpen()
        {
            _log?.Invoke("[WebSocket] Клиент карты подключился");
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                var json = e.Data;
                _log?.Invoke($"[WebSocket] Получен трек ({json.Length} байт)");
                var data = JObject.Parse(json);
                _onTrail?.Invoke(data);
                Send(JsonConvert.SerializeObject(new { status = "ok", message = "Трек получен" }));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[WebSocket] Ошибка обработки: {ex.Message}");
                Send(JsonConvert.SerializeObject(new { status = "error", message = ex.Message }));
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            _log?.Invoke("[WebSocket] Клиент карты отключился");
        }

        protected override void OnError(ErrorEventArgs e)
        {
            _log?.Invoke($"[WebSocket] Ошибка: {e.Message}");
        }
    }

    public class WebSocketServer
    {
        private WebSocketSharp.Server.WebSocketServer? _server;
        private readonly int _port;
        public Action<string>? OnLog { get; set; }
        public Action<JObject>? OnTrailReceived { get; set; }
        public bool IsRunning => _server?.IsListening ?? false;

        public WebSocketServer(int port = 8084)
        {
            _port = port;
        }

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                TrailBehavior.SetLog(msg => OnLog?.Invoke(msg));
                TrailBehavior.SetOnTrail(data => OnTrailReceived?.Invoke(data));

                _server = new WebSocketSharp.Server.WebSocketServer($"ws://localhost:{_port}");
                _server.AddWebSocketService<TrailBehavior>("/");
                _server.Start();

                OnLog?.Invoke($"[WebSocket] Сервер запущен на порту {_port}");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[WebSocket] Ошибка запуска: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            try
            {
                _server?.Stop();
                _server = null;
                OnLog?.Invoke("[WebSocket] Сервер остановлен");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[WebSocket] Ошибка остановки: {ex.Message}");
            }
        }
    }
}