using System;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    // ================================================================
    // v86: PINHOLE-ПРОЕКЦИЯ КАБИНЫ (по копилке «Определение текущей камеры
    // и коррекция FOV», разделы 2/4/5/8/9).
    //
    // ЗАДАЧА: устранить «плавание» меток при повороте головы. Копилка требует:
    //   ScreenPosition = Project(WorldPos, CamPos, CamRot, FOV, ScreenSize) —
    //   ПОЛНЫЙ пересчёт каждый кадр (без previous+delta и накопительных схем).
    //
    // Отличия от старой ProjectPoint (v85, копия projectPoint ar_hud.js):
    //   1) FOV = CabinFovDegrees (65°, конфиг, НЕ зашит) вместо 75;
    //   2) Горизонтальный FOV задаёт f, ВЕРТИКАЛЬНЫЙ выводится из aspect —
    //      «не использовать одинаковый FOV одновременно по X и Y»;
    //   3) ProjectionCenterX/Y (0.5/0.5 по умолчанию) — компенсация смещения
    //      точки зрения ETS2 относительно центра viewport;
    //   4) Разделение: projection ЗНАЕТ ТОЛЬКО геометрию (не WS/UI/lifetime).
    //
    // Камера-space: право = +X, вверх = +Y, ВПЕРЁД = −Z (правосторонняя,
    // как в D3D lookAt); depth = −Pcamera.Z (положителен перед камерой).
    // ================================================================
    public sealed class CabinArProjection
    {
        /// <summary>FOV кабины в градусах (ГОРИЗОНТАЛЬНЫЙ, конфигурируемый).
        /// v90: 75° — совпадает с эталоном ar_hud.js (CFG.fovDeg=75, подтверждён
        /// пользователем «практически идеально»).</summary>
        public double CabinFovDegrees { get; set; } = 100.0;   // v40.1: временная калибровка пользователя (было 95)

        /// <summary>Центр проекции в долях экрана (0.5 = центр viewport).</summary>
        public double ProjectionCenterX { get; set; } = 0.5;
        public double ProjectionCenterY { get; set; } = 0.5;

        /// <summary>
        /// v40: ВЕРТИКАЛЬНАЯ КОМПЕНСАЦИЯ ПИТЧА ГРУЗОВИКА.
        /// Чем ближе взгляд к ПРОДОЛЬНОЙ ОСИ грузовика — тем сильнее питч кузова
        /// влияет на вертикальное положение точки (горизонт наклонён). Когда
        /// смотрим В БОК (≈90° к оси) — питч кузова НЕ должен смещать боковую
        /// точку (её вертикальный угол задаёт только голова). Компенсация =
        /// ослабление питча кузова пропорционально боковому углу взгляда.
        /// Это «приклеивает» точки к мировым координатам при движении в гору/с горы.
        /// </summary>
        public static bool EnablePitchCompensation = true;

        /// <summary>
        /// Полная pinhole-проекция мировой точки на экран.
        /// v90: ТОЧНЫЙ ПОРТ projectPoint из ar_hud.js (эталон, подтверждён
        /// пользователем). Убраны: инверсии (v87) и двойной сдвиг центра (баг
        /// u/v — метки уезжали на пол-экрана вправо, «всегда за экраном»).
        /// Формула идентична JS: yaw НЕ инвертирован, f один для u и v (без
        /// aspect — как в JS), центр = W/2, H/2.
        /// camPose: yawBody/pitchBody — placement[3]/[4] (доля оборота),
        /// yawHead/pitchHead — head.offset[3]/[4] (доля оборота).
        /// Возвращает (u, v) в пикселях и depth (&gt;0 = перед камерой).
        /// </summary>
        public (float u, float v, double depth) Project(
            double worldX, double worldY, double worldZ,
            double camX, double camY, double camZ,
            double yawBody, double pitchBody, double yawHead, double pitchHead,
            int screenWidth, int screenHeight)
        {
            // 1) Высота относительно камеры (как в JS: wy = dispY - camY).
            // v95 КОРЕНЬ «метка на горизонте»: JS ставит глаза на
            // camY = placement + eyeHeight (1.9 м), а C# передавал placement.
            // Без +eyeH точка на земле (worldY=placement) давала wy=0 → горизонт.
            const double EyeHeightM = 1.5;   // v40.7: Actros — глаза 2.25 м от полотна − 0.75 (опорная точка)
            double eyeY = camY + EyeHeightM;
            double wy = worldY - eyeY;

            // 2) Yaw: НЕ инвертирован (как в JS). yawHead — доля оборота → ×2π.
            double yaw = yawBody * Math.PI * 2 + yawHead * Math.PI * 2;
            double sinY = Math.Sin(yaw), cosY = Math.Cos(yaw);
            double fwdX = -sinY, fwdZ = -cosY;      // вперёд (как на миникарте)
            double rightX = cosY, rightZ = -sinY;   // вправо

            double fdot0 = (worldX - camX) * fwdX + (worldZ - camZ) * fwdZ;
            double rdot = (worldX - camX) * rightX + (worldZ - camZ) * rightZ;

            // 3) Питч кузова (placement[4], доля оборота) — вокруг right.
            // v40 КОМПЕНСАЦИЯ: насколько точка «впереди» оси грузовика (0..1).
            //   Вперёд (к оси) → компенсация мала (вертикальный угол головы уже
            //   совпадает с питчем грузовика) → питч кузова применяем ПОЛНОСТЬЮ.
            //   В бок (90° к оси) → компенсация максимальна (питч кузова НЕ влияет
            //   на вертикаль боковой точки) → питч кузова почти обнуляем.
            double forwardness = 1.0;
            if (EnablePitchCompensation)
            {
                double mag = Math.Abs(fdot0) + Math.Abs(rdot);
                if (mag > 1e-9) forwardness = Math.Abs(fdot0) / mag;   // ↑ вперёд, ↓ вбок
                forwardness = forwardness * forwardness;               // резче к 0 в боку
            }
            double bodyPitch = pitchBody * Math.PI * 2 * forwardness;
            double cosB = Math.Cos(bodyPitch), sinB = Math.Sin(bodyPitch);
            double fwd1 = fdot0 * cosB + wy * sinB;
            double up1 = wy * cosB - fdot0 * sinB;

            // 4) Питч головы (head.offset[4], доля оборота) — добавляется сверху.
            double headPitch = pitchHead * Math.PI * 2;
            double cosH = Math.Cos(headPitch), sinH = Math.Sin(headPitch);
            double depth = fwd1 * cosH + up1 * sinH;
            double up = up1 * cosH - fwd1 * sinH;

            if (depth <= 0.5) return (0, 0, depth);

            // 5) PINHOLE: f = (W*0.5)/tan(FOV/2) — ОДИН f для u и v (как в JS).
            double halfTan = Math.Tan(CabinFovDegrees * Math.PI / 180.0 / 2.0);
            double f = (screenWidth * 0.5) / halfTan;

            double u = screenWidth / 2.0 + f * (rdot / depth);
            double v = screenHeight / 2.0 - f * (up / depth);

            if (!double.IsFinite(u) || !double.IsFinite(v)) return (0, 0, depth);
            return ((float)u, (float)v, depth);
        }
    }
}