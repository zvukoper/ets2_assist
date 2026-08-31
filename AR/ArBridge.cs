using System;
using System.Numerics;

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
    }
}