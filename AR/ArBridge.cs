using System;
using System.Numerics;
using System.Threading;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// AR v2.0 — мост между существующим каналом AR (MainForm.ArTarget: WS TruckTel,
    /// command WS рассылки ar_target/ar_pin) и рендер-потоком.
    /// Правило архитектуры: WebSocket только источник данных, НЕ каданс рендера.
    /// Writer — UI/AR-канал; Reader — render thread (LatestBuffer, latest wins).
    /// </summary>
    public static class ArBridge
    {
        /// <summary>Последний GameState (камера/цель/pin/города) — latest wins.</summary>
        public static readonly LatestBuffer<ArGameState> Game = new();

        /// <summary>Статистика для debug overlay (ар_телеметрия/ар_таргет частоты).</summary>
        public static long TelemetryVersion => Game.Version;
        public static long SkippedStates => Game.Skipped;

        private static long _telAt = Environment.TickCount64;
        private static long _tgtAt;

        /// <summary>Вызывается из MainForm.ArTarget при ar_telemetry (событийно).</summary>
        public static void PublishTelemetry(ArGameState snapshot) => Game.Publish(snapshot);

        /// <summary>Возраст последнего GameState, мс (диагностика «GameState age»).</summary>
        public static double GameAgeMs => Environment.TickCount64 - _lastPublishTick;
        private static long _lastPublishTick;

        public static void MarkPublished() => _lastPublishTick = Environment.TickCount64;

        // ================================================================
        // v92: FOV АР2-проекции — DUMB-ПРИЁМНИК. Приложение (MainForm.WndProc,
        // Ctrl+колесо) меняет это значение; рендер-поток читает его каждый кадр.
        // Никакого перехвата колеса в AR2-окне (оно click-through).
        // ================================================================
        private static double _fovDegrees = 100.0;   // v40.1: временная калибровка пользователя (было 95)
        public static double FovDegrees
        {
            get => Volatile.Read(ref _fovDegrees);
            set => Volatile.Write(ref _fovDegrees, value);
        }

        // ================================================================
        // v96: СМЕЩЕНИЕ ПЛОСКОСТИ ЗЕМЛИ (м). Меняется приложением через
        // Ctrl+Shift+PGUP/PGDN (±0.25 м). Влияет ТОЛЬКО на создание новых
        // меток точек (pin) и на отрисовку 3D-сетки плоскости в AR2.
        // Рендер-поток читает каждый кадр (dumb-приёмник).
        // ================================================================
        // v40.1: временная калибровка пользователя (было 0.0): плоскость ниже truckY
        private static double _planeOffsetM = -0.75;
        public static double PlaneOffsetM
        {
            get => Volatile.Read(ref _planeOffsetM);
            set => Volatile.Write(ref _planeOffsetM, value);
        }

        // v96: показывать ли 3D-сетку плоскости (Ctrl+Shift+* / чекбокс).
        // v99: по умолчанию ВКЛ (сетка рисуется сразу).
        private static int _showGrid = 1;
        public static bool ShowGrid
        {
            get => Volatile.Read(ref _showGrid) != 0;
            set => Volatile.Write(ref _showGrid, value ? 1 : 0);
        }
    }
}