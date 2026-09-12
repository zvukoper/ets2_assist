using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    // Лёгкий общий фид телеметрии фуры для Map Editor 2 (и потенциально других окон).
    //
    // Источники (как в старом редакторе карты MapEditorForm и в AR-канале):
    //   1) WS-дельта TruckTel: ws://localhost:{port}/api/ws/delta/flat/?throttle=50
    //      — «горячий» поток в движении; на паузе грузовых полей НЕ шлёт;
    //   2) REST-снимок: http://localhost:{port}/api/rest/flat/truck раз в секунду
    //      — работает и на паузе (подтверждено 31.08.2026).
    // Порт берётся из web_data.json (wsPort), иначе 8080.
    //
    // Поля placement (метры карты, БЕЗ делителей — эмпирика 31.08.2026):
    //   [0]=X, [1]=Y, [2]=Z, [3]=heading (доля оборота), [4]=pitch, [5]=roll.
    // Поворот головы: truck.head.offset[3]=yaw, [4]=pitch (доли оборота).
    //
    // Жизненный цикл: Start() при открытии окна-потребителя, Stop() при закрытии —
    // поток не должен висеть постоянно (правило экономии ресурсов проекта).
    internal static class TruckTelemetry
    {
        // Свежесть данных: если последний валидный сэмпл старше — считаем телеметрию
        // отключившейся (потребитель красит маркер серым и замораживает позицию).
        private const int StaleMs = 3000;
        private const int RestIntervalMs = 1000;

        internal readonly struct Snapshot
        {
            public double X { get; init; }
            public double Y { get; init; }
            public double Z { get; init; }
            public double Heading { get; init; }   // доля оборота (0 = север)
            public double HeadYaw { get; init; }   // доля оборота
            public double HeadPitch { get; init; } // доля оборота (полуугол конуса обзора)
            public double SpeedKmh { get; init; }  // скорость из truck.speed (м/с -> км/ч)
            public bool Live { get; init; }        // данные свежее StaleMs
        }

        private static readonly object Sync = new();
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

        private static bool _running;
        private static int _refCount;
        private static int _port = 8080;
        private static ClientWebSocket? _ws;
        private static CancellationTokenSource? _cts;
        private static Task? _restTask;

        // Последний ВАЛИДНЫЙ сэмпл. При пропадании телеметрии НЕ обнуляется —
        // потребитель обязан сохранить маркер на последней позиции (заморозка).
        private static double _x, _y, _z, _heading, _headYaw, _headPitch;
        private static double _speedKmh;
        private static bool _haveSample;
        private static DateTime _lastSeen = DateTime.MinValue;
        // Счётчик применённых сэмплов: потребитель шлёт снимок в JS только при изменении.
        private static long _revision;

        // ---- ДИАГНОСТИКА (v1.0.40.22): без неё было не понять, приходят ли данные ----
        private static long _framesWs, _framesRest, _framesNoPlacement, _framesBad, _applied;
        private static DateTime _lastDiag = DateTime.MinValue;
        private static DateTime _lastData = DateTime.MinValue;

        private static void Diag(string msg) => Logger.Current?.Data("[TRUCK] " + msg);

        // Периодический отчёт о приёме: раз в 15 с — видно, идёт ли поток, есть ли placement,
        // каков возраст последних данных. Появляется ТОЛЬКО при изменении (без спама).

        internal static bool IsRunning { get { lock (Sync) return _running; } }

        // Текущий порт TruckTel (из web_data.json, иначе 8080) — для подписи индикатора.
        internal static int Port { get { lock (Sync) return _port; } }

        // Жива ли телеметрия прямо сейчас (быстрый доступ без выдачи снимка).
        internal static bool IsLive
        {
            get
            {
                lock (Sync) return _haveSample && (DateTime.Now - _lastSeen).TotalMilliseconds <= StaleMs;
            }
        }

        // Скорость в км/ч из последнего валидного сэмпла (0, если данных ещё не было).
        internal static double SpeedKmh { get { lock (Sync) return _speedKmh; } }

        // Запускает фид. Счётчик ссылок: фид держится, пока хотя бы один потребитель
        // (MainForm для индикатора, MapEditor2Form для карты) не вызвал Stop().
        internal static void Start()
        {
            lock (Sync)
            {
                _refCount++;
                if (_running) return;
                _running = true;
                _cts = new CancellationTokenSource();
            }
            try
            {
                _ = ConnectWsAsync(_cts!.Token);
                _restTask = RestLoopAsync(_cts.Token);
                Logger.Current?.Workflow("[TRUCK] Фид телеметрии запущен (WS + REST-снимок).");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[TRUCK] Ошибка запуска фида: " + ex.Message);
            }
        }

        // Уменьшает счётчик ссылок; при нуле останавливает фид и освобождает соединения.
        internal static void Stop()
        {
            CancellationTokenSource? cts;
            ClientWebSocket? ws;
            lock (Sync)
            {
                if (_refCount > 0) _refCount--;
                if (_refCount > 0 || !_running) return;
                _running = false;
                cts = _cts;
                ws = _ws;
                _cts = null;
                _ws = null;
            }
            try { cts?.Cancel(); } catch { }
            try { ws?.Abort(); ws?.Dispose(); } catch { }
            try { cts?.Dispose(); } catch { }
            Logger.Current?.Workflow("[TRUCK] Фид телеметрии остановлен.");
        }

        // Возвращает последний снимок. haveSample=false, если валидных данных ещё не было.
        // revision меняется при каждом новом валидном сэмпле — удобно для «шлём при изменении».
        internal static bool TryGetSnapshot(out Snapshot snapshot, out long revision, out bool haveSample)
        {
            lock (Sync)
            {
                snapshot = new Snapshot
                {
                    X = _x,
                    Y = _y,
                    Z = _z,
                    Heading = _heading,
                    HeadYaw = _headYaw,
                    HeadPitch = _headPitch,
                    SpeedKmh = _speedKmh,
                    Live = _haveSample && (DateTime.Now - _lastSeen).TotalMilliseconds <= StaleMs
                };
                revision = _revision;
                haveSample = _haveSample;
                return _haveSample;
            }
        }

        private static int ResolvePort()
        {
            try
            {
                if (File.Exists(AppDataPaths.WebDataFile))
                {
                    var json = JObject.Parse(File.ReadAllText(AppDataPaths.WebDataFile));
                    int p = json["wsPort"]?.Value<int>() ?? 0;
                    if (p > 0) return p;
                }
            }
            catch { }
            return 8080;
        }

        private static async Task ConnectWsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int port = ResolvePort();
                lock (Sync) _port = port;
                try
                {
                    var ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    await ws.ConnectAsync(new Uri($"ws://localhost:{port}/api/ws/delta/flat/?throttle=50"), token);
                    lock (Sync) _ws = ws;
                    Logger.Current?.Data($"[TRUCK] WS подключён: ws://localhost:{port}/api/ws/delta/flat/");
                    await ReceiveLoopAsync(ws, token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Logger.Current?.Data($"[TRUCK] WS недоступен ({port}): {ex.Message} (REST-снимок продолжит работу).");
                }
                // Переподключение через 2.5 с, пока фид не остановлен.
                try { await Task.Delay(2500, token); } catch { return; }
            }
        }

        private static async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            var buf = new byte[32768];
            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token);
                        if (res.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    if (res.MessageType == WebSocketMessageType.Close) break;

                    try
                    {
                        var json = JObject.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                        lock (Sync) _framesWs++;
                        Apply(json);
                    }
                    catch (Exception ex)
                    {
                        lock (Sync) _framesBad++;
                        Diag("битый кадр WS: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* сеть закрылась — переподключение в ConnectWsAsync */ }
            finally
            {
                try { ws.Dispose(); } catch { }
                lock (Sync) { if (_ws == ws) _ws = null; }
            }
        }

        private static async Task RestLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int port;
                    lock (Sync) port = _port;
                    var resp = await Http.GetAsync($"http://localhost:{port}/api/rest/flat/truck", token);
                    resp.EnsureSuccessStatusCode();
                    var text = await resp.Content.ReadAsStringAsync(token);
                    lock (Sync) _framesRest++;
                    Apply(JObject.Parse(text));
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // REST молчал ранее — теперь любая ошибка видна в app_data (1 строка/15 с).
                    DiagOnce("REST err (порт может быть занят/недоступен): " + ex.GetBaseException().Message);
                }
                try { await Task.Delay(RestIntervalMs, token); } catch { return; }
            }
        }

        // Единый парсер кадра (WS-дельта или REST-снимок). Значения читаем через
        // Value<double>() — НЕ через ToString(): в ru-RU десятичная запятая ломает
        // Invariant TryParse и координаты уезжают на порядки (урок v64).
        private static void Apply(JObject json)
        {
            // Скорость обновляем и без placement (поле может прийти отдельным кадром).
            var spd = json["truck.speed"];
            if (spd != null && spd.Type != JTokenType.Null)
            {
                double sv = spd.Value<double>();
                if (double.IsFinite(sv)) { lock (Sync) _speedKmh = Math.Abs(sv) * 3.6; }
            }

            var placement = json["truck.world.placement"] as JArray
                            ?? json.SelectToken("truck.world.placement") as JArray;

            // ВАЖНО (эмпирика 12.09.2026): TruckTel отдаёт truck.world.placement ТОЛЬКО
            // когда окно игры активно и симуляция не на паузе. На паузе placement в потоке
            // отсутствует — это НОРМА, а не ошибка: последний валидный сэмпл сохраняем
            // (заморозка маркера) и просто считаем такие кадры отдельным счётчиком.
            if (placement == null || placement.Count < 3)
            {
                lock (Sync) _framesNoPlacement++;
                ReportDiag(hasPlacement: false);
                return;
            }

            double tx = placement[0].Value<double>();
            double tz = placement[2].Value<double>();
            if (double.IsNaN(tx) || double.IsInfinity(tx) || double.IsInfinity(tz))
            {
                lock (Sync) _framesBad++;
                return;
            }

            double ty = 0, th = 0;
            if (placement.Count >= 2 && placement[1] != null) ty = placement[1].Value<double>();
            if (placement.Count >= 4 && placement[3] != null) th = placement[3].Value<double>();

            // Поворот головы (yaw/pitch) — из truck.head.offset, не из placement.
            // Питч КУЗОВА (placement[4]) для конуса обзора не используем (как в старом редакторе).
            double hy = 0, hpi = 0;
            var head = json["truck.head.offset"] as JArray
                       ?? json.SelectToken("truck.head.offset") as JArray;
            if (head != null && head.Count >= 4 && head[3] != null) hy = head[3].Value<double>();
            if (head != null && head.Count >= 5 && head[4] != null) hpi = head[4].Value<double>();

            lock (Sync)
            {
                // Первый сэмпл задаёт позицию/угол; далее обновляем как есть — сглаживание
                // не нужно, у потребителя маркер рисуется по последнему значению.
                double prevH = _heading;
                double diff = th - prevH;
                if (diff > 0.5) diff -= 1.0;
                if (diff < -0.5) diff += 1.0;
                double applied = _haveSample ? prevH + diff : th;
                applied -= Math.Floor(applied);

                bool first = !_haveSample;
                _x = tx;
                _y = ty;
                _z = tz;
                _heading = applied;
                _headYaw = hy;
                _headPitch = hpi;
                _haveSample = true;
                _lastSeen = DateTime.Now;
                _lastData = DateTime.Now;
                _revision++;
                _applied++;
                if (first) Diag($"ПЕРВЫЙ placement: x={tx:F1} y={ty:F1} z={tz:F1} h={th:F4} (включены метка и конус)");
            }
            ReportDiag(hasPlacement: true);
        }

        // Отчёт по приёму — не чаще 1 раза в 15 с и только при смене картины (без спама).
        private static void ReportDiag(bool hasPlacement)
        {
            long ws, rest, noPl, bad, applied;
            DateTime lastData, now = DateTime.Now;
            lock (Sync)
            {
                if ((now - _lastDiag).TotalSeconds < 15) return;
                _lastDiag = now;
                ws = _framesWs; rest = _framesRest; noPl = _framesNoPlacement; bad = _framesBad; applied = _applied;
                lastData = _lastData;
            }
            double ageS = lastData == DateTime.MinValue ? -1 : (now - lastData).TotalSeconds;
            Diag($"приём: WS={ws} REST={rest} без-placement={noPl} битых={bad} применено={applied} " +
                 $"live={IsLive} возраст_данных={(ageS < 0 ? "нет" : ageS.ToString("F1") + "с")}" +
                 (hasPlacement ? "" : " | placement нет (пауза/окно игры неактивно — это норма)"));
        }

        // Одна строка диагностики в 15 с (для частых ошибок REST/WS).
        private static void DiagOnce(string msg)
        {
            DateTime now = DateTime.Now;
            lock (Sync)
            {
                if ((now - _lastDiag).TotalSeconds < 15) return;
                _lastDiag = now;
            }
            Diag(msg);
        }
    }
}
