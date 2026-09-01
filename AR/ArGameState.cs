using System;
using System.Collections.Generic;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    // ================================================================
    // AR v2.0 — GameState (последнее известное состояние игры/AR-канала)
    // ================================================================
    // Заполняется СУЩЕСТВУЮЩИМ каналом MainForm.ArTarget (WS TruckTel + command WS
    // рассылки ar_*). Renderer читает только ссылки на immutable-снимки.
    // Идентичность логике ar_hud.js: точки статичны, рассылка событийная.
    public sealed class ArMarker
    {
        public string GameName = "";
        public string RealName = "";
        public double X, Y, Z;          // мир (метры карты)
        public double Dist;             // дистанция на момент отправки
        public string Kind = "poi";     // city | poi | target
        public string Category = "";
        public string Color = "";       // #rrggbb | ""
    }

    public sealed class ArGameState
    {
        // Камера (глаз) — от ar_telemetry (WS TruckTel → ApplyPlacementJson)
        public long Sequence;                  // монотонный номер телеметрии
        public double CamX, CamY, CamZ;
        public double YawBase;                 // heading (доля оборота)
        public double PitchBody;               // placement[4] (доля оборота)
        public double Roll;
        public double YawHead, PitchHead;      // head.offset[3]/[4] (доля оборота, +вверх)

        public double GroundY;                 // высота «земли» под фурой (placement[1])

        // v96: смещение плоскости земли (м) — влияет на создание новых меток
        // и на отрисовку 3D-сетки. Читается из ArBridge.PlaneOffsetM.
        public double PlaneOffsetM;

        // v96: показывать ли 3D-сетку плоскости (Ctrl+Shift+END).
        public bool ShowGrid;

        // Текущая цель (ar_target, разово при смене)
        public ArMarker? Target;

        // Пометка «Пометить в АР» (ar_pin, разово)
        public (double X, double Y, double Z)? Pin;

        // Города для компенсации высот (ar_telemetry.cities — первые N)
        public IReadOnlyList<(double X, double Y, double Z)> Cities = Array.Empty<(double, double, double)>();
    }
}