using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using FormsCursors = System.Windows.Forms.Cursors;
using FormsFormClosedEventArgs = System.Windows.Forms.FormClosedEventArgs;
using FormsInvalidateEventArgs = System.Windows.Forms.InvalidateEventArgs;
using DrawingColor = System.Drawing.Color;
using WpfPoint = System.Windows.Point;

namespace ETS2_Assist_GUI
{
    public partial class MapEditorForm
    {
        private Grid? _behaviorMapGrid;
        private MapEditorScreenPointOverlay? _behaviorPointOverlay;
        private bool _behaviorOverlayInstalled;
        private string? _behaviorHoverId;

        protected override void OnLoad(EventArgs e)
        {
            // Подключаем быстрый renderer до первой нормальной отрисовки формы.
            EnsureVectorRenderer();
            InstallBehaviorOverlay();
            base.OnLoad(e);
        }

        private void InstallBehaviorOverlay()
        {
            if (_behaviorOverlayInstalled || _vectorMapHost == null || _vectorMapSurface == null)
                return;

            _behaviorOverlayInstalled = true;

            var surface = _vectorMapSurface;
            var host = _vectorMapHost;

            // Оставляем существующий быстрый vector renderer только для дорог/динамики.
            // Его старые world-space точки и подписи полностью выключаем.
            surface.SetStaticData(_roads, BuildHiddenLegacyPoints());
            surface.SetSelectionState(Array.Empty<string>(), null, false);
            surface.IsHitTestVisible = false;

            // После установки WPF overlay сам принимает весь mouse input.
            // Старые ElementHost handlers отключаем, чтобы один клик не обрабатывался дважды.
            host.MouseDown -= ForwardMouseDown;
            host.MouseMove -= ForwardMouseMove;
            host.MouseUp -= ForwardMouseUp;
            host.MouseClick -= ForwardMouseClick;
            host.MouseLeave -= ForwardMouseLeave;
            host.MouseWheel -= ForwardMouseWheel;

            host.Child = null;

            _behaviorMapGrid = new Grid();
            _behaviorMapGrid.Children.Add(surface);

            _behaviorPointOverlay = new MapEditorScreenPointOverlay(this);
            _behaviorMapGrid.Children.Add(_behaviorPointOverlay);

            host.Child = _behaviorMapGrid;
            _mapPanel.Invalidated += BehaviorOverlayInvalidated;
            FormClosed += BehaviorOverlayFormClosed;

            _behaviorPointOverlay.InvalidateVisual();
        }

        private void BehaviorOverlayFormClosed(object? sender, FormsFormClosedEventArgs e)
        {
            FormClosed -= BehaviorOverlayFormClosed;
            _mapPanel.Invalidated -= BehaviorOverlayInvalidated;
            _behaviorPointOverlay = null;
            _behaviorMapGrid = null;
            _behaviorHoverId = null;
            _behaviorOverlayInstalled = false;
        }

        private void BehaviorOverlayInvalidated(object? sender, FormsInvalidateEventArgs e)
        {
            // Existing vector surface must not draw a second selected point layer.
            if (_vectorMapSurface != null)
                _vectorMapSurface.SetSelectionState(Array.Empty<string>(), null, false);

            _behaviorPointOverlay?.InvalidateVisual();
        }

        private List<MapEditorVectorPoint> BuildHiddenLegacyPoints()
        {
            var points = new List<MapEditorVectorPoint>(_targets.Count);

            foreach (var target in _targets)
            {
                _pointModel.TryGetValue(target.id, out var pm);
                var isCity = pm?.IsCity == true;
                var isPoi = pm?.IsPoi == true;
                var isSdo = pm?.IsSdo == true;
                var disabled = pm != null && !pm.Enabled;

                var name = isPoi && pm != null
                    ? (string.IsNullOrEmpty(pm.RealName) ? pm.Category : pm.RealName)
                    : isCity && pm != null ? pm.RealName : target.name;

                var color = target.color;
                if (isCity) color = DrawingColor.FromArgb(255, 230, 0);
                else if (isPoi && pm != null) color = CategoryColor(pm.Category);
                else if (disabled) color = DrawingColor.FromArgb(120, 120, 120);

                var layer = isCity ? 1_000_000 : (isSdo || isPoi ? SdoMeta.LayerOf(pm?.Category ?? "") : 100);

                points.Add(new MapEditorVectorPoint(
                    target.id, name, target.x, target.z, layer, color,
                    Visible: false, Selected: false, isCity, isPoi, isSdo, disabled));
            }

            return points;
        }

        private bool TryGetBehaviorPoint(
            (string id, string name, double x, double z, DrawingColor color) target,
            out string name,
            out DrawingColor color,
            out bool isCity,
            out bool isPoi,
            out bool disabled)
        {
            name = target.name;
            color = target.color;
            isCity = false;
            isPoi = false;
            disabled = false;

            _pointModel.TryGetValue(target.id, out var pm);
            isCity = pm?.IsCity == true;
            isPoi = pm?.IsPoi == true;
            var isSdo = pm?.IsSdo == true;
            disabled = pm != null && !pm.Enabled;

            var category = isCity ? "Города" : isPoi || isSdo ? pm?.Category ?? "" : "Цели";
            bool visible = disabled
                ? (!_catVisible.TryGetValue("Отключенные", out var dv) || dv)
                : (!_catVisible.TryGetValue(category, out var cv) || cv);

            if (!visible)
                return false;

            if (isPoi && pm != null)
                name = string.IsNullOrEmpty(pm.RealName) ? pm.Category : pm.RealName;
            else if (isCity && pm != null)
                name = pm.RealName;

            if (isCity) color = DrawingColor.FromArgb(255, 230, 0);
            else if (isPoi && pm != null) color = CategoryColor(pm.Category);
            else if (disabled) color = DrawingColor.FromArgb(120, 120, 120);

            return true;
        }

        private bool BehaviorIsSelected(string id)
            => _selectedIds.Contains(id) ||
               (!string.IsNullOrWhiteSpace(_selectedGameName) &&
                string.Equals(_selectedGameName, id, StringComparison.Ordinal));

        private bool BehaviorOnlySelected => _onlySelectedChk?.Checked == true;

        private WpfPoint BehaviorWorldToScreen(double x, double z, double width, double height)
            => new(
                width / 2.0 + (x - _centerX) / Math.Max(0.0001, _scale),
                height / 2.0 + (z - _centerZ) / Math.Max(0.0001, _scale));

        private string? BehaviorHitTarget(double sx, double sy)
        {
            if (_behaviorPointOverlay == null)
                return null;

            var width = _behaviorPointOverlay.ActualWidth;
            var height = _behaviorPointOverlay.ActualHeight;
            const double hitRadius = 14.0;
            var bestD2 = hitRadius * hitRadius;
            string? bestId = null;

            foreach (var target in _targets)
            {
                if (!TryGetBehaviorPoint(target, out _, out _, out _, out _, out _))
                    continue;
                if (BehaviorOnlySelected && !BehaviorIsSelected(target.id))
                    continue;

                var p = BehaviorWorldToScreen(target.x, target.z, width, height);
                var dx = p.X - sx;
                var dy = p.Y - sy;
                var d2 = dx * dx + dy * dy;
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    bestId = target.id;
                }
            }

            return bestId;
        }

        private sealed class MapEditorScreenPointOverlay : FrameworkElement
        {
            private readonly MapEditorForm _owner;
            private readonly Dictionary<(double, bool), Typeface> _typefaces = new();

            public MapEditorScreenPointOverlay(MapEditorForm owner)
            {
                _owner = owner;
                Focusable = false;
                SnapsToDevicePixels = false;
                UseLayoutRounding = false;
            }

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);

                var width = Math.Max(1, ActualWidth);
                var height = Math.Max(1, ActualHeight);

                foreach (var target in _owner._targets)
                {
                    if (!_owner.TryGetBehaviorPoint(
                            target, out var name, out var color,
                            out var isCity, out var isPoi, out _, out var disabled))
                        continue;

                    if (_owner.BehaviorOnlySelected && !_owner.BehaviorIsSelected(target.id))
                        continue;

                    var s = _owner.BehaviorWorldToScreen(target.x, target.z, width, height);
                    var radius = isCity ? 5.5 : isPoi ? 3.5 : 5.0;

                    var fill = new SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
                    fill.Freeze();

                    var outline = new Pen(Brushes.Black, isCity ? 2.0 : 1.5);
                    outline.Freeze();
                    dc.DrawEllipse(fill, outline, s, radius, radius);

                    var selected = _owner.BehaviorIsSelected(target.id);
                    var hovered = string.Equals(target.id, _owner._behaviorHoverId, StringComparison.Ordinal);

                    if (selected)
                    {
                        var pen = new Pen(Brushes.Lime, 4.0);
                        pen.Freeze();
                        dc.DrawEllipse(null, pen, s, radius + 3.0, radius + 3.0);
                    }
                    else if (hovered)
                    {
                        var pen = new Pen(Brushes.White, 2.0);
                        pen.Freeze();
                        dc.DrawEllipse(null, pen, s, radius + 3.0, radius + 3.0);
                    }

                    var textColor = selected
                        ? System.Windows.Media.Colors.White
                        : isCity
                            ? System.Windows.Media.Colors.Yellow
                            : disabled
                                ? System.Windows.Media.Colors.Gray
                                : System.Windows.Media.Color.FromRgb(166, 166, 166);

                    var bold = selected || isCity;
                    var fontSize = isCity ? 10.0 : 9.0;
                    var key = (fontSize, bold);
                    if (!_typefaces.TryGetValue(key, out var typeface))
                    {
                        typeface = new Typeface(
                            new FontFamily("Segoe UI"),
                            FontStyles.Normal,
                            bold ? FontWeights.Bold : FontWeights.Normal,
                            FontStretches.Normal);
                        _typefaces[key] = typeface;
                    }

                    var brush = new SolidColorBrush(textColor);
                    brush.Freeze();
                    var formatted = new FormattedText(
                        name,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        brush,
                        1.0)
                    {
                        TextAlignment = TextAlignment.Center
                    };

                    var tx = s.X;
                    var ty = s.Y - 6 - formatted.Height;
                    DrawOutlinedText(dc, formatted, tx, ty, selected ? 2 : 1);
                }
            }

            private static void DrawOutlinedText(
                DrawingContext dc,
                FormattedText text,
                double x,
                double y,
                int radius)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        dc.DrawText(text, new WpfPoint(x - text.Width / 2 + dx, y + dy));
                    }
                }

                dc.DrawText(text, new WpfPoint(x - text.Width / 2, y));
            }

            protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var p = e.GetPosition(this);

                _owner._behaviorHoverId = _owner.BehaviorHitTarget(p.X, p.Y);
                _owner.Cursor = _owner._behaviorHoverId != null || _owner._panning
                    ? FormsCursors.Hand
                    : FormsCursors.Default;
                Cursor = _owner._behaviorHoverId != null
                    ? System.Windows.Input.Cursors.Hand
                    : System.Windows.Input.Cursors.Arrow;

                _owner.OnMouseMove(_owner._mapPanel,
                    new FormsMouseEventArgs(FormsMouseButtons.None, 0, (int)p.X, (int)p.Y, 0));
                InvalidateVisual();
            }

            protected override void OnMouseDown(MouseButtonEventArgs e)
            {
                base.OnMouseDown(e);
                CaptureMouse();
                var p = e.GetPosition(this);
                _owner._behaviorHoverId = _owner.BehaviorHitTarget(p.X, p.Y);
                _owner.OnMouseDown(_owner._mapPanel,
                    new FormsMouseEventArgs(ToFormsButton(e.ChangedButton), e.ClickCount, (int)p.X, (int)p.Y, 0));
            }

            protected override void OnMouseUp(MouseButtonEventArgs e)
            {
                base.OnMouseUp(e);
                var p = e.GetPosition(this);
                var args = new FormsMouseEventArgs(
                    ToFormsButton(e.ChangedButton), e.ClickCount, (int)p.X, (int)p.Y, 0);

                _owner.OnMouseUp(_owner._mapPanel, args);
                _owner.OnMouseClick(_owner._mapPanel, args);
                ReleaseMouseCapture();

                _owner._behaviorHoverId = _owner.BehaviorHitTarget(p.X, p.Y);
                _owner.Cursor = _owner._behaviorHoverId != null
                    ? FormsCursors.Hand
                    : FormsCursors.Default;
                InvalidateVisual();
            }

            protected override void OnMouseWheel(MouseWheelEventArgs e)
            {
                base.OnMouseWheel(e);
                var p = e.GetPosition(this);
                _owner.OnMouseWheel(_owner._mapPanel,
                    new FormsMouseEventArgs(FormsMouseButtons.None, 0, (int)p.X, (int)p.Y, e.Delta));
            }

            protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
            {
                base.OnMouseLeave(e);
                if (!_owner._panning)
                    _owner.Cursor = FormsCursors.Default;
                _owner._behaviorHoverId = null;
                InvalidateVisual();
            }

            private static FormsMouseButtons ToFormsButton(MouseButton button)
                => button switch
                {
                    MouseButton.Left => FormsMouseButtons.Left,
                    MouseButton.Right => FormsMouseButtons.Right,
                    MouseButton.Middle => FormsMouseButtons.Middle,
                    MouseButton.XButton1 => FormsMouseButtons.XButton1,
                    MouseButton.XButton2 => FormsMouseButtons.XButton2,
                    _ => FormsMouseButtons.None
                };
        }
    }
}
