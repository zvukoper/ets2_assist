using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// РЕАЛЬНАЯ плоскость дороги, построенная по данным колёс TruckTel
    /// (задание «Ground Plane по колёсам для ETS2 Assist»).
    ///
    /// Источник истины (ключи телеметрии):
    ///   truck.world.placement                — опорная точка + ориентация кузова
    ///   truck.wheel.position[]               — центры колёс в ЛОКАЛЬНОЙ системе фуры
    ///   truck.wheel.radius[]                 — радиусы
    ///   truck.wheel.on_ground[]              — касание земли
    ///   truck.wheel.suspension.deflection[]  — ход подвески (диагностика)
    ///
    /// ИДЕЯ РАСЧЁТА (итеративная, как требует задание):
    ///   1) перевести wheel.position из vehicle space в world space (ScsCameraPose.Rotate);
    ///   2) начальное касание = центр колеса − мировой Up × radius;
    ///   3) fit плоскости Y = aX + bZ + c по касаниям;
    ///   4) нормаль = (−a, 1, −b);
    ///   5) пересчитать касание как wheelCenter − normal × radius;
    ///   6) повторить несколько итераций;
    ///   7) финальный fit + остатки (residual) каждого контакта.
    ///
    /// Почему это важно: прежняя «плоскость земли» была горизонтальной плоскостью
    /// на высоте опорной точки (GroundY + PlaneOffsetM). На уклонах/поперечных
    /// кренах это давало сдвиг высоты и «плавающие» точки. Теперь плоскость
    /// наклонная и соответствует реальному полотну.
    ///
    /// ТОЧНОСТЬ: fit выполняется в СИСТЕМЕ КООРДИНАТ ФУРЫ (координаты ±5 м),
    /// а не в мировых (≈160000) — иначе float потерял бы сантиметры.
    /// Коэффициенты хранятся в относительной системе; мировая высота считается
    /// как TruckY + A·(X−TruckX) + B·(Z−TruckZ) + C.
    /// </summary>
    public sealed class ArGroundPlane
    {
        // ================================================================
        // ДИАГНОСТИКА ПО КОЛЕСУ (сохраняется целиком, даже если колесо не на земле)
        // ================================================================
        public readonly struct WheelInfo
        {
            public readonly int Index;
            public readonly Vector3 LocalPosition;   // vehicle space (м)
            public readonly Vector3 WorldCenter;     // мировой центр колеса
            public readonly Vector3 Contact;         // мировая точка касания
            public readonly double Radius;
            public readonly bool OnGround;
            public readonly double SuspensionDeflection;
            public readonly double Residual;         // остаток относительно финальной плоскости

            public WheelInfo(
                int index, Vector3 localPosition, Vector3 worldCenter, Vector3 contact,
                double radius, bool onGround, double suspensionDeflection, double residual)
            {
                Index = index;
                LocalPosition = localPosition;
                WorldCenter = worldCenter;
                Contact = contact;
                Radius = radius;
                OnGround = onGround;
                SuspensionDeflection = suspensionDeflection;
                Residual = residual;
            }

            /// <summary>
            /// Продольная координата в системе фуры (м), «+вперёд».
            /// SCS local space: X вправо, Y вверх, Z НАЗАД → вперёд = −Z.
            /// </summary>
            public double LongitudinalM => -LocalPosition.Z;
        }

        public const int MaxIterations = 6;

        /// <summary>Минимум колёс, касающихся земли, для построения плоскости.</summary>
        public const int MinWheelsOnGround = 3;

        // ---- Плоскость (коэффициенты в СИСТЕМЕ ФУРЫ) ----
        private readonly double _truckX, _truckY, _truckZ;
        private readonly double _a, _b, _c;     // Y' = a·X' + b·Z' + c  (относительные коорд.)

        // ---- Мировой якорь ----
        public readonly double OriginX, OriginY, OriginZ;
        public readonly Vector3 Normal;         // единичная нормаль (вверх по полотну)
        public readonly Vector3 AxisU;          // ортонормальная ось сетки №1
        public readonly Vector3 AxisV;          // ортонормальная ось сетки №2

        public readonly double AverageWheelHeight;   // средняя мировая высота контактов
        public readonly double ReferenceHeight;      // высота плоскости ПОД опорной точкой фуры
        public readonly double MaxResidual;          // максимальный остаток контакта (м)
        public readonly int UsedWheelCount;
        public readonly bool Valid;

        // ================================================================
        // v1.0.40.37: ПРИЗНАК ГОРИЗОНТАЛЬНОЙ ПЛОСКОСТИ ВИЗУАЛИЗАЦИИ.
        //
        // Пользователь: «Плоскость и сетку рисуем ВСЕГДА ПО ГОРИЗОНТУ, чтобы даже
        // с креном на обочине носом вниз можно было прицельной точкой поставить
        // метку строго на плоскости земли. Плоскость земли будет параллельна
        // горизонту мира ВСЕГДА.» Оценка уклона полотна — отдельная задача;
        // для визуализации/постановки точки плоскость берётся горизонтальной.
        // ================================================================
        public readonly bool IsHorizontal;

        public readonly IReadOnlyList<WheelInfo> Wheels;

        private ArGroundPlane(
            double truckX, double truckY, double truckZ,
            double a, double b, double c,
            double originX, double originY, double originZ,
            Vector3 normal, Vector3 axisU, Vector3 axisV,
            double averageWheelHeight, double referenceHeight, double maxResidual,
            int usedWheelCount, IReadOnlyList<WheelInfo> wheels,
            bool isHorizontal = false)
        {
            _truckX = truckX; _truckY = truckY; _truckZ = truckZ;
            _a = a; _b = b; _c = c;
            OriginX = originX; OriginY = originY; OriginZ = originZ;
            Normal = normal; AxisU = axisU; AxisV = axisV;
            AverageWheelHeight = averageWheelHeight;
            ReferenceHeight = referenceHeight;
            MaxResidual = maxResidual;
            UsedWheelCount = usedWheelCount;
            Wheels = wheels;
            IsHorizontal = isHorizontal;
            Valid = true;
        }

        // ================================================================
        // v1.0.40.37: ПЛОСКОСТЬ ВИЗУАЛИЗАЦИИ — ВСЕГДА ПО ГОРИЗОНТУ.
        //
        // Пользователь пересмотрел требование: сетку больше НЕ строим по плоскости
        // колёс. Причина: с креном на обочине/носом вниз построенная по колёсам
        // плоскость уезжает от реального горизонта, и поставить точку «строго на
        // плоскости земли» прицелом невозможно (именно это и происходило).
        // Новая модель:
        //   • ПЛОСКОСТЬ ЗЕМЛИ — строго горизонтальна (параллельна горизонту мира)
        //     и проходит через высоту контакта колёс. Мировая вертикаль = (0,1,0),
        //     оси — мировые X/Z, поэтому на экране эта плоскость даёт ровно линию
        //     горизонта, и точка ставится корректно при ЛЮБОМ положении грузовика;
        //   • ОРИЕНТАЦИЯ ГРУЗОВИКА — показывается отдельно, осями шасси.
        //
        // Уклон полотна для этого НЕ нужен: высота берётся из контактов колёс
        // (AverageWheelHeight/ReferenceHeight), а наклон плоскости — ноль.
        // ================================================================
        public static ArGroundPlane CreateHorizontal(ArGroundPlane measured)
        {
            Vector3 normal = new Vector3(0f, 1f, 0f);

            // Мировой якорь — тот же принцип (ближайший целый узел), но высота
            // берётся с учётом горизонтальности: Y постоянна по всей плоскости.
            double ax = Math.Round(measured._truckX);
            double az = Math.Round(measured._truckZ);
            double originY = measured.AverageWheelHeight;

            // Оси — мировые X/Z (плоскость строго горизонтальна).
            Vector3 axisU = new Vector3(1f, 0f, 0f);
            Vector3 axisV = new Vector3(0f, 0f, 1f);

            // Колёса сохраняем как были (диагностика/визуализация высот), но
            // пересчитываем остаток относительно ГОРИЗОНТАЛЬНОЙ плоскости.
            var wheels = new List<WheelInfo>(measured.Wheels.Count);
            foreach (var w in measured.Wheels)
            {
                double residual = w.Contact.Y - originY;
                wheels.Add(new WheelInfo(
                    w.Index, w.LocalPosition, w.WorldCenter, w.Contact,
                    w.Radius, w.OnGround, w.SuspensionDeflection, residual));
            }

            // Максимальный остаток: насколько контакты реально расходятся с
            // горизонтальной плоскостью (это и есть мера уклона/крена полотна).
            double maxResidual = 0.0;
            foreach (var w in wheels)
            {
                if (!w.OnGround) continue;
                maxResidual = Math.Max(maxResidual, Math.Abs(w.Residual));
            }

            return new ArGroundPlane(
                measured._truckX, measured._truckY, measured._truckZ,
                // a=b=0 (плоскость горизонтальна); c подобран так, чтобы
                // HeightAtRelative давала ПОСТОЯННУЮ высоту originY при любых X/Z:
                //   truckY + 0 + 0 + (originY − truckY) = originY
                // Без этого HeightAt вернула бы truckY (высоту опорной точки), что
                // не совпадает с высотой контакта колёс.
                0.0, 0.0, originY - measured._truckY,
                ax, originY, az,
                normal, axisU, axisV,
                measured.AverageWheelHeight,
                originY,                            // высота под фурой = высота плоскости
                maxResidual,
                measured.UsedWheelCount,
                wheels,
                isHorizontal: true);
        }

        // ================================================================
        // ПОСТРОЕНИЕ
        // ================================================================
        /// <summary>
        /// Строит плоскость дороги по колёсам. false — данных недостаточно
        /// (меньше MinWheelsOnGround колёс на земле / вырожденная геометрия).
        /// </summary>
        public static bool TryBuild(
            double truckX, double truckY, double truckZ,
            ScsEuler truckOrientation,
            IReadOnlyList<Vector3>? wheelPositions,
            IReadOnlyList<double>? wheelRadii,
            IReadOnlyList<bool>? wheelOnGround,
            IReadOnlyList<double>? wheelSuspensionDeflection,
            out ArGroundPlane plane)
        {
            plane = null!;

            if (wheelPositions == null || wheelPositions.Count == 0) return false;
            if (!double.IsFinite(truckX) || !double.IsFinite(truckY) || !double.IsFinite(truckZ)) return false;

            int n = wheelPositions.Count;
            var local = new Vector3[n];
            var centerRel = new Vector3[n];       // центр колеса в системе фуры (мировая ориентация)
            var radius = new double[n];
            var onGround = new bool[n];
            var susp = new double[n];

            for (int i = 0; i < n; i++)
            {
                local[i] = wheelPositions[i];
                radius[i] = (wheelRadii != null && i < wheelRadii.Count) ? wheelRadii[i] : 0.5;
                onGround[i] = wheelOnGround == null || i >= wheelOnGround.Count || wheelOnGround[i];
                susp[i] = (wheelSuspensionDeflection != null && i < wheelSuspensionDeflection.Count)
                    ? wheelSuspensionDeflection[i] : 0.0;

                if (!float.IsFinite(local[i].X) || !float.IsFinite(local[i].Y) || !float.IsFinite(local[i].Z))
                    return false;
                if (!double.IsFinite(radius[i]) || radius[i] <= 0.05 || radius[i] > 2.0)
                    return false;

                // ШАГ 1: vehicle space → world space (через существующий ScsCameraPose.Rotate),
                // сразу в ОТНОСИТЕЛЬНОЙ системе фуры (точность float).
                centerRel[i] = ScsCameraPose.Rotate(local[i], truckOrientation);

                // truck.wheel.position.y — НОМИНАЛЬНОЕ (полностью разжатое) положение;
                // реальный центр колеса смещён вниз на ход подвески (SCS: deflection
                // вычитается из position.y). Величина ~1 см, но берём её как есть,
                // раз deflection приходит в телеметрии.
                centerRel[i].Y -= (float)susp[i];
            }

            int used = 0;
            for (int i = 0; i < n; i++) if (onGround[i]) used++;
            if (used < MinWheelsOnGround) return false;

            // ШАГ 2: начальное касание = центр − мировой Up × radius.
            var contact = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (!onGround[i]) { contact[i] = centerRel[i]; continue; }
                contact[i] = centerRel[i] - new Vector3(0f, (float)radius[i], 0f);
            }

            double a = 0, b = 0, c = 0;
            Vector3 normal = new(0, 1, 0);

            // ШАГИ 3–6: итерации fit → нормаль → пересчёт касаний.
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                // ШАГ 3: fit плоскости Y = aX + bZ + c по касаниям колёс НА ЗЕМЛЕ.
                if (!FitPlane(contact, onGround, out a, out b, out c)) return false;

                // ШАГ 4: нормаль плоскости.
                normal = Normalize(new Vector3((float)(-a), 1f, (float)(-b)), new Vector3(0f, 1f, 0f));

                // Плоскость должна быть «землёй», а не стеной.
                if (Math.Abs(normal.Y) < 0.30f) return false;

                // ШАГ 5: пересчитать касание вдоль нормали.
                for (int i = 0; i < n; i++)
                {
                    if (!onGround[i]) { contact[i] = centerRel[i]; continue; }
                    contact[i] = centerRel[i] - normal * (float)radius[i];
                }
            }

            // ШАГ 7: ФИНАЛЬНЫЙ fit по пересчитанным касаниям.
            if (!FitPlane(contact, onGround, out a, out b, out c)) return false;
            normal = Normalize(new Vector3((float)(-a), 1f, (float)(-b)), new Vector3(0f, 1f, 0f));
            if (Math.Abs(normal.Y) < 0.30f) return false;

            // Остатки контактов и диагностика по каждому колесу.
            var wheels = new WheelInfo[n];
            double maxResidual = 0.0;
            double sumHeight = 0.0;
            int counted = 0;

            for (int i = 0; i < n; i++)
            {
                double worldCenterX = truckX + centerRel[i].X;
                double worldCenterY = truckY + centerRel[i].Y;
                double worldCenterZ = truckZ + centerRel[i].Z;

                double cx = truckX + contact[i].X;
                double cy0 = truckY + contact[i].Y;
                double cz = truckZ + contact[i].Z;

                double residual = 0.0;
                if (onGround[i])
                {
                    // Высота плоскости в точке контакта (в относительной системе).
                    double planeYRel = a * contact[i].X + b * contact[i].Z + c;
                    residual = contact[i].Y - planeYRel;
                    if (Math.Abs(residual) > maxResidual) maxResidual = Math.Abs(residual);
                    sumHeight += cy0;
                    counted++;
                }

                wheels[i] = new WheelInfo(
                    i,
                    local[i],
                    new Vector3((float)worldCenterX, (float)worldCenterY, (float)worldCenterZ),
                    new Vector3((float)cx, (float)cy0, (float)cz),
                    radius[i],
                    onGround[i],
                    susp[i],
                    residual);
            }

            double averageWheelHeight = counted > 0 ? sumHeight / counted : truckY;

            // Мировой якорь сетки: ближайший «целый» узел мира, положенный НА плоскость.
            double ax = Math.Round(truckX);
            double az = Math.Round(truckZ);
            double originY = HeightAtRelative(truckX, truckY, truckZ, a, b, c, ax, az);

            // Ортонормальные оси сетки ВНУТРИ плоскости: U — проекция мировой X, V — cross(N,U).
            Vector3 axisU = new Vector3(1f, 0f, 0f) - normal * Vector3.Dot(normal, new Vector3(1f, 0f, 0f));
            axisU = Normalize(axisU, new Vector3(1f, 0f, 0f));
            Vector3 axisV = Normalize(Vector3.Cross(normal, axisU), new Vector3(0f, 0f, 1f));

            double referenceHeight = HeightAtRelative(truckX, truckY, truckZ, a, b, c, truckX, truckZ);

            plane = new ArGroundPlane(
                truckX, truckY, truckZ,
                a, b, c,
                ax, originY, az,
                normal, axisU, axisV,
                averageWheelHeight, referenceHeight, maxResidual,
                counted, wheels);

            return true;
        }

        private static double HeightAtRelative(
            double truckX, double truckY, double truckZ,
            double a, double b, double c, double x, double z)
            => truckY + a * (x - truckX) + b * (z - truckZ) + c;

        /// <summary>
        /// МНК-фит плоскости Y = aX + bZ + c по точкам (только отмеченные used).
        /// Работает в относительной системе координат фуры (точность).
        /// Используется нормальная система 3×3 с частичным выбором ведущего элемента.
        /// </summary>
        private static bool FitPlane(Vector3[] pts, bool[] used, out double a, out double b, out double c)
        {
            a = 0; b = 0; c = 0;

            double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sy = 0, sxy = 0, szy = 0;
            int n = 0;

            for (int i = 0; i < pts.Length; i++)
            {
                if (!used[i]) continue;
                double x = pts[i].X, y = pts[i].Y, z = pts[i].Z;
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) continue;
                sxx += x * x; sxz += x * z; szz += z * z;
                sx += x; sz += z; sy += y;
                sxy += x * y; szy += z * y;
                n++;
            }

            if (n < MinWheelsOnGround) return false;

            // Решаем M^T M · [a b c]^T = M^T y  (3×3).
            double m00 = sxx, m01 = sxz, m02 = sx;
            double m10 = sxz, m11 = szz, m12 = sz;
            double m20 = sx, m21 = sz, m22 = n;
            double r0 = sxy, r1 = szy, r2 = sy;

            // Метод Гаусса с частичным выбором ведущего элемента.
            double[,] m = { { m00, m01, m02, r0 }, { m10, m11, m12, r1 }, { m20, m21, m22, r2 } };

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                double best = Math.Abs(m[col, col]);
                for (int row = col + 1; row < 3; row++)
                {
                    double v = Math.Abs(m[row, col]);
                    if (v > best) { best = v; pivot = row; }
                }
                if (best < 1e-12) return false;   // вырожденная геометрия (колёса на одной линии)
                if (pivot != col)
                {
                    for (int k = 0; k < 4; k++) { (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]); }
                }
                for (int row = col + 1; row < 3; row++)
                {
                    double f = m[row, col] / m[col, col];
                    for (int k = col; k < 4; k++) m[row, k] -= f * m[col, k];
                }
            }

            double cOut = m[2, 3] / m[2, 2];
            double bOut = (m[1, 3] - m[1, 2] * cOut) / m[1, 1];
            double aOut = (m[0, 3] - m[0, 1] * bOut - m[0, 2] * cOut) / m[0, 0];

            if (!double.IsFinite(aOut) || !double.IsFinite(bOut) || !double.IsFinite(cOut)) return false;

            a = aOut; b = bOut; c = cOut;
            return true;
        }

        private static Vector3 Normalize(Vector3 v, Vector3 fallback)
        {
            float len = v.Length();
            if (!float.IsFinite(len) || len < 1e-9f) return fallback;
            return v / len;
        }

        // ================================================================
        // ЗАПРОСЫ К ПЛОСКОСТИ
        // ================================================================
        /// <summary>Высота плоскости в мировой точке (X,Z).</summary>
        public double HeightAt(double worldX, double worldZ)
            => HeightAtRelative(_truckX, _truckY, _truckZ, _a, _b, _c, worldX, worldZ);

        // ================================================================
        // v1.0.40.32: РАСШИРЕНИЕ СЕТКИ ДО КАМЕРЫ И «ВНИЗ ЭКРАНА».
        //
        // КОРЕНЬ бага «сетка рисуется только на расстоянии, а рядом с грузовиком
        // её нет»: сетка жила в круге R=150 м ВОКРУГ ОПОРНОЙ ТОЧКИ ФУРЫ. Камера
        // стоит ВЫШЕ и СЗАДИ (cabin + head offset), поэтому в направлении «вниз»
        // экрана плоскость попадает в круг лишь через десятки метров.
        //
        // v1.0.40.33: круг и fade УБРАНЫ совсем — сетка теперь обычная плоскость,
        // а её центр привязан к КАМЕРЕ (snapToPlane(camX, camZ)), поэтому низ
        // экрана покрыт без всяких «расширений». CameraPad* оставлены как no-op
        // для совместимости вызовов.
        // ================================================================

        /// <summary>Устарело (v1.0.40.33): сетка больше не ограничена кругом.</summary>
        [Obsolete("Сетка v1.0.40.33 — плоскость без круга; смещение не требуется.")]
        public const double CameraPadM = 60.0;

        /// <summary>Устарело (v1.0.40.33): центр сетки привязан к камере.</summary>
        [Obsolete("Сетка v1.0.40.33 — плоскость без круга; смещение не требуется.")]
        public void CameraPadInGrid(double camX, double camY, double camZ,
            out double padU, out double padV)
        {
            ProjectToGridCoordinates(camX, camY, camZ, out double cu, out double cv);
            padU = Math.Abs(cu) > CameraPadM ? Math.Sign(cu) * CameraPadM : cu;
            padV = Math.Abs(cv) > CameraPadM ? Math.Sign(cv) * CameraPadM : cv;
        }

        /// <summary>
        /// Пересечение луча с плоскостью. Возвращает false, если луч параллелен
        /// или пересечение ПОЗАДИ начала луча (t ≤ 0).
        /// </summary>
        public bool IntersectRay(
            double ox, double oy, double oz,
            double dx, double dy, double dz,
            out double t, out double hx, out double hy, out double hz)
        {
            t = 0; hx = hy = hz = 0;

            // Точка плоскости: (OriginX, OriginY, OriginZ), нормаль Normal.
            double denom = dx * Normal.X + dy * Normal.Y + dz * Normal.Z;
            if (!double.IsFinite(denom) || Math.Abs(denom) < 1e-9) return false;

            double numer = (OriginX - ox) * Normal.X + (OriginY - oy) * Normal.Y + (OriginZ - oz) * Normal.Z;
            double tHit = numer / denom;
            if (!double.IsFinite(tHit) || tHit <= 0) return false;

            t = tHit;
            hx = ox + dx * tHit;
            hy = oy + dy * tHit;
            hz = oz + dz * tHit;
            return double.IsFinite(hx) && double.IsFinite(hy) && double.IsFinite(hz);
        }

        /// <summary>Знаковое расстояние точки от плоскости (м, +над плоскостью).</summary>
        public double SignedDistance(double x, double y, double z)
            => (x - OriginX) * Normal.X + (y - OriginY) * Normal.Y + (z - OriginZ) * Normal.Z;

        /// <summary>Проекция мировой точки на координаты сетки (u,v) внутри плоскости.</summary>
        public void ProjectToGridCoordinates(double x, double y, double z, out double u, out double v)
        {
            double dx = x - OriginX, dy = y - OriginY, dz = z - OriginZ;
            u = dx * AxisU.X + dy * AxisU.Y + dz * AxisU.Z;
            v = dx * AxisV.X + dy * AxisV.Y + dz * AxisV.Z;
        }

        /// <summary>Мировая точка по координатам сетки (u,v) — ЛЕЖИТ на плоскости.</summary>
        public void FromGrid(double u, double v, out double x, out double y, out double z)
        {
            x = OriginX + AxisU.X * u + AxisV.X * v;
            y = OriginY + AxisU.Y * u + AxisV.Y * v;
            z = OriginZ + AxisU.Z * u + AxisV.Z * v;
        }

        /// <summary>Точка на плоскости, спроецированная из мировой X/Z (высота по плоскости).</summary>
        public void SnapToPlane(double worldX, double worldZ, out double x, out double y, out double z)
        {
            x = worldX;
            z = worldZ;
            y = HeightAt(worldX, worldZ);
        }

        // ================================================================
        // САМОТЕСТЫ (задание: горизонтальная плоскость, продольный уклон,
        // поперечный уклон, диагональный уклон, поворот фуры, поднятое колесо, snap)
        // ================================================================
        public static void RunSelfTests(Action<string> log)
        {
            int pass = 0, fail = 0;

            void Check(string name, bool ok, string detail)
            {
                if (ok) pass++; else fail++;
                log($"{(ok ? "OK  " : "FAIL")} {name} {detail}");
            }

            // Типовая фура: 2 передних (−1.64) и 2 задних (+2.094) колеса, R=0.506.
            Vector3[] Wheels4() => new[]
            {
                new Vector3(-1.045f, 0.5f, -1.640f),
                new Vector3( 1.045f, 0.5f, -1.640f),
                new Vector3(-0.930f, 0.5f,  2.094f),
                new Vector3( 0.930f, 0.5f,  2.094f)
            };
            double[] Radii() => new[] { 0.506, 0.506, 0.506, 0.506 };
            bool[] Ground() => new[] { true, true, true, true };

            // --- 1. Горизонтальная плоскость ---
            {
                bool ok = TryBuild(1000, 100, 2000, ScsEuler.Identity, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid;
                ok = ok && Math.Abs(p.Normal.Y - 1.0f) < 1e-3f;
                // контакт = 0.5 − 0.506 = −0.006 относительно фуры
                ok = ok && Math.Abs(p.AverageWheelHeight - (100 - 0.006)) < 0.01;
                Check("горизонтальная", ok,
                    $"normal.Y={p.Normal.Y:F4} avgH={p.AverageWheelHeight:F4} maxRes={p.MaxResidual:F5}");
            }

            // --- 2. Продольный уклон (+3°) ---
            {
                double slope = Math.Tan(3.0 * Math.PI / 180.0);
                var e = new ScsEuler(0.0, 3.0 / 360.0, 0.0);
                // Крен оси: с наклоном кузова контакты должны лечь на плоскость с b≈−slope
                bool ok = TryBuild(1000, 100, 2000, e, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid;
                double expected = -slope;
                ok = ok && Math.Abs(-p.Normal.Z / p.Normal.Y - expected) < 0.02;
                Check("продольный уклон", ok,
                    $"dz/dy={(-p.Normal.Z / p.Normal.Y):F4} ожид≈{expected:F4} maxRes={p.MaxResidual:F5}");
            }

            // --- 3. Поперечный уклон (крен +2°) ---
            {
                var e = new ScsEuler(0.0, 0.0, 2.0 / 360.0);
                bool ok = TryBuild(1000, 100, 2000, e, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid;
                ok = ok && Math.Abs(p.Normal.X) > 0.01f;
                Check("поперечный уклон", ok, $"normal.X={p.Normal.X:F4} maxRes={p.MaxResidual:F5}");
            }

            // --- 4. Диагональный уклон (pitch + roll) ---
            {
                var e = new ScsEuler(0.0, 2.0 / 360.0, 3.0 / 360.0);
                bool ok = TryBuild(1000, 100, 2000, e, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid && Math.Abs(p.Normal.X) > 0.005f && Math.Abs(p.Normal.Z) > 0.005f;
                ok = ok && p.MaxResidual < 0.01;
                Check("диагональный уклон", ok,
                    $"N=({p.Normal.X:F4},{p.Normal.Y:F4},{p.Normal.Z:F4}) maxRes={p.MaxResidual:F5}");
            }

            // --- 5. Поворот фуры (heading 37°) — плоскость не должна измениться ---
            {
                var e = new ScsEuler(37.0 / 360.0, 0.0, 0.0);
                bool ok = TryBuild(1000, 100, 2000, e, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid && Math.Abs(p.Normal.Y - 1.0f) < 1e-3f;
                Check("поворот фуры", ok, $"normal.Y={p.Normal.Y:F4} maxRes={p.MaxResidual:F5}");
            }

            // --- 6. Поднятое колесо (on_ground=false не участвует в fit) ---
            {
                var ground = new[] { true, true, true, false };
                bool ok = TryBuild(1000, 100, 2000, ScsEuler.Identity, Wheels4(), Radii(), ground, null, out var p);
                ok = ok && p.Valid && p.UsedWheelCount == 3;
                // Колесо остаётся в диагностическом массиве.
                ok = ok && p.Wheels.Count == 4 && !p.Wheels[3].OnGround;
                ok = ok && Math.Abs(p.Normal.Y - 1.0f) < 1e-3f;
                Check("поднятое колесо", ok, $"used={p.UsedWheelCount} wheel3on={p.Wheels[3].OnGround}");
            }

            // --- 7. Snap по НАКЛОННОЙ плоскости: (u,v) → world → обратно ---
            {
                var e = new ScsEuler(12.0 / 360.0, 1.5 / 360.0, 2.0 / 360.0);
                bool ok = TryBuild(1000, 100, 2000, e, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid;

                p.FromGrid(10, -7, out double x, out double y, out double z);
                ok = ok && Math.Abs(p.SignedDistance(x, y, z)) < 1e-3;   // точка ЛЕЖИТ на плоскости
                p.ProjectToGridCoordinates(x, y, z, out double u, out double v);
                ok = ok && Math.Abs(u - 10.0) < 1e-3 && Math.Abs(v + 7.0) < 1e-3;
                ok = ok && Math.Abs(p.HeightAt(x, z) - y) < 1e-3;        // высота согласована
                Check("snap наклонной", ok,
                    $"u={u:F4} v={v:F4} signed={p.SignedDistance(x, y, z):F6} hAt={p.HeightAt(x, z):F4} y={y:F4}");
            }

            // --- 8. Пересечение луча с плоскостью (луч вниз) ---
            {
                bool ok = TryBuild(1000, 100, 2000, ScsEuler.Identity, Wheels4(), Radii(), Ground(), null, out var p);
                ok = ok && p.Valid;
                bool hit = p.IntersectRay(1000, 102.0, 2000, 0, -1, 0, out double t, out _, out double hy, out _);
                ok = ok && hit && Math.Abs(t - 2.006) < 0.02 && Math.Abs(hy - 99.994) < 0.02;
                Check("луч × плоскость", ok, $"t={t:F4} hy={hy:F4}");
            }

            log($"[AR] ArGroundPlane self-test: OK={pass} FAIL={fail}.");
        }

        /// <summary>Одна строка диагностики плоскости (для app_data.log).</summary>
        public string Summary()
            => string.Format(CultureInfo.InvariantCulture,
                "plane origin=({0:F2},{1:F2},{2:F2}) N=({3:F4},{4:F4},{5:F4}) avgWheelH={6:F3} refH={7:F3} " +
                "maxRes={8:F4} used={9} wheels={10}",
                OriginX, OriginY, OriginZ, Normal.X, Normal.Y, Normal.Z,
                AverageWheelHeight, ReferenceHeight, MaxResidual, UsedWheelCount, Wheels.Count);
    }
}
