using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MapEditor2Form
    {
        private sealed class TerrainSettings
        {
            public string LowColor { get; set; } = "#0f1c06";
            public string HighColor { get; set; } = "#2d4a18";
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
                        return settings;
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
            try
            {
                process = Process.Start(psi);
            }
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

            if (process == null)
                return false;

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
            var q = (string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(pythonSelector))
                args.Add(pythonSelector);
            args.Add(q(TerrainScriptPath));
            args.Add("--data-root");
            args.Add(q(AppDataPaths.StaticDataDirectory));
            args.Add("--output");
            args.Add(q(TerrainPngPath));
            args.Add("--metadata");
            args.Add(q(TerrainMetaPath));
            args.Add("--width");
            args.Add(settings.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--low-color");
            args.Add(q(settings.LowColor));
            args.Add("--high-color");
            args.Add(q(settings.HighColor));
            args.Add("--neighbors");
            args.Add(settings.Neighbors.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--power");
            args.Add(settings.Power.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--sigma");
            args.Add(settings.Sigma.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" ", args);
        }

        private string PrepareMapEditor2Html(string html)
        {
            html = html.Replace("<canvas id=\"grid\"></canvas>", "<canvas id=\"terrain\"></canvas><canvas id=\"grid\"></canvas>");
            html = html.Replace("#grid{z-index:0}#gl{z-index:1;cursor:default}#labels{z-index:2;pointer-events:none}", "#terrain{z-index:0;pointer-events:none}#grid{z-index:1}#gl{z-index:2;cursor:default}#labels{z-index:3;pointer-events:none}");
            html = html.Replace("<div class=\"menuItem\" data-menu=\"service\">Сервис</div>", "<div class=\"menuItem\" data-menu=\"service\">Сервис</div><div class=\"menuItem\" data-menu=\"tools\">Инструменты</div>");
            html = html.Replace("<div id=\"viewPopup\" class=\"menuPopup\"><button class=\"menuBtn\" id=\"fontPlus\">Шрифт+</button><button class=\"menuBtn\" id=\"fontMinus\">Шрифт-</button></div>", "<div id=\"viewPopup\" class=\"menuPopup\"><button class=\"menuBtn\" id=\"fontPlus\">Шрифт+</button><button class=\"menuBtn\" id=\"fontMinus\">Шрифт-</button></div><div id=\"toolsPopup\" class=\"menuPopup\" style=\"left:310px\"><button class=\"menuBtn\" id=\"generateTerrain\">Генерировать карту высот</button></div>");
            html = html.Replace("labelCtx.lineWidth=selected?5:3;labelCtx.strokeStyle=selected?'lime':'black';", "labelCtx.lineWidth=selected?5:2;labelCtx.strokeStyle=selected?'lime':'#0a0c0f';");

            const marker = "window.chrome?.webview?.postMessage('map2-ready');";
            var patch = @"
const terrainCanvas=document.getElementById('terrain'),terrainCtx=terrainCanvas?.getContext('2d');
let terrainImage=null,terrainMeta=null;
async function loadTerrain(){try{const m=await fetch('../map_editor2/terrain_height_meta.json?ts='+Date.now(),{cache:'no-store'});if(!m.ok)throw Error('HTTP '+m.status);terrainMeta=await m.json();const img=new Image();img.src='../map_editor2/terrain_height.png?ts='+Date.now();await img.decode();terrainImage=img;}catch(e){terrainMeta=null;terrainImage=null;console.warn('Terrain load failed',e)}dirty=true;requestFrame()}
function drawTerrain(){if(!terrainCanvas||!terrainCtx||!terrainImage||!terrainMeta)return;const s=mapSize();terrainCanvas.width=Math.floor(s.w*dpr);terrainCanvas.height=Math.floor(s.h*dpr);terrainCanvas.style.width=s.w+'px';terrainCanvas.style.height=s.h+'px';terrainCtx.setTransform(dpr,0,0,dpr,0,0);terrainCtx.clearRect(0,0,s.w,s.h);const mx0=Number(terrainMeta.minX),mx1=Number(terrainMeta.maxX),mz0=Number(terrainMeta.minZ),mz1=Number(terrainMeta.maxZ);const sx=s.w*.5+(mx0-camera.x)/camera.mpp,sy=s.h*.5+(mz0-camera.z)/camera.mpp,ex=s.w*.5+(mx1-camera.x)/camera.mpp,ey=s.h*.5+(mz1-camera.z)/camera.mpp;terrainCtx.drawImage(terrainImage,sx,sy,ex-sx,ey-sy)}
function terrainProgress(text){if(window.MapEditor2TerrainProgress){} status.textContent='Map Editor 2 · '+text}
window.MapEditor2TerrainProgress=terrainProgress;window.MapEditor2ReloadTerrain=loadTerrain;
const originalResize=resize;resize=function(){originalResize();if(terrainCanvas){const s=mapSize();terrainCanvas.width=Math.floor(s.w*dpr);terrainCanvas.height=Math.floor(s.h*dpr);terrainCanvas.style.width=s.w+'px';terrainCanvas.style.height=s.h+'px';}dirty=true;requestFrame()};
const originalRender=render;render=function(){if(!dirty)return;dirty=false;gl.viewport(0,0,glCanvas.width,glCanvas.height);gl.clearColor(15/255,18/255,23/255,1);gl.clear(gl.COLOR_BUFFER_BIT);drawTerrain();updateGrid();drawRoads();drawPointsAndLabels();mapInfo.textContent=`Zoom ${camera.mpp.toFixed(3)} m/px · клетка ${gridWorldStep.toFixed(3)} м · roads ${roadsCount.toLocaleString()} · points ${allPoints.length.toLocaleString()} · cities ${categories.find(c=>c.key==='__cities')?.points.length?.toLocaleString()||'0'}`};
loadTerrain();
const toolsPopup=document.getElementById('toolsPopup');document.querySelector('[data-menu="tools"]').addEventListener('click',e=>{e.stopPropagation();toolsPopup.classList.toggle('open');viewPopup.classList.remove('open')});document.getElementById('generateTerrain')?.addEventListener('click',()=>window.chrome?.webview?.postMessage('map2-generate-terrain'));";
            return html.Replace(marker, patch + "\n" + marker);
        }
    }
}
"}