using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Управление ETS2 через проверенный ets2_assist_input.dll.
    /// Плагин поднимает Named Pipe: ETS2AssistPause.
    /// Команды: PING, PAUSE.
    /// </summary>
    public static class SCSController
    {
        private const string PipeName = "ETS2AssistPause";
        private const int ConnectTimeoutMs = 1500;
        private const int ReadTimeoutMs = 1500;

        private static bool _initialized;

        public static event Action<string>? OnLog;

        private static void Log(string message) => OnLog?.Invoke(message);

        public static bool Initialize()
        {
            // Инициализация лёгкая: сам канал создаётся плагином в ETS2.
            // Проверяем его через PING только при наличии запущенной игры.
            try
            {
                bool ok = SendCommand("PING", out string response);
                _initialized = ok;

                if (ok)
                    Log($"[SCS] ets2_assist_input.dll connected. PING -> {response}");
                else
                    Log("[SCS] ets2_assist_input.dll / Named Pipe ETS2AssistPause is not available.");

                return ok;
            }
            catch (Exception ex)
            {
                _initialized = false;
                Log($"[SCS] Initialize error: {ex.Message}");
                return false;
            }
        }

        public static bool SetPause(bool enabled)
        {
            // Проверенный плагин принимает команду PAUSE. Она переключает состояние паузы.
            string command = "PAUSE";

            try
            {
                if (!SendCommand(command, out string response, requireResponse: false))
                {
                    _initialized = false;
                    Log($"[SCS] SetPause({enabled}) FAILED: pipe unavailable.");
                    return false;
                }

                _initialized = true;
                Log($"[SCS] SetPause({enabled}) -> команда PAUSE успешно отправлена в Named Pipe.");
                return true;
            }
            catch (Exception ex)
            {
                _initialized = false;
                Log($"[SCS] SetPause({enabled}) EXCEPTION: {ex.Message}");
                return false;
            }
        }

        private static bool SendCommand(string command, out string response, bool requireResponse = true)
        {
            response = string.Empty;

            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                pipe.Connect(ConnectTimeoutMs);
            }
            catch (TimeoutException)
            {
                Log($"[SCS] Pipe '{PipeName}' connect timeout ({ConnectTimeoutMs} ms).");
                return false;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(command);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();

            // В PAUSE-команде важен сам факт отправки команды.
            // Проверенный plugin может изменить состояние игры даже если
            // ответ от pipe не пришёл/не был сформирован вовремя.
            if (!requireResponse)
                return true;

            var buffer = new byte[128];
            using var cts = new System.Threading.CancellationTokenSource(ReadTimeoutMs);

            try
            {
                int n = pipe.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).AsTask().GetAwaiter().GetResult();
                if (n > 0)
                {
                    response = Encoding.ASCII.GetString(buffer, 0, n).Trim();
                    return !string.IsNullOrWhiteSpace(response);
                }
            }
            catch (OperationCanceledException)
            {
                Log($"[SCS] Ответ на '{command}' не получен за {ReadTimeoutMs} ms. Команда уже отправлена.");
            }
            catch (IOException ex)
            {
                Log($"[SCS] Ошибка чтения ответа на '{command}': {ex.Message}");
            }

            return false;
        }

        public static void Dispose()
        {
            _initialized = false;
            Log("[SCS] Controller disposed.");
        }
    }
}
