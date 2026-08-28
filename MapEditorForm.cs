using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Text;

namespace ETS2_Assist_GUI
{
    public partial class MapEditorForm : Form
    {
        private class MapPanel : Panel
        {
            public MapPanel() { DoubleBuffered = true; }
        }

        private readonly List<(double x1, double z1, double x2, double z2)> _roads = new();
        private GraphicsPath? _roadsPath;
        private bool _mapReady;
        private readonly List<(string name, string id, double x, double y, double z)> _cities = new();
        private readonly List<(string id, string name, double x, double z, Color color)> _targets = new();

        private double _centerX, _centerZ;
        private double _scale = 1.5;
        private bool _viewReady;
        private double? _truckX, _truckZ;

        private bool _truckKnown;
        private DateTime _truckLastSeen = DateTime.MinValue;
        private DateTime _lastTruckCoordApply = DateTime.MinValue;
        private double _candTx, _candTz;
        private bool _haveCandidate = false;
        // Правдоподобный диапазон карты (м) — вне его сырые координаты считаем мусором.
        private const double TruckBoundsMinX = 100000, TruckBoundsMaxX = 175000;
        private const double TruckBoundsMinZ = -70000, TruckBoundsMaxZ = 25000;
        private const double TruckSanityMaxJumpM = 5000;
        private System.Windows.Forms.Timer _truckWatchdog = null!;

        private bool _panning;
        private int _panStartX, _panStartY;
        private double _panStartCenterX, _panStartCenterZ;
        private const double ClickThresholdPx = 12;
        private const string TelemetryWsUrlFallback = "ws://localhost:8080/api/ws/delta/flat/?throttle=50";
        private const double MaxScale = 8000;
        private const double TruckCoordScaleX = 1e11;
        private const double TruckCoordScaleZ = 1e11;
        private const double ClipXMin = 111805.88;
        private const double ClipZMin = -36536.58;

        private ClientWebSocket? _ws;
        private readonly System.Windows.Forms.Timer _wsReconnectTimer = new() { Interval = 2500 };
        private CancellationTokenSource? _cts;
        private readonly List<int> _telemetryPorts = new();
        private int _telemetryPortIdx = 0;
        private bool _disposed;

        private readonly MapPanel _mapPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 18, 23) };
        private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6), WrapContents = false };
        private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.FromArgb(143, 160, 185), BackColor = Color.FromArgb(15, 18, 23), Padding = new Padding(4, 3, 0, 0) };
        private readonly TreeView _sidebar = new() { Dock = DockStyle.Left, Width = 220, BackColor = Color.FromArgb(20, 25, 35), ForeColor = Color.LightGray, Font = new Font("Segoe UI", 9) };
        private readonly ToolTip _tooltip = new() { InitialDelay = 0, ReshowDelay = 0, ShowAlways = true };

        private readonly string _stateFile = Path.Combine(AppDataPaths.UserDataDirectory, "map_editor_state.json");
        private readonly string _targetsFile = AppDataPaths.CustomTargetsFile;
        private readonly string _roadsFile = Path.Combine(AppDataPaths.StaticDataDirectory, "GeoJson", "roads.geojson");
        private readonly string _citiesFile = Path.Combine(AppDataPaths.StaticDataDirectory, "localized_cities", "cities_sibirmap.json");
        private readonly string _overlaysFile = Path.Combine(AppDataPaths.StaticDataDirectory, "Overlays.json");
        private readonly string _webDataFile = AppDataPaths.WebDataFile;
        private readonly List<(string category, string uid, double x, double z)> _pois = new();
        private readonly List<(string category, string uid, double x, double z)> _poisRaw = new();

        public MapEditorForm()
        {
            InitializeComponent();
            LoadCities();
            StartRoadsLoad();
            LoadTargets();
            LoadOverlays();
            PopulateSidebar();
            LoadEditorState();
            if (_viewReady) RequestRender();
            else FitToAllCities();
            StartTelemetry();
            _truckWatchdog = new System.Windows.Forms.Timer { Interval = 1000 };
            _truckWatchdog.Tick += (s, e) =>
            {
                if (_disposed) return;
                if (_truckKnown && (DateTime.Now - _truckLastSeen).TotalSeconds > 3)
                {
                    _truckKnown = false;
                    SetTruckStatus();
                    InvalidateMap();
                }
            };
            _truckWatchdog.Start();
            _wsReconnectTimer.Tick += (s, e) => { _wsReconnectTimer.Stop(); EnsureTelemetry(); };
            this.MouseWheel += OnMouseWheel;
            LogEditor($"Редактор карты открыт. Городов={_cities.Count}, целей={_targets.Count}, POI={_pois.Count}, дороги={(_roadsPath != null ? "загружены" : "нет")}.");
            SetTruckStatus();
        }

        private void InitializeComponent()
        {
            Text = "Редактор карты";
            ClientSize = new Size(1000, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(15, 18, 23);
            Controls.Add(_mapPanel);
            Controls.Add(_statusLabel);
            Controls.Add(_toolbar);
            Controls.Add(_sidebar);

            var findTruck = new Button { Text = "найти грузовик", Width = 130, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            findTruck.Click += (s, e) => FindTruck();

            var showAll = new Button { Text = "показать всё", Width = 110, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            showAll.Click += (s, e) => FitToAll();

            var reloadTargets = new Button { Text = "обновить цели", Width = 120, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            reloadTargets.Click += (s, e) => { LoadTargets(); PopulateSidebar(); RequestRender(); };

            _toolbar.Controls.Add(findTruck);
            _toolbar.Controls.Add(showAll);
            _toolbar.Controls.Add(reloadTargets);

            _sidebar.AfterSelect += (s, e) =>
            {
                if (e.Node?.Tag is (double x, double z)) CenterOn(x, z);
            };

            _mapPanel.Paint += OnPaint;
            _mapPanel.Resize += (s, e) => RequestRender();
            _mapPanel.MouseDown += OnMouseDown;
            _mapPanel.MouseMove += OnMouseMove;
            _mapPanel.MouseUp += OnMouseUp;
            _mapPanel.MouseClick += OnMouseClick;
            _mapPanel.MouseLeave += (s, e) => { if (_panning) { _panning = false; Cursor = Cursors.Default; } };

            FormClosing += OnFormClosing;
        }

        private void InvalidateMap()
        {
            if (_disposed) return;
            if (_mapPanel.IsHandleCreated)
                _mapPanel.BeginInvoke((Action)(() => { if (!_disposed) _mapPanel.Invalidate(); }));
            else
                _mapPanel.Invalidate();
        }

        private void RequestRender()
        {
            InvalidateMap();
        }

        private void LoadCities()
        {
            try
            {
                if (!File.Exists(_citiesFile)) return;
                var json = JObject.Parse(File.ReadAllText(_citiesFile));
                var list = json["citiesList"] as JArray;
                if (list == null) return;
                foreach (var c in list)
                {
                    var id = (string?)c["gameName"] ?? "";
                    var name = (string?)c["realName"] ?? id ?? "?";
                    if (!double.TryParse((string?)c["x"], NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) continue;
                    if (!double.TryParse((string?)c["z"], NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) continue;
                    double.TryParse((string?)c["y"], NumberStyles.Any, CultureInfo.InvariantCulture, out var y);
                    _cities.Add((name, id, x, y, z));
                }
            }
            catch (Exception ex) { Debug.WriteLine("LoadCities: " + ex.Message); }
        }

        private void LoadTargets()
        {
            _targets.Clear();
            try
            {
                if (!File.Exists(_targetsFile)) return;
                var json = JObject.Parse(File.ReadAllText(_targetsFile));
                var list = json["customTargets"] as JArray;
                if (list == null) return;
                foreach (var t in list)
                {
                    var id = (string?)t["gameName"];
                    if (string.IsNullOrEmpty(id)) continue;
                    var name = (string?)t["realName"] ?? id;
                    var coords = (string?)t["coords"] ?? "";
                    var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;
                    if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) continue;
                    _targets.Add((id, name, x, z, ParseColor((string?)t["color"] ?? "default")));
                }
            }
            catch (Exception ex) { Debug.WriteLine("LoadTargets: " + ex.Message); }
        }

        private static Color ParseColor(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "default") return Color.FromArgb(255, 165, 0);
            try { return s.StartsWith("#") ? ColorTranslator.FromHtml(s) : Color.FromName(s); }
            catch { return Color.FromArgb(255, 165, 0); }
        }

        private void StartRoadsLoad()
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Task.Run(() =>
            {
                try
                {
                    ReportProgress("Этап 1/2: разбор файла дорог (45 МБ)...");
                    if (!File.Exists(_roadsFile)) { ReportProgress("Файл дорог не найден."); return; }
                    int total = CountFeatures();
                    int processed = 0, segs = 0;
                    using (var sr = new StreamReader(_roadsFile))
                    using (var reader = new JsonTextReader(sr))
                    {
                        while (reader.Read())
                        {
                            if (ct.IsCancellationRequested) return;
                            if (reader.TokenType == JsonToken.PropertyName && (string?)reader.Value == "features")
                            {
                                reader.Read(); // StartArray
                                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                                {
                                    if (ct.IsCancellationRequested) return;
                                    if (reader.TokenType != JsonToken.StartObject) continue;
                                    var feat = JObject.Load(reader);
                                    var geom = feat["geometry"];
                                    if ((string?)geom?["type"] != "LineString") continue;
                                    var coords = geom?["coordinates"] as JArray;
                                    if (coords == null || coords.Count < 2) continue;
                                    for (int i = 0; i < coords.Count - 1; i++)
                                    {
                                        var a = coords[i] as JArray;
                                        var b = coords[i + 1] as JArray;
                                        if (a == null || b == null || a.Count < 2 || b.Count < 2) continue;
                                        if (!double.TryParse((string?)a[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var ax)) continue;
                                        if (!double.TryParse((string?)a[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var az)) continue;
                                        if (!double.TryParse((string?)b[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var bx)) continue;
                                        if (!double.TryParse((string?)b[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var bz)) continue;
                                        if (ax < ClipXMin || bx < ClipXMin || az > ClipZMin || bz > ClipZMin) continue;
                                        _roads.Add((ax, az, bx, bz));
                                        segs++;
                                    }
                                    processed++;
                                    if (processed % 500 == 0)
                                        ReportProgress($"Этап 2/2: дороги {processed} из {total}, сегментов {segs}");
                                }
                            }
                        }
                    }
                    ReportProgress($"Готово: дорог {processed}, сегментов {segs}. Отрисовка карты...");
                    BuildRoadsPath(ct);
                    if (ct.IsCancellationRequested) return;
                    ReportProgress($"Карта готова. Дорог {processed}, сегментов {segs}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Roads load error: " + ex.Message);
                    ReportProgress("Ошибка загрузки дорог: " + ex.Message);
                }
            }, ct);
        }

        private int CountFeatures()
        {
            try
            {
                using var sr = new StreamReader(_roadsFile);
                using var reader = new JsonTextReader(sr);
                int count = 0;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject && reader.Path.StartsWith("features"))
                        count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private void BuildRoadsPath(CancellationToken ct)
        {
            var path = new GraphicsPath();
            foreach (var r in _roads)
            {
                if (ct.IsCancellationRequested) { path.Dispose(); return; }
                path.StartFigure();
                path.AddLine((float)r.x1, (float)r.z1, (float)r.x2, (float)r.z2);
            }
            _roadsPath?.Dispose();
            _roadsPath = path;
            _mapReady = true;
            InvalidateMap();
        }

        private DateTime _lastProgress = DateTime.MinValue;
        private void ReportProgress(string msg)
        {
            if (_disposed) return;
            var now = DateTime.Now;
            if ((now - _lastProgress).TotalMilliseconds < 120 && msg.StartsWith("Этап 2")) return;
            _lastProgress = now;
            if (IsDisposed || !_statusLabel.IsHandleCreated) return;
            BeginInvoke((Action)(() => { if (!_disposed) _statusLabel.Text = msg; }));
        }

        private PointF WorldToScreen(double wx, double wz)
        {
            float sx = (float)(_mapPanel.Width / 2.0 + (wx - _centerX) / _scale);
            float sy = (float)(_mapPanel.Height / 2.0 + (wz - _centerZ) / _scale);
            return new PointF(sx, sy);
        }

        private (double wx, double wz) ScreenToWorld(float sx, float sy)
        {
            double wx = _centerX + (sx - _mapPanel.Width / 2.0) * _scale;
            double wz = _centerZ + (sy - _mapPanel.Height / 2.0) * _scale;
            return (wx, wz);
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double halfW = _mapPanel.Width / 2.0 * _scale;
            double halfH = _mapPanel.Height / 2.0 * _scale;
            double vMinX = _centerX - halfW, vMaxX = _centerX + halfW;
            double vMinZ = _centerZ - halfH, vMaxZ = _centerZ + halfH;

            if (_mapReady && _roadsPath != null)
            {
                var m = new Matrix();
                m.Translate(_mapPanel.Width / 2f, _mapPanel.Height / 2f);
                m.Scale(1f / (float)_scale, 1f / (float)_scale);
                m.Translate(-(float)_centerX, -(float)_centerZ);
                g.Transform = m;
                using var pen = new Pen(Color.FromArgb(110, 145, 165), (float)(2.2 * _scale));
                g.DrawPath(pen, _roadsPath);
                g.Transform = new Matrix();
            }
            else
            {
                g.Clear(Color.FromArgb(15, 18, 23));
                using var font = new Font("Segoe UI", 12);
                using var tb = new SolidBrush(Color.FromArgb(143, 160, 185));
                g.DrawString("Загрузка карты…", font, tb, 12, _mapPanel.Height / 2 - 10);
            }

            DrawGridTo(g, vMinX, vMaxX, vMinZ, vMaxZ);

            // POI — РИСУЕМ ПЕРВЫМИ, чтобы города и цели перекрывали их при наложении.
            // Скрываем ПОИ при масштабе > 10 м/px (слишком мелко — только шум на карте).
            if (_scale <= 10)
            {
                foreach (var poi in _pois)
                {
                    var p = WorldToScreen(poi.x, poi.z);
                    if (p.X < -50 || p.Y < -50 || p.X > _mapPanel.Width + 50 || p.Y > _mapPanel.Height + 50) continue;
                    using var brush = new SolidBrush(CategoryColor(poi.category));
                    g.FillEllipse(brush, p.X - 3, p.Y - 3, 6, 6);
                    g.DrawEllipse(new Pen(Color.Black, 1f), p.X - 3, p.Y - 3, 6, 6);
                    DrawLabelAbove(g, poi.category, p.X, p.Y, CategoryColor(poi.category));
                }
            }

            foreach (var c in _cities)
            {
                var p = WorldToScreen(c.x, c.z);
                if (p.X < -50 || p.Y < -50 || p.X > _mapPanel.Width + 50 || p.Y > _mapPanel.Height + 50) continue;
                using var brush = new SolidBrush(Color.FromArgb(204, 255, 230, 0));
                using var outline = new Pen(Color.Black, 2);
                g.FillEllipse(brush, p.X - 5, p.Y - 5, 11, 11);
                g.DrawEllipse(outline, p.X - 5, p.Y - 5, 11, 11);
                DrawLabelAbove(g, c.name, p.X, p.Y, Color.FromArgb(255, 235, 0), true);
            }

            foreach (var t in _targets)
            {
                var p = WorldToScreen(t.x, t.z);
                if (p.X < -50 || p.Y < -50 || p.X > _mapPanel.Width + 50 || p.Y > _mapPanel.Height + 50) continue;
                using var brush = new SolidBrush(t.color);
                g.FillEllipse(brush, p.X - 5, p.Y - 5, 10, 10);
                g.DrawEllipse(new Pen(Color.Black, 1.5f), p.X - 5, p.Y - 5, 10, 10);
                DrawLabelAbove(g, t.name, p.X, p.Y, Color.White, true);
            }

            if (_truckKnown && _truckX.HasValue && _truckZ.HasValue)
            {
                var p = WorldToScreen(_truckX.Value, _truckZ.Value);
                using var brush = new SolidBrush(Color.Red);
                var pts = new[] { new PointF(p.X, p.Y - 8), new PointF(p.X - 6, p.Y + 6), new PointF(p.X + 6, p.Y + 6) };
                g.FillPolygon(brush, pts);
                DrawLabelAbove(g, "Грузовик", p.X, p.Y, Color.Red, true);
            }
        }

        private static void LogEditor(string msg)
        {
            try { Logger.Current?.Info("[EDITOR] " + msg); }
            catch { }
        }

        private static void DrawLabelAbove(Graphics g, string text, float cx, float cy, Color textColor, bool bold = false, int gap = 6)
        {
            if (string.IsNullOrEmpty(text)) return;
            using var font = new Font("Segoe UI", bold ? 9.5f : 9f, bold ? FontStyle.Bold : FontStyle.Regular);
            var size = g.MeasureString(text, font);
            float x = cx - size.Width / 2f;
            float y = cy - size.Height - gap;
            using var black = new SolidBrush(Color.Black);
            foreach (var (dx, dy) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                g.DrawString(text, font, black, x + dx, y + dy);
            using var fg = new SolidBrush(textColor);
            g.DrawString(text, font, fg, x, y);
        }

        private void DrawGridTo(Graphics g, double worldLeft, double worldRight, double worldTop, double worldBottom)
        {
            double step = 100;
            double approxStep = _scale * 80;
            while (step < approxStep) step *= (step < 500 ? 2 : (step < 2500 ? 5 : 10));

            using var pen = new Pen(Color.FromArgb(58, 74, 90), 0.5f) { DashPattern = new[] { 4f, 6f } };
            for (double x = Math.Ceiling(worldLeft / step) * step; x <= worldRight; x += step)
            {
                var p1 = WorldToScreen(x, worldTop);
                var p2 = WorldToScreen(x, worldBottom);
                g.DrawLine(pen, p1, p2);
            }
            for (double z = Math.Ceiling(worldTop / step) * step; z <= worldBottom; z += step)
            {
                var p1 = WorldToScreen(worldLeft, z);
                var p2 = WorldToScreen(worldRight, z);
                g.DrawLine(pen, p1, p2);
            }
            using var font = new Font("Segoe UI", 11);
            using var brush = new SolidBrush(Color.FromArgb(143, 160, 185));
            g.DrawString($"1 клетка = {step} м    Масштаб: {_scale:F1} м/px", font, brush, 8, 4);
        }

        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            if (!_viewReady) return;
            int px = e.X - _mapPanel.Left;
            int py = e.Y - _mapPanel.Top;
            var (wx, wz) = ScreenToWorld(px, py);
            double factor = e.Delta > 0 ? 0.9 : 1.1;
            _scale = Math.Max(0.05, Math.Min(MaxScale, _scale * factor));
            _centerX = wx - (px - _mapPanel.Width / 2.0) * _scale;
            _centerZ = wz - (py - _mapPanel.Height / 2.0) * _scale;
            UpdateStatus();
            RequestRender();
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _panning = true;
                _panStartX = e.X;
                _panStartY = e.Y;
                _panStartCenterX = _centerX;
                _panStartCenterZ = _centerZ;
                Cursor = Cursors.Hand;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_panning)
            {
                _centerX = _panStartCenterX - (e.X - _panStartX) * _scale;
                _centerZ = _panStartCenterZ - (e.Y - _panStartY) * _scale;
                UpdateStatus();
                RequestRender();
                return;
            }

            var tip = HoverInfo(e.X, e.Y);
            if (tip != null)
            {
                Cursor = Cursors.Hand;
                _tooltip.Show(tip, _mapPanel, e.X + 12, e.Y + 12, 4000);
            }
            else
            {
                Cursor = Cursors.Default;
                _tooltip.Hide(_mapPanel);
            }
        }

        private string? HoverInfo(int sx, int sy)
        {
            double best = 10;
            string? found = null;
            foreach (var c in _cities)
            {
                var p = WorldToScreen(c.x, c.z);
                var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
                if (d < best) { best = d; found = string.IsNullOrEmpty(c.id) ? c.name : c.id; }
            }
            foreach (var t in _targets)
            {
                var p = WorldToScreen(t.x, t.z);
                var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
                if (d < best) { best = d; found = t.id; }
            }
            foreach (var poi in _pois)
            {
                var p = WorldToScreen(poi.x, poi.z);
                var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
                if (d < best) { best = d; found = poi.category + "  " + poi.x.ToString("F2", CultureInfo.InvariantCulture) + ", 0, " + poi.z.ToString("F2", CultureInfo.InvariantCulture); }
            }
            return found;
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _panning)
            {
                _panning = false;
                Cursor = Cursors.Default;
            }
        }

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var (wx, wz) = ScreenToWorld(e.X, e.Y);

            foreach (var t in _targets)
            {
                var p = WorldToScreen(t.x, t.z);
                if (Math.Abs(p.X - e.X) <= ClickThresholdPx && Math.Abs(p.Y - e.Y) <= ClickThresholdPx)
                {
                    Clipboard.SetText(t.id);
                    ShowCopied($"Скопирован id цели: {t.id}  (открываю файл целей)");
                    try { Process.Start(new ProcessStartInfo(_targetsFile) { UseShellExecute = true }); } catch { }
                    return;
                }
            }

            foreach (var c in _cities)
            {
                var p = WorldToScreen(c.x, c.z);
                if (Math.Abs(p.X - e.X) <= ClickThresholdPx && Math.Abs(p.Y - e.Y) <= ClickThresholdPx)
                {
                    var coord = $"{c.x.ToString("F2", CultureInfo.InvariantCulture)}, {c.y.ToString("F2", CultureInfo.InvariantCulture)}, {c.z.ToString("F2", CultureInfo.InvariantCulture)}";
                    Clipboard.SetText(coord);
                    ShowCopied($"Скопированы координаты POI {c.name}: {coord}");
                    return;
                }
            }

            var anyCoord = $"{wx.ToString("F2", CultureInfo.InvariantCulture)}, 0, {wz.ToString("F2", CultureInfo.InvariantCulture)}";
            Clipboard.SetText(anyCoord);
            ShowCopied($"Скопированы координаты: {anyCoord}");
        }

        private DateTime _lastCopied;
        private string _baseStatus = "";
        private void ShowCopied(string msg)
        {
            _lastCopied = DateTime.Now;
            _statusLabel.Text = msg;
        }

        private void SetTruckStatus()
        {
            if (_disposed) return;
            string ind = _truckKnown ? "● Координаты грузовика онлайн" : "● Нет данных от грузовика";
            _statusLabel.ForeColor = _truckKnown ? Color.FromArgb(70, 200, 90) : Color.FromArgb(230, 90, 90);
            _baseStatus = $"{ind}   Центр: {_centerX:F0}, {_centerZ:F0}  Масштаб: {_scale:F1} м/px";
            if ((DateTime.Now - _lastCopied).TotalSeconds > 3) _statusLabel.Text = _baseStatus;
        }

        private void UpdateStatus()
        {
            SetTruckStatus();
        }

        private void CenterOnTruck()
        {
            if (!_truckKnown || !_truckX.HasValue || !_truckZ.HasValue) return;
            _centerX = _truckX.Value;
            _centerZ = _truckZ.Value;
            _scale = 1.5;
            UpdateStatus();
            RequestRender();
        }

        private void FitToAll()
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            bool any = false;
            void Ext(double x, double z)
            {
                any = true;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
            foreach (var r in _roads) { Ext(r.x1, r.z1); Ext(r.x2, r.z2); }
            foreach (var c in _cities) Ext(c.x, c.z);
            foreach (var t in _targets) Ext(t.x, t.z);
            foreach (var p in _pois) Ext(p.x, p.z);
            if (_truckKnown && _truckX.HasValue && _truckZ.HasValue) { Ext(_truckX.Value, _truckZ.Value); }
            if (!any) return;
            _centerX = (minX + maxX) / 2;
            _centerZ = (minZ + maxZ) / 2;
            double pad = 2000;
            double worldW = (maxX - minX) + pad * 2;
            double worldH = (maxZ - minZ) + pad * 2;
            double sx = _mapPanel.Width / worldW;
            double sz = _mapPanel.Height / worldH;
            _scale = Math.Max(0.05, Math.Min(sx, sz));
            UpdateStatus();
            RequestRender();
        }

        private void FitToAllCities()
        {
            if (_cities.Count == 0) return;
            FitToAll();
            _viewReady = true;
        }

        private static readonly Dictionary<string, Color> _poiPalette = new()
        {
            ["Company"] = Color.FromArgb(255, 120, 200),
            ["BusStop"] = Color.FromArgb(120, 220, 255),
            ["Ferry"] = Color.FromArgb(120, 255, 180),
            ["Fuel"] = Color.FromArgb(255, 200, 80),
            ["Garage"] = Color.FromArgb(180, 160, 255),
            ["Overlay"] = Color.FromArgb(200, 200, 200),
            ["Parking"] = Color.FromArgb(255, 160, 90),
            ["Recruitment"] = Color.FromArgb(255, 120, 120),
            ["Service"] = Color.FromArgb(120, 255, 255),
            ["Train"] = Color.FromArgb(160, 200, 255),
            ["TruckDealer"] = Color.FromArgb(255, 220, 120),
            ["WeightStation"] = Color.FromArgb(220, 180, 255),
        };

        private static Color CategoryColor(string cat)
            => _poiPalette.TryGetValue(cat, out var c) ? c : Color.Magenta;

        private void LoadOverlays()
        {
            _poisRaw.Clear();
            try
            {
                if (!File.Exists(_overlaysFile)) return;
                var json = JObject.Parse(File.ReadAllText(_overlaysFile));
                foreach (var prop in json.Properties())
                {
                    var arr = prop.Value as JArray;
                    if (arr == null) continue;
                    foreach (var item in arr)
                    {
                        var uid = (string?)item["uid"] ?? prop.Name;
                        var xv = item["x"];
                        var zv = item["z"];
                        if (xv == null || zv == null) continue;
                        if (!double.TryParse(xv.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)) continue;
                        if (!double.TryParse(zv.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var z)) continue;
                        double xr = x, zr = z;
                        if (Math.Abs(xr) > 1_000_000 && Math.Abs(zr) > 1_000_000)
                        {
                            xr /= 100; zr /= 100;
                            Debug.WriteLine($"LoadOverlays: POI {uid} нормализован /100 (было x={x}, z={z}) -> x={xr}, z={zr}");
                        }
                        _poisRaw.Add((prop.Name, uid, xr, zr));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("LoadOverlays: " + ex.Message); LogEditor("LoadOverlays: ошибка " + ex.Message); }
            ApplyPoiTransform();
            LogEditor($"LoadOverlays: загружено POI={_pois.Count} (raw={_poisRaw.Count}) из {_overlaysFile}.");
        }

        private void ApplyPoiTransform()
        {
            _pois.Clear();
            foreach (var p in _poisRaw)
            {
                double x = p.x;
                double z = p.z;
                if (x < ClipXMin || z > ClipZMin) continue;
                _pois.Add((p.category, p.uid, x, z));
            }
        }

        private void PopulateSidebar()
        {
            _sidebar.Nodes.Clear();
            var tNode = new TreeNode("Цели (" + _targets.Count + ")");
            foreach (var t in _targets)
            {
                var n = new TreeNode(t.name) { Tag = (t.x, t.z) };
                tNode.Nodes.Add(n);
            }
            tNode.Expand();
            _sidebar.Nodes.Add(tNode);

            var cNode = new TreeNode("Города (" + _cities.Count + ")");
            foreach (var c in _cities)
            {
                var n = new TreeNode(c.name) { Tag = (c.x, c.z) };
                cNode.Nodes.Add(n);
            }
            cNode.Expand();
            _sidebar.Nodes.Add(cNode);

            foreach (var grp in _pois.GroupBy(p => p.category).OrderBy(g => g.Key))
            {
                var catNode = new TreeNode(grp.Key + " (" + grp.Count() + ")");
                foreach (var p in grp)
                {
                    var n = new TreeNode(p.uid) { Tag = (p.x, p.z) };
                    catNode.Nodes.Add(n);
                }
                _sidebar.Nodes.Add(catNode);
            }
        }

        private void CenterOn(double x, double z)
        {
            _centerX = x;
            _centerZ = z;
            _scale = 2.0;
            UpdateStatus();
            RequestRender();
        }

        private void FindTruck()
        {
            if (_truckKnown) CenterOnTruck();
            else ShowCopied("Данные фуры недоступны (игра не запущена?)");
        }

        private void StartTelemetry()
        {
            try { EnsureTelemetry(); }
            catch { _wsReconnectTimer.Start(); }
        }

        private List<int> GetCandidatePorts()
        {
            var ports = new List<int>();
            try
            {
                if (File.Exists(_webDataFile))
                {
                    var json = JObject.Parse(File.ReadAllText(_webDataFile));
                    var port = json["wsPort"];
                    if (port != null && int.TryParse(port.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 && !ports.Contains(p))
                        ports.Add(p);
                }
            }
            catch { }
            if (!ports.Contains(8080)) ports.Add(8080);
            LogEditor($"GetCandidatePorts: web_data.json={_webDataFile} существует={File.Exists(_webDataFile)}; кандидаты=[" + string.Join(",", ports) + "].");
            return ports;
        }

        private async void EnsureTelemetry()
        {
            if (_disposed) return;
            if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)) return;
            var ports = GetCandidatePorts();
            if (ports.Count == 0) ports.Add(8080);
            if (_telemetryPortIdx >= ports.Count) _telemetryPortIdx = 0;
            int port = ports[_telemetryPortIdx];
            LogEditor($"EnsureTelemetry: попытка подключения к ws://localhost:{port}/api/ws/delta/flat/ (индекс {_telemetryPortIdx} из {ports.Count})");
            var cts = new CancellationTokenSource();
            _cts = cts;
            try
            {
                var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(new Uri($"ws://localhost:{port}/api/ws/delta/flat/?throttle=50"), cts.Token).ConfigureAwait(false);
                _ws = ws;
                LogEditor($"EnsureTelemetry: ПОДКЛЮЧЕНО к порту {port}.");
                _ = ReceiveLoop(ws, port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogEditor($"EnsureTelemetry: подключение к порту {port} отменено.");
            }
            catch (Exception ex)
            {
                LogEditor($"EnsureTelemetry: ОШИБКА подключения к порту {port}: {ex.GetType().Name}: {ex.Message}");
                _telemetryPortIdx = (_telemetryPortIdx + 1) % Math.Max(1, ports.Count);
                if (!_disposed) _wsReconnectTimer.Start();
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, int port, CancellationToken token)
        {
            var buf = new byte[16384];
            try
            {
                while (!_disposed && ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token).ConfigureAwait(false);
                        if (res.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessTelemetry(text, port);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogEditor($"EnsureTelemetry: соединение с портом {port} разорвано: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { ws.Dispose(); } catch { }
                if (_ws == ws) _ws = null;
                if (!_disposed) _wsReconnectTimer.Start();
            }
        }

        private void ProcessTelemetry(string text, int port)
        {
            try
            {
                var json = JObject.Parse(text);
                var placement = json["truck.world.placement"] as JArray;
                if (placement == null) placement = json.SelectToken("truck.world.placement") as JArray;
                if (placement == null)
                {
                    var truck = json["truck"] as JObject;
                    if (truck != null) placement = truck["world"]?["placement"] as JArray;
                }
                        if (placement != null && placement.Count >= 3)
                        {
                            var xTok = placement[0];
                            var zTok = placement[2];
                            if (xTok != null && zTok != null
                                && double.TryParse(xTok.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tx)
                                && double.TryParse(zTok.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tz))
                            {
                                // Координаты применяем НЕ чаще 1 раза в секунду и только если
                                // сэмпл правдоподобен (в границах карты и без «прыжка» >5 км) —
                                // иначе мусорные кадры телеметрии уносят фуру за много км.
                                _truckLastSeen = DateTime.Now;
                                double ax = tx / TruckCoordScaleX;
                                double az = tz / TruckCoordScaleZ;
                                bool inBounds = ax >= TruckBoundsMinX && ax <= TruckBoundsMaxX
                                                && az >= TruckBoundsMinZ && az <= TruckBoundsMaxZ;
                                bool sane = inBounds && (!_truckKnown || !_truckX.HasValue || !_truckZ.HasValue
                                    || Math.Sqrt((ax - _truckX.Value) * (ax - _truckX.Value) + (az - _truckZ.Value) * (az - _truckZ.Value)) <= TruckSanityMaxJumpM);
                                if (sane) { _candTx = tx; _candTz = tz; _haveCandidate = true; }

                                var now = DateTime.Now;
                                if ((now - _lastTruckCoordApply).TotalMilliseconds >= 1000)
                                {
                                    _lastTruckCoordApply = now;
                                    if (!_truckKnown)
                                    {
                                        if (inBounds)
                                        {
                                            _truckX = ax; _truckZ = az; _truckKnown = true;
                                            if (!_disposed) BeginInvoke((Action)(() => { if (!_disposed) { SetTruckStatus(); InvalidateMap(); } }));
                                        }
                                        else
                                        {
                                            LogEditor("[TELEMETRY] первый сэмпл отброшен: вне границ карты (мусор).");
                                        }
                                    }
                                    else if (_haveCandidate)
                                    {
                                        double cax = _candTx / TruckCoordScaleX;
                                        double caz = _candTz / TruckCoordScaleZ;
                                        _truckX = cax; _truckZ = caz;
                                        if (!_disposed) BeginInvoke((Action)(() => { if (!_disposed) { SetTruckStatus(); InvalidateMap(); } }));
                                    }
                                    else
                                    {
                                        LogEditor($"[TELEMETRY] за секунду все сэмплы отброшены (мусор/прыжок >{TruckSanityMaxJumpM}м).");
                                    }
                                    _haveCandidate = false;
                                }
                            }
                            else
                            {
                                LogEditor($"EnsureTelemetry: placement найден, но координаты не распознаны (x='{xTok}', z='{zTok}').");
                            }
                        }
                else
                {
                    LogEditor($"EnsureTelemetry: сообщение получено, placement отсутствует (ключи: {string.Join(",", ((System.Collections.Generic.IDictionary<string, JToken>)json).Keys.Take(8))}).");
                }
            }
            catch (Exception ex)
            {
                LogEditor($"EnsureTelemetry: ошибка разбора сообщения: {ex.Message}");
            }
        }

        private void LoadEditorState()
        {
            try
            {
                if (!File.Exists(_stateFile)) return;
                var json = JObject.Parse(File.ReadAllText(_stateFile));
                if (json["centerX"] != null) _centerX = (double)json["centerX"];
                if (json["centerZ"] != null) _centerZ = (double)json["centerZ"];
                if (json["scale"] != null) _scale = (double)json["scale"];
                if (_cities.Count > 0) _viewReady = true;
            }
            catch { }
        }

        private void SaveEditorState()
        {
            try
            {
                var json = new JObject
                {
                    ["centerX"] = _centerX,
                    ["centerZ"] = _centerZ,
                    ["scale"] = _scale
                };
                File.WriteAllText(_stateFile, json.ToString());
            }
            catch { }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _wsReconnectTimer.Stop(); } catch { }
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            try { _roadsPath?.Dispose(); } catch { _roadsPath = null; }
            try { _tooltip.Dispose(); } catch { }
            SaveEditorState();
        }
    }
}
