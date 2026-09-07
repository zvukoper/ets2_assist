using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private sealed class TerrainSettings
        {
            public string LowColor { get; set; } = "#15260a";
            public string HighColor { get; set; } = "#2f531c";
            public int Width { get; set; } = 4096;
            public int Neighbors { get; set; } = 12;
            public double Power { get; set; } = 2.0;
            public double Sigma { get; set; } = 1.15;
        }

        private string TerrainSettingsPath => Path.Combine(AppDataPaths.UserDataDirectory, "map_editor2_terrain_settings.json");
        private string TerrainScriptPath => Path.Combine(AppDataPaths.StaticDataDirectory, "map_editor2", "tools", "generate_heightmap.py");
        private string TerrainPngPath => Path.Combine(AppDataPaths.StaticDataDirectory, "map_editor2", "terrain_height.png");
        private string TerrainMetaPath => Path.Combine(AppDataPaths.StaticDataDirectory, "map_editor2", "terrain_height_meta.json");

        private TerrainSettings LoadTerrainSettings()
        {
            try
            {
                if (File.Exists(TerrainSettingsPath))
                {
                    var settings = JsonConvert.DeserializeObject<TerrainSettings>(File.ReadAllText(TerrainSettingsPath));
                    if (settings != null)
                    {
                        if (string.Equals(settings.LowColor, "#0f1c06", StringComparison.OrdinalIgnoreCase))
                            settings.LowColor = "#15260a";
                        if (string.Equals(settings.HighColor, "#2d4a18", StringComparison.OrdinalIgnoreCase))
                            settings.HighColor = "#2f531c";
                        return settings;
                    }
                }
            }
            catch { }
            return new TerrainSettings();
        }

        private TerrainSettings SaveTerrainSettings()
        {
            AppDataPaths.EnsureUserData();
            var settings = LoadTerrainSettings();
            File.WriteAllText(TerrainSettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented), Encoding.UTF8);
            return settings;
        }

        private async Task<bool> GenerateTerrainAsync()
        {
            if (!File.Exists(TerrainScriptPath))
            {
                MessageBox.Show(this, "Не найден генератор высот:\n" + TerrainScriptPath, "Карта высот", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var settings = SaveTerrainSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(TerrainPngPath)!);
            if (_pageReady && _webView.CoreWebView2 != null)
                await _webView.CoreWebView2.ExecuteScriptAsync("window.MapEditor2TerrainProgress && window.MapEditor2TerrainProgress('Генерация карты высот…');");

            var psi = new ProcessStartInfo
            {
                FileName = "py",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = BuildTerrainArguments("-3")
            };
            Process? process = null;
            try { process = Process.Start(psi); }
            catch
            {
                psi.FileName = "python";
                psi.Arguments = BuildTerrainArguments(string.Empty);
                try { process = Process.Start(psi); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Python 3 не найден.\n\nУстановите Python и зависимости из data\\map_editor2\\tools\\requirements-heightmap.txt.\n\n" + ex.Message, "Карта высот", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            if (process == null) return false;
            using (process)
            {
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(TerrainPngPath) || !File.Exists(TerrainMetaPath))
                {
                    var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    MessageBox.Show(this, "Генерация карты высот завершилась с ошибкой.\n\n" + details, "Карта высот", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            if (_pageReady && _webView.CoreWebView2 != null)
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("window.MapEditor2ReloadTerrain && window.MapEditor2ReloadTerrain();");
                await _webView.CoreWebView2.ExecuteScriptAsync("window.MapEditor2TerrainProgress && window.MapEditor2TerrainProgress('Карта высот обновлена.');");
            }
            return true;
        }

        private string BuildTerrainArguments(string pythonSelector)
        {
            var settings = LoadTerrainSettings();
            static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(pythonSelector)) args.Add(pythonSelector);
            args.Add(Quote(TerrainScriptPath));
            args.Add("--data-root"); args.Add(Quote(AppDataPaths.StaticDataDirectory));
            args.Add("--output"); args.Add(Quote(TerrainPngPath));
            args.Add("--metadata"); args.Add(Quote(TerrainMetaPath));
            args.Add("--width"); args.Add(settings.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--low-color"); args.Add(Quote(settings.LowColor));
            args.Add("--high-color"); args.Add(Quote(settings.HighColor));
            args.Add("--neighbors"); args.Add(settings.Neighbors.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--power"); args.Add(settings.Power.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--sigma"); args.Add(settings.Sigma.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" ", args);
        }
    }
}