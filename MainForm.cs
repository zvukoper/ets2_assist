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
        private Button btnRefreshTracks = null!;
        private Button btnRandomTarget = null!;  // Кнопка "Случайная цель" (привязка в InitializeComponents)

        private RichTextBox logConsole = null!;
        private Panel indicatorsPanel = null!;
        private ListBox listTracks = null!;

        private Label indicatorEts2Assist = null!;
        private Label indicatorEts2 = null!;
        private Label indicatorEts2Plugins = null!;
        private Label indicatorTruckTel = null!;
        private Label indicatorEts2Telemetry = null!;
        private Label indicatorWebServer = null!;
        private Label indicatorWebOverlay = null!;
        private Label indicatorArduino = null!;

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

        // ========== WEBSOCKET-СЕРВЕР ДЛЯ СОХРАНЕНИЯ ТРЕКОВ (порт 8084) ==========
        private WebSocketSharp.Server.WebSocketServer? _wsSaveServer;
        private bool _wsSaveRunning = false;

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

        // ========== УПРАВЛЕНИЕ ПОЯВЛЕНИЕМ UI ==========
        private bool _uiShown = false;
        private System.Windows.Forms.Timer _pauseCheckTimer;

        // ==========================================

        public MainForm()
        {
            try
            {
                var version = Application.ProductVersion ?? "0.0.0";
                this.Text = $"ETS2 Assist v{version}";
            }
            catch
            {
                this.Text = "ETS2 Assist";
            }
            this.Size = new Size(1100, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); } };

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
                AppendLog("Hotkeys registered: S (save), R (start rec), X (stop rec), N (marker), T (test)");
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

            btnRefreshTracks = new Button { Text = "Обновить список", Location = new Point(leftX, topY + 210), Size = new Size(120, 30) };
            btnRefreshTracks.Click += (s, e) => RefreshTrackList();

            // Кнопка "Случайная цель"
            btnRandomTarget = new Button { Text = "Случайная цель", Location = new Point(leftX, topY + 260), Size = new Size(120, 30) };
            btnRandomTarget.Click += BtnRandomTarget_Click; // метод определен в QuestsManager.cs

            int consoleLeft = leftX + 140;
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
            int listWidth = this.ClientSize.Width - listLeft - 200;
            listTracks = new ListBox
            {
                Location = new Point(listLeft, topY),
                Size = new Size(listWidth, this.ClientSize.Height - topY - 40),
                BackColor = Color.FromArgb(20, 25, 35),
                ForeColor = Color.LightGray,
                Font = new Font("Segoe UI", 9),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            listTracks.DoubleClick += (s, e) => OpenSelectedTrack();
            listTracks.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) OpenSelectedTrack(); };

            int indicatorLeft = listLeft + listWidth + 10;
            indicatorsPanel = new Panel
            {
                Location = new Point(indicatorLeft, topY),
                Size = new Size(180, this.ClientSize.Height - topY - 40),
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
            indicatorTop += step;
            indicatorEts2Telemetry = CreateIndicator("ETS2 Telemetry", indicatorTop);
            indicatorTop += step;
            indicatorWebServer = CreateIndicator("Web Server", indicatorTop);
            indicatorTop += step;
            indicatorWebOverlay = CreateIndicator("Web Overlay", indicatorTop);
            indicatorTop += step;
            indicatorArduino = CreateIndicator("Arduino", indicatorTop);

            indicatorsPanel.Controls.AddRange(new Control[] {
                indicatorEts2Assist, indicatorEts2, indicatorEts2Plugins,
                indicatorTruckTel, indicatorEts2Telemetry, indicatorWebServer,
                indicatorWebOverlay, indicatorArduino
            });

            this.Controls.AddRange(new Control[] {
                btnStart, btnStop, btnRestartOverlay, btnMinimize, btnExit, btnRefreshTracks, btnRandomTarget,
                logConsole, listTracks, indicatorsPanel, mainMenu
            });
        }

        private Label CreateIndicator(string labelText, int top)
        {
            Label lbl = new Label
            {
                Text = labelText + ": OFF",
                Location = new Point(5, top),
                Size = new Size(170, 28),
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
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "config.json");
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
            logger.OnLogMessage += (msg) => AppendLog(msg);
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
            this.Text = $"ETS2 Assist v{version}";

            btnStart.Text = lang.Get("ui_start") ?? "Start";
            btnStop.Text = lang.Get("ui_stop") ?? "Stop";
            btnRestartOverlay.Text = lang.Get("ui_restart_overlay") ?? "Restart Overlay";
            btnMinimize.Text = lang.Get("ui_minimize") ?? "Minimize";
            btnExit.Text = lang.Get("ui_exit") ?? "Exit";
            btnRefreshTracks.Text = "Обновить список";
            btnRandomTarget.Text = "Случайная цель";
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

            if (!CheckPlugins())
            {
                AppendLog("Plugins check failed. System start aborted.");
                return;
            }

            if (!StartTelemetryServer())
            {
                AppendLog("Telemetry server start failed. System start aborted.");
                return;
            }

            if (!StartPythonServer())
            {
                AppendLog("Web server start failed. System start aborted.");
                return;
            }

            StartTriggerServer();
            StartWebSocketSaveServer();

            await procManager.StartAsync();

            StartWebOverlay();

            // Запускаем проверку паузы для показа UI
            StartPauseCheck();

            AppendLog("System started successfully.");

            UpdateStartButton();
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

                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/check_trigger")
                {
                    string file = request.QueryString["file"] ?? "save_trail.trigger";
                    string triggerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", file);
                    bool exists = File.Exists(triggerPath);
                    AppendLog($"[HTTP] /check_trigger: file={file}, exists={exists}");
                    string json = JsonConvert.SerializeObject(new { exists = exists });
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/delete_trigger")
                {
                    string file = request.QueryString["file"] ?? "save_trail.trigger";
                    string triggerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", file);
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
                    string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "saved_tracks");
                    if (!Directory.Exists(tracksDir)) Directory.CreateDirectory(tracksDir);
                    var files = Directory.GetFiles(tracksDir, "*.json")
                        .Select(f => Path.GetFileName(f))
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
                    string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "saved_tracks");
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
                else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/update_targets")
                {
                    using var reader = new StreamReader(request.InputStream);
                    var body = reader.ReadToEnd();
                    try
                    {
                        var targets = JArray.Parse(body);
                        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "custom_targets.json");
                        var json = new JObject { ["customTargets"] = targets };
                        File.WriteAllText(filePath, json.ToString(Formatting.Indented));
                        AppendLog("[HTTP] custom_targets.json обновлён.");
                        response.StatusCode = 200;
                        byte[] buffer = Encoding.UTF8.GetBytes("{\"success\":true}");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[HTTP] Ошибка обновления custom_targets.json: {ex.Message}");
                        response.StatusCode = 500;
                        byte[] buffer = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
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

            public static void SetLog(Action<string> log) => _log = log;
            public static void SetOnTrail(Action<JObject> action) => _onTrail = action;
            public static void SetPlaySoundAction(Action<string> action) => _playSoundAction = action;
            public static void SetOnCommand(Action<JObject> action) => _onCommand = action;

            protected override void OnOpen()
            {
                _log?.Invoke("[WebSocket] Клиент карты подключился");
            }


            protected override void OnMessage(MessageEventArgs e)
            {
                try
                {
                    var json = e.Data;
                    _log?.Invoke($"[WebSocket] Получено сообщение: {json.Substring(0, Math.Min(json.Length, 200))}...");
                    var data = JObject.Parse(json);

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
                    _onTrail?.Invoke(data);
                    Send(JsonConvert.SerializeObject(new { status = "ok", message = "Трек получен" }));
                    _log?.Invoke("[WebSocket] Ответ отправлен клиенту");
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[WebSocket] Ошибка обработки: {ex.Message}");
                    Send(JsonConvert.SerializeObject(new { status = "error", message = ex.Message }));
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
                TrailBehavior.SetOnCommand(data => OnClientCommand(data)); // метод в QuestsManager

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
        // СОХРАНЕНИЕ ТРЕКА (новый компактный формат)

        private void SaveTrailFromWebSocket(JObject data)
        {
            try
            {
                string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "saved_tracks");
                if (!Directory.Exists(tracksDir))
                    Directory.CreateDirectory(tracksDir);

                string format = data["format"]?.Value<string>() ?? "";
                string compactData = data["data"]?.Value<string>() ?? "";
                JObject? meta = data["meta"] as JObject;
                JObject? mapData = data["mapData"] as JObject;
                if (mapData == null) mapData = new JObject();
                if (mapData["cities"] == null) mapData["cities"] = new JArray();
                if (mapData["roads"] == null) mapData["roads"] = new JArray();
                // Добавляем customTargets из полезной нагрузки
                if (data["customTargets"] != null)
                    mapData["customTargets"] = data["customTargets"];

                // Определяем имя файла (как было)
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
                                if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float z))
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

                // Сохраняем файлы
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
                if (!File.Exists(playerPath))
                    File.WriteAllText(playerPath, GenerateTrailPlayerHtml());

                AppendLog($"[WebSocket] Трек сохранён: {baseName}.html");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", $"Трек сохранён: {baseName}.html", ToolTipIcon.Info);

                RefreshTrackList();
                Process.Start(new ProcessStartInfo($"http://localhost:8082/saved_tracks/{baseName}.html") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"[WebSocket] Ошибка сохранения трека: {ex.Message}");
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
                string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "saved_tracks");
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
                        listTracks.Items.Add(name);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка обновления списка треков: {ex.Message}");
            }
        }

        private void OpenSelectedTrack()
        {
            if (listTracks.SelectedItem == null) return;
            string? selected = listTracks.SelectedItem.ToString();
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("(")) return;
            string url = $"http://localhost:8082/saved_tracks/{selected}.html";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                AppendLog($"Открыт трек: {selected}.html");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия трека: {ex.Message}");
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
            bool hasTelemetry = File.Exists(Path.Combine(pluginsDir, "ets2-telemetry-server.dll"));
            bool hasTruckTel = File.Exists(Path.Combine(pluginsDir, "trucktel.dll"));

            if (!hasTelemetry || !hasTruckTel)
            {
                DialogResult result = MessageBox.Show(
                    lang.Get("plugins_missing_prompt") ?? "Some plugins are missing. Install them now?",
                    lang.Get("plugins_missing_title") ?? "Plugins Missing",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.Yes)
                {
                    string sourcePlugins = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugins");
                    if (Directory.Exists(sourcePlugins))
                    {
                        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
                        foreach (var file in Directory.GetFiles(sourcePlugins))
                            File.Copy(file, Path.Combine(pluginsDir, Path.GetFileName(file)), true);
                        AppendLog("Plugins copied.");
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
            AppendLog("All plugins present.");
            return true;
        }

        private bool ArePluginsInstalled()
        {
            string ets2Path = GetEts2Path();
            if (string.IsNullOrEmpty(ets2Path)) return false;
            string pluginsDir = Path.Combine(ets2Path, "bin", "win_x64", "plugins");
            return File.Exists(Path.Combine(pluginsDir, "ets2-telemetry-server.dll")) &&
                   File.Exists(Path.Combine(pluginsDir, "trucktel.dll"));
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

        private bool StartTelemetryServer()
        {
            string serverExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "ets2_server", "Ets2Telemetry.exe");
            if (!File.Exists(serverExe))
            {
                AppendLog("Telemetry server executable not found.");
                return false;
            }
            try
            {
                var proc = Process.Start(serverExe);
                AppendLog($"Telemetry server started with PID {proc?.Id}");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start server: {ex.Message}");
                return false;
            }
        }

        private bool StartPythonServer()
        {
            try
            {
                string pythonCmd = "python";
                if (File.Exists("pythonw.exe")) pythonCmd = "pythonw";
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = "-m http.server 8082",
                    WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
                AppendLog($"Web server started on port 8082.");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start web server: {ex.Message}");
                return false;
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
                string urlMain = "http://localhost:8082/web_ui_hybrid.html";
                string urlPda = "http://localhost:8082/web_pda_map.html";
                Process.Start(overlayExe, urlMain);
                AppendLog("Main overlay started.");
                System.Threading.Thread.Sleep(500);
                Process.Start(overlayExe, $"append {urlPda}");
                AppendLog("PDA map overlay appended.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start overlay: {ex.Message}");
            }
        }

        private void StopSystem()
        {
            if (!procManager.IsRunning) return;
            AppendLog("Stopping system...");
            procManager.Stop();
            StopTriggerServer();
            StopWebSocketSaveServer();
            KillChildProcesses();
            AppendLog("System stopped.");
            RefreshUI();
        }

        private void KillChildProcesses()
        {
            string[] processNames = { "Ets2Telemetry", "python", "pythonw", "WebOverlay", "pano" };
            foreach (var name in processNames)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                        proc.Kill();
                }
                catch { }
            }
            AppendLog("All child processes killed.");
        }

        private void RestartOverlay()
        {
            AppendLog("Restarting overlay...");
            foreach (var proc in Process.GetProcessesByName("WebOverlay"))
            {
                try { proc.Kill(); } catch { }
            }
            foreach (var proc in Process.GetProcessesByName("pano"))
            {
                try { proc.Kill(); } catch { }
            }
            AppendLog("Overlay processes killed. Restarting...");
            StartWebOverlay();
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

        private void OpenLogFolder()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (Directory.Exists(logDir))
                Process.Start("explorer.exe", logDir);
        }

        private void AppendLog(string msg)
        {
            if (logConsole.InvokeRequired)
                logConsole.Invoke(new Action(() => AppendLog(msg)));
            else
            {
                logConsole.AppendText(msg + Environment.NewLine);
                logConsole.ScrollToCaret();
            }
        }

        private bool IsTelemetryConnected()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "web_data.json");
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
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "web_data.json");
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
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "web_data.json");
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
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "web_data.json");
                if (!File.Exists(jsonPath)) return false;
                var json = File.ReadAllText(jsonPath);
                var obj = JObject.Parse(json);
                return obj["game"]?["connected"]?.Value<bool>() ?? false;
            }
            catch { return false; }
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
            bool python = Process.GetProcessesByName("python").Length > 0 ||
                          Process.GetProcessesByName("pythonw").Length > 0;
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
                SetStatusText(indicatorTruckTel, "TruckTel", speedText, true);
            }
            else
            {
                SetStatusText(indicatorTruckTel, "TruckTel", "NO DATA", false);
            }
            SetStatusText(indicatorEts2Telemetry, "ETS2 Telemetry",
                dataFresh ? "RECEIVING" : "NO DATA", dataFresh);
            SetStatusText(indicatorWebServer, "Web Server",
                python ? "ON" : "OFF", python);
            SetStatusText(indicatorWebOverlay, "Web Overlay",
                webOverlay ? "ON" : "OFF", webOverlay);
            SetStatusText(indicatorArduino, "Arduino",
                arduino ? "CONNECTED" : "NONE", arduino);
        }

        private bool IsEts2ProcessRunning()
        {
            try { return Process.GetProcessesByName("eurotrucks2").Length > 0; }
            catch { return false; }
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
                        AppendLog("Hotkey save (Shift+Ctrl+S) pressed. Triggering save regardless of pause.");
                        TriggerTrailSave();
                        break;
                    case HOTKEY_START_REC:
                        AppendLog("Hotkey start recording (Shift+Ctrl+R)");
                        SendCommandToMap("start_recording"); // метод в QuestsManager
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
                    var response = client.GetAsync("http://localhost:25555/api/ets2/telemetry").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var json = response.Content.ReadAsStringAsync().Result;
                        var obj = JObject.Parse(json);
                        return obj["game"]?["paused"]?.Value<bool>() ?? false;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"IsGamePaused error: {ex.Message}");
            }
            return false;
        }

        private void TriggerTrailSave()
        {
            try
            {
                string triggerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "save_trail.trigger");
                File.WriteAllText(triggerPath, "trigger");
                AppendLog($"Trail save triggered. File created: {triggerPath}");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Сохранение трека инициировано.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to trigger trail save: {ex.Message}");
            }
        }

        // Тестовое окно (Shift+Ctrl+T)
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
        // УПРАВЛЕНИЕ ПОЯВЛЕНИЕМ UI ПОСЛЕ ВЫХОДА ИЗ ПАУЗЫ
        // ================================================================
        private void StartPauseCheck()
        {
            _pauseCheckTimer = new System.Windows.Forms.Timer();
            _pauseCheckTimer.Interval = 1000;
            _pauseCheckTimer.Tick += (s, e) => CheckPauseAndShowUI();
            _pauseCheckTimer.Start();
            AppendLog("Pause check timer started.");
        }

        private async void CheckPauseAndShowUI()
        {
            if (_uiShown) return;
            bool paused = await IsGamePausedAsync();
            if (paused)
            {
                // Игра на паузе – ждём
                return;
            }
            // Игра вышла из паузы – показываем UI
            _uiShown = true;
            _pauseCheckTimer?.Stop();
            SendCommandToMap("show_ui");
            AppendLog("[UI] Отправлена команда show_ui на веб-страницы");
        }

        private async Task<bool> IsGamePausedAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(1);
                    var response = await client.GetAsync("http://localhost:25555/api/ets2/telemetry");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        return obj["game"]?["paused"]?.Value<bool>() ?? false;
                    }
                }
            }
            catch
            {
                // Если не удалось получить, считаем не на паузе (чтобы не блокировать показ)
            }
            return false;
        }

        // ================================================================
        // ЗАКРЫТИЕ ФОРМЫ
        // ================================================================
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