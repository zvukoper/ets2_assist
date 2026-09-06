using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace ETS2_Assist_GUI
{
    public partial class MapEditorForm
    {
        private ElementHost? _vectorMapHost;
        private MapEditorVectorSurface? _vectorMapSurface;
        private bool _vectorMapHooked;
        private bool _vectorMapInvalidationHooked;
        private bool _vectorSyncing;

        private static bool _vectorMapIdleHookInstalled;

        static MapEditorForm()
        {
            InstallVectorBootstrap();
        }

        private static void InstallVectorBootstrap()
        {
            if (_vectorMapIdleHookInstalled)
                return;

            _vectorMapIdleHookInstalled = true;
            Application.Idle += AttachVectorRendererToOpenMapEditors;
        }

        private static void AttachVectorRendererToOpenMapEditors(object? sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is not MapEditorForm mapEditor)
                    continue;

                mapEditor.EnsureVectorRenderer();

                // В приложении используется один редактор карты за раз.
                // Не оставляем глобальный Idle-hook работать постоянно.
                Application.Idle -= AttachVectorRendererToOpenMapEditors;
                _vectorMapIdleHookInstalled = false;
                return;
            }
        }

        private void EnsureVectorRenderer()
        {
            if (_vectorMapHooked || _disposed || _mapPanel.IsDisposed || !_mapPanel.IsHandleCreated)
                return;

            _vectorMapSurface = new MapEditorVectorSurface();

            _vectorMapHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 18, 23),
                Child = _vectorMapSurface
            };

            // Внутри host поверхность WPF не участвует в hit-test:
            // мышь получает ElementHost, а ниже мы прокидываем её в старые handlers.
            _vectorMapSurface.IsHitTestVisible = false;

            // Отключаем старый GDI+ renderer карты.
            _mapPanel.Paint -= OnPaint;

            _mapPanel.Controls.Add(_vectorMapHost);
            _vectorMapHost.BringToFront();

            _vectorMapHost.MouseDown += ForwardMouseDown;
            _vectorMapHost.MouseMove += ForwardMouseMove;
            _vectorMapHost.MouseUp += ForwardMouseUp;
            _vectorMapHost.MouseClick += ForwardMouseClick;
            _vectorMapHost.MouseLeave += ForwardMouseLeave;
            _vectorMapHost.MouseWheel += ForwardMouseWheel;

            if (!_vectorMapInvalidationHooked)
            {
                _mapPanel.Invalidated += VectorMapInvalidated;
                _vectorMapInvalidationHooked = true;
            }

            FormClosed -= VectorMapFormClosed;
            FormClosed += VectorMapFormClosed;

            _vectorMapHooked = true;

            RebuildVectorStaticCache();
            SyncVectorRenderer();
        }

        private void VectorMapFormClosed(object? sender, FormClosedEventArgs e)
        {
            FormClosed -= VectorMapFormClosed;
            DisposeVectorRenderer();
        }

        private void VectorMapInvalidated(object? sender, InvalidateEventArgs e)
        {
            // Этот event является мостом из существующего RequestRender()/InvalidateMap()
            // в retained-mode renderer. Старый MapEditorForm менять не требуется.
            SyncVectorRenderer();
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

        private void SyncVectorRenderer()
        {
            if (_disposed || !_vectorMapHooked || _vectorMapSurface == null || _vectorSyncing)
                return;

            _vectorSyncing = true;
            try
            {
                // SetCamera() сам проверяет, изменились ли камера/размер.
                _vectorMapSurface.SetCamera(_centerX, _centerZ, _scale);

                _vectorMapSurface.SetSelectionState(
                    _selectedIds,
                    _selectedGameName,
                    _onlySelectedChk?.Checked == true);

                _vectorMapSurface.SetDynamicState(
                    BuildVectorTruckState(),
                    BuildVectorCreateMarkerState(),
                    _onlySelectedChk?.Checked == true,
                    showCone: true);
            }
            finally
            {
                _vectorSyncing = false;
            }
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
            if (!_vectorMapHooked || _vectorMapSurface == null)
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
                    visible = _catVisible.TryGetValue("Отключенные", out var disabledVisible)
                        ? disabledVisible
                        : true;
                }
                else
                {
                    var category =
                        isCity ? "Города" :
                        isPoi || isSdo ? pm?.Category ?? "" :
                        "Цели";

                    if (_catVisible.TryGetValue(category, out var categoryVisible))
                        visible = categoryVisible;
                }

                var color = target.color;

                if (isCity)
                    color = Color.FromArgb(255, 230, 0);
                else if (isPoi && pm != null)
                    color = CategoryColor(pm.Category);
                else if (disabled)
                    color = Color.FromArgb(120, 120, 120);

                var name =
                    isPoi && pm != null
                        ? (string.IsNullOrEmpty(pm.RealName) ? pm.Category : pm.RealName)
                        : isCity && pm != null
                            ? pm.RealName
                            : target.name;

                var layer = 100;
                if (isCity)
                    layer = 1_000_000;
                else if (isSdo || isPoi)
                    layer = SdoMeta.LayerOf(pm?.Category ?? "");

                points.Add(new MapEditorVectorPoint(
                    target.id,
                    name,
                    target.x,
                    target.z,
                    layer,
                    color,
                    visible,
                    Selected: false,
                    isCity,
                    isPoi,
                    isSdo,
                    disabled));
            }

            points.Sort(static (a, b) => a.Layer.CompareTo(b.Layer));
            _vectorMapSurface.SetStaticData(_roads, points);
        }

        private void RebuildVectorDynamicCache()
            => SyncVectorRenderer();

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

            if (_vectorMapInvalidationHooked)
            {
                _mapPanel.Invalidated -= VectorMapInvalidated;
                _vectorMapInvalidationHooked = false;
            }

            _vectorMapSurface?.Dispose();
            _vectorMapSurface = null;
            _vectorMapHooked = false;
            _vectorSyncing = false;

            InstallVectorBootstrap();
        }
    }
}
