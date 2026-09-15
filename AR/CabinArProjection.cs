// ================================================================
// v1.0.40.30 (ETS2_AR_CAMERA_POSE_IMPLEMENTATION): ПОЛНАЯ pinhole-проекция
// мировой точки через РЕАЛЬНУЮ 6DoF-позу камеры.//
// УБРАНО ПОЛНОСТЬЮ (старые хаки, искажавшие геометрию):
//   - EyeHeightM 1.5 (камера больше НЕ на фиксированной высоте);
//   - EnablePitchCompensation и «forwardness²»;
//   - ручное складывание yawBody+yawHead / pitchBody+pitchHead.
//
// Единственный путь world → screen:
//   world − camera.position → (dot Right, dot Up, dot Forward) → pinhole FOV → px.
// Поза камеры приходит готовой (ScsCameraPose) — см. AR/ScsCameraPose.cs.
// ================================================================
using System;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    public sealed class CabinArProjection
    {
        /// <summary>FOV кабины в градусах (ГОРИЗОНТАЛЬНЫЙ, конфигурируемый).</summary>
        public double CabinFovDegrees { get; set; } = 100.0;

        /// <summary>Центр проекции в долях экрана (0.5 = геометрический центр viewport).</summary>
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

            // World -> camera через РЕАЛЬНЫЙ базис камеры.
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

            if (!double.IsFinite(depth) || !double.IsFinite(right) || !double.IsFinite(up))
                return (0, 0, double.NaN);

            // За камерой / слишком близко — не проектируем.
            if (depth <= 0.5)
                return (0, 0, depth);

            double fov = Math.Clamp(CabinFovDegrees, 10.0, 170.0);
            double halfTan = Math.Tan(fov * Math.PI / 180.0 * 0.5);
            if (!double.IsFinite(halfTan) || halfTan <= 1e-12)
                return (0, 0, depth);

            // Горизонтальный FOV задаёт focal length в px; по Y применяется тот же f
            // (квадратные пиксели) — вертикальный FOV выводится из aspect ratio.
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
