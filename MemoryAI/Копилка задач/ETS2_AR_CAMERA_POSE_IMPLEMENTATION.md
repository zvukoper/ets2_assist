# ETS2 Assist — AR Camera Pose / Horizon Fix

Репозиторий: `zvukoper/ets2_assist`
Ветка: `main`

## 0. Цель

Убрать из AR все приближённые параметры положения/наклона камеры и перейти на полную 6DoF-позу головы, вычисленную по штатной SCS telemetry hierarchy:

`truck.world.placement` → `truck.cabin.position` + `truck.cabin.offset` → `truck.head.position` + `truck.head.offset`.

Runtime AR не использовать:

- `EyeHeight`;
- `PitchCompensation`;
- отдельный `Roll`/`Pitch`-хак;
- зеркалирование `u`;
- сглаживание экранной координаты точки;
- prediction экранной координаты;
- высоту точки через ближайший город;
- плоскость колёс/земли для положения камеры.

Положение и ориентация камеры должны использоваться непосредственно из вычисленной мировой 6DoF-позы.

Источник формулы положения головы: официальный пример SCS SDK `telemetry_position`: `head = head.position + head.offset.position`, затем `cabin.position + cabin.offset.position + rotate(cabin.offset.orientation, head)`, затем `truck.world.placement.position + rotate(truck.world.placement.orientation, ...)`. SCS rotation — Roll вокруг Z, затем Pitch вокруг X, затем Heading вокруг Y. TruckTel публикует эти SCS данные как `fvector`/`fplacement`; локальная система: X вправо, Y вверх, Z назад.

---

# 1. Новый файл: `AR/ScsCameraPose.cs`

Создать полностью новый файл со следующим содержимым:

```csharp
using System;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// Euler SCS: heading/pitch/roll — доли оборота.
    /// </summary>
    public readonly record struct ScsEuler(
        double Heading,
        double Pitch,
        double Roll)
    {
        public static ScsEuler Identity => new(0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Полная мировая 6DoF-поза камеры игрока.
    ///
    /// Position — мировые координаты глаза.
    /// Forward — направление взгляда, единичный вектор.
    /// Right   — экранное право, единичный вектор.
    /// Up      — экранный верх, единичный вектор.
    ///
    /// По SCS: локальный Z смотрит НАЗАД, поэтому локальный Forward = (0,0,-1).
    /// </summary>
    public readonly struct ScsCameraPose
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public readonly Vector3 Forward;
        public readonly Vector3 Right;
        public readonly Vector3 Up;

        public ScsCameraPose(
            double x,
            double y,
            double z,
            Vector3 forward,
            Vector3 right,
            Vector3 up)
        {
            X = x;
            Y = y;
            Z = z;
            Forward = NormalizeSafe(forward, new Vector3(0, 0, -1));
            Right = NormalizeSafe(right, new Vector3(1, 0, 0));
            Up = NormalizeSafe(up, new Vector3(0, 1, 0));
        }

        public static bool TryCreate(
            double truckX,
            double truckY,
            double truckZ,
            ScsEuler truckOrientation,
            Vector3 cabinPosition,
            ScsEuler cabinOffsetOrientation,
            Vector3 cabinOffsetPosition,
            Vector3 headPosition,
            ScsEuler headOffsetOrientation,
            Vector3 headOffsetPosition,
            out ScsCameraPose pose)
        {
            // ------------------------------------------------------------
            // ПОЛОЖЕНИЕ — точно по официальному SCS telemetry_position:
            // head_cabin = head.position + head.offset.position
            // head_vehicle = cabin.position + cabin.offset.position
            //                 + rotate(cabin.offset.orientation, head_cabin)
            // head_world = truck.world.position
            //              + rotate(truck.world.orientation, head_vehicle)
            // ------------------------------------------------------------
            Vector3 headInCabin = headPosition + headOffsetPosition;

            Vector3 rotatedHeadInVehicle =
                Rotate(headInCabin, cabinOffsetOrientation);

            Vector3 headInVehicle =
                cabinPosition + cabinOffsetPosition + rotatedHeadInVehicle;

            Vector3 headWorldOffset =
                Rotate(headInVehicle, truckOrientation);

            double camX = truckX + headWorldOffset.X;
            double camY = truckY + headWorldOffset.Y;
            double camZ = truckZ + headWorldOffset.Z;

            // ------------------------------------------------------------
            // ОРИЕНТАЦИЯ — та же иерархия вращений.
            // Базовая голова смотрит по локальной оси -Z.
            // head offset -> cabin offset -> truck world orientation.
            // ------------------------------------------------------------
            Vector3 forward = new(0, 0, -1);
            Vector3 right = new(1, 0, 0);
            Vector3 up = new(0, 1, 0);

            forward = Rotate(forward, headOffsetOrientation);
            right = Rotate(right, headOffsetOrientation);
            up = Rotate(up, headOffsetOrientation);

            forward = Rotate(forward, cabinOffsetOrientation);
            right = Rotate(right, cabinOffsetOrientation);
            up = Rotate(up, cabinOffsetOrientation);

            forward = Rotate(forward, truckOrientation);
            right = Rotate(right, truckOrientation);
            up = Rotate(up, truckOrientation);

            // Устраняем накопленную float-погрешность и делаем basis ортонормальным.
            forward = NormalizeSafe(forward, new Vector3(0, 0, -1));

            right = right - forward * Vector3.Dot(right, forward);
            right = NormalizeSafe(right, new Vector3(1, 0, 0));

            up = up
                 - forward * Vector3.Dot(up, forward)
                 - right * Vector3.Dot(up, right);
            up = NormalizeSafe(up, new Vector3(0, 1, 0));

            pose = new ScsCameraPose(
                camX,
                camY,
                camZ,
                forward,
                right,
                up);

            return true;
        }

        /// <summary>
        /// ТОЧНАЯ SCS rotate(), повторяющая официальный пример SCS SDK.
        /// Roll Z -> Pitch X -> Heading Y.
        /// Все углы — доли оборота.
        /// </summary>
        public static Vector3 Rotate(Vector3 vector, ScsEuler orientation)
        {
            double heading = orientation.Heading * Math.PI * 2.0;
            double pitch = orientation.Pitch * Math.PI * 2.0;
            double roll = orientation.Roll * Math.PI * 2.0;

            double cosHeading = Math.Cos(heading);
            double sinHeading = Math.Sin(heading);
            double cosPitch = Math.Cos(pitch);
            double sinPitch = Math.Sin(pitch);
            double cosRoll = Math.Cos(roll);
            double sinRoll = Math.Sin(roll);

            // Roll around Z.
            double postRollX = vector.X * cosRoll - vector.Y * sinRoll;
            double postRollY = vector.X * sinRoll + vector.Y * cosRoll;
            double postRollZ = vector.Z;

            // Pitch around X.
            double postPitchX = postRollX;
            double postPitchY = postRollY * cosPitch - postRollZ * sinPitch;
            double postPitchZ = postRollY * sinPitch + postRollZ * cosPitch;

            // Heading around Y.
            double x = postPitchX * cosHeading + postPitchZ * sinHeading;
            double y = postPitchY;
            double z = -postPitchX * sinHeading + postPitchZ * cosHeading;

            return new Vector3((float)x, (float)y, (float)z);
        }

        private static Vector3 NormalizeSafe(Vector3 v, Vector3 fallback)
        {
            float lenSq = v.LengthSquared();
            if (!float.IsFinite(lenSq) || lenSq < 1e-12f)
                return fallback;

            return Vector3.Normalize(v);
        }
    }
}
```

---

# 2. Полностью заменить `AR/ArGameState.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    public sealed class ArMarker
    {
        public string GameName = "";
        public string RealName = "";
        public double X, Y, Z;
        public double Dist;
        public string Kind = "poi";
        public string Category = "";
        public string Color = "";
    }

    public sealed class ArGameState
    {
        // Truck sequence.
        public long Sequence;

        // ВАЖНО: CamX/Y/Z теперь ВСЕГДА означают мировое положение ГЛАЗА/КАМЕРЫ,
        // а не truck.world.placement.
        public double CamX, CamY, CamZ;

        // Полная мировая ориентация камеры.
        public Vector3 CameraForward = new(0, 0, -1);
        public Vector3 CameraRight = new(1, 0, 0);
        public Vector3 CameraUp = new(0, 1, 0);
        public bool CameraPoseValid;

        // Оставить для существующих диагностических экранов/совместимости.
        public double YawBase;
        public double PitchBody;
        public double Roll;
        public double YawHead;
        public double PitchHead;

        // Высота reference point грузовика — НЕ камера.
        public double GroundY;

        public double PlaneOffsetM;
        public bool ShowGrid;

        public ArMarker? Target;
        public (double X, double Y, double Z)? Pin;

        public IReadOnlyList<(double X, double Y, double Z)> Cities =
            Array.Empty<(double, double, double)>();
    }
}
```

---

# 3. Полностью заменить `AR/CabinArProjection.cs`

Убрать полностью старую реализацию с `EyeHeightM` и `EnablePitchCompensation`.

```csharp
using System;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// ПОЛНАЯ pinhole-проекция мировой точки через реальную 6DoF-позу камеры.
    /// Никаких truck-pitch/head-pitch компенсаций и фиксированной высоты глаз.
    /// </summary>
    public sealed class CabinArProjection
    {
        public double CabinFovDegrees { get; set; } = 100.0;

        // 0.5 = геометрический центр viewport.
        public double ProjectionCenterX { get; set; } = 0.5;
        public double ProjectionCenterY { get; set; } = 0.5;

        public (float u, float v, double depth) Project(
            double worldX,
            double worldY,
            double worldZ,
            double camX,
            double camY,
            double camZ,
            Vector3 cameraForward,
            Vector3 cameraRight,
            Vector3 cameraUp,
            int screenWidth,
            int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return (0, 0, double.NegativeInfinity);

            double dx = worldX - camX;
            double dy = worldY - camY;
            double dz = worldZ - camZ;

            // World -> camera coordinates через реальный basis камеры.
            double depth =
                dx * cameraForward.X +
                dy * cameraForward.Y +
                dz * cameraForward.Z;

            double right =
                dx * cameraRight.X +
                dy * cameraRight.Y +
                dz * cameraRight.Z;

            double up =
                dx * cameraUp.X +
                dy * cameraUp.Y +
                dz * cameraUp.Z;

            if (!double.IsFinite(depth) ||
                !double.IsFinite(right) ||
                !double.IsFinite(up))
            {
                return (0, 0, double.NaN);
            }

            // За камерой / слишком близко — не проектируем.
            if (depth <= 0.5)
                return (0, 0, depth);

            double fov = Math.Clamp(CabinFovDegrees, 10.0, 170.0);
            double halfTan = Math.Tan(fov * Math.PI / 180.0 * 0.5);
            if (!double.IsFinite(halfTan) || halfTan <= 1e-12)
                return (0, 0, depth);

            // Горизонтальный FOV.
            // Для квадратных пикселей тот же focal length в px применяется и по Y;
            // вертикальный FOV автоматически определяется aspect ratio.
            double focalPx = (screenWidth * 0.5) / halfTan;

            double cx = screenWidth * ProjectionCenterX;
            double cy = screenHeight * ProjectionCenterY;

            double u = cx + focalPx * (right / depth);
            double v = cy - focalPx * (up / depth);

            if (!double.IsFinite(u) || !double.IsFinite(v))
                return (0, 0, depth);

            return ((float)u, (float)v, depth);
        }
    }
}
```

---

# 4. `MainForm.ArTarget.cs` — добавить поля полной телеметрии камеры

В блоке полей телеметрии, рядом с `_arLastHead`, добавить:

```csharp
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
```

---

# 5. `MainForm.ArTarget.cs` — добавить helper-функции для SCS arrays

Внутрь `MainForm`, сразу перед `ApplyPlacementJson(...)`, вставить:

```csharp
private static JArray? FlatArray(JObject json, string key)
{
    if (json[key] is JArray direct)
        return direct;

    return json.SelectToken(key) as JArray;
}

private static bool TryReadVector3(
    JObject json,
    string key,
    out System.Numerics.Vector3 value)
{
    value = System.Numerics.Vector3.Zero;

    var a = FlatArray(json, key);
    if (a == null || a.Count < 3)
        return false;

    try
    {
        double x = a[0].Value<double>();
        double y = a[1].Value<double>();
        double z = a[2].Value<double>();

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return false;

        value = new System.Numerics.Vector3(
            (float)x,
            (float)y,
            (float)z);

        return true;
    }
    catch
    {
        return false;
    }
}

private static bool TryReadScsPlacement(
    JObject json,
    string key,
    out System.Numerics.Vector3 position,
    out AR.ScsEuler orientation)
{
    position = System.Numerics.Vector3.Zero;
    orientation = AR.ScsEuler.Identity;

    var a = FlatArray(json, key);
    if (a == null || a.Count < 6)
        return false;

    try
    {
        double x = a[0].Value<double>();
        double y = a[1].Value<double>();
        double z = a[2].Value<double>();
        double h = a[3].Value<double>();
        double p = a[4].Value<double>();
        double r = a[5].Value<double>();

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            !double.IsFinite(h) || !double.IsFinite(p) || !double.IsFinite(r))
            return false;

        position = new System.Numerics.Vector3(
            (float)x,
            (float)y,
            (float)z);

        orientation = new AR.ScsEuler(h, p, r);
        return true;
    }
    catch
    {
        return false;
    }
}

private void RebuildArCameraPose()
{
    if (!_arTruckKnown || !_arHeadPositionKnown)
    {
        _arCameraPoseValid = false;
        return;
    }

    if (!AR.ScsCameraPose.TryCreate(
            _arTruckX,
            _arTruckY,
            _arTruckZ,
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
```

---

# 6. `MainForm.ArTarget.cs` — заменить `ApplyPlacementJson(...)` целиком

Удалить текущий метод `ApplyPlacementJson(JObject json, string source)` и вставить вместо него:

```csharp
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
        // STATIC/CONFIG POSITION DATA
        // TruckTel exposes these through the same data namespace.
        // ------------------------------------------------------------
        if (TryReadVector3(json, "truck.cabin.position", out var cabinPos))
        {
            changed |= Vector3Changed(_arCabinPosition, cabinPos);
            _arCabinPosition = cabinPos;
            anyData = true;
        }

        if (TryReadScsPlacement(
                json,
                "truck.cabin.offset",
                out var cabinOffsetPos,
                out var cabinOffsetOrientation))
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

        if (TryReadScsPlacement(
                json,
                "truck.head.offset",
                out var headOffsetPos,
                out var headOffsetOrientation))
        {
            changed |= Vector3Changed(_arHeadOffsetPosition, headOffsetPos);
            changed |= EulerChanged(_arHeadOffsetOrientation, headOffsetOrientation);

            _arHeadOffsetPosition = headOffsetPos;
            _arHeadOffsetOrientation = headOffsetOrientation;

            // Оставляем raw-массив для совместимости со старым payload.
            var rawHead = FlatArray(json, "truck.head.offset");
            if (rawHead != null)
                _arLastHead = new JArray(rawHead);

            anyData = true;
        }

        if (!anyData)
        {
            if ((DateTime.Now - _arSrcLogAt).TotalMilliseconds > 5000)
            {
                _arSrcLogAt = DateTime.Now;
                Logger.Current?.Data(
                    $"[AR] нет применимых telemetry-полей в источнике '{source}'.");
            }
            return;
        }

        RebuildArCameraPose();

        // Любое изменение положения/ориентации/головы/кабины = новый camera pose.
        if (_arCameraPoseValid)
            changed = true;

        _arTruckChanged = _arTruckChanged || changed;

        // УСПЕШНЫЙ кадр без отдельного requirement на placement.
        // Это важно: head.offset/cabin.offset могут приходить отдельным delta-пакетом.
        _arTruckLastSeen = DateTime.Now;

        if (_arCameraPoseValid)
        {
            PublishArV2Snapshot();
        }

        if ((DateTime.Now - _arSrcOkLogAt).TotalMilliseconds > 5000)
        {
            _arSrcOkLogAt = DateTime.Now;
            Logger.Current?.Data(
                $"[AR] pose '{source}': " +
                $"truck={_arTruckX:F2},{_arTruckY:F2},{_arTruckZ:F2} " +
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

private static bool Vector3Changed(
    System.Numerics.Vector3 a,
    System.Numerics.Vector3 b)
{
    return Vector3.DistanceSquared(a, b) > 1e-8f;
}

private static bool EulerChanged(
    AR.ScsEuler a,
    AR.ScsEuler b)
{
    return Math.Abs(a.Heading - b.Heading) > 0.000001 ||
           Math.Abs(a.Pitch - b.Pitch) > 0.000001 ||
           Math.Abs(a.Roll - b.Roll) > 0.000001;
}
```

`MainForm.ArTarget.cs` также должен иметь `using System.Numerics;`, либо оставить в коде полные имена, как выше.

---

# 7. `MainForm.ArTarget.cs` — заменить `PublishArV2Snapshot()`

Полностью заменить текущий метод на:

```csharp
private void PublishArV2Snapshot()
{
    try
    {
        if (!_arTruckKnown || !_arCameraPoseValid)
            return;

        var s = new AR.ArGameState
        {
            Sequence = _arPoseSequence,

            // Это ТЕПЕРЬ реальные мировые координаты глаз.
            CamX = _arCameraPose.X,
            CamY = _arCameraPose.Y,
            CamZ = _arCameraPose.Z,

            CameraForward = _arCameraPose.Forward,
            CameraRight = _arCameraPose.Right,
            CameraUp = _arCameraPose.Up,
            CameraPoseValid = true,

            YawBase = _arHeading,
            PitchBody = _arPitch,
            Roll = _arRoll,

            GroundY = _arTruckY,
            PlaneOffsetM = AR.ArBridge.PlaneOffsetM,
            ShowGrid = AR.ArBridge.ShowGrid
        };

        if (_arHeadOffsetOrientation != AR.ScsEuler.Identity)
        {
            s.YawHead = _arHeadOffsetOrientation.Heading;
            s.PitchHead = _arHeadOffsetOrientation.Pitch;
        }

        if (_arPin.HasValue)
        {
            // Существующую pin-функцию НЕ ломать.
            // Это отдельная точка world space; она больше не задаёт положение камеры.
            var pin = _arPin.Value;
            s.Pin = (pin.x, pin.y, pin.z);
        }

        // Cities можно оставить для старого UI/диагностики,
        // но они НЕ участвуют в world-to-screen camera projection.
        if (_arPoints.Count > 0)
        {
            var cities = new List<(double, double, double)>();
            foreach (var it in _arPoints)
            {
                if (it.kind != "city" || Math.Abs(it.y) < 0.001)
                    continue;

                double d2 =
                    (it.x - _arTruckX) * (it.x - _arTruckX) +
                    (it.z - _arTruckZ) * (it.z - _arTruckZ);

                if (d2 > 5000.0 * 5000.0)
                    continue;

                cities.Add((it.x, it.y + ArCityHeightCorrectionM, it.z));
            }

            s.Cities = cities;
        }

        if (_arV2Target != null)
            s.Target = _arV2Target;

        AR.ArBridge.PublishTelemetry(s);
        AR.ArBridge.MarkPublished();
    }
    catch (Exception ex)
    {
        Logger.Current?.Data($"[AR] PublishArV2Snapshot error: {ex.Message}");
    }
}
```

---

# 8. `MainForm.ArTarget.cs` — payload `ar_telemetry` должен передавать готовую камеру

В `ArUpdateTick()` найти текущий блок:

```csharp
if (_arTruckChanged && _arLastHead != null && _arLastHead.Count >= 4)
{
    var tel = new JObject
    {
        ["placement"] = new JArray(_arTruckX, _arTruckY, _arTruckZ, _arHeading, _arPitch, _arRoll),
        ["head"] = _arLastHead
    };
```

Заменить начало блока на:

```csharp
if (_arTruckChanged && _arCameraPoseValid)
{
    var tel = new JObject
    {
        ["placement"] = new JArray(
            _arTruckX,
            _arTruckY,
            _arTruckZ,
            _arHeading,
            _arPitch,
            _arRoll),

        ["camera"] = new JObject
        {
            ["position"] = new JArray(
                _arCameraPose.X,
                _arCameraPose.Y,
                _arCameraPose.Z),

            ["forward"] = new JArray(
                _arCameraPose.Forward.X,
                _arCameraPose.Forward.Y,
                _arCameraPose.Forward.Z),

            ["right"] = new JArray(
                _arCameraPose.Right.X,
                _arCameraPose.Right.Y,
                _arCameraPose.Right.Z),

            ["up"] = new JArray(
                _arCameraPose.Up.X,
                _arCameraPose.Up.Y,
                _arCameraPose.Up.Z),

            ["fovDeg"] = AR.ArBridge.FovDegrees
        }
    };

    if (_arLastHead != null)
        tel["head"] = new JArray(_arLastHead);
```

Остальную часть текущего блока (`pin`, `cities`, `SendCommandToMap`, `_arTruckChanged = false`) оставить.

---

# 9. `MainForm.ArTarget.cs` — увеличить частоту AR-канала

Заменить:

```csharp
private const int ArUpdateIntervalMs = 33;
```

на:

```csharp
private const int ArUpdateIntervalMs = 16;
```

И заменить URL:

```csharp
ws://localhost:{port}/api/ws/delta/flat/?throttle=50
```

на:

```csharp
ws://localhost:{port}/api/ws/delta/flat/?throttle=16
```

Это не меняет геометрию, а уменьшает temporal latency между pose update и overlay.

---

# 10. `AR/ArRenderer.cs` — полностью убрать старую A/B-проекцию

Найти блок:

```csharp
public static bool UsePinholeProjection = true;
public static double CabinFovDegrees = 100.0;
private readonly CabinArProjection _pinhole = new();
```

Заменить на:

```csharp
private readonly CabinArProjection _pinhole = new();
```

Полностью удалить `UsePinholeProjection`.

Полностью удалить старый fallback v85/копию JS.

---

# 11. `AR/ArRenderer.cs` — полностью заменить `ProjectPoint(...)`

Удалить текущий `ProjectPoint(...)` и вставить:

```csharp
private (float u, float v, bool inFront, double dist, double depth)? ProjectPoint(
    double wx,
    double wy,
    double wz,
    ArGameState? s)
{
    if (s == null || !s.CameraPoseValid)
        return null;

    _pinhole.CabinFovDegrees = ArBridge.FovDegrees;

    var r = _pinhole.Project(
        wx,
        wy,
        wz,
        s.CamX,
        s.CamY,
        s.CamZ,
        s.CameraForward,
        s.CameraRight,
        s.CameraUp,
        _width,
        _height);

    bool inFront = r.depth > 0.5;

    double dx = wx - s.CamX;
    double dy = wy - s.CamY;
    double dz = wz - s.CamZ;

    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

    if (!double.IsFinite(dist))
        return null;

    return (r.u, r.v, inFront, dist, r.depth);
}
```

После этого в `ArRenderer` больше не должно быть ручной математики вида:

```text
yawBody + yawHead
pitchBody + pitchHead
EyeHeight
forwardness
PitchCompensation
```

Вся world->camera геометрия должна проходить только через `CameraForward/Right/Up`.

---

# 12. `AR/ArRenderer.cs` — добавить реальную линию мирового горизонта

Вставить в `ArRenderer` сразу перед `RenderFrame()` новый метод:

```csharp
private void DrawWorldHorizon(ArGameState s)
{
    if (!s.CameraPoseValid || _width <= 0 || _height <= 0)
        return;

    double fov = Math.Clamp(ArBridge.FovDegrees, 10.0, 170.0);
    double halfTan = Math.Tan(fov * Math.PI / 180.0 * 0.5);
    if (!double.IsFinite(halfTan) || Math.Abs(halfTan) < 1e-12)
        return;

    double f = (_width * 0.5) / halfTan;
    double cx = _width * 0.5;
    double cy = _height * 0.5;

    // Горизонт = все world-space лучи, ортогональные мировому Up=(0,1,0).
    // Для screen pixel:
    // rayWorld = Forward + Right*x + Up*((cy-v)/f)
    // rayWorld.Y = 0
    // => v = cy + f * (Forward.Y + Right.Y*x) / Up.Y
    double fy = s.CameraForward.Y;
    double ry = s.CameraRight.Y;
    double uy = s.CameraUp.Y;

    const double eps = 1e-7;

    if (Math.Abs(uy) > eps)
    {
        double x0 = (0.0 - cx) / f;
        double x1 = (_width - cx) / f;

        double y0 = cy + f * (fy + ry * x0) / uy;
        double y1 = cy + f * (fy + ry * x1) / uy;

        if (double.IsFinite(y0) && double.IsFinite(y1))
        {
            DrawLine(
                0f,
                (float)y0,
                (float)_width,
                (float)y1,
                0.1f,
                0.95f,
                1.0f,
                0.90f);
        }
    }
    else if (Math.Abs(ry) > eps)
    {
        // Дегenerate case: горизонт проходит вертикально через экран.
        double x = cx - f * fy / ry;

        if (double.IsFinite(x))
        {
            DrawLine(
                (float)x,
                0f,
                (float)x,
                (float)_height,
                0.1f,
                0.95f,
                1.0f,
                0.90f);
        }
    }
}
```

---

# 13. `AR/ArRenderer.cs` — отрисовывать горизонт до маркеров

В `RenderFrame()` сразу после:

```csharp
ctx.ClearRenderTargetView(_rtv, new Color4(0f, 0f, 0f, 0f));
```

добавить:

```csharp
try
{
    if (state != null && state.CameraPoseValid)
        DrawWorldHorizon(state);
}
catch
{
    // Диагностическая линия не должна ломать AR render loop.
}
```

Линия должна рисоваться до маркеров/текста/pin.

---

# 14. `AR/ArRenderer.cs` — отключить перспективный warp для проверки геометрии

До первого успешного теста полного camera pose оставить `PerspectiveCalibrationWarp` выключенным.

Если в `RenderFrame()` или marker pipeline применяется `_gridWarp` к экранным координатам, временно не применять его к AR-маркерам.

Нужный режим первой проверки:

`World point -> exact camera pose -> pinhole -> screen`

без дополнительного homography.

После совпадения геометрии perspective-warp можно вернуть отдельным опциональным шагом.

---

# 15. `data/js/ar_hud.js` — перейти на готовую мировую camera pose

## 15.1. В `CFG` удалить camera hacks

Удалить:

```javascript
eyeHeight: 1.9,
groundOffset: 0.5,
smooth: 0.25,
headPitchSign: 1,
pinSmooth: 0.35,
pinLead: 0.6,
```

Добавить:

```javascript
showHorizon: true,
horizonAlpha: 0.90,
```

`groundOffset` не использовать в world-to-screen projection.

---

# 16. `data/js/ar_hud.js` — заменить состояние `ar`

В объект `ar` добавить/заменить camera fields на:

```javascript
const ar = {
    camX: 0, camY: 0, camZ: 0,

    cameraForward: { x: 0, y: 0, z: -1 },
    cameraRight:   { x: 1, y: 0, z: 0 },
    cameraUp:      { x: 0, y: 1, z: 0 },
    cameraValid: false,

    fovDeg: 100,
    projectionCenterX: 0.5,
    projectionCenterY: 0.5,

    yawBase: 0,
    yawHead: 0,
    pitchHead: 0,
    headPitchRaw: 0,
    pitch: 0,
    roll: 0,

    haveTruck: false,
    haveHead: false,
    haveHeadPitch: false,

    lastTelemetryAt: 0,
    target: null,
    pin: null,
    cities: [],
    sel: null
};
```

---

# 17. `data/js/ar_hud.js` — заменить `applyArTelemetry(data)`

Полностью заменить функцию на:

```javascript
function readVec3(a, fallback) {
    if (!Array.isArray(a) || a.length < 3) return fallback;

    const x = Number(a[0]);
    const y = Number(a[1]);
    const z = Number(a[2]);

    if (![x, y, z].every(Number.isFinite)) return fallback;
    return { x, y, z };
}

function applyArTelemetry(data) {
    const camera = data.camera;

    if (camera && typeof camera === 'object') {
        const p = readVec3(camera.position, null);
        const fwd = readVec3(camera.forward, null);
        const right = readVec3(camera.right, null);
        const up = readVec3(camera.up, null);

        if (p && fwd && right && up) {
            ar.camX = p.x;
            ar.camY = p.y;
            ar.camZ = p.z;

            ar.cameraForward = fwd;
            ar.cameraRight = right;
            ar.cameraUp = up;
            ar.cameraValid = true;

            const fov = Number(camera.fovDeg);
            if (Number.isFinite(fov))
                ar.fovDeg = Math.max(10, Math.min(170, fov));

            ar.haveTruck = true;
            ar.lastTelemetryAt = performance.now();
        }
    }

    // Legacy telemetry fields остаются только для диагностики.
    const p = data.placement;
    if (Array.isArray(p) && p.length >= 6) {
        ar.yawBase = Number(p[3]) || 0;
        ar.pitch = Number(p[4]) || 0;
        ar.roll = Number(p[5]) || 0;
        ar.haveTruck = ar.cameraValid;
    }

    const h = data.head;
    if (Array.isArray(h) && h.length >= 4) {
        ar.yawHead = (Number(h[3]) || 0) * Math.PI * 2;
        ar.haveHead = true;

        if (h.length >= 5) {
            ar.headPitchRaw = Number(h[4]) || 0;
            ar.pitchHead = ar.headPitchRaw * Math.PI * 2;
            ar.haveHeadPitch = true;
        }
    }

    // Только для старой диагностики UI.
    if (Array.isArray(data.cities)) {
        ar.cities = data.cities.map(c => ({
            x: Number(c.x) || 0,
            y: Number(c.y) || 0,
            z: Number(c.z) || 0
        }));
    }
}
```

---

# 18. `data/js/ar_hud.js` — полностью заменить `projectPoint(pt, cam)`

```javascript
function projectPoint(pt, cam) {
    const c = cam || ar;

    if (!c.cameraValid)
        return { dist: Infinity, u: 0, v: 0, inFront: false, depth: -Infinity };

    const dx = Number(pt.x) - c.camX;
    const dy = Number(pt.y) - c.camY;
    const dz = Number(pt.z) - c.camZ;

    const fwd = c.cameraForward;
    const right = c.cameraRight;
    const up = c.cameraUp;

    const depth =
        dx * fwd.x +
        dy * fwd.y +
        dz * fwd.z;

    const rdot =
        dx * right.x +
        dy * right.y +
        dz * right.z;

    const udot =
        dx * up.x +
        dy * up.y +
        dz * up.z;

    const dist = Math.hypot(dx, dy, dz);

    if (!Number.isFinite(depth) ||
        !Number.isFinite(rdot) ||
        !Number.isFinite(udot)) {
        return { dist, u: 0, v: 0, inFront: false, depth };
    }

    const halfTan = Math.tan((c.fovDeg * Math.PI / 180) / 2);
    const f = (W * 0.5) / halfTan;

    const cx = W * (c.projectionCenterX || 0.5);
    const cy = H * (c.projectionCenterY || 0.5);

    if (depth <= 0.5) {
        return {
            dist,
            u: rdot >= 0 ? Infinity : -Infinity,
            v: udot >= 0 ? -Infinity : Infinity,
            inFront: false,
            depth
        };
    }

    const u = cx + f * (rdot / depth);
    const v = cy - f * (udot / depth);

    return {
        dist,
        u,
        v,
        inFront: true,
        depth
    };
}
```

---

# 19. `data/js/ar_hud.js` — убрать prediction/экстраполяцию camera

Удалить полностью блок:

```javascript
// ---- Экстраполяция КАМЕРЫ ...
const nowT = performance.now();
...
let exCam = ar;
...
```

Заменить на:

```javascript
const exCam = ar;
```

Не интерполировать camera basis между кадрами.

---

# 20. `data/js/ar_hud.js` — убрать зеркалирование pin

Найти:

```javascript
let pu = Number.isFinite(pPr.u) ? (W - pPr.u) : ...;
```

Удалить `W - pPr.u`.

Использовать:

```javascript
let pu = Number.isFinite(pPr.u)
    ? pPr.u
    : (pPr.u > 0 ? CFG.edgeMargin : W - CFG.edgeMargin);

let pv = Number.isFinite(pPr.v)
    ? pPr.v
    : (pPr.v > 0 ? H - CFG.edgeMargin : CFG.edgeMargin);
```

Удалить весь блок `pinSmooth` / `pinLead` / `_pinSm` / `_pinPrevU` / `_pinPrevV`.

Pin должен отображаться по свежей геометрической проекции каждый кадр.

---

# 21. `data/js/ar_hud.js` — убрать экранное сглаживание target

Удалить:

```javascript
let _sm = null;
```

и весь блок:

```javascript
if (!_sm || _sm.ident !== ident) {
    ...
} else {
    ...
}
```

Вместо него:

```javascript
ar.sel = {
    u: cl.u,
    v: cl.v,
    clamped: cl.clamped,
    inFront: pr.inFront
};

const drawU = cl.u;
const drawV = cl.v;
```

Все marker/text/crosshair места, где используется `_sm.u`/`_sm.v`, заменить на `drawU`/`drawV`.

Сглаживание допускается только для размера/alpha, но не для геометрической экранной позиции.

---

# 22. `data/js/ar_hud.js` — полностью убрать высотную эвристику city/truck

Удалить:

```javascript
const _ySmooth = new Map();
function nearestCityY(...) { ... }
function displayYFor(...) { ... }
```

И удалить из render loop:

```javascript
const dist2d = ...;
ar.target.dispY = displayYFor(...);
```

`projectPoint()` должен получать мировую точку напрямую:

```javascript
const pr = projectPoint(ar.target, exCam);
```

Y точки не менять перед проектированием.

Если конкретная точка имеет `Y=0`, это означает, что у неё нет точной высоты; такую точку нельзя искусственно поднимать/опускать через камеру/город.

---

# 23. `data/js/ar_hud.js` — добавить отрисовку мирового горизонта

Перед `render()` вставить:

```javascript
function drawWorldHorizon(cam) {
    if (!CFG.showHorizon || !cam.cameraValid) return;

    const fov = Math.max(10, Math.min(170, Number(cam.fovDeg) || 100));
    const halfTan = Math.tan((fov * Math.PI / 180) / 2);
    if (!Number.isFinite(halfTan) || Math.abs(halfTan) < 1e-12) return;

    const f = (W * 0.5) / halfTan;
    const cx = W * (cam.projectionCenterX || 0.5);
    const cy = H * (cam.projectionCenterY || 0.5);

    const fy = cam.cameraForward.y;
    const ry = cam.cameraRight.y;
    const uy = cam.cameraUp.y;

    const eps = 1e-7;

    ctx.save();
    ctx.strokeStyle = 'rgba(0,255,255,' + CFG.horizonAlpha + ')';
    ctx.lineWidth = 2;
    ctx.beginPath();

    if (Math.abs(uy) > eps) {
        const x0 = (0 - cx) / f;
        const x1 = (W - cx) / f;

        const y0 = cy + f * (fy + ry * x0) / uy;
        const y1 = cy + f * (fy + ry * x1) / uy;

        if (Number.isFinite(y0) && Number.isFinite(y1)) {
            ctx.moveTo(0, y0);
            ctx.lineTo(W, y1);
        }
    } else if (Math.abs(ry) > eps) {
        const x = cx - f * fy / ry;
        if (Number.isFinite(x)) {
            ctx.moveTo(x, 0);
            ctx.lineTo(x, H);
        }
    }

    ctx.stroke();
    ctx.restore();
}
```

---

# 24. `data/js/ar_hud.js` — вызвать горизонт до marker drawing

В начале `render()`, после очистки canvas:

```javascript
ctx.clearRect(0, 0, W, H);
```

добавить:

```javascript
if (CFG.showHorizon)
    drawWorldHorizon(ar);
```

---

# 25. `data/js/ar_hud.js` — исправить ожидание telemtry

Текущая проверка:

```javascript
if (!ar.haveTruck || !ar.target) return;
```

заменить на:

```javascript
if (!ar.cameraValid || !ar.target) return;
```

Pin должен отрисовываться при `ar.cameraValid`, даже если `target == null`.

---

# 26. Что НЕ менять в этой задаче

Не менять:

- модель поиска ближайшей точки;
- карту/overrides;
- категории/цвета;
- размеры marker;
- текст marker;
- target selection;
- `PlaneOffsetM` как отдельную пользовательскую настройку, если она нужна старым функциям;
- `Cities` как совместимость старого интерфейса.

`Cities` и `PlaneOffsetM` не должны участвовать в расчёте camera pose и world-to-screen projection.

---

# 27. Обязательная диагностика

На каждом новом camera pose логировать не чаще 1 раза в 5 секунд:

```text
[AR] pose 'ws': truck=X,Y,Z camera=X,Y,Z fwd=X,Y,Z up=X,Y,Z
```

Добавить временную диагностику в AR HUD:

```text
CAM: x y z
FWD: x y z
RIGHT: x y z
UP: x y z
FOV: xx.x
```

Сравнивать именно эти значения, а не `heading/pitch/roll` по отдельности.

---

# 28. Тестовый порядок на калибровочном полигоне

1. Сделать ровный участок с длинной прямой дорогой/стеной для проверки горизонтальной линии.
2. Дополнительно сделать уклон вперёд/назад.
3. Сделать поперечный уклон.
4. Сделать диагональный склон.
5. На каждом участке проверить:
   - truck roll меняет наклон горизонта;
   - truck pitch меняет высоту/наклон горизонта;
   - head yaw двигает AR-точку влево/вправо;
   - head pitch двигает AR-точку вверх/вниз;
   - head roll наклоняет горизонт;
   - одновременно pitch + roll не дают отдельного «компенсационного» сдвига;
   - камера не находится на фиксированной высоте `placement.Y + 1.9/1.5`;
   - pin не зеркален по X;
   - target не получает сглаженный экранный lag.

---

# 29. Критерии успешности

## Камера

`CamX/Y/Z` должны быть фактическими мировыми координатами головы.

`CameraForward/Right/Up` должны быть взаимно ортогональны и иметь длину ≈ 1.

Проверять:

```text
|Forward| ≈ 1
|Right|   ≈ 1
|Up|      ≈ 1
Forward·Right ≈ 0
Forward·Up    ≈ 0
Right·Up      ≈ 0
```

## Горизонт

При полностью горизонтальной камере:

```text
horizonY ≈ screenHeight / 2
```

При чистом roll горизонтальная линия должна наклоняться вокруг центра.

При чистом pitch линия должна смещаться вверх/вниз.

При yaw линия должна оставаться горизонтальной и не менять вертикальное положение.

При диагональном truck attitude должна одновременно учитываться и вертикальная, и горизонтальная составляющая мирового Up через camera basis.

## AR points

Проекция должна быть исключительно:

```text
world point
    ↓
world - camera.position
    ↓
(dot Right, dot Up, dot Forward)
    ↓
pinhole FOV
    ↓
screen pixels
```

Не разрешать дополнительные `+eyeHeight`, `mirrorX`, `pitchCompensation`, city-height interpolation и screen-position smoothing.

---

# 30. Сборка

Следовать правилам репозитория: финальная сборка только через корневой `compile.ps1`.

После изменений:

```powershell
.\compile.ps1
```

Дополнительно проверить:

```powershell
git diff --check
```

и отсутствие старых символов:

```powershell
rg "EyeHeightM|EnablePitchCompensation|eyeHeight|pitchCompensation|displayYFor|nearestCityY|W - pPr.u|pinLead|pinSmooth|UsePinholeProjection" AR data/js MainForm.ArTarget.cs
```

По завершении старые camera hacks не должны участвовать в runtime projection.

---

# 31. Важное примечание по точности времени

Геометрия после этой правки является точной относительно последнего SCS telemetry sample. Полное отсутствие движения точки при быстром движении потребует отдельного этапа temporal sync/prediction; его не смешивать с геометрической коррекцией.
