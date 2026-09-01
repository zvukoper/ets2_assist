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
        private CancellationTokenSource? _arRestCts;
        private Task? _arRestTask;
        private static readonly System.Net.Http.HttpClient _arHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
        private System.Windows.Forms.Timer? _arTimer;
        private System.Windows.Forms.Timer? _arReconnectTimer;
        private double _arTruckX, _arTruckY, _arTruckZ;   // опорная точка фуры (мир)
        // v74: изменились ли данные фуры с последней телеметрии (событийная рассылка).
        private bool _arTruckChanged;
        private double _arHeading;                        // heading фуры (доля оборота)
        private double _arPitch, _arRoll;                 // тангаж/крен фуры
        private JArray? _arLastHead;                      // truck.head.offset (6 элементов)
        private bool _arTruckKnown;                       // был хотя бы один placement

        // ПОМЕТКА В АР (v70): «Пометить в АР» (кнопка миникарты) создаёт точку на
        // пересечении ЦЕНТРАЛЬНОГО ЛУЧА ВЗГЛЯДА с ГОРИЗОНТАЛЬНОЙ ПЛОСКОСТЬЮ на высоте
        // грузовика; в редакторе эта точка открывается как новая, в AR-оверлее рисуется
        // серый крестик. Снимается кнопкой «отменить» в редакторе.
        private (double x, double y, double z)? _arPin;
        private DateTime _arTruckLastSeen = DateTime.MinValue;
        // Порт TruckTel (перечитывается из web_data.json каждые 3с — порт может смениться).
        private int _arWsPort = 8080;
        private DateTime _arWsPortAt = DateTime.MinValue;

        // ---- Кэш модели точек (обновляем разово при изменениях конвейера) ----
        private sealed record ArPoint(
            string gameName, string realName, double x, double y, double z,
            string kind, bool isTarget, string category, string color);
        private List<ArPoint> _arPoints = new();
        private DateTime _arModelAt = DateTime.MinValue;
        // Подпись последней модели (имя+координаты) — для «слать только при изменении».
        private string? _arModelSig;
        // v74: cities отдаём в ПЕРВОЙ телеметрии (список у страницы дальше уже есть).
        private bool _arCitiesSent;
        // v74: компенсация высоты городов, м (приложение шлёт города УЖЕ скомпенсированными).
        internal const double ArCityHeightCorrectionM = -44.0;

        // РАЗОВАЯ рассылка ar_target (фидбек 31.08.2026: точки статичны, слать
        // постоянно бессмысленно). Отправляем ТОЛЬКО при СМЕНЕ выбранной цели
        // (или после рестарта страницы/канала).
        private string? _arLastSentGameName;
        private bool _arTargetMustClear; // прошлый tick: цели не было (нужно разово сказать null)
        private DateTime _arLastTargetSentAt = DateTime.MinValue;   // v93: дебаунс спама ar_target

        // Частота телеметрии (30 Гц): плавная проекция в AR (страница рисует rAF
        // ~60 FPS, интерполируя между кадрами). ar_target в этом тике НЕ шлётся
        // постоянно — только при смене цели (см. _arLastSentGameName).
        private const int ArUpdateIntervalMs = 33;

        // ================================================================
        // ЗАПУСК / ОСТАНОВКА (по кнопке «Запустить AR»)
        // ================================================================
        internal void StartArTargetFeed()
        {
            // Статическая модель точек: собираем один раз, обновляем по таймеру 1/с
            // (файл overrides редок меняется, статика вообще не меняется).
            RefreshArModel();
            // Новая страница AR начинает с чистого состояния — цель переотправим разово.
            _arLastSentGameName = null;
            _arTargetMustClear = false;
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
            _arReconnectTimer.Start();
            _ = ArConnectTelemetryAsync();

            // REST-снимок: TruckTel /api/rest/flat/truck ОТДАЁТ truck.world.placement —
            //Confirmed 31.08.2026 (Invoke-RestMethod): placement в метрах карты, работает и на паузе.
            // WS-дельта на паузе truck.* НЕ шлёт, поэтому REST — основной источник на паузе,
            // WS — «горячий» поток в движении. Эмпирика (сессия 39) про «REST без placement» оказалась ошибочной.
            _arRestCts = new CancellationTokenSource();
            _arRestTask = ArRestLoopAsync(_arRestCts.Token);

            AppendLog("[AR] Канал AR-целей запущен (REST-снимок + WS-дельта телеметрии, подбор ближайшей точки на C#).");
        }

        internal void StopArTargetFeed()
        {
            _arTimer?.Stop();
            _arReconnectTimer?.Stop();
            try { _arCts?.Cancel(); } catch { }
            try { _arRestCts?.Cancel(); } catch { }
            try { _arWs?.Dispose(); } catch { }
            _arWs = null;
            StopArV2Overlay();
            AppendLog("[AR] Канал AR-целей остановлен.");
        }

        // ================================================================
        // AR v2.0 (v76): нативный D3D11-рендер. Та же логика/данные (главный
        // канал ArTarget заполняет ArBridge.Game), другой графический движок.
        // Требование: WS — источник данных, НЕ каданс рендера; latest-state.
        // ================================================================
        private AR.ArOverlayWindow? _arV2Window;

        internal void LaunchArOverlayV2()
        {
            try
            {
                if (_arV2Window != null && _arV2Window.IsRunning)
                {
                    AppendLog("[ARv2] Уже запущен — остановка (повторный клик = тоггл).");
                    StopArV2Overlay();
                    return;
                }
                var screen = GetGameScreen();
                _arV2Window = new AR.ArOverlayWindow();
                _arV2Window.ShowOnScreen(screen);
                StartArTargetFeed();          // данные идут по существующему каналу
                SyncAr2Button();
                AppendLog($"[ARv2] Нативный D3D11-оверлей запущен на экране '{screen.DeviceName}' ({screen.Bounds.Width}x{screen.Bounds.Height}).");
            }
            catch (Exception ex)
            {
                AppendLog($"[ARv2] Ошибка запуска: {ex.Message}");
            }
        }

        internal void StopArV2Overlay()
        {
            try { _arV2Window?.Stop(); } catch { }
            _arV2Window = null;
            SyncAr2Button();
            AppendLog("[ARv2] Нативный оверлей остановлен.");
        }

        // ================================================================
        // ТЕЛЕМЕТРИЯ ФУРЫ (REST-снимок /flat/truck каждую секунду)
        // ================================================================
        private async Task ArRestLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var resp = await _arHttp.GetAsync($"http://localhost:{_arWsPort}/api/rest/flat/truck", token);
                    resp.EnsureSuccessStatusCode();
                    var json = JObject.Parse(await resp.Content.ReadAsStringAsync(token));
                    ApplyPlacementJson(json, source: "rest");
                }
                catch (OperationCanceledException) { break; }
                catch { /* TruckTel ещё не поднялся / пауза — повторим через секунду */ }
                try { await Task.Delay(1000, token); } catch { break; }
            }
        }

        // Единый парсер placement из любого источника (REST-снимок или WS-дельта).
        // КООРДИНАТЫ TruckTel приходит УЖЕ В МЕТРАХ КАРТЫ (эмпирика 31.08.2026:
        // X=122629.27 Z=-54727.70 напрямую совпадает с дорогами/городами) —
        // НИКАКИХ делителей больше не применяем.
        private void ApplyPlacementJson(JObject json, string source)
        {
            var placement = json["truck.world.placement"] as JArray;
            if (placement == null) placement = json.SelectToken("truck.world.placement") as JArray;
            if (placement == null || placement.Count < 3)
            {
                // Нет placement (пауза/странный кадр) — подробности в app_data, 1 строка/5с.
                if ((DateTime.Now - _arSrcLogAt).TotalMilliseconds > 5000)
                {
                    _arSrcLogAt = DateTime.Now;
                    Logger.Current?.Data($"[AR] placement отсутствует в источнике '{source}' (пауза/нет данных).");
                }
                return;
            }
            // КЛЮЧЕВОЙ ФИКС (v64): JValue.ToString() в ru-RU даёт «121657,32» (запятая), и
            // Invariant TryParse читает запятую как разделитель тысяч -> ×1e8 мусор.
            // Читаем число НАПРЯМУЮ через Value<double>() — Newtonsoft конвертирует
            // культуро-независимо, никакого текстового раунд-трипа.
            double tx = placement[0].Value<double>();
            double tz = placement[2].Value<double>();
            if (double.IsNaN(tx) || double.IsInfinity(tx) || double.IsInfinity(tz)) return;

            // v74 (требование «никаких регулярных хартбитов»): телеметрия на сторону AR
            // уходит ТОЛЬКО при ИЗМЕНЕНИИ данных фуры (перемещение/поворот/голова) —
            // WS-дельта TruckTel и так шлёт только изменения, но REST-снимок повторяет
            // те же значения; фильтр в этом методе гарантирует «одна рассылка = изменение».
            bool changed = Math.Abs(tx - _arTruckX) > 0.05 || Math.Abs(tz - _arTruckZ) > 0.05;
            if (placement.Count >= 2 && placement[1] != null)
            {
                double ny = placement[1].Value<double>();
                changed |= Math.Abs(ny - _arTruckY) > 0.05;
                _arTruckY = ny;
            }
            _arTruckX = tx;
            _arTruckZ = tz;
            if (placement.Count >= 4 && placement[3] != null)
            {
                double nh = placement[3].Value<double>();
                changed |= Math.Abs(nh - _arHeading) > 0.0005;
                _arHeading = nh;
            }
            if (placement.Count >= 5 && placement[4] != null)
                _arPitch = placement[4].Value<double>();
            if (placement.Count >= 6 && placement[5] != null)
                _arRoll = placement[5].Value<double>();
            var head = json["truck.head.offset"] as JArray;
            if (head != null && head.Count >= 4)
            {
                if (_arLastHead == null || head.Count >= 5 && !JToken.DeepEquals(head, _arLastHead))
                    changed = true;                    // голова повернулась — тоже событие
                _arLastHead = head;
            }
            _arTruckChanged = changed;
            _arTruckLastSeen = DateTime.Now;
            _arTruckKnown = true;

            // AR v2.0 (v76): публикация телеметрии в latest-буфер рендера (latest wins).
            PublishArV2Snapshot();

            // Успешное применение — в app_data, 1 строка/5с (значения координат — данные).
            if ((DateTime.Now - _arSrcOkLogAt).TotalMilliseconds > 5000)
            {
                _arSrcOkLogAt = DateTime.Now;
                Logger.Current?.Data($"[AR] placement применён из '{source}': x={tx:F1} y={_arTruckY:F1} z={tz:F1} h={_arHeading:F3}.");
            }
        }

        // ================================================================
        // AR v2.0 — публикация снимка GameState (кузов/камера/цель/pin/города).
        // Вызывается по тем же событиям, что и WS-рассылки (событийная модель,
        // никакой регулярки: snapshot уходит в LatestBuffer — рендер берёт latest).
        // ================================================================
        private void PublishArV2Snapshot()
        {
            try
            {
                var s = new AR.ArGameState
                {
                    CamX = _arTruckX, CamY = _arTruckY, CamZ = _arTruckZ,
                    YawBase = _arHeading,
                    PitchBody = _arPitch,
                    Roll = _arRoll,
                    GroundY = _arTruckY,
                    PlaneOffsetM = AR.ArBridge.PlaneOffsetM,
                    ShowGrid = AR.ArBridge.ShowGrid
                };
                var h = _arLastHead;
                if (h != null && h.Count >= 4)
                {
                    s.YawHead = h[3].Value<double>();
                    if (h.Count >= 5) s.PitchHead = h[4].Value<double>();
                }
                if (_arPin.HasValue)
                    s.Pin = (_arPin.Value.x, _arPin.Value.y, _arPin.Value.z);

                // Города (уже с компенсацией −44 м) — как в payload ar_telemetry.
                if (_arPoints.Count > 0)
                {
                    var cities = new List<(double, double, double)>();
                    foreach (var it in _arPoints)
                    {
                        if (it.kind != "city" || Math.Abs(it.y) < 0.001) continue;
                        double d2 = (it.x - _arTruckX) * (it.x - _arTruckX) + (it.z - _arTruckZ) * (it.z - _arTruckZ);
                        if (d2 > 5000.0 * 5000.0) continue;
                        cities.Add((it.x, it.y + ArCityHeightCorrectionM, it.z));
                    }
                    s.Cities = cities;
                }
                // Цель: если она уже известна каналу (последнее ar_target состояние).
                if (_arV2Target != null) s.Target = _arV2Target;

                AR.ArBridge.PublishTelemetry(s);
                AR.ArBridge.MarkPublished();
            }
            catch { /* рендер не должен падать из-за канала */ }
        }

        // Последняя отправленная цель (для снимка v2.0).
        private AR.ArMarker? _arV2Target;

        private DateTime _arSrcLogAt = DateTime.MinValue;
        private DateTime _arSrcOkLogAt = DateTime.MinValue;

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
            _arWsPort = port;
            _arWsPortAt = DateTime.Now;

            var cts = new CancellationTokenSource();
            _arCts = cts;
            try
            {
                var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/api/ws/delta/flat/?throttle=50"), cts.Token);
                _arWs = ws;
                AppendLog($"[AR] Телеметрия WS подключена: ws://localhost:{port}/api/ws/delta/flat/ (REST-снимок: http://localhost:{port}/api/rest/flat/truck).");
                _ = ArReceiveLoopAsync(ws, cts.Token);
            }
            catch (Exception ex)
            {
                if (!_arTruckKnown) AppendLog($"[AR] Телеметрия WS недоступна ({port}): {ex.Message} (REST-снимок продолжит работу).");
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
                        ApplyPlacementJson(json, source: "ws");
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
            var list = new List<ArPoint>();
            try
            {
                // Города (статика по gameName) — жёлтые как в редакторе
                var cities = LoadStaticCities();
                foreach (var c in cities.Values)
                    if (c.Enabled && c.Hidden != 1)
                        list.Add(new ArPoint(c.GameName, c.RealName, c.X, c.Y, c.Z, "city", false, "Город", ""));

                // POI (статика + merged) — без hidden; category = категория оверлея
                var pois = LoadStaticPois();
                foreach (var p in pois.Values)
                    if (p.Enabled && p.Hidden != 1)
                        list.Add(new ArPoint(p.GameName, p.RealName, p.X, 0, p.Z, "poi", false, p.Category, ""));

                // Накладываем overrides (те же правила, что в конвейере) поверх копии:
                foreach (var (file, entry) in ReadOverridesInLoadOrder())
                {
                    var key = (string?)entry["gameName"] ?? (string?)entry["id"];
                    if (string.IsNullOrEmpty(key)) continue;
                    var idx = list.FindIndex(it => it.gameName == key);
                    if (idx >= 0)
                    {
                        var kind = list[idx].kind;
                        var cat = list[idx].category;
                        var pd = new PointData
                        {
                            GameName = list[idx].gameName, RealName = list[idx].realName,
                            X = list[idx].x, Y = list[idx].y, Z = list[idx].z,
                            IsCity = kind == "city", IsPoi = kind == "poi",
                            Category = cat
                        };
                        MapEditorForm.ApplyJObjectToPoint(pd, entry);
                        if (pd.Hidden != 1 && pd.Enabled)
                            list[idx] = new ArPoint(pd.GameName, pd.RealName, pd.X, pd.Y, pd.Z, kind, false, pd.Category, pd.Color);
                        else
                            list.RemoveAt(idx);
                        // v88: НЕ сбрасываем _arLastSentGameName здесь — это происходило
                        // при КАЖДОМ RefreshArModel (раз в 5с) и ломало событийную модель:
                        // ar_target переотправлялся каждые 5 секунд (спам при паузе).
                        // Реальное изменение модели ловит сигнатура sig ниже
                        // (if (changed) _arLastSentGameName = null;).
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
                    var ovrColor = (string?)entry["color"] ?? "";
                    var ovrCat = (string?)entry["category"] ?? "";
                    bool onCooldown = !string.IsNullOrEmpty(cu) &&
                        DateTime.TryParse(cu, null, DateTimeStyles.RoundtripKind, out var until) &&
                        until > DateTime.UtcNow;

                    if (isTarget)
                    {
                        if (status == "inactive" || onCooldown) continue; // скрытые цели не показываем
                        list.Add(new ArPoint(key!, nm, ex, 0, ez, "target", true, string.IsNullOrEmpty(ovrCat) ? "Цель" : ovrCat, ovrColor));
                    }
                    else
                    {
                        if (((int?)entry["hidden"] ?? 0) == 1) continue;
                        list.Add(new ArPoint(key!, nm, ex, 0, ez, "poi", false, string.IsNullOrEmpty(ovrCat) ? "custom" : ovrCat, ovrColor)); // user-точка как poi
                    }
                }

                // Модель изменилась? Сравниваем подпись (имя+координаты) со старой —
                // пересборка раз в секунду НЕ должна переотправлять ar_target (фидбек
                // 31.08.2026: слать только при обновлении точек, не регулярно).
                string sig = string.Concat(list.OrderBy(p => p.gameName, StringComparer.Ordinal)
                    .Select(p => p.gameName + "|" + p.x.ToString("F1") + "," + p.y.ToString("F0") + "," + p.z.ToString("F1") + ";"));
                bool changed = sig != _arModelSig;
                if (changed) _arModelSig = sig;
                _arPoints = list;
                _arModelAt = DateTime.Now;
                // Цель переотправляем ТОЛЬКО при реальном изменении модели.
                if (changed) _arLastSentGameName = null;
                // Строка «Модель точек обновлена» — ПОТОКОВАЯ (1/с) → в app_data.
                // В workflow пишем только при реальном изменении (замен файла точек).
                if (changed)
                    AppendLog($"[AR] Модель точек обновлена: {list.Count} (цели: {list.Count(i => i.isTarget)}).");
                else
                    Logger.Current?.Data($"[AR] Модель точек без изменений: {list.Count}.");
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Ошибка обновления модели точек: {ex.Message}");
            }
        }

        // ================================================================
        // ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ + РАССЫЛКА ar_target (v74: СОБЫТИЙНАЯ модель)
        // ================================================================
        // ТРЕБОВАНИЕ 31.08.2026: приложение НЕ отправляет на AR ничего régulièrement.
        // AR — dumb-отрисовщик: хранит координаты точки и рисует сам. Мы шлём:
        //   1) ar_telemetry — ТОЛЬКО при изменении телеметрии фуры (см. _arTruckChanged);
        //   2) ar_target — ТОЛЬКО при смене ближайшей точки (приближение к другой);
        //   3) cities — ТОЛЬКО при первой телеметрии или изменении набора (высоты уже
        //      с компенсацией −44 м — в payload отдаём готовые города).
        private void ArUpdateTick()
        {
            try
            {
                // Порт TruckTel может смениться — перечитываем web_data.json не чаще 1 раз в 3с.
                if ((DateTime.Now - _arWsPortAt).TotalSeconds > 3)
                {
                    _arWsPortAt = DateTime.Now;
                    try
                    {
                        if (File.Exists(AppDataPaths.WebDataFile))
                        {
                            var j = JObject.Parse(File.ReadAllText(AppDataPaths.WebDataFile));
                            int p = j["wsPort"]?.Value<int>() ?? _arWsPort;
                            if (p != _arWsPort)
                            {
                                _arWsPort = p;
                                AppendLog($"[AR] Порт TruckTel сменился на {p} — REST-снимок переподключается.");
                            }
                        }
                    }
                    catch { }
                }

                // Собираем модель точек РЕДКО (она меняется только по событиям файлов,
                // но файлы мы не мониторим — поэтому 5с как «дешёвый» фоновый refresh без
                // рассылок: рассылка всё равно только по факту смены ближайшей точки).
                if ((DateTime.Now - _arModelAt).TotalMilliseconds > 5000) RefreshArModel();

                // Телеметрии нет полностью → тишина (страница сама покажет статус).
                if (!_arTruckKnown) return;

                // 1) ТЕЛЕМЕТРИЯ — ТОЛЬКО при изменении (v74).
                //    Компонент cities прилагаем только в ПЕРВОЙ телеметрии (дальше список
                //    у страницы уже есть; компенсация heights считаем на C# заранее).
                if (_arTruckChanged && _arLastHead != null && _arLastHead.Count >= 4)
                {
                    var tel = new JObject
                    {
                        ["placement"] = new JArray(_arTruckX, _arTruckY, _arTruckZ, _arHeading, _arPitch, _arRoll),
                        ["head"] = _arLastHead
                    };
                    if (_arPin.HasValue)
                    {
                        tel["pin"] = new JObject { ["x"] = _arPin.Value.x, ["y"] = _arPin.Value.y, ["z"] = _arPin.Value.z };
                    }
                    if (!_arCitiesSent && _arPoints.Count > 0)
                    {
                        var cityArr = new JArray();
                        foreach (var it in _arPoints)
                        {
                            if (it.kind != "city") continue;
                            double d2 = (it.x - _arTruckX) * (it.x - _arTruckX) + (it.z - _arTruckZ) * (it.z - _arTruckZ);
                            if (d2 > 5000.0 * 5000.0) continue;
                            if (Math.Abs(it.y) < 0.001) continue;
                            // v74: город в payload УЖЕ СКОМПЕНСИРОВАН (−44 м) — «приложение
                            // передаёт в АР уже скомпенсированную высоту точки города».
                            cityArr.Add(new JObject { ["x"] = it.x, ["y"] = it.y + ArCityHeightCorrectionM, ["z"] = it.z });
                        }
                        tel["cities"] = cityArr;
                        _arCitiesSent = true;
                    }
                    SendCommandToMap("ar_telemetry", tel);
                    _arTruckChanged = false;   // событие обработано
                }

                // 2) ПОДБОР БЛИЖАЙШЕЙ ТОЧКИ — на КАЖДОМ тике. Это ЛОКАЛЬНЫЙ расчёт
                //    (без сети!): перебор ~1200 точек — микросекунды. Регулярных ОТПРАВОК
                //    нет: ar_target уходит ТОЛЬКО при смене лучшей точки (см. ниже).
                //    (v75 фикс «в AR перестали появляться точки»: раньше подбор был
                //    привязан к _arTruckChanged, который сбрасывается после telemetry —
                //    при неизменной позиции подбор не выполнялся вовсе.)

                if ((DateTime.Now - _arTruckLastSeen).TotalSeconds > 10)
                {
                    if ((DateTime.Now - _arTruckLastSeen).TotalSeconds < 15)
                    {
                        SendCommandToMap("ar_target", new JObject
                        {
                            ["hasTarget"] = false,
                            ["reason"] = "нет телеметрии фуры (пауза/TruckTel недоступен)"
                        });
                        _arLastSentGameName = null;
                        _arTargetMustClear = false;
                        // v81: сброс v2-цели при потере телеметрии (маркер исчезает).
                        if (_arV2Target != null) { _arV2Target = null; PublishArV2Snapshot(); }
                    }
                    return;
                }

                // Выбор: ближайшая ЦЕЛЬ (приоритет), затем ближайшая ГОРОД/POI.
                // Угловой приоритет (перед фурой — выгоднее).
                ArPoint? best = null;
                double bestScore = double.MaxValue;
                // heading: 0 = север(-Z), растёт против часовой → fwd = (-sin h, -cos h)
                double s = Math.Sin(_arHeading), c = Math.Cos(_arHeading);
                double fwdX = -s, fwdZ = -c;

                foreach (var it in _arPoints)
                {
                    double dx = it.x - _arTruckX;
                    double dz = it.z - _arTruckZ;
                    double d2 = dx * dx + dz * dz;
                    // v73: ЕДИНЫЙ радиус 1500 м (фидбек: «дистанция почти 2 км, а крестик
                    // всё ещё отображается» + плашка «нет точек в радиусе 1.5 км»). Цели
                    // приоритетны по score, но тоже в пределах 1.5 км.
                    if (d2 > 1500.0 * 1500.0) continue;
                    if (d2 < 0.01) continue;
                    double dist = Math.Sqrt(d2);
                    double fdot = dx * fwdX + dz * fwdZ;
                    double score = dist * (fdot > 0 ? 1.0 : 2.5) + (it.isTarget ? 0 : 1000);
                    if (score < bestScore) { bestScore = score; best = it; }
                }

                // РАЗОВАЯ РАССЫЛКА (фидбек 31.08.2026): ar_target шлём ТОЛЬКО при смене
                // выбранной цели (или если предыдущий раз сообщали hasTarget=false).
                // Постоянные пакеты не нужны — все точки статичны, проекцию оверлей
                // выполняет сам по телеметрии (~60 FPS).
                if (best == null)
                {
                    if (!_arTargetMustClear)
                    {
                        _arTargetMustClear = true;
                        _arLastSentGameName = null;
                        SendCommandToMap("ar_target", new JObject
                        {
                            ["hasTarget"] = false,
                            ["reason"] = "нет точек в радиусе 1.5 км"
                        });
                        // v81: сброс цели и в v2-канале (маркер не должен оставаться
                        // висеть, когда точка вышла из радиуса).
                        _arV2Target = null;
                        PublishArV2Snapshot();
                        // ПОДРОБНОСТИ — в app_data (не спамим workflow): фура + число точек + near.
                        ArLogPickDetail(hasTarget: false, reason: "нет точек в радиусе 1.5 км");
                    }
                    return;
                }

                var b = best!;
                if (b.gameName == _arLastSentGameName)
                {
                    // Цель не сменилась — НЕ шлём (разово, точки статичны).
                    // (v81: в v2-канал она всё равно попала при первой рассылке —
                    //  см. PublishArV2Target ниже; повторять не нужно.)
                    return;
                }
                // v93 ДЕБАУНС: при равных score цель может скакать между двумя
                // точками каждый тик (fdot меняет знак при повороте) → спам
                // ar_target в лог. Не шлём чаще 1 раза в 500мс.
                if ((DateTime.Now - _arLastTargetSentAt).TotalMilliseconds < 500)
                    return;

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
                    ["category"] = b.category,
                    ["color"] = b.color,
                    ["heading"] = _arHeading
                });
                _arLastSentGameName = b.gameName;
                _arTargetMustClear = false;
                _arLastTargetSentAt = DateTime.Now;   // v93: дебаунс
                // v81 КОРЕНЬ БАГА v80: _arV2Target нигде не присваивался → в v2-snapshot
                // Target всегда null → рендеру нечего было рисовать (1193 точки, цели 0
                // в логе; рассылка ar_target шла только на WS-страницу, не в ArBridge).
                _arV2Target = new AR.ArMarker
                {
                    GameName = b.gameName,
                    RealName = b.realName,
                    X = b.x, Y = b.y, Z = b.z,
                    Dist = distM,
                    Kind = b.kind,
                    Category = b.category,
                    Color = b.color
                };
                PublishArV2Snapshot();   // цель сразу в latest-буфер рендера
                ArLogPickDetail(hasTarget: true, reason: $"{b.gameName} kind={b.kind} cat={b.category} dist={distM:F0}м");
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Ошибка подбора цели: {ex.Message}");
            }
        }

        // Данные подбора цели — в app_data.log (раз в 5с максимум, чтобы не плодить мегабайты).
        private DateTime _arPickLogAt = DateTime.MinValue;
        private void ArLogPickDetail(bool hasTarget, string reason)
        {
            if ((DateTime.Now - _arPickLogAt).TotalMilliseconds < 5000) return;
            _arPickLogAt = DateTime.Now;
            try
            {
                int near3k = 0; int total = _arPoints.Count;
                foreach (var it in _arPoints)
                {
                    double ddx = it.x - _arTruckX, ddz = it.z - _arTruckZ;
                    if (ddx * ddx + ddz * ddz <= 1500.0 * 1500.0) near3k++;
                }
                Logger.Current?.Data($"[AR] tick: truck=({_arTruckX:F1},{_arTruckY:F1},{_arTruckZ:F1}) h={_arHeading:F3}" +
                    $" known={_arTruckKnown} points={_arPoints.Count} near3km={near3k} hasTarget={hasTarget} ({reason})");
            }
            catch { }
        }

        // ================================================================
        // ПОМЕТКА В АР (v70→v74): точка пересечения ЦЕНТРАЛЬНОГО ЛУЧА ВЗГЛЯДА
        // ГОЛОВЫ с ГОРИЗОНТАЛЬНОЙ ПЛОСКОСТЬЮ на высоте грузовика. «Пометить в АР».
        // v74 ФИКС ИНВЕРСИИ ВЕРТИКАЛИ (фидбек: «смотрю на землю — точка на макс.
        // дистанции; выше горизонта — на земле; чем выше голова, тем ближе»):
        //   head.offset[4] > 0 = взгляд ВВЕРХ (v72-эмпирика), < 0 = ВНИЗ.
        //   Компонент луча по Y: dirY = +sin(pitchRad) — при взгляде ВНИЗ (pitch<0)
        //   dirY<0, t = dyPlane/dirY = (−1.9)/(<0) > 0 → корректное пересечение.
        //   Питч КУЗОВА (placement[4]) не учитываем (как и в отрисовке AR v74).
        // ================================================================
        internal void ArPlacePinFromViewCenter()
        {
            if (!_arTruckKnown)
            {
                AppendLog("[AR] Пометка невозможна: нет телеметрии фуры.");
                return;
            }
            // Ориентация камеры — согласована с pinhole-проекцией (v90):
            // проекция теперь ТОЧНЫЙ ПОРТ ar_hud.js (эталон, подтверждён
            // пользователем) — БЕЗ инверсий. Поэтому и луч pin НЕ инвертируем
            // (v89-инверсия была следствием v87-инверсии проекции; обе убраны).
            // yaw = (heading + headYaw)*2π, «вперёд» = (-sin,-cos).
            double yaw = _arHeading * Math.PI * 2;
            var head = _arLastHead;
            if (head != null && head.Count >= 4)
            {
                double hy = head[3].Value<double>();
                if (double.IsFinite(hy)) yaw += hy * Math.PI * 2;
            }
            double fx = -Math.Sin(yaw), fz = -Math.Cos(yaw);
            // pitch ТОЛЬКО ГОЛОВЫ (head.offset[4], доля оборота).
            // v94 ФИКС ВЕРТИКАЛИ: знак pitch БЕЗ инверсии (как в JS-эталоне,
            // headPitchSign=1). v91-инверсия (pitch=-hp*2π) давала инверсию:
            //   голова вверх (hp>0) → pitch<0 → dirY<0 → t близко (симптом).
            //   С pitch = +hp*2π:
            //   голова вверх (hp>0) → pitch>0 → dirY>0 → t=макс (ДАЛЬШЕ) ✓
            //   голова вниз (hp<0) → pitch<0 → dirY<0 → t ближе ✓
            double pitch = 0;
            if (head != null && head.Count >= 5)
            {
                double hp = head[4].Value<double>();
                if (double.IsFinite(hp)) pitch = hp * Math.PI * 2;
            }
            // Луч к плоскости Y = truckY + planeOffset (высота грузовика + смещение).
            // v96: смещение плоскости земли (Ctrl+Shift+PGUP/PGDN) влияет на
            // создание новых меток точек — плоскость, куда ставится метка.
            const double PinMaxDistM = 1500.0;
            const double EyeHeightM = 1.9;
            double planeY = _arTruckY + AR.ArBridge.PlaneOffsetM;
            double dirY = Math.Sin(pitch);        // взгляд вниз (pitch<0) => dirY<0 (вниз)
            double dirXZ = Math.Cos(pitch);       // |компонента в горизонтали|
            double eyeY = _arTruckY + EyeHeightM;
            double dyPlane = planeY - eyeY;       // до плоскости (≈ −1.9 м + смещение)
            double t;
            if (dirY > -1e-4 || dyPlane / dirY <= 0)
            {
                // Взгляд ВЫШЕ горизонта (или ровно на него) → точка на МАКС. дистанции.
                t = PinMaxDistM;
            }
            else
            {
                t = dyPlane / dirY;               // чем сильнее вниз смотрим — тем ближе
                if (t > PinMaxDistM) t = PinMaxDistM;
            }
            if (t < 1) t = 1;
            double px = _arTruckX + fx * dirXZ * t;
            double pz = _arTruckZ + fz * dirXZ * t;
            double py = planeY;                   // высота = плоскость земли (со смещением)
            _arPin = (px, py, pz);
            SendCommandToMap("ar_pin", new JObject
            {
                ["active"] = true,
                ["x"] = px, ["y"] = py, ["z"] = pz
            });
            Logger.Current?.Data($"[AR] pin placed: x={px:F1} y={py:F1} z={pz:F1} (t={t:F1}м, pitch={pitch:F3})");
            AppendLog($"[AR] Пометка установлена: ({px:F0}, {pz:F0}) на {t:F0}м.");
        }

        // Снять пометку (кнопка «отменить» в редакторе / закрытие формы).
        internal void ArClearPin()
        {
            if (!_arPin.HasValue) return;
            _arPin = null;
            SendCommandToMap("ar_pin", new JObject { ["active"] = false });
            AppendLog("[AR] Пометка снята (отмена в редакторе).");
        }

        // Текущая пометка (для открытия в редакторе); null — нет пометки.
        internal (double x, double y, double z)? GetArPin() => _arPin;

        // Пометка по явным координатам (создание точки кликом по карте в редакторе):
        // высота = плоскость земли АР (truckY + смещение, v96).
        internal void ArPlacePinAtWorld(double x, double z)
        {
            if (!_arTruckKnown) return;            // нет телеметрии — пометку не рисуем
            double py = _arTruckY + AR.ArBridge.PlaneOffsetM;
            _arPin = (x, py, z);
            SendCommandToMap("ar_pin", new JObject
            {
                ["active"] = true,
                ["x"] = x, ["y"] = py, ["z"] = z
            });
        }
    }
}