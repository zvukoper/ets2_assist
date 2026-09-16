using System;
using System.IO;
using System.Text.Json;

namespace ETS2_Assist_GUI
{
    public static class AppSettings
    {
        private static readonly string SettingsFile = Path.Combine(AppDataPaths.UserDataDirectory, "appsettings.json");

        public static bool DebugMode { get; set; } = false;
        public static string Language { get; set; } = "en";
        public static bool AutoStartSystem { get; set; } = false;
        public static bool StartMinimized { get; set; } = false;
        public static bool CheckUpdatesOnStart { get; set; } = true;
        public static string GitHubRepoUrl { get; set; } = "https://api.github.com/repos/zvukoper/ets2_assist/releases/latest";

        public static int? WindowX { get; set; }
        public static int? WindowY { get; set; }
        public static int? WindowWidth { get; set; }
        public static int? WindowHeight { get; set; }
        public static string? WindowDeviceName { get; set; }

        // v1.0.40.26: интервал обновления метки грузовика в Map Editor 2 (мс).
        // Задаётся полем ввода рядом с кнопкой «Найти грузовик».
        public static int TruckIntervalMs { get; set; } = 1000;

        // v1.0.40.27: горизонтальный FOV страницы AR1 (web_ar_hud.html), градусы.
        // Меняется CTRL+PGUP/PGDN (шаг 1°), НЕ влияет на FOV AR2 (D3D).
        // v1.0.40.39: значение по умолчанию 105° (подобрано пользователем).
        public static double Ar1FovDeg { get; set; } = 105.0;

        // ================================================================
        // v1.0.40.34: ВЕРТИКАЛЬНЫЙ FOV AR1 — НЕЗАВИСИМАЯ настройка (градусы).
        // Пользователь: «Вертикальный фов по умолчанию пока делаем 64,5» —
        // подстраивается Ctrl+Shift+PGUP/PGDN шагом 0.2° и сохраняется.
        // ВАЖНО: это НЕ производная от горизонтального. При «одном focal по X и Y»
        // вертикаль была бы 2·atan(tan(hFov/2)/aspect) ≈ 59.6° при hFov 91°;
        // реальное значение проверяется глазами по совпадению линии горизонта.
        // ================================================================
        // v1.0.40.39: значение по умолчанию 65° (подобрано пользователем).
        public static double Ar1FovVerticalDeg { get; set; } = 65.0;

        // ================================================================
        // v1.0.40.40: НАСТРОЙКИ ВИДА AR (меню «Вид»).
        //
        // Требование пользователя: «Вынеси АР сетку, высоты и оси в настройки меню
        // вид. Чтобы при желании можно было включить и ещё отлаживать. Убираем оси,
        // сетку, визуализацию высот.» ⇒ всё это по умолчанию ВЫКЛЮЧЕНО.
        // ================================================================
        public static bool Ar1ShowGrid { get; set; } = false;
        public static bool Ar1ShowChassisAxes { get; set; } = false;
        public static bool Ar1ShowHorizon { get; set; } = false;
        public static bool Ar1ShowPlaneHorizon { get; set; } = false;

        /// <summary>Окно визуализации высот (отдельный оверлей WebOverlay).</summary>
        public static bool ShowHeightsWindow { get; set; } = false;

        /// <summary>Радиус показа точек в AR, м (требование: 50).</summary>
        public static int ArDisplayRadiusM { get; set; } = 50;

        // ================================================================
        // v1.0.40.44: ПЛАВНОСТЬ ДВИЖЕНИЯ ТОЧЕК (сглаживание позы камеры).
        // Телеметрия приходит ~28–35 Гц, рендер идёт 60+ Гц: без сглаживания
        // точки стояли между пакетами и «прыгали» (визуально как 14 fps).
        // Ar1SmoothTau — постоянная времени сглаживания, с.
        // ================================================================
        public static bool Ar1SmoothCamera { get; set; } = true;
        public static double Ar1SmoothTau { get; set; } = 0.035;

        static AppSettings()
        {
            AppDataPaths.EnsureUserData();
            Load();
        }

        public static void Load()
        {
            if (!File.Exists(SettingsFile)) return;
            try
            {
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                if (settings != null)
                {
                    DebugMode = settings.DebugMode;
                    Language = settings.Language ?? "en";
                    AutoStartSystem = settings.AutoStartSystem;
                    StartMinimized = settings.StartMinimized;
                    CheckUpdatesOnStart = settings.CheckUpdatesOnStart;
                    GitHubRepoUrl = settings.GitHubRepoUrl ?? "https://api.github.com/repos/zvukoper/ets2_assist/releases/latest";
                    WindowX = settings.WindowX;
                    WindowY = settings.WindowY;
                    WindowWidth = settings.WindowWidth;
                    WindowHeight = settings.WindowHeight;
                    WindowDeviceName = settings.WindowDeviceName;
                    TruckIntervalMs = settings.TruckIntervalMs > 0 ? settings.TruckIntervalMs : 1000;
                    Ar1FovDeg = settings.Ar1FovDeg is >= 30.0 and <= 150.0 ? settings.Ar1FovDeg : 105.0;
                    Ar1FovVerticalDeg = settings.Ar1FovVerticalDeg is >= 10.0 and <= 150.0
                        ? settings.Ar1FovVerticalDeg : 65.0;
                    // v1.0.40.40: вид AR + радиус показа точек.
                    Ar1ShowGrid = settings.Ar1ShowGrid;
                    Ar1ShowChassisAxes = settings.Ar1ShowChassisAxes;
                    Ar1ShowHorizon = settings.Ar1ShowHorizon;
                    Ar1ShowPlaneHorizon = settings.Ar1ShowPlaneHorizon;
                    ShowHeightsWindow = settings.ShowHeightsWindow;
                    ArDisplayRadiusM = settings.ArDisplayRadiusM is >= 5 and <= 5000
                        ? settings.ArDisplayRadiusM : 50;
                    // v1.0.40.44: плавность движения точек.
                    Ar1SmoothCamera = settings.Ar1SmoothCamera;
                    Ar1SmoothTau = settings.Ar1SmoothTau is >= 0.005 and <= 0.5
                        ? settings.Ar1SmoothTau : 0.035;
                }
            }
            catch { /* ignore errors */ }
        }

        public static void Save()
        {
            try
            {
                var settings = new SettingsData
                {
                    DebugMode = DebugMode,
                    Language = Language,
                    AutoStartSystem = AutoStartSystem,
                    StartMinimized = StartMinimized,
                    CheckUpdatesOnStart = CheckUpdatesOnStart,
                    GitHubRepoUrl = GitHubRepoUrl,
                    WindowX = WindowX,
                    WindowY = WindowY,
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight,
                    WindowDeviceName = WindowDeviceName,
                    TruckIntervalMs = TruckIntervalMs,
                    Ar1FovDeg = Ar1FovDeg,
                    Ar1FovVerticalDeg = Ar1FovVerticalDeg,
                    // v1.0.40.40: настройки вида AR (меню «Вид») + радиус показа точек.
                    Ar1ShowGrid = Ar1ShowGrid,
                    Ar1ShowChassisAxes = Ar1ShowChassisAxes,
                    Ar1ShowHorizon = Ar1ShowHorizon,
                    Ar1ShowPlaneHorizon = Ar1ShowPlaneHorizon,
                    ShowHeightsWindow = ShowHeightsWindow,
                    ArDisplayRadiusM = ArDisplayRadiusM,
                    // v1.0.40.44: плавность движения точек.
                    Ar1SmoothCamera = Ar1SmoothCamera,
                    Ar1SmoothTau = Ar1SmoothTau
                };
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { /* ignore */ }
        }

        private class SettingsData
        {
            public bool DebugMode { get; set; }
            public string Language { get; set; } = string.Empty;
            public bool AutoStartSystem { get; set; }
            public bool StartMinimized { get; set; }
            public bool CheckUpdatesOnStart { get; set; }
            public string GitHubRepoUrl { get; set; } = string.Empty;
            public int? WindowX { get; set; }
            public int? WindowY { get; set; }
            public int? WindowWidth { get; set; }
            public int? WindowHeight { get; set; }
            public string? WindowDeviceName { get; set; }
            public int TruckIntervalMs { get; set; } = 1000;
            // v1.0.40.27: FOV AR1 (без значения в файле остаётся 0 → подставляем 75).
            public double Ar1FovDeg { get; set; }
            // v1.0.40.34: вертикальный FOV AR1 (0 в файле → 65).
            public double Ar1FovVerticalDeg { get; set; }
            // ============================================================
            // v1.0.40.40: НАСТРОЙКИ ВИДА AR (меню «Вид»).
            // По умолчанию отладочная визуализация ВЫКЛЮЧЕНА (требование
            // пользователя: «Убираем оси, сетку, визуализацию высот»).
            // ============================================================
            public bool Ar1ShowGrid { get; set; }
            public bool Ar1ShowChassisAxes { get; set; }
            public bool Ar1ShowHorizon { get; set; }
            public bool Ar1ShowPlaneHorizon { get; set; }
            public bool ShowHeightsWindow { get; set; }
            public int ArDisplayRadiusM { get; set; }
            // v1.0.40.44: сглаживание позы камеры (плавность точек).
            public bool Ar1SmoothCamera { get; set; }
            public double Ar1SmoothTau { get; set; }
        }
    }
}