using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Threading.Tasks;
using System.Management;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

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

        private RichTextBox logConsole = null!;
        private Panel indicatorsPanel = null!;

        // Индикаторы
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

        public MainForm()
        {
            this.Text = "ETS2 Assist";
            this.Size = new Size(980, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); } };

            InitializeComponents();
            InitializeTray();
            InitializeLanguage();
            InitializeProcessManager();
            InitializeStatusTimer();
            ApplyLanguage();
            RefreshUI();

            if (AppSettings.AutoStartSystem)
            {
                Task.Run(async () => await StartSystemAsync());
            }

            if (AppSettings.StartMinimized)
            {
                this.Hide();
            }
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

            int consoleLeft = leftX + 140;
            int consoleWidth = this.ClientSize.Width - consoleLeft - 190;
            logConsole = new RichTextBox
            {
                Location = new Point(consoleLeft, topY),
                Size = new Size(consoleWidth, this.ClientSize.Height - topY - 40),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            logConsole.DoubleClick += (s, e) => OpenLogFolder();

            indicatorsPanel = new Panel
            {
                Location = new Point(consoleLeft + consoleWidth + 10, topY),
                Size = new Size(180, this.ClientSize.Height - topY - 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
            };

            int indicatorTop = 10;
            int step = 32;

            // Индикаторы в новом порядке
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
                btnStart, btnStop, btnRestartOverlay, btnMinimize, btnExit,
                logConsole, indicatorsPanel, mainMenu
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

        private void InitializeLanguage()
        {
            lang = LanguageManager.Instance;
            lang.LanguageChanged += OnLanguageChanged;
            string currentLang = AppSettings.Language;
            if (!string.IsNullOrEmpty(currentLang))
                lang.LoadLanguage(currentLang);
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
            btnStart.Text = lang.Get("ui_start") ?? "Start";
            btnStop.Text = lang.Get("ui_stop") ?? "Stop";
            btnRestartOverlay.Text = lang.Get("ui_restart_overlay") ?? "Restart Overlay";
            btnMinimize.Text = lang.Get("ui_minimize") ?? "Minimize";
            btnExit.Text = lang.Get("ui_exit") ?? "Exit";
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

            await procManager.StartAsync();

            bool dataReceived = false;
            for (int i = 0; i < 45; i++)
            {
                if (IsGameDataAvailable())
                {
                    dataReceived = true;
                    break;
                }
                await Task.Delay(1000);
            }

            if (!dataReceived)
            {
                AppendLog("Game data not received within 45 seconds. Overlay not started.");
                return;
            }

            StartWebOverlay();
            AppendLog("System started successfully.");
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

        private bool IsGameDataAvailable()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "web_data.json");
                if (!File.Exists(jsonPath)) return false;
                var json = File.ReadAllText(jsonPath);
                var obj = JObject.Parse(json);
                bool connected = obj["game"]?["connected"]?.Value<bool>() ?? false;
                bool hasSpeed = obj["speed"] != null;
                return connected && hasSpeed;
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
                string url = "http://localhost:8082/web_ui_hybrid.html";
                Process.Start(overlayExe, url);
                AppendLog("Web overlay started.");
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
            AppendLog("Checking for updates...");
            try
            {
                var latestVersion = await Updater.CheckLatestVersion(AppSettings.GitHubRepoUrl);
                if (latestVersion == null)
                {
                    MessageBox.Show(
                        lang.Get("update_check_error") ?? "Failed to check for updates.",
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion == null || latestVersion > currentVersion)
                {
                    DialogResult result = MessageBox.Show(
                        string.Format(lang.Get("update_available") ?? "New version {0} is available. Current version: {1}. Open download page?", latestVersion, currentVersion ?? new Version(0, 0, 0)),
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information
                    );
                    if (result == DialogResult.Yes)
                    {
                        Process.Start("https://github.com/zvukoper/ets2_assist/releases");
                    }
                }
                else
                {
                    MessageBox.Show(
                        string.Format(lang.Get("update_no_updates") ?? "You have the latest version ({0}).", currentVersion ?? new Version(0, 0, 0)),
                        lang.Get("update_check_title") ?? "Check Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Update check error: {ex.Message}");
                MessageBox.Show(
                    lang.Get("update_check_error") ?? $"Failed to check for updates: {ex.Message}",
                    lang.Get("update_check_title") ?? "Check Updates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
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
                    // Если файла нет, открываем GitHub README
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

        private void UpdateIndicators()
        {
            bool arduino = !string.IsNullOrEmpty(GetArduinoPort());
            bool plugins = ArePluginsInstalled();
            bool python = Process.GetProcessesByName("python").Length > 0 || Process.GetProcessesByName("pythonw").Length > 0;
            bool webOverlay = Process.GetProcessesByName("WebOverlay").Length > 0 || Process.GetProcessesByName("pano").Length > 0;
            bool mainScript = procManager.IsRunning;
            bool gameRunning = IsGameRunning();
            bool dataAvailable = IsGameDataAvailable() && gameRunning;
            int speed = GetCurrentSpeed();

            // ETS2 Assist (Main Script)
            SetStatusText(indicatorEts2Assist, "ETS2 Assist", mainScript ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF", mainScript);

            // ETS2
            SetStatusText(indicatorEts2, "ETS2", gameRunning ? lang.Get("status_running") ?? "RUNNING" : lang.Get("status_not_running") ?? "NOT RUNNING", gameRunning);

            // ETS2 Plugins
            SetStatusText(indicatorEts2Plugins, "ETS2 Plugins", plugins ? lang.Get("status_installed") ?? "INSTALLED" : lang.Get("status_not_installed") ?? "NOT INSTALLED", plugins);

            // TruckTel
            if (gameRunning)
            {
                if (dataAvailable && speed >= 0)
                    SetStatusText(indicatorTruckTel, "TruckTel", $"{speed} km/h", true);
                else
                    SetStatusText(indicatorTruckTel, "TruckTel", lang.Get("status_no_data") ?? "NO DATA", false);
            }
            else
            {
                SetStatusText(indicatorTruckTel, "TruckTel", lang.Get("status_no_data") ?? "NO DATA", false);
            }

            // ETS2 Telemetry
            SetStatusText(indicatorEts2Telemetry, "ETS2 Telemetry", dataAvailable ? lang.Get("status_receiving") ?? "RECEIVING" : lang.Get("status_no_data") ?? "NO DATA", dataAvailable);

            // Web Server
            SetStatusText(indicatorWebServer, "Web Server", python ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF", python);

            // Web Overlay
            SetStatusText(indicatorWebOverlay, "Web Overlay", webOverlay ? lang.Get("status_on") ?? "ON" : lang.Get("status_off") ?? "OFF", webOverlay);

            // Arduino
            SetStatusText(indicatorArduino, "Arduino", arduino ? lang.Get("status_connected") ?? "CONNECTED" : lang.Get("status_none") ?? "NONE", arduino);
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