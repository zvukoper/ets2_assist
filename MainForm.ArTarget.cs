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
        // v1.0.40.29: потоковый таймер AR-тика (System.Windows.Forms.Timer не тикал).
        private System.Threading.Timer? _arTickTimer;
        private int _arTickBusy;   // 0/1 — защита от наложения тиков (Interlocked)
        private System.Windows.Forms.Timer? _arReconnectTimer;
        private double _arTruckX, _arTruckY, _arTruckZ;   // опорная точка фуры (мир)
        // v74: изменились ли данные фуры с последней телеметрии (событийная рассылка).
        private bool _arTruckChanged;
        // v1.0.40.28 КОРЕНЬ БАГА «AR1 остаётся без визуальных изменений/нет телеметрии»:
        // `_arTruckChanged` перетирался ВХОДЯЩИМИ данными (ApplyPlacementJson ставит
        // _arTruckChanged = changed при КАЖДОМ пакете WS/REST) раньше, чем тик успевал
        // его обработать. При стоянке REST-снимок (1 Гц) сбрасывал флаг в false, и
        // ar_telemetry НЕ УХОДИЛА ВООБЩЕ (в логе: 0 отправок за сессию). Отдельный
        // «залипающий» флаг форс-отправки НЕ сбрасывается приёмом данных — его гасит
        // только сам тик после успешной отправки.
        private bool _arTelemetryForced;
        private double _arHeading;                        // heading фуры (доля оборота)
        private double _arPitch, _arRoll;                 // тангаж/крен фуры
        private JArray? _arLastHead;                      // truck.head.offset (6 элементов)
        // v1.0.40.27: последний ОТПРАВЛЕННЫЙ на страницу head — чтобы досылать телеметрию
        // в момент, когда head появился ПОЗЖЕ первого placement (событийная модель).
        private JArray? _arLastHeadSent;

        // ================================================================
        // v1.0.40.30 (ETS2_AR_CAMERA_POSE_IMPLEMENTATION): ПОЛНАЯ 6DoF-ПОЗА КАМЕРЫ.
        // Источник — штатная SCS hierarchy из телеметрии:
        //   truck.world.placement → truck.cabin.position + truck.cabin.offset
        //                         → truck.head.position + truck.head.offset
        // Все величины ЛОКАЛЬНЫЕ (метры в системе фуры), кроме truck.world.placement.
        // Никаких EyeHeight/PitchCompensation/roll-хаков — поза считается ОДИН раз
        // (ScsCameraPose.TryCreate) и далее используется как есть.
        // ================================================================
        private System.Numerics.Vector3 _arCabinPosition = System.Numerics.Vector3.Zero;
        private System.Numerics.Vector3 _arCabinOffsetPosition = System.Numerics.Vector3.Zero;
        private System.Numerics.Vector3 _arHeadPosition = System.Numerics.Vector3.Zero;
        private System.Numerics.Vector3 _arHeadOffsetPosition = System.Numerics.Vector3.Zero;

        private AR.ScsEuler _arCabinOffsetOrientation = AR.ScsEuler.Identity;
        private AR.ScsEuler _arHeadOffsetOrientation = AR.ScsEuler.Identity;

        private bool _arHeadPositionKnown;
        private bool _arCameraPoseValid;
        private AR.ScsCameraPose _arCameraPose;
        private long _arPoseSequence;
        // ================================================================
        // v1.0.40.30 (ETS2_AR_CAMERA_POSE_IMPLEMENTATION): ПОЛНАЯ 6DoF-ПОЗА КАМЕРЫ.
        // Источник — штатная SCS hierarchy из телеметрии:
        //   truck.world.placement → truck.cabin.position + truck.cabin.offset
        //                         → truck.head.position + truck.head.offset
        // Все величины ЛОКАЛЬНЫЕ (метры в системе фуры), кроме truck.world.placement.
        // Никаких EyeHeight/PitchCompensation/roll-хаков — поза считается ОДИН раз
        // (ScsCameraPose.TryCreate) и далее используется как есть.
        // ================================================================
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

        // Частота AR-тика. v1.0.40.30: 33 → 16 мс (ETS2_AR_CAMERA_POSE §9) — поза
        // камеры обновляется чаще, чтобы уменьшить временну́ю задержку до оверлея.
        // Геометрия от этого НЕ меняется (она точна относительно последнего сэмпла).
        private const int ArUpdateIntervalMs = 16;

        // ================================================================
        // ЗАПУСК / ОСТАНОВКА (по кнопке «Запустить AR»)
        // ================================================================
        internal void StartArTargetFeed()
        {
            // Статическая модель точек: собираем один раз, обновляем по таймеру 1/с
            // (файл overrides редок меняется, статика вообще не меняется).
            RefreshArModel();
            // v1.0.40.27 КОРЕНЬ БАГА «AR1 пишет: нет телеметрии от приложения»:
            // рассылка ar_telemetry событийная (_arTruckChanged), а при старте AR1
            // флаг НЕ сбрасывался. Если фура стоит/на паузе (координаты не меняются),
            // ни одного пакета телеметрии не уходило, и страница вечно показывала
            // «нет телеметрии», хотя данные есть. Теперь старт канала = форс-рассылка:
            // одна телеметрия (с городами) и одна цель уходят сразу.
            // v1.0.40.28: форс живёт в ОТДЕЛЬНОМ залипающем флаге (_arTelemetryForced) —
            // _arTruckChanged перетирается приёмом данных до тика (см. объявление поля).
            _arTruckChanged = true;
            _arTelemetryForced = true;
            _arCitiesSent = false;
            _arLastHeadSent = null;
            // Новая страница AR начинает с чистого состояния — цель переотправим разово.
            _arLastSentGameName = null;
            _arTargetMustClear = false;
            try { EnsureTestTargetsFile(); } catch { }

            // v1.0.40.29 КОРЕНЬ «в AR1 вообще ничего не меняется» (второй дефект):
            // System.Windows.Forms.Timer для AR-тика НЕ тикал (в логе за сессии 19:20 и
            // 20:54 — НИ ОДНОЙ записи тика; работал только прямой RefreshArModel при
            // старте канала). Тот же баг уже ловили в MapEditor2Form («Timer, созданный
            // внутри async-метода, может НЕ тикать») и лечили сменой на
            // System.Threading.Timer. Здесь делаем так же: потоковый таймер не зависит
            // от очереди сообщений WinForms, а работа с UI/отправкой — через BeginInvoke.
            StartArTickTimer();

            if (_arReconnectTimer == null)
            {
                _arReconnectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _arReconnectTimer.Tick += (_, _) => { _arReconnectTimer!.Stop(); _ = ArConnectTelemetryAsync(); };
            }
            _arReconnectTimer.Start();
            _ = ArConnectTelemetryAsync();
            // v1.0.40.27: страница получает актуальный FOV AR1 сразу (CTRL+PGUP/PGDN его меняет).
            SendAr1FovToPage();

            // REST-снимок: TruckTel /api/rest/flat/truck ОТДАЁТ truck.world.placement —
            //Confirmed 31.08.2026 (Invoke-RestMethod): placement в метрах карты, работает и на паузе.
            // WS-дельта на паузе truck.* НЕ шлёт, поэтому REST — основной источник на паузе,
            // WS — «горячий» поток в движении. Эмпирика (сессия 39) про «REST без placement» оказалась ошибочной.
            _arRestCts = new CancellationTokenSource();
            _arRestTask = ArRestLoopAsync(_arRestCts.Token);

            AppendLog("[AR] Канал AR-целей запущен (REST-снимок + WS-дельта телеметрии, подбор ближайшей точки на C#).");
        }

        // ================================================================
        // v1.0.40.29: AR-ТИК — ПОТОКОВЫЙ ТАЙМЕР (не System.Windows.Forms.Timer).
        // ПРИЧИНА: WinForms-таймер AR-тика НЕ тикал (в логе за сессии 19:20 / 20:54 —
        // ни одной записи тика, работал только прямой RefreshArModel при старте канала).
        // Симптом для пользователя: «в AR1 вообще ничего не меняется», хотя данные есть.
        // Тот же баг уже ловили в MapEditor2Form и лечили сменой таймера — повторяем
        // проверенное решение: System.Threading.Timer (не зависит от очереди сообщений
        // WinForms) + маршалинг в UI-поток через BeginInvoke для работы с WebSocket/UI.
        // ================================================================
        private void StartArTickTimer()
        {
            try
            {
                _arTickTimer ??= new System.Threading.Timer(
                    _ => QueueArUpdateTick(),
                    null,
                    Timeout.Infinite,
                    Timeout.Infinite);
                _arTickTimer.Change(ArUpdateIntervalMs, ArUpdateIntervalMs);
                Logger.Current?.Data("[AR] AR-тик: потоковый таймер запущен (интервал " +
                    ArUpdateIntervalMs + " мс).");
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Не удалось запустить AR-тик: {ex.Message}");
            }
        }

        private void StopArTickTimer()
        {
            try { _arTickTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        }

        // Тик приходит в потоке пула: работа с WS/UI — на UI-потоке (BeginInvoke).
        private void QueueArUpdateTick()
        {
            // Наложение тиков недопустимо (отправка 33 мс может не успеть) — пропускаем.
            if (Interlocked.CompareExchange(ref _arTickBusy, 1, 0) != 0) return;
            bool handed = false;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;   // форму закрывают
                BeginInvoke((Action)(() =>
                {
                    try { ArUpdateTick(); }
                    finally { Volatile.Write(ref _arTickBusy, 0); }
                }));
                handed = true;   // флаг снимет сам UI-вызов в finally
            }
            catch
            {
                // BeginInvoke не сработал (форма закрывается) — тик дальше не нужен.
            }
            finally
            {
                // ВАЖНО: если вызов НЕ передан в UI-очередь, флаг надо снять здесь,
                // иначе он «залипнет» в 1 и тик умрёт навсегда (ловушка того же класса,
                // что и исходный баг с неработающим таймером).
                if (!handed) Volatile.Write(ref _arTickBusy, 0);
            }
        }

        internal void StopArTargetFeed()
        {
            StopArTickTimer();
            _arReconnectTimer?.Stop();
            try { _arCts?.Cancel(); } catch { }
            try { _arRestCts?.Cancel(); } catch { }
            try { _arWs?.Dispose(); } catch { }
            _arWs = null;
            StopArV2Overlay();
            AppendLog("[AR] Канал AR-целей остановлен.");
        }

        // ================================================================
        // AR1: FOV (CTRL+PGUP / CTRL+PGDN, шаг 1°) — v1.0.40.27
        // Значение живёт в AR.ArBridge.FovDegreesAr1 (отдельно от FOV AR2),
        // сохраняется в AppSettings и транслируется странице командой ar_fov
        // (ar_hud.js: CFG.fovDeg). При старте AR1 значение досылается сразу.
        // ================================================================
        internal void SetAr1Fov(double degrees, string source)
        {
            double clamped = Math.Clamp(degrees, 30.0, 150.0);
            AR.ArBridge.FovDegreesAr1 = clamped;
            try { AppSettings.Ar1FovDeg = clamped; AppSettings.Save(); } catch { }
            SendCommandToMap("ar_fov", new JObject { ["fov"] = clamped });
            // Смена FOV не должна спамить workflow при автоповторе — подробности в app_data.
            Logger.Current?.Data($"[AR] FOV AR1 = {clamped:F1}° ({source})");
        }

        // Отправка текущего FOV странице AR1 (при старте канала/страницы).
        private void SendAr1FovToPage()
        {
            double fov = AR.ArBridge.FovDegreesAr1;
            SendCommandToMap("ar_fov", new JObject { ["fov"] = fov });
        }

        // ================================================================
        // AR1: форс-рассылка данных (v1.0.40.27)
        // Событийная модель (телеметрия только при изменении) ломала первый показ:
        // если фура стоит (пауза/стоянка) и страница подключилась ПОСЛЕ первой
        // рассылки — она навсегда оставалась с «нет телеметрии». Форс-сброс флагов
        // заставляет ближайший тик отправить телеметрию, города и цель заново.
        // Вызывается из OnOpen WS-клиента (фоновый поток) — маршалим на UI-поток.
        // ================================================================
        internal void ForceArDataResend(string reason)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    _arTruckChanged = true;      // телеметрия уйдёт следующим тиком
                    _arTelemetryForced = true;   // v1.0.40.28: приём данных его не перетрёт
                    _arCitiesSent = false;       // города нужны новой странице
                    _arLastHeadSent = null;      // head дослать вместе с placement
                    _arLastSentGameName = null;  // цель переотправить
                    Logger.Current?.Data($"[AR] Форс-рассылка данных AR ({reason}).");
                }));
            }
            catch { /* канал не запущен — нечего пересылать */ }
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

        // v1.0.40.28: чтение массивов камеры из телеметрии. Возвращает true, если
        // значения изменились (событийная модель: лишних рассылок не делаем).
        private static bool TryReadVec3(JObject json, string key, double[] dst)
        {
            try
            {
                var arr = json[key] as JArray ?? json.SelectToken(key) as JArray;
                if (arr == null || arr.Count < 3) return false;
                double nx = arr[0].Value<double>(), ny = arr[1].Value<double>(), nz = arr[2].Value<double>();
                if (!double.IsFinite(nx) || !double.IsFinite(ny) || !double.IsFinite(nz)) return false;
                bool ch = Math.Abs(nx - dst[0]) > 0.0005 || Math.Abs(ny - dst[1]) > 0.0005 || Math.Abs(nz - dst[2]) > 0.0005;
                dst[0] = nx; dst[1] = ny; dst[2] = nz;
                return ch;
            }
            catch { return false; }
        }

        // ================================================================
        // v1.0.40.30: чтение SCS-компонентов позы камеры из телеметрии.
        // TruckTel публикует SCS fvector/fplacement как массивы:
        //   [x,y,z]          — position (truck.cabin.position / truck.head.position)
        //   [x,y,z,h,p,r]    — placement (truck.cabin.offset / truck.head.offset)
        // Числа читаем через Value<double>() — культуро-независимо (урок v64).
        // ================================================================
        private static JArray? FlatArray(JObject json, string key)
        {
            if (json[key] is JArray direct) return direct;
            return json.SelectToken(key) as JArray;
        }

        private static bool TryReadVector3(JObject json, string key, out System.Numerics.Vector3 value)
        {
            value = System.Numerics.Vector3.Zero;
            var a = FlatArray(json, key);
            if (a == null || a.Count < 3) return false;
            try
            {
                double x = a[0].Value<double>();
                double y = a[1].Value<double>();
                double z = a[2].Value<double>();
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;
                value = new System.Numerics.Vector3((float)x, (float)y, (float)z);
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadScsPlacement(JObject json, string key,
            out System.Numerics.Vector3 position, out AR.ScsEuler orientation)
        {
            position = System.Numerics.Vector3.Zero;
            orientation = AR.ScsEuler.Identity;
            var a = FlatArray(json, key);
            if (a == null || a.Count < 6) return false;
            try
            {
                double x = a[0].Value<double>();
                double y = a[1].Value<double>();
                double z = a[2].Value<double>();
                double h = a[3].Value<double>();
                double p = a[4].Value<double>();
                double r = a[5].Value<double>();
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
                    !double.IsFinite(h) || !double.IsFinite(p) || !double.IsFinite(r)) return false;
                position = new System.Numerics.Vector3((float)x, (float)y, (float)z);
                orientation = new AR.ScsEuler(h, p, r);
                return true;
            }
            catch { return false; }
        }

        // Пересчёт мировой позы камеры из последних SCS-компонентов.
        // Вызывается после КАЖДОГО приёма телеметрии: cabin/head могут приходить
        // отдельными delta-пакетами (без truck.world.placement).
        private void RebuildArCameraPose()
        {
            if (!_arTruckKnown || !_arHeadPositionKnown)
            {
                _arCameraPoseValid = false;
                return;
            }

            if (!AR.ScsCameraPose.TryCreate(
                    _arTruckX, _arTruckY, _arTruckZ,
                    new AR.ScsEuler(_arHeading, _arPitch, _arRoll),
                    _arCabinPosition,
                    _arCabinOffsetOrientation,
                    _arCabinOffsetPosition,
                    _arHeadPosition,
                    _arHeadOffsetOrientation,
                    _arHeadOffsetPosition,
                    out var pose))
            {
                _arCameraPoseValid = false;
                return;
            }

            _arCameraPose = pose;
            _arCameraPoseValid = true;
            _arPoseSequence++;
        }

        private static bool Vector3Changed(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
            => System.Numerics.Vector3.DistanceSquared(a, b) > 1e-8f;

        private static bool EulerChanged(AR.ScsEuler a, AR.ScsEuler b)
            => Math.Abs(a.Heading - b.Heading) > 0.000001 ||
               Math.Abs(a.Pitch - b.Pitch) > 0.000001 ||
               Math.Abs(a.Roll - b.Roll) > 0.000001;

        // Единый парсер placement из любого источника (REST-снимок или WS-дельта).
        // КООРДИНАТЫ TruckTel приходит УЖЕ В МЕТРАХ КАРТЫ (эмпирика 31.08.2026:
        // X=122629.27 Z=-54727.70 напрямую совпадает с дорогами/городами) —
        // НИКАКИХ делителей больше не применяем.
        private void ApplyPlacementJson(JObject json, string source)
        {
            try
            {
                bool changed = false;
                bool anyData = false;

                // ------------------------------------------------------------
                // TRUCK WORLD PLACEMENT
                // ------------------------------------------------------------
                var placement = FlatArray(json, "truck.world.placement");
                if (placement != null && placement.Count >= 6)
                {
                    double tx = placement[0].Value<double>();
                    double ty = placement[1].Value<double>();
                    double tz = placement[2].Value<double>();
                    double th = placement[3].Value<double>();
                    double tp = placement[4].Value<double>();
                    double tr = placement[5].Value<double>();

                    if (double.IsFinite(tx) && double.IsFinite(ty) && double.IsFinite(tz) &&
                        double.IsFinite(th) && double.IsFinite(tp) && double.IsFinite(tr))
                    {
                        changed |= Math.Abs(tx - _arTruckX) > 0.0005;
                        changed |= Math.Abs(ty - _arTruckY) > 0.0005;
                        changed |= Math.Abs(tz - _arTruckZ) > 0.0005;
                        changed |= Math.Abs(th - _arHeading) > 0.000001;
                        changed |= Math.Abs(tp - _arPitch) > 0.000001;
                        changed |= Math.Abs(tr - _arRoll) > 0.000001;

                        _arTruckX = tx;
                        _arTruckY = ty;
                        _arTruckZ = tz;
                        _arHeading = th;
                        _arPitch = tp;
                        _arRoll = tr;

                        _arTruckLastSeen = DateTime.Now;
                        _arTruckKnown = true;
                        anyData = true;
                    }
                }

                // ------------------------------------------------------------
                // SCS HIERARCHY: cabin.position → cabin.offset
                //                 → head.position  → head.offset
                // ------------------------------------------------------------
                if (TryReadVector3(json, "truck.cabin.position", out var cabinPos))
                {
                    changed |= Vector3Changed(_arCabinPosition, cabinPos);
                    _arCabinPosition = cabinPos;
                    anyData = true;
                }

                if (TryReadScsPlacement(json, "truck.cabin.offset",
                        out var cabinOffsetPos, out var cabinOffsetOrientation))
                {
                    changed |= Vector3Changed(_arCabinOffsetPosition, cabinOffsetPos);
                    changed |= EulerChanged(_arCabinOffsetOrientation, cabinOffsetOrientation);
                    _arCabinOffsetPosition = cabinOffsetPos;
                    _arCabinOffsetOrientation = cabinOffsetOrientation;
                    anyData = true;
                }

                if (TryReadVector3(json, "truck.head.position", out var headPos))
                {
                    changed |= Vector3Changed(_arHeadPosition, headPos);
                    _arHeadPosition = headPos;
                    _arHeadPositionKnown = true;
                    anyData = true;
                }

                if (TryReadScsPlacement(json, "truck.head.offset",
                        out var headOffsetPos, out var headOffsetOrientation))
                {
                    changed |= Vector3Changed(_arHeadOffsetPosition, headOffsetPos);
                    changed |= EulerChanged(_arHeadOffsetOrientation, headOffsetOrientation);
                    _arHeadOffsetPosition = headOffsetPos;
                    _arHeadOffsetOrientation = headOffsetOrientation;

                    // raw-массив оставляем для совместимости со старым payload/логом.
                    var rawHead = FlatArray(json, "truck.head.offset");
                    if (rawHead != null) _arLastHead = new JArray(rawHead);
                    anyData = true;
                }

                if (!anyData)
                {
                    // Нет применимых полей (пауза/странный кадр) — 1 строка/5с.
                    if ((DateTime.Now - _arSrcLogAt).TotalMilliseconds > 5000)
                    {
                        _arSrcLogAt = DateTime.Now;
                        Logger.Current?.Data($"[AR] нет применимых telemetry-полей в источнике '{source}'.");
                    }
                    return;
                }

                RebuildArCameraPose();

                // Любое изменение положения/ориентации/головы/кабины = новая поза.
                if (_arCameraPoseValid) changed = true;

                _arTruckChanged = _arTruckChanged || changed;
                _arTruckLastSeen = DateTime.Now;

                if (_arCameraPoseValid) PublishArV2Snapshot();

                // Диагностика позы — в app_data, 1 строка/5с (требование §27).
                if (_arCameraPoseValid && (DateTime.Now - _arSrcOkLogAt).TotalMilliseconds > 5000)
                {
                    _arSrcOkLogAt = DateTime.Now;
                    Logger.Current?.Data(
                        $"[AR] pose '{source}': truck={_arTruckX:F2},{_arTruckY:F2},{_arTruckZ:F2} " +
                        $"camera={_arCameraPose.X:F2},{_arCameraPose.Y:F2},{_arCameraPose.Z:F2} " +
                        $"fwd={_arCameraPose.Forward.X:F3},{_arCameraPose.Forward.Y:F3},{_arCameraPose.Forward.Z:F3} " +
                        $"up={_arCameraPose.Up.X:F3},{_arCameraPose.Up.Y:F3},{_arCameraPose.Up.Z:F3}.");
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[AR] ошибка ApplyPlacementJson('{source}'): {ex.Message}");
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
                // v1.0.40.30: публикуем ТОЛЬКО при валидной позе камеры — до неё
                // рендереру нечего проецировать (CameraPoseValid=false).
                if (!_arTruckKnown || !_arCameraPoseValid) return;

                var s = new AR.ArGameState
                {
                    Sequence = _arPoseSequence,

                    // CamX/Y/Z — ТЕПЕРЬ реальные мировые координаты ГЛАЗА/КАМЕРЫ.
                    CamX = _arCameraPose.X,
                    CamY = _arCameraPose.Y,
                    CamZ = _arCameraPose.Z,

                    CameraForward = _arCameraPose.Forward,
                    CameraRight = _arCameraPose.Right,
                    CameraUp = _arCameraPose.Up,
                    CameraPoseValid = true,

                    // Ориентация фуры/головы — только для диагностики, НЕ для проекции.
                    YawBase = _arHeading,
                    PitchBody = _arPitch,
                    Roll = _arRoll,
                    YawHead = _arHeadOffsetOrientation.Heading,
                    PitchHead = _arHeadOffsetOrientation.Pitch,

                    // Высота reference point грузовика — НЕ камера.
                    GroundY = _arTruckY,
                    PlaneOffsetM = AR.ArBridge.PlaneOffsetM,
                    ShowGrid = AR.ArBridge.ShowGrid
                };

                if (_arPin.HasValue)
                {
                    // v1.0.40.30: pin — обычная точка world space; она БОЛЬШЕ НЕ задаёт
                    // положение камеры (раньше высота «приклеивалась» к плоскости земли
                    // через PlaneOffsetM — эта эвристика убрана из геометрии камеры).
                    var pin = _arPin.Value;
                    s.Pin = (pin.x, pin.y, pin.z);
                }

                // Города — только совместимость старого UI/диагностики;
                // в world-to-screen проекции НЕ участвуют.
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
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/api/ws/delta/flat/?throttle=16"), cts.Token);
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
                    if (c.Enabled && c.Hidden != 1 && c.ShowInAr)
                        list.Add(new ArPoint(c.GameName, c.RealName, c.X, c.Y, c.Z, "city", false, "Город", ""));

                // POI (статика + merged) — без hidden; category = категория оверлея
                var pois = LoadStaticPois();
                foreach (var p in pois.Values)
                    if (p.Enabled && p.Hidden != 1 && p.ShowInAr)
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
                        if (pd.Hidden != 1 && pd.Enabled && pd.ShowInAr)
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
                    // v1.0.40.27: галочка «Показать в AR» из редактора. Нет ключа → считаем
                    // включённой (совместимость со старыми записями); явный false/0 — скрыто.
                    bool showInAr = entry["showInAr"] == null || MapEditorForm.ReadBoolToken(entry["showInAr"], true);
                    bool onCooldown = !string.IsNullOrEmpty(cu) &&
                        DateTime.TryParse(cu, null, DateTimeStyles.RoundtripKind, out var until) &&
                        until > DateTime.UtcNow;

                    if (isTarget)
                    {
                        // Скрытые цели не показываем (статус/кулдаун/галочка AR).
                        if (status == "inactive" || onCooldown || !showInAr) continue;
                        list.Add(new ArPoint(key!, nm, ex, 0, ez, "target", true, string.IsNullOrEmpty(ovrCat) ? "Цель" : ovrCat, ovrColor));
                    }
                    else
                    {
                        if (((int?)entry["hidden"] ?? 0) == 1) continue;
                        if (!showInAr) continue;
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
        // v1.0.40.29: отметка времени последнего лога «жизни тика» (диагностика).
        private DateTime _arTickLogAt = DateTime.MinValue;

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
                // v1.0.40.29 ДИАГНОСТИКА: подтверждение жизни тика (в app_data, 1 строка/5с).
                // Без этого нельзя было отличить «тик не работает» от «условие отправки
                // ложно»: отсутствие ar_telemetry в логе выглядело одинаково.
                if ((DateTime.Now - _arTickLogAt).TotalMilliseconds > 5000)
                {
                    _arTickLogAt = DateTime.Now;
                    Logger.Current?.Data($"[AR] tick alive: known={_arTruckKnown} changed={_arTruckChanged} " +
                        $"forced={_arTelemetryForced} head={( _arLastHead != null)} pts={_arPoints.Count} " +
                        $"x={_arTruckX:F1} z={_arTruckZ:F1}");
                }

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
                //    v1.0.40.27: head может прийти ПОЗЖЕ первого placement (REST-снимок в
                //    первое время отдаёт только world.placement). Раньше условие требовало
                //    _arLastHead != null — телеметрия «зависала» и страница писала «нет
                //    телеметрии», хотя координаты уже были. Теперь head опционален:
                //    страница сама отрисует без головы, а с приходом head ждём его отправки.
                // v1.0.40.28: + _arTelemetryForced — гарантированная отправка по запросу
                // (старт канала / подключение новой страницы), независимо от изменений.
                bool headChanged = _arLastHead != null && !JToken.DeepEquals(_arLastHead, _arLastHeadSent);
                if (_arTruckChanged || _arTelemetryForced || headChanged)
                {
                    var tel = new JObject
                    {
                        ["placement"] = new JArray(_arTruckX, _arTruckY, _arTruckZ, _arHeading, _arPitch, _arRoll),
                        // ============================================================
                        // v1.0.40.30: ГОТОВАЯ МИРОВАЯ 6DoF-ПОЗА КАМЕРЫ (ETS2_AR_CAMERA_POSE).
                        // Страница AR1 НЕ собирает камеру из углов — она получает
                        // position/forward/right/up как есть и проецирует только через них.
                        // Это убирает ВСЕ старые хаки: eyeHeight, складывание питчей,
                        // зеркалирование u и сглаживание экранной координаты.
                        // ============================================================
                        ["camera"] = new JObject
                        {
                            ["position"] = new JArray(_arCameraPose.X, _arCameraPose.Y, _arCameraPose.Z),
                            ["forward"] = new JArray(
                                _arCameraPose.Forward.X, _arCameraPose.Forward.Y, _arCameraPose.Forward.Z),
                            ["right"] = new JArray(
                                _arCameraPose.Right.X, _arCameraPose.Right.Y, _arCameraPose.Right.Z),
                            ["up"] = new JArray(
                                _arCameraPose.Up.X, _arCameraPose.Up.Y, _arCameraPose.Up.Z),
                            ["fovDeg"] = AR.ArBridge.FovDegreesAr1,
                            ["valid"] = _arCameraPoseValid
                        }
                    };
                    // Legacy-поля оставляем для диагностики (проекция их не использует).
                    if (_arLastHead != null) tel["head"] = new JArray(_arLastHead);
                    if (_arPin.HasValue)
                    {
                        tel["pin"] = new JObject { ["x"] = _arPin.Value.x, ["y"] = _arPin.Value.y, ["z"] = _arPin.Value.z };
                    }
                    // Первый пакет (или смена модели городов): города уходят один раз —
                    // без них страница не компенсирует высоту точек с Y=0.
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
                    _arTelemetryForced = false; // v1.0.40.28: форс снят ТОЛЬКО после отправки
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
            const double EyeHeightM = 1.5;   // v40.7: Actros — глаза 2.25 м от полотна − 0.75 (опорная точка)
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
            // v40.6 СНЭП К СЕТКЕ: точка может создаваться ТОЛЬКО в перекрестьях
            // метровой сетки (Math.Round → ближайшее целое).
            px = Math.Round(px);
            pz = Math.Round(pz);
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

        // v1.0.40.28: та же пометка, но и на МИНИКАРТЕ (кружок+крест) — чтобы новая
        // точка, созданная в редакторе карты, появлялась и в AR1, и на миникарте.
        internal void ArSendPinMap(double x, double y, double z)
        {
            try
            {
                SendCommandToMap("ar_pin_map", new JObject
                {
                    ["active"] = true,
                    ["x"] = x, ["y"] = y, ["z"] = z
                });
                AppendLog($"[AR] Новая точка редактора ({x:F0}, {z:F0}) отправлена в AR1 и на миникарту.");
            }
            catch { }
        }
    }
}