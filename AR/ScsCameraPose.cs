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
    /// Forward — направление взгляда, единый вектор.
    /// Right   — экранное право, единый вектор.
    /// Up      — экранный верх, единый вектор.
    ///
    /// По SCS: локальный Z смотрит НАЗАД, поэтому локальный Forward = (0,0,-1).
    ///
    /// Источник формулы — официальный пример SCS SDK `telemetry_position`:
    ///   head_cabin   = head.position + head.offset.position
    ///   head_vehicle = cabin.position + cabin.offset.position
    ///                  + rotate(cabin.offset.orientation, head_cabin)
    ///   head_world   = truck.world.position
    ///                  + rotate(truck.world.orientation, head_vehicle)
    /// Ориентация — та же иерархия вращений (head offset → cabin offset → truck).
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

        /// <summary>
        /// Собирает позу из компонентов телеметрии (SCS hierarchy).
        /// Возвращает true при успехе; при вырожденных входных данных — false.
        /// </summary>
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
            pose = default;

            // ------------------------------------------------------------
            // ПОЛОЖЕНИЕ — точно по официальному SCS telemetry_position.
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

            if (!double.IsFinite(camX) || !double.IsFinite(camY) || !double.IsFinite(camZ))
                return false;

            // ------------------------------------------------------------
            // ОРИЕНТАЦИЯ — та же иерархия вращений.
            // Базовая голова смотрит по локальной оси -Z.
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

            // Ортонормализация: устраняем накопленную float-погрешность.
            forward = NormalizeSafe(forward, new Vector3(0, 0, -1));

            right -= forward * Vector3.Dot(right, forward);
            right = NormalizeSafe(right, new Vector3(1, 0, 0));

            up -= forward * Vector3.Dot(up, forward);
            up -= right * Vector3.Dot(up, right);
            up = NormalizeSafe(up, new Vector3(0, 1, 0));

            if (!IsFinite(forward) || !IsFinite(right) || !IsFinite(up))
                return false;

            pose = new ScsCameraPose(camX, camY, camZ, forward, right, up);
            return true;
        }

        /// <summary>
        /// ТОЧНАЯ SCS rotate(), повторяющая официальный пример SCS SDK.
        /// Порядок: Roll вокруг Z → Pitch вокруг X → Heading вокруг Y.
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

            // Roll вокруг Z.
            double postRollX = vector.X * cosRoll - vector.Y * sinRoll;
            double postRollY = vector.X * sinRoll + vector.Y * cosRoll;
            double postRollZ = vector.Z;

            // Pitch вокруг X.
            double postPitchX = postRollX;
            double postPitchY = postRollY * cosPitch - postRollZ * sinPitch;
            double postPitchZ = postRollY * sinPitch + postRollZ * cosPitch;

            // Heading вокруг Y.
            double x = postPitchX * cosHeading + postPitchZ * sinHeading;
            double y = postPitchY;
            double z = -postPitchX * sinHeading + postPitchZ * cosHeading;

            return new Vector3((float)x, (float)y, (float)z);
        }

        private static bool IsFinite(Vector3 v)
            => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static Vector3 NormalizeSafe(Vector3 v, Vector3 fallback)
        {
            if (!IsFinite(v))
                return fallback;

            float lenSq = v.LengthSquared();
            if (!float.IsFinite(lenSq) || lenSq < 1e-12f)
                return fallback;

            return Vector3.Normalize(v);
        }
    }
}
