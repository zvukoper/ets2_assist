using System;
using System.Numerics;
using System.Threading;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// Мост между каналом AR и render thread.
    ///
    /// WebSocket/telemetry являются только источником GameState.
    /// Render thread читает latest state.
    ///
    /// Здесь также хранится состояние пользовательской perspective calibration.
    /// </summary>
    public static class ArBridge
    {
        public static readonly LatestBuffer<ArGameState> Game = new();

        public static long TelemetryVersion => Game.Version;
        public static long SkippedStates => Game.Skipped;

        private static long _telAt = Environment.TickCount64;
        private static long _tgtAt;

        public static void PublishTelemetry(ArGameState snapshot)
            => Game.Publish(snapshot);

        public static double GameAgeMs =>
            Environment.TickCount64 - _lastPublishTick;

        private static long _lastPublishTick;

        public static void MarkPublished()
            => _lastPublishTick = Environment.TickCount64;

        // ================================================================
        // FOV
        // ================================================================

        private static double _fovDegrees = 100.0;

        public static double FovDegrees
        {
            get => Volatile.Read(ref _fovDegrees);
            set => Volatile.Write(ref _fovDegrees, value);
        }

        // ================================================================
        // FOV AR1 (JS-страница web_ar_hud.html) — ОТДЕЛЬНЫЙ параметр.
        // Требование пользователя (v1.0.40.27): CTRL+PGUP/PGDN меняют FOV только AR1,
        // шаг 1°, не затрагивая калибровку AR2 (FovDegrees выше). Значение транслируется
        // на страницу командой ar_fov (CFG.fovDeg в ar_hud.js) и хранится в AppSettings.
        // ================================================================

        private static double _fovDegreesAr1 = 105.0;
        public static double FovDegreesAr1
        {
            get => Volatile.Read(ref _fovDegreesAr1);
            set => Volatile.Write(ref _fovDegreesAr1, value);
        }

        // ================================================================
        // v1.0.40.32 КОРЕНЬ «горизонт и метка уплывают тем сильнее, чем дальше
        // прицел от горизонта»: ВЕРТИКАЛЬНЫЙ FOV — НЕ ПРОИЗВОДНАЯ ОТ ГОРИЗОНТАЛЬНОГО.
        //
        // Геометрия ошибки (доказана измерениями по скриншотам):
        //   проекция считала ОДИН focal length f = (W/2)/tan(hFov/2) и по X, и по Y.
        //   Это соответствует вертикальному FOV ≈ 2·atan(tan(hFov/2)/aspect).
        //   Реальный вертикальный FOV игры меньше ≈ на 10…15° (в ETS2 вертикаль и
        //   горизонталь масштабируются независимо; в config.cfg есть отдельные
        //   r_multimon_fov_vertical / r_multimon_fov_horizontal). Ошибка Δf даёт
        //   смещение Δv ≈ Δf·tan(pitch) — ровно ноль на горизонте и рост с углом
        //   наклона головы. Это и наблюдалось: на 0° приклеено, на ±10° — уплывает.
        //
        // Поэтому вертикальный focal length задаётся ОТДЕЛЬНОЙ величиной
        // (_fovDegreesAr1Vertical). Пока пользователь не подстроил её вручную
        // (Ar1VerticalFovManual), значение ВЫВОДИТСЯ из горизонтального с типичным
        // для ETS2 сжатием по вертикали — см. VerticalFromHorizontalFactor.
        // ================================================================

        /// <summary>
        /// Отношение tan(vFov/2) к «геометрическому» (16:9-выведенному) значению
        /// в ETS2. 1.0 = строго геометрическое; меньше 1.0 = игра «сжимает» вертикаль.
        ///
        /// ИЗМЕРЕНО по трём скриншотам (1920x1080, hFov=80°, центральный столбец):
        ///   h=−10.8° → горизонт 110 px;  h=0° → 400 px;  h=+10.8° → 860 px.
        /// МНК-подбор (базовый наклон b + вертикальный focal fv, переопределённая
        /// система — три уравнения на две неизвестные):
        ///   fv = 1965 px → vFov ≈ 30.7°, базовый наклон камеры b ≈ −2.35°,
        ///   RMS = 41 px. Результат устойчив к ±20 px ошибки измерений.
        ///   Для сравнения «один focal» (fv = fh = 1144 px) даёт vFov = 50.5°,
        ///   RMS = 158 px — то есть старая модель НЕ объясняет данные.
        ///   factor = tan(30.7°/2)/tan(50.5°/2) ≈ 0.58.
        ///
        /// Δfv ≈ 820 px даёт смещение Δy = Δf·tan(pitch): ровно 0 на горизонте и
        /// 157 px на 10.8° — наблюдавшийся «уплывающий» горизонт и метка.
        /// </summary>
        public const double Ar1VerticalFromHorizontalFactor = 0.58;

        private static double _fovDegreesAr1Vertical = 65.0;
        public static double FovDegreesAr1Vertical
        {
            get => Volatile.Read(ref _fovDegreesAr1Vertical);
            set => Volatile.Write(ref _fovDegreesAr1Vertical, value);
        }

        private static int _ar1VerticalFovManual;
        /// <summary>true — вертикальный FOV задан вручную и не выводится из горизонтального.</summary>
        public static bool Ar1VerticalFovManual
        {
            get => Volatile.Read(ref _ar1VerticalFovManual) != 0;
            set => Volatile.Write(ref _ar1VerticalFovManual, value ? 1 : 0);
        }

        /// <summary>
        /// Пересчёт вертикального FOV из горизонтального (когда ручная подстройка не задана).
        /// aspect = W/H, factor — Ar1VerticalFromHorizontalFactor.
        /// </summary>
        public static double DeriveAr1VerticalFov(double horizontalDeg, double aspect, double factor)
        {
            if (!double.IsFinite(horizontalDeg) || horizontalDeg <= 1.0) return 30.7;
            if (!double.IsFinite(aspect) || aspect <= 0.05) aspect = 16.0 / 9.0;
            if (!double.IsFinite(factor) || factor <= 0.05) factor = 1.0;
            double halfTanH = Math.Tan(horizontalDeg * Math.PI / 180.0 * 0.5);
            double halfTanGeom = halfTanH / aspect;
            double halfTanV = halfTanGeom * factor;
            return Math.Clamp(2.0 * Math.Atan(halfTanV) * 180.0 / Math.PI, 10.0, 170.0);
        }

        // ================================================================
        // DEBUG-РЕЖИМ ВЕБ-КОНТЕНТА (v1.0.40.28).
        // Состояние чекбокса «Отладка веб (debugShow)»: true — страницы оверлея
        // игнорируют логику скрытия и принудительно показывают контент (для отладки
        // и настройки), false — исходная логика. Команда debug_show уходит на все
        // страницы по WS 8084; нативная сторона (AR2) читает флаг отсюда и рисует
        // отладочные отметки камеры.
        // ================================================================

        private static int _debugShow;
        public static bool DebugShow
        {
            get => Volatile.Read(ref _debugShow) != 0;
            set => Volatile.Write(ref _debugShow, value ? 1 : 0);
        }

        // ================================================================
        // GROUND PLANE
        // ================================================================

        private static double _planeOffsetM = -0.75;

        public static double PlaneOffsetM
        {
            get => Volatile.Read(ref _planeOffsetM);
            set => Volatile.Write(ref _planeOffsetM, value);
        }

        // ================================================================
        // GRID
        // ================================================================

        private static int _showGrid = 1;

        public static bool ShowGrid
        {
            get => Volatile.Read(ref _showGrid) != 0;
            set => Volatile.Write(ref _showGrid, value ? 1 : 0);
        }

        // ================================================================
        // PERSPECTIVE WARP
        // ================================================================

        private static readonly object _warpLock = new();

        // Source = первоначальная проекция четырёх углов 10x10 м.
        private static readonly Vector2[] _warpSource =
            new Vector2[4];

        // Current = текущие экранные позиции четырёх жёлтых точек
        // (source, пропущенные через homography).
        private static readonly Vector2[] _warpCurrent =
            new Vector2[4];

        private static PerspectiveWarp.Homography _warpMatrix =
            PerspectiveWarp.Homography.Identity;

        private static bool _warpInitialized;
        private static int _dragIndex = -1;

        /// <summary>
        /// True после первого успешного построения четырёх исходных точек.
        /// </summary>
        public static bool PerspectiveWarpInitialized
        {
            get
            {
                lock (_warpLock)
                    return _warpInitialized;
            }
        }

        /// <summary>
        /// Получить текущую homography.
        /// </summary>
        public static PerspectiveWarp.Homography GetPerspectiveWarp()
        {
            lock (_warpLock)
                return _warpMatrix;
        }

        /// <summary>
        /// Получить текущие экранные позиции четырёх жёлтых управляющих точек.
        /// </summary>
        public static Vector2[] GetPerspectivePoints()
        {
            lock (_warpLock)
            {
                return new[]
                {
                    _warpCurrent[0],
                    _warpCurrent[1],
                    _warpCurrent[2],
                    _warpCurrent[3]
                };
            }
        }

        /// <summary>
        /// Инициализирует calibration.
        ///
        /// Source и Current сначала совпадают, поэтому первый кадр не меняет
        /// существующую перспективу вообще.
        /// </summary>
        public static void InitializePerspectiveWarp(Vector2[] source)
        {
            if (source == null || source.Length != 4)
                return;

            lock (_warpLock)
            {
                for (int i = 0; i < 4; i++)
                {
                    _warpSource[i] = source[i];
                    _warpCurrent[i] = source[i];
                }

                _warpMatrix = PerspectiveWarp.Homography.Identity;
                _warpInitialized = true;
                _dragIndex = -1;
            }
        }

        /// <summary>
        /// Обновляет текущие source-точки (красные) каждый кадр.
        /// Жёлтые точки (Current) пересчитываются через homography.
        /// </summary>
        public static void UpdatePerspectiveSources(Vector2[] source)
        {
            if (source == null || source.Length != 4)
                return;

            lock (_warpLock)
            {
                if (!_warpInitialized)
                    return;

                for (int i = 0; i < 4; i++)
                {
                    _warpSource[i] = source[i];

                    if (_warpMatrix.TryTransform(
                            source[i],
                            out Vector2 transformed))
                    {
                        _warpCurrent[i] = transformed;
                    }
                    else
                    {
                        _warpCurrent[i] = source[i];
                    }
                }
            }
        }

        /// <summary>
        /// Полностью сбрасывает calibration.
        /// Вызывается при каждом новом запуске AR2.
        /// </summary>
        public static void ResetPerspectiveWarp()
        {
            lock (_warpLock)
            {
                Array.Clear(_warpSource, 0, _warpSource.Length);
                Array.Clear(_warpCurrent, 0, _warpCurrent.Length);

                _warpMatrix =
                    PerspectiveWarp.Homography.Identity;

                _warpInitialized = false;
                _dragIndex = -1;
            }
        }

        /// <summary>
        /// Начать drag ближайшей жёлтой точки.
        /// </summary>
        public static bool TryBeginPerspectiveDrag(
            Vector2 mouse,
            float hitRadiusPx)
        {
            lock (_warpLock)
            {
                if (!_warpInitialized)
                    return false;

                int best = -1;
                float bestDist2 = hitRadiusPx * hitRadiusPx;

                for (int i = 0; i < 4; i++)
                {
                    Vector2 d = mouse - _warpCurrent[i];
                    float dist2 = d.X * d.X + d.Y * d.Y;

                    if (dist2 <= bestDist2)
                    {
                        bestDist2 = dist2;
                        best = i;
                    }
                }

                _dragIndex = best;
                return best >= 0;
            }
        }

        /// <summary>
        /// Переместить текущую управляющую точку.
        ///
        /// Source = текущие красные точки, destination = жёлтые (одна — под мышью).
        /// При перетаскивании одной точки вся перспектива перестраивается.
        /// </summary>
        public static void UpdatePerspectiveDrag(Vector2 mouse)
        {
            lock (_warpLock)
            {
                if (!_warpInitialized ||
                    _dragIndex < 0 ||
                    _dragIndex >= 4)
                    return;

                var destination = new Vector2[4];

                for (int i = 0; i < 4; i++)
                {
                    if (i == _dragIndex)
                    {
                        destination[i] = mouse;
                    }
                    else
                    {
                        destination[i] = _warpCurrent[i];
                    }
                }

                if (!PerspectiveWarp.TryCreate(
                        _warpSource,
                        destination,
                        out var newWarp))
                {
                    return;
                }

                _warpMatrix = newWarp;

                for (int i = 0; i < 4; i++)
                {
                    if (_warpMatrix.TryTransform(
                            _warpSource[i],
                            out Vector2 p))
                    {
                        _warpCurrent[i] = p;
                    }
                }
            }
        }

        /// <summary>
        /// Завершить drag.
        /// </summary>
        public static void EndPerspectiveDrag()
        {
            lock (_warpLock)
                _dragIndex = -1;
        }

        public static bool IsPerspectiveDragging
        {
            get
            {
                lock (_warpLock)
                    return _dragIndex >= 0;
            }
        }
    }
}