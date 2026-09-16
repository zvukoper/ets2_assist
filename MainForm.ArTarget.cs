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
        // ================================================================
        // v1.0.40.34: ЗНАК КРЕНА КУЗОВА.
        // truck.world.placement[5] в телеметрии имеет ЗНАК, ОБРАТНЫЙ официальному
        // примеру SCS `telemetry_position`. ДОКАЗАНО ФИТОМ ПЛОСКОСТИ (v1.0.40.37):
        // при крене кузова на ровной земле верный знак обязан дать ВЕРТИКАЛЬНУЮ
        // нормаль. Проверка на живом кадре (кузов roll=9.187°, лог дал наклон
        // нормали 9.03°):
        //   sign=+1 → наклон нормали 0.00°  <== ВЕРНО
        //   sign=−1 → наклон нормали 18.37° <== НЕВЕРНО (удвоенный наклон)
        // Это независимая проверка: значение 9.03° в логе = |9.187 − 9.187/…|,
        // т.е. знак вычитался вместо сложения. Было −1 — ИСПРАВЛЕНО на +1.
        // ================================================================
        private static double _arTruckRollSign = 1.0;

        // ================================================================
        // v1.0.40.36: РЕЖИМ КРЕНА КАМЕРЫ — вместо угадывания знака.
        //
        // ЧТО НЕ СХОДИТСЯ (живые данные, лог app_workflow):
        //   14:33  кузов roll=-6.81° → крен КАМЕРЫ +9.93°   (крен «перевернулся»)
        //   16:24  кузов roll=-8.05° → крен КАМЕРЫ +0.62°   (крен почти исчез)
        // Второй кадр — это РОВНО режим «крен камеры = 0» (я его включил в v40.35),
        // и пользователь на нём видит, что линия горизонта стала СТРОГО перпендикулярна
        // краю экрана («горизонт в принципе не компенсируется по крену») — то есть
        // ЭТОТ режим неверен. А в первом кадре знак крена был ОБРАТНЫМ настоящему
        // (кузов вниз-вправо ⇒ камера наклоняется вниз-влево, и линия должна уходить
        // низ-СЛЕВА/верх-справа, а не наоборот).
        //
        // ВЫВОД: правильный режим — крен ПЕРЕДАЁТСЯ, но с ИНВЕРСИЕЙ знака
        // (уравнение горизонта выполняется тождественно при roll_cam = −roll_truck).
        // Режим 0 оставлен как «стабилизировано игрой» для быстрой проверки:
        // если игрок действительно стабилизирует крен, пользователь это увидит.
        // ВЫВОД ИЗ НАБЛЮДЕНИЯ ПОЛЬЗОВАТЕЛЯ: на скриншоте горизонт «скренился больше
        // вправо и менее параллелен реальному» при крене кузова ВЛЕВО — это ровно
        // режим 1 (горизонт уходит низ-СПРАВА). Реальный горизонт при кренe остаётся
        // практически горизонтальным ⇒ верный режим — 0.
        // Переключение режимов: Ctrl+Shift+Y (по кругу 0 → 1 → 2 → 0).
        // ================================================================
        private static int _arCameraRollMode = 2;   // 0 = крен 0, 1 = крен с инверсией, 2 = крен как есть

        internal static int ArCameraRollMode => Volatile.Read(ref _arCameraRollMode);

        private static readonly string[] ArCameraRollModeNames =
        {
            "крен камеры = 0 (стабилизация игрой)",
            "крен кузова с ИНВЕРСИЕЙ знака (−roll)",
            "крен кузова как есть (+roll)"
        };

        internal static string ArCameraRollModeName =>
            ArCameraRollModeNames[Math.Clamp(ArCameraRollMode, 0, 2)];

        /// <summary>
        /// Крен, применяемый к базису КАМЕРЫ, по текущему режиму.
        /// </summary>
        private static double ArCameraRollTurn(double truckRollTurn) =>
            ArCameraRollMode switch
            {
                0 => 0.0,
                1 => -truckRollTurn * _arCameraRollFactor,
                _ => truckRollTurn * _arCameraRollFactor
            };

        // ================================================================
        // v1.0.40.38: КОЭФФИЦИЕНТ КОМПЕНСАЦИИ КРЕНА (0..1.5).
        //
        // Пользователь: «Игра НЕ приклеивает камеру строго параллельно плоскости
        // шасси или кабине. Голова компенсируется и стоит ровнее, чем кабина.
        // Поэтому ПОЛНАЯ компенсация питча и ролла грузовика будет некорректной.»
        //
        // НАЙДЕНО ЧИСЛЕННО (живой кадр: кузов roll=+9.56°, pitch=−2.04°):
        // пользователь заметил, что реальный горизонт ПАРАЛЛЕЛЕН оси X шасси, а его
        // экранный наклон = −5.99°. Перебором по кадрам получено:
        //   режим 1 (−roll) + коэффициент 0.7 → наклон горизонта −6.04° (ошибка 0.06°)
        //   0.6 → −5.18° (0.81°), 0.8 → −6.91° (0.92°), 1.0 → −8.64° (2.66°)
        //
        // ⚠️ НО на НЕСКОЛЬКИХ кадрах единого коэффициента НЕ существует (см. WORKLOG
        // v1.0.40.38: 9.19°→0.78, 9.56°→1.20, 16°→0.00, −12°→0.00 с остатками 3…12°),
        // поэтому 0.7 НЕ является вычисленной константой. Значения ниже зафиксированы
        // ПО ПОДБОРУ ПОЛЬЗОВАТЕЛЯ (лучшая видимая картина): режим 2 (+roll), доля 0.5.
        //
        // Подстройка: Ctrl+Shift+J (больше) / Ctrl+Shift+K (меньше), шаг 0.1.
        // Ориентир для точной подгонки — ось X шасси в AR1: горизонт должен стать
        // ей ПАРАЛЛЕЛЕН (и параллелен реальному горизонту на экране).
        // ================================================================
        private static double _arCameraRollFactor = 0.5;

        internal static double ArCameraRollFactor => Volatile.Read(ref _arCameraRollFactor);

        internal static double NudgeArCameraRollFactor(double delta)
        {
            double v = Math.Clamp(ArCameraRollFactor + delta, 0.0, 1.5);
            Volatile.Write(ref _arCameraRollFactor, v);
            return v;
        }

        /// <summary>
        /// v1.0.40.36: переключение РЕЖИМА крена камеры по кругу 1 → 0 → 2 → 1.
        /// Возвращает название нового режима.
        /// </summary>
        internal static string CycleArCameraRollMode()
        {
            int next = ArCameraRollMode switch { 0 => 1, 1 => 2, _ => 0 };
            Volatile.Write(ref _arCameraRollMode, next);
            return ArCameraRollModeName;
        }
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
        // v1.0.40.31 (Ground Plane по колёсам): РЕАЛЬНАЯ ПЛОСКОСТЬ ДОРОГИ.
        // Источник — truck.wheel.position/radius/on_ground/suspension.deflection.
        // Плоскость строится ОДИН раз на приём телеметрии (ArGroundPlane.TryBuild)
        // и далее используется ВСЕЙ геометрией: сетка AR1, создание новой точки
        // (луч × плоскость + snap в узел сетки), дистанция до земли, визуализация
        // высот. Горизонтальная эвристика «truckY + PlaneOffsetM» УБРАНА.
        // ================================================================
        private readonly List<System.Numerics.Vector3> _arWheelPositions = new();
        private readonly List<double> _arWheelRadii = new();
        private readonly List<bool> _arWheelOnGround = new();
        private readonly List<double> _arWheelSuspensionDeflection = new();
        private AR.ArGroundPlane? _arGroundPlane;

        // v1.0.40.37: измеренная (наклонная) плоскость — для оценки уклона/крена.
        /// <summary>v1.0.40.42: интервал фоновой пересборки модели точек (мс).</summary>
        /// <remarks>
        /// Модель — СТАТИКА (меняется только заменой файлов точек), поэтому частый
        /// опрос не нужен. При этом сам SDO уже кэширован (SdoLoader), так что даже
        /// при пересборке повторного парсинга 1.76 МБ не будет.
        /// </remarks>
        private const int ModelRefreshIntervalMs = 15000;

        /// <summary>
        /// v1.0.40.42: ПЕРВИЧНАЯ сборка модели (синхронно, при старте канала) +
        /// дальнейшие пересборки — в фоне. Вызывается один раз при запуске насоса.
        /// </summary>
        private void RefreshArModel()
        {
            try
            {
                var fresh = BuildArModel();
                lock (_arPointsLock) _arPoints = fresh;
                _arModelAt = DateTime.Now;
            }
            catch (Exception ex) { AppendLog($"[AR] Ошибка первичной сборки модели точек: {ex.Message}"); }
        }

        private AR.ArGroundPlane? _arMeasuredGroundPlane;

        // v1.0.40.37: рисовать ЛИ ГОРИЗОНТАЛЬНУЮ плоскость (требование пользователя:
        // плоскость земли всегда параллельна горизонту мира). Ctrl+Shift+U — сравнение.
        private static int _arUseHorizontalPlane = 1;
        internal static bool UseArHorizontalPlane => Volatile.Read(ref _arUseHorizontalPlane) != 0;

        /// <summary>
        /// v1.0.40.37: переключение «плоскость по горизонту» ↔ «плоскость по колёсам».
        /// По умолчанию — ПО ГОРИЗОНТУ (требование пользователя: точка должна ставиться
        /// на плоскости земли при любом положении грузовика).
        /// </summary>
        internal static void ToggleArHorizontalPlane()
            => Volatile.Write(ref _arUseHorizontalPlane, UseArHorizontalPlane ? 0 : 1);

        private bool _arGroundPlaneLogged;
        private DateTime _arGroundPlaneLogAt = DateTime.MinValue;
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

        // ================================================================
        // v1.0.40.42: ПЕРЕСБОРКА МОДЕЛИ ТОЧЕК — В ФОНОВОМ ПОТОКЕ.
        //
        // КОРЕНЬ «точки замирают на 2 секунды каждые 7-8 секунд»:
        // RefreshArModel вызывался из ArUpdateTick (UI-поток) и читал/парсил
        // ~1.9 МБ JSON (города + Overlays + 80 файлов SDO). UI-поток был занят,
        // тики не обрабатывались, точки стояли на месте. Замерено: интервал
        // «tick alive» 5010 мс → 5903 / 6483 / 6558 мс (провал ~1.5 с).
        //
        // Модель НЕ зависит от UI: собираем её в пуле потоков, затем АТОМАРНО
        // подменяем ссылку `_arPoints`. Доступ к списку — только через
        // ArPointsSnapshot() (под lock), чтобы не читать список во время подмены.
        // ================================================================
        private readonly object _arPointsLock = new();
        private int _arModelRebuilding;   // 0/1: не запускать вторую сборку параллельно

        /// <summary>Актуальный снимок модели точек (потокобезопасно).</summary>
        private List<ArPoint> ArPointsSnapshot()
        {
            lock (_arPointsLock) return _arPoints;
        }

        /// <summary>
        /// v1.0.40.42: запустить пересборку модели в фоновом потоке (если ещё не идёт).
        /// </summary>
        private void QueueArModelRebuild()
        {
            if (Interlocked.CompareExchange(ref _arModelRebuilding, 1, 0) != 0) return;
            Task.Run(() =>
            {
                try
                {
                    var fresh = BuildArModel();          // тяжёлое чтение JSON — НЕ на UI
                    lock (_arPointsLock) _arPoints = fresh;
                    _arModelAt = DateTime.Now;
                }
                catch (Exception ex)
                {
                    Logger.Current?.Data($"[AR] Ошибка фоновой пересборки модели точек: {ex.Message}");
                    _arModelAt = DateTime.Now;           // не крутить попытки каждые 16 мс
                }
                finally
                {
                    Volatile.Write(ref _arModelRebuilding, 0);
                }
            });
        }
        // Подпись последней модели (имя+координаты) — для «слать только при изменении».
        private string? _arModelSig;
        // v74: cities отдаём в ПЕРВОЙ телеметрии (список у страницы дальше уже есть).
        private bool _arCitiesSent;
        // ================================================================
        // v1.0.40.40: КОМПЕНСАЦИЯ ВЫСОТЫ ГОРОДОВ УБРАНА.
        // Здесь была константа −44 м: приложение отправляло города «уже
        // скомпенсированными», из-за чего компании/города с ТЕМИ ЖЕ координатами
        // висели НИЖЕ метки новой точки на эти 44 м. Причина компенсации
        // (согласование с прежним AR1/миникартой) больше не существует: сейчас
        // высота точки — это её РЕАЛЬНАЯ мировая Y, и она сравнивается напрямую.
        // ⛔ НЕ возвращать никакие поправки высоты по типу точки: все точки всех
        // типов должны отображаться на своей настоящей высоте.
        // ================================================================

        // РАЗОВАЯ рассылка ar_target (фидбек 31.08.2026: точки статичны, слать
        // постоянно бессмысленно). Отправляем ТОЛЬКО при СМЕНЕ выбранной цели
        // (или после рестарта страницы/канала).
        private string? _arLastSentGameName;
        private bool _arTargetMustClear; // прошлый tick: цели не было (нужно разово сказать null)
        private DateTime _arLastTargetSentAt = DateTime.MinValue;   // v93: дебаунс спама ar_target

        // ================================================================
        // v1.0.40.40: РАДИУС ПОКАЗА ТОЧЕК В АР.
        // Требование пользователя: «Отображаем в АР все точки в радиусе 50 м».
        // Заменяет прежний выбор ОДНОЙ ближайшей точки в радиусе 1.5 км.
        // Значение живёт в AppSettings (меняется в меню «Вид»).
        // ================================================================
        internal static double ArDisplayRadiusM => AppSettings.ArDisplayRadiusM;

        /// <summary>
        /// v1.0.40.40: сбросить подпись последнего набора точек — чтобы новый
        /// радиус/состав применился немедленно, а не на следующем изменении.
        /// </summary>
        internal static void ArResetNearSignature() => _arResetNearSig = true;
        private static volatile bool _arResetNearSig;

        // Подпись последнего отправленного НАБОРА точек (gameName через ';') —
        // чтобы не слать相同的 список каждый тик (событийная модель).
        private string? _arLastNearSig;

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
            // v1.0.40.31: НАСОС ТЕЛЕМЕТРИИ (WS + REST + тик + плоскость дороги)
            // запускается идемпотентно и НЕ зависит от AR1: окно визуализации высот
            // открыто всегда, значит телеметрия нужна и без нажатия «Запустить AR».
            EnsureArTelemetryPump(alwaysOn: false);
            // v1.0.40.32: вертикальный FOV — авто-вывод из горизонтального (если
            // пользователь не задал вручную). Держать в синхроне с FOV обязательно:
            // рассинхрон как раз и давал «уплывающий» горизонт.
            SyncAr1VerticalFov();
            // v1.0.40.34: диагностика наклона — сразу после старта канала и далее
            // раз в 5 с (см. ArUpdateTick). Нужна для разбора расхождения
            // нарисованного горизонта с реальным: сравнение углов КУЗОВА, КАБИНЫ,
            // ГОЛОВЫ и реальной позы КАМЕРЫ. Без этих цифр причина недоказуема.
            LogArTiltDiagnostics("start");
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

            if (_arReconnectTimer == null)
            {
                _arReconnectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _arReconnectTimer.Tick += (_, _) => { _arReconnectTimer!.Stop(); _ = ArConnectTelemetryAsync(); };
            }
            _arReconnectTimer.Start();
            _ = ArConnectTelemetryAsync();
            // v1.0.40.27: страница получает актуальный FOV AR1 сразу (CTRL+PGUP/PGDN его меняет).
            SendAr1FovToPage();
            // v1.0.40.40: и актуальные настройки вида (сетка/оси/горизонты/радиус).
            SendArViewToPage();

            // v1.0.40.31: разовая самопроверка расчёта плоскости дороги
            // (7 сценариев из задания) — результат в app_data.log.
            EnsureArGroundPlaneSelfTest();

            AppendLog("[AR] Канал AR-целей запущен (REST-снимок + WS-дельта телеметрии, подбор ближайшей точки на C#).");
        }

        // ================================================================
        // v1.0.40.31: НАСОС ТЕЛЕМЕТРИИ AR (идемпотентный).
        // Запускает тик, WS-дельту и REST-снимок ОДИН раз, независимо от того,
        // кто первым попросил данные: канал AR1 или окно визуализации высот
        // (оно открыто ВСЕГДА — значит телеметрия нужна и без нажатия кнопки AR).
        // ================================================================
        private bool _arPumpRunning;
        // v1.0.40.31: насос поднят на ПОСТОЯННОЙ основе (окно визуализации высот
        // открыто всегда) — тогда остановка AR1 не имеет права гасить телеметрию.
        private bool _arPumpAlwaysOn;

        internal void EnsureArTelemetryPump(bool alwaysOn = true)
        {
            if (_arPumpRunning)
            {
                if (alwaysOn) _arPumpAlwaysOn = true;
                return;
            }
            _arPumpRunning = true;
            if (alwaysOn) _arPumpAlwaysOn = true;

            // v1.0.40.29 КОРЕНЬ «в AR1 вообще ничего не меняется» (второй дефект):
            // System.Windows.Forms.Timer для AR-тика НЕ тикал (в логе за сессии 19:20 и
            // 20:54 — НИ ОДНОЙ записи тика). Тот же баг уже ловили в MapEditor2Form и
            // лечили сменой на System.Threading.Timer. Делаем так же: потоковый таймер
            // не зависит от очереди сообщений WinForms, работа с UI — через BeginInvoke.
            StartArTickTimer();

            if (_arReconnectTimer == null)
            {
                _arReconnectTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _arReconnectTimer.Tick += (_, _) => { _arReconnectTimer!.Stop(); _ = ArConnectTelemetryAsync(); };
            }
            _arReconnectTimer.Start();
            _ = ArConnectTelemetryAsync();

            // v1.0.40.31: разовая самопроверка расчёта плоскости дороги
            // (7 сценариев из задания) — результат в app_data.log. Живёт здесь,
            // а не в StartArTargetFeed, чтобы выполняться при ЛЮБОМ старте насоса
            // (окно визуализации высот открыто всегда, без нажатия кнопки AR).
            EnsureArGroundPlaneSelfTest();

            // REST-снимок: TruckTel /api/rest/flat/truck ОТДАЁТ truck.world.placement —
            // подтверждено 31.08.2026 (в метрах карты, работает и на паузе).
            // WS-дельта на паузе truck.* НЕ шлёт, поэтому REST — основной источник
            // на паузе, WS — «горячий» поток в движении.
            if (_arRestTask == null)
            {
                _arRestCts = new CancellationTokenSource();
                _arRestTask = ArRestLoopAsync(_arRestCts.Token);
            }

            Logger.Current?.Data("[AR] Насос телеметрии запущен (тик + WS-дельта + REST-снимок).");
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

        internal void StopArTelemetryPumpForShutdown()
        {
            _arPumpAlwaysOn = false;   // снимаем защиту окна высот
            if (!_arPumpRunning) return;
            StopArTickTimer();
            _arReconnectTimer?.Stop();
            try { _arCts?.Cancel(); } catch { }
            try { _arRestCts?.Cancel(); } catch { }
            try { _arWs?.Dispose(); } catch { }
            _arWs = null;
            _arRestTask = null;
            _arPumpRunning = false;
        }

        internal void StopArTargetFeed()
        {
            // v1.0.40.31: окно визуализации высот открыто ВСЕГДА и живёт на телеметрии
            // (плоскость дороги по колёсам). Если оно уже открыто (насос поднят при
            // старте системы), остановка AR1 НЕ должна его обесточивать — иначе окно
            // высот навсегда пишет «нет плоскости дороги».
            if (_arPumpAlwaysOn)
            {
                _arTruckChanged = false;
                _arTelemetryForced = false;
                SendCommandToMap("ar_target", new JObject
                {
                    ["hasTarget"] = false,
                    ["reason"] = "AR остановлен (телеметрия высот продолжает работать)"
                });
                _arLastSentGameName = null;
                _arTargetMustClear = false;
                AppendLog("[AR] Оверлей AR остановлен, канал телеметрии оставлен для окна высот.");
                return;
            }

            StopArTickTimer();
            _arReconnectTimer?.Stop();
            try { _arCts?.Cancel(); } catch { }
            try { _arRestCts?.Cancel(); } catch { }
            try { _arWs?.Dispose(); } catch { }
            _arWs = null;
            _arRestTask = null;
            _arPumpRunning = false;      // v1.0.40.31: насос можно запустить заново
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
            SyncAr1VerticalFov();
            SendAr1FovToPage();
            // Смена FOV не должна спамить workflow при автоповторе — подробности в app_data.
            Logger.Current?.Data($"[AR] FOV AR1 = {clamped:F1}° горизонтальный, " +
                $"вертикальный = {AR.ArBridge.FovDegreesAr1Vertical:F1}° ({source})");
        }

        // ================================================================
        // v1.0.40.32: ВЕРТИКАЛЬНЫЙ FOV AR1 — КОРЕНЬ «ГОРИЗОНТ/МЕТКА УПЛЫВАЮТ».
        //
        // Геометрия ошибки: проекция считала ОДИН focal length по X и Y. Это верно
        // ТОЛЬКО если вертикальный FOV строго выводится из горизонтального через
        // aspect. В ETS2 вертикаль и горизонталь масштабируются НЕЗАВИСИМО
        // (в config.cfg есть отдельные r_multimon_fov_vertical / _horizontal),
        // поэтому реальная вертикаль уже ≈ на 10…15°. Ошибка Δf даёт смещение
        // Δv ≈ Δf·tan(наклона головы): ноль на горизонте и рост с наклоном —
        // ровно то, что наблюдал пользователь.
        //
        // Вертикальный FOV выводится из горизонтального с типичным для ETS2
        // сжатием по вертикали, пока не задан вручную (Ar1VerticalFovManual).
        // ================================================================
        internal void SyncAr1VerticalFov()
        {
            // v1.0.40.34: ВЕРТИКАЛЬНЫЙ FOV — НЕЗАВИСИМАЯ настройка, а НЕ производная
            // от горизонтального. Пользователь подбирает его глазами по совпадению
            // линии горизонта (Ctrl+Shift+PGUP/PGDN, шаг 0.2°) — значение живёт в
            // AppSettings.Ar1FovVerticalDeg. Вывод из горизонтали через aspect убран:
            // он давал ≈59.6° при hFov 91°, и при смене горизонтального FOV вертикаль
            // тайно переезжала, сбрасывая калибровку.
            // Метод остался точкой привязки: при старте канала/страницы значение
            // применяется из настроек.
            double v = AppSettings.Ar1FovVerticalDeg;
            // v1.0.40.41: fallback приведён к 65 (значение по умолчанию), было 64.5 —
            // расхождение с AppSettings.Ar1FovVerticalDeg и меню «Настройки АР».
            if (!(v >= 10.0 && v <= 150.0)) v = 65.0;
            AR.ArBridge.FovDegreesAr1Vertical = v;
            AR.ArBridge.Ar1VerticalFovManual = true;
        }

        // Ручная подстройка вертикального FOV (шаг 0.2°, Ctrl+Shift+PGUP/PGDN).
        // v1.0.40.33: SHIFT+CTRL вместо CTRL+ALT — Alt-комбинации перехватывает
        // Windows (системное меню / Alt+Tab), поэтому RegisterHotKey для них не срабатывает.
        // v1.0.40.34: ШАГ 0.2° (было 0.5°) — требование пользователя для точной
        // подгонки горизонта. Значение СОХРАНЯЕТСЯ в настройках (раньше терялось
        // при перезапуске — калибровку приходилось делать заново).
        internal void NudgeAr1VerticalFov(double deltaDeg, string source)
        {
            double v = Math.Clamp(AR.ArBridge.FovDegreesAr1Vertical + deltaDeg, 10.0, 150.0);
            AR.ArBridge.FovDegreesAr1Vertical = v;
            AR.ArBridge.Ar1VerticalFovManual = true;
            try { AppSettings.Ar1FovVerticalDeg = v; AppSettings.Save(); } catch { }
            SendAr1FovToPage();
            AppendLog($"[AR] Вертикальный FOV AR1 = {v:F1}° ({source}); горизонт={AR.ArBridge.FovDegreesAr1:F0}°");
        }

        // ================================================================
        // v1.0.40.34: ДИАГНОСТИКА НАКЛОНА — какие углы РЕАЛЬНО даёт телеметрия.
        // Нужна, чтобы не гадать о причине расхождения нарисованного горизонта
        // с реальным: сравниваем три независимых источника:
        //   1) кузов: truck.world.placement[4]/[5]  (pitch/roll кузова, ×360°)
        //   2) камера: CameraForward.Y / CameraRight.Y (реальный наклон взгляда и крена)
        //   3) кабина: truck.cabin.offset[4]/[5]   (п.); если кабина ВРАЩАЕТСЯ при
        //      крене кузова (маятник), то рисовать горизонт от кузова НЕЛЬЗЯ.
        // ================================================================
        internal void LogArTiltDiagnostics(string source)
        {
            try
            {
                double truckPitchDeg = _arPitch * 360.0;
                double truckRollDeg = _arRoll * 360.0;
                double headPitchDeg = _arHeadOffsetOrientation.Pitch * 360.0;
                double headRollDeg = _arHeadOffsetOrientation.Roll * 360.0;
                double cabinPitchDeg = _arCabinOffsetOrientation.Pitch * 360.0;
                double cabinRollDeg = _arCabinOffsetOrientation.Roll * 360.0;

                double camNoseDownDeg = Math.Asin(Math.Clamp(-_arCameraPose.Forward.Y, -1.0, 1.0)) * 180.0 / Math.PI;
                double camRollDeg = Math.Asin(Math.Clamp(Math.Abs(_arCameraPose.Forward.Y) < 0.9999
                    ? -_arCameraPose.Right.Y / Math.Sqrt(Math.Max(1e-9, 1.0 - _arCameraPose.Forward.Y * _arCameraPose.Forward.Y))
                    : 0.0, -1.0, 1.0)) * 180.0 / Math.PI;

                double truckPitchFromFwdDeg = Math.Asin(Math.Clamp(-_arCameraPose.Forward.Y, -1.0, 1.0)) * 180.0 / Math.PI;

                // ---- ГЕОМЕТРИЯ ЛИНИИ ГОРИЗОНТА, которую РИСУЕТ страница ----
                // Та же формула, что и в ar_hud.js drawWorldHorizon:
                //   y = cy + fv·(Forward.Y + Right.Y·x)/Up.Y,  x = (px − cx)/fh
                // Печатаем предсказанную высоту линии у левого/правого края и её
                // наклон — чтобы сравнить с тем, что видно на экране.
                double hFov = AR.ArBridge.FovDegreesAr1;
                double vFov = AR.ArBridge.FovDegreesAr1Vertical;
                int sw = 1920, sh = 1080;
                try
                {
                    var scr = GetGameScreen();
                    if (scr != null && scr.Bounds.Width > 0) { sw = scr.Bounds.Width; sh = scr.Bounds.Height; }
                }
                catch { }
                double fh = (sw * 0.5) / Math.Tan(hFov * Math.PI / 180.0 * 0.5);
                double fv = (sh * 0.5) / Math.Tan(vFov * Math.PI / 180.0 * 0.5);
                double cx = sw * 0.5, cy = sh * 0.5;
                double fy2 = _arCameraPose.Forward.Y, ry2 = _arCameraPose.Right.Y, uy2 = _arCameraPose.Up.Y;
                double hy0 = double.NaN, hy1 = double.NaN, horizonTiltDeg = double.NaN, hyCentre = double.NaN;
                if (Math.Abs(uy2) > 1e-7)
                {
                    hy0 = cy + fv * (fy2 + ry2 * ((0 - cx) / fh)) / uy2;
                    hy1 = cy + fv * (fy2 + ry2 * ((sw - cx) / fh)) / uy2;
                    hyCentre = cy + fv * fy2 / uy2;
                    horizonTiltDeg = Math.Atan2(hy1 - hy0, sw) * 180.0 / Math.PI;
                }

                AppendLog(
                    $"[AR] TILT ({source}): кузов pitch={truckPitchDeg:F2}° roll={truckRollDeg:F2}° | " +
                    $"голова pitch={headPitchDeg:F2}° roll={headRollDeg:F2}° | " +
                    $"кабина pitch={cabinPitchDeg:F2}° roll={cabinRollDeg:F2}° | " +
                    $"КАМЕРА наклон={camNoseDownDeg:F2}° крен={camRollDeg:F2}° | " +
                    $"fwd.Y={_arCameraPose.Forward.Y:F4} right.Y={_arCameraPose.Right.Y:F4} up.Y={_arCameraPose.Up.Y:F4}");

                AppendLog(
                    $"[AR] HORIZON-GEOM: {sw}x{sh} hFov={hFov:F1}° vFov={vFov:F1}° fh={fh:F0} fv={fv:F0} " +
                    $"→ y(лево)={hy0:F0} y(центр)={hyCentre:F0} y(право)={hy1:F0} " +
                    $"наклон линии={horizonTiltDeg:F2}° (центр экрана cy={cy:F0})");
            }
            catch { }
        }

        // Отправка текущих FOV странице AR1 (при старте канала/страницы).
        private void SendAr1FovToPage()
        {
            SendCommandToMap("ar_fov", new JObject
            {
                ["fov"] = AR.ArBridge.FovDegreesAr1,
                ["fovVertical"] = AR.ArBridge.FovDegreesAr1Vertical
            });
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

            // v1.0.40.36: крен камеры — по РЕЖИМУ (Ctrl+Shift+Y переключает).
            // Мировой горизонт (эта линия) строится из базиса; горизонт РЕАЛЬНОЙ
            // ПЛОСКОСТИ полотна страница рисует отдельно по groundPlane — их совпадение
            // и есть критерий правильности режима.
            if (!AR.ScsCameraPose.TryCreate(
                    _arTruckX, _arTruckY, _arTruckZ,
                    new AR.ScsEuler(_arHeading, _arPitch, ArCameraRollTurn(_arRoll)),
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

        // ================================================================
        // v1.0.40.31: ЧТЕНИЕ ДАННЫХ КОЛЁС (Ground Plane по колёсам).
        // TruckTel отдаёт (проверено на живом REST-кадре):
        //   truck.wheels.count                 = 4
        //   truck.wheel.position               = [[x,y,z],[x,y,z],…]
        //   truck.wheel.radius                 = [r,…]
        //   truck.wheel.on_ground              = [true,…]
        //   truck.wheel.suspension.deflection  = [d,…]
        // Дополнительно поддерживаем «indexed» форму WS-дельты
        // (truck.wheel.position.0 / .1 / …), которая встречается в delta-пакетах.
        // Все числа — через Value<double>() (культуро-независимо, урок v64).
        // ================================================================
        private bool TryReadWheelTelemetry(JObject json)
        {
            var posArr = FlatArray(json, "truck.wheel.position");
            if (posArr == null || posArr.Count == 0) return false;

            int count = json["truck.wheels.count"]?.Value<int>() ?? posArr.Count;
            if (count <= 0 || count > 32) count = posArr.Count;

            var pos = new List<System.Numerics.Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                System.Numerics.Vector3 v;

                // Форма A: позиция — вложенный массив [[x,y,z],…].
                if (i < posArr.Count && posArr[i] is JArray inner && inner.Count >= 3)
                {
                    if (!TryReadVec3Array(inner, out v)) return false;
                }
                // Форма B: indexed-ключи truck.wheel.position.N (WS-дельта).
                else if (FlatArray(json, $"truck.wheel.position.{i}") is JArray indexed && indexed.Count >= 3)
                {
                    if (!TryReadVec3Array(indexed, out v)) return false;
                }
                else
                {
                    break;
                }

                pos.Add(v);
            }

            if (pos.Count < 3) return false;

            var radii = new List<double>(pos.Count);
            var ground = new List<bool>(pos.Count);
            var susp = new List<double>(pos.Count);

            var radArr = FlatArray(json, "truck.wheel.radius");
            var grArr = FlatArray(json, "truck.wheel.on_ground");
            var supArr = FlatArray(json, "truck.wheel.suspension.deflection");

            for (int i = 0; i < pos.Count; i++)
            {
                double r = 0.506;
                if (radArr != null && i < radArr.Count)
                {
                    try { double v = radArr[i].Value<double>(); if (double.IsFinite(v) && v > 0.05 && v < 2.0) r = v; }
                    catch { }
                }
                radii.Add(r);

                bool g = true;
                if (grArr != null && i < grArr.Count)
                {
                    try { g = grArr[i].Value<bool>(); }
                    catch { try { g = grArr[i].Value<int>() != 0; } catch { } }
                }
                ground.Add(g);

                double d = 0.0;
                if (supArr != null && i < supArr.Count)
                {
                    try { double v = supArr[i].Value<double>(); if (double.IsFinite(v) && Math.Abs(v) < 2.0) d = v; }
                    catch { }
                }
                susp.Add(d);
            }

            bool changed = pos.Count != _arWheelPositions.Count;
            if (!changed)
            {
                for (int i = 0; i < pos.Count; i++)
                {
                    if (Vector3Changed(pos[i], _arWheelPositions[i]) ||
                        Math.Abs(radii[i] - _arWheelRadii[i]) > 0.0005 ||
                        ground[i] != _arWheelOnGround[i] ||
                        Math.Abs(susp[i] - _arWheelSuspensionDeflection[i]) > 0.0005)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            _arWheelPositions.Clear(); _arWheelPositions.AddRange(pos);
            _arWheelRadii.Clear(); _arWheelRadii.AddRange(radii);
            _arWheelOnGround.Clear(); _arWheelOnGround.AddRange(ground);
            _arWheelSuspensionDeflection.Clear(); _arWheelSuspensionDeflection.AddRange(susp);

            return changed;
        }

        private static bool TryReadVec3Array(JArray a, out System.Numerics.Vector3 value)
        {
            value = System.Numerics.Vector3.Zero;
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

        // Пересчёт плоскости дороги по последним данным колёс.
        // Вызывается ТОЛЬКО когда есть и placement, и колёса (ориентация кузова нужна
        // для перевода колёс в мировую систему).
        private void RebuildArGroundPlane()
        {
            if (!_arTruckKnown || _arWheelPositions.Count < AR.ArGroundPlane.MinWheelsOnGround)
            {
                _arGroundPlane = null;
                return;
            }

            // ВНИМАНИЕ: в отличие от КАМЕРЫ, здесь крен кузова ПРИМЕНЯЕТСЯ —
            // колёса жёстко связаны с кузовом, их пятна контакта наклоняются
            // вместе с ним, значит и плоскость дороги тоже (это разные величины,
            // а не одна на два места).
            // v1.0.40.37: знак крена кузова ПРОВЕРЕН ФИТОМ ПЛОСКОСТИ (см. выше;
            // верный = +1: при крене на ровной земле нормаль должна быть вертикальной).
            if (!AR.ArGroundPlane.TryBuild(
                    _arTruckX, _arTruckY, _arTruckZ,
                    new AR.ScsEuler(_arHeading, _arPitch, _arRoll * _arTruckRollSign),
                    _arWheelPositions,
                    _arWheelRadii,
                    _arWheelOnGround,
                    _arWheelSuspensionDeflection,
                    out var groundPlane))
            {
                _arGroundPlane = null;
                if ((DateTime.Now - _arGroundPlaneLogAt).TotalMilliseconds > 5000)
                {
                    _arGroundPlaneLogAt = DateTime.Now;
                    Logger.Current?.Data($"[AR] ground plane: НЕ построена " +
                        $"(колёс={_arWheelPositions.Count}, на земле={_arWheelOnGround.Count(g => g)}).");
                }
                return;
            }

            // v1.0.40.37: оси шасси (локальные XZ кузова) — для отрисовки ориентации
            // грузовика отдельно от горизонта/плоскости.
            RebuildChassisAxes();

            // ================================================================
            // v1.0.40.37: ДЛЯ ВИЗУАЛИЗАЦИИ БЕРЁМ ГОРИЗОНТАЛЬНУЮ ПЛОСКОСТЬ.
            // Требование пользователя: сетка и плоскость ВСЕГДА параллельны
            // горизонту мира, чтобы точку можно было поставить строго на
            // плоскости земли при ЛЮБОМ положении грузовика (с креном на
            // обочине, носом вниз). Ориентация грузовика показывается отдельно —
            // осями шасси (локальные XZ).
            // Измеренная (наклонная) плоскость сохраняется в _arMeasuredGroundPlane
            // для оценки уклона; в _arGroundPlane лежит горизонтальная.
            // ================================================================
            _arMeasuredGroundPlane = groundPlane;

            if (UseArHorizontalPlane)
            {
                _arGroundPlane = AR.ArGroundPlane.CreateHorizontal(groundPlane);
            }
            else
            {
                _arGroundPlane = groundPlane;
            }
            var effective = _arGroundPlane;
            if (!_arGroundPlaneLogged || (DateTime.Now - _arGroundPlaneLogAt).TotalMilliseconds > 5000)
            {
                _arGroundPlaneLogged = true;
                _arGroundPlaneLogAt = DateTime.Now;
                Logger.Current?.Data($"[AR] ground plane: {effective.Summary()}" +
                    $" | режим={(UseArHorizontalPlane ? "ГОРИЗОНТ (виз.)" : "по колёсам")}" +
                    $" | измеренный уклон={groundPlane.MaxResidual * 1000:F0} мм");
            }
        }

        // Разовая самопроверка расчёта плоскости (требование задания: тесты на
        // горизонтальную/продольную/поперечную/диагональную плоскость, поворот
        // фуры, поднятое колесо и snap по наклонной плоскости).
        private static int _arGroundPlaneSelfTested;

        internal static void EnsureArGroundPlaneSelfTest()
        {
            if (Interlocked.Exchange(ref _arGroundPlaneSelfTested, 1) != 0) return;
            try
            {
                AR.ArGroundPlane.RunSelfTests(msg => Logger.Current?.Data(msg));
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[AR] ground plane self-test error: {ex.Message}");
            }
        }

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

                // ------------------------------------------------------------
                // v1.0.40.31: ДАННЫЕ КОЛЁС → РЕАЛЬНАЯ ПЛОСКОСТЬ ДОРОГИ
                // (Ground Plane по колёсам). Ключи: truck.wheel.position/radius/
                // on_ground/suspension.deflection (+ truck.wheels.count).
                // ------------------------------------------------------------
                if (TryReadWheelTelemetry(json))
                {
                    changed = true;
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
                RebuildArGroundPlane();

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
                        $"up={_arCameraPose.Up.X:F3},{_arCameraPose.Up.Y:F3},{_arCameraPose.Up.Z:F3} " +
                        $"ground={(Math.Abs(_arGroundPlane?.Normal.Y ?? 0f) > 0 ? _arGroundPlane!.ReferenceHeight.ToString("F2") : "нет")}.");
                }

                // v1.0.40.34: ДИАГНОСТИКА НАКЛОНА — 1 строка/5 с. Нужна для разбора
                // расхождения нарисованного горизонта с реальным.
                if (_arCameraPoseValid && (DateTime.Now - _arTiltLogAt).TotalMilliseconds > 5000)
                {
                    _arTiltLogAt = DateTime.Now;
                    LogArTiltDiagnostics(source);
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

                    // Высота reference point грузовика — НЕ камера. Оставлено для
                    // обратной совместимости; реальная земля — GroundPlane.
                    GroundY = _arTruckY,
                    PlaneOffsetM = AR.ArBridge.PlaneOffsetM,
                    ShowGrid = AR.ArBridge.ShowGrid,

                    // v1.0.40.31: реальная плоскость дороги по колёсам.
                    GroundPlane = _arGroundPlane
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
                var ptsForCities = ArPointsSnapshot();
                if (ptsForCities.Count > 0)
                {
                    var cities = new List<(double, double, double)>();
                    foreach (var it in ptsForCities)
                    {
                        if (it.kind != "city" || Math.Abs(it.y) < 0.001) continue;
                        double d2 = (it.x - _arTruckX) * (it.x - _arTruckX) + (it.z - _arTruckZ) * (it.z - _arTruckZ);
                        if (d2 > 5000.0 * 5000.0) continue;
                        // v1.0.40.40: БЕЗ компенсации — реальная высота точки.
                        cities.Add((it.x, it.y, it.z));
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

        // ================================================================
        // v1.0.40.31: ПЛОСКОСТЬ ДОРОГИ В PAYLOAD `ar_telemetry`.
        // Формат (по заданию «Ground Plane по колёсам»):
        //   { valid, origin:[x,y,z], normal:[nx,ny,nz], axisU:[…], axisV:[…],
        //     averageWheelHeight, referenceHeight, maxResidual, wheels:[…] }
        // Массив wheels НЕ удаляем — он нужен для диагностики и построения
        // реальной геометрии (в т.ч. новым окном визуализации высот).
        // ================================================================
        private JObject BuildGroundPlanePayload()
        {
            var gp = _arGroundPlane;
            if (gp == null || !gp.Valid)
                return new JObject { ["valid"] = false };

            var wheels = new JArray();
            foreach (var w in gp.Wheels)
            {
                wheels.Add(new JObject
                {
                    ["index"] = w.Index,
                    ["onGround"] = w.OnGround,
                    ["radius"] = w.Radius,
                    ["suspensionDeflection"] = w.SuspensionDeflection,
                    ["localPosition"] = new JArray(w.LocalPosition.X, w.LocalPosition.Y, w.LocalPosition.Z),
                    ["worldCenter"] = new JArray(w.WorldCenter.X, w.WorldCenter.Y, w.WorldCenter.Z),
                    ["contact"] = new JArray(w.Contact.X, w.Contact.Y, w.Contact.Z),
                    ["residual"] = w.Residual,
                    ["longitudinalM"] = w.LongitudinalM
                });
            }

            return new JObject
            {
                ["valid"] = true,
                ["origin"] = new JArray(gp.OriginX, gp.OriginY, gp.OriginZ),
                ["normal"] = new JArray(gp.Normal.X, gp.Normal.Y, gp.Normal.Z),
                ["axisU"] = new JArray(gp.AxisU.X, gp.AxisU.Y, gp.AxisU.Z),
                ["axisV"] = new JArray(gp.AxisV.X, gp.AxisV.Y, gp.AxisV.Z),
                ["averageWheelHeight"] = gp.AverageWheelHeight,
                ["referenceHeight"] = gp.ReferenceHeight,
                ["maxResidual"] = gp.MaxResidual,
                ["usedWheelCount"] = gp.UsedWheelCount,
                ["isHorizontal"] = gp.IsHorizontal,
                // ============================================================
                // v1.0.40.37: ОСИ ШАССИ ГРУЗОВИКА (локальные X/Z кузова в мире).
                // Требование пользователя: ориентацию грузовика относительно земли и
                // горизонта показывать ОТДЕЛЬНО — двумя линиями в центре экрана
                // (вертикальной и горизонтальной), которые НЕ параллельны краям
                // экрана, а параллельны осям шасси (локальным XZ плоскости шасси).
                // Считаются от ОРИЕНТАЦИИ КУЗОВА (не камеры!), поэтому крен/питч
                // грузовика виден даже когда камера компенсирована.
                // ============================================================
                ["chassisForward"] = new JArray(ChassisForwardX, ChassisForwardY, ChassisForwardZ),
                ["chassisRight"] = new JArray(ChassisRightX, ChassisRightY, ChassisRightZ),
                ["chassisUp"] = new JArray(ChassisUpX, ChassisUpY, ChassisUpZ),
                ["truckPitchDeg"] = _arPitch * 360.0,
                ["truckRollDeg"] = _arRoll * 360.0,
                ["wheels"] = wheels
            };
        }

        // ================================================================
        // v1.0.40.37: ОСИ ШАССИ В МИРЕ (локальные X/Z кузова).
        // Считаются напрямую из углов КУЗОВА через ту же SCS-композицию, что и
        // поза камеры: forward = (0,0,−1), right = (1,0,0), up = (0,1,0),
        // повёрнутые на heading/pitch/roll КУЗОВА.
        // ================================================================
        private double ChassisForwardX, ChassisForwardY, ChassisForwardZ;
        private double ChassisRightX, ChassisRightY, ChassisRightZ;
        private double ChassisUpX, ChassisUpY, ChassisUpZ;

        private void RebuildChassisAxes()
        {
            var o = new AR.ScsEuler(_arHeading, _arPitch, _arRoll * _arTruckRollSign);
            var f = AR.ScsCameraPose.Rotate(new System.Numerics.Vector3(0f, 0f, -1f), o);
            var r = AR.ScsCameraPose.Rotate(new System.Numerics.Vector3(1f, 0f, 0f), o);
            var u = AR.ScsCameraPose.Rotate(new System.Numerics.Vector3(0f, 1f, 0f), o);

            ChassisForwardX = f.X; ChassisForwardY = f.Y; ChassisForwardZ = f.Z;
            ChassisRightX = r.X; ChassisRightY = r.Y; ChassisRightZ = r.Z;
            ChassisUpX = u.X; ChassisUpY = u.Y; ChassisUpZ = u.Z;
        }

        // ================================================================
        // v1.0.40.31: PAYLOAD ДЛЯ ОКНА ВИЗУАЛИЗАЦИИ ВЫСОТ (`heights`).
        //
        // Окно рисует БОКОВУЮ проекцию (вид сбоку, грузовик условен):
        //   • горизонтальная белая линия  — плоскость «земли» по БЛИЖАЙШЕЙ
        //     известной высоте (высота плоскости под опорной точкой фуры);
        //   • две красные окружности      — два колеса сбоку; их НИЖНИЕ точки
        //     лежат на этой плоскости (именно той, что даёт ArGroundPlane);
        //   • полупрозрачный оранжевый конус — ВЕРТИКАЛЬНЫЙ угол обзора камеры,
        //     вершина — в позиции головы-камеры, угол = вертикальный FOV
        //     (вычисляется из ГОРИЗОНТАЛЬНОГО по пропорции экрана 16:9);
        //   • белая прицельная линия из вершины через центр конуса, упирается в
        //     плоскость; длина (дистанция камера→земля) печатается над меткой.
        //
        // Масштаб визуализации (задание): 1 м за задним колесом, расстояние между
        // колёсами — по телеметрии (продольные координаты), от камеры до КРАЯ
        // визуализации 40 м. Все величины — в МЕТРАХ системы фуры.
        // ================================================================
        private JObject BuildHeightsPayload()
        {
            var res = new JObject { ["valid"] = false };

            var gp = _arGroundPlane;
            if (gp == null || !gp.Valid || !_arCameraPoseValid) return res;

            // ---- Колёса: продольная координата + радиус, разделение перед/зад ----
            var wheelArr = new JArray();
            double frontLong = double.NaN, rearLong = double.NaN;
            double frontRadius = 0.506, rearRadius = 0.506;

            foreach (var w in gp.Wheels)
            {
                double lon = w.LongitudinalM;      // + вперёд
                // Высота колеса относительно плоскости под ним.
                double groundUnder = gp.HeightAt(w.Contact.X, w.Contact.Z);
                double lift = w.Contact.Y - groundUnder;

                wheelArr.Add(new JObject
                {
                    ["index"] = w.Index,
                    ["onGround"] = w.OnGround,
                    ["longitudinalM"] = lon,
                    ["radius"] = w.Radius,
                    ["liftM"] = lift,                  // отрыв от плоскости (поднятое колесо)
                    ["suspensionDeflection"] = w.SuspensionDeflection
                });

                if (!w.OnGround) continue;
                if (double.IsNaN(rearLong) || lon < rearLong)
                {
                    // Самое заднее колесо (наименьшая продольная координата).
                    rearLong = lon; rearRadius = w.Radius;
                }
                if (double.IsNaN(frontLong) || lon > frontLong)
                {
                    frontLong = lon; frontRadius = w.Radius;
                }
            }

            if (double.IsNaN(rearLong)) rearLong = -1.64;
            if (double.IsNaN(frontLong)) frontLong = 2.094;

            // ---- ВЕРТИКАЛЬНЫЙ FOV ----
            // v1.0.40.32: берём ИЗМЕРЕННЫЙ вертикальный FOV (ArBridge.FovDegreesAr1Vertical),
            // а не выводим из горизонтального: «один focal по X и Y» давал ≈50.5°
            // вместо реальных ≈30.7° и «уплывающий» горизонт. Геометрический вывод
            // оставлен только как fallback, если вертикальный не задан.
            double hFovDeg = AR.ArBridge.FovDegreesAr1;
            double aspect = 16.0 / 9.0;
            var screen = GetGameScreen();
            try
            {
                if (screen != null && screen.Bounds.Height > 0)
                    aspect = (double)screen.Bounds.Width / screen.Bounds.Height;
            }
            catch { }
            if (!double.IsFinite(aspect) || aspect < 0.2 || aspect > 8.0) aspect = 16.0 / 9.0;

            double vFovDeg = AR.ArBridge.FovDegreesAr1Vertical;
            if (!(vFovDeg > 1.0))
            {
                double hHalfTan = Math.Tan(hFovDeg * Math.PI / 180.0 * 0.5);
                double vHalfTan = hHalfTan / aspect;
                vFovDeg = 2.0 * Math.Atan(vHalfTan) * 180.0 / Math.PI;
            }

            // ---- Камера в СИСТЕМЕ ФУРЫ: продольная координата и высота над полотном ----
            // Продольная координата камеры = проекция (camera − truck) на продольную ось
            // фуры (мировой forward кузова). Высота — расстояние до плоскости.
            double truckForwardX = -Math.Sin(_arHeading * Math.PI * 2.0);
            double truckForwardZ = -Math.Cos(_arHeading * Math.PI * 2.0);
            double camDx = _arCameraPose.X - _arTruckX;
            double camDz = _arCameraPose.Z - _arTruckZ;
            double camLong = camDx * truckForwardX + camDz * truckForwardZ;
            double camHeight = gp.SignedDistance(_arCameraPose.X, _arCameraPose.Y, _arCameraPose.Z);

            // ============================================================
            // v1.0.40.37: НАКЛОН/КРЕН ПОЛОТНА СЧИТАЕМ ОТ ИЗМЕРЕННОЙ ПЛОСКОСТИ.
            // Визуализация теперь использует ГОРИЗОНТАЛЬНУЮ плоскость, у которой
            // нормаль = (0,1,0) по построению, поэтому углы от неё ВСЕГДА были бы
            // нулевыми. Индикатор наклона/крена должен показывать РЕАЛЬНЫЙ уклон
            // полотна — берём его из измеренной (наклонной) плоскости.
            // ============================================================
            var gpTilt = _arMeasuredGroundPlane ?? gp;

            // ---- Угол прицельной линии (луча камеры) к плоскости полотна ----
            // sin(угол к плоскости) = |dot(Forward, Normal)| — устойчиво на уклонах.
            double dot = _arCameraPose.Forward.X * gp.Normal.X +
                         _arCameraPose.Forward.Y * gp.Normal.Y +
                         _arCameraPose.Forward.Z * gp.Normal.Z;
            double rayAngleDeg = Math.Asin(Math.Clamp(Math.Abs(dot), 0.0, 1.0)) * 180.0 / Math.PI;
            bool aimsDown = dot < 0;

            // ---- Прицельная метка: луч × плоскость ----
            double hitLong = double.NaN, hitDist = double.NaN;
            if (gp.IntersectRay(_arCameraPose.X, _arCameraPose.Y, _arCameraPose.Z,
                    _arCameraPose.Forward.X, _arCameraPose.Forward.Y, _arCameraPose.Forward.Z,
                    out double t, out double hx, out double hy, out double hz))
            {
                hitLong = (hx - _arTruckX) * truckForwardX + (hz - _arTruckZ) * truckForwardZ;
                hitDist = Math.Sqrt((hx - _arCameraPose.X) * (hx - _arCameraPose.X) +
                                    (hy - _arCameraPose.Y) * (hy - _arCameraPose.Y) +
                                    (hz - _arCameraPose.Z) * (hz - _arCameraPose.Z));
            }

            res["valid"] = true;
            res["groundY"] = 0.0;                      // плоскость = «нулевая» линия визуализации
            res["averageWheelHeight"] = gp.AverageWheelHeight;
            res["referenceHeight"] = gp.ReferenceHeight;
            res["maxResidual"] = gp.MaxResidual;
            // ============================================================
            // v1.0.40.33: НАКЛОН ПЛОСКОСТИ И РОЛЛ ДЛЯ ВИЗУАЛИЗАЦИИ.
            // pitchDeg — продольный наклон полотна (положительный = подъём/в гору),
            // rollDeg  — поперечный крен (положительный = вправо).
            // Считаем ОТ ГЕОМЕТРИИ ПЛОСКОСТИ (normal), а не от углов кузова:
            // так индикатор показывает реальный наклон ПОЛОТНА.
            //   продольный наклон = наклон плоскости вдоль продольной оси фуры:
            //     sin(pitch) = dot(ForwardTruck, Normal)
            //   поперечный крен = то же вдоль поперечной оси фуры.
            // ============================================================
            double fwdX = truckForwardX, fwdZ = truckForwardZ;
            // Поперечная ось фуры (вправо) = forward, повёрнутый на 90° по часовой.
            double rightX = -fwdZ, rightZ = fwdX;

            double alongForward = fwdX * gpTilt.Normal.X + fwdZ * gpTilt.Normal.Z;
            double alongRight = rightX * gpTilt.Normal.X + rightZ * gpTilt.Normal.Z;

            // Наклон вдоль оси: положительный, если полотно ПОДНИМАЕТСЯ вперёд.
            // Нормаль отклонена НАЗАД от вертикали при подъёме вперёд, поэтому знак
            // берём с минусом относительно компоненты нормали по forward.
            double pitchDeg = -Math.Asin(Math.Clamp(alongForward, -1.0, 1.0)) * 180.0 / Math.PI;
            double rollDeg = Math.Asin(Math.Clamp(alongRight, -1.0, 1.0)) * 180.0 / Math.PI;

            res["pitchDeg"] = pitchDeg;
            res["rollDeg"] = rollDeg;
            res["planeNormalY"] = gp.Normal.Y;
            res["wheels"] = wheelArr;
            res["rearLongitudinalM"] = rearLong;
            res["rearRadiusM"] = rearRadius;
            res["frontLongitudinalM"] = frontLong;
            res["frontRadiusM"] = frontRadius;
            res["cameraLongitudinalM"] = camLong;
            res["cameraHeightM"] = camHeight;
            res["hFovDeg"] = hFovDeg;
            res["vFovDeg"] = vFovDeg;
            res["screenAspect"] = aspect;
            res["rayAngleDeg"] = rayAngleDeg;
            res["aimsDown"] = aimsDown;
            res["hitLongitudinalM"] = hitLong;
            res["hitDistanceM"] = hitDist;
            // ============================================================
            // v1.0.40.38: ДАННЫЕ ДЛЯ ЗАДНЕЙ ПРОЕКЦИИ (вид на грузовик СЗАДИ).
            //
            // Требование пользователя: «сделать в визуализации высоты ещё и заднюю
            // проекцию — смотрим на грузовик сзади, два колеса красными
            // прямоугольниками высотой с диаметр колеса, на расстоянии друг от
            // друга по координатам, указываем точку камеры относительно колёс и
            // ориентацию камеры двумя осями от этой точки. Рисуем голубой горизонт
            // сразу над плоскостью земли. При крене креним и землю, колёса на ней,
            // но горизонт остаётся на месте.»
            //
            // Задний вид = проекция на плоскость (поперечная ось фуры × мировая
            // вертикаль). Поперечная координата = проекция на правую ось шасси,
            // вертикальная = высота относительно плоскости земли.
            // ============================================================
            double latRightX = rightX, latRightZ = rightZ;
            double rearBaseY = gp.AverageWheelHeight;   // «плоскость земли» для заднего вида

            // Разнос колёс по поперечной оси (из реальных контактов).
            double halfTrackM = 0.65;                    // fallback
            {
                double minLat = double.NaN, maxLat = double.NaN;
                foreach (var w in gp.Wheels)
                {
                    if (!w.OnGround) continue;
                    double lat = (w.Contact.X - _arTruckX) * latRightX +
                                 (w.Contact.Z - _arTruckZ) * latRightZ;
                    if (double.IsNaN(minLat) || lat < minLat) minLat = lat;
                    if (double.IsNaN(maxLat) || lat > maxLat) maxLat = lat;
                }
                if (!double.IsNaN(minLat) && !double.IsNaN(maxLat) && maxLat - minLat > 0.2)
                    halfTrackM = (maxLat - minLat) * 0.5;
            }

            // Камера: поперечное смещение и высота над землёй.
            double camLateralM = (_arCameraPose.X - _arTruckX) * latRightX +
                                 (_arCameraPose.Z - _arTruckZ) * latRightZ;
            double camHeightM = _arCameraPose.Y - rearBaseY;

            // Ориентация КАМЕРЫ двумя осями (задний вид проецирует их на плоскость
            // «поперечная × вертикаль»): Forward — куда смотрит, Up — где верх кадра.
            JArray ProjectAxis(double ax, double ay, double az)
                => new JArray(
                    ax * latRightX + az * latRightZ,   // поперечная составляющая
                    ay);                               // вертикальная составляющая

            res["rearBaseY"] = rearBaseY;
            res["halfTrackM"] = halfTrackM;
            res["wheelDiameterM"] = 2.0 * rearRadius;
            res["cameraLateralM"] = camLateralM;
            res["cameraHeightAboveGroundM"] = camHeightM;
            res["camAxisForward"] = ProjectAxis(_arCameraPose.Forward.X, _arCameraPose.Forward.Y, _arCameraPose.Forward.Z);
            res["camAxisUp"] = ProjectAxis(_arCameraPose.Up.X, _arCameraPose.Up.Y, _arCameraPose.Up.Z);
            // Реальный крен КАМЕРЫ (из базиса) — сколько камера завалена к горизонту.
            {
                double fy = _arCameraPose.Forward.Y;
                double camRoll = 0.0;
                if (Math.Abs(fy) < 0.9999)
                {
                    double v = -_arCameraPose.Right.Y / Math.Sqrt(Math.Max(1e-9, 1.0 - fy * fy));
                    camRoll = Math.Asin(Math.Clamp(v, -1.0, 1.0)) * 180.0 / Math.PI;
                }
                res["cameraRollDeg"] = camRoll;
            }
            // Оси ШАССИ в заднем виде (крен кузова виден как наклон поперечной оси).
            res["chassisRightRear"] = ProjectAxis(ChassisRightX, ChassisRightY, ChassisRightZ);
            res["chassisUpRear"] = ProjectAxis(ChassisUpX, ChassisUpY, ChassisUpZ);
            res["cameraRollFactor"] = ArCameraRollFactor;
            // Масштаб визуализации по заданию: 1 м за задним колесом, 40 м от камеры
            // до края — страница использует эти два числа как границы вида.
            res["rearMarginM"] = 1.0;
            res["cameraToEdgeM"] = 40.0;
            return res;
        }

        private DateTime _arSrcLogAt = DateTime.MinValue;
        private DateTime _arSrcOkLogAt = DateTime.MinValue;
        // v1.0.40.34: троттлинг диагностики наклона (1 строка / 5 с).
        private DateTime _arTiltLogAt = DateTime.MinValue;

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
        // v1.0.40.42: метод переименован в BuildArModel и стал ЧИСТОЙ ФУНКЦИЕЙ
        // (только строит список). Никакого UI/логов из UI — он вызывается в ФОНОВОМ
        // потоке (см. QueueArModelRebuild). Это устраняет «замирание точек»: раньше
        // чтение ~1.9 МБ JSON шло на UI-потоке и блокировало тики.
        private List<ArPoint> BuildArModel()
        {
            var list = new List<ArPoint>();
            try
            {
                // Города (статика по gameName).
                // v1.0.40.42: ЦВЕТ передаём как на МИНИКАРТЕ (c.Color). Раньше здесь
                // стояла пустая строка, и JS подставлял голубой KIND_FALLBACK.poi —
                // отсюда «у точек всегда голубой цвет».
                var cities = LoadStaticCities();
                foreach (var c in cities.Values)
                    if (c.Enabled && c.Hidden != 1 && c.ShowInAr)
                        list.Add(new ArPoint(c.GameName, c.RealName, c.X, c.Y, c.Z, "city", false, "Город", c.Color));

                // POI (статика + merged) — без hidden; category = категория оверлея.
                // v1.0.40.40: РЕАЛЬНАЯ ВЫСОТА (было жёстко 0!). Именно из-за этого
                // компании висели НИЖЕ метки новой точки с теми же координатами:
                // метка берёт реальную y, а POI шли с нулём. Высоту даёт SDO-слой
                // (`editor_static_data\*.json`, поле y), который накладывается поверх
                // Overlays.json в `LoadStaticPois` (в Overlays.json поля y НЕТ).
                var pois = LoadStaticPois();
                // v1.0.40.40: ПОДГРУЖАЕМ SDO — ВОТ ГДЕ РЕАЛЬНАЯ ВЫСОТА.
                // В Overlays.json поля y нет вообще, а в `editor_static_data\*.json`
                // (SDO) оно есть (напр. overlay_company.json: 231 компания с y≈113 м).
                // Без этого слоя AR-модель не знала высот и все POI шли с нулём.
                // Порядок как в конвейере карты: SDO ПОВЕРХ Overlays (uid совпадают
                // у 231 компании, «последний побеждает»).
                LoadSdoPointsInto(pois);
                // v1.0.40.42: ЦВЕТ берём из PointData (SDO кладёт SdoMeta.ColorHexOf) —
                // тот же цвет, что на миникарте и в сайдбаре, а не пустая строка.
                foreach (var p in pois.Values)
                    if (p.Enabled && p.Hidden != 1 && p.ShowInAr)
                        list.Add(new ArPoint(p.GameName, p.RealName, p.X, p.Y, p.Z, "poi", false, p.Category, p.Color));

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
                    double ex = 0, ey = 0, ez = 0;
                    var coords = (string?)entry["coords"];
                    if (!string.IsNullOrEmpty(coords))
                    {
                        // v1.0.40.40: ЧИТАЕМ И ВЫСОТУ (parts[1]) — раньше она терялась,
                        // и точки целей/пользователя получали Y=0 при реальной высоте
                        // в файле («coords = x,y,z»).
                        var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out ex);
                            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out ey);
                            double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out ez);
                        }
                    }
                    else
                    {
                        ex = entry["x"]?.Value<double>() ?? 0;
                        ey = entry["y"]?.Value<double>() ?? 0;
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
                        list.Add(new ArPoint(key!, nm, ex, ey, ez, "target", true, string.IsNullOrEmpty(ovrCat) ? "Цель" : ovrCat, ovrColor));
                    }
                    else
                    {
                        if (((int?)entry["hidden"] ?? 0) == 1) continue;
                        if (!showInAr) continue;
                        list.Add(new ArPoint(key!, nm, ex, ey, ez, "poi", false, string.IsNullOrEmpty(ovrCat) ? "custom" : ovrCat, ovrColor)); // user-точка как poi
                    }
                }

                // Модель изменилась? Сравниваем подпись (имя+координаты) со старой —
                // пересборка НЕ должна переотправлять ar_target (фидбек 31.08.2026:
                // слать только при обновлении точек, не регулярно).
                string sig = string.Concat(list.OrderBy(p => p.gameName, StringComparer.Ordinal)
                    .Select(p => p.gameName + "|" + p.x.ToString("F1") + "," + p.y.ToString("F0") + "," + p.z.ToString("F1") + ";"));
                bool changed = sig != _arModelSig;
                if (changed) _arModelSig = sig;
                // Цель переотправляем ТОЛЬКО при реальном изменении модели.
                // (подмена _arPoints — в QueueArModelRebuild, под lock)
                if (changed) _arLastSentGameName = null;
                // Логи: методы потокобезопасны (AppendLog сам маршалит в UI-поток).
                // В workflow — только реальное изменение; иначе в app_data.
                if (changed)
                    AppendLog($"[AR] Модель точек обновлена: {list.Count} (цели: {list.Count(i => i.isTarget)}).");
                else
                    Logger.Current?.Data($"[AR] Модель точек без изменений: {list.Count}.");
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[AR] Ошибка обновления модели точек: {ex.Message}");
            }
            return list;
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
                        $"forced={_arTelemetryForced} head={( _arLastHead != null)} pts={ArPointsSnapshot().Count} " +
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
                //
                // v1.0.40.42 — КОРЕНЬ «ТОЧКИ ЗАМИРАЮТ НА 2 СЕКУНДЫ».
                // RefreshArModel выполнялся ЗДЕСЬ, то есть НА UI-ПОТОКЕ (ArUpdateTick
                // вызывается через BeginInvoke). Внутри — LoadStaticCities +
                // LoadStaticPois + SdoLoader.LoadAll (чтение и парсинг ~1.9 МБ JSON).
                // Пока это считалось, UI-поток был занят: тики (16 мс) не обрабатывались,
                // телеметрия не отправлялась, и точки «стояли» на месте.
                // ЗАМЕРЕНО в логе: интервал «tick alive» 5010 мс → 5903 / 6483 / 6558 мс,
                // то есть провал ~1.5 с ровно в момент пересборки модели.
                //
                // ФИКС: пересборка — в ФОНОВОМ потоке (модель не зависит от UI);
                // в _arPoints результат подменяется АТОМАРНО (ссылка, синхронизация lock).
                // UI-поток при этом свободен и продолжает рассылать телеметрию.
                if ((DateTime.Now - _arModelAt).TotalMilliseconds > ModelRefreshIntervalMs)
                    QueueArModelRebuild();

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
                            // v1.0.40.32: ВЕРТИКАЛЬНЫЙ FOV — ОТДЕЛЬНО (корень
                            // «горизонт/метка уплывают тем сильнее, чем дальше
                            // прицел от горизонта»). Один focal по X и Y давал
                            // смещение Δv ≈ Δf·tan(наклона головы).
                            ["fovDegVertical"] = AR.ArBridge.FovDegreesAr1Vertical,
                            ["valid"] = _arCameraPoseValid
                        }
                    };
                    // Legacy-поля оставляем для диагностики (проекция их не использует).
                    if (_arLastHead != null) tel["head"] = new JArray(_arLastHead);
                    // ============================================================
                    // v1.0.40.31: РЕАЛЬНАЯ ПЛОСКОСТЬ ДОРОГИ (Ground Plane по колёсам).
                    // Отдаём готовую плоскость: origin/normal/axisU/axisV/высоты и
                    // диагностический массив колёс. Страница AR1 и визуализация высот
                    // живут НА этой плоскости — никаких groundY/PlaneOffsetM.
                    // ============================================================
                    tel["groundPlane"] = BuildGroundPlanePayload();
                    // ============================================================
                    // v1.0.40.31: ДАННЫЕ ДЛЯ ОКНА ВИЗУАЛИЗАЦИИ ВЫСОТ (боковая проекция).
                    // Отдаём УЖЕ В СИСТЕМЕ ФУРЫ: продольные координаты колёс, положение
                    // камеры, угол луча прицела к полотну и ВЕРТИКАЛЬНЫЙ FOV, вычисленный
                    // из горизонтального по пропорции экрана (16:9 по умолчанию).
                    // Страница визуализации ничего не пересчитывает из углов.
                    // ============================================================
                    tel["heights"] = BuildHeightsPayload();
                    if (_arPin.HasValue)
                    {
                        tel["pin"] = new JObject { ["x"] = _arPin.Value.x, ["y"] = _arPin.Value.y, ["z"] = _arPin.Value.z };
                    }
                    // Первый пакет (или смена модели городов): города уходят один раз —
                    // без них страница не компенсирует высоту точек с Y=0.
                    if (!_arCitiesSent && ArPointsSnapshot().Count > 0)
                    {
                        var cityArr = new JArray();
                        foreach (var it in ArPointsSnapshot())
                        {
                            if (it.kind != "city") continue;
                            double d2 = (it.x - _arTruckX) * (it.x - _arTruckX) + (it.z - _arTruckZ) * (it.z - _arTruckZ);
                            if (d2 > 5000.0 * 5000.0) continue;
                            if (Math.Abs(it.y) < 0.001) continue;
                            // v1.0.40.40: БЕЗ компенсации (−44 м УБРАНА) — города и
                            // компании идут с РЕАЛЬНОЙ мировой высотой, иначе они
                            // висели ниже метки новой точки при тех же координатах.
                            cityArr.Add(new JObject { ["x"] = it.x, ["y"] = it.y, ["z"] = it.z });
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

                // ============================================================
                // v1.0.40.40: ПОКАЗЫВАЕМ ВСЕ ТОЧКИ В РАДИУСЕ 50 м.
                //
                // Требование пользователя: «Убираем оси, сетку, визуализацию высот.
                // Отображаем в АР все точки в радиусе 50 м.»
                //
                // Раньше приложение выбирало ОДНУ лучшую точку (score с угловым
                // приоритетом) в радиусе 1.5 км и слало `ar_target` разово при смене.
                // Теперь шлём СПИСОК точек в радиусе ArDisplayRadiusM: страница
                // рисует все сразу. Порог 50 м — требование пользователя.
                // ============================================================
                var near = new List<ArPoint>();
                foreach (var it in ArPointsSnapshot())
                {
                    double dx = it.x - _arTruckX;
                    double dz = it.z - _arTruckZ;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > ArDisplayRadiusM * ArDisplayRadiusM) continue;
                    if (d2 < 0.01) continue;
                    near.Add(it);
                }

                // Сортируем: цели первыми, затем по дистанции — стабильный порядок
                // отрисовки без скачков между кадрами (иначе точки «мигают» порядком).
                near.Sort((a, b) =>
                {
                    if (a.isTarget != b.isTarget) return a.isTarget ? -1 : 1;
                    double da = (a.x - _arTruckX) * (a.x - _arTruckX) + (a.z - _arTruckZ) * (a.z - _arTruckZ);
                    double db = (b.x - _arTruckX) * (b.x - _arTruckX) + (b.z - _arTruckZ) * (b.z - _arTruckZ);
                    int cmp = da.CompareTo(db);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.gameName, b.gameName);
                });

                // Подпись набора — чтобы НЕ спамить одинаковыми списками каждый тик.
                string nearSig = string.Join(";", near.Select(p => p.gameName));
                bool forceSend = _arResetNearSig;
                if (forceSend) _arResetNearSig = false;
                if (!forceSend && nearSig == _arLastNearSig) return;   // набор не изменился — не шлём
                _arLastNearSig = nearSig;

                if (near.Count == 0)
                {
                    if (!_arTargetMustClear)
                    {
                        _arTargetMustClear = true;
                        _arLastSentGameName = null;
                        SendCommandToMap("ar_target", new JObject
                        {
                            ["hasTarget"] = false,
                            ["reason"] = $"нет точек в радиусе {ArDisplayRadiusM:F0} м"
                        });
                        _arV2Target = null;
                        PublishArV2Snapshot();
                        ArLogPickDetail(hasTarget: false, reason: $"нет точек в радиусе {ArDisplayRadiusM:F0} м");
                    }
                    return;
                }

                // Список точек для отрисовки всех сразу.
                var pointsArr = new JArray();
                foreach (var it in near)
                {
                    double distM = Math.Sqrt((it.x - _arTruckX) * (it.x - _arTruckX) +
                                             (it.z - _arTruckZ) * (it.z - _arTruckZ));
                    pointsArr.Add(new JObject
                    {
                        ["gameName"] = it.gameName,
                        ["realName"] = it.realName,
                        // v1.0.40.40: РЕАЛЬНАЯ высота точки (было бы 0 у POI —
                        // именно это заставляло компании висеть ниже метки).
                        ["x"] = it.x,
                        ["y"] = it.y,
                        ["z"] = it.z,
                        ["dist"] = distM,
                        ["kind"] = it.kind,
                        ["category"] = it.category,
                        ["color"] = it.color,
                        ["isTarget"] = it.isTarget
                    });
                }

                // Ближайшая — для обратной совместимости (статус-строка, v2-канал).
                var b = near.FirstOrDefault(p => p.isTarget) ?? near[0];
                double bDistM = Math.Sqrt((b.x - _arTruckX) * (b.x - _arTruckX) +
                                          (b.z - _arTruckZ) * (b.z - _arTruckZ));

                SendCommandToMap("ar_target", new JObject
                {
                    ["hasTarget"] = true,
                    ["count"] = near.Count,
                    ["radiusM"] = ArDisplayRadiusM,
                    ["points"] = pointsArr,
                    // Поля ближайшей — для старого кода страницы (совместимость).
                    ["gameName"] = b.gameName,
                    ["realName"] = b.realName,
                    ["x"] = b.x,
                    ["y"] = b.y,
                    ["z"] = b.z,
                    ["dist"] = bDistM,
                    ["kind"] = b.kind,
                    ["category"] = b.category,
                    ["color"] = b.color,
                    ["heading"] = _arHeading
                });
                _arLastSentGameName = b.gameName;
                _arTargetMustClear = false;
                _arLastTargetSentAt = DateTime.Now;
                _arV2Target = new AR.ArMarker
                {
                    GameName = b.gameName,
                    RealName = b.realName,
                    X = b.x, Y = b.y, Z = b.z,
                    Dist = bDistM,
                    Kind = b.kind,
                    Category = b.category,
                    Color = b.color
                };
                PublishArV2Snapshot();
                ArLogPickDetail(hasTarget: true,
                    reason: $"радиус {ArDisplayRadiusM:F0} м: {near.Count} точек, ближайшая {b.gameName} d={bDistM:F0}м");
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
                int near3k = 0; int total = ArPointsSnapshot().Count;
                foreach (var it in ArPointsSnapshot())
                {
                    double ddx = it.x - _arTruckX, ddz = it.z - _arTruckZ;
                    if (ddx * ddx + ddz * ddz <= 1500.0 * 1500.0) near3k++;
                }
                Logger.Current?.Data($"[AR] tick: truck=({_arTruckX:F1},{_arTruckY:F1},{_arTruckZ:F1}) h={_arHeading:F3}" +
                    $" known={_arTruckKnown} points={total} near3km={near3k} hasTarget={hasTarget} ({reason})");
            }
            catch { }
        }

        // ================================================================
        // ПОМЕТКА В АР (v70 → v1.0.40.31): точка на пересечении ЛУЧА ВЗГЛЯДА
        // КАМЕРЫ с РЕАЛЬНОЙ ПЛОСКОСТЬЮ ДОРОГИ (по колёсам), затем snap в узел
        // метровой сетки. «Пометить в АР» / CTRL+X.
        //
        // v1.0.40.31 ИСПРАВЛЯЕТ КОРЕНЬ «точка создаётся не по лучу»:
        //   было  — самодельные углы (heading + head[3], head[4]), EyeHeightM=1.5,
        //           горизонтальная плоскость truckY+PlaneOffsetM, Math.Round(px/pz);
        //   стало — ГОТОВЫЙ базис камеры (CameraForward из SCS-позы) × реальная
        //           плоскость дороги → (u,v) плоскости → округление до метров →
        //           обратно в мир. Точка всегда лежит на полотне и в узле сетки.
        //
        // Взгляд выше горизонта — пересечения с плоскостью НЕТ (или точка позади):
        // пометка не создаётся, в лог уходит причина (не ставим точку «за спиной»).
        // ================================================================
        internal void ArPlacePinFromViewCenter()
        {
            if (!_arTruckKnown)
            {
                AppendLog("[AR] Пометка невозможна: нет телеметрии фуры.");
                return;
            }
            if (!_arCameraPoseValid)
            {
                AppendLog("[AR] Пометка невозможна: поза камеры не построена.");
                return;
            }
            var plane = _arGroundPlane;
            if (plane == null || !plane.Valid)
            {
                AppendLog("[AR] Пометка невозможна: плоскость дороги не построена (нет данных колёс).");
                return;
            }

            var origin = new System.Numerics.Vector3(
                (float)_arCameraPose.X, (float)_arCameraPose.Y, (float)_arCameraPose.Z);
            var dir = _arCameraPose.Forward;

            const double MaxDistM = 1500.0;

            // Пересечение луча с плоскостью: t из уравнения плоскости.
            if (!plane.IntersectRay(origin.X, origin.Y, origin.Z,
                    dir.X, dir.Y, dir.Z,
                    out double t, out double hx, out double hy, out double hz))
            {
                AppendLog("[AR] Пометка не создана: взгляд выше горизонта (нет пересечения с дорогой).");
                return;
            }
            if (t > MaxDistM)
            {
                AppendLog($"[AR] Пометка не создана: пересечение слишком далеко ({t:F0} м > {MaxDistM:F0} м).");
                return;
            }

            // (u,v) внутри плоскости → округление до метров → обратно в мир.
            plane.ProjectToGridCoordinates(hx, hy, hz, out double u, out double v);
            u = Math.Round(u, MidpointRounding.AwayFromZero);
            v = Math.Round(v, MidpointRounding.AwayFromZero);
            plane.FromGrid(u, v, out double px, out double py, out double pz);

            _arPin = (px, py, pz);
            SendCommandToMap("ar_pin", new JObject
            {
                ["active"] = true,
                ["x"] = px, ["y"] = py, ["z"] = pz
            });
            Logger.Current?.Data($"[AR] pin placed: x={px:F2} y={py:F2} z={pz:F2} " +
                $"(луч × плоскость: t={t:F2} м, узел сетки u={u:F0} v={v:F0}).");
            AppendLog($"[AR] Пометка установлена: ({px:F0}, {pz:F0}) на {t:F0} м (плоскость дороги, узел сетки).");
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
        // v1.0.40.31 — существующий X/Z проецируется на РЕАЛЬНУЮ плоскость дороги,
        // затем snap в узел метровой сетки и обратно в мир. Плоскости нет → работаем
        // как раньше (опорная точка фуры), чтобы функция не пропадала.
        internal void ArPlacePinAtWorld(double x, double z)
        {
            if (!_arTruckKnown) return;            // нет телеметрии — пометку не рисуем

            var plane = _arGroundPlane;
            if (plane != null && plane.Valid)
            {
                plane.SnapToPlane(x, z, out double sx, out double sy, out double sz);
                plane.ProjectToGridCoordinates(sx, sy, sz, out double u, out double v);
                u = Math.Round(u, MidpointRounding.AwayFromZero);
                v = Math.Round(v, MidpointRounding.AwayFromZero);
                plane.FromGrid(u, v, out double px, out double py, out double pz);
                _arPin = (px, py, pz);
                SendCommandToMap("ar_pin", new JObject
                {
                    ["active"] = true,
                    ["x"] = px, ["y"] = py, ["z"] = pz
                });
                return;
            }

            double pyFallback = _arTruckY;
            _arPin = (x, pyFallback, z);
            SendCommandToMap("ar_pin", new JObject
            {
                ["active"] = true,
                ["x"] = x, ["y"] = pyFallback, ["z"] = z
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