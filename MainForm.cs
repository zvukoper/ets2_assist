using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Threading.Tasks;
using System.Management;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Http;
using System.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ETS2_Assist_GUI
{
    public class MainForm : Form
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

        private Button btnStart = null!;
        private Button btnStop = null!;
        private Button btnRestartOverlay = null!;
        private Button btnMinimize = null!;
        private Button btnExit = null!;
        private Button btnRefreshTracks = null!; // новая кнопка

        private RichTextBox logConsole = null!;
        private Panel indicatorsPanel = null!;
        private ListBox listTracks = null!; // новый список треков

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

        // ========== ГОРЯЧАЯ КЛАВИША ==========
        private const int HOTKEY_ID = 9000;
        private bool hotKeyRegistered = false;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        // ========== WEBSOCKET-СЕРВЕР ДЛЯ СОХРАНЕНИЯ ТРЕКОВ (порт 8084) ==========
        private WebSocketSharp.Server.WebSocketServer? _wsSaveServer;
        private bool _wsSaveRunning = false;

        // ========== HTTP-СЕРВЕР ДЛЯ ТРИГГЕР-ФАЙЛА И СПИСКА ТРЕКОВ (порт 8083) ==========
        private HttpListener? _triggerListener;
        private Thread? _triggerListenerThread;
        private bool _triggerListenerRunning = false;

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
                RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, (uint)Keys.S.GetHashCode());
                hotKeyRegistered = true;
                AppendLog("Hotkey Shift+Ctrl+S registered.");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to register hotkey: {ex.Message}");
            }

            if (AppSettings.AutoStartSystem)
            {
                Task.Run(async () => await StartSystemAsync());
            }

            if (AppSettings.StartMinimized)
            {
                this.Hide();
            }

            // Загружаем список треков при запуске
            RefreshTrackList();
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

            // Левая колонка кнопок
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

            // Консоль логов
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

            // Список треков (новая панель)
            int listLeft = consoleLeft + consoleWidth + 10;
            int listWidth = this.ClientSize.Width - listLeft - 200; // оставляем место для индикаторов
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

            // Индикаторы (правая панель)
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

            // Добавляем всё на форму
            this.Controls.AddRange(new Control[] {
                btnStart, btnStop, btnRestartOverlay, btnMinimize, btnExit, btnRefreshTracks,
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

            // Запускаем HTTP-сервер для триггер-файла, списка треков и отдачи файлов (порт 8083)
            StartTriggerServer();

            // Запускаем WebSocket-сервер для сохранения треков (порт 8084)
            StartWebSocketSaveServer();

            await procManager.StartAsync();

            StartWebOverlay();

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

                // Добавляем CORS-заголовки
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

            public static void SetLog(Action<string> log) => _log = log;
            public static void SetOnTrail(Action<JObject> action) => _onTrail = action;
            public static void SetPlaySoundAction(Action<string> action) => _playSoundAction = action;

            protected override void OnOpen()
            {
                _log?.Invoke("[WebSocket] Клиент карты подключился");
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                try
                {
                    var json = e.Data;
                    var data = JObject.Parse(json);

                    if (data["command"]?.Value<string>() == "play_sound")
                    {
                        var soundType = data["type"]?.Value<string>() ?? "beep";
                        _log?.Invoke($"[WebSocket] Команда звука: {soundType}");
                        _playSoundAction?.Invoke(soundType);
                        return;
                    }

                    _log?.Invoke($"[WebSocket] Получен трек ({json.Length} байт)");
                    _onTrail?.Invoke(data);
                    Send(JsonConvert.SerializeObject(new { status = "ok", message = "Трек получен" }));
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

        private void SaveTrailFromWebSocket(JObject data)
        {
            try
            {
                string tracksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "saved_tracks");
                if (!Directory.Exists(tracksDir))
                    Directory.CreateDirectory(tracksDir);

                string filename = $"track_{DateTime.Now:yyyyMMdd_HHmmss}";
                string jsonPath = Path.Combine(tracksDir, filename + ".json");
                File.WriteAllText(jsonPath, data.ToString(Formatting.Indented));

                string html = GenerateTrailHtml(data);
                string htmlPath = Path.Combine(tracksDir, filename + ".html");
                File.WriteAllText(htmlPath, html);

                string playerPath = Path.Combine(tracksDir, "trail_player.html");
                if (!File.Exists(playerPath))
                {
                    File.WriteAllText(playerPath, GenerateTrailPlayerHtml());
                }

                AppendLog($"[WebSocket] Трек сохранён: {filename}.html");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", $"Трек сохранён: {filename}.html", ToolTipIcon.Info);

                // Обновляем список треков в GUI
                RefreshTrackList();

                // Открываем в браузере через веб-сервер
                Process.Start(new ProcessStartInfo($"http://localhost:8082/saved_tracks/{filename}.html") { UseShellExecute = true });
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
                    {
                        listTracks.Items.Add(name);
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
            if (listTracks.SelectedItem == null) return;
            string? selected = listTracks.SelectedItem.ToString();
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("(")) return;

            string fileName = selected + ".html";
            string url = $"http://localhost:8082/saved_tracks/{fileName}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                AppendLog($"Открыт трек: {fileName}");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия трека: {ex.Message}");
            }
        }

        private string GenerateTrailPlayerHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8""/>
<title>ETS2 Trail Player</title>
<style>
    body { background:#0a0c10; color:#e0e0e0; font-family:'Segoe UI',sans-serif; margin:20px; }
    #trackList { display:flex; flex-direction:column; gap:6px; max-width:500px; margin:20px auto; }
    .track-item { background:#1a1f26; padding:10px 16px; border:1px solid #333; border-radius:6px; cursor:pointer; transition:0.2s; }
    .track-item:hover { background:#2a3545; }
    #playerContainer { margin-top:20px; }
    iframe { width:100%; height:600px; border:none; background:#111; border-radius:8px; }
</style>
</head>
<body>
<h1 style=""text-align:center;"">ETS2 Trail Player</h1>
<div id=""trackList"">Загрузка...</div>
<div id=""playerContainer""></div>
<script>
async function loadTracks() {
    try {
        const res = await fetch('http://localhost:8083/list_tracks');
        const data = await res.json();
        const list = document.getElementById('trackList');
        list.innerHTML = '';
        if (data.files.length === 0) {
            list.innerHTML = '<div style=""color:#666;"">Нет сохранённых треков</div>';
            return;
        }
        for (const file of data.files) {
            const div = document.createElement('div');
            div.className = 'track-item';
            const name = file.replace('.json', '');
            div.textContent = name;
            div.addEventListener('click', () => {
                const container = document.getElementById('playerContainer');
                // Загружаем отдельный HTML-файл трека (он уже полноценный)
                container.innerHTML = `<iframe src=""http://localhost:8082/saved_tracks/${name}.html""></iframe>`;
            });
            list.appendChild(div);
        }
    } catch (e) {
        document.getElementById('trackList').innerHTML = 'Ошибка загрузки списка: ' + e.message;
    }
}
loadTracks();
</script>
</body>
</html>";
        }

        private string GenerateTrailHtml(JObject data)
        {
            string trailDataJson = data.ToString(Formatting.None);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"utf-8\"/>");
            sb.AppendLine("    <title>ETS2 Trail Viewer</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { margin:0; background:#0a0c10; color:#e0e0e0; font-family:'Segoe UI',sans-serif; overflow:hidden; }");
            sb.AppendLine("        #mapCanvas { display:block; width:100vw; height:calc(100vh - 120px); background:#111; cursor:grab; }");
            sb.AppendLine("        #controls { position:fixed; bottom:0; left:0; right:0; background:#1a1f26; padding:10px 20px; border-top:1px solid #333; display:flex; align-items:center; gap:12px; flex-wrap:wrap; }");
            sb.AppendLine("        #controls button, #controls input { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:6px; padding:6px 14px; font-size:14px; cursor:pointer; }");
            sb.AppendLine("        #controls button:hover { background:#3a4a5a; }");
            sb.AppendLine("        #timeSlider { flex:1; min-width:200px; }");
            sb.AppendLine("        #speedLabel { font-size:13px; color:#8fa0b9; }");
            sb.AppendLine("        #checkboxFollow { margin-left:12px; }");
            sb.AppendLine("        #info { position:absolute; top:10px; right:10px; background:rgba(0,0,0,0.7); padding:6px 12px; border-radius:6px; font-size:12px; color:#ccc; }");
            sb.AppendLine("        #dataPanel { position:absolute; bottom:80px; left:20px; background:rgba(0,0,0,0.7); padding:8px 14px; border-radius:6px; font-size:11px; color:#aabbcc; border:1px solid #333; backdrop-filter:blur(4px); pointer-events:none; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div id=\"info\">Просмотр трека</div>");
            sb.AppendLine("<div id=\"dataPanel\">⛽ -- л &nbsp;|&nbsp; 🛠️ --%</div>");
            sb.AppendLine("<canvas id=\"mapCanvas\"></canvas>");
            sb.AppendLine("<div id=\"controls\">");
            sb.AppendLine("    <button id=\"playBtn\">▶</button>");
            sb.AppendLine("    <button id=\"speedDownBtn\">−</button>");
            sb.AppendLine("    <span id=\"speedLabel\">1×</span>");
            sb.AppendLine("    <button id=\"speedUpBtn\">+</button>");
            sb.AppendLine("    <input type=\"range\" id=\"timeSlider\" min=\"0\" max=\"1000\" value=\"0\" style=\"flex:1;\">");
            sb.AppendLine("    <label><input type=\"checkbox\" id=\"followCheck\" checked> Следить</label>");
            sb.AppendLine("    <span id=\"timeDisplay\" style=\"font-size:13px; color:#aabbcc;\">0:00 / 0:00</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const trailData = {trailDataJson};");
            sb.AppendLine("// ================================================================");
            sb.AppendLine("// ПОЛНАЯ ЛОГИКА ПЛЕЕРА");
            sb.AppendLine("// ================================================================");
            sb.AppendLine("const canvas = document.getElementById('mapCanvas');");
            sb.AppendLine("const ctx = canvas.getContext('2d');");
            sb.AppendLine("let W, H;");
            sb.AppendLine("function resize() { W = canvas.width = window.innerWidth; H = canvas.height = window.innerHeight - 120; drawMap(); }");
            sb.AppendLine("window.addEventListener('resize', resize);");
            sb.AppendLine("");
            sb.AppendLine("// Данные из трека");
            sb.AppendLine("const trailPoints = trailData.trail || [];");
            sb.AppendLine("const dataPoints = trailData.dataPoints || [];");
            sb.AppendLine("const events = trailData.events || [];");
            sb.AppendLine("const markers = trailData.markers || [];");
            sb.AppendLine("const cities = trailData.mapData?.cities || [];");
            sb.AppendLine("const roads = trailData.mapData?.roads || [];");
            sb.AppendLine("const customTargets = trailData.customTargets || [];");
            sb.AppendLine("");
            sb.AppendLine("function parsePoint(p) { const parts = p.split(' '); return { x: parseFloat(parts[0]), z: parseFloat(parts[1]), h: parseFloat(parts[2] || 0) }; }");
            sb.AppendLine("");
            sb.AppendLine("// Времена для каждой точки из данных");
            sb.AppendLine("const times = trailPoints.map(tp => parseFloat(tp.t) || 0);");
            sb.AppendLine("const totalDuration = times.length > 0 ? times[times.length-1] : 0;");
            sb.AppendLine("");
            sb.AppendLine("let centerX = 0, centerZ = 0, scale = 1;");
            sb.AppendLine("let dragStartX = 0, dragStartY = 0, dragStartCX = 0, dragStartCZ = 0, isDragging = false;");
            sb.AppendLine("let currentInterpPos = { x:0, z:0, h:0 };");
            sb.AppendLine("");
            sb.AppendLine("function fitMap() {");
            sb.AppendLine("    if (trailPoints.length < 2) return;");
            sb.AppendLine("    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;");
            sb.AppendLine("    for (const tp of trailPoints) {");
            sb.AppendLine("        const p = parsePoint(tp.p);");
            sb.AppendLine("        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;");
            sb.AppendLine("        if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;");
            sb.AppendLine("    }");
            sb.AppendLine("    centerX = (minX + maxX) / 2;");
            sb.AppendLine("    centerZ = (minZ + maxZ) / 2;");
            sb.AppendLine("    const range = Math.max(maxX - minX, maxZ - minZ, 1);");
            sb.AppendLine("    scale = (Math.min(W, H) * 0.85) / (range * 1.15);");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("function worldToScreen(wx, wz) {");
            sb.AppendLine("    const dx = (wx - centerX) * scale;");
            sb.AppendLine("    const dz = (wz - centerZ) * scale;");
            sb.AppendLine("    return { x: W/2 + dx, y: H/2 - dz };");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("function getTrailColor(speed) {");
            sb.AppendLine("    const s = Math.max(0, Math.min(125, speed));");
            sb.AppendLine("    let r,g,b;");
            sb.AppendLine("    if (s <= 10) { const t=s/10; r=0; g=t*255; b=255; }");
            sb.AppendLine("    else if (s <= 25) { const t=(s-10)/15; r=0; g=255; b=255-t*255; }");
            sb.AppendLine("    else if (s <= 50) { const t=(s-25)/25; r=t*255; g=255; b=0; }");
            sb.AppendLine("    else if (s <= 75) { const t=(s-50)/25; r=255; g=255-(255-165)*t; b=0; }");
            sb.AppendLine("    else if (s <= 100) { const t=(s-75)/25; r=255-(255-128)*t; g=165-165*t; b=t*255; }");
            sb.AppendLine("    else { const t=(s-100)/25; r=128+(255-128)*t; g=0; b=255-255*t; }");
            sb.AppendLine("    return `rgb(${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)})`;");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("function drawMap() {");
            sb.AppendLine("    ctx.clearRect(0, 0, W, H);");
            sb.AppendLine("    ctx.fillStyle = '#0f1217'; ctx.fillRect(0, 0, W, H);");
            sb.AppendLine("    const gridStep = 200 / scale;");
            sb.AppendLine("    ctx.strokeStyle = '#2a3545'; ctx.lineWidth = 0.5; ctx.setLineDash([4,6]);");
            sb.AppendLine("    for (let x = -W/2; x < W/2; x += gridStep) { const p = worldToScreen(centerX+x, centerZ); ctx.beginPath(); ctx.moveTo(p.x,0); ctx.lineTo(p.x,H); ctx.stroke(); }");
            sb.AppendLine("    for (let z = -H/2; z < H/2; z += gridStep) { const p = worldToScreen(centerX, centerZ+z); ctx.beginPath(); ctx.moveTo(0,p.y); ctx.lineTo(W,p.y); ctx.stroke(); }");
            sb.AppendLine("    ctx.setLineDash([]);");
            sb.AppendLine("    // Дороги");
            sb.AppendLine("    for (const r of roads) { const p1=worldToScreen(r.x1,r.z1); const p2=worldToScreen(r.x2,r.z2); ctx.beginPath(); ctx.moveTo(p1.x,p1.y); ctx.lineTo(p2.x,p2.y); ctx.strokeStyle='#5a7a8a'; ctx.lineWidth=1.5; ctx.globalAlpha=0.6; ctx.stroke(); }");
            sb.AppendLine("    ctx.globalAlpha=1;");
            sb.AppendLine("    // Города (видимые)");
            sb.AppendLine("    ctx.textAlign='center'; ctx.textBaseline='middle';");
            sb.AppendLine("    for (const c of cities) { const p=worldToScreen(c.x,c.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue; ctx.beginPath(); ctx.arc(p.x,p.y,4,0,2*Math.PI); ctx.fillStyle='#ffdd88'; ctx.shadowColor='#ffdd8844'; ctx.shadowBlur=6; ctx.fill(); ctx.shadowBlur=0; ctx.font='10px \"Segoe UI\"'; ctx.fillStyle='#c8ddee'; ctx.fillText(c.name, p.x, p.y-6); }");
            sb.AppendLine("    // Цели (видимые)");
            sb.AppendLine("    for (const t of customTargets) {");
            sb.AppendLine("        const p = worldToScreen(t.x, t.z);");
            sb.AppendLine("        if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        ctx.beginPath(); ctx.arc(p.x, p.y, t.active ? 6 : 4, 0, 2*Math.PI);");
            sb.AppendLine("        ctx.fillStyle = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');");
            sb.AppendLine("        ctx.shadowColor = t.active ? '#ffc85788' : '#88aadd88';");
            sb.AppendLine("        ctx.shadowBlur = t.active ? 16 : 8;");
            sb.AppendLine("        ctx.fill(); ctx.shadowBlur = 0;");
            sb.AppendLine("        ctx.strokeStyle = '#ffffff'; ctx.lineWidth = t.active ? 1.5 : 0.5; ctx.stroke();");
            sb.AppendLine("        ctx.font = t.active ? 'bold 10px \"Segoe UI\"' : '9px \"Segoe UI\"';");
            sb.AppendLine("        ctx.fillStyle = '#fff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=4;");
            sb.AppendLine("        ctx.fillText(t.name, p.x, p.y - (t.active ? 14 : 10));");
            sb.AppendLine("        ctx.shadowBlur=0;");
            sb.AppendLine("    }");
            sb.AppendLine("    // Указатели для городов за пределами экрана (ближайшие 4)");
            sb.AppendLine("    const cx0 = W/2, cy0 = H/2;");
            sb.AppendLine("    const radius = Math.min(W, H)*0.42;");
            sb.AppendLine("    const truckPos = currentInterpPos;");
            sb.AppendLine("    const nearCities = cities.map(c => ({ ...c, dist: Math.hypot(c.x - truckPos.x, c.z - truckPos.z) })).sort((a,b)=>a.dist-b.dist).slice(0,4);");
            sb.AppendLine("    for (const c of nearCities) {");
            sb.AppendLine("        const p = worldToScreen(c.x, c.z);");
            sb.AppendLine("        if (p.x>=0 && p.x<=W && p.y>=0 && p.y<=H) continue;");
            sb.AppendLine("        const dx = p.x - cx0, dy = p.y - cy0; const len = Math.hypot(dx, dy); if (len<0.01) continue;");
            sb.AppendLine("        const nx = dx/len, ny = dy/len;");
            sb.AppendLine("        const arrowX = cx0 + nx * radius, arrowY = cy0 + ny * radius;");
            sb.AppendLine("        const angle = Math.atan2(ny, nx);");
            sb.AppendLine("        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);");
            sb.AppendLine("        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();");
            sb.AppendLine("        ctx.fillStyle = '#aabbcc'; ctx.shadowColor='rgba(0,0,0,0.6)'; ctx.shadowBlur=4; ctx.fill();");
            sb.AppendLine("        ctx.strokeStyle='#000'; ctx.lineWidth=1; ctx.stroke(); ctx.shadowBlur=0; ctx.restore();");
            sb.AppendLine("        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;");
            sb.AppendLine("        if (lx<50) lx=50; if (lx>W-50) lx=W-50; if (ly<20) ly=20; if (ly>H-20) ly=H-20;");
            sb.AppendLine("        ctx.font='10px \"Segoe UI\"'; ctx.fillStyle='#c8ddee'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=4;");
            sb.AppendLine("        ctx.textAlign='center'; ctx.textBaseline='middle';");
            sb.AppendLine("        ctx.fillText(`${c.name} (${formatDistance(c.dist)})`, lx, ly);");
            sb.AppendLine("        ctx.shadowBlur=0;");
            sb.AppendLine("    }");
            sb.AppendLine("    // Указатели для целей за пределами экрана");
            sb.AppendLine("    for (const t of customTargets) {");
            sb.AppendLine("        const p = worldToScreen(t.x, t.z);");
            sb.AppendLine("        if (p.x>=0 && p.x<=W && p.y>=0 && p.y<=H) continue;");
            sb.AppendLine("        const dx = p.x - cx0, dy = p.y - cy0; const len = Math.hypot(dx, dy); if (len<0.01) continue;");
            sb.AppendLine("        const nx = dx/len, ny = dy/len;");
            sb.AppendLine("        const arrowX = cx0 + nx * radius, arrowY = cy0 + ny * radius;");
            sb.AppendLine("        const angle = Math.atan2(ny, nx);");
            sb.AppendLine("        const color = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');");
            sb.AppendLine("        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);");
            sb.AppendLine("        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();");
            sb.AppendLine("        ctx.fillStyle = color; ctx.shadowColor='rgba(0,0,0,0.6)'; ctx.shadowBlur=4; ctx.fill();");
            sb.AppendLine("        ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke(); ctx.shadowBlur=0; ctx.restore();");
            sb.AppendLine("        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;");
            sb.AppendLine("        if (lx<50) lx=50; if (lx>W-50) lx=W-50; if (ly<20) ly=20; if (ly>H-20) ly=H-20;");
            sb.AppendLine("        ctx.font=t.active?'bold 10px \"Segoe UI\"':'9px \"Segoe UI\"';");
            sb.AppendLine("        ctx.fillStyle='#fff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=4;");
            sb.AppendLine("        ctx.textAlign='center'; ctx.textBaseline='middle';");
            sb.AppendLine("        ctx.fillText(`${t.name} (${formatDistance(t.dist||0)})`, lx, ly);");
            sb.AppendLine("        ctx.shadowBlur=0;");
            sb.AppendLine("    }");
            sb.AppendLine("    // Шлейф");
            sb.AppendLine("    if (trailPoints.length > 1) {");
            sb.AppendLine("        for (let i=1; i<trailPoints.length; i++) {");
            sb.AppendLine("            const p1=parsePoint(trailPoints[i-1].p); const p2=parsePoint(trailPoints[i].p);");
            sb.AppendLine("            const s1=worldToScreen(p1.x,p1.z); const s2=worldToScreen(p2.x,p2.z);");
            sb.AppendLine("            const speed = parseFloat(trailPoints[i].s) || 0;");
            sb.AppendLine("            ctx.beginPath(); ctx.moveTo(s1.x,s1.y); ctx.lineTo(s2.x,s2.y); ctx.strokeStyle=getTrailColor(speed); ctx.lineWidth=2.5; ctx.shadowColor='rgba(0,0,0,0.5)'; ctx.shadowBlur=4; ctx.stroke(); ctx.shadowBlur=0;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    // События");
            sb.AppendLine("    for (const e of events) {");
            sb.AppendLine("        const p=worldToScreen(e.x,e.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        const size=10; ctx.save(); ctx.translate(p.x,p.y); ctx.shadowColor='rgba(0,0,0,0.5)'; ctx.shadowBlur=6;");
            sb.AppendLine("        ctx.beginPath(); ctx.arc(0,0,size,0,2*Math.PI); ctx.fillStyle=e.color||'#ffffff'; ctx.fill(); ctx.shadowBlur=0; ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke();");
            sb.AppendLine("        ctx.fillStyle='#fff'; ctx.font='bold 10px \"Segoe UI\"'; ctx.textAlign='center'; ctx.textBaseline='middle'; ctx.fillText(e.label||'?',0,-1);");
            sb.AppendLine("        if (e.subtext) { ctx.fillStyle='#fff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=3; ctx.font='7px \"Segoe UI\"'; ctx.textBaseline='top'; ctx.fillText(e.subtext,0,size+2); ctx.shadowBlur=0; }");
            sb.AppendLine("        ctx.restore();");
            sb.AppendLine("    }");
            sb.AppendLine("    // Маркеры старт/финиш");
            sb.AppendLine("    for (const m of markers) {");
            sb.AppendLine("        const p=worldToScreen(parseFloat(m.x), parseFloat(m.z));");
            sb.AppendLine("        if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        ctx.save(); ctx.translate(p.x,p.y); ctx.font='12px \"Segoe UI\"'; ctx.fillStyle='#ffffff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=4; ctx.textAlign='center'; ctx.textBaseline='bottom'; ctx.fillText(m.label,0,-10); ctx.restore();");
            sb.AppendLine("    }");
            sb.AppendLine("    // Текущая позиция (грузовик) с интерполяцией");
            sb.AppendLine("    const sp = worldToScreen(currentInterpPos.x, currentInterpPos.z);");
            sb.AppendLine("    const heading = currentInterpPos.h || 0;");
            sb.AppendLine("    const speed = currentIndex < trailPoints.length ? parseFloat(trailPoints[currentIndex].s) || 0 : 0;");
            sb.AppendLine("    ctx.save(); ctx.translate(sp.x, sp.y);");
            sb.AppendLine("    ctx.rotate(heading + Math.PI); // разворот на 180");
            sb.AppendLine("    ctx.beginPath(); ctx.moveTo(0, -14); ctx.lineTo(-9, 9); ctx.lineTo(0, 3); ctx.lineTo(9, 9); ctx.closePath();");
            sb.AppendLine("    ctx.fillStyle='#ff4d4d'; ctx.shadowColor='#ff4d4d88'; ctx.shadowBlur=12; ctx.fill(); ctx.shadowBlur=0; ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke();");
            sb.AppendLine("    ctx.restore();");
            sb.AppendLine("    // Подпись скорости под грузовиком");
            sb.AppendLine("    ctx.save(); ctx.translate(sp.x, sp.y+22); ctx.font='10px \"Segoe UI\"'; ctx.fillStyle='#fff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=4; ctx.textAlign='center'; ctx.textBaseline='top'; ctx.fillText(`${speed.toFixed(0)} km/h`,0,0); ctx.restore();");
            sb.AppendLine("    // Данные (топливо, повреждения)");
            sb.AppendLine("    if (dataPoints.length > 0 && currentIndex < trailPoints.length) {");
            sb.AppendLine("        let closest = null; let minDist = Infinity;");
            sb.AppendLine("        const curP = currentInterpPos;");
            sb.AppendLine("        for (const dp of dataPoints) {");
            sb.AppendLine("            const pp = parsePoint(dp.p);");
            sb.AppendLine("            const d = Math.hypot(pp.x-curP.x, pp.z-curP.z);");
            sb.AppendLine("            if (d < minDist) { minDist = d; closest = dp; }");
            sb.AppendLine("        }");
            sb.AppendLine("        if (closest) {");
            sb.AppendLine("            document.getElementById('dataPanel').innerHTML = `⛽ ${closest.fuel || '--'} л &nbsp;|&nbsp; 🛠️ ${closest.damage || '--'}%`;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("// formatDistance для подписей");
            sb.AppendLine("function formatDistance(d) { if (d < 1000) return Math.round(d)+'м'; return (Math.round(d/1000))+'км'; }");
            sb.AppendLine("");
            sb.AppendLine("// Зум колесиком");
            sb.AppendLine("canvas.addEventListener('wheel', (e) => {");
            sb.AppendLine("    e.preventDefault();");
            sb.AppendLine("    const delta = e.deltaY > 0 ? 0.9 : 1.1;");
            sb.AppendLine("    scale *= delta;");
            sb.AppendLine("    if (scale < 0.001) scale = 0.001; if (scale > 1000) scale = 1000;");
            sb.AppendLine("    drawMap();");
            sb.AppendLine("}, { passive: false });");
            sb.AppendLine("");
            sb.AppendLine("// Перетаскивание карты (инвертировано)");
            sb.AppendLine("canvas.addEventListener('mousedown', (e) => {");
            sb.AppendLine("    if (e.button === 0) {");
            sb.AppendLine("        isDragging = true;");
            sb.AppendLine("        dragStartX = e.clientX; dragStartY = e.clientY;");
            sb.AppendLine("        dragStartCX = centerX; dragStartCZ = centerZ;");
            sb.AppendLine("        canvas.style.cursor = 'grabbing';");
            sb.AppendLine("    }");
            sb.AppendLine("});");
            sb.AppendLine("window.addEventListener('mousemove', (e) => {");
            sb.AppendLine("    if (isDragging) {");
            sb.AppendLine("        const dx = (e.clientX - dragStartX) / scale;");
            sb.AppendLine("        const dy = (dragStartY - e.clientY) / scale;");
            sb.AppendLine("        centerX = dragStartCX - dx; // инвертировано по X");
            sb.AppendLine("        centerZ = dragStartCZ - dy; // инвертировано по Y");
            sb.AppendLine("        drawMap();");
            sb.AppendLine("    }");
            sb.AppendLine("});");
            sb.AppendLine("window.addEventListener('mouseup', () => { if (isDragging) { isDragging = false; canvas.style.cursor = 'grab'; } });");
            sb.AppendLine("");
            sb.AppendLine("// Воспроизведение по времени с интерполяцией");
            sb.AppendLine("let playing = false; let speedFactor = 1; let currentIndex = 0; let follow = true;");
            sb.AppendLine("let playStartTime = 0; let playStartElapsed = 0;");
            sb.AppendLine("const playBtn = document.getElementById('playBtn');");
            sb.AppendLine("const speedLabel = document.getElementById('speedLabel');");
            sb.AppendLine("const timeSlider = document.getElementById('timeSlider');");
            sb.AppendLine("const timeDisplay = document.getElementById('timeDisplay');");
            sb.AppendLine("const followCheck = document.getElementById('followCheck');");
            sb.AppendLine("");
            sb.AppendLine("function updateTimeDisplay() {");
            sb.AppendLine("    const total = trailPoints.length;");
            sb.AppendLine("    const percent = total > 1 ? currentIndex / (total - 1) : 0;");
            sb.AppendLine("    timeSlider.value = percent * 1000;");
            sb.AppendLine("    const curSec = Math.floor(times[currentIndex] || 0);");
            sb.AppendLine("    const totalSec = Math.floor(totalDuration);");
            sb.AppendLine("    const format = (s) => { const m = Math.floor(s/60); const sec = s%60; return m+':'+(sec<10?'0':'')+sec; };");
            sb.AppendLine("    timeDisplay.textContent = format(curSec) + ' / ' + format(totalSec);");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("function setTime(value) {");
            sb.AppendLine("    const total = trailPoints.length;");
            sb.AppendLine("    if (total < 2) return;");
            sb.AppendLine("    const targetTime = value * totalDuration;");
            sb.AppendLine("    let idx = 0;");
            sb.AppendLine("    for (let i=1; i<times.length; i++) {");
            sb.AppendLine("        if (times[i] >= targetTime) { idx = i; break; }");
            sb.AppendLine("    }");
            sb.AppendLine("    currentIndex = Math.min(idx, total-1);");
            sb.AppendLine("    // Интерполируем позицию");
            sb.AppendLine("    const idxFloor = currentIndex;");
            sb.AppendLine("    const idxCeil = Math.min(idxFloor + 1, total - 1);");
            sb.AppendLine("    const t0 = times[idxFloor] || 0;");
            sb.AppendLine("    const t1 = times[idxCeil] || 0;");
            sb.AppendLine("    const frac = (t1 - t0) > 0.001 ? (targetTime - t0) / (t1 - t0) : 0;");
            sb.AppendLine("    const p1 = parsePoint(trailPoints[idxFloor].p);");
            sb.AppendLine("    const p2 = parsePoint(trailPoints[idxCeil].p);");
            sb.AppendLine("    currentInterpPos.x = p1.x + (p2.x - p1.x) * frac;");
            sb.AppendLine("    currentInterpPos.z = p1.z + (p2.z - p1.z) * frac;");
            sb.AppendLine("    currentInterpPos.h = p1.h + (p2.h - p1.h) * frac;");
            sb.AppendLine("    if (follow) { centerX = currentInterpPos.x; centerZ = currentInterpPos.z; }");
            sb.AppendLine("    drawMap(); updateTimeDisplay();");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("function playStep() {");
            sb.AppendLine("    if (!playing) return;");
            sb.AppendLine("    const now = performance.now() / 1000;");
            sb.AppendLine("    const elapsed = (now - playStartTime) * speedFactor + playStartElapsed;");
            sb.AppendLine("    const progress = Math.min(elapsed / totalDuration, 1);");
            sb.AppendLine("    const total = trailPoints.length;");
            sb.AppendLine("    const rawIdx = progress * (total - 1);");
            sb.AppendLine("    const idxFloor = Math.floor(rawIdx);");
            sb.AppendLine("    const idxCeil = Math.min(idxFloor + 1, total - 1);");
            sb.AppendLine("    const frac = rawIdx - idxFloor;");
            sb.AppendLine("    if (idxFloor < total - 1) {");
            sb.AppendLine("        const p1 = parsePoint(trailPoints[idxFloor].p);");
            sb.AppendLine("        const p2 = parsePoint(trailPoints[idxCeil].p);");
            sb.AppendLine("        currentInterpPos.x = p1.x + (p2.x - p1.x) * frac;");
            sb.AppendLine("        currentInterpPos.z = p1.z + (p2.z - p1.z) * frac;");
            sb.AppendLine("        currentInterpPos.h = p1.h + (p2.h - p1.h) * frac;");
            sb.AppendLine("    } else {");
            sb.AppendLine("        const p = parsePoint(trailPoints[total-1].p);");
            sb.AppendLine("        currentInterpPos = { x: p.x, z: p.z, h: p.h };");
            sb.AppendLine("    }");
            sb.AppendLine("    currentIndex = idxFloor;");
            sb.AppendLine("    if (follow) { centerX = currentInterpPos.x; centerZ = currentInterpPos.z; }");
            sb.AppendLine("    drawMap(); updateTimeDisplay();");
            sb.AppendLine("    if (progress >= 1) { playing = false; playBtn.textContent = '▶'; return; }");
            sb.AppendLine("    requestAnimationFrame(playStep);");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("playBtn.addEventListener('click', () => {");
            sb.AppendLine("    playing = !playing;");
            sb.AppendLine("    playBtn.textContent = playing ? '⏸' : '▶';");
            sb.AppendLine("    if (playing) {");
            sb.AppendLine("        if (currentIndex >= trailPoints.length - 1) { currentIndex = 0; currentInterpPos = parsePoint(trailPoints[0].p); if (follow) { centerX = currentInterpPos.x; centerZ = currentInterpPos.z; } }");
            sb.AppendLine("        playStartTime = performance.now() / 1000;");
            sb.AppendLine("        playStartElapsed = times[currentIndex] || 0;");
            sb.AppendLine("        playStep();");
            sb.AppendLine("    }");
            sb.AppendLine("});");
            sb.AppendLine("");
            sb.AppendLine("document.getElementById('speedDownBtn').addEventListener('click', () => { speedFactor = Math.max(0.5, speedFactor/1.5); speedLabel.textContent = speedFactor.toFixed(1)+'×'; });");
            sb.AppendLine("document.getElementById('speedUpBtn').addEventListener('click', () => { speedFactor = Math.min(5, speedFactor*1.5); speedLabel.textContent = speedFactor.toFixed(1)+'×'; });");
            sb.AppendLine("");
            sb.AppendLine("timeSlider.addEventListener('input', () => {");
            sb.AppendLine("    if (playing) { playing = false; playBtn.textContent = '▶'; }");
            sb.AppendLine("    const val = parseFloat(timeSlider.value) / 1000;");
            sb.AppendLine("    setTime(val);");
            sb.AppendLine("});");
            sb.AppendLine("");
            sb.AppendLine("followCheck.addEventListener('change', () => {");
            sb.AppendLine("    follow = followCheck.checked;");
            sb.AppendLine("    if (follow && currentIndex < trailPoints.length) { const p = currentInterpPos; centerX=p.x; centerZ=p.z; drawMap(); }");
            sb.AppendLine("});");
            sb.AppendLine("");
            sb.AppendLine("// Инициализация");
            sb.AppendLine("resize(); fitMap();");
            sb.AppendLine("if (trailPoints.length > 0) { currentIndex = 0; currentInterpPos = parsePoint(trailPoints[0].p); centerX=currentInterpPos.x; centerZ=currentInterpPos.z; }");
            sb.AppendLine("drawMap(); updateTimeDisplay();");
            sb.AppendLine("window.addEventListener('resize', () => { resize(); fitMap(); drawMap(); });");
            sb.AppendLine("</script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

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
                        {
                            File.Copy(file, Path.Combine(pluginsDir, Path.GetFileName(file)), true);
                        }
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
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                }
                catch { /* ignore */ }
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

                Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                AppendLog($"Current application version: {currentVersion}");

                if (currentVersion == null)
                {
                    AppendLog("WARNING: currentVersion is null, treating as 0.0.0");
                    currentVersion = new Version(0, 0, 0);
                }

                if (latestVersion > currentVersion)
                {
                    AppendLog($"New version available: {latestVersion} > {currentVersion}");
                    DialogResult result = MessageBox.Show(
                        string.Format(lang.Get("update_available") ?? "New version {0} is available. Current version: {1}. Open download page?", latestVersion, currentVersion),
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information
                    );
                    if (result == DialogResult.Yes)
                    {
                        Process.Start("https://github.com/zvukoper/ets2_assist/releases");
                        AppendLog("User opened download page.");
                    }
                    else
                    {
                        AppendLog("User declined update.");
                    }
                }
                else
                {
                    AppendLog($"No updates: latest {latestVersion} <= current {currentVersion}");
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
                AppendLog($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    AppendLog($"Inner exception: {ex.InnerException.Message}");
                    AppendLog($"Inner stack: {ex.InnerException.StackTrace}");
                }

                string userMessage = lang.Get("update_check_error") ?? $"Failed to check for updates: {ex.Message}";
                MessageBox.Show(
                    userMessage,
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
        }

        private void ShowHelp()
        {
            try
            {
                string helpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
                if (File.Exists(helpPath))
                {
                    Process.Start("notepad.exe", helpPath);
                }
                else
                {
                    Process.Start("https://github.com/zvukoper/ets2_assist");
                }
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
                    UnregisterHotKey(this.Handle, HOTKEY_ID);
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
            catch
            {
                return false;
            }
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
            catch
            {
                return false;
            }
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
                btnStart.BackColor = System.Drawing.Color.LightCoral;
            }
            else
            {
                btnStart.Text = lang.Get("btn_start") ?? "Start";
                btnStart.BackColor = System.Drawing.Color.LightGreen;
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
            bool gameRunning = IsEts2ProcessRunning();
            bool dataFresh = IsTelemetryDataFresh() && gameRunning;
            int speed = GetCurrentSpeed();

            SetStatusText(indicatorEts2Assist, "ETS2 Assist",
                mainScript ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF",
                mainScript);

            SetStatusText(indicatorEts2, "ETS2",
                gameRunning ? lang.Get("status_running") ?? "RUNNING" : lang.Get("status_not_running") ?? "NOT RUNNING",
                gameRunning);

            SetStatusText(indicatorEts2Plugins, "ETS2 Plugins",
                plugins ? lang.Get("status_installed") ?? "INSTALLED" : lang.Get("status_not_installed") ?? "NOT INSTALLED",
                plugins);

            if (gameRunning && dataFresh)
            {
                int currentSpeed = GetCurrentSpeed();
                string speedText = currentSpeed >= 0 ? $"{currentSpeed} km/h" : "0 km/h";
                SetStatusText(indicatorTruckTel, "TruckTel", speedText, true);
            }
            else
            {
                SetStatusText(indicatorTruckTel, "TruckTel", lang.Get("status_no_data") ?? "NO DATA", false);
            }

            SetStatusText(indicatorEts2Telemetry, "ETS2 Telemetry",
                dataFresh ? lang.Get("status_receiving") ?? "RECEIVING" : lang.Get("status_no_data") ?? "NO DATA",
                dataFresh);

            SetStatusText(indicatorWebServer, "Web Server",
                python ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF",
                python);

            SetStatusText(indicatorWebOverlay, "Web Overlay",
                webOverlay ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF",
                webOverlay);

            SetStatusText(indicatorArduino, "Arduino",
                arduino ? lang.Get("status_connected") ?? "CONNECTED" : lang.Get("status_none") ?? "NONE",
                arduino);
        }

        private bool IsEts2ProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName("eurotrucks2").Length > 0;
            }
            catch
            {
                return false;
            }
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
        // ГОРЯЧАЯ КЛАВИША + ПРОВЕРКА ПАУЗЫ
        // ================================================================
        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (IsGamePaused())
                {
                    TriggerTrailSave();
                }
                else
                {
                    AppendLog("Hotkey ignored: game is not paused.");
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
                AppendLog("Trail save triggered.");
                trayIcon.ShowBalloonTip(2000, "ETS2 Assist", "Сохранение трека инициировано.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to trigger trail save: {ex.Message}");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (hotKeyRegistered)
                UnregisterHotKey(this.Handle, HOTKEY_ID);
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