using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    /// <summary>
    /// Optional AR1 diagnostics which are useful during calibration but must be
    /// hidden for normal gameplay. Stored separately so old appsettings files do
    /// not need a migration and the defaults remain safely OFF.
    /// </summary>
    public static class ArDebugOverlaySettings
    {
        private static readonly string SettingsFile =
            Path.Combine(AppDataPaths.UserDataDirectory, "ar_debug_overlay.json");

        public static bool ShowTopStatus { get; set; } = false;
        public static bool ShowGroundDiagnostic { get; set; } = false;

        static ArDebugOverlaySettings()
        {
            AppDataPaths.EnsureUserData();
            Load();
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsFile));
                if (data == null) return;
                ShowTopStatus = data.ShowTopStatus;
                ShowGroundDiagnostic = data.ShowGroundDiagnostic;
            }
            catch
            {
                ShowTopStatus = false;
                ShowGroundDiagnostic = false;
            }
        }

        public static void Save()
        {
            try
            {
                var data = new SettingsData
                {
                    ShowTopStatus = ShowTopStatus,
                    ShowGroundDiagnostic = ShowGroundDiagnostic
                };
                File.WriteAllText(SettingsFile,
                    JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private sealed class SettingsData
        {
            public bool ShowTopStatus { get; set; }
            public bool ShowGroundDiagnostic { get; set; }
        }
    }

    /// <summary>
    /// Adds the optional AR diagnostic switches to the existing "Настройки АР"
    /// menu without duplicating the large MainForm menu implementation.
    /// </summary>
    internal static class ArDebugOverlayController
    {
        private static bool _installed;
        private static Timer? _syncTimer;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            Application.Idle += OnIdle;
        }

        private static void OnIdle(object? sender, EventArgs e)
        {
            if (_installed) return;

            try
            {
                var form = Application.OpenForms.Cast<Form>().OfType<MainForm>().FirstOrDefault();
                if (form == null || form.IsDisposed) return;

                var menu = form.Controls.OfType<MenuStrip>().FirstOrDefault();
                var arMenu = menu?.Items.OfType<ToolStripMenuItem>()
                    .FirstOrDefault(x => string.Equals(x.Text, "Настройки АР", StringComparison.OrdinalIgnoreCase));
                if (arMenu == null) return;

                Install(form, arMenu);
                _installed = true;
                Application.Idle -= OnIdle;
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[AR] debug menu install error: {ex.Message}");
            }
        }

        private static void Install(MainForm form, ToolStripMenuItem arMenu)
        {
            var diagnostics = new ToolStripMenuItem("Отладочная информация AR")
            {
                ToolTipText = "Дополнительные элементы AR1 для калибровки. По умолчанию скрыты."
            };

            var topStatus = new ToolStripMenuItem("Показывать верхнюю плашку AR (точки в радиусе)")
            {
                CheckOnClick = true,
                Checked = ArDebugOverlaySettings.ShowTopStatus,
                ToolTipText = "Показывает диагностический статус сверху AR1: число точек и радиус."
            };
            topStatus.CheckedChanged += (_, _) =>
            {
                ArDebugOverlaySettings.ShowTopStatus = topStatus.Checked;
                ArDebugOverlaySettings.Save();
                SendArDebugView(form);
            };

            var ground = new ToolStripMenuItem("Показывать микроточку и дистанцию прицела")
            {
                CheckOnClick = true,
                Checked = ArDebugOverlaySettings.ShowGroundDiagnostic,
                ToolTipText = "Показывает серый диагностический микрокрестик и расстояние до земли в центре AR1."
            };
            ground.CheckedChanged += (_, _) =>
            {
                ArDebugOverlaySettings.ShowGroundDiagnostic = ground.Checked;
                ArDebugOverlaySettings.Save();
                SendArDebugView(form);
            };

            diagnostics.DropDownItems.Add(topStatus);
            diagnostics.DropDownItems.Add(ground);
            arMenu.DropDownItems.Add(new ToolStripSeparator());
            arMenu.DropDownItems.Add(diagnostics);

            // The AR page can be created after this menu has been installed. A
            // low-rate sync guarantees a newly-created WebOverlay receives the
            // diagnostic flags as soon as AR1 is running, without touching the
            // normal high-rate telemetry path.
            _syncTimer = new Timer { Interval = 1000 };
            _syncTimer.Tick += (_, _) =>
            {
                try
                {
                    if (form.IsDisposed) return;
                    if (form.GetType().GetField("_ar1Running",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.GetValue(form) is bool running && running)
                    {
                        SendArDebugView(form);
                    }
                }
                catch { }
            };
            _syncTimer.Start();
            SendArDebugView(form);
        }

        private static void SendArDebugView(MainForm form)
        {
            try
            {
                // Keep the same ar_view contract as MainForm.SendArViewToPage and
                // add only the two optional diagnostic flags consumed by
                // ar_debug_view.js.
                form.GetType().GetMethod("SendCommandToMap",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(form, new object?[]
                    {
                        "ar_view",
                        new JObject
                        {
                            ["grid"] = AppSettings.Ar1ShowGrid,
                            ["axes"] = AppSettings.Ar1ShowChassisAxes,
                            ["horizon"] = AppSettings.Ar1ShowHorizon,
                            ["planeHorizon"] = AppSettings.Ar1ShowPlaneHorizon,
                            ["radiusM"] = ReadArRadius(form),
                            ["smooth"] = AppSettings.Ar1SmoothCamera,
                            ["smoothTau"] = AppSettings.Ar1SmoothTau,
                            ["showDebugStatus"] = ArDebugOverlaySettings.ShowTopStatus,
                            ["showGroundDiagnostic"] = ArDebugOverlaySettings.ShowGroundDiagnostic
                        }
                    });
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[AR] debug view send error: {ex.Message}");
            }
        }

        private static int ReadArRadius(MainForm form)
        {
            try
            {
                return form.GetType().GetProperty("ArDisplayRadiusM",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    ?.GetValue(form) is int value ? value : 50;
            }
            catch { return 50; }
        }
    }
}
