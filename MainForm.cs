using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using static System.Windows.Forms.AxHost;

namespace ETS2_Assist_GUI
{
    public partial class MainForm : Form
    {
        // UI Components
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;
        private MenuStrip mainMenu = null!;
        private ToolStripMenuItem fileMenu = null!;
        private ToolStripMenuItem settingsMenu = null!;
        private ToolStripMenuItem helpMenu = null!;
        private ToolStripMenuItem checkUpdatesMenu = null!;
        private ToolStripMenuItem exitMenu = null!;

        private List<CityData> _cities = new();
        private List<RoadSegment> _roads = new();

        private Button btnStart = null!;
        private Button btnStop = null!;
        private Button btnRestartOverlay = null!;
        private Button btnMinimize = null!;
        private Button btnExit = null!;
        private Button btnTheme = null!;
        private Button btnRefreshTracks = null!;
        private Button btnRandomTarget = null!;
        private Button btnRandomTarget2 = null!;
        private Button btnRandomTarget3 = null!;
        private Button btnRandomTarget4 = null!;
        private Button btnCheckTargets = null!;
        private Button btnTestPause = null!;
        private Button btnResetRecordingOrigin = null!;
        private Button btnOpenTrack = null!;
        private Button btnDeleteTracks = null!;
        private Button btnOpenTracksFolder = null!;
        private Button btnShowMap = null!;
        private Button btnShowHybrid = null!;
        private Button btnMapEditor = null!;

        private RichTextBox logConsole = null!;
        private Panel indicatorsPanel = null!;
        private Panel trackActionsPanel = null!;
        private ListBox listTracks = null!;
        private ToolTip trackToolTip = null!;

        private sealed class TrackListEntry
        {
            public string BaseName { get; init; } = "";
            public string Title { get; init; } = "";
            public string Description { get; init; } = "";
            public override string ToString() => Title;
        }

        private Label indicatorEts2Assist = null!;
        private Label indicatorEts2 = null!;
        private Label indicatorEts2Plugins = null!;
        private Label indicatorTruckTel = null!;
        private Label indicatorWebServer = null!;
        private Label indicatorWebOverlay = null!;
        private Label indicatorArduino = null!;
        private Label buildVersionLabel = null!;

        private System.Windows.Forms.Timer statusTimer = null!;

        private ProcessManager procManager = null!;
        private Logger logger = null!;
        private LanguageManager lang = null!;

        private Dictionary<string, JobInitialData> _jobData = new();

        private class JobInitialData
        {
            public double InitialDistance { get; set; }
            public double InitialJobRemaining { get; set; }
            public string JobId { get; set; } = "";
        }

        // ========== ГОРЯЧИЕ КЛАВИШИ ==========
        private const int HOTKEY_SAVE = 9000;
        private const int HOTKEY_START_REC = 9001;
        private const int HOTKEY_STOP_REC = 9002;
        private const int HOTKEY_MARKER = 9003;
        private const int HOTKEY_TEST = 9004;

        private bool hotKeyRegistered = false;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_ALT = 0x0001;

        // ========== УПРАВЛЕНИЕ UI И ПАУЗОЙ ==========
        private bool _uiShown = false;
        private bool? _lastPauseState = null;
        // Последнее «намерение» приложения по паузе. SetGamePause(true/false) выставляет
        // его. Используется как запасной источник детекции паузы, когда телеметрия
        // (TruckTel /api/rest/single/frame/paused) недоступна или отдаёт неразбираемый ответ.
        private bool _pausedIntent = false;
        private System.Windows.Forms.Timer _pauseCheckTimer = null!;

        // Состояние показа оверлеев (карта/гибрид/пауз-лого) и тоггл авто-показа миникарты.
        private bool _minimapAutoLogic = true;     // кнопка «Показать карту»: true = авто-логика включена
        private bool _darkTheme = false;           // тёмная тема интерфейса (кнопка «Тема»)
        private bool? _lastMinimapVisible;
        private bool? _lastMinimapAuto;
        private bool _committedActive = false;     // подтверждённое (с гистерезисом) состояние активности
        private int _activeMismatch = 0;

        // ========== WEBSOCKET-СЕРВЕР ДЛЯ СОХРАНЕНИЯ ТРЕКОВ (порт 8084) ==========
        private WebSocketSharp.Server.WebSocketServer? _wsSaveServer;
        private long _saveRequestsReceived;
        private bool _wsSaveRunning = false;
        private readonly object _saveRequestLock = new();
        private readonly HashSet<string> _processedSaveRequestIds = new(StringComparer.Ordinal);

        // Local static file server for WebOverlay. It disables browser caching so stable URLs
        // can be retained for WebOverlay position persistence without stale HTML/JS.
        private HttpListener? _staticWebListener;
        private Thread? _staticWebThread;
        private volatile bool _staticWebRunning;
        // One cache epoch per application start. WebOverlay URLs stay stable for saved window state;
        // only local JS/CSS asset URLs receive ?t=<epoch>.
        private readonly long _webCacheEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ========== HTTP-СЕРВЕР ДЛЯ ТРИГГЕР-ФАЙЛА И СПИСКА ТРЕКОВ (порт 8083) ==========
        private HttpListener? _triggerListener;
        private Thread? _triggerListenerThread;
        private bool _triggerListenerRunning = false;

        // ========== НАСТРОЙКИ ЗАПИСИ ==========
        private string _recordingMode = "auto";
        private int _maxRecordingDuration = 0;
        private bool _autoSave = false;
        private string _titleSuffix = "";
        private string _description = "";
        private int _saveFormat = 1;

        // ==========================================

        public MainForm()
        {
            AppDataPaths.EnsureUserData();
            try
            {
                var version = Application.ProductVersion ?? "0.0.0";
                this.Text = $"ETS2 Assist v{BuildInfo.Version}";
            }
            catch
            {
                this.Text = $"ETS2 Assist v{BuildInfo.Version}";
            }
            // Минимальная ширина гарантирует, что правая панель индикаторов
            // (список + действия + индикаторы) помещается и не обрезает текст.
            this.MinimumSize = new Size(1180, 480);
            ApplySavedWindowBounds();
            this.FormClosing += (s, e) =>
            {
                SaveWindowBounds();
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };
            this.Resize += (s, e) => PositionBuildLabel();

            InitializeComponents();
            InitializeTray();
            InitializeLanguage();
            InitializeProcessManager();
            InitializeStatusTimer();
            ApplyLanguage();
            RefreshUI();

            try
            {
                RegisterHotKey(this.Handle, HOTKEY_SAVE, MOD_CONTROL | MOD_SHIFT, (uint)Keys.S.GetHashCode());
                RegisterHotKey(this.Handle, HOTKEY_START_REC, MOD_CONTROL | MOD_SHIFT, (uint)Keys.R.GetHashCode());
                RegisterHotKey(this.Handle, HOTKEY_STOP_REC, MOD_CONTROL | MOD_SHIFT, (uint)Keys.X.GetHashCode());
                RegisterHotKey(this.Handle, HOTKEY_MARKER, MOD_CONTROL | MOD_SHIFT, (uint)Keys.N.GetHashCode());
                RegisterHotKey(this.Handle, HOTKEY_TEST, MOD_CONTROL | MOD_SHIFT, (uint)Keys.T.GetHashCode());
                hotKeyRegistered = true;
                AppendLog("Hotkeys registered: S (save+pause), R (start/reset rec), X (stop rec), N (marker), T (test)");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to register hotkeys: {ex.Message}");
            }

            if (AppSettings.AutoStartSystem)
            {
                Task.Run(async () => await StartSystemAsync());
            }

            if (AppSettings.StartMinimized)
            {
                this.Hide();
            }

            RefreshTrackList();
            LoadRecordingSettings();

            this.Shown += (_, _) => EnsureStartupForeground();
            _ = Task.Run(WaitForInstanceSignal);
        }

        private void WaitForInstanceSignal()
        {
            var signal = Program.InstanceSignal;
            if (signal == null) return;

            while (!IsDisposed && !Disposing)
            {
                if (!signal.WaitOne(1000)) continue;
                try
                {
                    BeginInvoke(() => EnsureStartupForeground(force: true));
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
        }

        private void EnsureStartupForeground(bool force = false)
        {
            if (AppSettings.StartMinimized && !force) return;

            void BringToFront()
            {
                try
                {
                    if (IsDisposed || Disposing) return;
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Show();
                    Activate();
                    bool confirmed = ForceForegroundWindow(Handle, "application-startup");
                    if (!confirmed)
                    {
                        TopMost = true;
                        TopMost = false;
                        ForceForegroundWindow(Handle, "application-startup-retry");
                    }
                    AppendLog($"[UI] ETS2 Assist выведен на передний план при запуске. confirmed={confirmed}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[UI] Ошибка вывода главного окна на передний план: {ex.Message}");
                }
            }

            BeginInvoke((Action)BringToFront);
            var retry1 = new System.Windows.Forms.Timer { Interval = 400 };
            retry1.Tick += (_, _) => { retry1.Stop(); retry1.Dispose(); BringToFront(); };
            retry1.Start();
            var retry2 = new System.Windows.Forms.Timer { Interval = 1200 };
            retry2.Tick += (_, _) => { retry2.Stop(); retry2.Dispose(); BringToFront(); };
            retry2.Start();
        }

        private void LoadRecordingSettings()
        {
            _recordingMode = "auto";
            _maxRecordingDuration = 0;
            _autoSave = false;
            _titleSuffix = "";
            _description = "";
            _saveFormat = 1;
        }

        private void PositionBuildLabel()
        {
            if (buildVersionLabel == null) return;
            buildVersionLabel.Location = new Point(Math.Max(0, ClientSize.Width - buildVersionLabel.Width - 10), Math.Max(0, ClientSize.Height - buildVersionLabel.Height - 8));
            buildVersionLabel.BringToFront();
        }

        private void InitializeComponents()
        {
            mainMenu = new MenuStrip();
            fileMenu = new ToolStripMenuItem("File");
            settingsMenu = new ToolStripMenuItem("Settings", null, (s, e) => OpenSettings());
            helpMenu = new ToolStripMenuItem("Help", null, (s, e) => ShowHelp());
            checkUpdatesMenu = new ToolStripMenuItem("Check Updates", null, (s, e) => CheckUpdates());
            exitMenu = new ToolStripMenuItem("Exit", null, (s, e) => ConfirmExit());

            fileMenu.DropDownItems.Add(settingsMenu);
            fileMenu.DropDownItems.Add(checkUpdatesMenu);
            fileMenu.DropDownItems.Add(helpMenu);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitMenu);
            mainMenu.Items.Add(fileMenu);
            this.Controls.Add(mainMenu);

            int leftX = 20;
            int topY = mainMenu.Height + 20;

            btnStart = new Button { Text = "Start", Location = new Point(leftX, topY), Size = new Size(120, 30) };
            btnStart.Click += (s, e) => StartSystem();

            btnStop = new Button { Text = "Stop", Location = new Point(leftX, topY + 40), Size = new Size(120, 30), Enabled = false };
            btnStop.Click += (s, e) => StopSystem();

            btnRestartOverlay = new Button { Text = "Restart Overlay", Location = new Point(leftX, topY + 80), Size = new Size(120, 30) };
            btnRestartOverlay.Click += (s, e) => RestartOverlay();

            btnMinimize = new Button { Text = "Minimize", Location = new Point(leftX, topY + 120), Size = new Size(120, 30) };
            btnMinimize.Click += (s, e) => this.Hide();

            btnExit = new Button { Text = "Exit", Location = new Point(leftX, topY + 160), Size = new Size(120, 30) };
            btnExit.Click += (s, e) => ConfirmExit();

            btnTheme = new Button { Text = "Тема: светлая", Location = new Point(this.ClientSize.Width - 150, 4), Size = new Size(140, 26) };
            btnTheme.Click += (s, e) => { _darkTheme = !_darkTheme; ApplyTheme(); };

            btnRefreshTracks = new Button { Text = "Обновить список", Location = new Point(leftX, topY + 210), Size = new Size(120, 30) };
            btnRefreshTracks.Click += (s, e) => RefreshTrackList();

            btnRandomTarget = new Button { Text = "Курьер 100 POI дорога т50 а.у.", Location = new Point(leftX, topY + 260), Size = new Size(230, 20) };
            btnRandomTarget.Click += BtnRandomTarget_Click;

            btnRandomTarget2 = new Button { Text = "Тайник 2 200м т30", Location = new Point(leftX, topY + 290), Size = new Size(230, 20) };
            btnRandomTarget2.Click += BtnRandomTarget2_Click;

            btnRandomTarget3 = new Button { Text = "Перекус 400", Location = new Point(leftX, topY + 320), Size = new Size(230, 20) };
            btnRandomTarget3.Click += BtnRandomTarget3_Click;

            btnRandomTarget4 = new Button { Text = "Обзор целей", Location = new Point(leftX, topY + 350), Size = new Size(230, 20) };
            btnRandomTarget4.Click += BtnRandomTarget4_Click;

            btnCheckTargets = new Button { Text = "Проверка точек", Location = new Point(leftX, topY + 380), Size = new Size(120, 20) };
            btnCheckTargets.Click += BtnCheckTargets_Click;

            btnShowMap = new Button { Text = "Показать карту ✔", Location = new Point(leftX, topY + 410), Size = new Size(130, 30) };
            btnShowMap.Click += (s, e) => {
                _minimapAutoLogic = !_minimapAutoLogic;
                btnShowMap.Text = _minimapAutoLogic ? "Показать карту ✔" : "Показать карту ✖";
                AppendLog($"[UI] Авто-показ миникарты {( _minimapAutoLogic ? "ВКЛ" : "ВЫКЛ" )}");
                // Применяем немедленно, не дожидаясь таймера.
                bool activeNow = IsGameRunning() && !IsGamePaused() && IsGameFocused();
                bool minimapVisible = activeNow && _minimapAutoLogic;
                SendCommandToMap(minimapVisible ? "minimap_show" : "minimap_hide");
                SendCommandToMap("minimap_auto", new JObject { ["enabled"] = _minimapAutoLogic });
                _lastMinimapVisible = minimapVisible;
                _lastMinimapAuto = _minimapAutoLogic;
            };

            btnShowHybrid = new Button { Text = "Показать hybrid", Location = new Point(leftX, topY + 440), Size = new Size(120, 30) };
            btnShowHybrid.Click += (s, e) => {
                AppendLog("Debug: Show hybrid button clicked");
                SendCommandToMap("show_ui");
            };

            // Тест паузы через тот же Named Pipe, который используется при достижении цели.
            btnTestPause = new Button { Text = "Тест паузы SDK", Location = new Point(leftX, topY + 470), Size = new Size(135, 30) };
            btnTestPause.Click += (s, e) => {
                AppendLog("=== ТЕСТ ПАУЗЫ SDK ===");
                bool ok = SCSController.SetPause(true);
                AppendLog(ok
                    ? "[SCS] Команда PAUSE подтверждена."
                    : "[SCS] Команда PAUSE не подтверждена. Проверить ets2_assist_input.dll и ETS2.");
            };
            btnResetRecordingOrigin = new Button
            {
                Text = "Сбросить начало\nзаписи трека",
                Location = new Point(leftX, topY + 510),
                Size = new Size(120, 42),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnResetRecordingOrigin.Click += (s, e) => {
                AppendLog("[REC] Запрошен сброс начала записи трека.");
                SendCommandToMap("reset_recording_origin");
            };

            btnMapEditor = new Button { Text = "Редактор карты", Location = new Point(leftX, topY + 560), Size = new Size(230, 30) };
            btnMapEditor.Click += (s, e) => {
                try { new MapEditorForm().Show(); }
                catch (Exception ex) { AppendLog($"[EDITOR] Ошибка открытия редактора: {ex.Message}"); }
            };

            int consoleLeft = leftX + 240;
            int consoleWidth = 400;
            logConsole = new RichTextBox
            {
                Location = new Point(consoleLeft, topY),
                Size = new Size(consoleWidth, this.ClientSize.Height - topY - 40),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            logConsole.DoubleClick += (s, e) => OpenLogFolder();

            int listLeft = consoleLeft + consoleWidth + 10;
            int actionsWidth = 120;
            int indicatorsWidth = 190;
            int listWidth = Math.Max(150, this.ClientSize.Width - listLeft - actionsWidth - indicatorsWidth - 30);
            listTracks = new ListBox
            {
                Location = new Point(listLeft, topY),
                Size = new Size(listWidth, this.ClientSize.Height - topY - 40),
                BackColor = Color.FromArgb(20, 25, 35),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9),
                SelectionMode = SelectionMode.MultiExtended,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            trackToolTip = new ToolTip();
            listTracks.MouseMove += (_, e) =>
            {
                int index = listTracks.IndexFromPoint(e.Location);
                if (index < 0 || index >= listTracks.Items.Count || listTracks.Items[index] is not TrackListEntry entry)
                {
                    trackToolTip.SetToolTip(listTracks, "");
                    return;
                }
                trackToolTip.SetToolTip(listTracks, entry.Description);
            };
            listTracks.DoubleClick += (s, e) => OpenSelectedTrack();
            listTracks.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) OpenSelectedTrack(); };

            int actionsLeft = listLeft + listWidth + 10;
            trackActionsPanel = new Panel
            {
                Location = new Point(actionsLeft, topY),
                Size = new Size(actionsWidth, this.ClientSize.Height - topY - 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };

            btnOpenTrack = new Button
            {
                Text = "Открыть",
                Location = new Point(5, 10),
                Size = new Size(actionsWidth - 10, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnOpenTrack.Click += (s, e) => OpenSelectedTrack();

            btnDeleteTracks = new Button
            {
                Text = "Удалить",
                Location = new Point(5, 48),
                Size = new Size(actionsWidth - 10, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnDeleteTracks.Click += (s, e) => DeleteSelectedTracks();

            btnOpenTracksFolder = new Button
            {
                Text = "Открыть папку",
                Location = new Point(5, 86),
                Size = new Size(actionsWidth - 10, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnOpenTracksFolder.Click += (s, e) => OpenTracksFolder();
            trackActionsPanel.Controls.AddRange(new Control[] { btnOpenTrack, btnDeleteTracks, btnOpenTracksFolder });

            int indicatorLeft = actionsLeft + actionsWidth + 10;
            indicatorsPanel = new Panel
            {
                Location = new Point(indicatorLeft, topY),
                Size = new Size(190, this.ClientSize.Height - topY - 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };

            int indicatorTop = 10;
            int step = 32;

            indicatorEts2Assist = CreateIndicator("ETS2 Assist", indicatorTop);
            indicatorTop += step;
            indicatorEts2 = CreateIndicator("ETS2", indicatorTop);
            indicatorTop += step;
            indicatorEts2Plugins = CreateIndicator("ETS2 Plugins", indicatorTop);
            indicatorTop += step;
            indicatorTruckTel = CreateIndicator("TruckTel", indicatorTop);
            indicatorTruckTel.Cursor = Cursors.Hand;
            indicatorTruckTel.Click += (_, _) => OpenTelemetryInspector();
            indicatorTop += step;
            indicatorWebServer = CreateIndicator("Web Server", indicatorTop);
            indicatorTop += step;
            indicatorWebOverlay = CreateIndicator("Web Overlay", indicatorTop);
            indicatorTop += step;
            indicatorArduino = CreateIndicator("Arduino", indicatorTop);

            indicatorsPanel.Controls.AddRange(new Control[] {
                indicatorEts2Assist, indicatorEts2, indicatorEts2Plugins,
                indicatorTruckTel, indicatorWebServer,
                indicatorWebOverlay, indicatorArduino
            });

            buildVersionLabel = new Label
            {
                Text = $"BUILD {BuildInfo.Version}",
                AutoSize = true,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.DarkGray,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(this.ClientSize.Width - 210, this.ClientSize.Height - 25)
            };

            this.Controls.AddRange(new Control[] {
                btnStart, btnStop, btnRestartOverlay, btnMinimize, btnExit, btnRefreshTracks, btnRandomTarget,
                btnRandomTarget2, btnRandomTarget3, btnRandomTarget4, btnCheckTargets, btnShowMap, btnShowHybrid, btnTestPause, btnResetRecordingOrigin, btnMapEditor,
                logConsole, listTracks, trackActionsPanel, indicatorsPanel, buildVersionLabel, mainMenu, btnTheme
            });
            PositionBuildLabel();
            ApplyTheme();
        }

        // ============================================================
        // ТЁМНАЯ / СВЕТЛАЯ ТЕМА
        // ============================================================
        private void ApplyTheme()
        {
            Color back = _darkTheme ? Color.FromArgb(43, 43, 43) : SystemColors.Control;
            Color fore = _darkTheme ? Color.FromArgb(232, 232, 232) : SystemColors.ControlText;
            this.BackColor = back;
            this.ForeColor = fore;
            btnTheme.Text = _darkTheme ? "Тема: тёмная" : "Тема: светлая";
            foreach (Control c in this.Controls) SetControlTheme(c, back, fore);
        }

        private static void SetControlTheme(Control c, Color back, Color fore)
        {
            switch (c)
            {
                case Button _:
                case GroupBox _:
                case Panel _:
                    c.BackColor = back == SystemColors.Control ? SystemColors.Control : Color.FromArgb(60, 60, 60);
                    c.ForeColor = fore;
                    break;
                case TextBox _:
                case RichTextBox _:
                case ListBox _:
                case ComboBox _:
                    c.BackColor = back == SystemColors.Control ? Color.White : Color.FromArgb(30, 30, 30);
                    c.ForeColor = fore;
                    break;
                default:
                    c.BackColor = back;
                    c.ForeColor = fore;
                    break;
            }
            foreach (Control cc in c.Controls) SetControlTheme(cc, back, fore);
        }

        private Label CreateIndicator(string labelText, int top)
        {
            Label lbl = new Label
            {
                Text = labelText + ": OFF",
                Location = new Point(5, top),
                Size = new Size(180, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.Gray
            };
            return lbl;
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Start System", null, (s, e) => StartSystem());
            trayMenu.Items.Add("Stop System", null, (s, e) => StopSystem());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Check Updates", null, (s, e) => CheckUpdates());
            trayMenu.Items.Add("Exit", null, (s, e) => ConfirmExit());

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "icon.ico");
            Icon icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

            trayIcon = new NotifyIcon
            {
                Icon = icon,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.MouseDoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }

        private void SaveLanguageToConfig(string lang)
        {
            try
            {
                string configPath = AppDataPaths.ConfigFile;
                JObject config;
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    config = JObject.Parse(json);
                }
                else
                {
                    config = new JObject();
                    string dir = Path.GetDirectoryName(configPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir!);
                }
                config["language"] = lang;
                File.WriteAllText(configPath, config.ToString(Formatting.Indented));
                AppendLog($"Language saved to config.json: {lang}");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to save language to config.json: {ex.Message}");
            }
        }

        private void InitializeLanguage()
        {
            lang = LanguageManager.Instance;
            lang.LanguageChanged += OnLanguageChanged;
            string currentLang = AppSettings.Language;
            if (!string.IsNullOrEmpty(currentLang))
                lang.LoadLanguage(currentLang);
            SaveLanguageToConfig(currentLang);
        }

        private void InitializeProcessManager()
        {
            logger = new Logger();
            Logger.Current = logger;
            logger.OnLogMessage += (msg) => AppendLog(msg, persistWorkflow: false);
            logger.Workflow("ETS2 Assist logger initialized.");
            procManager = new ProcessManager(logger);
            procManager.StatusChanged += (s, e) => RefreshUI();
        }

        private void InitializeStatusTimer()
        {
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 2000;
            statusTimer.Tick += (s, e) => UpdateIndicators();
            statusTimer.Start();
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

        private void ApplyLanguage()
        {
            this.Text = lang.Get("app_title") ?? "ETS2 Assist";
            var version = Application.ProductVersion ?? "0.0.0";
            this.Text = $"ETS2 Assist v{BuildInfo.Version}";

            btnStart.Text = lang.Get("ui_start") ?? "Start";
            btnStop.Text = lang.Get("ui_stop") ?? "Stop";
            btnRestartOverlay.Text = lang.Get("ui_restart_overlay") ?? "Restart Overlay";
            btnMinimize.Text = lang.Get("ui_minimize") ?? "Minimize";
            btnExit.Text = lang.Get("ui_exit") ?? "Exit";
            btnRefreshTracks.Text = "Обновить список";
            btnRandomTarget.Text = "Курьер 100 POI дорога т50 а.у.";
            btnRandomTarget2.Text = "Тайник 2 200м т30";
            btnRandomTarget3.Text = "Перекус 400";
            btnRandomTarget4.Text = "Обзор целей";
            btnCheckTargets.Text = "Проверка точек";
            btnShowMap.Text = _minimapAutoLogic ? "Показать карту ✔" : "Показать карту ✖";
            btnShowHybrid.Text = "Показать hybrid";
            btnMapEditor.Text = "Редактор карты";
            fileMenu.Text = lang.Get("ui_file") ?? "File";
            settingsMenu.Text = lang.Get("ui_settings") ?? "Settings";
            helpMenu.Text = lang.Get("ui_help") ?? "Help";
            checkUpdatesMenu.Text = lang.Get("ui_check_updates") ?? "Check Updates";
            exitMenu.Text = lang.Get("ui_exit") ?? "Exit";
            trayMenu.Items[0].Text = lang.Get("tray_start") ?? "Start System";
            trayMenu.Items[1].Text = lang.Get("tray_stop") ?? "Stop System";
            trayMenu.Items[3].Text = lang.Get("tray_check_updates") ?? "Check Updates";
            trayMenu.Items[4].Text = lang.Get("tray_exit") ?? "Exit";
        }

        private void RefreshUI()
        {
            if (procManager.IsRunning)
            {
                btnStart.Enabled = false;
                btnStart.Text = lang.Get("ui_starting") ?? "Starting...";
                btnStop.Enabled = true;
                btnStop.Text = lang.Get("ui_stop") ?? "Stop";
            }
            else
            {
                btnStart.Enabled = !procManager.IsStarting;
                btnStart.Text = lang.Get("ui_start") ?? "Start";
                btnStop.Enabled = false;
                btnStop.Text = lang.Get("ui_stop") ?? "Stop";
            }
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            if (procManager.IsRunning && !procManager.HasErrors)
                trayIcon.Icon = SystemIcons.Application;
            else if (procManager.IsRunning && procManager.HasErrors)
                trayIcon.Icon = SystemIcons.Error;
            else
                trayIcon.Icon = SystemIcons.Application;
        }

        private async void StartSystem()
        {
            await StartSystemAsync();
        }

        private async Task StartSystemAsync()
        {
            if (procManager.IsRunning) return;

            AppendLog("Starting system...");
            CleanupStaleTriggerFiles();

            if (!CheckPlugins())
            {
                AppendLog("Plugins check failed. System start aborted.");
                return;
            }

            // Старый Ets2Telemetry.exe больше не запускаем.
            // Телеметрия должна приходить напрямую из TruckTel / ETS2 Assist Plugin.
            AppendLog("Legacy ETS2 Telemetry Server disabled; using direct plugin WebSocket API.");

            EnsureStaticMapData();

            if (!StartPythonServer())
            {
                AppendLog("Web server start failed. System start aborted.");
                return;
            }

            StartTriggerServer();
            StartWebSocketSaveServer();

            await procManager.StartAsync();

            StartWebOverlay();

            UpdateTruckTelPort();

            StartPauseCheck();

            // ===== ИНИЦИАЛИЗАЦИЯ SCS CONTROLLER =====
            // ===== ИНИЦИАЛИЗАЦИЯ SCS CONTROLLER =====
            SCSController.OnLog += (msg) => AppendLog(msg);
            if (!SCSController.Initialize())
            {
                AppendLog("SCS SDK input controller initialization failed. Pause via SDK may not work until ETS2/plugin is running.");
            }
            else
            {
                AppendLog("SCS SDK input controller initialized successfully.");
            }

            UpdateStartButton();
        }

        private void CleanupStaleTriggerFiles()
        {
            try
            {
                string dataDir = AppDataPaths.UserDataDirectory;
                if (!Directory.Exists(dataDir))
                {
                    AppendLog("[TRIGGER] Папка data не найдена, очистка старых триггеров не требуется.");
                    return;
                }

                var triggerFiles = Directory.GetFiles(dataDir, "*.trigger", SearchOption.TopDirectoryOnly);
                if (triggerFiles.Length == 0)
                {
                    AppendLog("[TRIGGER] Старых trigger-файлов не найдено.");
                    return;
                }

                foreach (string path in triggerFiles)
                {
                    try
                    {
                        File.Delete(path);
                        AppendLog($"[TRIGGER] Удалён старый trigger-файл: {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[TRIGGER] Не удалось удалить старый trigger-файл {Path.GetFileName(path)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[TRIGGER] Ошибка очистки старых trigger-файлов: {ex.Message}");
            }
        }


        private void EnsureStaticMapData()
        {
            try
            {
                string publishData = AppDataPaths.StaticDataDirectory;
                Directory.CreateDirectory(publishData);
                var required = new[]
                {
                    Path.Combine("GeoJson", "roads.geojson"),
                    Path.Combine("GeoJson", "cities.geojson"),
                    "Overlays.json",
                    Path.Combine("localized_cities", "cities_sibirmap.json")
                };
                bool complete = required.All(r => File.Exists(Path.Combine(publishData, r)));
                if (complete)
                {
                    AppendLog("[STATIC] Map static data is present in publish\\data.");
                    foreach (string rel in required)
                    {
                        string fp = Path.Combine(publishData, rel);
                        AppendLog($"[STATIC] File {rel}: {new FileInfo(fp).Length} bytes");
                    }
                    return;
                }

                DirectoryInfo? cur = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                DirectoryInfo? candidate = null;
                for (int i = 0; i < 8 && cur != null; i++, cur = cur.Parent)
                {
                    string rootData = Path.Combine(cur.FullName, "data");
                    if (Directory.Exists(rootData) && required.Any(r => File.Exists(Path.Combine(rootData, r))))
                    {
                        candidate = new DirectoryInfo(rootData);
                        break;
                    }
                }

                if (candidate != null && !candidate.FullName.Equals(publishData, StringComparison.OrdinalIgnoreCase))
                {
                    int copied = 0;
                    foreach (string rel in required)
                    {
                        string source = Path.Combine(candidate.FullName, rel);
                        string dest = Path.Combine(publishData, rel);
                        if (File.Exists(source) && !File.Exists(dest))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            File.Copy(source, dest, overwrite: false);
                            copied++;
                            AppendLog($"[STATIC] Recovered missing static file: {rel}");
                        }
                    }
                    if (copied > 0)
                    {
                        AppendLog($"[STATIC] Recovered {copied} static file(s) from legacy data directory.");
                      }
                }

                var missing = required.Where(r => !File.Exists(Path.Combine(publishData, r))).ToArray();
                if (missing.Length > 0)
                {
                    AppendLog("[STATIC][ERROR] Missing map static data:");
                    foreach (string rel in missing) AppendLog($"[STATIC][ERROR]   {rel}");
                    AppendLog("[STATIC][ERROR] The minimap cannot show roads/cities/POI until these files are restored.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[STATIC][ERROR] Static map data check failed: {ex.Message}");
            }
        }

        private void StartTriggerServer()
        {
            if (_triggerListenerRunning) return;
            try
            {
                _triggerListener = new HttpListener();
                _triggerListener.Prefixes.Add("http://localhost:8083/");
                _triggerListener.Start();
                _triggerListenerRunning = true;
                _triggerListenerThread = new Thread(() => TriggerListenerLoop());
                _triggerListenerThread.IsBackground = true;
                _triggerListenerThread.Start();
                AppendLog("Trigger HTTP server started on port 8083.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start trigger server: {ex.Message}");
            }
        }

        private void StopTriggerServer()
        {
            if (!_triggerListenerRunning) return;
            try
            {
                _triggerListenerRunning = false;
                _triggerListener?.Stop();
                _triggerListener?.Close();
                _triggerListenerThread?.Join(1000);
                AppendLog("Trigger HTTP server stopped.");
            }
            catch (Exception ex)
            {
                AppendLog($"Error stopping trigger server: {ex.Message}");
            }
        }

        private void TriggerListenerLoop()
        {
            while (_triggerListenerRunning && _triggerListener != null && _triggerListener.IsListening)
            {
                try
                {
                    var context = _triggerListener.GetContext();
                    Task.Run(() => ProcessTriggerRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppendLog($"Trigger server error: {ex.Message}");
                }
            }
        }

        private void ProcessTriggerRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.OutputStream.Close();
                    return;
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/check_trigger")
                {
                    string file = request.QueryString["file"] ?? "save_trail.trigger";
                    string triggerPath = Path.Combine(AppDataPaths.UserDataDirectory, Path.GetFileName(file));
                    bool exists = File.Exists(triggerPath);
                    bool paused = false;

                    // A trail-save trigger may only be consumed while ETS2 is paused.
                    // Do not let the browser begin a save while the truck is moving.
                    if (exists)
                    {
                        paused = IsGamePaused();
                        if (!paused)
                            AppendLog("[SAVE] Trigger found, but saving is blocked because ETS2 is not paused.");
                    }

                    string json = JsonConvert.SerializeObject(new
                    {
                        exists = exists && paused,
                        paused,
                        blocked = exists && !paused
                    });
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/delete_trigger")
                {
                    string file = request.QueryString["file"] ?? "save_trail.trigger";
                    string triggerPath = Path.Combine(AppDataPaths.UserDataDirectory, Path.GetFileName(file));
                    if (File.Exists(triggerPath)) File.Delete(triggerPath);
                    string json = JsonConvert.SerializeObject(new { success = true });
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/list_tracks")
                {
                    string tracksDir = AppDataPaths.SavedTracksDirectory;
                    if (!Directory.Exists(tracksDir)) Directory.CreateDirectory(tracksDir);
                    var files = Directory.GetFiles(tracksDir, "track_*.html")
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .OrderByDescending(f => f)
                        .ToList();
                    var list = new { files = files };
                    string json = JsonConvert.SerializeObject(list);
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath.StartsWith("/get_track/"))
                {
                    string fileName = request.Url.AbsolutePath.Substring("/get_track/".Length);
                    string tracksDir = AppDataPaths.SavedTracksDirectory;
                    string filePath = Path.Combine(tracksDir, fileName);
                    if (File.Exists(filePath))
                    {
                        string json = File.ReadAllText(filePath);
                        byte[] buffer = Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }
                    response.OutputStream.Close();
                }
                else
                {
                    response.StatusCode = 404;
                    response.OutputStream.Close();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error processing trigger request: {ex.Message}");
                try { context.Response.StatusCode = 500; context.Response.OutputStream.Close(); } catch { }
            }
        }

        // ================================================================
        // WEBSOCKET-СЕРВЕР ДЛЯ СОХРАНЕНИЯ ТРЕКОВ (порт 8084)
        // ================================================================
        public class TrailBehavior : WebSocketBehavior
        {
            private static Action<string>? _log;
            private static Action<JObject>? _onTrail;
            private static Action<string>? _playSoundAction;
            private static Action<JObject>? _onCommand;
            private static Func<string>? _uiSyncCommand;

            public static void SetLog(Action<string> log) => _log = log;
            public static void SetOnTrail(Action<JObject> action) => _onTrail = action;
            public static void SetPlaySoundAction(Action<string> action) => _playSoundAction = action;
            public static void SetOnCommand(Action<JObject> action) => _onCommand = action;
            public static void SetUiSync(Func<string> provider) => _uiSyncCommand = provider;

            protected override void OnOpen()
            {
                _log?.Invoke("[WebSocket] Клиент карты подключился");
                try
                {
                    var command = _uiSyncCommand?.Invoke() ?? "show_ui";
                    Send(JsonConvert.SerializeObject(new { command }));
                    _log?.Invoke($"[WebSocket] Новому клиенту отправлена синхронизация UI: {command}");
                }
                catch (Exception ex) { _log?.Invoke($"[WebSocket] UI sync error: {ex.Message}"); }
            }


            protected override void OnMessage(MessageEventArgs e)
            {
                string? requestId = null;
                try
                {
                    if (!e.IsText || string.IsNullOrWhiteSpace(e.Data))
                    {
                        _log?.Invoke("[WebSocket] Игнорировано пустое или бинарное сообщение.");
                        return;
                    }

                    var json = e.Data;
                    _log?.Invoke($"[WebSocket] Получено сообщение: {json.Substring(0, Math.Min(json.Length, 200))}...");
                    var data = JObject.Parse(json);
                    requestId = data["requestId"]?.Value<string>();

                    if (data["command"]?.Value<string>() == "play_sound")
                    {
                        var soundType = data["type"]?.Value<string>() ?? "beep";
                        _log?.Invoke($"[WebSocket] Команда звука: {soundType}");
                        _playSoundAction?.Invoke(soundType);
                        return;
                    }

                    if (data["command"] != null)
                    {
                        _log?.Invoke($"[WebSocket] Команда от клиента: {data["command"]}");
                        _onCommand?.Invoke(data);
                        return;
                    }

                    _log?.Invoke($"[WebSocket] Получен трек ({json.Length} байт)");
                    _log?.Invoke($"[SAVE] Начало сохранения requestId={requestId ?? "-"}");
                    _onTrail?.Invoke(data);
                    Send(JsonConvert.SerializeObject(new { status = "ok", message = "Трек сохранён", requestId }));
                    _log?.Invoke($"[SAVE] Успешный ответ отправлен requestId={requestId ?? "-"}");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[WebSocket] Ошибка обработки: {ex.Message}");
                    Send(JsonConvert.SerializeObject(new { status = "error", message = ex.Message, requestId }));
                }
            }

            protected override void OnClose(CloseEventArgs e)
            {
                _log?.Invoke("[WebSocket] Клиент карты отключился");
            }

            protected override void OnError(WebSocketSharp.ErrorEventArgs e)
            {
                _log?.Invoke($"[WebSocket] Ошибка: {e.Message}");
            }
        }

        private void StartWebSocketSaveServer()
        {
            if (_wsSaveRunning) return;
            try
            {
                TrailBehavior.SetLog(msg => AppendLog(msg));
                TrailBehavior.SetOnTrail(data => SaveTrailFromWebSocket(data));
                TrailBehavior.SetPlaySoundAction(PlaySound);
                TrailBehavior.SetOnCommand(data => OnClientCommand(data));
                TrailBehavior.SetUiSync(() => _lastPauseState == true ? "hide_ui" : "show_ui");

                _wsSaveServer = new WebSocketSharp.Server.WebSocketServer($"ws://localhost:8084");
                _wsSaveServer.AddWebSocketService<TrailBehavior>("/");
                _wsSaveServer.Start();
                _wsSaveRunning = true;
                AppendLog("WebSocket save server started on port 8084.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start WebSocket save server: {ex.Message}");
            }
        }

        private void StopWebSocketSaveServer()
        {
            if (!_wsSaveRunning) return;
            try
            {
                _wsSaveServer?.Stop();
                _wsSaveServer = null;
                _wsSaveRunning = false;
                AppendLog("WebSocket save server stopped.");
            }
            catch (Exception ex)
            {
                AppendLog($"Error stopping WebSocket save server: {ex.Message}");
            }
        }

        private void PlaySound(string soundType)
        {
            try
            {
                switch (soundType)
                {
                    case "success":
                        System.Media.SystemSounds.Asterisk.Play();
                        break;
                    case "icon":
                        System.Media.SystemSounds.Beep.Play();
                        break;
                    default:
                        System.Media.SystemSounds.Beep.Play();
                        break;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Sound] Ошибка воспроизведения: {ex.Message}");
            }
        }

        // ================================================================
        // ОТПРАВКА КОМАНД НА ВСЕ ПОДКЛЮЧЁННЫЕ КЛИЕНТЫ (ВЕБ-СТРАНИЦЫ)
        // ================================================================
        private void SendCommandToMap(string command, JObject? extra = null)
        {
            if (_wsSaveRunning && _wsSaveServer != null)
            {
                var msg = new JObject();
                msg["command"] = command;
                if (extra != null)
                {
                    foreach (var prop in extra.Properties())
                        msg[prop.Name] = prop.Value;
                }
                _wsSaveServer.WebSocketServices["/"]?.Sessions?.Broadcast(msg.ToString(Formatting.None));
                AppendLog($"Command '{command}' sent to map.");
            }
            else
            {
                AppendLog($"Cannot send command '{command}': WebSocket save server is not running.");
            }
        }

        // ================================================================
        // СОХРАНЕНИЕ ТРЕКА
        // ================================================================
        private void SaveTrailFromWebSocket(JObject data)
        {
            try
            {
                Interlocked.Increment(ref _saveRequestsReceived);
                AppendLog("[SAVE] Сервер получил запрос сохранения трека.");
                string tracksDir = AppDataPaths.SavedTracksDirectory;
                if (!Directory.Exists(tracksDir))
                    Directory.CreateDirectory(tracksDir);

                string format = data["format"]?.Value<string>() ?? "";
                string compactData = data["data"]?.Value<string>() ?? "";
                string requestId = data["requestId"]?.Value<string>() ?? "-";
                if (requestId != "-")
                {
                    lock (_saveRequestLock)
                    {
                        if (!_processedSaveRequestIds.Add(requestId))
                        {
                            AppendLog($"[SAVE] Повторный запрос пропущен: requestId={requestId}");
                            return;
                        }

                        if (_processedSaveRequestIds.Count > 256)
                            _processedSaveRequestIds.Clear();
                    }
                }
                AppendDataLog($"save_trail requestId={requestId} format={format} compactChars={compactData.Length}");
                AppendLog($"[SAVE] Начата обработка трека: format={format}, compactChars={compactData.Length}");
                JObject? meta = data["meta"] as JObject;
                JObject? mapData = data["mapData"] as JObject;
                if (mapData == null) mapData = new JObject();
                if (mapData["cities"] == null) mapData["cities"] = new JArray();
                if (mapData["roads"] == null) mapData["roads"] = new JArray();
                if (data["customTargets"] != null)
                    mapData["customTargets"] = data["customTargets"];
                if (data["pois"] != null)
                    mapData["pois"] = data["pois"];

                string baseName;
                if (meta != null && meta["title"] != null)
                {
                    string title = meta["title"]?.Value<string>() ?? "track";
                    string startPos = "0_0";
                    if (!string.IsNullOrEmpty(compactData))
                    {
                        var lines = compactData.Split('\n');
                        if (lines.Length > 1)
                        {
                            var parts = lines[1].Split(';');
                            if (parts.Length >= 3)
                            {
                                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                                {
                                    startPos = $"{Math.Round(x)}_{Math.Round(z)}";
                                }
                            }
                        }
                    }
                    string dateStr = DateTime.Now.ToString("yyMMdd_HHmm");
                    baseName = $"track_{dateStr}_{startPos}";
                }
                else
                {
                    baseName = $"track_{DateTime.Now:yyyyMMdd_HHmmss}";
                }

                string trackFile = Path.Combine(tracksDir, baseName + ".track");
                File.WriteAllText(trackFile, compactData);

                string metaFile = Path.Combine(tracksDir, baseName + ".meta.json");
                if (meta != null)
                    File.WriteAllText(metaFile, meta.ToString(Formatting.Indented));

                string mapFile = Path.Combine(tracksDir, baseName + ".map.json");
                File.WriteAllText(mapFile, mapData.ToString(Formatting.Indented));

                string html = GenerateTrailHtml(compactData, meta, mapData);
                string htmlFile = Path.Combine(tracksDir, baseName + ".html");
                File.WriteAllText(htmlFile, html);

                string playerPath = Path.Combine(tracksDir, "trail_player.html");
                var localTrackNames = Directory.GetFiles(tracksDir, "track_*.html")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderByDescending(n => n)
                    .ToList();
                File.WriteAllText(playerPath, GenerateTrailPlayerHtml(localTrackNames!));

                AppendDataLog($"track_saved baseName={baseName} compactChars={compactData.Length}");
                AppendLog($"[SAVE] Трек сохранён: {baseName}.track/.meta.json/.map.json/.html");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", $"Трек сохранён: {baseName}.html", ToolTipIcon.Info);

                RefreshTrackList();
                Process.Start(new ProcessStartInfo(htmlFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"[SAVE] Ошибка сохранения трека: {ex.Message}");
                throw;
            }
        }

        // ================================================================
        // РАБОТА СО СПИСКОМ ТРЕКОВ
        // ================================================================
        private void RefreshTrackList()
        {
            if (listTracks.InvokeRequired)
            {
                listTracks.Invoke(new Action(RefreshTrackList));
                return;
            }

            try
            {
                string tracksDir = AppDataPaths.SavedTracksDirectory;
                if (!Directory.Exists(tracksDir))
                {
                    listTracks.Items.Clear();
                    listTracks.Items.Add("(папка с треками не найдена)");
                    return;
                }

                var files = Directory.GetFiles(tracksDir, "*.html")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(name => name.StartsWith("track_"))
                    .OrderByDescending(name => name)
                    .ToList();

                listTracks.Items.Clear();
                if (files.Count == 0)
                {
                    listTracks.Items.Add("(нет сохранённых треков)");
                }
                else
                {
                    foreach (var name in files)
                    {
                        string title = name;
                        string description = "";
                        string metaPath = Path.Combine(tracksDir, name + ".meta.json");
                        try
                        {
                            if (File.Exists(metaPath))
                            {
                                var meta = JObject.Parse(File.ReadAllText(metaPath));
                                title = meta["title"]?.Value<string>() ?? name;
                                description = meta["description"]?.Value<string>() ?? "";
                            }
                        }
                        catch (Exception ex) { AppendLog($"Ошибка чтения метаданных трека {name}: {ex.Message}"); }
                        listTracks.Items.Add(new TrackListEntry { BaseName = name, Title = title, Description = description });
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка обновления списка треков: {ex.Message}");
            }
        }

        private void OpenSelectedTrack()
        {
            var selected = listTracks.SelectedItems.Count > 0 ? listTracks.SelectedItems[0] as TrackListEntry : null;
            if (selected == null) return;
            string tracksDir = AppDataPaths.SavedTracksDirectory;
            string baseName = Path.GetFileName(selected.BaseName);
            string htmlPath = Path.Combine(tracksDir, baseName + ".html");
            try
            {
                if (!File.Exists(htmlPath))
                {
                    AppendLog($"Файл трека не найден: {htmlPath}");
                    return;
                }
                if (!_staticWebRunning && !StartStaticWebServer()) return;
                string trackUrl = $"http://localhost:8082/saved_tracks/{Uri.EscapeDataString(baseName + ".html")}";
                Process.Start(new ProcessStartInfo(trackUrl) { UseShellExecute = true });
                AppendLog($"Открыт трек через HTTP: {trackUrl}");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия трека: {ex.Message}");
            }
        }

        private void OpenTracksFolder()
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.SavedTracksDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppDataPaths.SavedTracksDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия папки треков: {ex.Message}");
            }
        }

        private void DeleteSelectedTracks()
        {
            var selectedNames = listTracks.SelectedItems
                .Cast<object>()
                .OfType<TrackListEntry>()
                .Select(entry => Path.GetFileName(entry.BaseName))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (selectedNames.Count == 0) return;

            string message = selectedNames.Count == 1
                ? "Удалить выбранную запись трека?"
                : $"Удалить выбранные записи треков ({selectedNames.Count})?";
            if (MessageBox.Show(message, "Удаление записей", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                string tracksDir = AppDataPaths.SavedTracksDirectory;
                string[] extensions = { ".html", ".track", ".meta.json", ".map.json" };
                int deleted = 0;

                foreach (string baseName in selectedNames)
                {
                    foreach (string extension in extensions)
                    {
                        string path = Path.Combine(tracksDir, baseName + extension);
                        if (!File.Exists(path)) continue;
                        File.Delete(path);
                        deleted++;
                    }
                }

                var remainingTracks = Directory.GetFiles(tracksDir, "track_*.html")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderByDescending(name => name)
                    .ToList();
                File.WriteAllText(Path.Combine(tracksDir, "trail_player.html"), GenerateTrailPlayerHtml(remainingTracks!));

                AppendLog($"Удалено файлов записей: {deleted}.");
                RefreshTrackList();
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка удаления записей: {ex.Message}");
                MessageBox.Show($"Не удалось удалить записи:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================================================================
        // ОСТАЛЬНЫЕ МЕТОДЫ (загрузка плагинов, серверов, индикаторы)
        // ================================================================

        private bool CheckPlugins()
        {
            AppendLog("Checking ETS2 plugins...");
            string ets2Path = GetEts2Path();
            if (string.IsNullOrEmpty(ets2Path))
            {
                AppendLog("ETS2 installation not found.");
                return false;
            }
            string pluginsDir = Path.Combine(ets2Path, "bin", "win_x64", "plugins");

            // Старый ets2-telemetry-server больше не нужен.
            // На переходном этапе принимаем TruckTel, а целевой вариант — наш ets2_assist_plugin.dll.
            bool hasAssistPlugin = File.Exists(Path.Combine(pluginsDir, "ets2_assist_plugin.dll"));
            bool hasTruckTel = File.Exists(Path.Combine(pluginsDir, "trucktel.dll"));
            bool hasSdkController = File.Exists(Path.Combine(pluginsDir, "ets2_assist_input.dll"));
            bool hasTelemetryProvider = hasAssistPlugin || hasTruckTel;

            if (!hasTelemetryProvider || !hasSdkController)
            {
                string missing = "";
                if (!hasTelemetryProvider) missing += "ets2_assist_plugin.dll (или trucktel.dll на время миграции), ";
                if (!hasSdkController) missing += "ets2_assist_input.dll, ";
                missing = missing.TrimEnd(' ', ',');

                DialogResult result = MessageBox.Show(
                    $"Some plugins are missing: {missing}. Install them now?",
                    "Plugins Missing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.Yes)
                {
                    string sourcePlugins = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugins");
                    if (Directory.Exists(sourcePlugins))
                    {
                        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
                        // Копируем все файлы из sourcePlugins
                        foreach (var file in Directory.GetFiles(sourcePlugins))
                        {
                            string dest = Path.Combine(pluginsDir, Path.GetFileName(file));
                            File.Copy(file, dest, true);
                            AppendLog($"Copied {Path.GetFileName(file)} to plugins.");
                        }
                        AppendLog("All plugins copied.");
                        return true;
                    }
                    else
                    {
                        AppendLog("Source plugins folder not found.");
                        return false;
                    }
                }
                else
                {
                    AppendLog("Plugin installation skipped.");
                    return false;
                }
            }
            AppendLog(hasAssistPlugin
                ? "Our ETS2 Assist native plugin is present."
                : "TruckTel is present (temporary telemetry provider during native-plugin migration).");
            AppendLog("ets2-telemetry-server.dll is no longer required.");
            return true;
        }

        private bool ArePluginsInstalled()
        {
            string ets2Path = GetEts2Path();
            if (string.IsNullOrEmpty(ets2Path)) return false;
            string pluginsDir = Path.Combine(ets2Path, "bin", "win_x64", "plugins");
            bool hasAssistPlugin = File.Exists(Path.Combine(pluginsDir, "ets2_assist_plugin.dll"));
            bool hasTruckTel = File.Exists(Path.Combine(pluginsDir, "trucktel.dll"));
            bool hasSdkController = File.Exists(Path.Combine(pluginsDir, "ets2_assist_input.dll"));
            return (hasAssistPlugin || hasTruckTel) && hasSdkController;
        }

   
        private string? GetEts2Path()
        {
            string[] commonPaths = {
                @"C:\Program Files\Steam\steamapps\common\Euro Truck Simulator 2",
                @"C:\Program Files (x86)\Steam\steamapps\common\Euro Truck Simulator 2",
                @"D:\Steam\steamapps\common\Euro Truck Simulator 2",
                @"E:\Steam\steamapps\common\Euro Truck Simulator 2",
                @"F:\Steam\steamapps\common\Euro Truck Simulator 2"
            };
            foreach (var p in commonPaths)
                if (Directory.Exists(p)) return p;

            try
            {
                string? steamPath = null;
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                        steamPath = key.GetValue("InstallPath") as string;
                }
                if (string.IsNullOrEmpty(steamPath))
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                            steamPath = key.GetValue("SteamPath") as string;
                    }
                }
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string testPath = Path.Combine(steamPath, "steamapps", "common", "Euro Truck Simulator 2");
                    if (Directory.Exists(testPath)) return testPath;
                }
            }
            catch { }

            try
            {
                string? steamInstall = null;
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                        steamInstall = key.GetValue("InstallPath") as string;
                }
                if (string.IsNullOrEmpty(steamInstall))
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                            steamInstall = key.GetValue("SteamPath") as string;
                    }
                }
                if (!string.IsNullOrEmpty(steamInstall))
                {
                    string vdfPath = Path.Combine(steamInstall, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdfPath))
                    {
                        string content = File.ReadAllText(vdfPath);
                        var matches = System.Text.RegularExpressions.Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            string libPath = m.Groups[1].Value;
                            string testPath = Path.Combine(libPath, "steamapps", "common", "Euro Truck Simulator 2");
                            if (Directory.Exists(testPath)) return testPath;
                        }
                    }
                }
            }
            catch { }

            return null;
        }


        // Legacy method retained for source compatibility; intentionally disabled.
        private bool StartTelemetryServer()
        {
            AppendLog("[Telemetry] Legacy Ets2Telemetry.exe is disabled.");
            return true;
        }


        private bool StartPythonServer()
        {
            return StartStaticWebServer();
        }

        private bool StartStaticWebServer()
        {
            try
            {
                StopStaticWebServer();
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(root))
                {
                    AppendLog($"Static web root not found: {root}");
                    return false;
                }

                _staticWebListener = new HttpListener();
                _staticWebListener.Prefixes.Add("http://localhost:8082/");
                _staticWebListener.Start();
                _staticWebRunning = true;
                _staticWebThread = new Thread(() => StaticWebLoop(root)) { IsBackground = true, Name = "ETS2AssistStaticWeb" };
                _staticWebThread.Start();
                AppendLog($"Web server started on port 8082 (stable overlay URLs, asset cache epoch={_webCacheEpoch}).");
                LogWebRuntimeFingerprint(root);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start no-cache web server: {ex.Message}");
                try { _staticWebListener?.Close(); } catch { }
                _staticWebListener = null;
                _staticWebRunning = false;
                return false;
            }
        }

        private void StopStaticWebServer()
        {
            _staticWebRunning = false;
            try { _staticWebListener?.Stop(); } catch { }
            try { _staticWebListener?.Close(); } catch { }
            _staticWebListener = null;
            try { _staticWebThread?.Join(300); } catch { }
            _staticWebThread = null;
        }

        private void StaticWebLoop(string root)
        {
            while (_staticWebRunning && _staticWebListener != null)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = _staticWebListener.GetContext();
                }
                catch
                {
                    if (!_staticWebRunning) break;
                    continue;
                }

                _ = Task.Run(() => ProcessStaticRequest(context, root));
            }
        }

        private void ProcessStaticRequest(HttpListenerContext context, string root)
        {
            try
            {
                string urlPath = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
                if (urlPath == "/") urlPath = "/web_ui_hybrid.html";
                urlPath = urlPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string tracksFull = Path.GetFullPath(AppDataPaths.SavedTracksDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string? userFilePath = urlPath switch
                {
                    "web_data.json" => AppDataPaths.WebDataFile,
                    _ => null
                };
                if (userFilePath == null && urlPath.StartsWith("saved_tracks" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.GetFullPath(Path.Combine(AppDataPaths.SavedTracksDirectory, urlPath.Substring("saved_tracks".Length + 1)));
                    if (candidate.StartsWith(tracksFull, StringComparison.OrdinalIgnoreCase))
                        userFilePath = candidate;
                }
                string filePath = userFilePath ?? Path.GetFullPath(Path.Combine(root, urlPath));

                 bool isUserFile = userFilePath != null;
                 if ((!isUserFile && !filePath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) || !File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    byte[] notFound = Encoding.UTF8.GetBytes("File not found");
                    context.Response.ContentLength64 = notFound.Length;
                    context.Response.OutputStream.Write(notFound, 0, notFound.Length);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(filePath);
                string contentType = GetStaticMimeType(filePath);

                // Keep WebOverlay window URLs stable for saved geometry, but rewrite every local
                // JS/CSS reference in HTML to a unique epoch query string for this application run.
                if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    string html = Encoding.UTF8.GetString(bytes);
                    html = AddEpochCacheBusterToLocalAssets(html, _webCacheEpoch);
                    bytes = Encoding.UTF8.GetBytes(html);
                }

                context.Response.StatusCode = 200;
                context.Response.ContentType = contentType;
                context.Response.ContentEncoding = Encoding.UTF8;
                context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";
                context.Response.Headers["X-ETS2-Assist-Build"] = BuildInfo.Version;
                context.Response.Headers["X-ETS2-Assist-Cache-Epoch"] = _webCacheEpoch.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                try
                {
                    context.Response.StatusCode = 500;
                    byte[] error = Encoding.UTF8.GetBytes(ex.Message);
                    context.Response.ContentLength64 = error.Length;
                    context.Response.OutputStream.Write(error, 0, error.Length);
                }
                catch { }
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private static string AddEpochCacheBusterToLocalAssets(string html, long epochSeconds)
        {
            const string pattern = "(?<prefix>\\b(?:src|href)\\s*=\\s*[\"'])(?<url>(?!https?://)(?!data:)(?!#)[^\"']+\\.(?:js|css)(?:\\?[^\"']*)?)(?<quote>[\"'])";
            return Regex.Replace(
                html,
                pattern,
                match =>
                {
                    string url = match.Groups["url"].Value;
                    int q = url.IndexOf('?');
                    if (q >= 0)
                        url = url[..q];
                    url += $"?t={epochSeconds}";
                    return match.Groups["prefix"].Value + url + match.Groups["quote"].Value;
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private void LogWebRuntimeFingerprint(string root)
        {
            foreach (string fileName in new[] { "web_pda_map.html", "web_ui_hybrid.html" })
            {
                try
                {
                    string path = Path.Combine(root, fileName);
                    if (!File.Exists(path))
                    {
                        AppendLog($"[WEB] {fileName}: NOT FOUND at {path}");
                        continue;
                    }

                    string text = File.ReadAllText(path, Encoding.UTF8);
                    Match buildMatch = Regex.Match(text, @"1\.0\.[0-9]+[-A-Z0-9._]*", RegexOptions.IgnoreCase);
                    string build = buildMatch.Success ? buildMatch.Value : "unknown";
                    AppendLog($"[WEB] {fileName}: build={build}, bytes={new FileInfo(path).Length}, lastWrite={File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[WEB] {fileName}: fingerprint error: {ex.Message}");
                }
            }
        }

        private static string GetStaticMimeType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".txt" => "text/plain; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".webp" => "image/webp",
                ".wasm" => "application/wasm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
        }

        // ================================================================
        // КОНФИГУРАЦИЯ ПОЗИЦИЙ ОВЕРЛЕЕВ (WebOverlay)
        // При первом запуске системы, если файлов позиций ещё нет,
        // вычисляем и создаём их для экрана, где показано окно игры.
        // Формат файла (по weboverlay/Program.cs): X, Y, zoom, Width, Height.
        // ================================================================
        private Screen GetGameScreen()
        {
            try
            {
                foreach (var name in new[] { "eurotrucks2", "amtrucks2" })
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            var s = Screen.FromHandle(p.MainWindowHandle);
                            if (s != null) return s;
                        }
                    }
                }
            }
            catch { }
            return Screen.PrimaryScreen;
        }

        private static string OverlayStateFileName(string url)
        {
            string safe = string.Join("_", url.Split(Path.GetInvalidFileNameChars()));
            if (safe.Length > 200) safe = safe.Substring(0, 200);
            return safe + ".txt";
        }

        private static void ComputeOverlayGeometry(string url, System.Drawing.Rectangle wa, out int x, out int y, out int w, out int h)
        {
            // Доля площади экрана от WorkingArea. Площадь ~ frac, значит сторона ~ sqrt(frac).
            double frac;
            string kind = url.Contains("web_pda_map") ? "map"
                        : url.Contains("web_ui_hybrid") ? "hybrid"
                        : url.Contains("web_pause_logo") ? "logo" : "map";
            switch (kind)
            {
                case "map": // миникарта: левый нижний угол, квадрат, на 30% больше (~6% площади)
                    frac = 0.06;
                    int side = (int)Math.Round(Math.Min(wa.Width, wa.Height) * Math.Sqrt(frac) * 1.3);
                    w = side;
                    h = side;
                    x = wa.X;
                    y = wa.Bottom - h;
                    break;
                case "hybrid": // гибрид: по центру по горизонтали, на 15% ниже нижнего края
                    frac = 0.20;
                    w = (int)Math.Round(wa.Width * Math.Sqrt(frac));
                    h = (int)Math.Round(wa.Height * Math.Sqrt(frac));
                    x = wa.X + (wa.Width - w) / 2;
                    y = wa.Bottom - h + (int)(wa.Height * 0.15);
                    break;
                default: // pause_logo: левый верхний угол, на 15% больше (~3% площади), квадрат
                    frac = 0.03;
                    int logoSide = (int)Math.Round(Math.Min(wa.Width, wa.Height) * Math.Sqrt(frac) * 1.15);
                    w = logoSide;
                    h = logoSide;
                    x = wa.X;
                    y = wa.Y;
                    break;
            }
        }

        private void EnsureOverlayWindowConfig()
        {
            try
            {
                string configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WebOverlay", "config");
                var screen = GetGameScreen();
                var wa = screen.WorkingArea;
                var urls = new[]
                {
                    "http://localhost:8082/web_pda_map.html",
                    "http://localhost:8082/web_ui_hybrid.html",
                    "http://localhost:8082/web_pause_logo.html"
                };
                foreach (var url in urls)
                {
                    string path = Path.Combine(configDir, OverlayStateFileName(url));
                    if (File.Exists(path)) continue;
                    ComputeOverlayGeometry(url, wa, out int x, out int y, out int w, out int h);
                    Directory.CreateDirectory(configDir);
                    File.WriteAllLines(path, new[] { x.ToString(), y.ToString(), "1", w.ToString(), h.ToString() });
                    AppendLog($"[OVERLAY] Создан файл позиции {Path.GetFileName(path)}: X={x}, Y={y}, {w}x{h} (экран {wa.Width}x{wa.Height})");
                }
                AppendLog("[OVERLAY] Конфигурация позиций оверлеев проверена/создана.");
            }
            catch (Exception ex)
            {
                AppendLog($"[OVERLAY] Ошибка создания конфигурации позиций: {ex.Message}");
            }
        }

        private void StartWebOverlay()
        {
            string overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            if (!File.Exists(overlayExe))
                overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "pano.exe");
            if (!File.Exists(overlayExe))
            {
                AppendLog("WebOverlay executable not found.");
                return;
            }
            try
            {
                // При первом запуске — создать файлы позиций оверлеев (если их нет).
                EnsureOverlayWindowConfig();

                // Remove stale overlay host processes so old URL instances cannot remain stacked.
                foreach (var proc in Process.GetProcessesByName("WebOverlay"))
                {
                    try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                }
                foreach (var proc in Process.GetProcessesByName("pano"))
                {
                    try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                }

                // Keep URLs stable: WebOverlay persists position/size by URL.
                string urlMain = "http://localhost:8082/web_ui_hybrid.html";
                string urlPda = "http://localhost:8082/web_pda_map.html";
                string urlPauseLogo = "http://localhost:8082/web_pause_logo.html";
                Process.Start(overlayExe, urlMain);
                AppendLog("Main overlay started.");
                System.Threading.Thread.Sleep(500);
                Process.Start(overlayExe, $"append {urlPda}");
                AppendLog("PDA map overlay appended.");
                System.Threading.Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlPauseLogo}");
                AppendLog("Pause logo overlay appended.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start overlay: {ex.Message}");
            }
        }

        private void StopSystem()
        {
            AppendLog("Stopping system...");
            procManager.Stop();
            _pauseCheckTimer?.Stop();
            StopTriggerServer();
            StopWebSocketSaveServer();
            StopStaticWebServer();
            KillChildProcesses();

            // ===== ОСВОБОЖДЕНИЕ SCS CONTROLLER =====
            SCSController.OnLog -= (msg) => AppendLog(msg); // отписка
            SCSController.Dispose();

            AppendLog("System stopped.");
            RefreshUI();
        }

        private void KillChildProcesses()
        {
            bool gameRunning = IsEts2ProcessRunning();

            string[] processNames = { "Ets2Telemetry", "python", "pythonw", "WebOverlay", "pano" };
            foreach (var name in processNames)
            {
                if (gameRunning && name == "Ets2Telemetry")
                {
                    AppendLog($"Игра запущена, процесс {name} не убиваем.");
                    continue;
                }
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        proc.Kill();
                        AppendLog($"Процесс {name} (PID {proc.Id}) убит.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Ошибка при убийстве {name}: {ex.Message}");
                }
            }
            AppendLog("KillChildProcesses завершён.");
        }

        private bool IsEts2ProcessRunning()
        {
            try { return Process.GetProcessesByName("eurotrucks2").Length > 0; }
            catch { return false; }
        }

        // ================================================================
        // ОПРЕДЕЛЕНИЕ ПОРТА TRUCK TEL ИЗ АКТИВНЫХ TCP-СОЕДИНЕНИЙ
        // ================================================================
        private void UpdateTruckTelPort()
        {
            try
            {
                int? port = GetTruckTelPortFromProcess();
                if (port.HasValue)
                {
                    AppendLog($"[TruckTel] Найден порт через активные соединения: {port.Value}");
                    string webDataPath = AppDataPaths.WebDataFile;
                    JObject webData;
                    if (File.Exists(webDataPath))
                    {
                        string json = File.ReadAllText(webDataPath);
                        webData = JObject.Parse(json);
                    }
                    else
                    {
                        webData = new JObject();
                    }
                    webData["wsPort"] = port.Value;
                    Directory.CreateDirectory(AppDataPaths.UserDataDirectory);
                    File.WriteAllText(webDataPath, webData.ToString(Formatting.Indented));
                    AppendLog($"[TruckTel] web_data.json обновлён, wsPort = {port.Value}");
                }
                else
                {
                    AppendLog("[TruckTel] Порт не найден через активные соединения, используется 8080");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[TruckTel] Ошибка: {ex.Message}");
            }
        }

        private int? GetTruckTelPortFromProcess()
        {
            try
            {
                var gameProcesses = Process.GetProcessesByName("eurotrucks2");
                if (gameProcesses.Length == 0) return null;

                var gamePid = gameProcesses[0].Id;

                string script = $@"
                    Get-NetTCPConnection -State Listen |
                    Where-Object {{ $_.OwningProcess -eq {gamePid} }} |
                    Select-Object -ExpandProperty LocalPort
                ";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                process.WaitForExit(3000);

                if (process.ExitCode == 0)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    var ports = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.Trim())
                                      .Where(p => int.TryParse(p, out _))
                                      .Select(int.Parse)
                                      .ToList();

                    if (ports.Contains(8080))
                        return 8080;
                    else if (ports.Count > 0)
                        return ports.First();
                }
                else
                {
                    string error = process.StandardError.ReadToEnd();
                    AppendLog($"[TruckTel] PowerShell error: {error}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[TruckTel] GetTruckTelPortFromProcess error: {ex.Message}");
            }
            return null;
        }

        // ================================================================
        // УПРАВЛЕНИЕ ПОЯВЛЕНИЕМ UI В ЗАВИСИМОСТИ ОТ ПАУЗЫ (методы в WebUIManager.cs)
        // ================================================================
        // StartPauseCheck, CheckPauseAndUpdateUI, IsGamePausedAsync – определены в WebUIManager.cs (partial)

        // ================================================================
        // ОБРАБОТКА ГОРЯЧИХ КЛАВИШ
        // ================================================================
        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_SAVE:
                        AppendLog("Hotkey save (Shift+Ctrl+S) pressed: requesting pause, then saving.");
                        _ = TriggerTrailSaveAsync();
                        break;
                    case HOTKEY_START_REC:
                        AppendLog("Hotkey start recording (Shift+Ctrl+R)");
                        SendCommandToMap("start_recording");
                        break;
                    case HOTKEY_STOP_REC:
                        AppendLog("Hotkey stop recording (Shift+Ctrl+X)");
                        SendCommandToMap("stop_recording");
                        break;
                    case HOTKEY_MARKER:
                        if (IsGamePaused())
                        {
                            AppendLog("Hotkey marker (Shift+Ctrl+N)");
                            SendCommandToMap("add_marker");
                        }
                        else
                        {
                            AppendLog("Hotkey marker ignored: game is not paused.");
                        }
                        break;
                    case HOTKEY_TEST:
                        AppendLog("Hotkey test (Shift+Ctrl+T) - showing test window");
                        ShowTestWindow();
                        break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        private bool IsGamePaused()
{
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    // TruckTel-compatible direct REST endpoint.
                    // The native ETS2 Assist plugin will expose the same API.
                    var response = client.GetAsync("http://localhost:8080/api/rest/single/frame/paused").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var json = response.Content.ReadAsStringAsync().Result.Trim();
                        var parsed = ParsePausedResponse(json);
                        if (parsed.HasValue) return parsed.Value;
                        return _pausedIntent;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"IsGamePaused error: {ex.Message}");
            }
            return _pausedIntent;
        }

        // Универсальный разбор ответа эндпоинта паузы. Поддерживает:
        // "true"/true, "false"/false, {"paused":...}, числовое значение.
        // Возвращает null, если ответ вообще недоступен (вызывающий решает сам).
        private static bool? ParsePausedResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            string s = json.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2);
            if (bool.TryParse(s, out bool b)) return b;
            try
            {
                var t = JToken.Parse(json.Trim());
                if (t.Type == JTokenType.Boolean) return t.Value<bool>();
                if (t.Type == JTokenType.String && bool.TryParse(t.Value<string>(), out b)) return b;
                if (t.Type == JTokenType.Integer) return t.Value<int>() != 0;
                if (t.Type == JTokenType.Object)
                {
                    var p = t["paused"] ?? t["pause"] ?? t["isPaused"];
                    if (p != null) return ParsePausedResponse(p.ToString());
                }
            }
            catch { }
            return null;
        }

        private async Task TriggerTrailSaveAsync()
        {
            try
            {
                bool paused = await IsGamePausedAsync();
                if (!paused)
                {
                    AppendLog("[SAVE] ETS2 не на паузе. Отправляем PAUSE через SCS SDK и ждём фактическую паузу.");
                    if (!SCSController.SetPause(true))
                    {
                        AppendLog("[SAVE] Команда PAUSE не подтверждена. Сохранение отменено.");
                        trayIcon.ShowBalloonTip(2500, "ETS2 Assist", "Не удалось поставить ETS2 на паузу.", ToolTipIcon.Error);
                        return;
                    }

                    DateTime deadline = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(100);
                        if (await IsGamePausedAsync())
                        {
                            paused = true;
                            break;
                        }
                    }
                }

                if (!paused)
                {
                    AppendLog("[SAVE] Фактическая пауза не подтверждена за 4 с. Сохранение отменено.");
                    trayIcon.ShowBalloonTip(2500, "ETS2 Assist", "ETS2 не подтвердил паузу.", ToolTipIcon.Error);
                    return;
                }

                string saveRequestId = $"save_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                if (TryBroadcastMapCommand("save_trail", new JObject { ["requestId"] = saveRequestId }))
                {
                    AppendLog($"[SAVE] Команда save_trail отправлена на карту после подтверждения паузы (requestId={saveRequestId}).");
                    trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Начало сохранения трека.", ToolTipIcon.Info);
                    return;
                }

                string triggerPath = AppDataPaths.TriggerFile;
                File.WriteAllText(triggerPath, saveRequestId);
                AppendLog($"[SAVE] Карта не подключена — создан fallback trigger: {triggerPath}");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Начало сохранения трека (fallback).", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppendLog($"[SAVE] Trail save error: {ex.Message}");
            }
        }

        private bool TryBroadcastMapCommand(string command, JObject? extra = null)
        {
            try
            {
                if (!_wsSaveRunning || _wsSaveServer == null) return false;
                var sessions = _wsSaveServer.WebSocketServices["/"]?.Sessions;
                if (sessions == null || sessions.Count <= 0)
                {
                    AppendLog($"[SAVE] Нет подключённых web-клиентов для команды '{command}'.");
                    return false;
                }
                var msg = new JObject { ["command"] = command };
                if (extra != null) foreach (var prop in extra.Properties()) msg[prop.Name] = prop.Value;
                sessions.Broadcast(msg.ToString(Formatting.None));
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"[SAVE] Ошибка отправки команды '{command}': {ex.Message}");
                return false;
            }
        }

        private void ShowTestWindow()
        {
            Form testForm = new Form
            {
                Text = "Тестовое окно",
                Size = new Size(400, 300),
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                BackColor = Color.FromArgb(30, 30, 40),
                ForeColor = Color.White,
                FormBorderStyle = FormBorderStyle.Sizable,
                Icon = this.Icon
            };

            Button btnClose = new Button
            {
                Text = "Закрыть",
                Location = new Point(10, 10),
                Size = new Size(100, 30)
            };
            btnClose.Click += (s, e) => testForm.Close();

            Label lblInfo = new Label
            {
                Text = "Тестовое окно\nЗдесь будут тестовые кнопки и функционал.",
                Location = new Point(10, 50),
                AutoSize = true,
                ForeColor = Color.White
            };

            testForm.Controls.Add(btnClose);
            testForm.Controls.Add(lblInfo);
            testForm.ShowDialog(this);
        }

        // ================================================================
        // ОБРАБОТЧИКИ МЕНЮ, НАСТРОЙКИ, ПОМОЩЬ И Т.Д.
        // ================================================================
        private void OpenSettings()
        {
            var settingsForm = new SettingsForm();
            settingsForm.ShowDialog(this);
            ApplyLanguage();
            UpdateIndicators();
            LoadRecordingSettings();
        }

        private void ShowHelp()
        {
            try
            {
                string helpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
                if (File.Exists(helpPath))
                    Process.Start("notepad.exe", helpPath);
                else
                    Process.Start("https://github.com/zvukoper/ets2_assist");
            }
            catch (Exception ex)
            {
                AppendLog($"Help error: {ex.Message}");
                MessageBox.Show(
                    lang.Get("help_error") ?? "Failed to open help.",
                    lang.Get("help_title") ?? "Help",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async void CheckUpdates()
        {
            AppendLog("=== CheckUpdates started ===");
            try
            {
                string apiUrl = AppSettings.GitHubRepoUrl;
                AppendLog($"GitHub API URL: {apiUrl}");
                Version latestVersion = await Updater.CheckLatestVersion(apiUrl);
                AppendLog($"Latest version from server: {latestVersion}");
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                AppendLog($"Current application version: {currentVersion}");
                if (latestVersion > currentVersion)
                {
                    DialogResult result = MessageBox.Show(
                        string.Format(lang.Get("update_available") ?? "New version {0} available. Open download page?", latestVersion),
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information
                    );
                    if (result == DialogResult.Yes)
                    {
                        Process.Start("https://github.com/zvukoper/ets2_assist/releases");
                        AppendLog("User opened download page.");
                    }
                }
                else
                {
                    MessageBox.Show(
                        string.Format(lang.Get("update_no_updates") ?? "You have the latest version ({0}).", currentVersion),
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                AppendLog($"EXCEPTION in CheckUpdates: {ex.Message}");
                MessageBox.Show(
                    lang.Get("update_check_error") ?? $"Failed to check updates: {ex.Message}",
                    lang.Get("update_check_title") ?? "Check Updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                AppendLog("=== CheckUpdates finished ===");
            }
        }

        private void ConfirmExit()
        {
            if (MessageBox.Show(
                lang.Get("exit_confirm") ?? "Are you sure you want to exit?",
                lang.Get("exit_title") ?? "Exit",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                StopSystem();
                if (hotKeyRegistered)
                {
                    UnregisterHotKey(this.Handle, HOTKEY_SAVE);
                    UnregisterHotKey(this.Handle, HOTKEY_START_REC);
                    UnregisterHotKey(this.Handle, HOTKEY_STOP_REC);
                    UnregisterHotKey(this.Handle, HOTKEY_MARKER);
                    UnregisterHotKey(this.Handle, HOTKEY_TEST);
                }
                trayIcon.Visible = false;
                Application.Exit();
            }
        }

        private async void RestartOverlay()
        {
            if (!procManager.IsRunning)
            {
                AppendLog("Система остановлена. Запускаем полный pipeline перед стартом overlay.");
                await StartSystemAsync();
                return;
            }

            AppendLog("Restarting overlay...");
            await Task.Run(KillOverlayProcesses);
            AppendLog("Overlay processes killed. Restarting...");
            if (!_staticWebRunning)
            {
                if (!StartStaticWebServer()) return;
            }
            StartWebOverlay();
        }

        private void KillOverlayProcesses()
        {
            foreach (string name in new[] { "WebOverlay", "pano" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Ошибка остановки overlay {name}: {ex.Message}");
                    }
                }
            }
        }

        private void OpenLogFolder()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        }

        private void AppendLog(string msg, bool persistWorkflow = true)
        {
            if (persistWorkflow && logger != null)
                logger.Workflow(msg);

            if (logConsole.InvokeRequired)
                logConsole.Invoke(new Action(() => AppendLog(msg, false)));
            else
            {
                logConsole.AppendText(msg + Environment.NewLine);
                logConsole.ScrollToCaret();
            }
        }

        private void AppendDataLog(string msg)
        {
            if (logger != null)
                logger.Data(msg);
        }

        private void UpdateStartButton()
        {
            if (procManager.IsRunning)
            {
                btnStart.Text = lang.Get("btn_stop") ?? "Stop";
                btnStart.BackColor = Color.LightCoral;
            }
            else
            {
                btnStart.Text = lang.Get("btn_start") ?? "Start";
                btnStart.BackColor = Color.LightGreen;
            }
        }

        private void UpdateIndicators()
        {
            bool arduino = !string.IsNullOrEmpty(GetArduinoPort());
            bool plugins = ArePluginsInstalled();
            bool webOverlay = Process.GetProcessesByName("WebOverlay").Length > 0 ||
                              Process.GetProcessesByName("pano").Length > 0;
            bool mainScript = procManager.IsRunning;
            bool gameRunning = IsGameRunning();
            bool dataFresh = IsTelemetryDataFresh() && gameRunning;
            int speed = GetCurrentSpeed();

            SetStatusText(indicatorEts2Assist, "ETS2 Assist",
                mainScript ? "ON" : "OFF", mainScript);
            SetStatusText(indicatorEts2, "ETS2",
                gameRunning ? "RUNNING" : "NOT RUNNING", gameRunning);
            SetStatusText(indicatorEts2Plugins, "ETS2 Plugins",
                plugins ? "INSTALLED" : "NOT INSTALLED", plugins);
            if (gameRunning && dataFresh)
            {
                string speedText = speed >= 0 ? $"{speed} km/h" : "0 km/h";
                SetStatusText(indicatorTruckTel, "TruckTel 8080", speedText, true);
            }
            else
            {
                SetStatusText(indicatorTruckTel, "TruckTel 8080", "NO DATA", false);
            }
            SetStatusText(indicatorWebServer, "Web Server",
                _staticWebRunning ? "8082 ON" : "8082 OFF", _staticWebRunning);
            SetStatusText(indicatorWebOverlay, "Web Overlay",
                webOverlay ? "ON" : "OFF", webOverlay);
            SetStatusText(indicatorArduino, "Arduino",
                arduino ? "CONNECTED" : "NONE", arduino);
        }

        private void SetStatusText(Label lbl, string baseText, string statusText, bool isActive)
        {
            if (lbl.InvokeRequired)
            {
                lbl.Invoke(new Action(() => SetStatusText(lbl, baseText, statusText, isActive)));
                return;
            }
            lbl.Text = baseText + ": " + statusText;
            lbl.ForeColor = isActive ? Color.Green : Color.Gray;
            lbl.Font = isActive ? new Font(lbl.Font, FontStyle.Bold) : new Font(lbl.Font, FontStyle.Regular);
        }

        private bool IsTelemetryConnected()
        {
            try
            {
                string jsonPath = AppDataPaths.WebDataFile;
                if (!File.Exists(jsonPath)) return false;
                var json = File.ReadAllText(jsonPath);
                var obj = JObject.Parse(json);
                return obj["game"]?["connected"]?.Value<bool>() ?? false;
            }
            catch { return false; }
        }

        private bool IsTelemetryDataFresh()
        {
            try
            {
                string jsonPath = AppDataPaths.WebDataFile;
                if (!File.Exists(jsonPath)) return false;
                var lastWrite = File.GetLastWriteTime(jsonPath);
                return (DateTime.Now - lastWrite).TotalSeconds < 5;
            }
            catch { return false; }
        }

        private int GetCurrentSpeed()
        {
            try
            {
                string jsonPath = AppDataPaths.WebDataFile;
                if (!File.Exists(jsonPath)) return -1;
                var json = File.ReadAllText(jsonPath);
                var obj = JObject.Parse(json);
                var speedToken = obj["speed"];
                if (speedToken != null && speedToken.Type == JTokenType.Integer)
                    return speedToken.Value<int>();
                return -1;
            }
            catch { return -1; }
        }

        private bool IsGameRunning()
        {
            try
            {
                return Process.GetProcessesByName("eurotrucks2").Any(p =>
                {
                    try { return !p.HasExited; }
                    catch { return false; }
                    finally { p.Dispose(); }
                });
            }
            catch { return false; }
        }

        private void OpenTelemetryInspector()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://localhost:8082/web_telemetry_inspector.html",
                    UseShellExecute = true
                });
                AppendLog("Открыт telemetry inspector (WebSocket 8080).");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия telemetry inspector: {ex.Message}");
            }
        }

        private string? GetArduinoPort()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                foreach (var obj in searcher.Get())
                {
                    if (obj["Description"]?.ToString()?.Contains("Arduino Micro") == true)
                        return obj["DeviceID"]?.ToString();
                }
                return null;
            }
            catch { return null; }
        }

        // ================================================================
        // ЗАКРЫТИЕ ФОРМЫ
        // ================================================================
        private void ApplySavedWindowBounds()
        {
            const int defaultWidth = 1180;
            const int defaultHeight = 700;

            var savedWidth = AppSettings.WindowWidth.GetValueOrDefault(defaultWidth);
            var savedHeight = AppSettings.WindowHeight.GetValueOrDefault(defaultHeight);
            savedWidth = Math.Max(800, savedWidth);
            savedHeight = Math.Max(500, savedHeight);

            var hasSavedPosition = AppSettings.WindowX.HasValue && AppSettings.WindowY.HasValue;
            if (!hasSavedPosition)
            {
                Size = new Size(savedWidth, savedHeight);
                StartPosition = FormStartPosition.CenterScreen;
                return;
            }

            var savedBounds = new Rectangle(AppSettings.WindowX!.Value, AppSettings.WindowY!.Value, savedWidth, savedHeight);
            var screen = Screen.AllScreens.FirstOrDefault(sc =>
                !string.IsNullOrWhiteSpace(AppSettings.WindowDeviceName) &&
                sc.DeviceName.Equals(AppSettings.WindowDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? Screen.AllScreens.FirstOrDefault(sc => sc.WorkingArea.IntersectsWith(savedBounds))
                ?? Screen.PrimaryScreen;
            var work = screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            savedWidth = Math.Min(savedWidth, work.Width);
            savedHeight = Math.Min(savedHeight, work.Height);
            var x = Math.Clamp(savedBounds.X, work.Left, work.Right - savedWidth);
            var y = Math.Clamp(savedBounds.Y, work.Top, work.Bottom - savedHeight);

            Size = new Size(savedWidth, savedHeight);
            Location = new Point(x, y);
            StartPosition = FormStartPosition.Manual;
        }

        private void SaveWindowBounds()
        {
            if (WindowState == FormWindowState.Minimized) return;
            var bounds = RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            AppSettings.WindowX = bounds.X;
            AppSettings.WindowY = bounds.Y;
            AppSettings.WindowWidth = bounds.Width;
            AppSettings.WindowHeight = bounds.Height;
            AppSettings.WindowDeviceName = Screen.FromRectangle(bounds).DeviceName;
            AppSettings.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (hotKeyRegistered)
            {
                UnregisterHotKey(this.Handle, HOTKEY_SAVE);
                UnregisterHotKey(this.Handle, HOTKEY_START_REC);
                UnregisterHotKey(this.Handle, HOTKEY_STOP_REC);
                UnregisterHotKey(this.Handle, HOTKEY_MARKER);
                UnregisterHotKey(this.Handle, HOTKEY_TEST);
            }
            StopTriggerServer();
            StopWebSocketSaveServer();
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                trayIcon.ShowBalloonTip(1000, "ETS2 Assist", lang.Get("tray_minimized") ?? "Application minimized to tray.", ToolTipIcon.Info);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateIndicators();
        }
    }
}
