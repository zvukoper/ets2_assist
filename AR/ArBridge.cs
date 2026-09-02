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