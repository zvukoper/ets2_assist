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
            string escapedData = JsonConvert.ToString(compactData);
            string metaJson = meta != null ? meta.ToString(Formatting.None) : "{}";
            string mapJson = mapData != null ? mapData.ToString(Formatting.None) : "{}";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"utf-8\"/>");
            sb.AppendLine("    <title>ETS2 Trail Viewer</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { margin:0; background:#0a0c10; color:#e0e0e0; font-family:'Segoe UI',sans-serif; overflow:hidden; }");
            sb.AppendLine("        #mapCanvas { display:block; width:100vw; height:calc(100vh - 150px); background:#111; cursor:grab; }");
            sb.AppendLine("        #controls { position:fixed; bottom:0; left:0; right:0; background:#1a1f26; padding:8px 15px; border-top:1px solid #333; display:flex; flex-direction:column; gap:6px; }");
            sb.AppendLine("        #controlsTop { display:flex; align-items:center; gap:8px; flex-wrap:wrap; }");
            sb.AppendLine("        #controlsBottom { display:flex; align-items:center; gap:10px; }");
            sb.AppendLine("        #controls button, #controls input { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:4px 10px; font-size:13px; cursor:pointer; }");
            sb.AppendLine("        #controls button:hover { background:#3a4a5a; }");
            sb.AppendLine("        #timeSlider { flex:1; min-width:200px; }");
            sb.AppendLine("        #speedLabel { font-size:13px; color:#8fa0b9; }");
            sb.AppendLine("        #checkboxFollow { margin-left:8px; }");
            sb.AppendLine("        #timeDisplay { font-size:13px; color:#aabbcc; min-width:120px; font-family:monospace; }");
            sb.AppendLine("        #stepInput { width:60px; background:#0c1016; border:1px solid #2f3845; border-radius:4px; padding:3px 6px; color:#e0e9f5; font-size:13px; font-family:monospace; }");
            sb.AppendLine("        #info { position:absolute; top:10px; right:10px; background:rgba(0,0,0,0.7); padding:6px 12px; border-radius:6px; font-size:12px; color:#ccc; }");
            sb.AppendLine("        #dataPanel { position:absolute; bottom:160px; left:20px; background:rgba(0,0,0,0.7); padding:8px 14px; border-radius:6px; font-size:11px; color:#aabbcc; border:1px solid #333; backdrop-filter:blur(4px); pointer-events:none; }");
            sb.AppendLine("        #titlePanel { position:absolute; top:10px; left:10px; background:rgba(0,0,0,0.7); padding:6px 14px; border-radius:6px; font-size:12px; color:#e0e0e0; border:1px solid #444; backdrop-filter:blur(4px); pointer-events:none; }");
            sb.AppendLine("        #cursorCoords { position:absolute; bottom:160px; right:20px; background:rgba(0,0,0,0.7); padding:2px 8px; border-radius:4px; font-size:10px; color:#8fa0b9; pointer-events:none; }");
            sb.AppendLine("        #measureTool { position:absolute; top:60px; left:10px; background:rgba(0,0,0,0.7); padding:4px 10px; border-radius:4px; font-size:11px; color:#ffc857; border:1px solid #ffc85744; display:none; pointer-events:none; }");
            sb.AppendLine("        #measureTool.active { display:block; }");
            sb.AppendLine("        .note-btn { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:2px 6px; font-size:11px; cursor:pointer; }");
            sb.AppendLine("        .note-btn:hover { background:#3a4a5a; }");
            sb.AppendLine("        #prIndicator { position:absolute; bottom:160px; left:50%; transform:translateX(-50%); background:rgba(0,0,0,0.7); padding:4px 12px; border-radius:6px; font-size:11px; color:#aabbcc; border:1px solid #444; backdrop-filter:blur(4px); display:flex; gap:16px; pointer-events:none; }");
            sb.AppendLine("        #prIndicator .pr-item { display:flex; align-items:center; gap:6px; }");
            sb.AppendLine("        #prIndicator .pr-bar { width:60px; height:6px; background:#2a3545; border-radius:3px; overflow:hidden; }");
            sb.AppendLine("        #prIndicator .pr-fill { height:100%; border-radius:3px; transition:width 0.05s; }");
            sb.AppendLine("        #prIndicator .pr-label { color:#8fa0b9; font-size:10px; min-width:30px; text-align:right; }");
            sb.AppendLine("        #prPanel { position:absolute; top:50px; left:10px; background:rgba(0,0,0,0.85); padding:10px 14px; border-radius:8px; border:1px solid #444; backdrop-filter:blur(4px); pointer-events:none; z-index:30; min-width:200px; }");
            sb.AppendLine("        #truck3d { width:80px; height:80px; perspective:400px; display:inline-block; margin-right:12px; }");
            sb.AppendLine("        #truck3d .truck-body { width:100%; height:100%; transform-style:preserve-3d; transition:transform 0.05s; position:relative; }");
            sb.AppendLine("        #truck3d .truck-body svg { width:100%; height:100%; display:block; }");
            sb.AppendLine("        #prPanel .pr-info { display:flex; flex-direction:column; gap:2px; flex:1; }");
            sb.AppendLine("        #prPanel .pr-row { display:flex; gap:12px; }");
            sb.AppendLine("        #prPanel .pr-label { font-size:11px; }");
            sb.AppendLine("        #prPanel .pr-value { font-size:13px; font-weight:bold; }");
            sb.AppendLine("        #prPanel .pr-extremes { display:flex; justify-content:space-between; gap:8px; font-size:10px; color:#6a7b94; margin-top:2px; flex-wrap:wrap; }");
            sb.AppendLine("        #prPanel .pr-reset-btn { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:1px 8px; font-size:9px; cursor:pointer; pointer-events:auto; }");
            sb.AppendLine("        #debugLog { position:absolute; bottom:200px; right:20px; background:rgba(0,0,0,0.8); padding:4px 10px; border-radius:4px; font-size:9px; color:#88ff88; font-family:monospace; pointer-events:none; z-index:100; border:1px solid #4a5a6a; max-height:100px; overflow-y:auto; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div id=\"info\">Просмотр трека</div>");
            sb.AppendLine("<div id=\"titlePanel\"></div>");
            sb.AppendLine("<div id=\"dataPanel\">⛽ -- л &nbsp;|&nbsp; 🛠️ --%</div>");
            sb.AppendLine("<div id=\"cursorCoords\"></div>");
            sb.AppendLine("<div id=\"measureTool\">📏 Расстояние: <span id=\"measureDist\">0.0</span> м</div>");
            sb.AppendLine("<div id=\"prIndicator\">");
            sb.AppendLine("    <div class=\"pr-item\">");
            sb.AppendLine("        <span>Pitch</span>");
            sb.AppendLine("        <div class=\"pr-bar\"><div class=\"pr-fill\" id=\"pitchFill\" style=\"width:50%;background:#ff6b6b;\"></div></div>");
            sb.AppendLine("        <span class=\"pr-label\" id=\"pitchLabel\">0</span>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class=\"pr-item\">");
            sb.AppendLine("        <span>Roll</span>");
            sb.AppendLine("        <div class=\"pr-bar\"><div class=\"pr-fill\" id=\"rollFill\" style=\"width:50%;background:#4ecdc4;\"></div></div>");
            sb.AppendLine("        <span class=\"pr-label\" id=\"rollLabel\">0</span>");
            sb.AppendLine("    </div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div id=\"prPanel\">");
            sb.AppendLine("    <div style=\"display:flex; align-items:center;\">");
            sb.AppendLine("        <div id=\"truck3d\">");
            sb.AppendLine("            <div class=\"truck-body\" id=\"truck3dBody\">");
            sb.AppendLine("                <svg viewBox=\"0 0 100 100\">");
            sb.AppendLine("                    <rect x=\"25\" y=\"30\" width=\"30\" height=\"20\" rx=\"3\" fill=\"#2a7fff\" stroke=\"#fff\" stroke-width=\"1.5\"/>");
            sb.AppendLine("                    <rect x=\"45\" y=\"20\" width=\"40\" height=\"40\" rx=\"3\" fill=\"#3a9aff\" stroke=\"#fff\" stroke-width=\"1.5\"/>");
            sb.AppendLine("                    <circle cx=\"35\" cy=\"65\" r=\"8\" fill=\"#222\" stroke=\"#aaa\" stroke-width=\"2\"/>");
            sb.AppendLine("                    <circle cx=\"70\" cy=\"65\" r=\"8\" fill=\"#222\" stroke=\"#aaa\" stroke-width=\"2\"/>");
            sb.AppendLine("                    <circle cx=\"28\" cy=\"35\" r=\"3\" fill=\"#ffdd44\"/>");
            sb.AppendLine("                    <circle cx=\"28\" cy=\"45\" r=\"3\" fill=\"#ffdd44\"/>");
            sb.AppendLine("                    <rect x=\"28\" y=\"32\" width=\"12\" height=\"6\" rx=\"1\" fill=\"#88ccff\" opacity=\"0.7\"/>");
            sb.AppendLine("                    <rect x=\"28\" y=\"42\" width=\"12\" height=\"6\" rx=\"1\" fill=\"#88ccff\" opacity=\"0.7\"/>");
            sb.AppendLine("                </svg>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"pr-info\">");
            sb.AppendLine("            <div class=\"pr-row\">");
            sb.AppendLine("                <span class=\"pr-label\" style=\"color:#ff6b6b;\">Pitch</span>");
            sb.AppendLine("                <span class=\"pr-value\" style=\"color:#ff6b6b;\" id=\"pitchValue3d\">0.0°</span>");
            sb.AppendLine("                <span class=\"pr-label\" style=\"color:#4ecdc4;\">Roll</span>");
            sb.AppendLine("                <span class=\"pr-value\" style=\"color:#4ecdc4;\" id=\"rollValue3d\">0.0°</span>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"pr-extremes\">");
            sb.AppendLine("                <span>Pitch min: <span id=\"pitchMin3d\">0.0</span>°</span>");
            sb.AppendLine("                <span>Pitch max: <span id=\"pitchMax3d\">0.0</span>°</span>");
            sb.AppendLine("                <span>Roll min: <span id=\"rollMin3d\">0.0</span>°</span>");
            sb.AppendLine("                <span>Roll max: <span id=\"rollMax3d\">0.0</span>°</span>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div style=\"margin-top:4px; text-align:right;\">");
            sb.AppendLine("                <button id=\"resetPitchRollBtn\" class=\"pr-reset-btn\">Сбросить экстремумы</button>");
            sb.AppendLine("            </div>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div id=\"debugLog\">Debug</div>");
            sb.AppendLine("<canvas id=\"mapCanvas\"></canvas>");
            sb.AppendLine("<div id=\"controls\">");
            sb.AppendLine("    <div id=\"controlsTop\">");
            sb.AppendLine("        <button id=\"playBtn\">▶</button>");
            sb.AppendLine("        <button id=\"beginBtn\">⏮</button>");
            sb.AppendLine("        <button id=\"endBtn\">⏭</button>");
            sb.AppendLine("        <button id=\"stepBackBtn\">◀</button>");
            sb.AppendLine("        <button id=\"stepForwardBtn\">▶</button>");
            sb.AppendLine("        <button id=\"prevEventBtn\">⏪</button>");
            sb.AppendLine("        <button id=\"nextEventBtn\">⏩</button>");
            sb.AppendLine("        <span style=\"color:#8fa0b9;font-size:13px;\">Шаг:</span>");
            sb.AppendLine("        <input type=\"number\" id=\"stepInput\" value=\"1\" min=\"0.5\" max=\"600\" step=\"0.5\">");
            sb.AppendLine("        <span style=\"color:#8fa0b9;font-size:12px;\">с</span>");
            sb.AppendLine("        <span id=\"speedLabel\">1×</span>");
            sb.AppendLine("        <button id=\"speedDownBtn\">−</button>");
            sb.AppendLine("        <button id=\"speedUpBtn\">+</button>");
            sb.AppendLine("        <label style=\"color:#8fa0b9;font-size:13px;\"><input type=\"checkbox\" id=\"followCheck\" checked> Следить</label>");
            sb.AppendLine("        <label style=\"color:#8fa0b9;font-size:13px;\"><input type=\"checkbox\" id=\"interpolateCheck\" checked> Интерполяция</label>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div id=\"controlsBottom\">");
            sb.AppendLine("        <input type=\"range\" id=\"timeSlider\" min=\"0\" max=\"1000\" value=\"0\" style=\"flex:1;\">");
            sb.AppendLine("        <span id=\"timeDisplay\">00:00:00.000</span>");
            sb.AppendLine("    </div>");
            sb.AppendLine("</div>");
            sb.AppendLine("<script>");
            sb.AppendLine($"const compactData = {escapedData};");
            sb.AppendLine($"const metaData = {metaJson};");
            sb.AppendLine($"const mapData = {mapJson};");

            // Весь остальной JavaScript код пишем как verbatim string
            sb.AppendLine(@"
// ================================================================
// КОНСТАНТЫ
// ================================================================
const NEARBY_CITIES_COUNT = 4;
const RAW_TO_DEGREES = 360 / 283;
const ROLL_CALIBRATION_FACTOR = 0.5;
const PITCH_CALIBRATION_FACTOR = 1.0;

function normalizeAngle(deg) {
    while (deg > 180) deg -= 360;
    while (deg < -180) deg += 360;
    return deg * 10;
}

// ================================================================
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
                roll: parseFloat(parts[8] || 0)
            };
            dataPoints.push(dp);
            debugLines.push('[DEBUG] D: t=' + dp.t + ', pitch=' + dp.pitch + ', roll=' + dp.roll);
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
}

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
document.getElementById('titlePanel').innerHTML = title + (desc ? '<br><span style=''font-size:10px; color:#888;''>'+desc+'</span>' : '');

const canvas = document.getElementById('mapCanvas');
const ctx = canvas.getContext('2d');
let W, H;
function resize() { W = canvas.width = window.innerWidth; H = canvas.height = window.innerHeight - 150; drawMap(); }
window.addEventListener('resize', resize);

let centerX = 0, centerZ = 0, scale = 1;
let dragStartX = 0, dragStartY = 0, dragStartCX = 0, dragStartCZ = 0, isDragging = false;
let targetCenterX = 0, targetCenterZ = 0;

function fitMap() {
    if (trailPoints.length < 2) return;
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const p of trailPoints) {
        if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
        if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
    }
    centerX = (minX + maxX) / 2;
    centerZ = (minZ + maxZ) / 2;
    targetCenterX = centerX;
    targetCenterZ = centerZ;
    const range = Math.max(maxX - minX, maxZ - minZ, 1);
    scale = (Math.min(W, H) * 0.85) / (range * 1.15);
}

function worldToScreen(wx, wz) {
    const dx = (wx - centerX) * scale;
    const dz = (wz - centerZ) * scale;
    return { x: W/2 + dx, y: H/2 - dz };
}

function getTrailColor(speed) {
    const minS = meta.minSpeed || 0;
    const maxS = meta.maxSpeed || 125;
    const s = Math.max(minS, Math.min(maxS, speed));
    const t = (s - minS) / (maxS - minS);
    let r,g,b;
    if (t <= 0.2) { const u = t/0.2; r=0; g=u*255; b=255; }
    else if (t <= 0.4) { const u=(t-0.2)/0.2; r=0; g=255; b=255-u*255; }
    else if (t <= 0.6) { const u=(t-0.4)/0.2; r=u*255; g=255; b=0; }
    else if (t <= 0.8) { const u=(t-0.6)/0.2; r=255; g=255-u*(255-165); b=0; }
    else { const u=(t-0.8)/0.2; r=255-u*(255-128); g=165-u*165; b=u*255; }
    return 'rgb(' + Math.round(r) + ', ' + Math.round(g) + ', ' + Math.round(b) + ')';
}

function formatDistance(d) { if (d < 1000) return Math.round(d)+'м'; return (Math.round(d/1000))+'км'; }

let currentIndex = 0;
let currentTime = 0;
let playing = false;
let speedFactor = 1;
let follow = true;
let interpolate = true;
let playStartTime = 0;
let playStartElapsed = 0;
let closest = null;

function drawMap() {
    ctx.clearRect(0, 0, W, H);
    ctx.fillStyle = '#0f1217';
    ctx.fillRect(0, 0, W, H);

    // Сетка
    const gridStep = 200 / scale;
    ctx.strokeStyle = '#2a3545';
    ctx.lineWidth = 0.5;
    ctx.setLineDash([4,6]);
    for (let x = -W/2; x < W/2; x += gridStep) {
        const p = worldToScreen(centerX+x, centerZ);
        ctx.beginPath(); ctx.moveTo(p.x,0); ctx.lineTo(p.x,H); ctx.stroke();
    }
    for (let z = -H/2; z < H/2; z += gridStep) {
        const p = worldToScreen(centerX, centerZ+z);
        ctx.beginPath(); ctx.moveTo(0,p.y); ctx.lineTo(W,p.y); ctx.stroke();
    }
    ctx.setLineDash([]);

    // Дороги
    for (const r of roads) {
        const p1 = worldToScreen(r.x1, r.z1);
        const p2 = worldToScreen(r.x2, r.z2);
        ctx.beginPath(); ctx.moveTo(p1.x,p1.y); ctx.lineTo(p2.x,p2.y);
        ctx.strokeStyle = '#5a7a8a';
        ctx.lineWidth = 1.5;
        ctx.globalAlpha = 0.6;
        ctx.stroke();
    }
    ctx.globalAlpha = 1;

    // Города
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const c of cities) {
        const p = worldToScreen(c.x, c.z);
        if (p.x < 0 || p.x > W || p.y < 0 || p.y > H) continue;
        ctx.beginPath(); ctx.arc(p.x, p.y, 4, 0, 2*Math.PI);
        ctx.fillStyle = '#ffdd88';
        ctx.shadowColor = '#ffdd8844';
        ctx.shadowBlur = 6;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.font = '10px ""Segoe UI""';
        ctx.fillStyle = '#c8ddee';
        ctx.fillText(c.name, p.x, p.y-6);
    }

    // Цели
    for (const t of customTargets) {
        const p = worldToScreen(t.x, t.z);
        if (p.x < 0 || p.x > W || p.y < 0 || p.y > H) continue;
        ctx.beginPath(); ctx.arc(p.x, p.y, t.active ? 6 : 4, 0, 2*Math.PI);
        ctx.fillStyle = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');
        ctx.shadowColor = t.active ? '#ffc85788' : '#88aadd88';
        ctx.shadowBlur = t.active ? 16 : 8;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = t.active ? 1.5 : 0.5;
        ctx.stroke();
        ctx.font = t.active ? 'bold 10px ""Segoe UI""' : '9px ""Segoe UI""';
        ctx.fillStyle = '#fff';
        ctx.shadowColor = 'rgba(0,0,0,0.8)';
        ctx.shadowBlur = 4;
        ctx.fillText(t.name, p.x, p.y - (t.active ? 14 : 10));
        ctx.shadowBlur = 0;
    }

    // Ближайшие города и цели за пределами экрана
    const cx = W/2, cy = H/2;
    const radius = Math.min(W, H) * 0.42;
    const currentPos = currentIndex < trailPoints.length ? trailPoints[currentIndex] : { x:0, z:0 };
    // Ближайшие города
    const cityDist = cities.map(c => ({ ...c, dist: Math.hypot(c.x - currentPos.x, c.z - currentPos.z) }));
    cityDist.sort((a,b) => a.dist - b.dist);
    const nearCities = cityDist.slice(0, NEARBY_CITIES_COUNT);
    for (const c of nearCities) {
        const p = worldToScreen(c.x, c.z);
        if (p.x >= 0 && p.x <= W && p.y >= 0 && p.y <= H) continue;
        const dx = p.x - cx, dy = p.y - cy; const len = Math.hypot(dx, dy); if (len < 0.01) continue;
        const nx = dx/len, ny = dy/len;
        const arrowX = cx + nx * radius, arrowY = cy + ny * radius;
        const angle = Math.atan2(ny, nx);
        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);
        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();
        ctx.fillStyle = '#aabbcc'; ctx.shadowColor = 'rgba(0,0,0,0.6)'; ctx.shadowBlur = 4; ctx.fill();
        ctx.strokeStyle='#000'; ctx.lineWidth=1; ctx.stroke(); ctx.shadowBlur=0; ctx.restore();
        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;
        if (lx < 50) lx = 50; if (lx > W-50) lx = W-50; if (ly < 20) ly = 20; if (ly > H-20) ly = H-20;
        ctx.font = '10px ""Segoe UI""';
        ctx.fillStyle = '#c8ddee';
        ctx.shadowColor='rgba(0,0,0,0.8)';
        ctx.shadowBlur=4;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(c.name + ' (' + formatDistance(c.dist) + ')', lx, ly);
        ctx.shadowBlur = 0;
    }

    // Цели за пределами экрана
    for (const t of customTargets) {
        const p = worldToScreen(t.x, t.z);
        if (p.x >= 0 && p.x <= W && p.y >= 0 && p.y <= H) continue;
        const dx = p.x - cx, dy = p.y - cy; const len = Math.hypot(dx, dy); if (len < 0.01) continue;
        const nx = dx/len, ny = dy/len;
        const arrowX = cx + nx * radius, arrowY = cy + ny * radius;
        const angle = Math.atan2(ny, nx);
        const color = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');
        ctx.save(); ctx.translate(arrowX, arrowY); ctx.rotate(angle);
        ctx.beginPath(); ctx.moveTo(10,0); ctx.lineTo(-6,-6); ctx.lineTo(-6,6); ctx.closePath();
        ctx.fillStyle = color; ctx.shadowColor='rgba(0,0,0,0.6)'; ctx.shadowBlur=4; ctx.fill();
        ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke(); ctx.shadowBlur=0; ctx.restore();
        let lx = arrowX, ly = (ny>0) ? arrowY-16 : arrowY+22;
        if (lx < 50) lx = 50; if (lx > W-50) lx = W-50; if (ly < 20) ly = 20; if (ly > H-20) ly = H-20;
        ctx.font = t.active ? 'bold 10px ""Segoe UI""' : '9px ""Segoe UI""';
        ctx.fillStyle = '#fff';
        ctx.shadowColor='rgba(0,0,0,0.8)';
        ctx.shadowBlur=4;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(t.name + ' (' + formatDistance(t.dist || 0) + ')', lx, ly);
        ctx.shadowBlur = 0;
    }

    // Шлейф
    if (trailPoints.length > 1) {
        for (let i=1; i<trailPoints.length; i++) {
            const p1 = trailPoints[i-1]; const p2 = trailPoints[i];
            const s1 = worldToScreen(p1.x, p1.z);
            const s2 = worldToScreen(p2.x, p2.z);
            const speed = p2.speed || 0;
            ctx.beginPath(); ctx.moveTo(s1.x,s1.y); ctx.lineTo(s2.x,s2.y);
            ctx.strokeStyle = getTrailColor(speed); ctx.lineWidth = 2.5;
            ctx.shadowColor='rgba(0,0,0,0.5)'; ctx.shadowBlur=4; ctx.stroke(); ctx.shadowBlur=0;
        }
    }

    // События
    for (const e of events) {
        const p = worldToScreen(e.x, e.z); if (p.x<0||p.x>W||p.y<0||p.y>H) continue;
        const size=10;
        ctx.save(); ctx.translate(p.x,p.y); ctx.shadowColor='rgba(0,0,0,0.5)'; ctx.shadowBlur=6;
        ctx.beginPath(); ctx.arc(0,0,size,0,2*Math.PI);
        ctx.fillStyle=e.color||'#ffffff'; ctx.fill(); ctx.shadowBlur=0;
        ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke();
        ctx.fillStyle='#fff'; ctx.font='bold 10px ""Segoe UI""';
        ctx.textAlign='center'; ctx.textBaseline='middle';
        ctx.fillText(e.label||'?',0,-1);
        if (e.subtext) {
            ctx.fillStyle='#fff'; ctx.shadowColor='rgba(0,0,0,0.8)'; ctx.shadowBlur=3;
            ctx.font='7px ""Segoe UI""'; ctx.textBaseline='top';
            ctx.fillText(e.subtext,0,size+2); ctx.shadowBlur=0;
        }
        ctx.restore();
    }

    // Текущая позиция (грузовик)
    if (trailPoints.length > 0) {
        let idx = currentIndex;
        let nextIdx = Math.min(idx + 1, trailPoints.length - 1);
        let p1 = trailPoints[idx];
        let p2 = trailPoints[nextIdx];
        let t1 = p1.t;
        let t2 = p2.t;
        let currentPos = { x: p1.x, z: p1.z, heading: p1.heading || 0, speed: p1.speed || 0 };
        if (interpolate && idx < trailPoints.length - 1 && t2 > t1) {
            const progress = (currentTime - t1) / (t2 - t1);
            const clampedProgress = Math.max(0, Math.min(1, progress));
            currentPos.x = p1.x + (p2.x - p1.x) * clampedProgress;
            currentPos.z = p1.z + (p2.z - p1.z) * clampedProgress;
            let h1 = p1.heading || 0;
            let h2 = p2.heading || 0;
            let diff = h2 - h1;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            currentPos.heading = h1 + diff * clampedProgress;
            currentPos.speed = p1.speed + (p2.speed - p1.speed) * clampedProgress;
        }
        const sp = worldToScreen(currentPos.x, currentPos.z);
        // Рисуем грузовик (красный треугольник)
        const heading = currentPos.heading || 0;
        ctx.save(); ctx.translate(sp.x, sp.y); ctx.rotate(heading + Math.PI);
        ctx.beginPath(); ctx.moveTo(0, -14); ctx.lineTo(-9, 9); ctx.lineTo(0, 3); ctx.lineTo(9, 9); ctx.closePath();
        ctx.fillStyle = '#ff4d4d'; ctx.shadowColor = '#ff4d4d88'; ctx.shadowBlur = 12; ctx.fill();
        ctx.shadowBlur = 0; ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.stroke();
        ctx.restore();
        // Скорость под грузовиком
        ctx.save(); ctx.translate(sp.x, sp.y+22);
        ctx.font = '10px ""Segoe UI""';
        ctx.fillStyle = '#fff';
        ctx.shadowColor = 'rgba(0,0,0,0.8)';
        ctx.shadowBlur = 4;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.fillText(currentPos.speed.toFixed(0) + ' km/h', 0, 0);
        ctx.restore();
        // Обновление центра при слежении
        if (follow) { targetCenterX = currentPos.x; targetCenterZ = currentPos.z; }
        const camSmooth = 0.15;
        centerX += (targetCenterX - centerX) * camSmooth;
        centerZ += (targetCenterZ - centerZ) * camSmooth;
    }

    // Данные (топливо, повреждения, pitch/roll)
    closest = null;
    if (dataPoints.length > 0 && currentIndex < trailPoints.length) {
        let minDist = Infinity;
        const curP = trailPoints[currentIndex];
        for (const dp of dataPoints) {
            const d = Math.hypot(dp.x - curP.x, dp.z - curP.z);
            if (d < minDist) { minDist = d; closest = dp; }
        }
        if (closest) {
            document.getElementById('dataPanel').innerHTML = '⛽ ' + (closest.fuel.toFixed(1) || '--') + ' л &nbsp;|&nbsp; 🛠️ ' + (closest.damage.toFixed(1) || '--') + '%';
            const pitchRaw = closest.pitch || 0;
            const rollRaw = closest.roll || 0;
            let pitchDeg = (pitchRaw * RAW_TO_DEGREES) * PITCH_CALIBRATION_FACTOR;
            let rollDeg = (rollRaw * RAW_TO_DEGREES) * ROLL_CALIBRATION_FACTOR;
            pitchDeg = normalizeAngle(pitchDeg);
            rollDeg = normalizeAngle(rollDeg);
            // Обновляем полоски
            document.getElementById('pitchFill').style.width = Math.min(100, Math.max(0, 50 + pitchDeg * 0.5)) + '%';
            document.getElementById('rollFill').style.width = Math.min(100, Math.max(0, 50 + rollDeg * 0.5)) + '%';
            document.getElementById('pitchLabel').textContent = pitchDeg.toFixed(0);
            document.getElementById('rollLabel').textContent = rollDeg.toFixed(0);
            updatePitchRollPanel(pitchDeg, rollDeg);
        }
    }
}

// ================================================================
// ПАНЕЛЬ PITCH/ROLL
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
            if (closest) {
                const pitchRaw = closest.pitch || 0;
                const rollRaw = closest.roll || 0;
                let pitchDeg = (pitchRaw * RAW_TO_DEGREES) * PITCH_CALIBRATION_FACTOR;
                let rollDeg = (rollRaw * RAW_TO_DEGREES) * ROLL_CALIBRATION_FACTOR;
                pitchDeg = normalizeAngle(pitchDeg);
                rollDeg = normalizeAngle(rollDeg);
                updatePitchRollPanel(pitchDeg, rollDeg);
            }
        };
    }
}

// ================================================================
// УПРАВЛЕНИЕ КАРТОЙ
// ================================================================
canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    scale *= delta; if (scale < 0.001) scale = 0.001; if (scale > 1000) scale = 1000; drawMap();
}, { passive: false });

canvas.addEventListener('mousedown', (e) => {
    if (e.button === 0) {
        isDragging = true;
        dragStartX = e.clientX; dragStartY = e.clientY;
        dragStartCX = centerX; dragStartCZ = centerZ;
        canvas.style.cursor = 'grabbing';
    }
});
window.addEventListener('mousemove', (e) => {
    if (isDragging) {
        const dx = (e.clientX - dragStartX) / scale;
        const dy = (dragStartY - e.clientY) / scale;
        centerX = dragStartCX - dx;
        centerZ = dragStartCZ - dy;
        targetCenterX = centerX;
        targetCenterZ = centerZ;
        drawMap();
    }
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx >= 0 && mx <= W && my >= 0 && my <= H) {
        const wx = centerX + (mx - W/2) / scale;
        const wz = centerZ - (my - H/2) / scale;
        document.getElementById('cursorCoords').textContent = '📍 ' + wx.toFixed(1) + ', ' + wz.toFixed(1);
    }
});
window.addEventListener('mouseup', () => { if (isDragging) { isDragging = false; canvas.style.cursor = 'grab'; } });

let notes = [];
canvas.addEventListener('click', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx < 0 || mx > W || my < 0 || my > H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    const coordStr = wx.toFixed(2) + ', ' + wz.toFixed(2);
    let objName = '';
    for (const c of cities) { const dx = c.x - wx; const dz = c.z - wz; if (dx*dx + dz*dz < 100) { objName = c.name; break; } }
    if (!objName) { for (const t of customTargets) { const dx = t.x - wx; const dz = t.z - wz; if (dx*dx + dz*dz < 100) { objName = t.name; break; } } }
    if (!objName) { for (const e of events) { const dx = e.x - wx; const dz = e.z - wz; if (dx*dx + dz*dz < 100) { objName = e.label || ''; break; } } }
    const note = objName ? coordStr + ' – ' + objName : coordStr;
    notes.push(note);
    if (navigator.clipboard) navigator.clipboard.writeText(coordStr);
    const toast = document.createElement('div');
    toast.style.cssText = 'position:fixed;bottom:170px;left:50%;transform:translateX(-50%);background:rgba(0,0,0,0.8);color:#fff;padding:4px 12px;border-radius:4px;font-size:12px;z-index:999;pointer-events:none;';
    toast.textContent = 'Скопировано: ' + coordStr;
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 2000);
});

let measureMode = false; let measureStart = null;
canvas.addEventListener('dblclick', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    if (mx < 0 || mx > W || my < 0 || my > H) return;
    const wx = centerX + (mx - W/2) / scale;
    const wz = centerZ - (my - H/2) / scale;
    if (!measureMode) { measureMode = true; measureStart = { x: wx, z: wz }; document.getElementById('measureTool').classList.add('active'); document.getElementById('measureDist').textContent = '0.0'; return; }
    const dx = wx - measureStart.x; const dz = wz - measureStart.z;
    const dist = Math.sqrt(dx*dx + dz*dz);
    document.getElementById('measureDist').textContent = dist.toFixed(1);
    measureMode = false; measureStart = null;
    setTimeout(() => document.getElementById('measureTool').classList.remove('active'), 5000);
});

// ================================================================
// ТАЙМЛАЙН И ВОСПРОИЗВЕДЕНИЕ
// ================================================================
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

function formatTime(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    const ms = Math.floor((seconds - Math.floor(seconds)) * 1000);
    return String(h).padStart(2,'0') + ':' + String(m).padStart(2,'0') + ':' + String(s).padStart(2,'0') + '.' + String(ms).padStart(3,'0');
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

// ================================================================
// ИНИЦИАЛИЗАЦИЯ
// ================================================================
resize(); fitMap();
if (trailPoints.length > 0) { currentTime = 0; currentIndex = 0; targetCenterX = trailPoints[0].x; targetCenterZ = trailPoints[0].z; centerX = targetCenterX; centerZ = targetCenterZ; }
drawMap(); updateTimeDisplay();
window.addEventListener('resize', () => { resize(); fitMap(); drawMap(); });
");

            sb.AppendLine("</script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        // ================================================================
        // МЕТОД ГЕНЕРАЦИИ СТРАНИЦЫ СПИСКА ТРЕКОВ (trail_player.html)
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