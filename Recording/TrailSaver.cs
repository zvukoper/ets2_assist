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
            sb.AppendLine("    <link rel=\"icon\" href=\"/ets2_assist.ico\"/>");
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
            sb.AppendLine("<div id=\"debugLog\" style=\"position:absolute;left:10px;bottom:155px;z-index:50;background:rgba(0,0,0,.72);color:#b9c7d6;padding:5px 8px;border-radius:5px;font:11px/1.35 monospace;pointer-events:none;max-width:460px;\">Playback diagnostics</div>");
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
                roll: parseFloat(parts[8] || 0),
                lights: parts[9] || '{}',
                gameTime: parseFloat(parts[10] || 0),
                localScale: parseFloat(parts[11] || 1),
                steering: parseFloat(parts[12] || 0),
                throttle: parseFloat(parts[13] || 0),
                brake: parseFloat(parts[14] || 0),
                odometer: parseFloat(parts[15] || 0),
                headOffset: (parts[16] || '0,0,0,0,0,0').split(',').map(Number)
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
const cities = Array.isArray(mapData?.cities) ? mapData.cities : [];
const roads = Array.isArray(mapData?.roads) ? mapData.roads : [];
const pois = Array.isArray(mapData?.pois) ? mapData.pois : [];
const poiCategories = Array.isArray(mapData?.poiCategories) ? mapData.poiCategories : [];
const poiCategoryCounts = mapData?.poiCategoryCounts || {};
const customTargets = Array.isArray(mapData?.customTargets) ? mapData.customTargets : [];
const staticMapReady = roads.length > 0 || cities.length > 0 || pois.length > 0;
console.log('[PLAYBACK] Embedded static snapshot only:', { cities: cities.length, roads: roads.length, pois: pois.length, categories: poiCategories.length, targets: customTargets.length });
if (!staticMapReady) console.warn('[PLAYBACK] Static snapshot missing or empty. This track predates static-snapshot recording.');

const POI_COLORS = {
    Company:'#5ec8ff', Ferry:'#ff9f43', Garage:'#a678ff', Fuel:'#35d07f', BusStop:'#ffd166',
    Overlay:'#ff6b6b', Parking:'#8be9fd', Recruitment:'#f1fa8c', Service:'#ff79c6', Train:'#bd93f9',
    TruckDealer:'#50fa7b', WeightStation:'#ffb86c', default:'#c8ddee'
};

// Playback diagnostics + defensive timeline normalization.
const rawTimes = trailPoints.map(p => Number(p.t));
const invalidTimeCount = rawTimes.filter(t => !Number.isFinite(t)).length;
const nonMonotonicCount = rawTimes.slice(1).reduce((n, t, i) => n + (Number.isFinite(t) && Number.isFinite(rawTimes[i]) && t < rawTimes[i] ? 1 : 0), 0);
const metaDurationSec = Number(meta.durationMs) > 0 ? Number(meta.durationMs) / 1000 : 0;
const rawLastTime = rawTimes.length ? rawTimes[rawTimes.length - 1] : 0;
const timeUnit = (metaDurationSec > 0 && rawLastTime > metaDurationSec * 20) ? 'milliseconds' : 'seconds';
const normalizedRawTimes = timeUnit === 'milliseconds' ? rawTimes.map(t => t / 1000) : rawTimes;
const normalizedLastTime = normalizedRawTimes.length ? normalizedRawTimes[normalizedRawTimes.length - 1] : 0;
const rawTimelineUsable = normalizedRawTimes.length > 1 && invalidTimeCount === 0 && nonMonotonicCount === 0 && Number.isFinite(normalizedLastTime) && normalizedLastTime > 0;
const timelineMode = rawTimelineUsable ? `recorded-${timeUnit}` : (metaDurationSec > 0 && trailPoints.length > 1 ? 'meta-fallback' : 'index-fallback');
const totalDuration = rawTimelineUsable ? normalizedLastTime : (metaDurationSec > 0 ? metaDurationSec : Math.max(0, trailPoints.length - 1) / 10);
const times = trailPoints.map((p, i) => {
    if (rawTimelineUsable) return normalizedRawTimes[i];
    if (metaDurationSec > 0 && trailPoints.length > 1) return (i / (trailPoints.length - 1)) * totalDuration;
    return i / 10;
});

            console.log('[ETS2 ASSIST BUILD] 1.0.34-OVERLAY-TELEMETRY-TRACKS-2026.08.26-2215-RND3');
console.groupCollapsed('[PLAYBACK] Timeline diagnostics');
console.log('trailPoints:', trailPoints.length);
console.log('dataPoints:', dataPoints.length);
console.log('events:', events.length);
console.log('meta.durationMs:', Number(meta.durationMs) || 0);
console.log('metaDurationSec:', metaDurationSec.toFixed(3));
console.log('firstTrailTime:', rawTimes.length ? rawTimes[0] : null);
console.log('lastTrailTime:', rawLastTime);
console.log('timeUnit:', timeUnit);
console.log('normalizedLastTime:', normalizedLastTime);
console.log('totalDurationSec:', totalDuration.toFixed(3));
console.log('invalidTimeCount:', invalidTimeCount);
console.log('nonMonotonicCount:', nonMonotonicCount);
console.log('timelineMode:', timelineMode);
console.log('firstPoint:', trailPoints[0] || null);
console.log('lastPoint:', trailPoints[trailPoints.length - 1] || null);
console.groupEnd();

const title = meta.title || '';
const desc = meta.description || '';
// ETS2 Assist trail viewer title HTML: JS string uses single quotes; HTML attributes use double quotes.
document.getElementById('titlePanel').innerHTML = title + (desc ? '<br><span style=""font-size:10px; color:#888;"">' + desc + '</span>' : '');

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

function dataPointForTime(t) {
    if (!dataPoints.length) return null;
    let lo = 0, hi = dataPoints.length - 1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        if (Number(dataPoints[mid].t) <= t) lo = mid + 1; else hi = mid - 1;
    }
    return dataPoints[Math.max(0, Math.min(dataPoints.length - 1, hi))];
}

function interpolateValue(a, b, factor) {
    return Number(a || 0) + (Number(b || 0) - Number(a || 0)) * factor;
}

function trailPointForTime(t) {
    const current = trailPoints[currentIndex] || trailPoints[0];
    if (!current || !interpolate || currentIndex >= trailPoints.length - 1) return current;
    const next = trailPoints[currentIndex + 1];
    const start = Number(times[currentIndex] || current.t || 0);
    const end = Number(times[currentIndex + 1] || next.t || start);
    const factor = end > start ? Math.max(0, Math.min(1, (t - start) / (end - start))) : 0;
    let deltaHeading = Number(next.heading || 0) - Number(current.heading || 0);
    while (deltaHeading > Math.PI) deltaHeading -= Math.PI * 2;
    while (deltaHeading < -Math.PI) deltaHeading += Math.PI * 2;
    return {
        x: interpolateValue(current.x, next.x, factor),
        z: interpolateValue(current.z, next.z, factor),
        heading: Number(current.heading || 0) + deltaHeading * factor,
        speed: interpolateValue(current.speed, next.speed, factor)
    };
}

function interpolatedDataPoint(t) {
    const current = dataPointForTime(t);
    if (!current || !interpolate || dataPoints.length < 2) return current || {};
    const index = dataPoints.indexOf(current);
    if (index < 0 || index >= dataPoints.length - 1) return current;
    const next = dataPoints[index + 1];
    const start = Number(current.t || 0), end = Number(next.t || start);
    const factor = end > start ? Math.max(0, Math.min(1, (t - start) / (end - start))) : 0;
    const result = { ...current };
    for (const key of ['fuel','damage','pitch','roll','gameTime','localScale','steering','throttle','brake','odometer'])
        result[key] = interpolateValue(current[key], next[key], factor);
    result.headOffset = (Array.isArray(current.headOffset) ? current.headOffset : [0,0,0,0,0,0]).map((v, i) => interpolateValue(v, (next.headOffset || [])[i], factor));
    return result;
}

function drawMap() {
    if (!ctx || !W || !H) return;
    ctx.setTransform(1,0,0,1,0,0);
    ctx.clearRect(0,0,W,H);
    ctx.fillStyle='#0b0e13'; ctx.fillRect(0,0,W,H);
    const p = trailPointForTime(currentTime);
    if (!p) return;
    if (follow) { centerX = p.x; centerZ = p.z; }

    // Static snapshot: no HTTP/file fetch. It comes exclusively from mapData embedded into the HTML.
    ctx.strokeStyle='#4d6878'; ctx.globalAlpha=.75;
    for (const r of roads) {
        const a=worldToScreen(r.x1,r.z1), b=worldToScreen(r.x2,r.z2);
        if ((a.x<-50&&b.x<-50)||(a.x>W+50&&b.x>W+50)||(a.y<-50&&b.y<-50)||(a.y>H+50&&b.y>H+50)) continue;
        ctx.beginPath(); ctx.moveTo(a.x,a.y); ctx.lineTo(b.x,b.y); ctx.lineWidth=1.1; ctx.stroke();
    }
    ctx.globalAlpha=1;
    for (const c of cities) {
        const q=worldToScreen(c.x,c.z); if(q.x<-40||q.x>W+40||q.y<-30||q.y>H+30) continue;
        ctx.fillStyle='#e7d38a'; ctx.beginPath(); ctx.arc(q.x,q.y,3,0,Math.PI*2); ctx.fill();
        ctx.font='10px ""Segoe UI""'; ctx.fillStyle='#c8ddee'; ctx.textAlign='center'; ctx.textBaseline='bottom'; ctx.fillText(`${c.name || c.gameName || '?'}`,q.x,q.y-6);
    }
    for (const poi of pois) {
        const q=worldToScreen(poi.x,poi.z); if(q.x<-30||q.x>W+30||q.y<-25||q.y>H+25) continue;
        const color=POI_COLORS[poi.type]||POI_COLORS.default;
        ctx.fillStyle=color; ctx.beginPath(); ctx.arc(q.x,q.y,3,0,Math.PI*2); ctx.fill();
        ctx.font='9px ""Segoe UI""'; ctx.fillStyle='#dbe7f3'; ctx.fillText(poi.type || 'POI', q.x, q.y-5);
    }
    for (const target of customTargets) {
        if (!target || target.x === undefined || target.z === undefined) continue;
        const q=worldToScreen(Number(target.x), Number(target.z));
        if(q.x<-40||q.x>W+40||q.y<-40||q.y>H+40) continue;
        const color=target.color && target.color !== 'default' ? target.color : '#ffc857';
        ctx.fillStyle=color; ctx.strokeStyle='#000'; ctx.lineWidth=1.5;
        ctx.beginPath(); ctx.moveTo(q.x,q.y-8); ctx.lineTo(q.x+7,q.y+6); ctx.lineTo(q.x-7,q.y+6); ctx.closePath(); ctx.fill(); ctx.stroke();
        ctx.font='10px ""Segoe UI""'; ctx.textAlign='center'; ctx.textBaseline='bottom'; ctx.fillStyle=color;
        ctx.fillText(target.name || 'Цель', q.x, q.y-10);
    }

    // Trail up to current frame, coloured by recorded speed.
    ctx.lineWidth=4;
    for(let i=1;i<=currentIndex&&i<trailPoints.length;i++){
        const a=trailPoints[i-1], b=trailPoints[i]; const pa=worldToScreen(a.x,a.z), pb=worldToScreen(b.x,b.z);
        ctx.strokeStyle=getTrailColor(Number(b.speed)||0); ctx.beginPath(); ctx.moveTo(pa.x,pa.y); ctx.lineTo(pb.x,pb.y); ctx.stroke();
    }

    const t=p; const tp=worldToScreen(t.x,t.z);
    const dp=interpolatedDataPoint(currentTime) || {};
    const headOffset=Array.isArray(dp.headOffset)?dp.headOffset:[0,0,0,0,0,0];
    const rawHead=((Number(headOffset[3])||0)%1+1)%1;
    const headDeg=rawHead*360;
    const signedHead=headDeg<=180?headDeg:headDeg-360;

    // Head view cone relative to truck direction.
    ctx.save(); ctx.translate(tp.x,tp.y); ctx.rotate(-((Number(t.heading || 0) * 180 / Math.PI) + signedHead)*Math.PI/180);
    ctx.beginPath(); ctx.moveTo(0,-20); ctx.lineTo(-13,13); ctx.lineTo(13,13); ctx.closePath();
    ctx.fillStyle='rgba(245,248,255,.20)'; ctx.strokeStyle='rgba(255,255,255,.55)'; ctx.lineWidth=1; ctx.fill(); ctx.stroke(); ctx.restore();

    // Mirror the truck marker horizontally.
    ctx.save(); ctx.translate(tp.x,tp.y); ctx.rotate(-(Number(t.heading)||0)); ctx.scale(-1,1);
    ctx.beginPath(); ctx.moveTo(0,-18); ctx.lineTo(-10,12); ctx.lineTo(0,6); ctx.lineTo(10,12); ctx.closePath();
    ctx.fillStyle='#ff4d4d'; ctx.fill(); ctx.strokeStyle='#fff'; ctx.lineWidth=1.5; ctx.stroke(); ctx.restore();

    const currentSpeed=Number(t.speed)||0;
    const pitchDeg=Number(dp.pitch||0)*360*PITCH_CALIBRATION_FACTOR;
    const rollDeg=Number(dp.roll||0)*360*ROLL_CALIBRATION_FACTOR;
    const body=document.getElementById('truck3dBody');
    if(body) body.style.transform=`scaleX(-1) rotateX(${pitchDeg.toFixed(2)}deg) rotateZ(${rollDeg.toFixed(2)}deg)`;
    const pitchFill=document.getElementById('pitchFill'), rollFill=document.getElementById('rollFill');
    if(pitchFill) pitchFill.style.width=Math.max(0,Math.min(100,50+pitchDeg/1.8))+'%';
    if(rollFill) rollFill.style.width=Math.max(0,Math.min(100,50+rollDeg/3.6))+'%';
    const pitchLabel=document.getElementById('pitchLabel'), rollLabel=document.getElementById('rollLabel');
    if(pitchLabel) pitchLabel.textContent=pitchDeg.toFixed(1)+'°';
    if(rollLabel) rollLabel.textContent=rollDeg.toFixed(1)+'°';
    const fuel=Number(dp.fuel)||0;
    const localScale=Number(dp.localScale)||1;
    const steer=(Number(dp.steering)||0)*100;
    const throttle=(Number(dp.throttle)||0)*100;
    const brake=(Number(dp.brake)||0)*100;
    const panel=document.getElementById('playbackTelemetry');
    if(panel) panel.innerHTML=`Speed: ${currentSpeed.toFixed(0)} km/h | Fuel: ${fuel.toFixed(0)}%<br>Head: ${signedHead.toFixed(1)}° | Scale: ${localScale.toFixed(2)}×<br>Steer: ${steer.toFixed(0)}% | Throttle: ${throttle.toFixed(0)}% | Brake: ${brake.toFixed(0)}%<br>POI: ${pois.length} / ${poiCategories.length} cat. | Cities: ${cities.length} | Roads: ${roads.length} | Targets: ${customTargets.length}`;

    const debug=document.getElementById('debugLog'); if(debug) debug.textContent=playbackStatusText(`roads ${roads.length} | cities ${cities.length} | poi ${pois.length}`);
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
let playbackFrameCount = 0;
let playbackLastIndex = -1;
let playbackLastTime = -1;
let playbackWatchdog = null;

function playbackStatusText(extra='') {
    return `Frames ${trailPoints.length} | Duration ${totalDuration.toFixed(1)}s | Time ${currentTime.toFixed(1)}s | Index ${currentIndex}/${Math.max(0, trailPoints.length-1)} | ${playing ? 'PLAYING' : 'STOPPED'}${extra ? ' | ' + extra : ''}`;
}
function updatePlaybackDebug(extra='') {
    const logDiv = document.getElementById('debugLog');
    const text = playbackStatusText(extra);
    if (logDiv) logDiv.textContent = text;
    return text;
}

function formatTime(seconds) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    const ms = Math.floor((seconds - Math.floor(seconds)) * 1000);
    return String(h).padStart(2,'0') + ':' + String(m).padStart(2,'0') + ':' + String(s).padStart(2,'0') + '.' + String(ms).padStart(3,'0');
}

function indexForTime(t) {
    if (!times.length) return 0;
    let lo = 0, hi = times.length - 1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        const v = times[mid];
        if (v <= t) lo = mid + 1;
        else hi = mid - 1;
    }
    return Math.max(0, Math.min(hi, times.length - 1));
}

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
    const percent = totalDuration > 0 ? Math.max(0, Math.min(1, currentTime / totalDuration)) : 0;
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

let playbackDebugFrames = 0;
let playbackLastLoggedIndex = -1;

function playStep() {
    if (!playing) return;
    try {
        const now = performance.now() / 1000;
        const elapsed = (now - playStartTime) * speedFactor + playStartElapsed;
        currentTime = Math.min(elapsed, totalDuration);
        let idx = 0;
        for (let i=0; i<times.length; i++) { if (times[i] <= currentTime) idx = i; else break; }
        currentIndex = Math.min(idx, trailPoints.length - 1);
        if (follow && currentIndex >= 0 && currentIndex < trailPoints.length) {
            targetCenterX = trailPoints[currentIndex].x;
            targetCenterZ = trailPoints[currentIndex].z;
        }
        playbackDebugFrames++;
        if (playbackDebugFrames === 1 || currentIndex !== playbackLastLoggedIndex && (currentIndex % 100 === 0 || currentIndex === trailPoints.length - 1)) {
            console.log('[PLAYBACK] tick', { frame: playbackDebugFrames, currentTime: Number(currentTime.toFixed(3)), currentIndex, totalFrames: trailPoints.length, totalDuration: Number(totalDuration.toFixed(3)), speedFactor });
            playbackLastLoggedIndex = currentIndex;
        }
        updateTimeDisplay();
        if (currentTime >= totalDuration) {
            playing = false;
            playBtn.textContent = '▶';
            console.log('[PLAYBACK] finished', { frames: playbackDebugFrames, finalTime: Number(currentTime.toFixed(3)), finalIndex: currentIndex });
            return;
        }
        requestAnimationFrame(playStep);
    } catch (error) {
        playing = false;
        playBtn.textContent = '▶';
        console.error('[PLAYBACK] playStep exception:', error);
    }
}

playBtn.addEventListener('click', () => {
    const wasPlaying = playing;
    playing = !playing;
    playBtn.textContent = playing ? '⏸' : '▶';
    console.log('[PLAYBACK] play button', { wasPlaying, playing, currentTime: Number(currentTime.toFixed(3)), totalDuration: Number(totalDuration.toFixed(3)), currentIndex, totalFrames: trailPoints.length, speedFactor, timelineMode });
    if (playing) {
        if (trailPoints.length < 2) {
            playing = false;
            playBtn.textContent = '▶';
            console.warn('[PLAYBACK] Start blocked: less than 2 trail points.');
            return;
        }
        if (!(totalDuration > 0) || !Number.isFinite(totalDuration)) {
            playing = false;
            playBtn.textContent = '▶';
            console.error('[PLAYBACK] Start blocked: invalid totalDuration.', totalDuration);
            return;
        }
        if (currentTime >= totalDuration) {
            currentTime = 0;
            currentIndex = 0;
            if (follow) { targetCenterX = trailPoints[0].x; targetCenterZ = trailPoints[0].z; }
            console.log('[PLAYBACK] Restarting from beginning.');
        }
        playbackDebugFrames = 0;
        playbackLastLoggedIndex = -1;
        playStartTime = performance.now() / 1000;
        playStartElapsed = currentTime;
        console.log('[PLAYBACK] started', { startTime: Number(playStartTime.toFixed(3)), startElapsed: Number(playStartElapsed.toFixed(3)) });
        playStep();
    } else {
        console.log('[PLAYBACK] paused manually', { currentTime: Number(currentTime.toFixed(3)), currentIndex, playbackDebugFrames });
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
        private string GenerateTrailPlayerHtml(IEnumerable<string> trackNames)
        {
            var names = trackNames.Where(n => !string.IsNullOrWhiteSpace(n)).OrderByDescending(n => n).ToList();
            var items = string.Join("\n", names.Select(name => $"<li><a href='./{Uri.EscapeDataString(name)}.html' target='_blank'>{System.Net.WebUtility.HtmlEncode(name)}</a></li>"));
            return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/><link rel='icon' href='/ets2_assist.ico'/><title>ETS2 Trail Player</title>
<style>body{{background:#0a0c10;color:#e0e0e0;font-family:'Segoe UI',sans-serif;margin:20px}}a{{color:#d8e6f5;text-decoration:none}}a:hover{{text-decoration:underline}}#trackList{{max-width:800px;margin:20px auto}}li{{background:#1a1f26;padding:8px 12px;margin:6px 0;border:1px solid #333;border-radius:6px;list-style:none}}</style>
</head><body><h1 style='text-align:center;'>ETS2 Trail Player</h1><ul id='trackList'>{items}</ul></body></html>";
        }
    }
}