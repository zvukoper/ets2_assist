using System;
using System.Numerics;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// 2D perspective transform (homography).
    ///
    /// Four source points -> four destination points.
    /// The transform is:
    ///
    ///   x' = (H00*x + H01*y + H02) / (H20*x + H21*y + 1)
    ///   y' = (H10*x + H11*y + H12) / (H20*x + H21*y + 1)
    ///
    /// Used for AR2 ground-grid perspective calibration.
    /// </summary>
    public static class PerspectiveWarp
    {
        public readonly struct Homography
        {
            public readonly double H00;
            public readonly double H01;
            public readonly double H02;

            public readonly double H10;
            public readonly double H11;
            public readonly double H12;

            public readonly double H20;
            public readonly double H21;

            public Homography(
                double h00, double h01, double h02,
                double h10, double h11, double h12,
                double h20, double h21)
            {
                H00 = h00;
                H01 = h01;
                H02 = h02;

                H10 = h10;
                H11 = h11;
                H12 = h12;

                H20 = h20;
                H21 = h21;
            }

            public static Homography Identity =>
                new(
                    1, 0, 0,
                    0, 1, 0,
                    0, 0);

            public bool TryTransform(Vector2 source, out Vector2 result)
            {
                double x = source.X;
                double y = source.Y;

                double w = H20 * x + H21 * y + 1.0;

                if (Math.Abs(w) < 1e-10)
                {
                    result = default;
                    return false;
                }

                double tx =
                    (H00 * x + H01 * y + H02) / w;

                double ty =
                    (H10 * x + H11 * y + H12) / w;

                if (!double.IsFinite(tx) ||
                    !double.IsFinite(ty) ||
                    Math.Abs(tx) > 100000000.0 ||
                    Math.Abs(ty) > 100000000.0)
                {
                    result = default;
                    return false;
                }

                result = new Vector2((float)tx, (float)ty);
                return true;
            }
        }

        /// <summary>
        /// Calculates a homography from four source points to four destination points.
        /// </summary>
        public static bool TryCreate(
            ReadOnlySpan<Vector2> source,
            ReadOnlySpan<Vector2> destination,
            out Homography homography)
        {
            homography = Homography.Identity;

            if (source.Length != 4 || destination.Length != 4)
                return false;

            if (!IsValidQuad(source) || !IsValidQuad(destination))
                return false;

            // 8 equations, 8 unknowns.
            //
            // Unknown vector:
            //
            // H00 H01 H02 H10 H11 H12 H20 H21
            //
            // H22 is fixed to 1.
            var a = new double[8, 9];

            for (int i = 0; i < 4; i++)
            {
                double x = source[i].X;
                double y = source[i].Y;

                double u = destination[i].X;
                double v = destination[i].Y;

                int r0 = i * 2;
                int r1 = r0 + 1;

                // x' equation
                a[r0, 0] = x;
                a[r0, 1] = y;
                a[r0, 2] = 1.0;

                a[r0, 3] = 0.0;
                a[r0, 4] = 0.0;
                a[r0, 5] = 0.0;

                a[r0, 6] = -u * x;
                a[r0, 7] = -u * y;

                a[r0, 8] = u;

                // y' equation
                a[r1, 0] = 0.0;
                a[r1, 1] = 0.0;
                a[r1, 2] = 0.0;

                a[r1, 3] = x;
                a[r1, 4] = y;
                a[r1, 5] = 1.0;

                a[r1, 6] = -v * x;
                a[r1, 7] = -v * y;

                a[r1, 8] = v;
            }

            if (!Solve8x8(a, out double[] h))
                return false;

            homography = new Homography(
                h[0], h[1], h[2],
                h[3], h[4], h[5],
                h[6], h[7]);

            // Validate the resulting transform against all four control points.
            for (int i = 0; i < 4; i++)
            {
                if (!homography.TryTransform(source[i], out Vector2 p))
                    return false;

                double dx = p.X - destination[i].X;
                double dy = p.Y - destination[i].Y;

                if (dx * dx + dy * dy > 0.01)
                    return false;
            }

            return true;
        }

        private static bool Solve8x8(
            double[,] a,
            out double[] result)
        {
            result = new double[8];

            const double Epsilon = 1e-12;

            for (int col = 0; col < 8; col++)
            {
                int pivotRow = col;
                double maxAbs = Math.Abs(a[col, col]);

                for (int row = col + 1; row < 8; row++)
                {
                    double value = Math.Abs(a[row, col]);

                    if (value > maxAbs)
                    {
                        maxAbs = value;
                        pivotRow = row;
                    }
                }

                if (maxAbs < Epsilon)
                    return false;

                if (pivotRow != col)
                {
                    for (int c = col; c <= 8; c++)
                    {
                        (a[col, c], a[pivotRow, c]) =
                            (a[pivotRow, c], a[col, c]);
                    }
                }

                double pivot = a[col, col];

                for (int c = col; c <= 8; c++)
                    a[col, c] /= pivot;

                for (int row = 0; row < 8; row++)
                {
                    if (row == col)
                        continue;

                    double factor = a[row, col];

                    if (Math.Abs(factor) < Epsilon)
                        continue;

                    for (int c = col; c <= 8; c++)
                        a[row, c] -= factor * a[col, c];
                }
            }

            for (int i = 0; i < 8; i++)
            {
                result[i] = a[i, 8];

                if (!double.IsFinite(result[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Reject degenerate, self-intersecting or concave quadrilaterals.
        /// </summary>
        private static bool IsValidQuad(ReadOnlySpan<Vector2> p)
        {
            if (p.Length != 4)
                return false;

            double sign = 0.0;

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = p[i];
                Vector2 b = p[(i + 1) & 3];
                Vector2 c = p[(i + 2) & 3];

                Vector2 ab = b - a;
                Vector2 bc = c - b;

                double cross =
                    ab.X * bc.Y -
                    ab.Y * bc.X;

                if (Math.Abs(cross) < 1e-5)
                    return false;

                double currentSign = Math.Sign(cross);

                if (sign == 0.0)
                {
                    sign = currentSign;
                }
                else if (currentSign != sign)
                {
                    return false;
                }
            }

            double area2 = 0.0;

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = p[i];
                Vector2 b = p[(i + 1) & 3];

                area2 +=
                    a.X * b.Y -
                    b.X * a.Y;
            }

            return Math.Abs(area2) > 1e-4;
        }
    }
}