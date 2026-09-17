$ErrorActionPreference = 'Stop'

function Replace-Between([string]$Text, [string]$StartMarker, [string]$EndMarker, [string]$Replacement, [string]$Name) {
    $start = $Text.IndexOf($StartMarker, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Start marker not found: $Name" }
    $end = $Text.IndexOf($EndMarker, $start + $StartMarker.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { throw "End marker not found: $Name" }
    return $Text.Substring(0, $start) + $Replacement + $Text.Substring($end)
}

$path = 'MainForm.cs'
$text = Get-Content -Raw -Encoding UTF8 $path

$geometry = @'
        private static void ComputeOverlayGeometry(string url, System.Drawing.Rectangle bounds, out int x, out int y, out int w, out int h)
        {
            string normalized = (url ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("web_ar_hud.html") || normalized.Contains("web_quests.html") || normalized.Contains("web_notifications.html"))
            { x = bounds.X; y = bounds.Y; w = bounds.Width; h = bounds.Height; return; }
            if (normalized.Contains("web_pda_map.html"))
            {
                int side = Math.Max(100, (int)Math.Round(bounds.Height * 0.30));
                side = Math.Min(side, Math.Min(bounds.Width, bounds.Height));
                w = h = side; x = bounds.Left; y = bounds.Bottom - h; return;
            }
            if (normalized.Contains("web_ui_hybrid.html"))
            {
                w = Math.Max(100, (int)Math.Round(bounds.Width * 0.42));
                h = Math.Max(100, (int)Math.Round(bounds.Height * 0.32));
                w = Math.Min(w, bounds.Width); h = Math.Min(h, bounds.Height);
                x = bounds.Left + (bounds.Width - w) / 2; y = bounds.Bottom - h; return;
            }
            if (normalized.Contains("web_pause_logo.html"))
            {
                int side = Math.Max(100, (int)Math.Round(bounds.Height * 0.18));
                side = Math.Min(side, Math.Min(bounds.Width, bounds.Height));
                w = h = side; x = bounds.Right - w; y = bounds.Top; return;
            }
            if (normalized.Contains("web_heights.html"))
            {
                w = Math.Max(100, (int)Math.Round(bounds.Width * 0.34));
                h = Math.Max(100, (int)Math.Round(bounds.Height * 0.30));
                w = Math.Min(w, bounds.Width); h = Math.Min(h, bounds.Height);
                x = bounds.Right - w; y = bounds.Top; return;
            }
            w = 800; h = 600;
            x = bounds.Left + Math.Max(0, (bounds.Width - w) / 2);
            y = bounds.Top + Math.Max(0, (bounds.Height - h) / 2);
        }

'@
$text = Replace-Between $text '        private static void ComputeOverlayGeometry' '        private void EnsureOverlayWindowConfig' ($geometry) 'ComputeOverlayGeometry'

$configAndStart = @'
        private static bool TryReadOverlayState(string path, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 5) return false;
                x = int.Parse(lines[0]); y = int.Parse(lines[1]);
                w = int.Parse(lines[3]); h = int.Parse(lines[4]);
                return true;
            }
            catch { return false; }
        }

        private static bool IsKnownLegacyOverlayDefault(string url, int x, int y, int w, int h)
        {
            string n = (url ?? string.Empty).ToLowerInvariant();
            return (n.Contains("web_pda_map.html") && x == 130 && y == 130 && w == 331 && h == 331) ||
                   (n.Contains("web_ui_hybrid.html") && x == 208 && y == 208 && w == 859 && h == 465) ||
                   (n.Contains("web_pause_logo.html") && x == 156 && y == 156 && w == 207 && h == 207);
        }

        private void EnsureOverlayWindowConfig()
        {
            try
            {
                string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WebOverlay", "config");
                Directory.CreateDirectory(configDir);
                Screen screen = GetGameScreen() ?? Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
                if (screen == null) { AppendLog("[OVERLAY][ERROR] Не найден экран игры."); return; }

                var bounds = screen.Bounds;
                AppendLog($"[OVERLAY] Экран игры для дефолтов: {screen.DeviceName} bounds={bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
                var urls = new[]
                {
                    "http://localhost:8082/web_pda_map.html",
                    "http://localhost:8082/web_ui_hybrid.html",
                    "http://localhost:8082/web_pause_logo.html",
                    "http://localhost:8082/web_heights.html",
                    "http://localhost:8082/web_quests.html",
                    "http://localhost:8082/web_notifications.html",
                    "http://localhost:8082/web_ar_hud.html"
                };

                foreach (string url in urls)
                {
                    string file = Path.Combine(configDir, OverlayStateFileName(url));
                    bool fullscreen = url.Contains("web_ar_hud.html", StringComparison.OrdinalIgnoreCase) || url.Contains("web_quests.html", StringComparison.OrdinalIgnoreCase) || url.Contains("web_notifications.html", StringComparison.OrdinalIgnoreCase);
                    bool write = !File.Exists(file);
                    if (!write && TryReadOverlayState(file, out int oldX, out int oldY, out int oldW, out int oldH))
                    {
                        if (fullscreen) write = oldX != bounds.X || oldY != bounds.Y || oldW != bounds.Width || oldH != bounds.Height;
                        else if (IsKnownLegacyOverlayDefault(url, oldX, oldY, oldW, oldH)) write = true;
                    }
                    if (!write) continue;
                    ComputeOverlayGeometry(url, bounds, out int x, out int y, out int w, out int h);
                    File.WriteAllLines(file, new[] { x.ToString(), y.ToString(), "1", w.ToString(), h.ToString() }, Encoding.UTF8);
                    AppendLog($"[OVERLAY] Записана геометрия {Path.GetFileName(file)}: X={x}, Y={y}, {w}x{h}");
                }
            }
            catch (Exception ex) { AppendLog($"[OVERLAY] Ошибка создания конфигурации позиций: {ex.Message}"); }
        }

        private void StartWebOverlay()
        {
            string overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            if (!File.Exists(overlayExe)) overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "pano.exe");
            if (!File.Exists(overlayExe)) { AppendLog("WebOverlay executable not found."); return; }
            try
            {
                EnsureOverlayWindowConfig();
                foreach (var proc in Process.GetProcessesByName("WebOverlay")) { try { proc.Kill(); proc.WaitForExit(1500); } catch { } }
                foreach (var proc in Process.GetProcessesByName("pano")) { try { proc.Kill(); proc.WaitForExit(1500); } catch { } }

                string urlMain = "http://localhost:8082/web_ui_hybrid.html";
                string urlPda = "http://localhost:8082/web_pda_map.html";
                string urlPauseLogo = "http://localhost:8082/web_pause_logo.html";
                string urlQuests = "http://localhost:8082/web_quests.html";
                string urlNotifications = "http://localhost:8082/web_notifications.html";
                string urlHeights = "http://localhost:8082/web_heights.html";

                Process.Start(overlayExe, urlMain); Thread.Sleep(500);
                Process.Start(overlayExe, $"append {urlPda}"); Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlPauseLogo}"); Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlQuests}"); Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlNotifications}"); Thread.Sleep(200);
                AppendLog("[OVERLAY] Hybrid, minimap, mini-logo, quest and notification layers started.");
                if (AppSettings.ShowHeightsWindow)
                    Process.Start(overlayExe, $"append {urlHeights}");
            }
            catch (Exception ex) { AppendLog($"Failed to start overlay: {ex.Message}"); }
        }

'@
$text = Replace-Between $text '        private void EnsureOverlayWindowConfig' '        // AR HUD:' $configAndStart 'overlay config/startup'

# AR1 state and targeted close. Ensure field exists exactly once.
if ($text -notmatch 'private bool _ar1Running;') {
    $text = $text.Replace('        private void ToggleArOverlay()', '        private bool _ar1Running;' + [Environment]::NewLine + [Environment]::NewLine + '        private void ToggleArOverlay()')
}
$text = Replace-Between $text '        internal bool IsAr1Running' '        // Остановка AR1' @'
        internal bool IsAr1Running => _ar1Running;

'@ 'AR running state'
$text = Replace-Between $text '        internal void StopArOverlay(bool manual)' '        // v1.0.40.27: тоггл-подсветка' @'
        internal void StopArOverlay(bool manual)
        {
            string overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            if (!File.Exists(overlayExe)) overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "pano.exe");
            try { if (File.Exists(overlayExe)) Process.Start(overlayExe, "close http://localhost:8082/web_ar_hud.html"); }
            catch (Exception ex) { AppendLog($"[AR] Не удалось закрыть AR HUD: {ex.Message}"); }
            _ar1Running = false;
            if (!IsAr2Running) StopArTargetFeed();
            SyncAr1Button();
            AppendLog(manual ? "[AR] AR HUD закрывается кнопкой." : "[AR] AR HUD закрывается.");
        }

'@ 'AR targeted stop'

if ($text.Contains('                // Если AR уже запущен')) {
    $text = Replace-Between $text '                // Если AR уже запущен' '                StartArTargetFeed();' @'
                Process.Start(overlayExe, url);
                _ar1Running = true;
'@ 'remove AR process kill'
} elseif ($text -notmatch '_ar1Running = true;') {
    $text = $text.Replace('                Process.Start(overlayExe, url);' + [Environment]::NewLine + '                StartArTargetFeed();', '                Process.Start(overlayExe, url);' + [Environment]::NewLine + '                _ar1Running = true;' + [Environment]::NewLine + '                StartArTargetFeed();')
}
Set-Content -Path $path -Value $text -Encoding UTF8

# QuestRuntime now only publishes state; MainForm owns the permanent fullscreen page.
$path = 'Quests/QuestRuntime.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
$text = $text -replace '        private bool _overlayVisible;\s*\r?\n', ''
$text = $text -replace '        private Process\? _overlayProcess;\s*\r?\n', ''
$text = Replace-Between $text '        private async Task UpdateOverlayAsync()' '        private void EnforceArPointDebugMode' @'
        private Task UpdateOverlayAsync()
        {
            return Task.CompletedTask;
        }

'@ 'QuestRuntime overlay lifecycle'
Set-Content -Path $path -Value $text -Encoding UTF8

# Future notification effects can select top/left/right.
$path = 'Quests/QuestModels.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
if ($text -notmatch 'NotifyPosition') {
    $text = $text.Replace('public string NotifyTitle { get; set; } = ""; public string NotifyText { get; set; } = ""; public List<string> ResetQuests', 'public string NotifyTitle { get; set; } = ""; public string NotifyText { get; set; } = ""; public string NotifyPosition { get; set; } = "top"; public List<string> ResetQuests')
}
Set-Content -Path $path -Value $text -Encoding UTF8

$path = 'Quests/QuestRuntime.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
$text = $text.Replace('["title"]=effect.NotifyTitle ?? "", ["text"]=effect.NotifyText, ["icon"]="" } });', '["title"]=effect.NotifyTitle ?? "", ["text"]=effect.NotifyText, ["icon"]="", ["position"]=(effect.NotifyPosition ?? "top") } });')
Set-Content -Path $path -Value $text -Encoding UTF8

$main = Get-Content -Raw -Encoding UTF8 MainForm.cs
$qr = Get-Content -Raw -Encoding UTF8 Quests/QuestRuntime.cs
$models = Get-Content -Raw -Encoding UTF8 Quests/QuestModels.cs
if ($main -match 'Contains\("AR HUD"\).*proc\.Kill') { throw 'AR still kills WebOverlay host process' }
if ($main -notmatch 'web_quests\.html' -or $main -notmatch 'web_notifications\.html') { throw 'Quest/notification overlay startup missing' }
if ($main -notmatch 'Screen screen = GetGameScreen\(\)') { throw 'Game-screen geometry missing' }
if ($qr -match 'private async Task UpdateOverlayAsync') { throw 'Old async overlay lifecycle remains' }
if ($models -notmatch 'NotifyPosition') { throw 'NotifyPosition missing' }
Write-Host 'Quest overlay source patch applied and validated.'
