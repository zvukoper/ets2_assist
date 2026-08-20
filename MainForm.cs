using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using ETS2_Assist_GUI.Core;
using ETS2_Assist_GUI.UI;
using ETS2_Assist_GUI.Storage;
using ETS2_Assist_GUI.Input;

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
        private Button btnRefreshTracks = null!;

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

        // Менеджеры и контроллер
        private ApplicationController _appController = null!;
        private TrackListManager _trackListManager = null!;
        private Logger _logger = null!;
        private LanguageManager _lang = null!;

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

            // Инициализация базовых сервисов
            _logger = new Logger();
            _lang = LanguageManager.Instance;
            _lang.LanguageChanged += (s, e) => ApplyLanguage();

            // Создаём контроллер приложения
            _appController = new ApplicationController(this.Handle, _logger);
            _appController.OnStarted += OnSystemStarted;
            _appController.OnStopped += OnSystemStopped;

            // Менеджер списка треков
            _trackListManager = new TrackListManager(_logger);

            InitializeComponents();
            InitializeTray();
            ApplyLanguage();
            RefreshUI();

            // Регистрируем хоткеи (через контроллер)
            _appController.HotkeyManager.SetAction(HotkeyManager.HOTKEY_SAVE, () => _appController.TriggerTrailSave());
            _appController.HotkeyManager.SetAction(HotkeyManager.HOTKEY_START_RECORD, () => _appController.StartRecording());
            _appController.HotkeyManager.SetAction(HotkeyManager.HOTKEY_STOP_RECORD, () => _appController.StopRecording());
            _appController.HotkeyManager.SetAction(HotkeyManager.HOTKEY_ADD_MARKER, () => _appController.AddMarkerFromHotkey());
            _appController.HotkeyManager.SetAction(HotkeyManager.HOTKEY_TEST_WINDOW, () => _appController.ShowTestWindow());

            if (AppSettings.AutoStartSystem)
            {
                System.Threading.Tasks.Task.Run(async () => await _appController.StartAsync());
            }

            if (AppSettings.StartMinimized)
            {
                this.Hide();
            }

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

            btnStart = new Button { Text = "Start", Location = new Point(leftX, topY), Size = new Size(120, 30) };
            btnStart.Click += (s, e) => StartSystem();

            btnStop = new Button { Text = "Stop", Location = new Point(leftX, topY + 40), Size = new Size(120, 30), Enabled = false };
            btnStop.Click += (s, e) => StopSystem();

            btnRestartOverlay = new Button { Text = "Restart Overlay", Location = new Point(leftX, topY + 80), Size = new Size(120, 30) };
            btnRestartOverlay.Click += (s, e) => _appController.RestartOverlay();

            btnMinimize = new Button { Text = "Minimize", Location = new Point(leftX, topY + 120), Size = new Size(120, 30) };
            btnMinimize.Click += (s, e) => this.Hide();

            btnExit = new Button { Text = "Exit", Location = new Point(leftX, topY + 160), Size = new Size(120, 30) };
            btnExit.Click += (s, e) => ConfirmExit();

            btnRefreshTracks = new Button { Text = "Обновить список", Location = new Point(leftX, topY + 210), Size = new Size(120, 30) };
            btnRefreshTracks.Click += (s, e) => RefreshTrackList();

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

        private void ApplyLanguage()
        {
            this.Text = _lang.Get("app_title") ?? "ETS2 Assist";
            var version = Application.ProductVersion ?? "0.0.0";
            this.Text = $"ETS2 Assist v{version}";

            btnStart.Text = _lang.Get("ui_start") ?? "Start";
            btnStop.Text = _lang.Get("ui_stop") ?? "Stop";
            btnRestartOverlay.Text = _lang.Get("ui_restart_overlay") ?? "Restart Overlay";
            btnMinimize.Text = _lang.Get("ui_minimize") ?? "Minimize";
            btnExit.Text = _lang.Get("ui_exit") ?? "Exit";
            btnRefreshTracks.Text = "Обновить список";
            fileMenu.Text = _lang.Get("ui_file") ?? "File";
            settingsMenu.Text = _lang.Get("ui_settings") ?? "Settings";
            helpMenu.Text = _lang.Get("ui_help") ?? "Help";
            checkUpdatesMenu.Text = _lang.Get("ui_check_updates") ?? "Check Updates";
            exitMenu.Text = _lang.Get("ui_exit") ?? "Exit";
            trayMenu.Items[0].Text = _lang.Get("tray_start") ?? "Start System";
            trayMenu.Items[1].Text = _lang.Get("tray_stop") ?? "Stop System";
            trayMenu.Items[3].Text = _lang.Get("tray_check_updates") ?? "Check Updates";
            trayMenu.Items[4].Text = _lang.Get("tray_exit") ?? "Exit";
        }

        private void RefreshUI()
        {
            if (_appController.IsRunning)
            {
                btnStart.Enabled = false;
                btnStart.Text = _lang.Get("ui_starting") ?? "Starting...";
                btnStop.Enabled = true;
                btnStop.Text = _lang.Get("ui_stop") ?? "Stop";
            }
            else
            {
                btnStart.Enabled = !_appController.IsStarting;
                btnStart.Text = _lang.Get("ui_start") ?? "Start";
                btnStop.Enabled = false;
                btnStop.Text = _lang.Get("ui_stop") ?? "Stop";
            }
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            if (_appController.IsRunning && !_appController.HasErrors)
                trayIcon.Icon = SystemIcons.Application;
            else if (_appController.IsRunning && _appController.HasErrors)
                trayIcon.Icon = SystemIcons.Error;
            else
                trayIcon.Icon = SystemIcons.Application;
        }

        private async void StartSystem()
        {
            await _appController.StartAsync();
        }

        private void StopSystem()
        {
            _appController.Stop();
        }

        private void OnSystemStarted()
        {
            RefreshUI();
            AppendLog("System started successfully.");
        }

        private void OnSystemStopped()
        {
            RefreshUI();
            AppendLog("System stopped.");
        }

        private void RefreshTrackList()
        {
            _trackListManager.RefreshList(listTracks);
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                AppendLog($"Открыт трек: {fileName}");
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка открытия трека: {ex.Message}");
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
                    System.Diagnostics.Process.Start("notepad.exe", helpPath);
                }
                else
                {
                    System.Diagnostics.Process.Start("https://github.com/zvukoper/ets2_assist");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Help error: {ex.Message}");
                MessageBox.Show(
                    _lang.Get("help_error") ?? "Failed to open help.",
                    _lang.Get("help_title") ?? "Help",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void CheckUpdates()
        {
            _appController.CheckUpdates();
        }

        private void ConfirmExit()
        {
            if (MessageBox.Show(
                _lang.Get("exit_confirm") ?? "Are you sure you want to exit?",
                _lang.Get("exit_title") ?? "Exit",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                StopSystem();
                trayIcon.Visible = false;
                Application.Exit();
            }
        }

        private void OpenLogFolder()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (Directory.Exists(logDir))
                System.Diagnostics.Process.Start("explorer.exe", logDir);
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

        private void UpdateIndicators()
        {
            bool arduino = !string.IsNullOrEmpty(GetArduinoPort());
            bool plugins = ArePluginsInstalled();
            bool python = System.Diagnostics.Process.GetProcessesByName("python").Length > 0 ||
                          System.Diagnostics.Process.GetProcessesByName("pythonw").Length > 0;
            bool webOverlay = System.Diagnostics.Process.GetProcessesByName("WebOverlay").Length > 0 ||
                              System.Diagnostics.Process.GetProcessesByName("pano").Length > 0;
            bool mainScript = _appController.IsRunning;
            bool gameRunning = IsEts2ProcessRunning();
            bool dataFresh = IsTelemetryDataFresh() && gameRunning;
            int speed = GetCurrentSpeed();

            SetStatusText(indicatorEts2Assist, "ETS2 Assist",
                mainScript ? _lang.Get("status_on") ?? "ON" : _lang.Get("status_off") ?? "OFF",
                mainScript);

            SetStatusText(indicatorEts2, "ETS2",
                gameRunning ? _lang.Get("status_running") ?? "RUNNING" : _lang.Get("status_not_running") ?? "NOT RUNNING",
                gameRunning);

            SetStatusText(indicatorEts2Plugins, "ETS2 Plugins",
                plugins ? _lang.Get("status_installed") ?? "INSTALLED" : _lang.Get("status_not_installed") ?? "NOT INSTALLED",
                plugins);

            if (gameRunning && dataFresh)
            {
                int currentSpeed = GetCurrentSpeed();
                string speedText = currentSpeed >= 0 ? $"{currentSpeed} km/h" : "0 km/h";
                SetStatusText(indicatorTruckTel, "TruckTel", speedText, true);
            }
            else
            {
                SetStatusText(indicatorTruckTel, "TruckTel", _lang.Get("status_no_data") ?? "NO DATA", false);
            }

            SetStatusText(indicatorEts2Telemetry, "ETS2 Telemetry",
                dataFresh ? _lang.Get("status_receiving") ?? "RECEIVING" : _lang.Get("status_no_data") ?? "NO DATA",
                dataFresh);

            SetStatusText(indicatorWebServer, "Web Server",
                python ? _lang.Get("status_on") ?? "ON" : _lang.Get("status_off") ?? "OFF",
                python);

            SetStatusText(indicatorWebOverlay, "Web Overlay",
                webOverlay ? _lang.Get("status_on") ?? "ON" : _lang.Get("status_off") ?? "OFF",
                webOverlay);

            SetStatusText(indicatorArduino, "Arduino",
                arduino ? _lang.Get("status_connected") ?? "CONNECTED" : _lang.Get("status_none") ?? "NONE",
                arduino);
        }

        private bool IsEts2ProcessRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("eurotrucks2").Length > 0; }
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
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                foreach (var obj in searcher.Get())
                {
                    if (obj["Description"]?.ToString()?.Contains("Arduino Micro") == true)
                        return obj["DeviceID"]?.ToString();
                }
                return null;
            }
            catch { return null; }
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
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                        steamPath = key.GetValue("InstallPath") as string;
                }
                if (string.IsNullOrEmpty(steamPath))
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
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
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                        steamInstall = key.GetValue("InstallPath") as string;
                }
                if (string.IsNullOrEmpty(steamInstall))
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
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
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                return obj["speed"]?.Value<int>() ?? -1;
            }
            catch { return -1; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                trayIcon.ShowBalloonTip(1000, "ETS2 Assist", _lang.Get("tray_minimized") ?? "Application minimized to tray.", ToolTipIcon.Info);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            UpdateIndicators();
        }
    }
}