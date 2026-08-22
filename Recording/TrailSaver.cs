using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // ================================================================
        // ГЛАВНЫЙ МЕТОД ГЕНЕРАЦИИ HTML-ПЛЕЕРА
        // ================================================================
        private string GenerateTrailHtml(string compactData, JObject? meta, JObject? mapData)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine(GenerateHeadSection());
            sb.AppendLine(GenerateStyleSection());
            sb.AppendLine(GenerateBodyStart());
            sb.AppendLine(GenerateUISections());
            sb.AppendLine(GenerateScriptSection(compactData, meta, mapData));
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        // ================================================================
        // ГОЛОВА HTML
        // ================================================================
        private string GenerateHeadSection()
        {
            return @"<head>
    <meta charset='utf-8'/>
    <title>ETS2 Trail Viewer</title>
</head>";
        }

        // ================================================================
        // СТИЛИ
        // ================================================================
        private string GenerateStyleSection()
        {
            return @"<style>
    body { margin:0; background:#0a0c10; color:#e0e0e0; font-family:'Segoe UI',sans-serif; overflow:hidden; }
    #mapCanvas { display:block; width:100vw; height:calc(100vh - 150px); background:#111; cursor:grab; position:absolute; top:0; left:0; }
    #controls { position:fixed; bottom:0; left:0; right:0; background:#1a1f26; padding:8px 15px; border-top:1px solid #333; display:flex; flex-direction:column; gap:6px; z-index:10; }
    #controlsTop { display:flex; align-items:center; gap:8px; flex-wrap:wrap; }
    #controlsBottom { display:flex; align-items:center; gap:10px; }
    #controls button, #controls input { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:4px 10px; font-size:13px; cursor:pointer; }
    #controls button:hover { background:#3a4a5a; }
    #timeSlider { flex:1; min-width:200px; }
    #speedLabel { font-size:13px; color:#8fa0b9; }
    #checkboxFollow { margin-left:8px; }
    #timeDisplay { font-size:13px; color:#aabbcc; min-width:120px; font-family:monospace; }
    #stepInput { width:60px; background:#0c1016; border:1px solid #2f3845; border-radius:4px; padding:3px 6px; color:#e0e9f5; font-size:13px; font-family:monospace; }
    #info { position:absolute; top:10px; right:10px; background:rgba(0,0,0,0.7); padding:6px 12px; border-radius:6px; font-size:12px; color:#ccc; z-index:5; pointer-events:none; }
    #dataPanel { position:absolute; bottom:160px; left:20px; background:rgba(0,0,0,0.7); padding:8px 14px; border-radius:6px; font-size:11px; color:#aabbcc; border:1px solid #333; backdrop-filter:blur(4px); pointer-events:none; z-index:5; }
    #titlePanel { position:absolute; top:10px; left:10px; background:rgba(0,0,0,0.7); padding:6px 14px; border-radius:6px; font-size:12px; color:#e0e0e0; border:1px solid #444; backdrop-filter:blur(4px); pointer-events:none; z-index:5; }
    #cursorCoords { position:absolute; bottom:160px; right:20px; background:rgba(0,0,0,0.7); padding:2px 8px; border-radius:4px; font-size:10px; color:#8fa0b9; pointer-events:none; z-index:5; }
    #measureTool { position:absolute; top:60px; left:10px; background:rgba(0,0,0,0.7); padding:4px 10px; border-radius:4px; font-size:11px; color:#ffc857; border:1px solid #ffc85744; display:none; pointer-events:none; z-index:5; }
    #measureTool.active { display:block; }
    .note-btn { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:2px 6px; font-size:11px; cursor:pointer; }
    .note-btn:hover { background:#3a4a5a; }
    /* 3D контейнер */
    #threeContainer { position:absolute; top:0; left:0; width:100%; height:calc(100vh - 150px); pointer-events:none; z-index:2; }
    #threeContainer canvas { display:block; width:100%; height:100%; pointer-events:none; }
    /* Панель pitch/roll (только числа) */
    #prPanel { position:absolute; top:50px; left:10px; background:rgba(0,0,0,0.85); padding:8px 12px; border-radius:8px; border:1px solid #444; backdrop-filter:blur(4px); pointer-events:none; z-index:5; min-width:180px; font-size:12px; }
    #prPanel .pr-row { display:flex; gap:12px; margin-bottom:2px; }
    #prPanel .pr-label { color:#8fa0b9; }
    #prPanel .pr-value { font-weight:bold; }
    #prPanel .pr-extremes { display:flex; justify-content:space-between; gap:6px; font-size:10px; color:#6a7b94; flex-wrap:wrap; }
    #prPanel .pr-reset-btn { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:1px 8px; font-size:9px; cursor:pointer; pointer-events:auto; }
    /* Debug log */
    #debugLog { position:absolute; bottom:200px; right:20px; background:rgba(0,0,0,0.8); padding:4px 10px; border-radius:4px; font-size:9px; color:#88ff88; font-family:monospace; pointer-events:none; z-index:5; border:1px solid #4a5a6a; max-height:100px; overflow-y:auto; }
    /* Чекбокс 3D указателя */
    #controlsTop label.checkbox3d { color:#8fa0b9; font-size:13px; margin-left:10px; }
    /* Дополнительная информация (head heading, scale) */
    #extraInfo { position:absolute; bottom:160px; right:20px; background:rgba(0,0,0,0.7); padding:4px 10px; border-radius:4px; font-size:10px; color:#8fa0b9; pointer-events:none; z-index:5; }
</style>";
        }

        // ================================================================
        // НАЧАЛО BODY И UI-ЭЛЕМЕНТЫ
        // ================================================================
        private string GenerateBodyStart()
        {
            return @"<body>";
        }

        private string GenerateUISections()
        {
            return @"<div id='info'>Просмотр трека</div>
<div id='titlePanel'></div>
<div id='dataPanel'>⛽ -- л &nbsp;|&nbsp; 🛠️ --%</div>
<div id='cursorCoords'></div>
<div id='measureTool'>📏 Расстояние: <span id='measureDist'>0.0</span> м</div>
<div id='prPanel'>
    <div class='pr-row'>
        <span class='pr-label' style='color:#ff6b6b;'>Pitch</span>
        <span class='pr-value' style='color:#ff6b6b;' id='pitchValue3d'>0.0°</span>
        <span class='pr-label' style='color:#4ecdc4;'>Roll</span>
        <span class='pr-value' style='color:#4ecdc4;' id='rollValue3d'>0.0°</span>
    </div>
    <div class='pr-extremes'>
        <span>Pitch min: <span id='pitchMin3d'>0.0</span>°</span>
        <span>Pitch max: <span id='pitchMax3d'>0.0</span>°</span>
        <span>Roll min: <span id='rollMin3d'>0.0</span>°</span>
        <span>Roll max: <span id='rollMax3d'>0.0</span>°</span>
    </div>
    <div style='margin-top:4px; text-align:right;'>
        <button id='resetPitchRollBtn' class='pr-reset-btn'>Сбросить экстремумы</button>
    </div>
</div>
<div id='extraInfo'></div>
<div id='debugLog'>Debug</div>
<canvas id='mapCanvas'></canvas>
<div id='threeContainer'></div>
<div id='controls'>
    <div id='controlsTop'>
        <button id='playBtn'>▶</button>
        <button id='beginBtn'>⏮</button>
        <button id='endBtn'>⏭</button>
        <button id='stepBackBtn'>◀</button>
        <button id='stepForwardBtn'>▶</button>
        <button id='prevEventBtn'>⏪</button>
        <button id='nextEventBtn'>⏩</button>
        <span style='color:#8fa0b9;font-size:13px;'>Шаг:</span>
        <input type='number' id='stepInput' value='1' min='0.5' max='600' step='0.5'>
        <span style='color:#8fa0b9;font-size:12px;'>с</span>
        <span id='speedLabel'>1×</span>
        <button id='speedDownBtn'>−</button>
        <button id='speedUpBtn'>+</button>
        <label style='color:#8fa0b9;font-size:13px;'><input type='checkbox' id='followCheck' checked> Следить</label>
        <label style='color:#8fa0b9;font-size:13px;'><input type='checkbox' id='interpolateCheck' checked> Интерполяция</label>
        <label class='checkbox3d'><input type='checkbox' id='use3dCheck' checked> 3D указатель</label>
    </div>
    <div id='controlsBottom'>
        <input type='range' id='timeSlider' min='0' max='1000' value='0' style='flex:1;'>
        <span id='timeDisplay'>00:00:00.000</span>
    </div>
</div>";
        }

        // ================================================================
        // СКРИПТЫ (ВЕСЬ JAVASCRIPT)
        // ================================================================
        private string GenerateScriptSection(string compactData, JObject? meta, JObject? mapData)
        {
            string escapedData = JsonConvert.ToString(compactData);
            string metaJson = meta != null ? meta.ToString(Formatting.None) : "{}";
            string mapJson = mapData != null ? mapData.ToString(Formatting.None) : "{}";

            var sb = new StringBuilder();
            sb.AppendLine("<script src='https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js'></script>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const compactData = {escapedData};");
            sb.AppendLine($"const metaData = {metaJson};");
            sb.AppendLine($"const mapData = {mapJson};");
            sb.AppendLine(GenerateConstants());
            sb.AppendLine(GenerateParserFunctions());
            sb.AppendLine(GenerateInitData());
            sb.AppendLine(GenerateDrawMapFunction());
            sb.AppendLine(GenerateThreeJSInit());
            sb.AppendLine(GeneratePitchRollPanel());
            sb.AppendLine(GenerateUIControls());
            sb.AppendLine(GenerateTimelineControls());
            sb.AppendLine(GenerateInitialization());
            sb.AppendLine("</script>");
            return sb.ToString();
        }

        // ----- Константы -----
        private string GenerateConstants()
        {
            return @"// ================================================================
// КОНСТАНТЫ (калибровка по документации TruckTel)
// ================================================================
const NEARBY_CITIES_COUNT = 4;
// heading: 0..1 → 0..360
// pitch: -0.25..0.25 → -90..90 → умножаем на 360
// roll: -0.5..0.5 → -180..180 → умножаем на 360
const PITCH_SCALE = 360;   // -0.25..0.25 -> -90..90
const ROLL_SCALE = 360;    // -0.5..0.5 -> -180..180
const HEADING_SCALE = 360; // 0..1 -> 0..360
";
        }

        // ----- Парсер -----
        private string GenerateParserFunctions()
        {
            return @"// ================================================================
// ПАРСЕР КОМПАКТНОГО ТРЕКА
// ================================================================
function parseTrail(data) {
    const lines = data.split('\n').filter(l => l.trim() !== '');
    if (lines.length === 0) return { meta: {}, trail: [], dataPoints: [], events: [] };
    let meta = {};
    try { meta = JSON.parse(lines[0]); } catch(e) { meta = {}; }
    const trail = [];
    const dataPoints = [];
    const events = [];
    const eventTypes = meta.eventTypes || {};
    const typeNames = Object.fromEntries(Object.entries(eventTypes).map(([k,v]) => [v,k]));
    const debugLines = [];
    for (let i=1; i<lines.length; i++) {
        const parts = lines[i].split(';');
        if (parts.length === 0) continue;
        const type = parts[0];
        if (type === 'D') {
            const dp = {
                t: parseFloat(parts[1]),
                x: parseFloat(parts[2]),
                z: parseFloat(parts[3]),
                heading: parseFloat(parts[4]),
                fuel: parseFloat(parts[5]),
                damage: parseFloat(parts[6]),
                pitch: parseFloat(parts[7] || 0),
                roll: parseFloat(parts[8] || 0),
                // Новые поля
                lights: parts[9] ? JSON.parse(parts[9]) : {},
                gameTime: parseFloat(parts[10] || 0),
                localScale: parseFloat(parts[11] || 1),
                steering: parseFloat(parts[12] || 0),
                throttle: parseFloat(parts[13] || 0),
                brake: parseFloat(parts[14] || 0),
                odometer: parseFloat(parts[15] || 0),
                headOffset: parts[16] ? parts[16].split(',').map(Number) : [0,0,0,0,0,0]
            };
            dataPoints.push(dp);
            debugLines.push(`[DEBUG] D: t=${dp.t}, pitch=${dp.pitch}, roll=${dp.roll}`);
        } else if (type === 'E') {
            const eType = typeNames[parseInt(parts[4])] || 'unknown';
            events.push({ t: parseFloat(parts[1]), x: parseFloat(parts[2]), z: parseFloat(parts[3]), type: eType, label: parts[5], color: parts[6], subtext: parts[7] });
        } else {
            trail.push({ t: parseFloat(parts[0]), x: parseFloat(parts[1]), z: parseFloat(parts[2]), heading: parseFloat(parts[3]), speed: parseFloat(parts[4]) });
        }
    }
    console.log('[DEBUG] Parsed trail points:', trail.length);
    console.log('[DEBUG] Parsed data points:', dataPoints.length);
    console.log('[DEBUG] Parsed events:', events.length);
    if (dataPoints.length > 0) {
        console.log('[DEBUG] First data point:', dataPoints[0]);
    }
    const logDiv = document.getElementById('debugLog');
    if (logDiv) {
        logDiv.innerHTML = 'Data points: ' + dataPoints.length + ' | Trail: ' + trail.length + '<br>' + debugLines.slice(0,5).join('<br>');
    }
    return { meta, trail, dataPoints, events };
}";
        }

        // ----- Инициализация данных -----
        private string GenerateInitData()
        {
            return @"
const parsed = parseTrail(compactData);
const trailPoints = parsed.trail;
const dataPoints = parsed.dataPoints;
const events = parsed.events;
const meta = parsed.meta;

// Данные карты
const cities = mapData?.cities || [];
const roads = mapData?.roads || [];
const customTargets = mapData?.customTargets || [];
console.log('[DEBUG] Cities:', cities.length, 'Roads:', roads.length, 'Targets:', customTargets.length);

const times = trailPoints.map(p => p.t);
const totalDuration = times.length > 0 ? times[times.length-1] : 0;

const title = meta.title || '';
const desc = meta.description || '';
document.getElementById('titlePanel').innerHTML = title + (desc ? '<br><span style='font-size:10px; color:#888;'>'+desc+'</span>' : '');
";
        }

        // ----- drawMap (основная функция) -----

        private string GenerateDrawMapFunction()
        {
            var sb = new StringBuilder();
            sb.AppendLine("const canvas = document.getElementById('mapCanvas');");
            sb.AppendLine("const ctx = canvas.getContext('2d');");
            sb.AppendLine("let W, H;");
            sb.AppendLine("function resize() { W = canvas.width = window.innerWidth; H = canvas.height = window.innerHeight - 150; drawMap(); }");
            sb.AppendLine("window.addEventListener('resize', resize);");
            sb.AppendLine("let centerX = 0, centerZ = 0, scale = 1;");
            sb.AppendLine("let dragStartX = 0, dragStartY = 0, dragStartCX = 0, dragStartCZ = 0, isDragging = false;");
            sb.AppendLine("let targetCenterX = 0, targetCenterZ = 0;");
            sb.AppendLine("function fitMap() {");
            sb.AppendLine("    if (trailPoints.length < 2) return;");
            sb.AppendLine("    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;");
            sb.AppendLine("    for (const p of trailPoints) { if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x; if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z; }");
            sb.AppendLine("    centerX = (minX + maxX) / 2; centerZ = (minZ + maxZ) / 2;");
            sb.AppendLine("    targetCenterX = centerX; targetCenterZ = centerZ;");
            sb.AppendLine("    const range = Math.max(maxX - minX, maxZ - minZ, 1);");
            sb.AppendLine("    scale = (Math.min(W, H) * 0.85) / (range * 1.15);");
            sb.AppendLine("}");
            sb.AppendLine("function worldToScreen(wx, wz) {");
            sb.AppendLine("    const dx = (wx - centerX) * scale; const dz = (wz - centerZ) * scale;");
            sb.AppendLine("    return { x: W/2 + dx, y: H/2 - dz };");
            sb.AppendLine("}");
            sb.AppendLine("function getTrailColor(speed) {");
            sb.AppendLine("    const minS = meta.minSpeed || 0; const maxS = meta.maxSpeed || 125;");
            sb.AppendLine("    const s = Math.max(minS, Math.min(maxS, speed)); const t = (s - minS) / (maxS - minS);");
            sb.AppendLine("    let r,g,b;");
            sb.AppendLine("    if (t <= 0.2) { const u = t/0.2; r=0; g=u*255; b=255; }");
            sb.AppendLine("    else if (t <= 0.4) { const u=(t-0.2)/0.2; r=0; g=255; b=255-u*255; }");
            sb.AppendLine("    else if (t <= 0.6) { const u=(t-0.4)/0.2; r=u*255; g=255; b=0; }");
            sb.AppendLine("    else if (t <= 0.8) { const u=(t-0.6)/0.2; r=255; g=255-u*(255-165); b=0; }");
            sb.AppendLine("    else { const u=(t-0.8)/0.2; r=255-u*(255-128); g=165-u*165; b=u*255; }");
            sb.AppendLine("    return `rgb(${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)})`;");
            sb.AppendLine("}");
            sb.AppendLine("function formatDistance(d) { if (d < 1000) return Math.round(d)+'м'; return (Math.round(d/1000))+'км'; }");
            sb.AppendLine("let closest = null;");
            sb.AppendLine("function drawMap() {");
            sb.AppendLine("    ctx.clearRect(0, 0, W, H);");
            // --- Фон в зависимости от времени суток ---
            sb.AppendLine("    let gameTimeMinutes = 0;");
            sb.AppendLine("    if (closest && closest.gameTime !== undefined) gameTimeMinutes = closest.gameTime;");
            sb.AppendLine("    const hour = (gameTimeMinutes % 1440) / 60;");
            sb.AppendLine("    const isDay = hour >= 6 && hour < 18;");
            sb.AppendLine("    const nightColor = [15, 18, 23]; // #0f1217");
            sb.AppendLine("    const dayColor = [42, 74, 42];   // #2a4a2a");
            sb.AppendLine("    let mix = 0;");
            sb.AppendLine("    if (hour >= 6 && hour < 7) mix = (hour - 6) / 1;");
            sb.AppendLine("    else if (hour >= 17 && hour < 18) mix = 1 - (hour - 17) / 1;");
            sb.AppendLine("    else if (hour >= 7 && hour < 17) mix = 1;");
            sb.AppendLine("    else mix = 0;");
            sb.AppendLine("    const r = Math.round(nightColor[0] + (dayColor[0] - nightColor[0]) * mix);");
            sb.AppendLine("    const g = Math.round(nightColor[1] + (dayColor[1] - nightColor[1]) * mix);");
            sb.AppendLine("    const b = Math.round(nightColor[2] + (dayColor[2] - nightColor[2]) * mix);");
            sb.AppendLine("    ctx.fillStyle = `rgb(${r},${g},${b})`;");
            sb.AppendLine("    ctx.fillRect(0, 0, W, H);");
            // --- Сетка ---
            sb.AppendLine("    const gridStep = 200 / scale;");
            sb.AppendLine("    ctx.strokeStyle = '#2a3545'; ctx.lineWidth = 0.5; ctx.setLineDash([4,6]);");
            sb.AppendLine("    for (let x = -W/2; x < W/2; x += gridStep) { const p = worldToScreen(centerX+x, centerZ); ctx.beginPath(); ctx.moveTo(p.x,0); ctx.lineTo(p.x,H); ctx.stroke(); }");
            sb.AppendLine("    for (let z = -H/2; z < H/2; z += gridStep) { const p = worldToScreen(centerX, centerZ+z); ctx.beginPath(); ctx.moveTo(0,p.y); ctx.lineTo(W,p.y); ctx.stroke(); }");
            sb.AppendLine("    ctx.setLineDash([]);");
            // --- Дороги ---
            sb.AppendLine("    for (const r of roads) {");
            sb.AppendLine("        const p1 = worldToScreen(r.x1, r.z1); const p2 = worldToScreen(r.x2, r.z2);");
            sb.AppendLine("        ctx.beginPath(); ctx.moveTo(p1.x,p1.y); ctx.lineTo(p2.x,p2.y);");
            sb.AppendLine("        ctx.strokeStyle = '#5a7a8a'; ctx.lineWidth = 1.5; ctx.globalAlpha = 0.6; ctx.stroke();");
            sb.AppendLine("    }");
            sb.AppendLine("    ctx.globalAlpha = 1;");
            // --- Города (видимые) ---
            sb.AppendLine("    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';");
            sb.AppendLine("    for (const c of cities) {");
            sb.AppendLine("        const p = worldToScreen(c.x, c.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        ctx.beginPath(); ctx.arc(p.x,p.y,4,0,2*Math.PI);");
            sb.AppendLine("        ctx.fillStyle = '#ffdd88'; ctx.shadowColor = '#ffdd8844'; ctx.shadowBlur = 6; ctx.fill(); ctx.shadowBlur = 0;");
            sb.AppendLine("        ctx.font = '10px \"Segoe UI\"'; ctx.fillStyle = '#c8ddee'; ctx.fillText(c.name, p.x, p.y-6);");
            sb.AppendLine("    }");
            // --- Цели (customTargets) ---
            sb.AppendLine("    for (const t of customTargets) {");
            sb.AppendLine("        const p = worldToScreen(t.x, t.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        ctx.beginPath(); ctx.arc(p.x, p.y, t.active ? 6 : 4, 0, 2*Math.PI);");
            sb.AppendLine("        ctx.fillStyle = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');");
            sb.AppendLine("        ctx.shadowColor = t.active ? '#ffc85788' : '#88aadd88';");
            sb.AppendLine("        ctx.shadowBlur = t.active ? 16 : 8; ctx.fill(); ctx.shadowBlur = 0;");
            sb.AppendLine("        ctx.strokeStyle = '#ffffff'; ctx.lineWidth = t.active ? 1.5 : 0.5; ctx.stroke();");
            sb.AppendLine("        ctx.font = t.active ? 'bold 10px \"Segoe UI\"' : '9px \"Segoe UI\"';");
            sb.AppendLine("        ctx.fillStyle = '#fff'; ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 4;");
            sb.AppendLine("        ctx.fillText(t.name, p.x, p.y - (t.active ? 14 : 10));");
            sb.AppendLine("        ctx.shadowBlur = 0;");
            sb.AppendLine("    }");
            // --- Города и цели за пределами экрана ---
            sb.AppendLine("    const cx = W/2, cy = H/2; const radius = Math.min(W, H) * 0.42;");
            sb.AppendLine("    const currentPos = currentIndex < trailPoints.length ? trailPoints[currentIndex] : { x:0, z:0 };");
            sb.AppendLine("    // Ближайшие города");
            sb.AppendLine("    const cityDist = cities.map(c => ({ ...c, dist: Math.hypot(c.x - currentPos.x, c.z - currentPos.z) }));");
            sb.AppendLine("    cityDist.sort((a,b) => a.dist - b.dist);");
            sb.AppendLine("    const nearCities = cityDist.slice(0, NEARBY_CITIES_COUNT);");
            sb.AppendLine("    for (const c of nearCities) {");
            sb.AppendLine("        const p = worldToScreen(c.x, c.z); if (p.x>=0 && p.x<=W && p.y>=0 && p.y<=H) continue;");
            sb.AppendLine("        const dx = p.x - cx, dy = p.y - cy; const len = Math.hypot(dx, dy); if (len < 0.01) continue;");
            sb.AppendLine("        const nx = dx/len, ny = dy/len; const arrowX = cx + nx * radius, arrowY = cy + ny * radius;");
            sb.AppendLine("        const angle = Math.atan2(ny, nx);");
            sb.AppendLine("        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);");
            sb.AppendLine("        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();");
            sb.AppendLine("        ctx.fillStyle = '#aabbcc'; ctx.shadowColor = 'rgba(0,0,0,0.6)'; ctx.shadowBlur = 4; ctx.fill();");
            sb.AppendLine("        ctx.strokeStyle = '#000'; ctx.lineWidth = 1; ctx.stroke(); ctx.shadowBlur = 0; ctx.restore();");
            sb.AppendLine("        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;");
            sb.AppendLine("        if (lx < 50) lx = 50; if (lx > W-50) lx = W-50; if (ly < 20) ly = 20; if (ly > H-20) ly = H-20;");
            sb.AppendLine("        ctx.font = '10px \"Segoe UI\"'; ctx.fillStyle = '#c8ddee'; ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 4;");
            sb.AppendLine("        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';");
            sb.AppendLine("        ctx.fillText(`${c.name} (${formatDistance(c.dist)})`, lx, ly);");
            sb.AppendLine("        ctx.shadowBlur = 0;");
            sb.AppendLine("    }");
            // --- Цели за пределами экрана ---
            sb.AppendLine("    for (const t of customTargets) {");
            sb.AppendLine("        const p = worldToScreen(t.x, t.z); if (p.x>=0 && p.x<=W && p.y>=0 && p.y<=H) continue;");
            sb.AppendLine("        const dx = p.x - cx, dy = p.y - cy; const len = Math.hypot(dx, dy); if (len < 0.01) continue;");
            sb.AppendLine("        const nx = dx/len, ny = dy/len; const arrowX = cx + nx * radius, arrowY = cy + ny * radius;");
            sb.AppendLine("        const angle = Math.atan2(ny, nx);");
            sb.AppendLine("        const color = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');");
            sb.AppendLine("        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);");
            sb.AppendLine("        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();");
            sb.AppendLine("        ctx.fillStyle = color; ctx.shadowColor = 'rgba(0,0,0,0.6)'; ctx.shadowBlur = 4; ctx.fill();");
            sb.AppendLine("        ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke(); ctx.shadowBlur = 0; ctx.restore();");
            sb.AppendLine("        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;");
            sb.AppendLine("        if (lx < 50) lx = 50; if (lx > W-50) lx = W-50; if (ly < 20) ly = 20; if (ly > H-20) ly = H-20;");
            sb.AppendLine("        ctx.font = t.active ? 'bold 10px \"Segoe UI\"' : '9px \"Segoe UI\"';");
            sb.AppendLine("        ctx.fillStyle = '#fff'; ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 4;");
            sb.AppendLine("        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';");
            sb.AppendLine("        ctx.fillText(`${t.name} (${formatDistance(t.dist || 0)})`, lx, ly);");
            sb.AppendLine("        ctx.shadowBlur = 0;");
            sb.AppendLine("    }");
            // --- Шлейф ---
            sb.AppendLine("    if (trailPoints.length > 1) {");
            sb.AppendLine("        for (let i=1; i<trailPoints.length; i++) {");
            sb.AppendLine("            const p1 = trailPoints[i-1]; const p2 = trailPoints[i];");
            sb.AppendLine("            const s1 = worldToScreen(p1.x, p1.z); const s2 = worldToScreen(p2.x, p2.z);");
            sb.AppendLine("            const speed = p2.speed || 0;");
            sb.AppendLine("            ctx.beginPath(); ctx.moveTo(s1.x,s1.y); ctx.lineTo(s2.x,s2.y);");
            sb.AppendLine("            ctx.strokeStyle = getTrailColor(speed); ctx.lineWidth = 2.5;");
            sb.AppendLine("            ctx.shadowColor = 'rgba(0,0,0,0.5)'; ctx.shadowBlur = 4; ctx.stroke(); ctx.shadowBlur = 0;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            // --- События ---
            sb.AppendLine("    for (const e of events) {");
            sb.AppendLine("        const p = worldToScreen(e.x, e.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue;");
            sb.AppendLine("        const size = 10;");
            sb.AppendLine("        ctx.save(); ctx.translate(p.x,p.y); ctx.shadowColor = 'rgba(0,0,0,0.5)'; ctx.shadowBlur = 6;");
            sb.AppendLine("        ctx.beginPath(); ctx.arc(0,0,size,0,2*Math.PI);");
            sb.AppendLine("        ctx.fillStyle = e.color || '#ffffff'; ctx.fill(); ctx.shadowBlur = 0;");
            sb.AppendLine("        ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke();");
            sb.AppendLine("        ctx.fillStyle = '#fff'; ctx.font = 'bold 10px \"Segoe UI\"';");
            sb.AppendLine("        ctx.textAlign = 'center'; ctx.textBaseline = 'middle';");
            sb.AppendLine("        ctx.fillText(e.label || '?', 0, -1);");
            sb.AppendLine("        if (e.subtext) {");
            sb.AppendLine("            ctx.fillStyle = '#fff'; ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 3;");
            sb.AppendLine("            ctx.font = '7px \"Segoe UI\"'; ctx.textBaseline = 'top';");
            sb.AppendLine("            ctx.fillText(e.subtext, 0, size+2); ctx.shadowBlur = 0;");
            sb.AppendLine("        }");
            sb.AppendLine("        ctx.restore();");
            sb.AppendLine("    }");
            // --- Грузовик (2D или 3D) ---
            sb.AppendLine("    if (trailPoints.length > 0) {");
            sb.AppendLine("        let idx = currentIndex; let nextIdx = Math.min(idx + 1, trailPoints.length - 1);");
            sb.AppendLine("        let p1 = trailPoints[idx]; let p2 = trailPoints[nextIdx];");
            sb.AppendLine("        let t1 = p1.t; let t2 = p2.t;");
            sb.AppendLine("        let currentPos = { x: p1.x, z: p1.z, heading: p1.heading || 0, speed: p1.speed || 0 };");
            sb.AppendLine("        if (interpolate && idx < trailPoints.length - 1 && t2 > t1) {");
            sb.AppendLine("            const progress = (currentTime - t1) / (t2 - t1);");
            sb.AppendLine("            const clampedProgress = Math.max(0, Math.min(1, progress));");
            sb.AppendLine("            currentPos.x = p1.x + (p2.x - p1.x) * clampedProgress;");
            sb.AppendLine("            currentPos.z = p1.z + (p2.z - p1.z) * clampedProgress;");
            sb.AppendLine("            let h1 = p1.heading || 0; let h2 = p2.heading || 0;");
            sb.AppendLine("            let diff = h2 - h1; while (diff > Math.PI) diff -= 2*Math.PI; while (diff < -Math.PI) diff += 2*Math.PI;");
            sb.AppendLine("            currentPos.heading = h1 + diff * clampedProgress;");
            sb.AppendLine("            currentPos.speed = p1.speed + (p2.speed - p1.speed) * clampedProgress;");
            sb.AppendLine("        }");
            sb.AppendLine("        const sp = worldToScreen(currentPos.x, currentPos.z);");
            sb.AppendLine("        const heading = currentPos.heading || 0;");
            // Получаем pitch/roll из closest
            sb.AppendLine("        let pitchDeg = 0, rollDeg = 0;");
            sb.AppendLine("        if (closest) {");
            sb.AppendLine("            const pitchRaw = closest.pitch || 0;");
            sb.AppendLine("            const rollRaw = closest.roll || 0;");
            sb.AppendLine("            pitchDeg = pitchRaw * PITCH_SCALE; // -0.25..0.25 -> -90..90");
            sb.AppendLine("            rollDeg = rollRaw * ROLL_SCALE;   // -0.5..0.5 -> -180..180");
            sb.AppendLine("        }");
            sb.AppendLine("        truckPosScreen = sp;");
            sb.AppendLine("        truckHeading = heading * 180 / Math.PI; // радианы → градусы");
            sb.AppendLine("        truckPitch = pitchDeg;");
            sb.AppendLine("        truckRoll = rollDeg;");
            // Если 3D отключен или не готов – рисуем красный треугольник
            sb.AppendLine("        if (!use3d || !threeReady) {");
            sb.AppendLine("            ctx.save(); ctx.translate(sp.x, sp.y); ctx.rotate(heading + Math.PI);");
            sb.AppendLine("            ctx.beginPath(); ctx.moveTo(0, -14); ctx.lineTo(-9, 9); ctx.lineTo(0, 3); ctx.lineTo(9, 9); ctx.closePath();");
            sb.AppendLine("            ctx.fillStyle = '#ff4d4d'; ctx.shadowColor = '#ff4d4d88'; ctx.shadowBlur = 12; ctx.fill();");
            sb.AppendLine("            ctx.shadowBlur = 0; ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke(); ctx.restore();");
            sb.AppendLine("        }");
            // Скорость под грузовиком
            sb.AppendLine("        ctx.save(); ctx.translate(sp.x, sp.y+22);");
            sb.AppendLine("        ctx.font = '10px \"Segoe UI\"'; ctx.fillStyle = '#fff';");
            sb.AppendLine("        ctx.shadowColor = 'rgba(0,0,0,0.8)'; ctx.shadowBlur = 4;");
            sb.AppendLine("        ctx.textAlign = 'center'; ctx.textBaseline = 'top';");
            sb.AppendLine("        ctx.fillText(`${currentPos.speed.toFixed(0)} km/h`, 0, 0);");
            sb.AppendLine("        ctx.restore();");
            // Обновление центра камеры при слежении
            sb.AppendLine("        if (follow) { targetCenterX = currentPos.x; targetCenterZ = currentPos.z; }");
            sb.AppendLine("        const camSmooth = 0.15;");
            sb.AppendLine("        centerX += (targetCenterX - centerX) * camSmooth;");
            sb.AppendLine("        centerZ += (targetCenterZ - centerZ) * camSmooth;");
            sb.AppendLine("    }");
            // --- Обновление данных (топливо, повреждения, pitch/roll) ---
            sb.AppendLine("    closest = null;");
            sb.AppendLine("    if (dataPoints.length > 0 && currentIndex < trailPoints.length) {");
            sb.AppendLine("        let minDist = Infinity;");
            sb.AppendLine("        const curP = trailPoints[currentIndex];");
            sb.AppendLine("        for (const dp of dataPoints) {");
            sb.AppendLine("            const d = Math.hypot(dp.x - curP.x, dp.z - curP.z);");
            sb.AppendLine("            if (d < minDist) { minDist = d; closest = dp; }");
            sb.AppendLine("        }");
            sb.AppendLine("        if (closest) {");
            sb.AppendLine("            document.getElementById('dataPanel').innerHTML = `⛽ ${closest.fuel.toFixed(1) || '--'} л &nbsp;|&nbsp; 🛠️ ${closest.damage.toFixed(1) || '--'}%`;");
            sb.AppendLine("            const pitchRaw = closest.pitch || 0;");
            sb.AppendLine("            const rollRaw = closest.roll || 0;");
            sb.AppendLine("            let pitchDeg = pitchRaw * PITCH_SCALE;");
            sb.AppendLine("            let rollDeg = rollRaw * ROLL_SCALE;");
            sb.AppendLine("            if (typeof currentPitchRoll === 'undefined') window.currentPitchRoll = {};");
            sb.AppendLine("            window.currentPitchRoll.pitch = pitchDeg;");
            sb.AppendLine("            window.currentPitchRoll.roll = rollDeg;");
            sb.AppendLine("            updatePitchRollPanel(pitchDeg, rollDeg);");
            // --- Дополнительная информация (head heading, local scale) ---
            sb.AppendLine("            const headOffset = closest.headOffset || [0,0,0,0,0,0];");
            sb.AppendLine("            const headHeadingDeg = headOffset[3] * 360; // 0..1 -> 0..360");
            sb.AppendLine("            const localScale = closest.localScale || 1.0;");
            sb.AppendLine("            document.getElementById('extraInfo').innerHTML = `Head heading: ${headHeadingDeg.toFixed(1)}° | Scale: ${localScale.toFixed(2)}`;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ----- 3D инициализация -----
        private string GenerateThreeJSInit()
        {
            return @"// ================================================================
// 3D ИНИЦИАЛИЗАЦИЯ (ортографическая проекция, pivot на колёсах)
// ================================================================
let use3d = true;
let threeInitialized = false;
let scene, camera, renderer, pivotGroup, container;
let threeReady = false;
let truckPosScreen = { x: 0, y: 0 };
let truckHeading = 0;
let truckPitch = 0;
let truckRoll = 0;

function initThree() {
    if (typeof THREE === 'undefined') {
        console.warn('Three.js не загружен.');
        threeReady = false;
        return;
    }
    container = document.getElementById('threeContainer');
    if (!container) return;
    const w = window.innerWidth;
    const h = window.innerHeight - 150;
    const frustumSize = 4;
    const aspect = w / h;
    scene = new THREE.Scene();
    camera = new THREE.OrthographicCamera(
        -frustumSize * aspect / 2,
        frustumSize * aspect / 2,
        frustumSize / 2,
        -frustumSize / 2,
        0.1,
        100
    );
    camera.position.set(0, 5, 5);
    camera.lookAt(0, 0, 0);

    renderer = new THREE.WebGLRenderer({ alpha: true });
    renderer.setSize(w, h);
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.setClearColor(0x000000, 0);
    container.appendChild(renderer.domElement);

    // Создаём модель грузовика (упрощённо)
    const truckGroup = new THREE.Group();
    const geometry = new THREE.BoxGeometry(0.8, 0.4, 0.3);
    const material = new THREE.MeshStandardMaterial({ color: 0x2a7fff, roughness: 0.4, metalness: 0.6 });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.y = 0.2; // поднимаем, чтобы колёса были на уровне 0
    truckGroup.add(mesh);

    // Кабина
    const cabGeo = new THREE.BoxGeometry(0.3, 0.15, 0.2);
    const cabMat = new THREE.MeshStandardMaterial({ color: 0x3a9aff });
    const cab = new THREE.Mesh(cabGeo, cabMat);
    cab.position.set(0.3, 0.35, 0);
    truckGroup.add(cab);

    // Колёса
    const wheelMat = new THREE.MeshStandardMaterial({ color: 0x222222, roughness: 0.8 });
    const wheelGeo = new THREE.CylinderGeometry(0.08, 0.08, 0.04, 8);
    for (let x of [-0.3, 0.3]) {
        for (let z of [-0.2, 0.2]) {
            const wheel = new THREE.Mesh(wheelGeo, wheelMat);
            wheel.position.set(x, 0, z);
            wheel.rotation.x = Math.PI/2;
            truckGroup.add(wheel);
        }
    }

    // Группа-пивот (вращение будет вокруг нижней оси)
    pivotGroup = new THREE.Group();
    pivotGroup.add(truckGroup);
    // Смещаем так, чтобы pivot был в центре колёс (Y=0)
    // truckGroup.position.y = -0.2; // если нужно сместить, но мы уже задали position.y = 0.2 для mesh
    // Проще: pivotGroup.position.y = 0; и вращаем pivotGroup

    scene.add(pivotGroup);

    // Освещение
    const ambientLight = new THREE.AmbientLight(0x404060);
    scene.add(ambientLight);
    const dirLight = new THREE.DirectionalLight(0xffffff, 1);
    dirLight.position.set(1, 2, 1);
    scene.add(dirLight);
    const backLight = new THREE.DirectionalLight(0x8888ff, 0.5);
    backLight.position.set(-1, 0, -1);
    scene.add(backLight);

    threeInitialized = true;
    threeReady = true;

    window.addEventListener('resize', () => {
        if (!threeReady) return;
        const w2 = window.innerWidth;
        const h2 = window.innerHeight - 150;
        const aspect2 = w2 / h2;
        camera.left = -frustumSize * aspect2 / 2;
        camera.right = frustumSize * aspect2 / 2;
        camera.top = frustumSize / 2;
        camera.bottom = -frustumSize / 2;
        camera.updateProjectionMatrix();
        renderer.setSize(w2, h2);
    });

    function animate() {
        if (!threeReady) return;
        requestAnimationFrame(animate);
        if (pivotGroup && use3d) {
            // Позиционирование в NDC
            const x = (truckPosScreen.x / window.innerWidth) * 2 - 1;
            const y = - (truckPosScreen.y / (window.innerHeight - 150)) * 2 + 1;
            const scaleFactor = 0.5;
            pivotGroup.position.set(x * 3, y * 2, 0);
            // Вращение
            const hRad = truckHeading * Math.PI / 180;
            const pRad = truckPitch * Math.PI / 180;
            const rRad = truckRoll * Math.PI / 180;
            pivotGroup.rotation.order = 'YXZ';
            pivotGroup.rotation.set(pRad, hRad, rRad);
        }
        renderer.render(scene, camera);
    }
    animate();
}";
        }

        // ----- Панель pitch/roll -----
        private string GeneratePitchRollPanel()
        {
            return @"// ================================================================
// ПАНЕЛЬ PITCH/ROLL (только числа)
// ================================================================
let pitchRollState = { pitchMax: -Infinity, pitchMin: Infinity, rollMax: -Infinity, rollMin: Infinity };
function updatePitchRollPanel(pitchDeg, rollDeg) {
    if (pitchDeg > pitchRollState.pitchMax) pitchRollState.pitchMax = pitchDeg;
    if (pitchDeg < pitchRollState.pitchMin) pitchRollState.pitchMin = pitchDeg;
    if (rollDeg > pitchRollState.rollMax) pitchRollState.rollMax = rollDeg;
    if (rollDeg < pitchRollState.rollMin) pitchRollState.rollMin = rollDeg;
    document.getElementById('pitchValue3d').textContent = pitchDeg.toFixed(1) + '°';
    document.getElementById('rollValue3d').textContent = rollDeg.toFixed(1) + '°';
    document.getElementById('pitchMin3d').textContent = pitchRollState.pitchMin.toFixed(1);
    document.getElementById('pitchMax3d').textContent = pitchRollState.pitchMax.toFixed(1);
    document.getElementById('rollMin3d').textContent = pitchRollState.rollMin.toFixed(1);
    document.getElementById('rollMax3d').textContent = pitchRollState.rollMax.toFixed(1);
    const resetBtn = document.getElementById('resetPitchRollBtn');
    if (resetBtn) {
        resetBtn.onclick = function() {
            pitchRollState.pitchMax = -Infinity;
            pitchRollState.pitchMin = Infinity;
            pitchRollState.rollMax = -Infinity;
            pitchRollState.rollMin = Infinity;
            if (window.currentPitchRoll) {
                updatePitchRollPanel(window.currentPitchRoll.pitch, window.currentPitchRoll.roll);
            }
        };
    }
}";
        }

        // ----- Управление (UI controls) -----
        private string GenerateUIControls()
        {
            return @"// ================================================================
// УПРАВЛЕНИЕ КАРТОЙ (wheel, drag, click)
// ================================================================
canvas.addEventListener('wheel', (e) => { e.preventDefault(); const delta = e.deltaY > 0 ? 0.9 : 1.1; scale *= delta; if (scale<0.001) scale=0.001; if (scale>1000) scale=1000; drawMap(); }, { passive: false });
canvas.addEventListener('mousedown', (e) => { if (e.button===0) { isDragging = true; dragStartX = e.clientX; dragStartY = e.clientY; dragStartCX = centerX; dragStartCZ = centerZ; canvas.style.cursor = 'grabbing'; } });
window.addEventListener('mousemove', (e) => { if (isDragging) { const dx = (e.clientX - dragStartX) / scale; const dy = (dragStartY - e.clientY) / scale; centerX = dragStartCX - dx; centerZ = dragStartCZ - dy; targetCenterX = centerX; targetCenterZ = centerZ; drawMap(); } const rect = canvas.getBoundingClientRect(); const mx = e.clientX - rect.left; const my = e.clientY - rect.top; if (mx>=0 && mx<=W && my>=0 && my<=H) { const wx = centerX + (mx - W/2) / scale; const wz = centerZ - (my - H/2) / scale; document.getElementById('cursorCoords').textContent = `📍 ${wx.toFixed(1)}, ${wz.toFixed(1)}`; } });
window.addEventListener('mouseup', () => { if (isDragging) { isDragging = false; canvas.style.cursor = 'grab'; } });

// Клик для копирования
let notes = [];
canvas.addEventListener('click', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx<0 || mx>W || my<0 || my>H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    const coordStr = `${wx.toFixed(2)}, ${wz.toFixed(2)}`;
    let objName = '';
    for (const c of cities) { const dx = c.x - wx; const dz = c.z - wz; if (dx*dx + dz*dz < 100) { objName = c.name; break; } }
    if (!objName) { for (const t of customTargets) { const dx = t.x - wx; const dz = t.z - wz; if (dx*dx + dz*dz < 100) { objName = t.name; break; } } }
    if (!objName) { for (const e of events) { const dx = e.x - wx; const dz = e.z - wz; if (dx*dx + dz*dz < 100) { objName = e.label || ''; break; } } }
    const note = objName ? `${coordStr} – ${objName}` : coordStr;
    notes.push(note);
    navigator.clipboard?.writeText(coordStr);
    const toast = document.createElement('div');
    toast.style.cssText = 'position:fixed;bottom:170px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,0.8);color:#fff;padding:4px 12px;border-radius:4px;font-size:12px;z-index:999;pointer-events:none;';
    toast.textContent = `Скопировано: ${coordStr}`;
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 2000);
});

// Двойной клик - измерение
let measureMode = false; let measureStart = null;
canvas.addEventListener('dblclick', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx<0 || mx>W || my<0 || my>H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    if (!measureMode) { measureMode = true; measureStart = { x: wx, z: wz }; document.getElementById('measureTool').classList.add('active'); document.getElementById('measureDist').textContent = '0.0'; return; }
    const dx = wx - measureStart.x; const dz = wz - measureStart.z;
    const dist = Math.sqrt(dx*dx + dz*dz);
    document.getElementById('measureDist').textContent = dist.toFixed(1);
    measureMode = false; measureStart = null;
    setTimeout(() => document.getElementById('measureTool').classList.remove('active'), 5000);
});
";
        }

        // ----- Таймлайн -----
        private string GenerateTimelineControls()
        {
            return @"// ================================================================
// ТАЙМЛАЙН
// ================================================================
let playing = false, speedFactor = 1, currentIndex = 0, follow = true, interpolate = true;
let playStartTime = 0, playStartElapsed = 0, currentTime = 0;

const playBtn = document.getElementById('playBtn');
const speedLabel = document.getElementById('speedLabel');
const timeSlider = document.getElementById('timeSlider');
const timeDisplay = document.getElementById('timeDisplay');
const followCheck = document.getElementById('followCheck');
const interpolateCheck = document.getElementById('interpolateCheck');
const stepInput = document.getElementById('stepInput');
const beginBtn = document.getElementById('beginBtn');
const endBtn = document.getElementById('endBtn');
const stepBackBtn = document.getElementById('stepBackBtn');
const stepForwardBtn = document.getElementById('stepForwardBtn');
const prevEventBtn = document.getElementById('prevEventBtn');
const nextEventBtn = document.getElementById('nextEventBtn');
const use3dCheck = document.getElementById('use3dCheck');

function formatTime(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    const ms = Math.floor((seconds - Math.floor(seconds)) * 1000);
    return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}.${String(ms).padStart(3,'0')}`;
}

function updateTimeDisplay() {
    const percent = totalDuration > 0 ? currentTime / totalDuration : 0;
    timeSlider.value = percent * 1000;
    timeDisplay.textContent = formatTime(currentTime);
    drawMap();
}

function setTimeByValue(value) {
    const target = value * totalDuration;
    currentTime = Math.min(target, totalDuration);
    let idx = 0;
    for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; }
    currentIndex = Math.min(idx, trailPoints.length-1);
    if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; }
    updateTimeDisplay();
}

function playStep() {
    if (!playing) return;
    const now = performance.now() / 1000;
    const elapsed = (now - playStartTime) * speedFactor + playStartElapsed;
    currentTime = Math.min(elapsed, totalDuration);
    let idx = 0;
    for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; }
    currentIndex = Math.min(idx, trailPoints.length-1);
    if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; }
    updateTimeDisplay();
    if (currentTime >= totalDuration) { playing = false; playBtn.textContent = '▶'; return; }
    requestAnimationFrame(playStep);
}

playBtn.addEventListener('click', () => {
    playing = !playing;
    playBtn.textContent = playing ? '⏸' : '▶';
    if (playing) {
        if (currentTime >= totalDuration) { currentTime = 0; currentIndex = 0; if (follow) { targetCenterX = trailPoints[0].x; targetCenterZ = trailPoints[0].z; } }
        playStartTime = performance.now() / 1000;
        playStartElapsed = currentTime;
        playStep();
    }
});
beginBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } currentTime = 0; currentIndex = 0; if (follow) { targetCenterX = trailPoints[0].x; targetCenterZ = trailPoints[0].z; } updateTimeDisplay(); });
endBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } currentTime = totalDuration; currentIndex = trailPoints.length-1; if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; } updateTimeDisplay(); });
stepBackBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } const step = parseFloat(stepInput.value) || 1; let targetTime = currentTime - step; if (targetTime < 0) targetTime = 0; currentTime = targetTime; let idx = 0; for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; } currentIndex = Math.min(idx, trailPoints.length-1); if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; } updateTimeDisplay(); });
stepForwardBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } const step = parseFloat(stepInput.value) || 1; let targetTime = currentTime + step; if (targetTime > totalDuration) targetTime = totalDuration; currentTime = targetTime; let idx = 0; for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; } currentIndex = Math.min(idx, trailPoints.length-1); if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; } updateTimeDisplay(); });
prevEventBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } const currentT = currentTime; let prevEvent = null; for (const e of events) { if (e.t < currentT) { prevEvent = e; } else break; } if (prevEvent) { currentTime = prevEvent.t; let idx = 0; for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; } currentIndex = Math.min(idx, trailPoints.length-1); if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; } updateTimeDisplay(); } });
nextEventBtn.addEventListener('click', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } const currentT = currentTime; let nextEvent = null; for (const e of events) { if (e.t > currentT) { nextEvent = e; break; } } if (nextEvent) { currentTime = nextEvent.t; let idx = 0; for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; } currentIndex = Math.min(idx, trailPoints.length-1); if (follow) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; } updateTimeDisplay(); } });
speedDownBtn.addEventListener('click', () => { speedFactor = Math.max(0.5, speedFactor/1.5); speedLabel.textContent = speedFactor.toFixed(1)+'×'; });
speedUpBtn.addEventListener('click', () => { speedFactor = Math.min(5, speedFactor*1.5); speedLabel.textContent = speedFactor.toFixed(1)+'×'; });
timeSlider.addEventListener('input', () => { if (playing) { playing = false; playBtn.textContent = '▶'; } const val = parseFloat(timeSlider.value) / 1000; setTimeByValue(val); });
followCheck.addEventListener('change', () => { follow = followCheck.checked; if (follow && currentIndex < trailPoints.length) { targetCenterX = trailPoints[currentIndex].x; targetCenterZ = trailPoints[currentIndex].z; drawMap(); } });
interpolateCheck.addEventListener('change', () => { interpolate = interpolateCheck.checked; drawMap(); });
use3dCheck.addEventListener('change', () => {
    use3d = use3dCheck.checked;
    if (use3d && !threeInitialized) { initThree(); }
    const container = document.getElementById('threeContainer');
    if (container) container.style.display = use3d ? 'block' : 'none';
    drawMap();
});";
        }

        // ----- Инициализация -----
        private string GenerateInitialization()
        {
            return @"// ================================================================
// ИНИЦИАЛИЗАЦИЯ
// ================================================================
resize(); fitMap();
if (trailPoints.length > 0) { currentTime = 0; currentIndex = 0; targetCenterX = trailPoints[0].x; targetCenterZ = trailPoints[0].z; centerX = targetCenterX; centerZ = targetCenterZ; }
if (use3dCheck && use3dCheck.checked) {
    if (typeof THREE !== 'undefined') { initThree(); } else { console.warn('Three.js не загружен.'); use3d = false; use3dCheck.checked = false; }
} else { use3d = false; const container = document.getElementById('threeContainer'); if (container) container.style.display = 'none'; }
drawMap(); updateTimeDisplay();
window.addEventListener('resize', () => { resize(); fitMap(); drawMap(); });";
        }

        // ================================================================
        // ГЕНЕРАЦИЯ СТРАНИЦЫ СПИСКА ТРЕКОВ (без изменений)
        // ================================================================
        private string GenerateTrailPlayerHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
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
<h1 style='text-align:center;'>ETS2 Trail Player</h1>
<div id='trackList'>Загрузка...</div>
<div id='playerContainer'></div>
<script>
async function loadTracks() {
    try {
        const res = await fetch('http://localhost:8083/list_tracks');
        const data = await res.json();
        const list = document.getElementById('trackList');
        list.innerHTML = '';
        if (data.files.length === 0) {
            list.innerHTML = '<div style='color:#666;'>Нет сохранённых треков</div>';
            return;
        }
        for (const file of data.files) {
            const div = document.createElement('div');
            div.className = 'track-item';
            const name = file.replace('.json', '');
            div.textContent = name;
            div.addEventListener('click', () => {
                const container = document.getElementById('playerContainer');
                container.innerHTML = `<iframe src='http://localhost:8082/saved_tracks/${name}.html'></iframe>`;
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
    }
}