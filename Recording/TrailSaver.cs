using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using ETS2_Assist_GUI.Models;
using ETS2_Assist_GUI.Storage;
using ETS2_Assist_GUI.Helpers;

namespace ETS2_Assist_GUI.Recording
{
    /// <summary>
    /// Сохраняет трек в файлы (JSON + HTML плеер).
    /// Поддерживает форматы: 1 файл (всё в HTML), 2 файла (HTML + JSON трек), 3 файла (HTML + JSON трек + JSON карта).
    /// </summary>
    public class TrailSaver
    {
        private readonly Logger _logger;
        private readonly DataManager _dataManager;
        private readonly SettingsManager _settingsManager;
        private readonly string _savedTracksPath;

        public TrailSaver(Logger logger, DataManager dataManager, SettingsManager settingsManager)
        {
            _logger = logger;
            _dataManager = dataManager;
            _settingsManager = settingsManager;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _savedTracksPath = Path.Combine(baseDir, "data", "saved_tracks");

            if (!Directory.Exists(_savedTracksPath))
                Directory.CreateDirectory(_savedTracksPath);
        }

        /// <summary>
        /// Сохраняет трек из JSON-строки, полученной от веб-карты.
        /// </summary>
        public string SaveFromJson(string jsonData)
        {
            try
            {
                var trailData = JsonConvert.DeserializeObject<TrailData>(jsonData);
                if (trailData == null)
                    throw new InvalidOperationException("Не удалось десериализовать данные трека.");

                return Save(trailData);
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения трека: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Сохраняет трек в файлы в зависимости от выбранного формата.
        /// </summary>
        public string Save(TrailData trailData)
        {
            if (trailData == null || trailData.Meta == null || trailData.Data == null)
                throw new ArgumentException("Данные трека некорректны.");

            // Генерируем уникальное имя файла
            var timestamp = DateTime.Now.ToString("yyMMdd_HHmm");
            var coords = trailData.Data.Count > 0 ? trailData.Data[0].Split(';') : new[] { "0", "0" };
            var coordPart = $"{double.Parse(coords[1]):F0}_{double.Parse(coords[2]):F0}";
            var baseName = $"track_{timestamp}_{coordPart}";

            // Определяем суффикс из настроек
            var suffix = _settingsManager.Get<string>("DefaultSuffix", "");
            if (!string.IsNullOrEmpty(suffix))
                baseName += $"_{suffix}";

            var settings = _settingsManager.Get<Settings>();
            var format = settings?.SaveFormat ?? SaveFormat.ThreeFiles;

            switch (format)
            {
                case SaveFormat.OneFile:
                    return SaveOneFile(trailData, baseName);
                case SaveFormat.TwoFiles:
                    return SaveTwoFiles(trailData, baseName);
                case SaveFormat.ThreeFiles:
                    return SaveThreeFiles(trailData, baseName);
                default:
                    throw new ArgumentOutOfRangeException($"Неизвестный формат: {format}");
            }
        }

        /// <summary>
        /// Сохраняет всё в один HTML-файл.
        /// </summary>
        private string SaveOneFile(TrailData trailData, string baseName)
        {
            var html = GenerateHtml(trailData, embedMapData: true);
            var path = Path.Combine(_savedTracksPath, $"{baseName}.html");
            File.WriteAllText(path, html, Encoding.UTF8);
            _logger.Log($"Трек сохранён в один файл: {path}");
            return path;
        }

        /// <summary>
        /// Сохраняет HTML-плеер и JSON-файл с треком отдельно.
        /// </summary>
        private string SaveTwoFiles(TrailData trailData, string baseName)
        {
            // Сохраняем JSON трека
            var jsonData = JsonConvert.SerializeObject(trailData, Formatting.Indented);
            var jsonPath = Path.Combine(_savedTracksPath, $"{baseName}.json");
            File.WriteAllText(jsonPath, jsonData, Encoding.UTF8);

            // Сохраняем HTML (без встроенной карты, загружает JSON через fetch)
            var html = GenerateHtml(trailData, embedMapData: false);
            var htmlPath = Path.Combine(_savedTracksPath, $"{baseName}.html");
            File.WriteAllText(htmlPath, html, Encoding.UTF8);

            _logger.Log($"Трек сохранён в два файла: {htmlPath} и {jsonPath}");
            return htmlPath;
        }

        /// <summary>
        /// Сохраняет три файла: HTML, JSON трек, JSON карта.
        /// </summary>
        private string SaveThreeFiles(TrailData trailData, string baseName)
        {
            // Сохраняем JSON трека
            var trailJson = JsonConvert.SerializeObject(new { meta = trailData.Meta, data = trailData.Data }, Formatting.Indented);
            var trailPath = Path.Combine(_savedTracksPath, $"{baseName}.json");
            File.WriteAllText(trailPath, trailJson, Encoding.UTF8);

            // Сохраняем карту, если она есть
            if (trailData.MapData != null)
            {
                var mapJson = JsonConvert.SerializeObject(trailData.MapData, Formatting.Indented);
                var mapPath = Path.Combine(_savedTracksPath, $"{baseName}_map.json");
                File.WriteAllText(mapPath, mapJson, Encoding.UTF8);
            }

            // Сохраняем HTML (загружает оба JSON через fetch)
            var html = GenerateHtml(trailData, embedMapData: false, loadMapFromFile: true);
            var htmlPath = Path.Combine(_savedTracksPath, $"{baseName}.html");
            File.WriteAllText(htmlPath, html, Encoding.UTF8);

            _logger.Log($"Трек сохранён в три файла: {htmlPath}, {trailPath} и {baseName}_map.json");
            return htmlPath;
        }

        /// <summary>
        /// Генерирует HTML-плеер.
        /// </summary>

        /// <summary>
        /// Генерирует полноценный HTML-плеер для воспроизведения трека.
        /// Содержит карту, шлейф, таймлайн, управление, интерполяцию, инструменты.
        /// </summary>
        /// <summary>
        /// Генерирует полноценный HTML-плеер для воспроизведения трека.
        /// Содержит карту, шлейф, таймлайн, управление, интерполяцию, инструменты.
        /// </summary>
        private string GenerateHtml(TrailData trailData, bool embedMapData, bool loadMapFromFile = false)
        {
            var sb = new StringBuilder();
            string baseName = Path.GetFileNameWithoutExtension(trailData.Meta?.Name) ?? "track";

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"ru\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"utf-8\"/>");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("    <title>ETS2 Trail Player</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        * { margin:0; padding:0; box-sizing:border-box; }");
            sb.AppendLine("        body { background:#0a0c10; color:#e0e0e0; font-family:'Segoe UI',sans-serif; overflow:hidden; height:100vh; }");
            sb.AppendLine("        #mapCanvas { display:block; width:100vw; height:calc(100vh - 130px); background:#0f1217; cursor:grab; }");
            sb.AppendLine("        #controls { position:fixed; bottom:0; left:0; right:0; background:#1a1f26; padding:8px 16px; border-top:1px solid #333; display:flex; align-items:center; gap:8px; flex-wrap:wrap; z-index:10; }");
            sb.AppendLine("        #controls button { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:6px; padding:4px 12px; font-size:14px; cursor:pointer; min-width:32px; }");
            sb.AppendLine("        #controls button:hover { background:#3a4a5a; }");
            sb.AppendLine("        #controls input[type=\"range\"] { flex:1; min-width:100px; }");
            sb.AppendLine("        #controls input[type=\"number\"] { width:60px; background:#0c1016; border:1px solid #2f3845; border-radius:4px; color:#e0e9f5; padding:2px 6px; }");
            sb.AppendLine("        #controls label { font-size:12px; color:#8fa0b9; display:flex; align-items:center; gap:4px; }");
            sb.AppendLine("        #info { position:absolute; top:10px; right:10px; background:rgba(0,0,0,0.7); padding:4px 12px; border-radius:6px; font-size:12px; color:#ccc; pointer-events:none; z-index:5; }");
            sb.AppendLine("        #dataPanel { position:absolute; bottom:140px; left:16px; background:rgba(0,0,0,0.7); padding:6px 14px; border-radius:6px; font-size:11px; color:#aabbcc; border:1px solid #333; backdrop-filter:blur(4px); pointer-events:none; z-index:5; }");
            sb.AppendLine("        #coordsDisplay { position:absolute; bottom:140px; right:16px; background:rgba(0,0,0,0.7); padding:4px 10px; border-radius:4px; font-size:11px; color:#aabbcc; border:1px solid #333; pointer-events:none; z-index:5; font-family:monospace; }");
            sb.AppendLine("        #notesPanel { position:absolute; top:50px; right:16px; width:250px; max-height:300px; background:rgba(0,0,0,0.8); border:1px solid #333; border-radius:6px; padding:8px; overflow-y:auto; font-size:11px; color:#c8ddee; display:none; z-index:6; }");
            sb.AppendLine("        #notesPanel.visible { display:block; }");
            sb.AppendLine("        #notesPanel .note { border-bottom:1px solid #222; padding:4px 0; }");
            sb.AppendLine("        #notesPanel .note:last-child { border-bottom:none; }");
            sb.AppendLine("        #measurePanel { position:absolute; top:50px; left:16px; background:rgba(0,0,0,0.8); border:1px solid #333; border-radius:6px; padding:8px 12px; font-size:12px; color:#c8ddee; display:none; z-index:6; }");
            sb.AppendLine("        #measurePanel.visible { display:block; }");
            sb.AppendLine("        #toolbar { position:absolute; top:10px; left:10px; display:flex; gap:6px; z-index:5; }");
            sb.AppendLine("        #toolbar button { background:#2a3545; border:1px solid #4a5a6a; color:#d0def0; border-radius:4px; padding:4px 10px; font-size:12px; cursor:pointer; }");
            sb.AppendLine("        #toolbar button:hover { background:#3a4a5a; }");
            sb.AppendLine("        #toolbar button.active { background:#3a5a7a; border-color:#5f8aff; }");
            sb.AppendLine("        .time-display { font-size:13px; color:#aabbcc; min-width:80px; }");
            sb.AppendLine("        @media (max-width:700px) { #controls { gap:4px; padding:4px 8px; } #controls button { font-size:12px; padding:2px 8px; } #dataPanel { bottom:110px; } #coordsDisplay { bottom:110px; } }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div id=\"info\">Просмотр трека</div>");
            sb.AppendLine("<div id=\"dataPanel\">⛽ -- л &nbsp;|&nbsp; 🛠️ --%</div>");
            sb.AppendLine("<div id=\"coordsDisplay\">--</div>");
            sb.AppendLine("<div id=\"notesPanel\"></div>");
            sb.AppendLine("<div id=\"measurePanel\">Расстояние: 0.0 м</div>");
            sb.AppendLine("<div id=\"toolbar\">");
            sb.AppendLine("    <button id=\"btnNotes\">📝 Заметки</button>");
            sb.AppendLine("    <button id=\"btnMeasure\">📏 Измерить</button>");
            sb.AppendLine("</div>");
            sb.AppendLine("<canvas id=\"mapCanvas\"></canvas>");
            sb.AppendLine("<div id=\"controls\">");
            sb.AppendLine("    <button id=\"gotoStart\" title=\"В начало\">⏮</button>");
            sb.AppendLine("    <button id=\"stepBack\" title=\"Шаг назад\">⏪</button>");
            sb.AppendLine("    <button id=\"playBtn\" title=\"Воспроизвести\">▶</button>");
            sb.AppendLine("    <button id=\"stepForward\" title=\"Шаг вперёд\">⏩</button>");
            sb.AppendLine("    <button id=\"gotoEnd\" title=\"В конец\">⏭</button>");
            sb.AppendLine("    <span style=\"font-size:12px;color:#888;\">Шаг:</span>");
            sb.AppendLine("    <input type=\"number\" id=\"stepSeconds\" value=\"1.0\" min=\"0.5\" max=\"600\" step=\"0.5\" style=\"width:60px;\">");
            sb.AppendLine("    <span style=\"font-size:12px;color:#888;\">с</span>");
            sb.AppendLine("    <label><input type=\"checkbox\" id=\"followCheck\" checked> Следить</label>");
            sb.AppendLine("    <input type=\"range\" id=\"timeSlider\" min=\"0\" max=\"1000\" value=\"0\" style=\"flex:1;\">");
            sb.AppendLine("    <span class=\"time-display\" id=\"timeDisplay\">0:00 / 0:00</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<script>");
            sb.AppendLine("// ================================================================");
            sb.AppendLine("// ДАННЫЕ ТРЕКА");
            sb.AppendLine("// ================================================================");

            if (embedMapData)
            {
                var fullData = new { meta = trailData.Meta, data = trailData.Data, mapData = trailData.MapData };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(fullData, Newtonsoft.Json.Formatting.None);
                sb.AppendLine($"const trailData = {json};");
                sb.AppendLine("const mapData = trailData.mapData || null;");
            }
            else if (loadMapFromFile)
            {
                sb.AppendLine($"const trailData = {{ meta: {JsonConvert.SerializeObject(trailData.Meta)}, data: {JsonConvert.SerializeObject(trailData.Data)} }};");
                sb.AppendLine($"const mapData = null; // будет загружен отдельно");
            }
            else
            {
                sb.AppendLine($"const trailData = {{ meta: {JsonConvert.SerializeObject(trailData.Meta)}, data: {JsonConvert.SerializeObject(trailData.Data)} }};");
                sb.AppendLine("const mapData = null;");
            }

            sb.AppendLine(@"
// ================================================================
// ПАРСИНГ И ИНДЕКСАЦИЯ
// ================================================================
const SEP = ';';
function parseFrame(line) {
    const parts = line.split(SEP);
    if (parts.length < 6) return null;
    return {
        time: parseInt(parts[0]),
        x: parseFloat(parts[1]),
        z: parseFloat(parts[2]),
        heading: parseFloat(parts[3]) || 0,
        speed: parseFloat(parts[4]) || 0,
        eventType: parseInt(parts[5]) || 0,
        label: parts[6] || '',
        color: parts[7] || '',
        subtext: parts[8] || '',
        fuel: parts[9] ? parseFloat(parts[9]) : null,
        damage: parts[10] ? parseFloat(parts[10]) : null
    };
}

const frames = trailData.data.map(parseFrame).filter(f => f !== null);
const eventTypes = trailData.meta.eventTypes || { 0:'none',1:'stop',2:'service',3:'parking',4:'damage',5:'user_marker' };
const totalDuration = frames.length > 0 ? frames[frames.length-1].time : 0;

const timeIndex = frames.map(f => f.time);

function findFrameIndex(timeMs) {
    if (timeMs <= 0) return 0;
    if (timeMs >= totalDuration) return frames.length - 1;
    let lo = 0, hi = frames.length - 1;
    while (lo < hi) {
        const mid = Math.floor((lo + hi + 1) / 2);
        if (timeIndex[mid] <= timeMs) lo = mid;
        else hi = mid - 1;
    }
    return lo;
}

// ================================================================
// КАНВАС И КАРТА
// ================================================================
const canvas = document.getElementById('mapCanvas');
const ctx = canvas.getContext('2d');
let W, H;
function resize() {
    W = canvas.width = window.innerWidth;
    H = canvas.height = window.innerHeight - 130;
    drawMap();
}
window.addEventListener('resize', resize);

// Загрузка карты (города, дороги, цели)
let cities = [], roads = [], targets = [];
function loadMapData() {
    if (mapData) {
        cities = mapData.cities || [];
        roads = mapData.roads || [];
        targets = mapData.customTargets || [];
        return Promise.resolve();
    }
    const mapFile = '" + baseName + @"_map.json';
    return fetch(mapFile)
        .then(r => { if (!r.ok) throw new Error('Map not found'); return r.json(); })
        .then(data => {
            cities = data.cities || [];
            roads = data.roads || [];
            targets = data.customTargets || [];
        })
        .catch(() => {
            cities = [];
            roads = [];
            targets = [];
        });
}

let centerX = 0, centerZ = 0, scale = 1;
function fitMap() {
    if (frames.length < 2) return;
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const f of frames) {
        if (f.x < minX) minX = f.x;
        if (f.x > maxX) maxX = f.x;
        if (f.z < minZ) minZ = f.z;
        if (f.z > maxZ) maxZ = f.z;
    }
    centerX = (minX + maxX) / 2;
    centerZ = (minZ + maxZ) / 2;
    const range = Math.max(maxX - minX, maxZ - minZ, 1);
    scale = (Math.min(W, H) * 0.85) / (range * 1.15);
}

function worldToScreen(wx, wz) {
    const dx = (wx - centerX) * scale;
    const dz = (wz - centerZ) * scale;
    return { x: W/2 + dx, y: H/2 - dz };
}

// ================================================================
// ЦВЕТ ШЛЕЙФА
// ================================================================
function getTrailColor(speed) {
    const s = Math.max(0, Math.min(125, speed));
    let r,g,b;
    if (s <= 10) { const t=s/10; r=0; g=t*255; b=255; }
    else if (s <= 25) { const t=(s-10)/15; r=0; g=255; b=255-t*255; }
    else if (s <= 50) { const t=(s-25)/25; r=t*255; g=255; b=0; }
    else if (s <= 75) { const t=(s-50)/25; r=255; g=255-(255-165)*t; b=0; }
    else if (s <= 100) { const t=(s-75)/25; r=255-(255-128)*t; g=165-165*t; b=t*255; }
    else { const t=(s-100)/25; r=128+(255-128)*t; g=0; b=255-255*t; }
    return `rgb(${Math.round(r)},${Math.round(g)},${Math.round(b)})`;
}

// ================================================================
// ОТРИСОВКА КАРТЫ
// ================================================================
let currentIndex = 0;
let follow = true;
let isDragging = false;
let dragStartX = 0, dragStartY = 0, dragStartCX = 0, dragStartCZ = 0;

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
        const p = worldToScreen(centerX + x, centerZ);
        ctx.beginPath();
        ctx.moveTo(p.x, 0);
        ctx.lineTo(p.x, H);
        ctx.stroke();
    }
    for (let z = -H/2; z < H/2; z += gridStep) {
        const p = worldToScreen(centerX, centerZ + z);
        ctx.beginPath();
        ctx.moveTo(0, p.y);
        ctx.lineTo(W, p.y);
        ctx.stroke();
    }
    ctx.setLineDash([]);

    // Дороги
    for (const r of roads) {
        const p1 = worldToScreen(r.x1, r.z1);
        const p2 = worldToScreen(r.x2, r.z2);
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.strokeStyle = '#5a7a8a';
        ctx.lineWidth = 1.5;
        ctx.globalAlpha = 0.6;
        ctx.stroke();
    }
    ctx.globalAlpha = 1;

    // Города (видимые) - ИСПРАВЛЕНО ЭКРАНИРОВАНИЕ ШРИФТА
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (const c of cities) {
        const p = worldToScreen(c.x, c.z);
        if (p.x < -20 || p.x > W+20 || p.y < -20 || p.y > H+20) continue;
        ctx.beginPath();
        ctx.arc(p.x, p.y, 4, 0, 2*Math.PI);
        ctx.fillStyle = '#ffdd88';
        ctx.shadowColor = '#ffdd8844';
        ctx.shadowBlur = 6;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.font = '10px \"Segoe UI\"';
        
                ctx.fillStyle = '#c8ddee';
            ctx.fillText(c.name, p.x, p.y - 8);
        }

    // Цели
    for (const t of targets) {
        const p = worldToScreen(t.x, t.z);
        if (p.x< -20 || p.x> W+20 || p.y< -20 || p.y> H+20) continue;
        const color = t.active ? (t.color || '#ffc857') : (t.color || '#88aadd');
        ctx.beginPath();
        ctx.arc(p.x, p.y, t.active? 6 : 4, 0, 2*Math.PI);
        ctx.fillStyle = color;
        ctx.shadowColor = t.active? '#ffc85788' : '#88aadd88';
        ctx.shadowBlur = t.active? 16 : 8;
        ctx.fill();
        ctx.shadowBlur = 0;
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = t.active? 1.5 : 0.5;
        ctx.stroke();
        ctx.font = t.active? 'bold 10px \"Segoe UI\"' : '9px \"Segoe UI\"';
        ctx.fillStyle = '#fff';
        ctx.shadowColor = 'rgba(0,0,0,0.8)';
        ctx.shadowBlur = 4;
        ctx.fillText(t.name, p.x, p.y - (t.active? 14 : 10));
        ctx.shadowBlur = 0;
    }

    // Указатели для невидимых городов (ближайшие 4)
    if (currentIndex<frames.length) {
        const curF = frames[currentIndex];
    const cx0 = W / 2, cy0 = H / 2;
    const radius = Math.min(W, H) * 0.42;
    const nearCities = cities.map(c => ({ ...c, dist: Math.hypot(c.x - curF.x, c.z - curF.z) }))
            .sort((a, b) => a.dist - b.dist).slice(0, 4);
for (const c of nearCities) {
            const p = worldToScreen(c.x, c.z);
if (p.x >= 0 && p.x <= W && p.y >= 0 && p.y <= H) continue;
const dx = p.x - cx0, dy = p.y - cy0;
const len = Math.hypot(dx, dy);
if (len < 1) continue;
const nx = dx / len, ny = dy / len;
const arrowX = cx0 + nx * radius, arrowY = cy0 + ny * radius;
const angle = Math.atan2(ny, nx);
ctx.save();
ctx.translate(arrowX, arrowY);
ctx.rotate(angle);
ctx.beginPath();
ctx.moveTo(10, 0); ctx.lineTo(-6, -6); ctx.lineTo(-6, 6); ctx.closePath();
ctx.fillStyle = '#aabbcc';
ctx.shadowColor = 'rgba(0,0,0,0.6)';
ctx.shadowBlur = 4;
ctx.fill();
ctx.strokeStyle = '#000';
ctx.lineWidth = 1;
ctx.stroke();
ctx.shadowBlur = 0;
ctx.restore();
let lx = arrowX, ly = (ny > 0) ? arrowY - 16 : arrowY + 22;
if (lx < 50) lx = 50; if (lx > W - 50) lx = W - 50;
if (ly < 20) ly = 20; if (ly > H - 20) ly = H - 20;
ctx.font = '10px \"Segoe UI\"';
ctx.fillStyle = '#c8ddee';
ctx.shadowColor = 'rgba(0,0,0,0.8)';
ctx.shadowBlur = 4;
ctx.textAlign = 'center';
ctx.textBaseline = 'middle';
ctx.fillText(`${ c.name}
(${ c.dist < 1000 ? Math.round(c.dist) + 'м' : (Math.round(c.dist / 1000)) + 'км'})`, lx, ly);
ctx.shadowBlur = 0;
        }
    }

    // Шлейф
    if (frames.length > 1)
{
    for (let i = 1; i < frames.length; i++)
    {
        const p1 = worldToScreen(frames[i - 1].x, frames[i - 1].z);
        const p2 = worldToScreen(frames[i].x, frames[i].z);
        const speed = frames[i].speed || 0;
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.strokeStyle = getTrailColor(speed);
        ctx.lineWidth = 2.5;
        ctx.shadowColor = 'rgba(0,0,0,0.5)';
        ctx.shadowBlur = 4;
        ctx.stroke();
        ctx.shadowBlur = 0;
    }
}

// События (иконки)
for (let i = 0; i < frames.length; i++)
{
    const f = frames[i];
    if (f.eventType === 0) continue;
    const p = worldToScreen(f.x, f.z);
    if (p.x < -20 || p.x > W + 20 || p.y < -20 || p.y > H + 20) continue;
    const size = 10;
    ctx.save();
    ctx.translate(p.x, p.y);
    ctx.shadowColor = 'rgba(0,0,0,0.5)';
    ctx.shadowBlur = 6;
    ctx.beginPath();
    ctx.arc(0, 0, size, 0, 2 * Math.PI);
    ctx.fillStyle = f.color || '#ffffff';
    ctx.fill();
    ctx.shadowBlur = 0;
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.fillStyle = '#fff';
    ctx.font = 'bold 10px \"Segoe UI\"';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(f.label || '?', 0, -1);
    if (f.subtext)
    {
        ctx.fillStyle = '#fff';
        ctx.shadowColor = 'rgba(0,0,0,0.8)';
        ctx.shadowBlur = 3;
        ctx.font = '7px \"Segoe UI\"';
        ctx.textBaseline = 'top';
        ctx.fillText(f.subtext, 0, size + 2);
        ctx.shadowBlur = 0;
    }
    ctx.restore();
}

// Текущая позиция грузовика
if (currentIndex < frames.length)
{
    const f = frames[currentIndex];
    const p = worldToScreen(f.x, f.z);
    const heading = f.heading || 0;
    const speed = f.speed || 0;
    ctx.save();
    ctx.translate(p.x, p.y);
    ctx.rotate(heading + Math.PI);
    ctx.beginPath();
    ctx.moveTo(0, -14);
    ctx.lineTo(-9, 9);
    ctx.lineTo(0, 3);
    ctx.lineTo(9, 9);
    ctx.closePath();
    ctx.fillStyle = '#ff4d4d';
    ctx.shadowColor = '#ff4d4d88';
    ctx.shadowBlur = 12;
    ctx.fill();
    ctx.shadowBlur = 0;
    ctx.strokeStyle = '#fff';
    ctx.lineWidth = 1.5;
    ctx.stroke();
    ctx.restore();
    // Подпись скорости
    ctx.save();
    ctx.translate(p.x, p.y + 22);
    ctx.font = '10px \"Segoe UI\"';
    ctx.fillStyle = '#fff';
    ctx.shadowColor = 'rgba(0,0,0,0.8)';
    ctx.shadowBlur = 4;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillText(`${ speed.toFixed(0)}
    km / h`, 0, 0);
    ctx.restore();
}

// Данные (топливо, урон)
if (currentIndex < frames.length)
{
    const curF = frames[currentIndex];
    let closest = null, minDist = Infinity;
    for (const f of frames) {
        if (f.fuel === null && f.damage === null) continue;
        const d = Math.hypot(f.x - curF.x, f.z - curF.z);
        if (d < minDist) { minDist = d; closest = f; }
    }
    if (closest && closest.fuel !== null)
    {
        document.getElementById('dataPanel').innerHTML = `⛽ ${ closest.fuel.toFixed(1)}
        л & nbsp;| &nbsp; 🛠️ ${ (closest.damage || 0).toFixed(1)}%`;
    }
}

    // Координаты курсора (обновляются отдельно)
}

// ================================================================
// ИНСТРУМЕНТЫ: КООРДИНАТЫ, ЗАМЕТКИ, ИЗМЕРЕНИЕ
// ================================================================
let notes = [];
let measurePoints = [];
let isMeasuring = false;

canvas.addEventListener('mousemove', (e) => {
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const wx = (mx - W / 2) / scale + centerX;
    const wz = (H / 2 - my) / scale + centerZ;
    document.getElementById('coordsDisplay').textContent = `X: ${ wx.toFixed(1)}
Z: ${ wz.toFixed(1)}`;
});

canvas.addEventListener('click', (e) => {
const rect = canvas.getBoundingClientRect();
const mx = e.clientX - rect.left;
const my = e.clientY - rect.top;
const wx = (mx - W / 2) / scale + centerX;
const wz = (H / 2 - my) / scale + centerZ;

let clickedObject = null;
for (const c of cities) {
    const p = worldToScreen(c.x, c.z);
    if (Math.hypot(p.x - mx, p.y - my) < 10) { clickedObject = { type: 'city', name: c.name, x: c.x, z: c.z }; break; }
}
if (!clickedObject)
{
    for (const t of targets) {
        const p = worldToScreen(t.x, t.z);
        if (Math.hypot(p.x - mx, p.y - my) < 10) { clickedObject = { type: 'target', name: t.name, x: t.x, z: t.z }; break; }
    }
}

if (clickedObject)
{
    const noteText = `${ clickedObject.name}
    (${ clickedObject.x.toFixed(1)}, ${ clickedObject.z.toFixed(1)})`;
    notes.push(noteText);
    updateNotesPanel();
    navigator.clipboard?.writeText(`${ clickedObject.x.toFixed(1)}, ${ clickedObject.z.toFixed(1)}`).catch(() => { });
}
else
{
    if (isMeasuring)
    {
        measurePoints.push({ x: wx, z: wz });
        if (measurePoints.length === 2)
        {
            const dx = measurePoints[1].x - measurePoints[0].x;
            const dz = measurePoints[1].z - measurePoints[0].z;
            const dist = Math.hypot(dx, dz);
            document.getElementById('measurePanel').textContent = `Расстояние: ${ dist.toFixed(1)}
            м`;
            // Рисуем линию измерения (временная)
            const p1 = worldToScreen(measurePoints[0].x, measurePoints[0].z);
            const p2 = worldToScreen(measurePoints[1].x, measurePoints[1].z);
            ctx.save();
            ctx.beginPath();
            ctx.moveTo(p1.x, p1.y);
            ctx.lineTo(p2.x, p2.y);
            ctx.strokeStyle = '#ffcc44';
            ctx.lineWidth = 2;
            ctx.setLineDash([4, 4]);
            ctx.stroke();
            ctx.restore();
            measurePoints = [];
            isMeasuring = false;
            document.getElementById('btnMeasure').classList.remove('active');
            document.getElementById('measurePanel').classList.remove('visible');
        }
        else
        {
            document.getElementById('measurePanel').textContent = 'Кликните вторую точку...';
        }
    }
}
});

function updateNotesPanel()
{
    const panel = document.getElementById('notesPanel');
    panel.innerHTML = notes.map(n => `< div class=\"note\">${n}</div>`).join('');
    if (notes.length > 0) panel.classList.add('visible');
}

document.getElementById('btnNotes').addEventListener('click', () => {
    const panel = document.getElementById('notesPanel');
    panel.classList.toggle('visible');
});

document.getElementById('btnMeasure').addEventListener('click', () => {
    isMeasuring = !isMeasuring;
    document.getElementById('btnMeasure').classList.toggle('active');
    if (isMeasuring)
    {
        measurePoints = [];
        document.getElementById('measurePanel').classList.add('visible');
        document.getElementById('measurePanel').textContent = 'Кликните первую точку...';
    }
    else
    {
        document.getElementById('measurePanel').classList.remove('visible');
    }
});

// ================================================================
// ВОСПРОИЗВЕДЕНИЕ И ТАЙМЛАЙН
// ================================================================
let playing = false;
let playStartTime = 0;
let playStartElapsed = 0;
const playBtn = document.getElementById('playBtn');
const timeSlider = document.getElementById('timeSlider');
const timeDisplay = document.getElementById('timeDisplay');
const followCheck = document.getElementById('followCheck');
const stepSecondsInput = document.getElementById('stepSeconds');

function updateTimeDisplay()
{
    const idx = Math.min(currentIndex, frames.length - 1);
    const curTime = frames[idx] ? frames[idx].time : 0;
    const total = totalDuration;
    const percent = total > 0 ? curTime / total : 0;
    timeSlider.value = percent * 1000;
    const format = (ms) => {
    const s = Math.floor(ms / 1000);
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${ m}:${ sec.toString().padStart(2, '0')}`;
    };
timeDisplay.textContent = `${ format(curTime)} / ${ format(total)}`;
}

function setCurrentTime(timeMs)
{
    if (timeMs < 0) timeMs = 0;
    if (timeMs > totalDuration) timeMs = totalDuration;
    const idx = findFrameIndex(timeMs);
    currentIndex = idx;
    if (follow)
    {
        const f = frames[currentIndex];
        if (f) { centerX = f.x; centerZ = f.z; }
    }
    drawMap();
    updateTimeDisplay();
}

function playStep()
{
    if (!playing) return;
    const now = performance.now() / 1000;
    const elapsed = (now - playStartTime) * 1.0 + playStartElapsed;
    const targetTime = Math.min(elapsed * 1000, totalDuration);
    setCurrentTime(targetTime);
    if (targetTime >= totalDuration)
    {
        playing = false;
        playBtn.textContent = '▶';
        return;
    }
    requestAnimationFrame(playStep);
}

playBtn.addEventListener('click', () => {
    playing = !playing;
    playBtn.textContent = playing ? '⏸' : '▶';
    if (playing)
    {
        if (currentIndex >= frames.length - 1)
        {
            setCurrentTime(0);
        }
        playStartTime = performance.now() / 1000;
        const curTime = frames[currentIndex] ? frames[currentIndex].time : 0;
        playStartElapsed = curTime / 1000;
        playStep();
    }
});

timeSlider.addEventListener('input', () => {
    if (playing) { playing = false; playBtn.textContent = '▶'; }
    const val = parseFloat(timeSlider.value) / 1000;
    const targetTime = val * totalDuration;
    setCurrentTime(targetTime);
});

document.getElementById('gotoStart').addEventListener('click', () => {
    if (playing) { playing = false; playBtn.textContent = '▶'; }
    setCurrentTime(0);
});

document.getElementById('gotoEnd').addEventListener('click', () => {
    if (playing) { playing = false; playBtn.textContent = '▶'; }
    setCurrentTime(totalDuration);
});

document.getElementById('stepBack').addEventListener('click', () => {
    if (playing) { playing = false; playBtn.textContent = '▶'; }
    const step = parseFloat(stepSecondsInput.value) || 1;
    const curTime = frames[currentIndex] ? frames[currentIndex].time : 0;
    setCurrentTime(curTime - step * 1000);
});

document.getElementById('stepForward').addEventListener('click', () => {
    if (playing) { playing = false; playBtn.textContent = '▶'; }
    const step = parseFloat(stepSecondsInput.value) || 1;
    const curTime = frames[currentIndex] ? frames[currentIndex].time : 0;
    setCurrentTime(curTime + step * 1000);
});

followCheck.addEventListener('change', () => {
follow = followCheck.checked;
if (follow && currentIndex < frames.length)
{
    const f = frames[currentIndex];
    if (f) { centerX = f.x; centerZ = f.z; drawMap(); }
}
});

// ================================================================
// ЗУМ И ПАН
// ================================================================
canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? 0.9 : 1.1;
    scale *= delta;
    if (scale < 0.001) scale = 0.001;
    if (scale > 1000) scale = 1000;
    drawMap();
}, { passive: false });

canvas.addEventListener('mousedown', (e) => {
    if (e.button === 0)
    {
        isDragging = true;
        dragStartX = e.clientX;
        dragStartY = e.clientY;
        dragStartCX = centerX;
        dragStartCZ = centerZ;
        canvas.style.cursor = 'grabbing';
    }
});
window.addEventListener('mousemove', (e) => {
    if (isDragging)
    {
        const dx = (e.clientX - dragStartX) / scale;
        const dy = (dragStartY - e.clientY) / scale;
        centerX = dragStartCX - dx;
        centerZ = dragStartCZ - dy;
        drawMap();
    }
});
window.addEventListener('mouseup', () => {
    if (isDragging) { isDragging = false; canvas.style.cursor = 'grab'; }
});

// ================================================================
// ИНИЦИАЛИЗАЦИЯ
// ================================================================
loadMapData().then(() => {
    resize();
    fitMap();
    if (frames.length > 0)
    {
        currentIndex = 0;
        const f = frames[0];
        centerX = f.x;
        centerZ = f.z;
    }
    drawMap();
    updateTimeDisplay();
});

window.addEventListener('resize', () => {
    resize();
    fitMap();
    drawMap();
});
");
    sb.AppendLine("</script>");
sb.AppendLine("</body>");
sb.AppendLine("</html>");

return sb.ToString();
}

  
    }
}