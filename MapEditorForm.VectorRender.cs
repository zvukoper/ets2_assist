using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace ETS2_Assist_GUI
{
    public partial class MapEditorForm
    {
        private ElementHost? _vectorMapHost;
        private MapEditorVectorSurface? _vectorMapSurface;
        private bool _vectorMapHooked;

        private static bool _vectorMapIdleHookInstalled;

        static MapEditorForm()
        {
            if (!_vectorMapIdleHookInstalled)
            {
                _vectorMapIdleHookInstalled = true;
                Application.Idle += AttachVectorRendererToOpenMapEditors;
            }
        }

        private static void AttachVectorRendererToOpenMapEditors(object? sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is MapEditorForm mapEditor)
                    mapEditor.EnsureVectorRenderer();
            }
        }

        private void EnsureVectorRenderer()
        {
            if (_vectorMapHooked || _disposed || _mapPanel.IsDisposed)
                return;

            if (!_mapPanel.IsHandleCreated)
                return;

            _vectorMapSurface = new MapEditorVectorSurface
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            };

            _vectorMapHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 18, 23),
                Child = _vectorMapSurface
            };

            // Отключаем старый GDI+ Paint renderer.
            _mapPanel.Paint -= OnPaint;

            _mapPanel.Controls.Add(_vectorMapHost);
            _vectorMapHost.BringToFront();

            // ElementHost перекрывает старый MapPanel, поэтому его мышь
            // прокидываем в существующие обработчики редактора.
            _vectorMapHost.MouseDown += ForwardMouseDown;
            _vectorMapHost.MouseMove += ForwardMouseMove;
            _vectorMapHost.MouseUp += ForwardMouseUp;
            _vectorMapHost.MouseClick += ForwardMouseClick;
            _vectorMapHost.MouseLeave += ForwardMouseLeave;
            _vectorMapHost.MouseWheel += ForwardMouseWheel;

            _vectorMapSurface.SizeChanged += (_, _) => RequestVectorRender();

            _vectorMapHooked = true;

            RebuildVectorStaticCache();
            RequestVectorRender();
        }

        private void ForwardMouseDown(object? sender, MouseEventArgs e)
            => OnMouseDown(sender, e);

        private void ForwardMouseMove(object? sender, MouseEventArgs e)
            => OnMouseMove(sender, e);

        private void ForwardMouseUp(object? sender, MouseEventArgs e)
            => OnMouseUp(sender, e);

        private void ForwardMouseClick(object? sender, MouseEventArgs e)
            => OnMouseClick(sender, e);

        private void ForwardMouseLeave(object? sender, EventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
            }
        }

        private void ForwardMouseWheel(object? sender, MouseEventArgs e)
            => OnMouseWheel(sender, e);

        private void RequestVectorRender()
        {
            if (_disposed || !_vectorMapHooked ||
                _vectorMapSurface == null)
                return;

            _vectorMapSurface.SetCamera(
                _centerX,
                _centerZ,
                _scale);

            _vectorMapSurface.SetDynamicState(
                BuildVectorTruckState(),
                BuildVectorCreateMarkerState(),
                _onlySelectedChk?.Checked == true,
                showCone: true);
        }

        private MapEditorVectorTruck? BuildVectorTruckState()
        {
            if (!_truckX.HasValue || !_truckZ.HasValue)
                return null;

            return new MapEditorVectorTruck(
                _truckX.Value,
                _truckZ.Value,
                _truckHeading,
                _headYaw,
                _headPitch,
                _truckKnown);
        }

        private MapEditorVectorCreateMarker? BuildVectorCreateMarkerState()
        {
            if (!_createMode || _editingCopy == null)
                return null;

            return new MapEditorVectorCreateMarker(
                _editingCopy.X,
                _editingCopy.Z,
                _editingCopy.Y,
                _createModeFromAr);
        }

        private void RebuildVectorStaticCache()
        {
            if (!_vectorMapHooked ||
                _vectorMapSurface == null)
                return;

            var points = new List<MapEditorVectorPoint>(_targets.Count);

            foreach (var target in _targets)
            {
                _pointModel.TryGetValue(target.id, out var pm);

                var isCity = pm?.IsCity == true;
                var isPoi = pm?.IsPoi == true;
                var isSdo = pm?.IsSdo == true;
                var disabled = pm != null && !pm.Enabled;

                bool visible = true;

                if (disabled)
                {
                    visible =
                        _catVisible.TryGetValue(
                            "Отключенные",
                            out var disabledVisible)
                            ? disabledVisible
                            : true;
                }
                else
                {
                    var category =
                        isCity ? "Города" :
                        isPoi || isSdo
                            ? pm?.Category ?? ""
                            : "Цели";

                    if (_catVisible.TryGetValue(
                            category,
                            out var categoryVisible))
                        visible = categoryVisible;
                }

                var selected =
                    _selectedIds.Contains(target.id) ||
                    _selectedGameName == target.id;

                var color = target.color;

                if (isCity)
                {
                    color = Color.FromArgb(255, 230, 0);
                }
                else if (isPoi && pm != null)
                {
                    color = CategoryColor(pm.Category);
                }
                else if (disabled)
                {
                    color = Color.FromArgb(120, 120, 120);
                }

                var name =
                    isPoi && pm != null
                        ? (string.IsNullOrEmpty(pm.RealName)
                            ? pm.Category
                            : pm.RealName)
                        : isCity && pm != null
                            ? pm.RealName
                            : target.name;

                var layer = 100;

                if (isCity)
                    layer = 1000000;
                else if (isSdo || isPoi)
                    layer = SdoMeta.LayerOf(pm?.Category ?? "");

                points.Add(
                    new MapEditorVectorPoint(
                        target.id,
                        name,
                        target.x,
                        target.z,
                        layer,
                        color,
                        visible,
                        selected,
                        isCity,
                        isPoi,
                        isSdo,
                        disabled));
            }

            points = points
                .OrderBy(p => p.Layer)
                .ToList();

            _vectorMapSurface.SetStaticData(
                _roads,
                points);

            RequestVectorRender();
        }

        private void RebuildVectorDynamicCache()
        {
            RequestVectorRender();
        }

        protected void DisposeVectorRenderer()
        {
            if (_vectorMapHost != null)
            {
                _vectorMapHost.MouseDown -= ForwardMouseDown;
                _vectorMapHost.MouseMove -= ForwardMouseMove;
                _vectorMapHost.MouseUp -= ForwardMouseUp;
                _vectorMapHost.MouseClick -= ForwardMouseClick;
                _vectorMapHost.MouseLeave -= ForwardMouseLeave;
                _vectorMapHost.MouseWheel -= ForwardMouseWheel;

                _vectorMapHost.Child = null;
                _vectorMapHost.Dispose();
                _vectorMapHost = null;
            }

            _vectorMapSurface?.Dispose();
            _vectorMapSurface = null;
            _vectorMapHooked = false;
        }
    }
}
