$ErrorActionPreference = 'Stop'

function Replace-Once([string]$Text, [string]$Pattern, [string]$Replacement, [string]$Name) {
    $new = [regex]::Replace($Text, $Pattern, $Replacement, [Text.RegularExpressions.RegexOptions]::Singleline)
    if ($new -eq $Text) { throw "Pattern not found: $Name" }
    return $new
}

# ---------------------------------------------------------------------------
# MainForm: calculate every overlay default from the actual ETS2 screen.
# Fullscreen layers use Screen.Bounds; normal HUD windows use the same bounds.
# Known stale hardcoded defaults from the previous implementation are migrated.
# ---------------------------------------------------------------------------
$path = 'MainForm.cs'
$text = Get-Content -Raw -Encoding UTF8 $path

$geometry = @'
        private static void ComputeOverlayGeometry(string url, System.Drawing.Rectangle bounds, out int x, out int y, out int w, out int h)
        {
            string normalized = (url ?? string.Empty).ToLowerInvariant();

            if (normalized.Contains("web_ar_hud.html") ||
                normalized.Contains("web_quests.html") ||
                normalized.Contains("web_notifications.html"))
            {
                x = bounds.X;
                y = bounds.Y;
                w = bounds.Width;
                h = bounds.Height;
                return;
            }

            if (normalized.Contains("web_pda_map.html"))
            {
                int side = Math.Max(100, (int)Math.Round(bounds.Height * 0.30));
                side = Math.Min(side, Math.Min(bounds.Width, bounds.Height));
                w = h = side;
                x = bounds.Left;
                y = bounds.Bottom - h;
                return;
            }

            if (normalized.Contains("web_ui_hybrid.html"))
            {
                w = Math.Max(100, (int)Math.Round(bounds.Width * 0.42));
                h = Math.Max(100, (int)Math.Round(bounds.Height * 0.32));
                w = Math.Min(w, bounds.Width);
                h = Math.Min(h, bounds.Height);
                x = bounds.Left + (bounds.Width - w) / 2;
                y = bounds.Bottom - h;
                return;
            }

            if (normalized.Contains("web_pause_logo.html"))
            {
                int side = Math.Max(100, (int)Math.Round(bounds.Height * 0.18));
                side = Math.Min(side, Math.Min(bounds.Width, bounds.Height));
                w = h = side;
                x = bounds.Right - w;
                y = bounds.Top;
                return;
            }

            if (normalized.Contains("web_heights.html"))
            {
                w = Math.Max(100, (int)Math.Round(bounds.Width * 0.34));
                h = Math.Max(100, (int)Math.Round(bounds.Height * 0.30));
                w = Math.Min(w, bounds.Width);
                h = Math.Min(h, bounds.Height);
                x = bounds.Right - w;
                y = bounds.Top;
                return;
            }

            w = 800;
            h = 600;
            x = bounds.Left + Math.Max(0, (bounds.Width - w) / 2);
            y = bounds.Top + Math.Max(0, (bounds.Height - h) / 2);
        }

        private static bool TryReadOverlayState(string path, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length < 5) return false;
                x = int.Parse(lines[0]);
                y = int.Parse(lines[1]);
                w = int.Parse(lines[3]);
                h = int.Parse(lines[4]);
                return true;
            }
            catch { return false; }
        }

        private static bool IsKnownLegacyOverlayDefault(string url, int x, int y, int w, int h)
        {
            string normalized = (url ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("web_pda_map.html"))
                return x == 130 && y == 130 && w == 331 && h == 331;
            if (normalized.Contains("web_ui_hybrid.html"))
                return x == 208 && y == 208 && w == 859 && h == 465;
            if (normalized.Contains("web_pause_logo.html"))
                return x == 156 && y == 156 && w == 207 && h == 207;
            return false;
        }

        private void EnsureOverlayWindowConfig()
        {
            try
            {
                string configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WebOverlay", "config");
                Directory.CreateDirectory(configDir);

                Screen screen = GetGameScreen() ?? Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
                if (screen == null)
                {
                    AppendLog("[OVERLAY][ERROR] Не найден экран игры.");
                    return;
                }

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
                    string path = Path.Combine(configDir, OverlayStateFileName(url));
                    bool fullscreen = url.Contains("web_ar_hud.html", StringComparison.OrdinalIgnoreCase) ||
                                      url.Contains("web_quests.html", StringComparison.OrdinalIgnoreCase) ||
                                      url.Contains("web_notifications.html", StringComparison.OrdinalIgnoreCase);

                    bool write = !File.Exists(path);
                    if (!write && TryReadOverlayState(path, out int oldX, out int oldY, out int oldW, out int oldH))
                    {
                        if (fullscreen)
                        {
                            write = oldX != bounds.X || oldY != bounds.Y || oldW != bounds.Width || oldH != bounds.Height;
                        }
                        else if (IsKnownLegacyOverlayDefault(url, oldX, oldY, oldW, oldH))
                        {
                            write = true;
                            AppendLog($"[OVERLAY] Мигрируем старый дефолт {Path.GetFileName(path)}: {oldX},{oldY} {oldW}x{oldH}");
                        }
                    }

                    if (!write) continue;
                    ComputeOverlayGeometry(url, bounds, out int x, out int y, out int w, out int h);
                    File.WriteAllLines(path, new[] { x.ToString(), y.ToString(), "1", w.ToString(), h.ToString() }, Encoding.UTF8);
                    AppendLog($"[OVERLAY] Записана геометрия {Path.GetFileName(path)}: X={x}, Y={y}, {w}x{h}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[OVERLAY] Ошибка создания конфигурации позиций: {ex.Message}");
            }
        }
'@
$text = Replace-Once $text '(?s)        private static void ComputeOverlayGeometry\(string url,.*?        private void StartWebOverlay\(\)' ($geometry + "`r`n        private void StartWebOverlay()") 'screen-relative overlay geometry'

$startOverlay = @'
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
                EnsureOverlayWindowConfig();

                // A single WebOverlay process hosts all overlay forms. StartSystem is the clean
                // ownership boundary, so stale hosts from a previous session are closed here.
                foreach (var proc in Process.GetProcessesByName("WebOverlay"))
                {
                    try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                }
                foreach (var proc in Process.GetProcessesByName("pano"))
                {
                    try { proc.Kill(); proc.WaitForExit(1500); } catch { }
                }

                string urlMain = "http://localhost:8082/web_ui_hybrid.html";
                string urlPda = "http://localhost:8082/web_pda_map.html";
                string urlPauseLogo = "http://localhost:8082/web_pause_logo.html";
                string urlQuests = "http://localhost:8082/web_quests.html";
                string urlNotifications = "http://localhost:8082/web_notifications.html";
                string urlHeights = "http://localhost:8082/web_heights.html";

                Process.Start(overlayExe, urlMain);
                AppendLog("Main overlay started.");
                Thread.Sleep(500);
                Process.Start(overlayExe, $"append {urlPda}");
                AppendLog("PDA map overlay appended.");
                Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlPauseLogo}");
                AppendLog("Pause logo overlay appended.");
                Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlQuests}");
                AppendLog("Quest dialog overlay appended (fullscreen, clickable only on game pause).");
                Thread.Sleep(200);
                Process.Start(overlayExe, $"append {urlNotifications}");
                AppendLog("Notifications overlay appended (fullscreen, click-through).");
                Thread.Sleep(200);

                if (AppSettings.ShowHeightsWindow)
                {
                    Process.Start(overlayExe, $"append {urlHeights}");
                    AppendLog("Heights visualization overlay appended (включено в меню «Вид").");
                }
                else
                {
                    AppendLog("Окно визуализации высот не открывается (выключено в меню «Вид»).");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to start overlay: {ex.Message}");
            }
        }
'@
$text = Replace-Once $text '(?s)        private void StartWebOverlay\(\).*?\n        \}\n\n        // ================================================================\n        // AR HUD' ($startOverlay + "`r`n        // ================================================================`r`n        // AR HUD") 'StartWebOverlay'

# AR1 lifecycle: keep state in MainForm and close only the specific URL via WebOverlay pipe.
$text = $text.Replace('        private void ToggleArOverlay()', '        private bool _ar1Running;`r`n`r`n        private void ToggleArOverlay()')
$text = Replace-Once $text '(?s)        internal bool IsAr1Running\s*\{.*?        \}\r?\n\r?\n        // Остановка AR1' @'
        internal bool IsAr1Running => _ar1Running;

        // Остановка AR1
'@ 'AR running state'

$text = Replace-Once $text '(?s)        internal void StopArOverlay\(bool manual\)\s*\{.*?        \}\r?\n\r?\n        // v1\.0\.40\.27: тоггл-подсветка' @'
        internal void StopArOverlay(bool manual)
        {
            string overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            if (!File.Exists(overlayExe))
                overlayExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "pano.exe");

            try
            {
                if (File.Exists(overlayExe))
                    Process.Start(overlayExe, "close http://localhost:8082/web_ar_hud.html");
            }
            catch (Exception ex)
            {
                AppendLog($"[AR] Не удалось отправить close для AR HUD: {ex.Message}");
            }

            _ar1Running = false;
            if (!IsAr2Running) StopArTargetFeed();
            SyncAr1Button();
            AppendLog(manual ? "[AR] AR HUD закрывается кнопкой." : "[AR] AR HUD закрывается.");
        }

        // v1.0.40.27: тоггл-подсветка'@ 'AR targeted stop'

# Remove the old process-kill loop from LaunchArOverlay if it exists.
$text = [regex]::Replace($text, '(?s)\s*                // Если AR уже запущен.*?\s*\n\s*Process\.Start\(overlayExe, url\);', "`r`n                Process.Start(overlayExe, url);", [Text.RegularExpressions.RegexOptions]::Singleline)
# Also handle the exact comment variant used by the current main.
$text = [regex]::Replace($text, '(?s)\s*                // Если AR уже запущен — закрываем старое окно AR HUD\..*?\s*\n\s*Process\.Start\(overlayExe, url\);', "`r`n                Process.Start(overlayExe, url);", [Text.RegularExpressions.RegexOptions]::Singleline)
$text = $text.Replace('                Process.Start(overlayExe, url);`r`n                StartArTargetFeed();', '                Process.Start(overlayExe, url);' + [Environment]::NewLine + '                _ar1Running = true;' + [Environment]::NewLine + '                StartArTargetFeed();')
$text = $text.Replace('                Process.Start(overlayExe, url);' + [Environment]::NewLine + '                StartArTargetFeed();', '                Process.Start(overlayExe, url);' + [Environment]::NewLine + '                _ar1Running = true;' + [Environment]::NewLine + '                StartArTargetFeed();')

Set-Content -Path $path -Value $text -Encoding UTF8

# ---------------------------------------------------------------------------
# QuestRuntime: MainForm owns the fullscreen web_quests overlay. QuestRuntime
# only publishes state over WS; it never starts/stops/focuses another process.
# ---------------------------------------------------------------------------
$path = 'Quests/QuestRuntime.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
$text = $text -replace '        private bool _overlayVisible;\s*\r?\n', ''
$text = $text -replace '        private Process\? _overlayProcess;\s*\r?\n', ''
$text = Replace-Once $text '(?s)        private async Task UpdateOverlayAsync\(\).*?        private void EnforceArPointDebugMode' @'
        private Task UpdateOverlayAsync()
        {
            // MainForm starts web_quests.html as a permanent fullscreen overlay.
            // Its page is hidden while the game is running and becomes interactive only on pause.
            return Task.CompletedTask;
        }
        private void EnforceArPointDebugMode
'@ 'Quest overlay ownership'
$text = Replace-Once $text '(?s)        public void Dispose\(\)\s*\{.*?\s*\}' @'
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _tickTimer?.Dispose(); } catch { }
            try { _server?.Stop(); } catch { }
            _server = null;
            try { TruckTelemetry.Stop(); } catch { }
        }
'@ 'QuestRuntime Dispose'
Set-Content -Path $path -Value $text -Encoding UTF8

# ---------------------------------------------------------------------------
# Quest notifications: future definitions can choose top/left/right without
# changing the overlay transport.
# ---------------------------------------------------------------------------
$path = 'Quests/QuestModels.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
if ($text -notmatch 'NotifyPosition') {
    $text = $text.Replace('public string NotifyTitle { get; set; } = ""; public string NotifyText { get; set; } = "";', 'public string NotifyTitle { get; set; } = ""; public string NotifyText { get; set; } = ""; public string NotifyPosition { get; set; } = "top";')
}
Set-Content -Path $path -Value $text -Encoding UTF8

# Apply position to quest-effect notifications.
$path = 'Quests/QuestRuntime.cs'
$text = Get-Content -Raw -Encoding UTF8 $path
$text = $text.Replace('["title"]=effect.NotifyTitle ?? "", ["text"]=effect.NotifyText, ["icon"]=""', '["title"]=effect.NotifyTitle ?? "", ["text"]=effect.NotifyText, ["icon"]="", ["position"]=(effect.NotifyPosition ?? "top")')
Set-Content -Path $path -Value $text -Encoding UTF8

# Verify source-level invariants.
$main = Get-Content -Raw -Encoding UTF8 MainForm.cs
$qr = Get-Content -Raw -Encoding UTF8 Quests/QuestRuntime.cs
$models = Get-Content -Raw -Encoding UTF8 Quests/QuestModels.cs

if ($main -match 'Contains\("AR HUD"\).*proc\.Kill|AR HUD.*proc\.Kill') { throw 'AR still kills the WebOverlay host process' }
if ($main -notmatch 'web_notifications\.html' -or $main -notmatch 'web_quests\.html') { throw 'Quest/notification fullscreen overlays not launched' }
if ($main -notmatch 'web_ar_hud\.html' -or $main -notmatch 'bounds\.Width' -or $main -notmatch 'bounds\.Height') { throw 'Screen-bound geometry missing' }
if ($qr -match '_overlayProcess|_overlayVisible|EnsureOverlayAsync|FocusOverlay|HideOverlay') { throw 'QuestRuntime still owns WebOverlay process lifecycle' }
if ($models -notmatch 'NotifyPosition') { throw 'NotifyPosition model field missing' }

Write-Host 'ETS2 Assist quest overlay patch applied and validated.'
