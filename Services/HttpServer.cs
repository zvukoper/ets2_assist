using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;

namespace ETS2_Assist_GUI.Services
{
    /// <summary>
    /// HTTP-сервер для управления триггер-файлами и списком треков.
    /// Порт: 8083.
    /// Эндпоинты:
    /// - GET /check_trigger?file=... – проверка существования триггер-файла
    /// - GET /delete_trigger?file=... – удаление триггер-файла
    /// - GET /list_tracks – список JSON-файлов в папке saved_tracks
    /// - GET /get_track/{filename} – содержимое файла трека
    /// </summary>
    public class HttpServer
    {
        private readonly Logger _logger;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isRunning = false;
        private readonly int _port = 8083;
        private readonly string _baseDirectory;

        public HttpServer(Logger logger, int port = 8083)
        {
            _logger = logger;
            _port = port;
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
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

                _logger.Log($"HTTP-сервер управления запущен на порту {_port}");

                Task.Run(() => ListenLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка запуска HTTP-сервера: {ex.Message}");
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
                _logger.Log("HTTP-сервер управления остановлен.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка остановки HTTP-сервера: {ex.Message}");
            }
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995) // Операция отменена
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        _logger.Log($"Ошибка в цикле HTTP-сервера: {ex.Message}");
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Добавляем CORS-заголовки
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.OutputStream.Close();
                    return;
                }

                var path = request.Url.AbsolutePath;
                var query = request.QueryString;

                if (path == "/check_trigger")
                {
                    HandleCheckTrigger(query, response);
                }
                else if (path == "/delete_trigger")
                {
                    HandleDeleteTrigger(query, response);
                }
                else if (path == "/list_tracks")
                {
                    HandleListTracks(response);
                }
                else if (path.StartsWith("/get_track/"))
                {
                    var filename = path.Substring("/get_track/".Length);
                    HandleGetTrack(filename, response);
                }
                else
                {
                    response.StatusCode = 404;
                    response.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка обработки HTTP-запроса: {ex.Message}");
                try { context.Response.StatusCode = 500; context.Response.OutputStream.Close(); } catch { }
            }
        }

        private void HandleCheckTrigger(NameValueCollection query, HttpListenerResponse response)
        {
            var file = query["file"] ?? "save_trail.trigger";
            var triggerPath = Path.Combine(_baseDirectory, "data", file);
            bool exists = File.Exists(triggerPath);

            var json = JsonConvert.SerializeObject(new { exists = exists });
            SendJsonResponse(response, json);
        }

        private void HandleDeleteTrigger(NameValueCollection query, HttpListenerResponse response)
        {
            var file = query["file"] ?? "save_trail.trigger";
            var triggerPath = Path.Combine(_baseDirectory, "data", file);
            if (File.Exists(triggerPath))
                File.Delete(triggerPath);

            var json = JsonConvert.SerializeObject(new { success = true });
            SendJsonResponse(response, json);
        }

        private void HandleListTracks(HttpListenerResponse response)
        {
            var tracksDir = Path.Combine(_baseDirectory, "data", "saved_tracks");
            if (!Directory.Exists(tracksDir))
                Directory.CreateDirectory(tracksDir);

            var files = Directory.GetFiles(tracksDir, "*.json")
                .Select(f => Path.GetFileName(f))
                .ToList();

            var json = JsonConvert.SerializeObject(new { files = files });
            SendJsonResponse(response, json);
        }

        private void HandleGetTrack(string filename, HttpListenerResponse response)
        {
            var tracksDir = Path.Combine(_baseDirectory, "data", "saved_tracks");
            var filePath = Path.Combine(tracksDir, filename);

            if (!File.Exists(filePath))
            {
                response.StatusCode = 404;
                response.OutputStream.Close();
                return;
            }

            var content = File.ReadAllText(filePath);
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private void SendJsonResponse(HttpListenerResponse response, string json)
        {
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        public bool IsRunning => _isRunning;
    }
}