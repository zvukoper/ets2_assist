using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    // ВАЖНОЕ ТРЕБОВАНИЕ ПОЛЬЗОВАТЕЛЯ (30.08.2026, v60): веб-страница AR НЕ Загружает
    // список точек карты. Приложение САМО находит БЛИЖАЙШУЮ точку к фуре и просто
    // отправляет её координаты командой ar_target:
    //   { command:"ar_target",
    //     hasTarget:true/false,
    //     gameName:"...", realName:"...", x:.., y:.., z:..,
    //     dist:метры, kind:"target|city|poi" }
    // Подбор выполняется по КОПИИ модели конвейера overrides (статика + overrides +
    // цели из test_targets.json) — та же модель, что в map_overrides_data.
    // Фура берётся из СОБСТВЕННОГО WS-клиента телеметрии (TruckTel, порт из
    // web_data.json) — placement приходит только в WS-дельте (урок v59).
    public partial class MainForm
    {
        // ---- Телеметрия (как у редактора: WS-дельта, порт из web_data.json) ----
        private ClientWebSocket? _arWs;
        private CancellationTokenSource? _arCts;
        private System.Windows.Forms.Timer? _arTimer;
        private System.Windows.Forms.Timer? _arReconnectTimer;
        private double _arTruckX, _arTruckY, _arTruckZ;   // опорная точка фуры (мир)
        private double _arHeading;                        // heading фуры (доля оборота)
        private double _arPitch, _arRoll;                 // тангаж/крен фуры
        private JArray? _arLastHead;                      // truck.head.offset (6 элементов)
        private bool _arTruckKnown;                       // был хотя бы один placement
        private DateTime _arTruckLastSeen = DateTime.MinValue;

        // ---- Кэш модели точек (обновляем разово при изменениях конвейера) ----
        private List<(string gameName, string realName, double x, double y, double z, string kind, bool isTarget)> _arPoints
            = new();
        private DateTime _arModelAt = DateTime.MinValue;

        // Частота подборки/рассылки (Гц). 5 Гц достаточно для перекрестья.
        private const int ArUpdateIntervalMs = 200;

        // ================================================================
        // ЗАПУСК / ОСТАНОВКА (по кнопке «Запустить AR»)
        // ================================================================
        internal void StartArTargetFeed()
        {
            // Статическая модель точек: собираем один раз, обновляем по таймеру 1/с
            // (файл overrides редок меняется, статика вообще не меняется).
            RefreshArModel();
            try { EnsureTestTargetsFile(); } catch { }

            if (_arTimer == null)
            {
                _arTimer = new System.Windows.Forms.Timer { Interval = ArUpdateIntervalMs };
                _arTimer.Tick += (_, _) => ArUpdateTick();
            }
            _arTimer.Start();

            if (_arReconnectTimer == null)
            {
                _arReconnectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _arReconnectTimer.Tick += (_, _) => { _arReconnectTimer!.Stop(); _ = ArConnectTelemetryAsync(); };
            }
            _ = ArConnectTelemetryAsync();
            AppendLog("[AR] Канал AR-целей запущен (телеметрия WS + подбор ближайшей точки на C#).");
        }

        internal void StopArTargetFeed()
        {
            _arTimer?.Stop();
            _arReconnectTimer?.Stop();
            try { _arCts?.Cancel(); } catch { }
            try { _arWs?.Dispose(); } catch { }
            _arWs = null;
            AppendLog("[AR] Канал AR-целей остановлен.");
        }

        // ================================================================
        // ТЕЛЕМЕТРИЯ ФУРЫ (WS-дельта, порт из web_data.json)
        // ================================================================
        private async Task ArConnectTelemetryAsync()
        {
            if (_arWs != null &&
                (_arWs.State == WebSocketState.Open || _arWs.State == WebSocketState.Connecting)) return;
            int port = 8080;
            try
            {
                if (File.Exists(AppDataPaths.WebDataFile))
                {
                    var j = JObject.Parse(File.ReadAllText(AppDataPaths.WebDataFile));
                    port = j["wsPort"]?.Value<int>() ?? 8080;
                }
            }
            catch { }

            var cts = new CancellationTokenSource();
            _arCts = cts;
            try
            {
                var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/api/ws/delta/flat/?throttle=50"), cts.Token);
                _arWs = ws;
                AppendLog($"[AR] Телеметрия подключена: ws://localhost:{port}/api/ws/delta/flat/.");
                _ = ArReceiveLoopAsync(ws, cts.Token);
            }
            catch (Exception ex)
            {
                if (!_arTruckKnown) AppendLog($"[AR] Телеметрия недоступна ({port}): {ex.Message}");
                _arReconnectTimer?.Start();
            }
        }

        private async Task ArReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
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
                        var placement = json["truck.world.placement"] as JArray;
                        if (placement != null && placement.Count >= 3 &&
                            double.TryParse(placement[0].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tx) &&
                            double.TryParse(placement[2].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tz))
                        {
                            _arTruckX = tx;
                            _arTruckZ = tz;
                            if (placement.Count >= 2 && placement[1] != null)
                                double.TryParse(placement[1].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _arTruckY);
                            if (placement.Count >= 4 && placement[3] != null)
                                double.TryParse(placement[3].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _arHeading);
                            if (placement.Count >= 5 && placement[4] != null)
                                double.TryParse(placement[4].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _arPitch);
                            if (placement.Count >= 6 && placement[5] != null)
                                double.TryParse(placement[5].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _arRoll);
                            var head = json["truck.head.offset"] as JArray;
                            if (head != null && head.Count >= 4) _arLastHead = head;
                            _arTruckLastSeen = DateTime.Now;
                            _arTruckKnown = true;
                        }
                    }
                    catch { /* битый кадр — пропускаем */ }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* сеть закрылась */ }
            finally
            {
                try { ws.Dispose(); } catch { }
                if (_arWs == ws) _arWs = null;
                _arReconnectTimer?.Start();
            }
        }

        // ================================================================
        // МОДЕЛЬ ТОЧЕК (копия модели конвейера overrides — без рассылки)
        // ================================================================
        private void RefreshArModel()
        {
            var list = new List<(string, string, double, double, double, string, bool)>();
            try
            {
                // Города (статика по gameName)
                var cities = LoadStaticCities();
                foreach (var c in cities.Values)
                    if (c.Enabled && c.Hidden != 1)
                        list.Add((c.GameName, c.RealName, c.X, c.Y, c.Z, "city", false));

                // POI (статика + merged) — без hidden
                var pois = LoadStaticPois();
                foreach (var p in pois.Values)
                    if (p.Enabled && p.Hidden != 1)
                        list.Add((p.GameName, p.RealName, p.X, 0, p.Z, "poi", false));

                // Накладываем overrides (те же правила, что в конвейере) поверх копии:
                foreach (var (file, entry) in ReadOverridesInLoadOrder())
                {
                    var key = (string?)entry["gameName"] ?? (string?)entry["id"];
                    if (string.IsNullOrEmpty(key)) continue;
                    var idx = list.FindIndex(it => it.Item1 == key);
                    if (idx >= 0)
                    {
                        var kind = list[idx].Item6;
                        var pd = new PointData
                        {
                            GameName = list[idx].Item1, RealName = list[idx].Item2,
                            X = list[idx].Item3, Y = list[idx].Item4, Z = list[idx].Item5,
                            IsCity = kind == "city", IsPoi = kind == "poi"
                        };
                        MapEditorForm.ApplyJObjectToPoint(pd, entry);
                        if (pd.Hidden != 1 && pd.Enabled)
                            list[idx] = (pd.GameName, pd.RealName, pd.X, pd.Y, pd.Z, kind, false);
                        else
                            list.RemoveAt(idx);
                        continue;
                    }

                    // Целевая запись (isRandom/questType) или user-точка
                    bool isTarget = (entry["isRandom"]?.Value<bool>() ?? false) ||
                                    !string.IsNullOrEmpty(entry["questType"]?.Value<string>());
                    double ex = 0, ez = 0;
                    var coords = (string?)entry["coords"];
                    if (!string.IsNullOrEmpty(coords))
                    {
                        var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out ex);
                            double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out ez);
                        }
                    }
                    else
                    {
                        ex = entry["x"]?.Value<double>() ?? 0;
                        ez = entry["z"]?.Value<double>() ?? 0;
                    }
                    if (Math.Abs(ex) < 0.001 && Math.Abs(ez) < 0.001) continue; // заглушка (0,0)

                    var nm = (string?)entry["realName"] ?? (string?)entry["name"] ?? key ?? "";
                    var status = (string?)entry["status"];
                    var cu = (string?)entry["cooldown_until"];
                    bool onCooldown = !string.IsNullOrEmpty(cu) &&
                        DateTime.TryParse(cu, null, DateTimeStyles.RoundtripKind, out var until) &&
                        until > DateTime.UtcNow;

                    if (isTarget)
                    {
                        if (status == "inactive" || onCooldown) continue; // скрытые цели не показываем
                        list.Add((key!, nm, ex, 0, ez, "target", true));
                    }
                    else
                    {
                        if (((int?)entry["hidden"] ?? 0) == 1) continue;
                        list.Add((key!, nm, ex, 0, ez, "poi", false)); // user-точка как poi
                    }
                }

                _arPoints = list;
                _arModelAt = DateTime.Now;
                AppendLog($"[AR] Модель точек обновлена: {list.Count} (цели: {list.Count(i => i.Item7)}).");
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Ошибка обновления модели точек: {ex.Message}");
            }
        }

        // ================================================================
        // ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ + РАССЫЛКА ar_target
        // ================================================================
        private void ArUpdateTick()
        {
            try
            {
                // 1) Телеметрия (placement + head) — всегда, чтобы страница
                //    знала положение фуры и поворот головы.
                if (_arTruckKnown && _arLastHead != null && _arLastHead.Count >= 4)
                {
                    SendCommandToMap("ar_telemetry", new JObject
                    {
                        ["placement"] = new JArray(_arTruckX, _arTruckY, _arTruckZ, _arHeading, _arPitch, _arRoll),
                        ["head"] = _arLastHead
                    });
                }

                // 2) Подбор ближайшей точки (модель пересобираем не чаще 1/с).
                if ((DateTime.Now - _arModelAt).TotalMilliseconds > 1000) RefreshArModel();

                if (!_arTruckKnown) return; // нет телеметрии — страницу уведомит hasTarget=false при первом кадре
                if ((DateTime.Now - _arTruckLastSeen).TotalSeconds > 10)
                {
                    // Телеметрия умерла >10с: не спамим, но и не сбрасываем страницу.
                    return;
                }

                // Выбор: ближайшая ЦЕЛЬ (приоритет), затем ближайшая ГОРОД/POI.
                // Угловой приоритет (перед фурой — выгоднее) — как в JS-версии.
                (string gameName, string realName, double x, double y, double z, string kind, bool isTarget)? best = null;
                double bestScore = double.MaxValue;
                // heading: 0 = север(-Z), растёт против часовой → fwd = (-sin h, -cos h)
                double s = Math.Sin(_arHeading), c = Math.Cos(_arHeading);
                double fwdX = -s, fwdZ = -c;

                foreach (var it in _arPoints)
                {
                    double dx = it.x - _arTruckX;
                    double dz = it.z - _arTruckZ;
                    double d2 = dx * dx + dz * dz;
                    // Ограничение дальности для город/POI (3 км), цели — без limit.
                    if (!it.isTarget && d2 > 3000.0 * 3000.0) continue;
                    if (d2 < 0.01) continue;
                    double dist = Math.Sqrt(d2);
                    double fdot = dx * fwdX + dz * fwdZ;
                    double score = dist * (fdot > 0 ? 1.0 : 2.5) + (it.isTarget ? 0 : 1000);
                    if (score < bestScore) { bestScore = score; best = it; }
                }

                if (best == null)
                {
                    SendCommandToMap("ar_target", new JObject
                    {
                        ["hasTarget"] = false,
                        ["reason"] = "нет точек в радиусе 3 км"
                    });
                    return;
                }

                var b = best.Value;
                double distM = Math.Sqrt((b.x - _arTruckX) * (b.x - _arTruckX) + (b.z - _arTruckZ) * (b.z - _arTruckZ));
                SendCommandToMap("ar_target", new JObject
                {
                    ["hasTarget"] = true,
                    ["gameName"] = b.gameName,
                    ["realName"] = b.realName,
                    ["x"] = b.x,
                    ["y"] = b.y,
                    ["z"] = b.z,
                    ["dist"] = distM,
                    ["kind"] = b.kind,
                    ["heading"] = _arHeading
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Ошибка подбора цели: {ex.Message}");
            }
        }
    }
}