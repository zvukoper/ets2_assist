using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ETS2_Assist_GUI
{
    internal sealed record MapEditorVectorPoint(
        string Id,
        string Name,
        double X,
        double Z,
        int Layer,
        System.Drawing.Color Color,
        bool Visible,
        bool Selected,
        bool IsCity,
        bool IsPoi,
        bool IsSdo,
        bool Disabled);

    internal sealed record MapEditorVectorTruck(
        double X,
        double Z,
        double Heading,
        double HeadYaw,
        double HeadPitch,
        bool Online);

    internal sealed record MapEditorVectorCreateMarker(
        double X,
        double Z,
        double Y,
        bool FromAr);

    /// <summary>
    /// Retained-mode WPF vector surface.
    ///
    /// Static road/point geometry is recorded once into DrawingVisuals.
    /// Pan/zoom changes only the Transform of those visuals.
    /// Truck/selection/cone are drawn into a separate screen-space dynamic visual.
    /// </summary>
    internal sealed class MapEditorVectorSurface : FrameworkElement, IDisposable
    {
        private readonly VisualCollection _children;

        private readonly DrawingVisual _roadsVisual = new();
        private readonly DrawingVisual _citiesVisual = new();
        private readonly DrawingVisual _pointsVisual = new();
        private readonly DrawingVisual _poiVisual = new();
        private readonly DrawingVisual _dynamicVisual = new();

        private readonly Dictionary<string, BitmapSource> _iconCache = new(StringComparer.Ordinal);

        private double _centerX;
        private double _centerZ;
        private double _scale = 1.5;
        private double _width;
        private double _height;
        private bool _disposed;

        private List<MapEditorVectorPoint> _points = new();
        private MapEditorVectorTruck? _truck;
        private MapEditorVectorCreateMarker? _createMarker;
        private bool _onlySelected;
        private bool _showCone;

        private readonly Dictionary<string, System.Windows.Media.Brush> _brushCache = new(StringComparer.Ordinal);
        private readonly Dictionary<(double size, bool bold), Typeface> _typefaceCache = new();

        public MapEditorVectorSurface()
        {
            _children = new VisualCollection(this)
            {
                _roadsVisual,
                _citiesVisual,
                _pointsVisual,
                _poiVisual,
                _dynamicVisual
            };

            Focusable = false;
            SnapsToDevicePixels = false;
            UseLayoutRounding = false;

            SizeChanged += (_, _) =>
            {
                _width = ActualWidth;
                _height = ActualHeight;
                UpdateTransforms();
                RedrawDynamic();
            };
        }

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];

        public void SetCamera(double centerX, double centerZ, double scale)
        {
            if (_disposed) return;

            _centerX = centerX;
            _centerZ = centerZ;
            _scale = Math.Max(0.0001, scale);

            _width = ActualWidth;
            _height = ActualHeight;

            UpdateTransforms();
            RedrawDynamic();
        }

        public void SetStaticData(
            IReadOnlyList<(double x1, double z1, double x2, double z2)> roads,
            IReadOnlyList<MapEditorVectorPoint> points)
        {
            if (_disposed) return;

            _points = points?.ToList() ?? new List<MapEditorVectorPoint>();

            RebuildRoadVisual(roads);
            RebuildPointVisuals();

            UpdateTransforms();
        }

        public void SetDynamicState(
            MapEditorVectorTruck? truck,
            MapEditorVectorCreateMarker? createMarker,
            bool onlySelected,
            bool showCone)
        {
            if (_disposed) return;

            _truck = truck;
            _createMarker = createMarker;
            _onlySelected = onlySelected;
            _showCone = showCone;

            RedrawDynamic();
        }

        public void InvalidateDynamic() => RedrawDynamic();

        private void UpdateTransforms()
        {
            if (_disposed) return;

            var matrix = new System.Windows.Media.Matrix(
                1.0 / _scale,
                0,
                0,
                1.0 / _scale,
                (_width / 2.0) - (_centerX / _scale),
                (_height / 2.0) - (_centerZ / _scale));

            var transform = new System.Windows.Media.MatrixTransform(matrix);

            _roadsVisual.Transform = transform;
            _citiesVisual.Transform = transform;
            _pointsVisual.Transform = transform;
            _poiVisual.Transform = transform;

            // POI становятся скрытыми на большом масштабе, сохраняя поведение редактора.
            _poiVisual.Opacity = _scale <= 30.0 ? 1.0 : 0.0;
        }

        private void RebuildRoadVisual(
            IReadOnlyList<(double x1, double z1, double x2, double z2)> roads)
        {
            using var dc = _roadsVisual.RenderOpen();

            dc.DrawRectangle(
                GetBrush(System.Drawing.Color.FromArgb(255, 15, 18, 23)),
                null,
                new System.Windows.Rect(_centerX - 1000000, _centerZ - 1000000, 2000000, 2000000));

            if (roads == null || roads.Count == 0)
                return;

            var geometry = new System.Windows.Media.StreamGeometry();

            using (var ctx = geometry.Open())
            {
                foreach (var r in roads)
                {
                    if (!double.IsFinite(r.x1) || !double.IsFinite(r.z1) ||
                        !double.IsFinite(r.x2) || !double.IsFinite(r.z2))
                        continue;

                    ctx.BeginFigure(
                        new System.Windows.Point(r.x1, r.z1),
                        isFilled: false,
                        isClosed: false);

                    ctx.LineTo(
                        new System.Windows.Point(r.x2, r.z2),
                        isStroked: true,
                        isSmoothJoin: false);
                }
            }

            geometry.Freeze();

            var roadPen = new System.Windows.Media.Pen(
                GetBrush(System.Drawing.Color.FromArgb(255, 110, 145, 165)),
                2.2);

            roadPen.Freeze();

            dc.DrawGeometry(null, roadPen, geometry);
        }

        private void RebuildPointVisuals()
        {
            using var cityDc = _citiesVisual.RenderOpen();
            using var pointDc = _pointsVisual.RenderOpen();
            using var poiDc = _poiVisual.RenderOpen();

            foreach (var p in _points)
            {
                if (!p.Visible)
                    continue;

                // Выбранные точки рисуются отдельно динамическим слоем,
                // чтобы смена selection не требовала перерисовки статической сцены.
                if (p.Selected)
                    continue;

                if (_onlySelected)
                    continue;

                var color = ToBrush(p.Color);

                if (p.IsCity)
                {
                    cityDc.DrawEllipse(color, null, new System.Windows.Point(p.X, p.Z), 5.5, 5.5);
                    DrawWorldLabel(
                        cityDc,
                        p.Name,
                        p.X,
                        p.Z,
                        System.Drawing.Color.Yellow,
                        bold: true,
                        fontSize: 10,
                        gap: 6);
                }
                else if (p.IsPoi)
                {
                    poiDc.DrawEllipse(color, null, new System.Windows.Point(p.X, p.Z), 3.5, 3.5);

                    DrawWorldLabel(
                        poiDc,
                        p.Name,
                        p.X,
                        p.Z,
                        p.Disabled ? System.Drawing.Color.Gray : System.Drawing.Color.FromArgb(255, 166, 166, 166),
                        bold: false,
                        fontSize: 9,
                        gap: 6);
                }
                else
                {
                    pointDc.DrawEllipse(color, null, new System.Windows.Point(p.X, p.Z), 5, 5);

                    DrawWorldLabel(
                        pointDc,
                        p.Name,
                        p.X,
                        p.Z,
                        p.Disabled ? System.Drawing.Color.Gray : System.Drawing.Color.FromArgb(255, 166, 166, 166),
                        bold: false,
                        fontSize: 9,
                        gap: 6);
                }
            }
        }

        private void RedrawDynamic()
        {
            if (_disposed) return;

            using var dc = _dynamicVisual.RenderOpen();

            // Selection overlays.
            foreach (var p in _points)
            {
                if (!p.Selected || !p.Visible)
                    continue;

                var s = WorldToScreen(p.X, p.Z);

                dc.DrawEllipse(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 0, 255, 0)),
                    null,
                    s,
                    14,
                    14);

                dc.DrawEllipse(
                    null,
                    new System.Windows.Media.Pen(System.Windows.Media.Brushes.Lime, 2.0),
                    s,
                    8,
                    8);

                DrawScreenLabel(
                    dc,
                    p.Name,
                    s.X,
                    s.Y,
                    System.Windows.Media.Colors.White,
                    bold: true,
                    fontSize: 9,
                    gap: 6);
            }

            // Точка в режиме создания.
            if (_createMarker != null)
            {
                var p = WorldToScreen(_createMarker.X, _createMarker.Z);

                dc.DrawEllipse(
                    System.Windows.Media.Brushes.Gray,
                    new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 225, 225)), 1.5),
                    p,
                    6,
                    6);

                dc.DrawLine(
                    new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 225, 225)), 1.5),
                    new System.Windows.Point(p.X - 13, p.Y),
                    new System.Windows.Point(p.X + 13, p.Y));

                dc.DrawLine(
                    new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 225, 225)), 1.5),
                    new System.Windows.Point(p.X, p.Y - 13),
                    new System.Windows.Point(p.X, p.Y + 13));

                var hint = _createMarker.FromAr
                    ? $"Новая точка (АР)  Y={_createMarker.Y:F1}м"
                    : "Новая точка";

                DrawScreenLabel(
                    dc,
                    hint,
                    p.X,
                    p.Y,
                    System.Windows.Media.Color.FromRgb(166, 166, 166),
                    bold: true,
                    fontSize: 9,
                    gap: 6);
            }

            if (_truck is not null)
            {
                var truck = _truck;
                var p = WorldToScreen(truck.X, truck.Z);

                if (_showCone)
                    DrawCone(dc, p, truck);

                dc.PushTransform(
                    new RotateTransform(
                        -truck.Heading * 360.0,
                        p.X,
                        p.Y));

                var truckGeometry = new System.Windows.Media.StreamGeometry();
                using (var ctx = truckGeometry.Open())
                {
                    ctx.BeginFigure(
                        new System.Windows.Point(p.X, p.Y - 9),
                        isFilled: true,
                        isClosed: true);

                    ctx.LineTo(new System.Windows.Point(p.X - 5, p.Y + 7), true, false);
                    ctx.LineTo(new System.Windows.Point(p.X, p.Y + 4.5), true, false);
                    ctx.LineTo(new System.Windows.Point(p.X + 5, p.Y + 7), true, false);
                }

                truckGeometry.Freeze();

                var fill = truck.Online
                    ? System.Windows.Media.Brushes.Red
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 128, 128, 128));

                var outline = truck.Online
                    ? System.Windows.Media.Brushes.White
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 190, 190));

                var pen = new System.Windows.Media.Pen(outline, 1.5);
                pen.Freeze();

                dc.DrawGeometry(fill, pen, truckGeometry);

                dc.Pop();

                DrawScreenLabel(
                    dc,
                    truck.Online ? "Грузовик" : "Грузовик (нет данных)",
                    p.X,
                    p.Y,
                    truck.Online ? System.Windows.Media.Colors.Red : System.Windows.Media.Color.FromRgb(170, 170, 170),
                    bold: true,
                    fontSize: 9,
                    gap: 6);
            }
        }

        private void DrawCone(
            DrawingContext dc,
            System.Windows.Point origin,
            MapEditorVectorTruck truck)
        {
            var width = Math.Max(1, _width);
            var height = Math.Max(1, _height);

            var halfAngleDeg = Math.Clamp(
                Math.Abs(truck.HeadPitch * 360.0),
                5.0,
                45.0);

            var headingDeg = -(truck.Heading + truck.HeadYaw) * 360.0;
            var lenPx = Math.Min(
                1500.0 / _scale,
                Math.Sqrt(width * width + height * height) / 2.0);

            if (lenPx <= 1)
                return;

            var a0 = (headingDeg - halfAngleDeg) * Math.PI / 180.0;
            var a1 = (headingDeg + halfAngleDeg) * Math.PI / 180.0;

            var geometry = new System.Windows.Media.StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(origin, isFilled: true, isClosed: true);

                const int steps = 24;

                for (int i = 0; i <= steps; i++)
                {
                    var t = (double)i / steps;
                    var a = a0 + (a1 - a0) * t;

                    var x = origin.X + Math.Sin(a) * lenPx;
                    var y = origin.Y - Math.Cos(a) * lenPx;

                    ctx.LineTo(new System.Windows.Point(x, y), true, false);
                }
            }

            geometry.Freeze();

            var fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(38, 255, 210, 90));
            fill.Freeze();

            var pen = new System.Windows.Media.Pen(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 210, 90)),
                1);
            pen.Freeze();

            dc.DrawGeometry(fill, pen, geometry);
        }

        private System.Windows.Point WorldToScreen(double x, double z)
        {
            return new System.Windows.Point(
                _width / 2.0 + (x - _centerX) / _scale,
                _height / 2.0 + (z - _centerZ) / _scale);
        }

        private void DrawWorldLabel(
            DrawingContext dc,
            string text,
            double x,
            double y,
            System.Drawing.Color color,
            bool bold,
            double fontSize,
            double gap)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var typeface = GetTypeface(fontSize, bold);
            var formatted = new System.Windows.Media.FormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                ToBrush(color),
                1.0);

            formatted.TextAlignment = System.Windows.TextAlignment.Center;

            // В исходном редакторе подпись находится сверху точки.
            var p = new System.Windows.Point(x, y - gap - formatted.Height);

            dc.DrawText(formatted, p);
        }

        private void DrawScreenLabel(
            DrawingContext dc,
            string text,
            double x,
            double y,
            System.Windows.Media.Color color,
            bool bold,
            double fontSize,
            double gap)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var typeface = GetTypeface(fontSize, bold);

            var formatted = new System.Windows.Media.FormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                new System.Windows.Media.SolidColorBrush(color),
                1.0);

            formatted.TextAlignment = System.Windows.TextAlignment.Center;

            dc.DrawText(
                formatted,
                new System.Windows.Point(
                    x - formatted.Width / 2.0,
                    y - gap - formatted.Height));
        }

        private Typeface GetTypeface(double size, bool bold)
        {
            var key = (size, bold);

            if (_typefaceCache.TryGetValue(key, out var cached))
                return cached;

            var typeface = new System.Windows.Media.Typeface(
                new System.Windows.Media.FontFamily("Segoe UI"),
                System.Windows.FontStyles.Normal,
                bold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                System.Windows.FontStretches.Normal);

            _typefaceCache[key] = typeface;
            return typeface;
        }

        private System.Windows.Media.Brush GetBrush(System.Drawing.Color color)
        {
            var key =
                $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

            if (_brushCache.TryGetValue(key, out var cached))
                return cached;

            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(
                    color.A,
                    color.R,
                    color.G,
                    color.B));

            brush.Freeze();

            _brushCache[key] = brush;
            return brush;
        }

        private static System.Windows.Media.Brush ToBrush(System.Drawing.Color color)
            => new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(
                    color.A,
                    color.R,
                    color.G,
                    color.B));

        private static System.Windows.Media.Color ToMediaColor(System.Drawing.Color color)
            => System.Windows.Media.Color.FromArgb(
                color.A,
                color.R,
                color.G,
                color.B);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _iconCache.Clear();
            _brushCache.Clear();
            _typefaceCache.Clear();

        }
    }
}
