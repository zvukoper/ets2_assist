using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

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
        // Высота грузовика (placement[1]) — для клика-копирования координат (v72).
        private double? _truckY;
        // heading фуры (доля оборота, от placement[3]; 0 = север(-Z), растёт против часовой).
        private double _truckHeading;
        // УГОЛ ПОВОРОТА ГОЛОВЫ (truck.head.offset[3], доля оборота) — для конуса обзора.
        private double _headYaw;

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

        // Состояние наличия placement в потоке телеметрии (для анти-спам-лога):
        // null = ещё не определено; true/false = был/не был placement в последнем кадре.
        private bool? _teleHadPlacement = null;
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
        // Статусная строка (8px): левая половина — индикация состояний (окружность-индикатор + текст),
        // правая — выполняемые операции (вращающийся индикатор / зелёная галочка + текст).
        // Клик по строке открывает папку логов приложения.
        private readonly EditorStatusBar _statusBar = new() { Dock = DockStyle.Bottom, Height = 24 };
        private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.FromArgb(143, 160, 185), BackColor = Color.FromArgb(15, 18, 23), Padding = new Padding(4, 3, 0, 0), Visible = false };
        private readonly TreeView _sidebar = new() { Dock = DockStyle.Left, Width = 220, BackColor = Color.FromArgb(20, 25, 35), ForeColor = Color.LightGray, Font = new Font("Segoe UI", 9), CheckBoxes = true };
        private readonly ToolTip _tooltip = new() { InitialDelay = 0, ReshowDelay = 0, ShowAlways = true };

        private readonly string _stateFile = Path.Combine(AppDataPaths.UserDataDirectory, "map_editor_state.json");
        private readonly string _targetsFile = AppDataPaths.CustomTargetsFile;
        private readonly string _roadsFile = Path.Combine(AppDataPaths.StaticDataDirectory, "GeoJson", "roads.geojson");
        private readonly string _citiesFile = Path.Combine(AppDataPaths.StaticDataDirectory, "localized_cities", "cities_sibirmap.json");
        private readonly string _overlaysFile = Path.Combine(AppDataPaths.StaticDataDirectory, "Overlays.json");
        private readonly string _webDataFile = AppDataPaths.WebDataFile;
        private readonly List<(string category, string uid, double x, double z)> _pois = new();
        private readonly List<(string category, string uid, double x, double z)> _poisRaw = new();

        // --- СИСТЕМА OVERRIDES (кастомные точки поверх статических) ---
        private readonly string _overridesDir = Path.Combine(AppDataPaths.UserDataDirectory, "map_overrides");
        private readonly string _loadOrderFile = Path.Combine(AppDataPaths.UserDataDirectory, "map_overrides", "load_order.txt");
        private readonly List<string> _overrideFiles = new();   // файлы по load_order (сверху — приоритет)
        private string _selectedOverrideFile = "custom_map1.json";
        // Авторитетная модель точек (статика + overrides), ключ — GameName.
        private readonly Dictionary<string, PointData> _pointModel = new();
        private readonly HashSet<string> _staticNames = new(); // gameName, пришедшие из статического custom_targets.json
        private string? _selectedGameName;   // выбранная точка
        private bool _createMode;            // режим создания новой точки
        // ПОМЕТКА ИЗ АР (v70): серый крестик в месте будущей точки (координаты мира).
        private bool _createModeFromAr;
        private double _arPinWx, _arPinWy, _arPinWz;
        private readonly HashSet<string> _dirtyFields = new(); // изменённые поля текущей точки
        private PointData? _editingCopy;     // копия для редактирования (отмена)

        // Панели редактирования / overrides
        private Panel _topPanel = null!;
        private ComboBox _overrideCombo = null!;
        private Panel _editPanel = null!;
        private FlowLayoutPanel _editFields = null!;
        private readonly Dictionary<string, Control> _fieldControls = new();
        private Button _btnSavePoint = null!;
        private Button _btnCancelPoint = null!;
        private Button _btnDeletePoint = null!;
        private Button _btnAddPoint = null!;
        private CheckBox _onlySelectedChk = null!;

        // Перетаскивание точки мышью
        private string? _dragId;
        private bool _dragMoved;
        // Мультивыбор точек (для карты/копирования). Ключ: для целей — gameName,
        // для городов — "city:<name>", для POI — "poi:<uid>".
        private readonly HashSet<string> _selectedIds = new();
        // Координаты всех точек (цели/города/POI) для подсветки/вписывания в карту.
        private readonly Dictionary<string, (double x, double z, string label)> _selectLookup = new();
        private bool _suppressCheck;           // защита от рекурсии при программной установке чекбоксов
        private readonly ToolTip _editTip = new() { InitialDelay = 300, ShowAlways = true };
        private bool _sanitizing;                 // защита от рекурсии при санитизации GameName
        private bool _loadingPanel;               // подавляет OnFieldChanged при программной загрузке точки в панель
        private Label? _gameNameError;           // красная подпись под полем GameName
        private Label? _gameNameLabel;           // подпись «Системное имя» (для подсветки обязательного)
        private static readonly Color DirtyBg = Color.FromArgb(120, 60, 12); // тёмно-оранжевый — изменённое поле
        // Видимость категорий на карте (только в редакторе): имя категории -> показывать
        private readonly Dictionary<string, bool> _catVisible = new() { ["Цели"] = true, ["Города"] = true, ["Отключенные"] = true };

        public MapEditorForm()
        {
            InitializeComponent();
            LoadCities();
            StartRoadsLoad();
            LoadOverlays();
            LoadTargets();
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
            BuildEditPanel();
            this.KeyPreview = true; // чтобы CTRL+C перехватывался формой даже из полей ввода
            this.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedAsJson();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                }
            };
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
            Controls.Add(_statusBar);   // статусная строка (левый dock — нижняя, под картой)
            Controls.Add(_statusLabel); // legacy-строка скрыта (Visible=false), сохранена для совместимости
            Controls.Add(_toolbar);
            Controls.Add(_sidebar);

            var findTruck = new Button { Text = "найти грузовик", Width = 130, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            findTruck.Click += (s, e) => FindTruck();

            var showAll = new Button { Text = "показать всё", Width = 110, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            showAll.Click += (s, e) => FitToAll();

            var reloadTargets = new Button { Text = "обновить цели", Width = 120, Height = 30, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, FlatStyle = FlatStyle.Flat };
            reloadTargets.Click += (s, e) => { LoadTargets(); PopulateSidebar(); RequestRender(); };

            _onlySelectedChk = new CheckBox { Text = "Только выбранные", AutoSize = true, ForeColor = Color.LightGray, Height = 30, Margin = new Padding(8, 6, 0, 0) };
            _editTip.SetToolTip(_onlySelectedChk, "Показывать на карте только выделенные точки (снимает с карты все остальные цели).");
            _onlySelectedChk.CheckedChanged += (s, e) => RequestRender();

            _toolbar.Controls.Add(findTruck);
            _toolbar.Controls.Add(showAll);
            _toolbar.Controls.Add(reloadTargets);
            _toolbar.Controls.Add(_onlySelectedChk);

            _sidebar.AfterSelect += (s, e) =>
            {
                if (e.Node?.Tag is (double x, double z))
                {
                    string id = e.Node.Name; // для целей/отключенных — gameName; для городов/POI — составной ключ
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (ModifierKeys == Keys.Control) ToggleSelect(id);
                        else SelectPoint(id);
                    }
                    CenterOn(x, z);
                }
            };
            _sidebar.AfterCheck += (s, e) =>
            {
                if (_suppressCheck) return;
                // Родительская категория — переключить видимость на карте.
                if (e.Node != null && e.Node.Nodes.Count > 0)
                {
                    string key = !string.IsNullOrEmpty(e.Node.Name) ? e.Node.Name : e.Node.Text;
                    _catVisible[key] = e.Node.Checked;
                    RequestRender();
                }
                else if (e.Node != null && !string.IsNullOrEmpty(e.Node.Name))
                {
                    // Лист точки — чекбокс выделяет/снимает выделение и вписывает в карту.
                    if (e.Node.Checked) _selectedIds.Add(e.Node.Name);
                    else _selectedIds.Remove(e.Node.Name);
                    UpdateSidebarSelection();
                    if (_selectedIds.Count > 0) FitToSelection();
                    RequestRender();
                }
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
            _statusBar.SetOperation("загрузка точек", busy: true);
            EnsureOverridesInit();
            _pointModel.Clear();
            _staticNames.Clear();
            // 1) статические точки (custom_targets.json приложения)
            try
            {
                if (File.Exists(_targetsFile))
                {
                    var json = JObject.Parse(File.ReadAllText(_targetsFile));
                    var list = json["customTargets"] as JArray;
                    if (list != null)
                        foreach (var t in list)
                        {
                            var pd = PointDataFromJObject(t as JObject);
                            if (pd != null && !string.IsNullOrEmpty(pd.GameName) && !_pointModel.ContainsKey(pd.GameName))
                            {
                                _pointModel[pd.GameName] = pd;
                                _staticNames.Add(pd.GameName);
                            }
                        }
                }
            }
            catch (Exception ex) { Debug.WriteLine("LoadTargets static: " + ex.Message); }
            // 1.5) города и POI — регистрируем в модели как ПОЛНОЦЕННЫЕ редактируемые точки,
            // чтобы overrides накладывались ПОВЕРХ них (сохраняя координаты) и чтобы их можно
            // было выделять/перетаскивать через единый конвейер _targets/_pointModel.
            foreach (var c in _cities)
            {
                var key = string.IsNullOrEmpty(c.id) ? c.name : c.id;
                if (string.IsNullOrEmpty(key) || _pointModel.ContainsKey(key)) continue;
                _pointModel[key] = new PointData
                {
                    GameName = key,
                    RealName = string.IsNullOrEmpty(c.name) ? key : c.name,
                    Category = "Города",
                    Enabled = true,
                    X = c.x, Y = c.y, Z = c.z,
                    SourceFile = "",
                    IsCity = true
                };
            }
            foreach (var p in _pois)
            {
                if (string.IsNullOrEmpty(p.uid) || _pointModel.ContainsKey(p.uid)) continue;
                _pointModel[p.uid] = new PointData
                {
                    GameName = p.uid,
                    RealName = p.category, // метка по умолчанию = категория (напр. "Company")
                    Category = p.category,
                    Enabled = true,
                    X = p.x, Z = p.z,
                    SourceFile = "",
                    IsPoi = true
                };
            }
            // 2) overrides поверх (load_order: снизу вверх — последний файл побеждает)
            LoadLoadOrder();
            ApplyOverridesToModel();
            // 3) перестроить визуальный список
            RebuildTargetsFromModel();
            RebuildSelectLookup();
            _statusBar.SetOperation($"точки обновлены ({_pointModel.Count})", busy: false);
        }

        // Справочник координат ВСЕХ точек (цели/города/POI) для выделения/вписывания в карту.
        private void RebuildSelectLookup()
        {
            _selectLookup.Clear();
            foreach (var t in _targets) _selectLookup[t.id] = (t.x, t.z, t.name);
            foreach (var pd in _pointModel.Values)
                if (!_selectLookup.ContainsKey(pd.GameName)) _selectLookup[pd.GameName] = (pd.X, pd.Z, pd.RealName);
            foreach (var c in _cities) _selectLookup["city:" + c.name] = (c.x, c.z, c.name);
            foreach (var p in _pois) _selectLookup["poi:" + p.uid] = (p.x, p.z, p.uid);
        }

        // --- Система overrides ---
        private void EnsureOverridesInit()
        {
            try
            {
                if (!Directory.Exists(_overridesDir)) Directory.CreateDirectory(_overridesDir);
                // РЕМОНТ load_order (диагностика 17:26): если load_order отсутствует, создаём
                // с custom_map1.json; если существует — ГАРАНТИРУЕМ, что custom_map1.json в нём
                // есть (иначе его записи не читаются конвейером миникарты и редактором).
                if (!File.Exists(_loadOrderFile))
                    File.WriteAllText(_loadOrderFile, "custom_map1.json" + Environment.NewLine);
                else
                {
                    var lines = File.ReadAllLines(_loadOrderFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    if (!lines.Contains("custom_map1.json", StringComparer.OrdinalIgnoreCase))
                    {
                        // Вставляем ПЕРВЫМ (низший приоритет), существующие не двигаем.
                        lines.Insert(0, "custom_map1.json");
                        File.WriteAllLines(_loadOrderFile, lines);
                        LogEditor("[INIT] custom_map1.json добавлен в load_order.txt (был отсутствовал — записи не читались!).");
                    }
                }
                string custom = Path.Combine(_overridesDir, "custom_map1.json");
                bool needCreate = !File.Exists(custom);
                if (!needCreate)
                {
                    // Файл есть, но может быть пустым (0 байт) или повреждённым — такой не читается.
                    try
                    {
                        var txt = File.ReadAllText(custom);
                        if (string.IsNullOrWhiteSpace(txt) || (JObject.Parse(txt)["customTargets"] as JArray == null))
                            needCreate = true;
                    }
                    catch { needCreate = true; }
                }
                if (needCreate)
                {
                    File.WriteAllText(custom, new JObject { ["customTargets"] = new JArray() }.ToString(Formatting.Indented));
                    LogEditor($"[INIT] создан/восстановлен ПУСТОЙ overrides {custom}");
                }
            }
            catch (Exception ex) { Debug.WriteLine("EnsureOverridesInit: " + ex.Message); }
        }

        private void LoadLoadOrder()
        {
            _overrideFiles.Clear();
            try
            {
                if (File.Exists(_loadOrderFile))
                {
                    foreach (var line in File.ReadAllLines(_loadOrderFile))
                    {
                        var f = line.Trim();
                        if (f.Length == 0) continue;
                        if (!_overrideFiles.Contains(f)) _overrideFiles.Add(f);
                    }
                }
                // гарантируем наличие custom_map1.json в списке
                if (!_overrideFiles.Contains("custom_map1.json")) _overrideFiles.Add("custom_map1.json");
            }
            catch { }
            if (string.IsNullOrEmpty(_selectedOverrideFile) || !_overrideFiles.Contains(_selectedOverrideFile))
                _selectedOverrideFile = _overrideFiles.Count > 0 ? _overrideFiles[0] : "custom_map1.json";
        }

        private void SaveLoadOrder()
        {
            try { File.WriteAllText(_loadOrderFile, string.Join(Environment.NewLine, _overrideFiles) + Environment.NewLine); }
            catch (Exception ex) { Debug.WriteLine("SaveLoadOrder: " + ex.Message); }
        }

        private void ApplyOverridesToModel()
        {
            // load_order: индекс 0 — НИЗШИЙ приоритет, последний — ВЫСШИЙ.
            foreach (var f in _overrideFiles)
            {
                string path = Path.Combine(_overridesDir, f);
                if (!File.Exists(path)) continue;
                try
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    var list = json["customTargets"] as JArray;
                    if (list == null) continue;
                    foreach (var t in list)
                    {
                        var jo = t as JObject;
                        if (jo == null) continue;
                        var key = (string?)jo["gameName"];
                        if (string.IsNullOrEmpty(key)) continue;
                        if (_pointModel.TryGetValue(key, out var existing))
                        {
                            // Delta-merge: переопределяем ТОЛЬКО те поля, что есть в overrides.
                            ApplyJObjectToPoint(existing, jo);
                            existing.IsOverride = true;
                            existing.SourceFile = f;
                        }
                        else
                        {
                            var pd = new PointData { GameName = key };
                            ApplyJObjectToPoint(pd, jo);
                            pd.SourceFile = f;
                            pd.IsNew = false;     // добавлена через overrides (пользовательская)
                            _pointModel[key] = pd;
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"ApplyOverrides {f}: " + ex.Message); }
            }
        }

        private void RebuildTargetsFromModel()
        {
            _targets.Clear();
            foreach (var pd in _pointModel.Values)
                _targets.Add((pd.GameName, pd.RealName, pd.X, pd.Z, ParseColor(pd.Color)));
        }

        // Преобразует JObject (запись custom_targets/override) в PointData.
        // Применяет к точке ТОЛЬКО те поля, которые ПРИСУТСТВУЮТ в JObject (delta-merge).
        // Используется и при чтении статических точек, и при наложении overrides поверх статических,
        // и при наложении overrides на миникарту (MainForm.PointsOverrides.cs).
        internal static void ApplyJObjectToPoint(PointData target, JObject t)
        {
            var id = (string?)t["gameName"];
            if (!string.IsNullOrEmpty(id)) target.GameName = id;
            if (t.ContainsKey("realName")) target.RealName = (string?)t["realName"] ?? target.RealName;
            if (t.ContainsKey("category")) target.Category = (string?)t["category"] ?? target.Category;
            if (t.ContainsKey("description")) target.Description = (string?)t["description"] ?? "";
            if (t.ContainsKey("status")) target.Enabled = (string?)t["status"] != "inactive";
            if (t.ContainsKey("enabled")) { if (bool.TryParse((string?)t["enabled"], out var en)) target.Enabled = en; }
            double x = target.X, y = target.Y, z = target.Z;
            var coords = (string?)t["coords"];
            if (!string.IsNullOrEmpty(coords))
            {
                var parts = coords.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out x);
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out y);
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out z);
                }
            }
            else
            {
                if (t.ContainsKey("x")) double.TryParse((string?)t["x"], NumberStyles.Any, CultureInfo.InvariantCulture, out x);
                if (t.ContainsKey("y")) double.TryParse((string?)t["y"], NumberStyles.Any, CultureInfo.InvariantCulture, out y);
                if (t.ContainsKey("z")) double.TryParse((string?)t["z"], NumberStyles.Any, CultureInfo.InvariantCulture, out z);
            }
            target.X = x; target.Y = y; target.Z = z;
            if (t.ContainsKey("color")) target.Color = (string?)t["color"] ?? target.Color;
            if (t.ContainsKey("icon")) target.Icon = (string?)t["icon"] ?? target.Icon;
            if (t.ContainsKey("radius")) target.TriggerRadius = (double?)t["radius"] ?? target.TriggerRadius;
            else if (t.ContainsKey("triggerRadius")) target.TriggerRadius = (double?)t["triggerRadius"] ?? target.TriggerRadius;
            if (t.ContainsKey("cooldown")) target.CooldownMinutes = (int?)t["cooldown"] ?? target.CooldownMinutes;
            else if (t.ContainsKey("cooldownMinutes")) target.CooldownMinutes = (int?)t["cooldownMinutes"] ?? target.CooldownMinutes;
            if (t.ContainsKey("hidden")) target.Hidden = (int?)t["hidden"] ?? target.Hidden;
            if (t.ContainsKey("delete_on_complete")) target.DeleteOnComplete = (int?)t["delete_on_complete"] ?? target.DeleteOnComplete;
            if (t.ContainsKey("dialogId")) target.DialogId = (string?)t["dialogId"] ?? "";
            else if (t.ContainsKey("enterDialog")) target.DialogId = (string?)t["enterDialog"] ?? "";
            if (t.ContainsKey("action")) target.Action = (string?)t["action"] ?? "";
            if (t.ContainsKey("caption")) target.Caption = (string?)t["caption"] ?? "";
            if (t.ContainsKey("enterReward")) target.EnterReward = (int?)t["enterReward"] ?? target.EnterReward;
            else if (t.ContainsKey("enterMoney")) target.EnterReward = (int?)t["enterMoney"] ?? target.EnterReward;
            if (t.ContainsKey("afterReward")) target.AfterReward = (int?)t["afterReward"] ?? target.AfterReward;
            else if (t.ContainsKey("afterMoney")) target.AfterReward = (int?)t["afterMoney"] ?? target.AfterReward;
            if (t.ContainsKey("enterXp")) target.EnterXp = (int?)t["enterXp"] ?? target.EnterXp;
            if (t.ContainsKey("afterXp")) target.AfterXp = (int?)t["afterXp"] ?? target.AfterXp;
            if (t.ContainsKey("isRandom")) target.IsRandom = (bool?)t["isRandom"] ?? target.IsRandom;
            if (t.ContainsKey("questType")) target.QuestType = (string?)t["questType"] ?? "";
            if (t.ContainsKey("cooldown_until"))
            {
                var cu = (string?)t["cooldown_until"];
                if (!string.IsNullOrEmpty(cu) && DateTime.TryParse(cu, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
                    target.CooldownUntil = until;
            }
        }

        private static PointData? PointDataFromJObject(JObject? t)
        {
            if (t == null) return null;
            var id = (string?)t["gameName"];
            if (string.IsNullOrEmpty(id)) return null;
            var pd = new PointData { GameName = id };
            ApplyJObjectToPoint(pd, t);
            return pd;
        }

        // Преобразует PointData в JObject для записи в overrides (полная запись — для новых точек).
        private static JObject PointDataToJObject(PointData pd)
        {
            var o = new JObject
            {
                ["id"] = pd.GameName,
                ["gameName"] = pd.GameName,
                ["realName"] = pd.RealName,
                ["category"] = pd.Category,
                ["description"] = pd.Description,
                ["status"] = pd.Enabled ? "active" : "inactive",
                ["enabled"] = pd.Enabled,
                ["coords"] = $"{pd.X.ToString("F2", CultureInfo.InvariantCulture)}, {pd.Y.ToString("F2", CultureInfo.InvariantCulture)}, {pd.Z.ToString("F2", CultureInfo.InvariantCulture)}",
                ["x"] = pd.X, ["y"] = pd.Y, ["z"] = pd.Z,
                ["color"] = pd.Color,
                ["icon"] = pd.Icon,
                ["radius"] = pd.TriggerRadius,
                ["triggerRadius"] = pd.TriggerRadius,
                ["cooldown"] = pd.CooldownMinutes,
                ["cooldownMinutes"] = pd.CooldownMinutes,
                ["hidden"] = pd.Hidden,
                ["delete_on_complete"] = pd.DeleteOnComplete,
                ["dialogId"] = pd.DialogId,
                ["action"] = pd.Action,
                ["caption"] = pd.Caption,
                ["enterReward"] = pd.EnterReward,
                ["afterReward"] = pd.AfterReward,
                ["enterXp"] = pd.EnterXp,
                ["afterXp"] = pd.AfterXp,
                ["isRandom"] = pd.IsRandom,
                ["questType"] = pd.QuestType
            };
            return o;
        }

        // Сопоставление ключа поля PointData -> имена JSON-свойств (для дельта-записи в overrides).
        private static readonly Dictionary<string, string[]> FieldJson = new()
        {
            ["GameName"] = new[] { "gameName", "id" },
            ["RealName"] = new[] { "realName" },
            ["Category"] = new[] { "category" },
            ["Enabled"] = new[] { "status", "enabled" },
            ["Description"] = new[] { "description" },
            ["X"] = new[] { "coords" },
            ["Y"] = new[] { "coords" },
            ["Z"] = new[] { "coords" },
            ["Color"] = new[] { "color" },
            ["Icon"] = new[] { "icon" },
            ["TriggerRadius"] = new[] { "radius", "triggerRadius" },
            ["CooldownMinutes"] = new[] { "cooldown", "cooldownMinutes" },
            ["Hidden"] = new[] { "hidden" },
            ["DeleteOnComplete"] = new[] { "delete_on_complete" },
            ["DialogId"] = new[] { "dialogId" },
            ["Action"] = new[] { "action" },
            ["Caption"] = new[] { "caption" },
            ["EnterReward"] = new[] { "enterReward" },
            ["AfterReward"] = new[] { "afterReward" },
            ["EnterXp"] = new[] { "enterXp" },
            ["AfterXp"] = new[] { "afterXp" },
        };

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
            BeginInvoke((Action)(() => { if (!_disposed) { _statusLabel.Text = msg; _statusBar.SetOperation(msg, busy: true); } }));
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

            // КОНУС ОБЗОРА (v74: максимум 1.5 км; при малом зуме не выходит за панель —
            // экранный радиус ограничен половиной диагонали панели; масштабируется зумом).
            if (_truckX.HasValue && _truckZ.HasValue)
            {
                bool online = _truckKnown;                     // телеметрия живая?
                double dir = _truckHeading + _headYaw;         // итоговое направление взгляда
                double cx = _truckX.Value, cz = _truckZ.Value;
                // Экранные координаты вершины конуса.
                var p0 = WorldToScreen(cx, cz);
                const double halfAngleDeg = 17.5;
                // v74: длина = min(1.5 км, половина диагонали экрана в метрах).
                double maxPx = Math.Sqrt(_mapPanel.Width * _mapPanel.Width + _mapPanel.Height * _mapPanel.Height) / 2.0;
                double rWorld = Math.Min(1500.0, maxPx * _scale);
                // heading: 0 = север (-Z), растёт против часовой (влево).
                // Экран: ось X вправо, ось Y ВНИЗ → экранное направление взгляда:
                // screenAngle = heading*360 по часовой от вертикали вверх + поворот головы.
                double screenAngleDeg = -(dir * 360.0);        // соответствует RotateTransform стрелки
                double a0 = (screenAngleDeg - halfAngleDeg) * Math.PI / 180.0;
                double a1 = (screenAngleDeg + halfAngleDeg) * Math.PI / 180.0;
                double r = rWorld;
                // Точки дуги в МИРОВЫХ координатах (конус «лежит» на карте, зум работает).
                var pts = new List<PointF> { p0 };
                int steps = 24;
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double a = a0 + (a1 - a0) * t;
                    // Экранное направление (dx, dy) при угле a (радианы, 0 = вверх):
                    double dx = Math.Sin(a);
                    double dy = -Math.Cos(a);
                    // Преобразуем экранное направление обратно в мировое: мир = экран повернуть обратно.
                    // Мировая СК: X-восток ВПРАВО, Z-юг ВНИЗ (на экране +Z вниз).
                    // Экранная стрелка вращается на -heading*360: мир->экран. Обратно:
                    double wx = cx + dx * r;
                    double wz = cz + dy * r;   // прим.: направление уже в СК экрана ⇒ совпадает с миром
                    pts.Add(WorldToScreen(wx, wz));
                }
                // Онлайн — жёлтый полупрозрачный; офлайн — серый, почти бесцветный.
                using var coneBrush = new SolidBrush(online
                    ? Color.FromArgb(38, 255, 210, 90)
                    : Color.FromArgb(20, 150, 150, 150));
                using var conePen = new Pen(online
                    ? Color.FromArgb(120, 255, 210, 90)
                    : Color.FromArgb(60, 170, 170, 170), 1f);
                g.FillPolygon(coneBrush, pts.ToArray());
                g.DrawPolygon(conePen, pts.ToArray());
            }

            // Единый конвейер отрисовки ВСЕХ точек (цели/города/POI) из _targets.
            // Города и POI теперь — полноценные точки в _pointModel/_targets (как и цели),
            // поэтому перетаскиваются и выделяются единообразно. POI скрываем при масштабе > 10 м/px.
            bool onlySel = _onlySelectedChk != null && _onlySelectedChk.Checked && _selectedIds.Count > 0;
            foreach (var t in _targets)
            {
                _pointModel.TryGetValue(t.id, out var pm);
                bool isCity = pm != null && pm.IsCity;
                bool isPoi = pm != null && pm.IsPoi;
                if (isPoi && _scale > 30) continue; // слишком мелко — только шум
                if (onlySel && !_selectedIds.Contains(t.id)) continue;
                bool disabled = pm != null && !pm.Enabled;
                // видимость по категории: отключённые — группой "Отключенные", иначе по Category
                if (disabled)
                {
                    if (!_catVisible.TryGetValue("Отключенные", out var so) || !so) continue;
                }
                else
                {
                    string cat = pm != null ? pm.Category : "Цели";
                    // неизвестная категория (ещё не зарегистрирована в _catVisible) — показываем по умолчанию
                    if (_catVisible.TryGetValue(cat, out var st) && !st) continue;
                }
                var p = WorldToScreen(t.x, t.z);
                if (p.X < -50 || p.Y < -50 || p.X > _mapPanel.Width + 50 || p.Y > _mapPanel.Height + 50) continue;

                bool cooldown = pm != null && pm.CooldownUntil > DateTime.UtcNow;
                bool ovr = pm != null && (pm.IsOverride || pm.IsNew);

                Color fill;
                float rx, ry;
                if (isCity)
                {
                    fill = Color.FromArgb(204, 255, 230, 0);
                    rx = 5.5f; ry = 5.5f;
                }
                else if (isPoi)
                {
                    fill = CategoryColor(pm!.Category);
                    rx = 3.5f; ry = 3.5f;
                }
                else
                {
                    fill = disabled ? Color.FromArgb(120, 120, 120) : t.color;
                    rx = 5; ry = 5;
                }
                using var brush = new SolidBrush(fill);
                g.FillEllipse(brush, p.X - rx, p.Y - ry, rx * 2, ry * 2);
                Color outline = Color.Black;
                float ow = 1.5f;
                if (cooldown) { outline = Color.FromArgb(70, 140, 255); ow = 3f; }
                else if (disabled) { outline = Color.Gray; ow = 3f; }
                else if (ovr) { outline = Color.FromArgb(70, 140, 255); ow = 2f; }
                else if (isCity) { outline = Color.Black; ow = 2f; }
                g.DrawEllipse(new Pen(outline, ow), p.X - rx, p.Y - ry, rx * 2, ry * 2);
                if (_selectedIds.Contains(t.id))
                {
                    Color sel = (_selectedGameName == t.id && _dirtyFields.Count > 0) ? Color.Yellow : Color.White;
                    g.DrawEllipse(new Pen(sel, 2f), p.X - 8, p.Y - 8, 16, 16);
                }
                // Метка: для POI — реальное имя (по умолчанию = категория, напр. "Company"),
                // для города — имя города, иначе — имя цели.
                string label = (isPoi && pm != null) ? (string.IsNullOrEmpty(pm.RealName) ? pm.Category : pm.RealName)
                             : (isCity && pm != null ? pm.RealName : t.name);
                if (cooldown) label += " (кулдаун)";
                DrawLabelAbove(g, label, p.X, p.Y, disabled ? Color.Gray : Color.White, true);
            }

            // Временный маркер создаваемой точки: серая точка с перекрестьем (наглядность создания).
            // v70: если точка пришла ИЗ АР («Пометить в АР»), подпись и позиция от пометки;
            // Y из пометки (высота грузовика) показываем в подписи.
            if (_createMode && _editingCopy != null)
            {
                var p = WorldToScreen(_editingCopy.X, _editingCopy.Z);
                using var cmBrush = new SolidBrush(Color.Gray);
                g.FillEllipse(cmBrush, p.X - 6, p.Y - 6, 12, 12);
                using var cmPen = new Pen(Color.FromArgb(225, 225, 225), 1.5f);
                g.DrawEllipse(cmPen, p.X - 6, p.Y - 6, 12, 12);
                g.DrawLine(cmPen, p.X - 13, p.Y, p.X + 13, p.Y);
                g.DrawLine(cmPen, p.X, p.Y - 13, p.X, p.Y + 13);
                var hint = _createModeFromAr
                    ? $"Новая точка (АР)  Y={_editingCopy.Y:F1}м"
                    : "Новая точка";
                DrawLabelAbove(g, hint, p.X, p.Y, Color.LightGray, true);
            }

            if (_truckX.HasValue && _truckZ.HasValue)
            {
                bool online = _truckKnown;                     // телеметрия живая?
                var p = WorldToScreen(_truckX.Value, _truckZ.Value);
                // Треугольник-стрелка как на миникарте (map_draw.js, дельтоид), вращение
                // по heading фуры. ОТЗЕРКАЛЕНО по вертикали (фидбек 31.08.2026): вращение
                // было инверсным — минус 180 убран. heading=0 → нос К СЕВЕРУ (-Z, вверх).
                // v66b: при потере телеметрии НЕ удаляется — становится серым;
                // последняя позиция всегда остаётся на карте.
                var shape = new PointF[]
                {
                    new PointF(0, -9f),              // нос (вперёд, на экране ВВЕРХ)
                    new PointF(-5f, 7f),             // левое крыло
                    new PointF(0f, 4.5f),            // хвостовая впадина (как на миникарте)
                    new PointF(5f, 7f)               // правое крыло
                };
                var gstate = g.Save();
                g.TranslateTransform(p.X, p.Y);
                g.RotateTransform((float)(-_truckHeading * 360.0));
                using var brush = new SolidBrush(online
                    ? Color.FromArgb(255, 77, 77)    // #ff4d4d
                    : Color.FromArgb(140, 128, 128, 128));   // серый полупрозрачный
                using var outline = new Pen(online ? Color.White : Color.FromArgb(190, 190, 190), 1.5f);
                g.FillPolygon(brush, shape);
                g.DrawPolygon(outline, shape);
                g.Restore(gstate);
                DrawLabelAbove(g, online ? "Грузовик" : "Грузовик (нет данных)", p.X, p.Y,
                    online ? Color.Red : Color.FromArgb(170, 170, 170), true);
            }
        }

        private static void LogEditor(string msg)
        {
            try { Logger.Current?.Info("[EDITOR] " + msg); }
            catch { }
        }

        // ПОДРОБНЫЕ данные (значения, пакеты, кадры) — В app_data.log, НЕ в workflow
        // (правило пользователя: workflow — только шаги, данные — в app_data).
        private static void LogEditorData(string msg)
        {
            try { Logger.Current?.Data("[EDITOR] " + msg); }
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
            else if (e.Button == MouseButtons.Left)
            {
                // Перетаскивание НАЧИНАЕТСЯ ТОЛЬКО если точка УЖЕ выделена (первый клик —
                // только выделение, см. OnMouseClick). Если Ctrl зажат — не тащим (мультивыбор).
                var (wx, wz) = ScreenToWorld(e.X, e.Y);
                var tid = HitTarget(e.X, e.Y);
                if (tid != null && ModifierKeys != Keys.Control && _selectedIds.Contains(tid))
                {
                    _dragId = tid;
                    _dragMoved = false;
                    Cursor = Cursors.SizeAll;
                }
                else
                {
                    _dragId = null; // выделение произойдёт по клику (MouseUp→Click)
                }
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_dragId != null)
            {
                var (wx, wz) = ScreenToWorld(e.X, e.Y);
                if (_pointModel.TryGetValue(_dragId, out var pd))
                {
                    pd.X = wx; pd.Z = wz;
                    _dragMoved = true;
                    RebuildTargetsFromModel();
                    if (_selectedGameName == _dragId)
                    {
                        if (_fieldControls.TryGetValue("X", out var cx)) cx.Text = wx.ToString("F2", CultureInfo.InvariantCulture);
                        if (_fieldControls.TryGetValue("Z", out var cz)) cz.Text = wz.ToString("F2", CultureInfo.InvariantCulture);
                        _dirtyFields.Add("X"); _dirtyFields.Add("Z");
                        UpdateActionButtons();
                    }
                    RequestRender();
                }
                return;
            }

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
            // Города и POI теперь в _targets — единый перебор со всеми точками.
            foreach (var t in _targets)
            {
                var p = WorldToScreen(t.x, t.z);
                var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
                if (d < best) { best = d; found = t.id; }
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
            else if (e.Button == MouseButtons.Left && _dragId != null)
            {
                // Завершили перетаскивание. Точка уже выбрана и помечена грязной (X/Z) — готова к сохранению.
                _dragId = null;
                Cursor = Cursors.Default;
            }
        }

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Если только что было перетаскивание — не обрабатываем клик (не создаём точку).
            if (_dragMoved) { _dragMoved = false; return; }

            // КЛИК ПО ГРУЗОВИКУ (v72, требование): копируем координаты с высотой,
            // тултип-статус; точку НЕ создаём, выделение НЕ сбрасываем.
            if (_truckX.HasValue && _truckZ.HasValue)
            {
                var tp = WorldToScreen(_truckX.Value, _truckZ.Value);
                if (Math.Abs(tp.X - e.X) <= ClickThresholdPx && Math.Abs(tp.Y - e.Y) <= ClickThresholdPx)
                {
                    double ty = _truckY.HasValue ? _truckY.Value : 0;
                    var coord = $"{_truckX.Value.ToString("F2", CultureInfo.InvariantCulture)}, {ty.ToString("F2", CultureInfo.InvariantCulture)}, {_truckZ.Value.ToString("F2", CultureInfo.InvariantCulture)}";
                    Clipboard.SetText(coord);
                    ShowCopied($"Грузовик:  {coord}   (скопировано в буфер)");
                    return;
                }
            }

            var (wx, wz) = ScreenToWorld(e.X, e.Y);

            // Любая точка под курсором (цель/город/POI) — выделяем (Ctrl — мультивыбор)
            // и ставим чекбокс в сайдбаре. Логика «пустое место» в этот момент ОТКЛЮЧЕНА.
            // Города и POI теперь тоже в _targets, поэтому HitTarget их ловит единообразно.
            var tid = HitTarget(e.X, e.Y);
            if (tid != null)
            {
                if (ModifierKeys == Keys.Control) ToggleSelect(tid);
                else SelectPoint(tid);
                // В буфер — координаты точки (для городов/POI/целей единообразно).
                if (_pointModel.TryGetValue(tid, out var pd))
                {
                    var coord = $"{pd.X.ToString("F2", CultureInfo.InvariantCulture)}, {pd.Y.ToString("F2", CultureInfo.InvariantCulture)}, {pd.Z.ToString("F2", CultureInfo.InvariantCulture)}";
                    Clipboard.SetText(coord);
                    ShowCopied($"Выбрана точка: {tid} [{pd.RealName}]  {coord}");
                }
                else
                {
                    Clipboard.SetText(tid);
                    ShowCopied($"Выбрана точка: {tid}");
                }
                return;
            }

            // По-настоящему пустое место — режим создания новой точки.
            EnterCreateMode(wx, wz);
            // v73: журнал выбора новой точки (Logs\new_object_po_selections.txt).
            try { MainForm.LogNewPointSelection(wx, 0, wz); } catch { }
            var anyCoord = $"{wx.ToString("F2", CultureInfo.InvariantCulture)}, 0, {wz.ToString("F2", CultureInfo.InvariantCulture)}";
            Clipboard.SetText(anyCoord);
            ShowCopied($"Создание точки. Координаты: {anyCoord}");
        }

        private DateTime _lastCopied;
        private string _baseStatus = "";
        private void ShowCopied(string msg)
        {
            _lastCopied = DateTime.Now;
            _statusLabel.Text = msg;
            _statusBar.SetOperation(msg, busy: false);
        }

        private void SetTruckStatus()
        {
            if (_disposed) return;
            // Индикатор «данные транспорта»: lime при наличии данных, тусклый при их отсутствии.
            _statusBar.SetSystemState("данные транспорта", _truckKnown);
            _baseStatus = (_truckKnown ? "● Координаты грузовика онлайн" : "● Нет данных от грузовика")
                        + $"   Центр: {_centerX:F0}, {_centerZ:F0}  Масштаб: {_scale:F1} м/px";
            _statusLabel.ForeColor = _truckKnown ? Color.FromArgb(70, 200, 90) : Color.FromArgb(230, 90, 90);
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
                        // ФИКС v64: Value<double>() напрямую — ToString() в ru-RU даёт «,»
                        // (старый корень «парадокса ×100» на других машинах: Invariant TryParse
                        // читал запятую как разделитель тысяч).
                        if (!double.IsFinite(xv.Value<double>()) || !double.IsFinite(zv.Value<double>())) continue;
                        double x = xv.Value<double>(), z = zv.Value<double>();
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
            foreach (var p in _pois) if (!_catVisible.ContainsKey(p.category)) _catVisible[p.category] = true;
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
            foreach (var p in _pois) if (!_catVisible.ContainsKey(p.category)) _catVisible[p.category] = true;
            TreeNode? selNode = null;
            var tNode = new TreeNode("Цели (" + _targets.Count + ")") { Name = "Цели", Checked = _catVisible.TryGetValue("Цели", out var st) && st };
            foreach (var t in _targets)
            {
                // Города/POI вынесены в свои группы; пользовательские — в «Пользовательское».
                if (_pointModel.TryGetValue(t.id, out var tp) && (tp.IsCity || tp.IsPoi)) continue;
                if (tp != null && tp.SourceFile != "" && !_staticNames.Contains(t.id)) continue;
                var n = new TreeNode(t.name) { Tag = (t.x, t.z), Name = t.id };
                n.Checked = _selectedIds.Contains(t.id);
                if (_selectedIds.Contains(t.id)) { n.BackColor = Color.FromArgb(60, 70, 90); if (t.id == _selectedGameName) selNode = n; }
                tNode.Nodes.Add(n);
            }
            tNode.Expand();
            _sidebar.Nodes.Add(tNode);

            var cNode = new TreeNode("Города (" + _cities.Count + ")") { Name = "Города", Checked = _catVisible.TryGetValue("Города", out var sc) && sc };
            foreach (var pd in _pointModel.Values.Where(p => p.IsCity))
            {
                var n = new TreeNode(pd.RealName) { Tag = (pd.X, pd.Z), Name = pd.GameName };
                n.Checked = _selectedIds.Contains(pd.GameName);
                if (n.Checked) { n.BackColor = Color.FromArgb(60, 70, 90); if (pd.GameName == _selectedGameName) selNode = n; }
                cNode.Nodes.Add(n);
            }
            cNode.Expand();
            _sidebar.Nodes.Add(cNode);

            foreach (var grp in _pointModel.Values.Where(p => p.IsPoi).GroupBy(p => p.Category).OrderBy(g => g.Key))
            {
                var catNode = new TreeNode(grp.Key + " (" + grp.Count() + ")") { Name = grp.Key, Checked = _catVisible.TryGetValue(grp.Key, out var sp) && sp };
                foreach (var pd in grp)
                {
                    var n = new TreeNode(string.IsNullOrEmpty(pd.RealName) ? pd.GameName : pd.RealName) { Tag = (pd.X, pd.Z), Name = pd.GameName };
                    n.Checked = _selectedIds.Contains(pd.GameName);
                    if (n.Checked) n.BackColor = Color.FromArgb(60, 70, 90);
                    catNode.Nodes.Add(n);
                }
                _sidebar.Nodes.Add(catNode);
            }

            // Группа «Пользовательское» — точки, созданные пользователем (только в overrides, не в статике).
            var userPts = _pointModel.Values.Where(pd => pd.SourceFile != "" && !_staticNames.Contains(pd.GameName) && !pd.IsCity && !pd.IsPoi).ToList();
            if (userPts.Count > 0)
            {

                

                var uNode = new TreeNode("Пользовательское (" + userPts.Count + ")") { Name = "Пользовательское", Checked = _catVisible.TryGetValue("Пользовательское", out var su) && su };
                foreach (var pd in userPts)
                {

                    LogEditor($"USER POINTS: pd.SourceFile " + pd.SourceFile + " pd.GameName " + pd.GameName + " pd.IsPoi"+ pd.IsPoi);

                    var n = new TreeNode(pd.RealName + " [" + pd.GameName + "]") { Tag = (pd.X, pd.Z), Name = pd.GameName };
                    n.Checked = _selectedIds.Contains(pd.GameName);
                    if (_selectedIds.Contains(pd.GameName)) { n.BackColor = Color.FromArgb(60, 70, 90); if (pd.GameName == _selectedGameName) selNode = n; }
                    uNode.Nodes.Add(n);
                }
                _sidebar.Nodes.Add(uNode);
            }

            // Группа «Отключенные» — точки со статусом Отключена.
            var disabled = _pointModel.Values.Where(pd => !pd.Enabled).ToList();
            if (disabled.Count > 0)
            {
                var dNode = new TreeNode("Отключенные (" + disabled.Count + ")") { Name = "Отключенные", Checked = _catVisible.TryGetValue("Отключенные", out var sd) && sd };
                foreach (var pd in disabled)
                {
                    var n = new TreeNode(pd.RealName + " [" + pd.GameName + "]") { Tag = (pd.X, pd.Z), Name = pd.GameName };
                    n.Checked = _selectedIds.Contains(pd.GameName);
                    if (_selectedIds.Contains(pd.GameName)) { n.BackColor = Color.FromArgb(60, 70, 90); if (pd.GameName == _selectedGameName) selNode = n; }
                    dNode.Nodes.Add(n);
                }
                _sidebar.Nodes.Add(dNode);
            }

            if (selNode != null) _sidebar.SelectedNode = selNode;
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
            // REST-снимок TruckTel (каждую 1с): работает И на паузе (подтверждено 31.08.2026
            // для placement при движении; на паузе placement отсутствует в обоих каналах).
            // WS-дельта остаётся «горячим» потоком; REST страхует, если WS отвалится.
            StartEditorRestSnapshot();
        }

        private System.Threading.CancellationTokenSource? _editorRestCts;
        private Task? _editorRestTask;
        private static readonly System.Net.Http.HttpClient _editorHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
        // Кэш порта REST-снимка: НЕ дёргаем GetCandidatePorts каждую секунду
        // (иначе спамим workflow-лог). Перечитываем web_data.json раз в 10с.
        private int _editorRestPort = 8080;
        private DateTime _editorRestPortAt = DateTime.MinValue;

        private void StartEditorRestSnapshot()
        {
            if (_editorRestTask != null && !_editorRestTask.IsCompleted) return;
            _editorRestCts = new System.Threading.CancellationTokenSource();
            var token = _editorRestCts.Token;
            _editorRestTask = Task.Run(async () =>
            {
                LogEditor($"[REST] снимок телеметрии запущен (порт {_editorRestPort} из web_data.json).");
                while (!_disposed && !token.IsCancellationRequested)
                {
                    try
                    {
                        // Порт обновляем не чаще 1 раза в 10с (перечитывая web_data.json молча).
                        if ((DateTime.Now - _editorRestPortAt).TotalSeconds > 10)
                        {
                            _editorRestPortAt = DateTime.Now;
                            var ports = GetCandidatePorts();
                            if (ports.Count > 0 && ports[0] != _editorRestPort)
                            {
                                _editorRestPort = ports[0];
                                LogEditor($"[REST] порт TruckTel сменился на {_editorRestPort}.");
                            }
                        }
                        var resp = await _editorHttp.GetAsync($"http://localhost:{_editorRestPort}/api/rest/flat/truck", token);
                        resp.EnsureSuccessStatusCode();
                        var text = await resp.Content.ReadAsStringAsync(token);
                        ProcessTelemetry(text, _editorRestPort); // тот же парсер placement
                    }
                    catch { /* TruckTel недоступен — ждём следующий тик */ }
                    try { await Task.Delay(1000, token); } catch { break; }
                }
                LogEditor("[REST] снимок телеметрии остановлен.");
            }, token);
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
            // v73: это высокочастотный диагностический вывод (раз/10с) → app_data.
            LogEditorData($"GetCandidatePorts: web_data.json={_webDataFile} существует={File.Exists(_webDataFile)}; кандидаты=[" + string.Join(",", ports) + "].");
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
                // Поворот головы: truck.head.offset[3] (доля оборота; Value<double> — культуры-независимо).
                // Конус обзора рисуется по heading фуры + yaw головы.
                var headArr = json["truck.head.offset"] as JArray;
                if (headArr == null) headArr = json.SelectToken("truck.head.offset") as JArray;
                if (headArr == null)
                {
                    var truck = json["truck"] as JObject;
                    var ho = truck?["head"]?["offset"] as JArray;
                    if (ho != null) headArr = ho;
                }
                if (headArr != null && headArr.Count >= 4 && headArr[3] != null)
                {
                    double hy = headArr[3].Value<double>();
                    if (double.IsFinite(hy)) _headYaw = hy;
                }
                // АНТИ-СПАМ (урок логов 30.08.2026): при паузе TruckTel шлёт только
                // frame.render_time/simulation_time — «placement отсутствует» приходило
                // ~15 раз/сек (12k+ строк в логе за день). Логируем только СМЕНУ состояния:
                        if (placement != null && placement.Count >= 3)
                        {
                            // Смена состояния «placement вернулся» — одна строка, не спам.
                            if (_teleHadPlacement != true)
                            {
                                _teleHadPlacement = true;
                                LogEditor("EnsureTelemetry: placement получен (поток телеметрии восстановлен).");
                            }
                            var xTok = placement[0];
                            var zTok = placement[2];
                            // heading — 4-й элемент placement (доля оборота 0..1).
                            // КЛЮЧЕВОЙ ФИКС (v64): JValue.ToString() в ru-RU даёт «0,35»
                            // (запятая), Invariant TryParse читает «,» как разделитель тысяч
                            // → числа ×1e8 мусор («произвольное вращение», «вне границ»).
                            // Читаем числа НАПРЯМУЮ через Value<double>() — культуро-независимо.
                            double th = placement.Count >= 4 && placement[3] != null
                                ? placement[3].Value<double>() : 0.0;
                            bool haveHeading = placement.Count >= 4 && placement[3] != null &&
                                th >= -0.5 && th <= 1.5;
                            if (xTok != null && zTok != null)
                            {
                                // Числа читаем напрямую (Value<double>): без текстового раунд-трипа
                                // и без зависимости от культуры (корень бага «вне границ карты»).
                                double tx = xTok.Value<double>();
                                double tz = zTok.Value<double>();
                                if (double.IsNaN(tx) || double.IsInfinity(tx) || double.IsInfinity(tz))
                                {
                                    // Мусорный кадр (NaN/Inf) — подробности в app_data, не в workflow.
                                    LogEditorData($"[TELEMETRY][DROP] кадр с NaN/Inf координатами пропущен.");
                                    return;
                                }
                                // Координаты применяем НЕ чаще 1 раза в секунду и только если
                                // сэмпл правдоподобен (в границах карты и без «прыжка» >5 км) —
                                // иначе мусорные кадры телеметрии уносят фуру за много км.
                                _truckLastSeen = DateTime.Now;
                                // МАСШТАБ ТЕЛЕМЕТРИИ (эмпирика 31.08.2026, session 41):
                                // TruckTel отдаёт placement УЖЕ В МЕТРАХ КАРТЫ
                                // (X=122629.27, Z=-54727.70 — напрямую совпадает с дорогами).
                                // Делители 1e11 НЕ НУЖНЫ. Ограничение границ остаётся (мусор отсекается),
                                // но масштаб единый с оверлеями — фура встанет на своё место.
                                // (Предыдущее «×100 при парсинге» = следствие мусорных кадров, не реальный формат.)
                                double ax = tx;           // было: tx / TruckCoordScaleX
                                double az = tz;           // было: tz / TruckCoordScaleZ
                                bool inBounds = ax >= TruckBoundsMinX && ax <= TruckBoundsMaxX
                                                && az >= TruckBoundsMinZ && az <= TruckBoundsMaxZ;
                                bool sane = inBounds && (!_truckKnown || !_truckX.HasValue || !_truckZ.HasValue
                                    || Math.Sqrt((ax - _truckX.Value) * (ax - _truckX.Value) + (az - _truckZ.Value) * (az - _truckZ.Value)) <= TruckSanityMaxJumpM);
                                // heading применяется ТОЛЬКО вместе с правдоподобным сэмплом координат,
                                // причём сглаженно (купон против резких рывков ~0.6/кадр):
                                if (sane && haveHeading)
                                {
                                    double prev = _truckHeading;
                                    double diff = th - prev;
                                    // нормализуем разницу в диапазон [-0.5, 0.5] (переход через 0/1)
                                    if (diff > 0.5) diff -= 1.0;
                                    if (diff < -0.5) diff += 1.0;
                                    double maxStep = 0.6; // 216°/кадр — заведомо больше реального поворота
                                    if (Math.Abs(diff) <= maxStep || !_truckKnown)
                                    {
                                        double applied = prev + diff;
                                        // ре-нормализация в [0..1]
                                        applied -= Math.Floor(applied);
                                        _truckHeading = applied;
                                    }
                                }
                                if (sane) { _candTx = ax; _candTz = az; _haveCandidate = true; }
                                else if (!_haveCandidate && !_truckKnown)
                                {
                                    // ПОДРОБНАЯ диагностика отброса — ТОЛЬКО в app_data.log (не спамим workflow).
                                    // Периодичность: 1 строка/с максимум (по границе применения).
                                    if ((DateTime.Now - _lastTruckCoordApply).TotalMilliseconds >= 1000)
                                    {
                                        _lastTruckCoordApply = DateTime.Now;
                                        LogEditorData($"[TELEMETRY][DROP] сэмпл вне границ/мусор: raw x={tx}, z={tz}" +
                                            (haveHeading ? $", h={th}" : "") + " (в workflow — только смена состояния).");
                                    }
                                }

                                var now = DateTime.Now;
                                var tyTok = placement.Count >= 2 ? placement[1] : null;
                                if (tyTok != null)
                                {
                                    double tvy = tyTok.Value<double>();
                                    if (double.IsFinite(tvy)) _truckY = tvy;   // v72
                                }
                                if ((now - _lastTruckCoordApply).TotalMilliseconds >= 1000)
                                {
                                    _lastTruckCoordApply = now;
                                    if (!_truckKnown)
                                    {
                                        if (inBounds)
                                        {
                                            _truckX = ax; _truckZ = az; _truckKnown = true;
                                            LogEditor($"[TELEMETRY] первый валидный сэмпл применён: X={ax:F1} Z={az:F1}.");
                                            if (!_disposed) BeginInvoke((Action)(() => { if (!_disposed) { SetTruckStatus(); InvalidateMap(); } }));
                                        }
                                        else
                                        {
                                            // Подробности отброса уже ушли в app_data (см. DROP выше).
                                            // В workflow — только 1 строка/с (НЕ на каждый кадр).
                                        }
                                    }
                                    else if (_haveCandidate)
                                    {
                                        double cax = _candTx;   // метры (масштаб снят, см. эмпирику 31.08.2026)
                                        double caz = _candTz;   // метры
                                        _truckX = cax; _truckZ = caz;
                                        if (!_disposed) BeginInvoke((Action)(() => { if (!_disposed) { SetTruckStatus(); InvalidateMap(); } }));
                                    }
                                    else
                                    {
                                        // Все сэмплы за секунду мусор — фура НЕ трогаем (не улетаем).
                                        // Подробности (с какими координатами) — в app_data, см. DROP.
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
                    // Логируем ТОЛЬКО переход (был placement -> исчез): при паузе это
                    // происходит на КАЖДОМ кадре, спамить нельзя.
                    if (_teleHadPlacement != false)
                    {
                        _teleHadPlacement = false;
                        LogEditor("EnsureTelemetry: placement отсутствует в кадрах телеметрии (пауза/нет данных) — дальнейшие кадры без placement не логируются.");
                    }
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

        // ============================================================
        // ПАНЕЛЬ РЕДАКТИРОВАНИЯ ТОЧКИ + РАБОТА С OVERRIDES
        // ============================================================
        private void BuildEditPanel()
        {
            // Верхняя панель overrides (30px): Папка + выбор файла по load_order + реордер.
            _topPanel = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(20, 25, 35) };
            var btnFolder = new Button { Text = "Папка", Width = 60, Height = 22, Left = 6, Top = 4, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray };
            btnFolder.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(_overridesDir) { UseShellExecute = true }); } catch { } };
            _editTip.SetToolTip(btnFolder, "Открыть папку map_overrides в проводнике.");
            var lblOv = new Label { Text = "overrides:", AutoSize = true, ForeColor = Color.LightGray, Left = 72, Top = 7 };
            _overrideCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Left = 138, Top = 4, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray };
            _overrideCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_overrideCombo.SelectedItem is string f) { _selectedOverrideFile = f.StartsWith("*") ? f.Substring(1) : f; LogEditor($"Выбран файл overrides: {_selectedOverrideFile}"); }
            };
            _editTip.SetToolTip(_overrideCombo, "Файл overrides, в который будут записаны изменения (по load_order: сверху — приоритет). * — файл не в load_order.");
            var txtOrder = new TextBox { Width = 30, Left = 344, Top = 4, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, Text = "0" };
            var btnUp = new Button { Text = "↑", Width = 28, Height = 22, Left = 380, Top = 4, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray };
            btnUp.Click += (s, e) =>
            {
                if (int.TryParse(txtOrder.Text, out var idx)) { ReorderOverride(_selectedOverrideFile, idx); RefreshOverrideCombo(); }
            };
            _editTip.SetToolTip(btnUp, "Переместить файл в load_order на позицию (1=высший приоритет). 0 — удалить из load_order.");
            _topPanel.Controls.AddRange(new Control[] { btnFolder, lblOv, _overrideCombo, txtOrder, btnUp });

            // Правая панель редактирования.
            _editPanel = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Color.FromArgb(22, 27, 38), Padding = new Padding(6) };
            var hdr = new Label { Text = "Панель редактирования точки", Dock = DockStyle.Top, Height = 22, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            _editFields = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2) };
            _editFields.HorizontalScroll.Enabled = false; // горизонтальной прокрутки не бывает
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _btnSavePoint = new Button { Text = "сохранить", Width = 90, Height = 26, BackColor = Color.FromArgb(40, 90, 60), ForeColor = Color.White, Visible = false };
            _btnCancelPoint = new Button { Text = "отменить", Width = 90, Height = 26, BackColor = Color.FromArgb(90, 60, 40), ForeColor = Color.White, Visible = false };
            _btnDeletePoint = new Button { Text = "Удалить", Width = 90, Height = 26, BackColor = Color.FromArgb(90, 40, 40), ForeColor = Color.White, Visible = false };
            _btnAddPoint = new Button { Text = "Добавить точку", Width = 110, Height = 26, BackColor = Color.FromArgb(40, 60, 90), ForeColor = Color.White, Visible = false };
            _btnSavePoint.Click += (s, e) => SaveCurrentPoint();
            _btnCancelPoint.Click += (s, e) => CancelChanges();
            _btnDeletePoint.Click += (s, e) => DeleteCurrentPoint();
            _btnAddPoint.Click += (s, e) => CommitNewPoint();
            _editTip.SetToolTip(_btnSavePoint, "Сохранить изменения выбранной точки в файл overrides (custom_map1.json по умолчанию).");
            _editTip.SetToolTip(_btnCancelPoint, "Отменить ВСЕ изменения точки (вернуть к исходному состоянию до редактирования).");
            _editTip.SetToolTip(_btnDeletePoint, "Удалить точку из файла overrides / снять сделанные изменения со статической точки.");
            _editTip.SetToolTip(_btnAddPoint, "Создать новую точку в выбранном файле overrides с координатами из клика по карте.");
            btnRow.Controls.AddRange(new Control[] { _btnSavePoint, _btnCancelPoint, _btnDeletePoint, _btnAddPoint });
            _editPanel.Controls.AddRange(new Control[] { hdr, _editFields, btnRow });

            Controls.Add(_topPanel);
            Controls.Add(_editPanel);
            RefreshOverrideCombo();
            BuildFieldControls();
        }

        private static Color LightGray() => Color.LightGray;

        // Генерирует контролы полей по метаданным PointData.Fields (порядок/группы сохранены).
        private void BuildFieldControls()
        {
            _editFields.Controls.Clear();
            _fieldControls.Clear();
            _gameNameError = null;
            string lastGroup = "";
            foreach (var f in PointData.Fields)
            {
                if (f.Mode == PointFieldMode.Hidden) continue;
                if (f.Group != lastGroup)
                {
                    lastGroup = f.Group;
                    var gl = new Label { Text = "— " + f.Group + " —", AutoSize = true, ForeColor = Color.FromArgb(150, 170, 200), Margin = new Padding(0, 4, 0, 2) };
                    _editFields.Controls.Add(gl);
                }
                bool isDesc = f.Key == "Description";
                // Строка подгоняет высоту под содержимое (AutoSize) — поля НЕ обрезаются.
                var row = new Panel { Width = 320, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 3) };
                var lbl = new Label
                {
                    Text = f.Label + (f.Required ? " *" : ""),
                    Left = 0, Top = 0, AutoSize = true, MaximumSize = new Size(312, 0),
                    ForeColor = f.Mode == PointFieldMode.ReadOnly ? Color.Gray : Color.LightGray,
                    Font = new Font("Segoe UI", 8.5f, f.Required ? (FontStyle.Bold | FontStyle.Underline) : FontStyle.Regular)
                };
                Control ctrl;
                if (f.ValueType == typeof(bool))
                    ctrl = new CheckBox { Left = 0, Top = 0, Width = 266, AutoSize = true, ForeColor = Color.LightGray, Enabled = f.Mode == PointFieldMode.Editable };
                else if (isDesc)
                    ctrl = new TextBox { Left = 0, Top = 0, Width = 312, Height = 95, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, ReadOnly = f.Mode == PointFieldMode.ReadOnly };
                else if (f.Key == "Category")
                {
                    var cb = new ComboBox { Left = 0, Top = 0, Width = 266, DropDownStyle = ComboBoxStyle.DropDown, BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray };
                    foreach (var cat in DistinctCategories()) cb.Items.Add(cat);
                    if (!cb.Items.Contains("Пользовательское")) cb.Items.Add("Пользовательское");
                    ctrl = cb;
                }
                else
                {
                    int tbH = TextRenderer.MeasureText("Пример", new Font("Segoe UI", 8.5f)).Height + 8;
                    ctrl = new TextBox { Left = 0, Top = 0, Width = 266, Height = Math.Max(24, tbH), BackColor = Color.FromArgb(40, 48, 62), ForeColor = Color.LightGray, ReadOnly = f.Mode == PointFieldMode.ReadOnly };
                }
                ctrl.Tag = f.Key;
                if (f.ValueType != typeof(bool))
                    ctrl.TextChanged += (s, e) => OnFieldChanged(f.Key);
                else
                    ((CheckBox)ctrl).CheckedChanged += (s, e) => OnFieldChanged(f.Key);

                // Метка AutoSize — высота измерена; контрол ставим СТРОГО под ней (без обрезки сверху).
                int top = lbl.GetPreferredSize(new Size(312, 0)).Height + 3;
                ctrl.Top = top;
                row.Controls.Add(lbl); row.Controls.Add(ctrl);
                if (f.Key == "GameName")
                {
                    _gameNameLabel = lbl;
                    ctrl.LostFocus += (s, e2) => CheckGameNameUnique();
                    _gameNameError = new Label { Text = "", Left = 0, Top = top + ctrl.Height + 2, Width = 312, AutoSize = true, ForeColor = Color.FromArgb(230, 90, 90), Font = new Font("Segoe UI", 8f), Visible = false };
                    row.Controls.Add(_gameNameError);
                }
                if (f.Mode != PointFieldMode.ReadOnly)
                {
                    var ub = new Button { Text = "отмена", Width = 44, Height = 24, Left = 272, Top = top - 1, Font = new Font("Segoe UI", 7.5f), BackColor = Color.FromArgb(60, 50, 40), ForeColor = Color.LightGray };
                    ub.Click += (s, e2) => ResetField(f.Key);
                    _editTip.SetToolTip(ub, "Отменить изменение ТОЛЬКО этого поля (вернуть исходное значение).");
                    row.Controls.Add(ub);
                }
                _editFields.Controls.Add(row);
                _fieldControls[f.Key] = ctrl;
            }
        }

        // Сброс ОДНОГО поля к значению из копии (кнопка «отмена» у поля).
        private void ResetField(string key)
        {
            if (_editingCopy == null) return;
            var f = PointData.Fields.FirstOrDefault(x => x.Key == key);
            if (f == null || !_fieldControls.TryGetValue(key, out var ctrl)) return;
            var fld = typeof(PointData).GetField(key);
            var v = fld?.GetValue(_editingCopy);
            _loadingPanel = true; // программный сброс: OnFieldChanged не должен санитизировать/грязнить поле
            try
            {
                if (f.ValueType == typeof(bool))
                    ((CheckBox)ctrl).Checked = v is bool b && b;
                else if (key == "Category")
                {
                    var cb = (ComboBox)ctrl;
                    var s = v?.ToString() ?? "";
                    if (!cb.Items.Contains(s)) cb.Items.Add(s);
                    cb.SelectedItem = s;
                }
                else
                    ctrl.Text = v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                        
                        

            }
            finally { _loadingPanel = false; }
            if (ctrl is TextBox tbx) tbx.BackColor = Color.FromArgb(40, 48, 62);
            if (key == "GameName") CheckGameNameUnique();
            _dirtyFields.Remove(key);
            UpdateActionButtons();
            RequestRender();
        }

        private IEnumerable<string> DistinctCategories()
        {
            var set = new HashSet<string>();
            foreach (var pd in _pointModel.Values) if (!string.IsNullOrEmpty(pd.Category)) set.Add(pd.Category);
            return set.OrderBy(x => x);
        }

        private void RefreshOverrideCombo()
        {
            _overrideCombo.Items.Clear();
            for (int i = _overrideFiles.Count - 1; i >= 0; i--) // первый сверху = высший приоритет
                _overrideCombo.Items.Add(_overrideFiles[i]);
            if (Directory.Exists(_overridesDir))
                foreach (var f in Directory.GetFiles(_overridesDir, "*.json"))
                {
                    var name = Path.GetFileName(f);
                    if (!_overrideFiles.Contains(name)) _overrideCombo.Items.Add("*" + name);
                }
            if (_overrideCombo.Items.Contains(_selectedOverrideFile)) _overrideCombo.SelectedItem = _selectedOverrideFile;
            else if (_overrideCombo.Items.Count > 0) _overrideCombo.SelectedIndex = 0;
        }

        // Перемещает файл в позицию idx в load_order (0 = удалить из списка приоритета).
        private void ReorderOverride(string file, int idx)
        {
            if (string.IsNullOrEmpty(file)) return;
            _overrideFiles.Remove(file);
            if (idx <= 0) { SaveLoadOrder(); LogEditor($"overrides {file} удалён из load_order."); return; }
            idx = Math.Min(idx, _overrideFiles.Count + 1);
            _overrideFiles.Insert(idx - 1, file);
            SaveLoadOrder();
            LogEditor($"overrides {file} перемещён на позицию {idx} в load_order.");
        }

        private void SelectPoint(string id) => SelectKey(id, fit: false);
        private void ToggleSelect(string id) => ToggleKey(id, fit: true);

        // Одиночный выбор (первый клик по карте/сайдбару): без вписывания в карту.
        private void SelectKey(string key, bool fit)
        {
            // Повторный клик по уже выбранной (единственной) точке — не сбрасываем правки
            // (перетаскивание обрабатывается отдельно, см. OnMouseDown/OnMouseMove).
            if (_selectedIds.Count == 1 && _selectedIds.Contains(key) && _pointModel.ContainsKey(key))
            {
                UpdateSidebarSelection();
                if (fit) FitToSelection();
                RequestRender();
                return;
            }
            _selectedIds.Clear();
            _selectedIds.Add(key);
            ApplySelectionToPanel(key);
            UpdateSidebarSelection();
            if (fit && _selectedIds.Count > 0) FitToSelection();
            RequestRender();
        }

        // Переключение (Ctrl+Клик / чекбокс в сайдбаре): выделение может быть множественным.
        private void ToggleKey(string key, bool fit)
        {
            if (_selectedIds.Contains(key)) _selectedIds.Remove(key);
            else _selectedIds.Add(key);
            string? editable = _selectedIds.FirstOrDefault(k => _pointModel.ContainsKey(k));
            if (editable != null) ApplySelectionToPanel(editable);
            else { _selectedGameName = null; _editingCopy = null; _dirtyFields.Clear(); LoadPointIntoPanel(new PointData()); }
            UpdateSidebarSelection();
            if (_selectedIds.Count > 0 && (fit || _selectedIds.Count > 1)) FitToSelection();
            RequestRender();
        }

        // Загружает точку в панель, только если она редактируемая (есть в _pointModel).
        private void ApplySelectionToPanel(string key)
        {
            if (_pointModel.TryGetValue(key, out var pd))
            {
                _selectedGameName = key;
                _editingCopy = pd.Clone();
                _dirtyFields.Clear();
                _createMode = false;
                LoadPointIntoPanel(pd);
                return;
            }
            // Города: системное имя = id (gameName), отображаемое = name.
            if (key.StartsWith("city:"))
            {
                var cn = key.Substring(5);
                var city = _cities.FirstOrDefault(c => c.name == cn || c.id == cn);
                if (city.name != null)
                {
                    var sys = string.IsNullOrEmpty(city.id) ? city.name : city.id;
                    var p = new PointData
                    {
                        GameName = sys,
                        RealName = string.IsNullOrEmpty(city.name) ? sys : city.name,
                        Category = "Город",
                        Enabled = true,
                        X = city.x, Y = city.y, Z = city.z,
                        SourceFile = ""
                    };
                    RegisterEditablePoint(p, key);
                }
                return;
            }
            // POI из оверлеев: системное имя = uid, отдельного отображаемого имени нет.
            if (key.StartsWith("poi:"))
            {
                var uid = key.Substring(4);
                var poi = _pois.FirstOrDefault(p => p.uid == uid);
                if (poi.uid != null)
                {
                    var p = new PointData
                    {
                        GameName = uid,
                        RealName = uid,
                        Category = string.IsNullOrEmpty(poi.category) ? "POI" : poi.category,
                        Enabled = true,
                        X = poi.x, Z = poi.z,
                        SourceFile = ""
                    };
                    RegisterEditablePoint(p, key);
                }
                return;
            }
        }

        // Регистрирует точку (город/POI) в модели под системным именем и загружает в панель.
        private void RegisterEditablePoint(PointData p, string sidebarKey)
        {
            _pointModel[p.GameName] = p; // ключ — системное имя (для сохранения/поиска)
            _selectedGameName = p.GameName;
            _editingCopy = p.Clone();
            _dirtyFields.Clear();
            _createMode = false;
            LoadPointIntoPanel(p);
        }

        // Лёгкое обновление выделения/чекбоксов в сайдбаре БЕЗ полной перестройки (нет мерцания).
        private void UpdateSidebarSelection()
        {
            _suppressCheck = true;
            foreach (var node in EnumNodes(_sidebar.Nodes))
            {
                if (node.Nodes.Count > 0) continue; // родительские узлы категорий — не трогаем (это видимость)
                bool sel = !string.IsNullOrEmpty(node.Name) && _selectedIds.Contains(node.Name);
                if (node.Checked != sel) node.Checked = sel;
                node.BackColor = sel ? Color.FromArgb(60, 70, 90) : Color.FromArgb(22, 27, 38);
            }
            _suppressCheck = false;
        }

        private static IEnumerable<TreeNode> EnumNodes(TreeNodeCollection col)
        {
            foreach (TreeNode n in col) { yield return n; foreach (var c in EnumNodes(n.Nodes)) yield return c; }
        }

        // Подогнать карту, чтобы вместились ВСЕ выделенные точки (по _selectLookup).
        private void FitToSelection()
        {
            if (_selectedIds.Count == 0) return;
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            bool any = false;
            foreach (var id in _selectedIds)
                if (_selectLookup.TryGetValue(id, out var pt)) { any = true; if (pt.x < minX) minX = pt.x; if (pt.x > maxX) maxX = pt.x; if (pt.z < minZ) minZ = pt.z; if (pt.z > maxZ) maxZ = pt.z; }
            if (!any) return;
            _centerX = (minX + maxX) / 2;
            _centerZ = (minZ + maxZ) / 2;
            double pad = 1500;
            double worldW = (maxX - minX) + pad * 2;
            double worldH = (maxZ - minZ) + pad * 2;
            double sx = _mapPanel.Width / worldW;
            double sz = _mapPanel.Height / worldH;
            _scale = Math.Max(0.05, Math.Min(sx, sz));
            UpdateStatus();
            RequestRender();
        }

        private string? HitTarget(int sx, int sy)
        {
            foreach (var t in _targets)
            {
                var p = WorldToScreen(t.x, t.z);
                if (Math.Abs(p.X - sx) <= ClickThresholdPx && Math.Abs(p.Y - sy) <= ClickThresholdPx) return t.id;
            }
            return null;
        }

        private string? HitCity(int sx, int sy)
        {
            foreach (var c in _cities)
            {
                var p = WorldToScreen(c.x, c.z);
                if (Math.Abs(p.X - sx) <= ClickThresholdPx && Math.Abs(p.Y - sy) <= ClickThresholdPx) return "city:" + c.name;
            }
            return null;
        }

        private string? HitPOI(int sx, int sy)
        {
            foreach (var poi in _pois)
            {
                var p = WorldToScreen(poi.x, poi.z);
                if (Math.Abs(p.X - sx) <= ClickThresholdPx && Math.Abs(p.Y - sy) <= ClickThresholdPx) return "poi:" + poi.uid;
            }
            return null;
        }

        private void EnterCreateMode(double x, double z)
        {
            _createMode = true;
            _selectedGameName = null;
            _selectedIds.Clear();
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var pd = new PointData
            {
                GameName = "",               // намеренно пустое — обязательное, подсвечивается красным
                RealName = "Новая точка",
                Category = "Пользовательское",
                Enabled = true,
                X = x, Y = 0, Z = z,
                Color = "#ffff00",
                TriggerRadius = 200,
                IsNew = true
            };
            _editingCopy = pd.Clone();
            _dirtyFields.Clear();
            // Для новой точки координаты, системное имя и название — грязные автоматически
            // (остальные поля грязнятся только при ручном изменении пользователем).
            _dirtyFields.Add("X"); _dirtyFields.Add("Y"); _dirtyFields.Add("Z");
            _dirtyFields.Add("GameName"); _dirtyFields.Add("RealName");
            LoadPointIntoPanel(pd);
            // v70: пометка в АР-оверлее (серый крестик) в точке будущей новой точки.
            try { MainForm.Current?.ArPlacePinAtWorld(x, z); } catch { }
            RequestRender();
        }

        // СОЗДАНИЕ ТОЧКИ ИЗ АР (v70): точка на пересечении взгляда с плоскостью высоты
        // грузовика. Высота prefill = высота грузовика (из пометки); серая пометка
        // рисуется на карте, пока пользователь редактирует; снимается «отменить».
        public void OpenCreateAt(double x, double z, double y = 0, bool fromArPin = false)
        {
            _createMode = true;
            _createModeFromAr = fromArPin;
            if (fromArPin) _arPinWx = x; _arPinWz = z; _arPinWy = y;
            _selectedGameName = null;
            _selectedIds.Clear();
            var pd = new PointData
            {
                GameName = "",
                RealName = "Новая точка (АР)",
                Category = "Пользовательское",
                Enabled = true,
                X = x, Y = fromArPin ? Math.Round(y, 2) : 0, Z = z,
                Color = "#ffff00",
                TriggerRadius = 200,
                IsNew = true
            };
            _editingCopy = pd.Clone();
            _dirtyFields.Clear();
            _dirtyFields.Add("X"); _dirtyFields.Add("Y"); _dirtyFields.Add("Z");
            _dirtyFields.Add("GameName"); _dirtyFields.Add("RealName");
            LoadPointIntoPanel(pd);
            // Центрируем вид на место пометки, чтобы пользователь сразу его увидел.
            _centerX = x; _centerZ = z;
            UpdateStatus();
            RequestRender();
            LogEditor($"OpenCreateAt из АР: ({x:F1}, {y:F1}, {z:F1}) — пометка серым крестиком.");
        }

        // Помечаем место будущей точки (серый крестик) — рисуется в OnPaint.

        private void LoadPointIntoPanel(PointData pd)
        {
            _loadingPanel = true;
            try
            {
            foreach (var f in PointData.Fields)
            {
                if (!_fieldControls.TryGetValue(f.Key, out var ctrl)) continue;
                if (ctrl is TextBox tbx) tbx.BackColor = Color.FromArgb(40, 48, 62);
                var fld = typeof(PointData).GetField(f.Key);
                if (f.ValueType == typeof(bool))
                    ((CheckBox)ctrl).Checked = fld != null && (bool)fld.GetValue(pd)!;
                else if (f.Key == "Category")
                {
                    var cb = (ComboBox)ctrl;
                    if (!cb.Items.Contains(pd.Category)) cb.Items.Add(pd.Category);
                    cb.SelectedItem = pd.Category;
                }
                else
                {
                    // Если отображаемое имя (realName) пустое — в поле подставляем системное имя (gameName).

                    LogEditor($"LoadPointIntoPanel pd.GameName:{pd.GameName}"); 

                    if (f.Key == "RealName" && string.IsNullOrEmpty(pd.RealName) && !string.IsNullOrEmpty(pd.GameName))
                    {
                        ctrl.Text = pd.GameName;
                    }
                    else
                    {
                        var v = fld?.GetValue(pd);
                        ctrl.Text = v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                        
                        

                    }
                    LogEditor($"LoadPointIntoPanel ctrl.Text:{ctrl.Text}");

                }
            }
            }
            finally
            {
                _loadingPanel = false;
            }
            if (_gameNameError != null) _gameNameError.Visible = false;
            RefreshRequiredHighlight();
            UpdateActionButtons();
        }

        private void OnFieldChanged(string key)
        {
            if (_editingCopy == null) return;
            if (_loadingPanel) return; // программная загрузка — не санитизируем и не трогаем dirty-статус
            var f = PointData.Fields.FirstOrDefault(x => x.Key == key);
            if (f == null) return;
            var ctrl = _fieldControls[key];

            // Системное имя: только [a-z0-9_], заглавные автоматически переводятся в строчные.
            if (key == "GameName" && !_sanitizing)
            {
                _sanitizing = true;
                var tb = (TextBox)ctrl;
                var sb = new StringBuilder(tb.Text.Length);
                foreach (var ch in tb.Text)
                {
                    var c = char.ToLowerInvariant(ch);
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') sb.Append(c);
                }
                var clean = sb.ToString();
                if (tb.Text != clean)
                {
                    int p = tb.SelectionStart;
                    tb.Text = clean;
                    tb.SelectionStart = Math.Min(p, clean.Length);
                }
                _sanitizing = false;
                CheckGameNameUnique();
            }

            string cur = f.ValueType == typeof(bool) ? ((CheckBox)ctrl).Checked.ToString() : ctrl.Text;
            var fld = typeof(PointData).GetField(key);
            var origVal = fld?.GetValue(_editingCopy);
            string orig = origVal == null ? "" : (f.ValueType == typeof(bool) ? ((bool)origVal).ToString() : Convert.ToString(origVal, CultureInfo.InvariantCulture) ?? "");
            bool changed = cur != orig;
            if (changed) _dirtyFields.Add(key); else _dirtyFields.Remove(key);

            // Тёмно-оранжевый фон у изменённого поля (только текстовые/не-readonly).
            if (f.Mode != PointFieldMode.ReadOnly && f.ValueType != typeof(bool) && ctrl is TextBox tbx)
                tbx.BackColor = changed ? DirtyBg : Color.FromArgb(40, 48, 62);

            UpdateActionButtons();
            RequestRender();
        }

        // Проверка уникальности системного имени (при уходе фокуса из поля).
        private void CheckGameNameUnique()
        {
            if (_gameNameError == null) return;
            if (!_fieldControls.TryGetValue("GameName", out var ctrl)) return;
            var name = ((TextBox)ctrl).Text.Trim();
            if (string.IsNullOrEmpty(name)) { _gameNameError.Visible = false; return; }
            bool dup = _pointModel.Keys.Any(k => !string.Equals(k, _selectedGameName, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (dup) { _gameNameError.Text = "Имя уже существует, сделайте имя уникальным"; _gameNameError.Visible = true; }
            else _gameNameError.Visible = false;
            RefreshRequiredHighlight();
        }

        // Подсветка обязательного поля GameName (красное, если пустое в режиме создания или имя не уникально).
        private void RefreshRequiredHighlight()
        {
            if (_gameNameLabel == null) return;
            bool emptyCreate = _createMode && _fieldControls.TryGetValue("GameName", out var g) && string.IsNullOrWhiteSpace(((TextBox)g).Text);
            bool dupErr = _gameNameError != null && _gameNameError.Visible;
            _gameNameLabel.ForeColor = (emptyCreate || dupErr) ? Color.FromArgb(230, 90, 90) : Color.LightGray;
        }

        private void UpdateActionButtons()
        {
            bool hasPoint = _createMode || !string.IsNullOrEmpty(_selectedGameName);
            _btnSavePoint.Visible = !_createMode && _dirtyFields.Count > 0;
            _btnCancelPoint.Visible = _createMode || _dirtyFields.Count > 0;
            _btnAddPoint.Visible = _createMode;
            _btnDeletePoint.Visible = !_createMode && !string.IsNullOrEmpty(_selectedGameName);
            if (!_createMode && _selectedGameName != null && _pointModel.TryGetValue(_selectedGameName, out var pd))
                _btnDeletePoint.Text = pd.SourceFile != "" ? "Удалить" : "ОТМЕНИТЬ ИЗМЕНЕНИЯ";
        }

        private PointData? ReadPanelIntoPoint()
        {
            if (_editingCopy == null) return null;
            var pd = _editingCopy.Clone();
            foreach (var f in PointData.Fields)
            {
                if (f.Mode == PointFieldMode.ReadOnly) continue;
                var ctrl = _fieldControls[f.Key];
                var fld = typeof(PointData).GetField(f.Key);
                if (fld == null) continue;
                if (f.ValueType == typeof(bool))
                    fld.SetValue(pd, ((CheckBox)ctrl).Checked);
                else if (f.ValueType == typeof(double))
                {
                    if (double.TryParse(ctrl.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) fld.SetValue(pd, d);
                }
                else if (f.ValueType == typeof(int))
                {
                    if (int.TryParse(ctrl.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) fld.SetValue(pd, n);
                }
                else
                    fld.SetValue(pd, ctrl.Text);
            }
            return pd;
        }

        private bool ValidatePoint(PointData pd, out string err)
        {
            err = "";
            if (string.IsNullOrWhiteSpace(pd.GameName)) { err = "Системное имя (id) обязательно."; return false; }
            if (string.IsNullOrWhiteSpace(pd.RealName)) { err = "Отображаемое имя обязательно."; return false; }
            if (pd.X == 0 && pd.Z == 0) { err = "Координаты обязательны (X/Z не могут быть оба 0)."; return false; }
            return true;
        }

        private void SaveCurrentPoint()
        {
            var pd = ReadPanelIntoPoint();
            if (pd == null) return;
            if (!ValidatePoint(pd, out var err)) { MessageBox.Show(err, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // Предпросмотр JSON, который будет записан, с подтверждением сохранения.
            var jo = ComputeOverrideJObject(pd);
            string preview = jo.ToString(Formatting.Indented);
            var res = MessageBox.Show(
                $"Сохранить точку с новыми данными?\n\nБудет записано в файл overrides «{_selectedOverrideFile}»:\n{preview}",
                "Сохранение точки", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (res != DialogResult.Yes) return;

            string name = pd.GameName;
            pd.SourceFile = _selectedOverrideFile;
            pd.IsNew = false;
            LogEditor($"SaveCurrentPoint: pd.SourceFile:'{pd.SourceFile}' pd.GameName:{pd.GameName} pd.IsNew:{pd.IsNew} pd.RealName:{pd.RealName}");
            WritePointToOverrideFile(pd);
            _createMode = false;

            // После сохранения — команда «Обновить»: перезагрузить статику + overrides и применить на карту.
            LoadTargets();
            RebuildSelectLookup();

            // Точка теперь загружена ИЗ файлов (не изменена в редакторе) => грязных полей нет.
            if (_pointModel.TryGetValue(name, out var reloaded))
            {
                _selectedGameName = name;
                _selectedIds.Add(name);
                _editingCopy = reloaded.Clone();
                _dirtyFields.Clear();
                LoadPointIntoPanel(reloaded);
            }
            PopulateSidebar();
            UpdateActionButtons();
            RequestRender();
            LogEditor($"Точка '{name}' сохранена (delta) в overrides {_selectedOverrideFile}; выполнено обновление карты.");
            _statusBar.SetOperation($"сохранено: {name}", busy: false);
            MainForm.NotifyPointsOverridesChanged();
        }

        private void CommitNewPoint() { if (_createMode) SaveCurrentPoint(); }

        private void CancelChanges()
        {
            if (_createMode)
            {
                _createMode = false; _selectedGameName = null; _editingCopy = null; _dirtyFields.Clear();
                // v70: пометка из АР снимается кнопкой «отменить» (требование) —
                // серый крестик в AR-оверлее исчезает.
                if (_createModeFromAr)
                {
                    _createModeFromAr = false;
                    MainForm.NotifyArPinCancelled();
                }
                UpdateActionButtons(); RequestRender(); return;
            }
            if (!string.IsNullOrEmpty(_selectedGameName) && _pointModel.TryGetValue(_selectedGameName, out var pd))
            {
                _editingCopy = pd.Clone();
                _dirtyFields.Clear();
                LoadPointIntoPanel(pd);
                UpdateActionButtons();
                RequestRender();
            }
        }

        private void DeleteCurrentPoint()
        {
            if (string.IsNullOrEmpty(_selectedGameName)) return;
            if (!_pointModel.TryGetValue(_selectedGameName, out var pd)) return;
            string label = (pd.RealName + " [" + pd.GameName + "]").Trim();
            var res = MessageBox.Show(
                $"Удалить точку «{label}»? Действие необратимо (запись в overrides будет удалена/сброшена).",
                "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (res != DialogResult.Yes) return;
            if (pd.SourceFile != "")
            {
                RemovePointFromOverrideFile(pd.GameName, pd.SourceFile);
                _pointModel.Remove(pd.GameName);
            }
            else
                RemoveAnyOverrideFor(pd.GameName);
            _selectedGameName = null; _createMode = false; _editingCopy = null; _dirtyFields.Clear();
            RebuildTargetsFromModel(); PopulateSidebar(); UpdateActionButtons(); RequestRender();
            LogEditor($"Точка '{pd.GameName}' удалена/сброшена.");
            _statusBar.SetOperation($"удалено: {pd.GameName}", busy: false);
            MainForm.NotifyPointsOverridesChanged();
        }

        // v70: безопасные обёртки раскладки (для внешних вызывающих — AR-пометка).
        public void SuspendLayoutIfPossible() { if (!_disposed) try { SuspendLayout(); } catch { } }
        public void ResumeLayoutIfPossible()
        {
            if (_disposed) return;
            try { ResumeLayout(true); } catch { }
            try { ResumeLayout(false); } catch { }
        }

        private void CopySelectedAsJson()
        {
            if (_selectedIds.Count == 0) return;
            var arr = new JArray();
            foreach (var id in _selectedIds)
            {
                if (_pointModel.TryGetValue(id, out var pd)) arr.Add(PointDataToJObject(pd));
                else if (_selectLookup.TryGetValue(id, out var pt))
                    arr.Add(new JObject { ["key"] = id, ["name"] = pt.label, ["x"] = pt.x, ["z"] = pt.z });
            }
            var json = arr.ToString(Formatting.Indented);
            Clipboard.SetText(json);
            ShowCopied($"Скопировано точек: {_selectedIds.Count} (JSON в буфере обмена).");
        }

        // Записывает точку в выбранный файл overrides.
        // Безопасная загрузка корня overrides: пустой/повреждённый/отсутствующий файл трактуем
        // как свежий корень {"customTargets":[]} (без броска исключения JObject.Parse).
        private JObject LoadOverrideRoot(string path)
        {
            if (!File.Exists(path)) return new JObject { ["customTargets"] = new JArray() };
            try
            {
                var txt = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(txt))
                {
                    LogEditor($"[EDITOR][WARN] файл overrides '{path}' пустой — пересоздаём корень.");
                    return new JObject { ["customTargets"] = new JArray() };
                }
                var root = JObject.Parse(txt);
                if (root["customTargets"] as JArray == null) root["customTargets"] = new JArray();
                return root;
            }
            catch (Exception ex)
            {
                LogEditor($"[EDITOR][WARN] файл overrides '{path}' повреждён ({ex.Message}) — пересоздаём корень.");
                return new JObject { ["customTargets"] = new JArray() };
            }
        }

        // Для НОВОЙ точки — полная запись; для существующей — ТОЛЬКО изменённые (грязные) поля (delta).
        // Вычисляет JObject, который БУДЕТ записан для точки (для предпросмотра и самой записи).
        private JObject ComputeOverrideJObject(PointData pd)
        {
            JObject root = LoadOverrideRoot(Path.Combine(_overridesDir, _selectedOverrideFile));
            var arr = root["customTargets"] as JArray;
            if (arr == null) { arr = new JArray(); root["customTargets"] = arr; }
            var existing = arr.OfType<JObject>().FirstOrDefault(o => (o["gameName"]?.Value<string>() ?? "") == pd.GameName);
            JObject jo;
            if (existing != null)
            {
                // Точка УЖЕ есть в overrides — дельта-слияние: перезаписываем ТОЛЬКО грязные поля (+ имя).
                jo = (JObject)existing.DeepClone();
                jo["gameName"] = pd.GameName;
                var full = PointDataToJObject(pd);
                foreach (var key in _dirtyFields)
                    if (FieldJson.TryGetValue(key, out var names))
                        foreach (var nm in names) if (full.ContainsKey(nm)) jo[nm] = full[nm];
            }
            else
            {
                // Нет записи (новая точка ИЛИ первая правка статической) — ТОЛЬКО грязные поля (+ имя).
                jo = new JObject { ["gameName"] = pd.GameName };
                var full = PointDataToJObject(pd);
                foreach (var key in _dirtyFields)
                    if (FieldJson.TryGetValue(key, out var names))
                        foreach (var nm in names) if (full.ContainsKey(nm)) jo[nm] = full[nm];
            }
            return jo;
        }

        private void WritePointToOverrideFile(PointData pd)
        {
            Directory.CreateDirectory(_overridesDir);
            string path = Path.Combine(_overridesDir, _selectedOverrideFile);
            JObject root = LoadOverrideRoot(path);
            // ВАЖНО: не переприсваивать root["customTargets"] = arr, если arr уже получен ИЗ root —
            // Newtonsoft отвязывает токен-ребёнка при повторном присваивании, и добавленные записи
            // не попадают в root (файл сохраняется пустым). Назначаем массив ТОЛЬКО когда его нет.
            var arr = root["customTargets"] as JArray;
            if (arr == null) { arr = new JArray(); root["customTargets"] = arr; }
            var existing = arr.OfType<JObject>().FirstOrDefault(o => (o["gameName"]?.Value<string>() ?? "") == pd.GameName);
            var jo = ComputeOverrideJObject(pd);
            if (existing != null) { int idx = arr.IndexOf(existing); arr[idx] = jo; }
            else arr.Add(jo);

            string content = root.ToString(Formatting.Indented);
            LogEditor($"[SAVE-START] начало записи точки '{pd.GameName}' в файл: {path} (режим: {(existing == null ? "новая(только грязные)" : "delta")}, грязных полей: {_dirtyFields.Count})");
            LogEditor($"[SAVE-PATH] {path}");
            LogEditor($"[SAVE-CONTENT] содержимое для записи ({content.Length} симв.):\n{content}");
            File.WriteAllText(path, content);
            // После записи читаем файл и логируем его реальное содержимое.
            try
            {
                string readBack = File.ReadAllText(path);
                long vbytes = new FileInfo(path).Length;
                LogEditor($"[SAVE-VERIFY] файл записан: байт={vbytes}, записей={arr.Count}");
                LogEditor($"[SAVE-READBACK] прочитано из файла ({readBack.Length} симв.):\n{readBack}");
            }
            catch (Exception vex) { LogEditor($"[SAVE-ERROR] не удалось перечитать файл: {vex.Message}"); }
            LogEditor($"Запись точки '{pd.GameName}' в файл: {path} (полей в записи: {jo.Count})");
            // ФАЙЛ ОБЯЗАН быть в load_order — иначе конвейер миникарты его НЕ прочитает
            // (диагноз 17:26: custom_map1.json писался, но load_order содержал только test_targets).
            if (!_overrideFiles.Contains(_selectedOverrideFile))
            {
                _overrideFiles.Add(_selectedOverrideFile);
                SaveLoadOrder();
                LogEditor($"[OVR][FIX] файл {_selectedOverrideFile} добавлен в load_order.txt (не был подключён — миникарта бы его не прочитала!)");
                MainForm.NotifyPointsOverridesChanged(); // файл стал видимым конвейеру — переотправить пакет
            }
        }

        private void RemovePointFromOverrideFile(string gameName, string file)
        {
            string path = Path.Combine(_overridesDir, file);
            if (!File.Exists(path)) return;
            var root = LoadOverrideRoot(path);
            var arr = root["customTargets"] as JArray;
            if (arr == null) return;
            var toRemove = arr.OfType<JObject>().Where(o => (o["gameName"]?.Value<string>() ?? "") == gameName).ToList();
            foreach (var r in toRemove) arr.Remove(r);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            LogEditor($"[OVR] RemovePointFromOverrideFile: '{gameName}' удалён из {file} (убрано {toRemove.Count}).");
        }

        private void RemoveAnyOverrideFor(string gameName)
        {
            foreach (var f in _overrideFiles)
            {
                string path = Path.Combine(_overridesDir, f);
                if (!File.Exists(path)) continue;
                var root = LoadOverrideRoot(path);
                var arr = root["customTargets"] as JArray;
                if (arr == null) continue;
                var toRemove = arr.OfType<JObject>().Where(o => (o["gameName"]?.Value<string>() ?? "") == gameName).ToList();
                if (toRemove.Count > 0) { foreach (var r in toRemove) arr.Remove(r); File.WriteAllText(path, root.ToString(Formatting.Indented)); }
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _editorRestCts?.Cancel(); } catch { }
            try { _wsReconnectTimer.Stop(); } catch { }
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            try { _roadsPath?.Dispose(); } catch { _roadsPath = null; }
            try { _tooltip.Dispose(); } catch { }
            SaveEditorState();
        }
    }
}
