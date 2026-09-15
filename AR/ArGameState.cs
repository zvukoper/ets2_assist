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
    //
    // v1.0.40.30 (ETS2_AR_CAMERA_POSE_IMPLEMENTATION): камера — ПОЛНАЯ 6DoF-поза
    // (позиция + базис Forward/Right/Up), вычисленная по SCS hierarchy:
    //   truck.world.placement → cabin.position/offset → head.position/offset.
    // Никаких EyeHeight/PitchCompensation/roll-хаков в проекции больше нет.
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
        // Номер позы камеры (монотонный) — растёт при каждом пересчёте позы.
        public long Sequence;

        // ВАЖНО: CamX/Y/Z — ВСЕГДА мировое положение ГЛАЗА/КАМЕРЫ,
        // а НЕ truck.world.placement (как было до v1.0.40.30).
        public double CamX, CamY, CamZ;

        // Полная мировая ориентация камеры (ортонормальный базис).
        public System.Numerics.Vector3 CameraForward = new(0, 0, -1);
        public System.Numerics.Vector3 CameraRight = new(1, 0, 0);
        public System.Numerics.Vector3 CameraUp = new(0, 1, 0);
        public bool CameraPoseValid;

        // Оставлено для существующих диагностических экранов/совместимости.
        // НЕ участвует в world-to-screen проекции.
        public double YawBase;                 // heading фуры (доля оборота)
        public double PitchBody;               // placement[4]
        public double Roll;                    // placement[5]
        public double YawHead, PitchHead;      // head.offset[3]/[4] (доля оборота)

        // Высота reference point грузовика — НЕ камера.
        public double GroundY;

        // v96: смещение плоскости земли (м) — влияет на создание новых меток
        // и на отрисовку 3D-сетки. Читается из ArBridge.PlaneOffsetM.
        public double PlaneOffsetM;

        // v96: показывать ли 3D-сетку плоскости (Ctrl+Shift+END).
        public bool ShowGrid;

        // Текущая цель (ar_target, разово при смене)
        public ArMarker? Target;

        // Пометка «Пометить в АР» / новая точка (ar_pin, разово)
        public (double X, double Y, double Z)? Pin;

        // Города — ТОЛЬКО для совместимости старого UI/диагностики.
        // НЕ участвуют в расчёте позы камеры и в world-to-screen проекции.
        public IReadOnlyList<(double X, double Y, double Z)> Cities = Array.Empty<(double, double, double)>();
    }
}