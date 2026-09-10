using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    // Секция «Сохранение точек» редактора карты 2 (map_overrides).
    // Мост между JS-редактором и папкой %LocalAppData%\ETS2_Assist\map_overrides:
    //   - map2-override-save    : сохранить грязные поля точки в выбранный override-файл;
    //   - map2-override-setpos  : изменить позицию файла в load_order.txt (0 = исключить);
    //   - map2-open-overrides-folder : открыть папку в проводнике.
    // Рассылка в JS:
    //   - MapEditor2SetOverrideFiles([{name,pos}]) — список файлов + позиции;
    //   - MapEditor2ApplyOverrides([{name,points:[...]}]) — данные для переопределения
    //     точек. ПЕРВЫЙ файл в массиве = высший приоритет (первая строка load_order).
    // Миникарта и её конвейер (MainForm.OverridesPipeline.cs) НЕ затрагиваются.
    internal sealed partial class MapEditor2Form
    {
        private bool _mapEditor2OverridesHooked;

        private void AttachOverridesBridge()
        {
            if (_mapEditor2OverridesHooked || _webView.CoreWebView2 == null) return;
            _mapEditor2OverridesHooked = true;
            _webView.CoreWebView2.WebMessageReceived += OnMapEditor2OverridesMessage;
        }

        private async void OnMapEditor2OverridesMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(message)) return;
            var trimmed = message.TrimStart();
            if (trimmed.Length == 0) return;

            try
            {
                if (string.Equals(message, "map2-open-overrides-folder", StringComparison.Ordinal))
                {
                    Logger.Current?.Data("[MAP2OVR][MSG] map2-open-overrides-folder");
                    OpenOverridesFolder();
                    return;
                }
                if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return;
                var cmd = JObject.Parse(message);
                var cmdType = (string?)cmd["type"] ?? "?";
                Logger.Current?.Data($"[MAP2OVR][MSG] type={cmdType} rawLen={message.Length}");
                switch (cmdType)
                {
                    case "map2-override-save":
                        await SavePointToOverrideFileAsync(cmd);
                        break;
                    case "map2-override-setpos":
                        SetOverrideFilePosition(
                            (string?)cmd["file"] ?? "",
                            cmd["pos"]?.Value<int>() ?? 0);
                        break;
                    case "map2-override-delete":
                        await DeletePointFromOverrideFilesAsync(cmd);
                        break;
                    case "map2-override-create":
                        await CreateOverrideFileAsync(cmd);
                        break;
                    case "map2-export":
                        await ExportPointsAsync(cmd);
                        break;
                    case "map2-open-override-file":
                        OpenOverrideFileInExplorer((string?)cmd["file"] ?? "");
                        break;
                    case "map2-log":
                        // JS-логирование: пишем в app_data.log с префиксом [MAP2JS].
                        // Длинные data обрезаем, чтобы не засорять лог.
                        try
                        {
                            var jsm = (string?)cmd["msg"] ?? "";
                            var jsv = cmd["data"];
                            var jsvStr = jsv == null ? "" : (jsv.ToString(Newtonsoft.Json.Formatting.None) ?? "");
                            if (jsvStr.Length > 500) jsvStr = jsvStr.Substring(0, 500) + "...";
                            Logger.Current?.Data($"[MAP2JS] {jsm} {jsvStr}");
                        }
                        catch (Exception lex) { Logger.Current?.Data("[MAP2JS][log-parse-err] " + lex.Message); }
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка обработки сообщения overrides: " + ex.Message);
            }
        }

        private void OpenOverridesFolder()
        {
            try
            {
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + AppDataPaths.MapOverridesDirectory + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку overrides:\n\n" + ex.Message,
                    "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Клик по заголовку файла в категории «Сохранённые» редактора карты 2:
        // открывает файл в дефолтном редакторе (ассоциированном с .json), как
        // просили. Если файла нет — открывает папку overrides.
        // Защита: путь должен быть ВНУТРИ AppDataPaths.MapOverridesDirectory (защита
        // от path traversal, если придёт мусорное имя).
        private void OpenOverrideFileInExplorer(string file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    OpenOverridesFolder();
                    return;
                }
                var baseDir = AppDataPaths.MapOverridesDirectory;
                Directory.CreateDirectory(baseDir);
                var abs = Path.GetFullPath(Path.Combine(baseDir, file));
                // Нормализуем базу тоже (чтобы корректно сравнивать через StartsWith).
                var baseFull = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
                if (!abs.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this,
                        $"Недопустимый путь к файлу: {file}\n\nФайл должен быть в {baseDir}.",
                        "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (File.Exists(abs))
                {
                    // Открыть в ассоциированном редакторе (VS Code/Notepad++/блокнот).
                    // UseShellExecute=true + FileName=путь к файлу → запускается
                    // программа, ассоциированная с расширением .json.
                    Logger.Current?.Data($"[MAP2OVR][OPEN-FILE] exist=true path={abs}");
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = abs,
                            UseShellExecute = true
                        });
                        Logger.Current?.Data($"[MAP2OVR][OPEN-FILE] Process.Start OK");
                    }
                    catch (Exception exStart)
                    {
                        Logger.Current?.Warning($"[MAP2OVR][OPEN-FILE] Process.Start FAIL: {exStart.Message}");
                        // Fallback: открыть проводник с подсветкой файла.
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = "/select,\"" + abs + "\"",
                                UseShellExecute = true
                            });
                            Logger.Current?.Data($"[MAP2OVR][OPEN-FILE] fallback explorer OK");
                        }
                        catch (Exception exExp)
                        {
                            Logger.Current?.Warning($"[MAP2OVR][OPEN-FILE] fallback FAIL: {exExp.Message}");
                            MessageBox.Show(this, "Не удалось открыть файл overrides:\n\n" + exStart.Message,
                                "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
                else
                {
                    // Файл удалён/переименован — откроем папку.
                    Logger.Current?.Data($"[MAP2OVR][OPEN-FILE] exist=false path={abs} -> open folder");
                    OpenOverridesFolder();
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR][OPEN-FILE] outer-err: " + ex.Message);
                MessageBox.Show(this, "Не удалось открыть файл overrides:\n\n" + ex.Message,
                    "Редактор карты 2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==== ЗАПИСЬ ГРЯЗНЫХ ПОЛЕЙ ТОЧКИ В OVERRIDE-ФАЙЛ ====

        private async Task SavePointToOverrideFileAsync(JObject cmd)
        {
            var file = (string?)cmd["file"] ?? "";
            var gn = (string?)cmd["gameName"] ?? "";
            var fields = cmd["fields"] as JObject;
            Logger.Current?.Data($"[MAP2OVR][SAVE] enter file='{file}' gn='{gn}' fieldsNull={fields==null} fieldsCount={fields?.Count??0}");
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(gn) || fields == null)
            {
                Logger.Current?.Data($"[MAP2OVR][SAVE] reject: пустое имя или fields");
                await SendSaveResultAsync(false, file, "Не указан файл или системное имя");
                return;
            }
            // Защита: не даём писать в test_targets.json (это цели тестовых кнопок, не overrides точек).
            if (file.Equals("test_targets.json", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Current?.Data($"[MAP2OVR][SAVE] reject: test_targets.json");
                await SendSaveResultAsync(false, file, "test_targets.json недоступен для сохранения точек");
                return;
            }

            try
            {
                AppDataPaths.EnsureUserData();
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                // Резолвим относительный путь (рекурсивный поиск, если имя без каталога).
                var rel = ResolveOverrideRelativePath(file);
                string path = string.IsNullOrEmpty(rel) ? "" : OverrideFileAbsolutePath(rel);
                Logger.Current?.Data($"[MAP2OVR][SAVE] resolved rel='{rel}' path='{path}'");
                // Безопасность пути: только внутри map_overrides.
                if (string.IsNullOrEmpty(path))
                {
                    Logger.Current?.Data($"[MAP2OVR][SAVE] reject: недопустимое имя файла");
                    await SendSaveResultAsync(false, file, "Недопустимое имя файла");
                    return;
                }

                JObject root;
                if (File.Exists(path))
                {
                    root = JObject.Parse(File.ReadAllText(path));
                }
                else
                {
                    root = new JObject { ["customTargets"] = new JArray() };
                }
                if (root["customTargets"] is not JArray targets)
                {
                    targets = new JArray();
                    root["customTargets"] = targets;
                }

                bool updated = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is JObject existing &&
                        string.Equals((string?)existing["gameName"] ?? (string?)existing["id"], gn, StringComparison.Ordinal))
                    {
                        // Запись уже есть — перезаписываем ТОЛЬКО присутствующие (грязные) поля.
                        foreach (var prop in fields.Properties())
                            existing[prop.Name] = prop.Value;
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                {
                    // Новая запись: системное имя обязательно + грязные поля.
                    var entry = new JObject { ["gameName"] = gn };
                    foreach (var prop in fields.Properties())
                        if (prop.Name != "gameName") entry[prop.Name] = prop.Value;
                    targets.Add(entry);
                }
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                Logger.Current?.Data($"[MAP2OVR][SAVE] file written ok path={path} updated={updated}");

                // Файл мог быть не в load_order — регистрируем (в конец, низший приоритет).
                EnsureFileInLoadOrder(rel);

                bool inList = IsFileInLoadOrder(rel);
                var files = BuildOverrideFileList();
                Logger.Current?.Workflow($"[MAP2OVR] Сохранено '{gn}' -> {file} ({(updated ? "перезаписано" : "добавлено")}, полей={fields.Properties().Count()})");
                await SendSaveResultAsync(true, file, null, updated, files);
                Logger.Current?.Data($"[MAP2OVR][SAVE] SendSaveResultAsync(ok=true) отправлен");
                // Повторная рассылка данных: точка в редакторе получает метки override
                // (обводка полей, имя файла у label, звёздочка в имени).
                await SendOverridesDataAsync();
                Logger.Current?.Data($"[MAP2OVR][SAVE] SendOverridesDataAsync done");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка сохранения в " + file + ": " + ex.Message);
                Logger.Current?.Data($"[MAP2OVR][SAVE] FAIL ex.Message={ex.Message} StackTrace={ex.StackTrace}");
                await SendSaveResultAsync(false, file, ex.Message);
            }
        }

        // ==== УДАЛЕНИЕ ТОЧКИ ИЗ OVERRIDE-ФАЙЛОВ ====
        // Удаляет запись точки из ПЕРВОГО по приоритету файла (первая строка load_order),
        // где она сохранена. Если в более низких файлах остаются другие данные — они
        // продолжают применяться. Возвращает имя удалённого файла.
        private async Task DeletePointFromOverrideFilesAsync(JObject cmd)
        {
            var gn = (string?)cmd["gameName"] ?? "";
            Logger.Current?.Data($"[MAP2OVR][DEL] enter gn='{gn}'");
            if (string.IsNullOrWhiteSpace(gn))
            {
                await SendDeleteResultAsync(false, null, "Пустое системное имя");
                return;
            }
            try
            {
                AppDataPaths.EnsureUserData();
                var order = ReadLoadOrder();
                foreach (var f in order)
                {
                    var rel = ResolveOverrideRelativePath(f);
                    string path = string.IsNullOrEmpty(rel) ? "" : OverrideFileAbsolutePath(rel);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    JObject root;
                    try { root = JObject.Parse(File.ReadAllText(path)); }
                    catch { continue; }
                    if (root["customTargets"] is not JArray targets) continue;
                    bool removed = false;
                    for (int i = targets.Count - 1; i >= 0; i--)
                    {
                        if (targets[i] is JObject t &&
                            string.Equals((string?)t["gameName"] ?? (string?)t["id"], gn, StringComparison.Ordinal))
                        {
                            targets.RemoveAt(i);
                            removed = true;
                        }
                    }
                    if (removed)
                    {
                        File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                        Logger.Current?.Workflow($"[MAP2OVR] Удалена точка '{gn}' из {f}");
                        // Сколько файлов ещё содержат эту точку (для частичного удаления).
                        int remaining = CountFilesContainingPoint(gn);
                        await SendDeleteResultAsync(true, f, null, remaining);
                        await SendOverridesDataAsync();
                        return;
                    }
                }
                await SendDeleteResultAsync(false, null, "Точка не найдена в override-файлах");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка удаления точки: " + ex.Message);
                await SendDeleteResultAsync(false, null, ex.Message);
            }
        }

        private async Task SendDeleteResultAsync(bool ok, string? file, string? error, int remaining = 0)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null)
            {
                Logger.Current?.Data($"[MAP2OVR][DEL-RESULT] webview DISPOSED/null — результат НЕ отправлен в JS: ok={ok} file={file} err={error}");
                return;
            }
            var res = new JObject { ["ok"] = ok, ["file"] = file ?? "", ["error"] = error ?? "", ["remaining"] = remaining };
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            Logger.Current?.Data($"[MAP2OVR][DEL-RESULT] -> JS: ok={ok} file={file} err={error} remaining={remaining}");
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2DeleteOverrideResult && window.MapEditor2DeleteOverrideResult(JSON.parse({json}));"); }
            catch (Exception ex) { Logger.Current?.Data($"[MAP2OVR][DEL-RESULT] ExecuteScriptAsync FAIL: {ex.Message}"); }
        }

        // Сколько override-файлов (по load_order) содержат запись точки.
        private int CountFilesContainingPoint(string gn)
        {
            int count = 0;
            foreach (var f in ReadLoadOrder())
            {
                var rel = ResolveOverrideRelativePath(f);
                string path = string.IsNullOrEmpty(rel) ? "" : OverrideFileAbsolutePath(rel);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    if (root["customTargets"] is JArray targets &&
                        targets.OfType<JObject>().Any(t =>
                            string.Equals((string?)t["gameName"] ?? (string?)t["id"], gn, StringComparison.Ordinal)))
                        count++;
                }
                catch { }
            }
            return count;
        }

        // ==== СОЗДАНИЕ НОВОГО OVERRIDE-ФАЙЛА ====
        private async Task CreateOverrideFileAsync(JObject cmd)
        {
            var file = (string?)cmd["file"] ?? "";
            if (string.IsNullOrWhiteSpace(file))
            {
                await SendCreateResultAsync(false, null, "Пустое имя файла");
                return;
            }
            // Валидация имени: латиница, цифры, подчёркивание, точка в подкаталоге
            // (имя вида "подкаталог/файл" или голое имя) + .json.
            var baseName = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - 5)
                : file;
            var parts = baseName.Replace('\\', '/').Split('/');
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part) || !System.Text.RegularExpressions.Regex.IsMatch(part, "^[A-Za-z0-9_]+$") || part.Equals("..", StringComparison.Ordinal))
                {
                    await SendCreateResultAsync(false, null, "Имя файла: только латиница, цифры и подчёркивание");
                    return;
                }
            }
            var fname = string.Join('\\', parts) + ".json";
            try
            {
                AppDataPaths.EnsureUserData();
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                string path = OverrideFileAbsolutePath(fname);
                if (string.IsNullOrEmpty(path))
                {
                    await SendCreateResultAsync(false, null, "Недопустимое имя файла");
                    return;
                }
                // Голое имя: проверяем дубли и в существующих подкаталогах.
                if (File.Exists(path) || EnumerateOverrideFilesRecursive().Any(f => string.Equals(Path.GetFileName(f), Path.GetFileName(fname), StringComparison.OrdinalIgnoreCase)))
                {
                    await SendCreateResultAsync(false, null, $"Файл «{fname}» уже существует");
                    return;
                }
                // Создаём пустой файл с пустым массивом customTargets.
                var root = new JObject { ["customTargets"] = new JArray() };
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                // Регистрируем в load_order (в конец, низший приоритет).
                EnsureFileInLoadOrder(fname);
                Logger.Current?.Workflow($"[MAP2OVR] Создан override-файл {fname}");
                var files = BuildOverrideFileList();
                await SendCreateResultAsync(true, fname, null, files);
                await SendOverrideFilesAsync();
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка создания файла: " + ex.Message);
                await SendCreateResultAsync(false, null, ex.Message);
            }
        }

        private async Task SendCreateResultAsync(bool ok, string? file, string? error, JArray? files = null)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["file"] = file ?? "", ["error"] = error ?? "" };
            if (files != null) res["files"] = files;
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2CreateOverrideResult && window.MapEditor2CreateOverrideResult(JSON.parse({json}));"); }
            catch { }
        }

        private async Task SendSaveResultAsync(bool ok, string file, string? error, bool updated = false, JArray? files = null)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null)
            {
                Logger.Current?.Data($"[MAP2OVR][SAVE-RESULT] webview DISPOSED/null — результат НЕ отправлен в JS: ok={ok} file={file} err={error}");
                return;
            }
            var res = new JObject { ["ok"] = ok, ["file"] = file, ["updated"] = updated, ["error"] = error ?? "" };
            if (files != null) res["files"] = files;
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            Logger.Current?.Data($"[MAP2OVR][SAVE-RESULT] -> JS: ok={ok} file={file} err={error} updated={updated}");
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SaveOverrideResult && window.MapEditor2SaveOverrideResult(JSON.parse({json}));"); }
            catch (Exception ex) { Logger.Current?.Data($"[MAP2OVR][SAVE-RESULT] ExecuteScriptAsync FAIL: {ex.Message}"); }
        }

        // ==== ЭКСПОРТ ВЫДЕЛЕННЫХ ТОЧЕК ====
        // json — формат оверрайдов ({customTargets:[...]}) в папку map_overrides (БЕЗ
        // подключения в load_order — файл просто лежит рядом, подключается вручную);
        // sii — хранилище игрового редактора (editor_item_storage + viewport_placements),
        // тот же формат, что генерируют generate_viewports*.ps1, в
        //  <ETS2>\editor\storages\ (если найдена) иначе — в map_overrides.
        private async Task ExportPointsAsync(JObject cmd)
        {
            var file = (string?)cmd["file"] ?? "";
            var format = ((string?)cmd["format"] ?? "json").ToLowerInvariant();
            var points = cmd["points"] as JArray;
            if (string.IsNullOrWhiteSpace(file) || points == null || points.Count == 0)
            {
                await SendExportResultAsync(false, file, format, 0, "Нет файла или точек");
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(file, "^[A-Za-z0-9_]+$") || file.Length > 64)
            {
                await SendExportResultAsync(false, file, format, 0, "Имя файла: только латиница, цифры и подчёркивание");
                return;
            }
            try
            {
                AppDataPaths.EnsureUserData();
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                string outPath;
                if (format == "sii")
                {
                    string content = BuildSiiStorage(points);
                    string outDir = FindEditorStoragesDirectory();
                    Directory.CreateDirectory(outDir);
                    outPath = Path.Combine(outDir, file + ".sii");
                    File.WriteAllText(outPath, content, new UTF8Encoding(false));
                }
                else
                {
                    var root = new JObject();
                    var arr = new JArray();
                    foreach (var t in points.OfType<JObject>())
                    {
                        var entry = new JObject
                        {
                            ["gameName"] = (string?)t["gameName"] ?? "",
                            ["realName"] = (string?)t["realName"] ?? "",
                            ["coords"] = (string?)t["coords"] ?? "0, 0, 0",
                            ["status"] = "active",
                            ["icon"] = "default",
                            ["color"] = string.IsNullOrEmpty((string?)t["color"]) ? "default" : t["color"],
                            ["targetMapOverview"] = false,
                            ["isRandom"] = false
                        };
                        var cat = (string?)t["category"];
                        if (!string.IsNullOrWhiteSpace(cat)) entry["category"] = cat;
                        arr.Add(entry);
                    }
                    root["customTargets"] = arr;
                    outPath = Path.Combine(AppDataPaths.MapOverridesDirectory, file + ".json");
                    File.WriteAllText(outPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
                }
                Logger.Current?.Workflow($"[MAP2OVR] Экспорт {points.Count} точек -> {outPath} (формат {format})");
                await SendExportResultAsync(true, Path.GetFileName(outPath), format, points.Count, null);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка экспорта: " + ex.Message);
                await SendExportResultAsync(false, file, format, 0, ex.Message);
            }
        }

        // Ищет папку editor\storages в стандартных местах установки ETS2/ATS.
        private static string FindEditorStoragesDirectory()
        {
            var candidates = new List<string>();
            string? documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                candidates.Add($@"{c}:\Users\Docs\Euro Truck Simulator 2\editor\storages");
                candidates.Add($@"{c}:\Users\Docs\American Truck Simulator\editor\storages");
            }
            if (!string.IsNullOrEmpty(documents))
            {
                candidates.Add(Path.Combine(documents, "Euro Truck Simulator 2", "editor", "storages"));
                candidates.Add(Path.Combine(documents, "American Truck Simulator", "editor", "storages"));
            }
            foreach (var dir in candidates)
                if (Directory.Exists(dir)) return dir;
            // Не нашли — сохраняем рядом с оверрайдами, чтобы файл не потерялся.
            return AppDataPaths.MapOverridesDirectory;
        }

        // Формат editor_item_storage — как в generate_viewports.ps1 (проверен пользователем):
        // каждая точка = один viewport (камера к югу от точки +Z, 10 м, 40°).
        private static string BuildSiiStorage(JArray points)
        {
            var pts = points.OfType<JObject>()
                .Select(t => new
                {
                    Name = (string?)t["realName"] ?? (string?)t["gameName"] ?? "",
                    Coords = (string?)t["coords"] ?? "0, 0, 0"
                })
                // Защита от NaN/Infinity: невалидная точка не должна попадать в sii.
                .Where(t => { var v = ParseXYZ(t.Coords); return v[0] == v[0] && v[2] == v[2]; })
                .ToList();
            double[] ParseXYZ(string s)
            {
                var parts = s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                double x = 0, y = 0, z = 0;
                if (parts.Length > 0) double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
                if (parts.Length > 1) double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
                if (parts.Length > 2) double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
                return new[] { x, y, z };
            }
            static string FloatToHex(float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) f = 0f;   // NaN/Infinity → 0 (не «ffc00000»)
                var bytes = BitConverter.GetBytes(f);
                Array.Reverse(bytes);
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            const double dist = 10.0;
            double ang = Math.PI * (40.0 / 180.0);   // 40° возвышения, 10 м — как в генераторе viewports
            double horiz = dist * Math.Cos(ang);
            double vert = dist * Math.Sin(ang);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var lines = new List<string>
            {
                "SiiNunit",
                "{",
                "editor_item_storage : _nameless.278.335e.44f8 {",
                " map_name: \"/map/europe.mbd\"",
                " version: 1",
                " map_items: 0",
                " map_item_colors: 0",
                " map_item_names: 0",
                " map_item_timestamps: 0",
                $" viewport_placements: {pts.Count}"
            };
            for (int i = 0; i < pts.Count; i++)
            {
                var xyz = ParseXYZ(pts[i].Coords);
                double x = xyz[0], y = xyz[1], z = xyz[2];
                double cx = x, cy = y + vert, cz = z + horiz;
                double dx = x - cx, dy = y - cy, dz = z - cz;
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 1e-9) { dx = 0; dy = 0; dz = -1; len = 1; }
                dx /= len; dy /= len; dz /= len;
                double w = 1 - dz, qx = dy, qy = -dx, qz = 0.0;
                double qlen = Math.Sqrt(w * w + qx * qx + qy * qy + qz * qz);
                if (qlen < 1e-9) { w = 1; qx = qy = qz = 0; qlen = 1; }
                w /= qlen; qx /= qlen; qy /= qlen; qz /= qlen;
                lines.Add($" viewport_placements[{i}]: (&{FloatToHex((float)cx)}, &{FloatToHex((float)cy)}, &{FloatToHex((float)cz)}) (&{FloatToHex((float)w)}; &{FloatToHex((float)qx)}, &{FloatToHex((float)qy)}, &{FloatToHex((float)qz)})");
            }
            lines.Add($" viewport_types: {pts.Count}");
            for (int i = 0; i < pts.Count; i++) lines.Add($" viewport_types[{i}]: free_camera");
            lines.Add($" viewport_colors: {pts.Count}");
            for (int i = 0; i < pts.Count; i++) lines.Add($" viewport_colors[{i}]: 16777215");
            lines.Add($" viewport_names: {pts.Count}");
            for (int i = 0; i < pts.Count; i++) lines.Add($" viewport_names[{i}]: \"{EscapeSiiString(pts[i].Name)}\"");
            lines.Add($" viewport_timestamps: {pts.Count}");
            for (int i = 0; i < pts.Count; i++) lines.Add($" viewport_timestamps[{i}]: {now}");
            lines.Add(" selected_viewport: (0, 0, 0) (1; 0, 0, 0)");
            lines.Add("}");
            lines.Add("");
            lines.Add("}");
            return string.Join("\r\n", lines);
        }

        private static string EscapeSiiString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\\\"");
        }

        private async Task SendExportResultAsync(bool ok, string? file, string format, int count, string? error)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["file"] = file ?? "", ["format"] = format, ["count"] = count, ["error"] = error ?? "" };
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2ExportResult && window.MapEditor2ExportResult(JSON.parse({json}));"); }
            catch { }
        }

        // ==== ПОЗИЦИЯ ФАЙЛА В LOAD_ORDER ====

        // pos: 1..N — новая позиция файла (файл с позиции N сдвигается вниз);
        // 0 — файл исключается из load_order (остаётся в списке с префиксом *,
        // данные из него не применяются).
        private void SetOverrideFilePosition(string file, int pos)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file)) return;
                var order = ReadLoadOrder();
                var rel = ResolveOverrideRelativePath(file);
                string? cur = order.FirstOrDefault(f => LoadOrderMatches(f, rel));
                int curIdx = cur != null ? order.IndexOf(cur) : -1;

                if (pos <= 0)
                {
                    if (cur != null) { order.Remove(cur); Logger.Current?.Workflow($"[MAP2OVR] {file} исключён из load_order (позиция 0)"); }
                }
                else
                {
                    int target = Math.Min(pos - 1, order.Count - (cur != null ? 1 : 0));
                    if (target < 0) target = 0;
                    if (curIdx >= 0) order.RemoveAt(curIdx);
                    order.Insert(Math.Min(target, order.Count), string.IsNullOrEmpty(rel) ? file : rel);
                    Logger.Current?.Workflow($"[MAP2OVR] {file} -> позиция {target + 1} в load_order");
                }
                WriteLoadOrder(order);
                _ = SendOverrideFilesAsync();
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка изменения load_order: " + ex.Message);
            }
        }

        private static List<string> ReadLoadOrder()
        {
            try
            {
                return File.Exists(AppDataPaths.MapOverridesLoadOrderFile)
                    ? File.ReadAllLines(AppDataPaths.MapOverridesLoadOrderFile)
                        .Select(l => l.Trim()).Where(l => l.Length > 0)
                        // Нормализация разделителей к '\\' и фильтр вложенных путей (..).
                        .Select(l => l.Replace('/', '\\'))
                        .Where(l => !l.Contains("..")).ToList()
                    : new List<string>();
            }
            catch { return new List<string>(); }
        }

        // Нормализация имени файла к ОТНОСИТЕЛЬНОМУ пути внутри map_overrides:
        // если пришло голое имя — ищем его рекурсивно во всех подкаталогах;
        // иначе нормализуем разделители и возвращаем относительный путь как есть.
        private static string ResolveOverrideRelativePath(string name)
        {
            var rel = (name ?? "").Replace('/', '\\').TrimStart('\\');
            if (string.IsNullOrWhiteSpace(rel)) return string.Empty;
            // Уже содержит подкаталог — оставляем как есть (после нормализации).
            if (rel.Contains('\\'))
            {
                var abs = OverrideFileAbsolutePath(rel);
                return string.IsNullOrEmpty(abs) ? string.Empty : rel;
            }
            // Голое имя: сначала корень, затем рекурсивный поиск по подкаталогам.
            var rootPath = Path.Combine(AppDataPaths.MapOverridesDirectory, rel);
            if (File.Exists(rootPath)) return rel;
            foreach (var found in EnumerateOverrideFilesRecursive())
            {
                if (string.Equals(Path.GetFileName(found), rel, StringComparison.OrdinalIgnoreCase))
                    return found;
            }
            return rel;   // не нашли — вернём как есть (файл может появиться позже)
        }

        // Рекурсивный поиск *.json в map_overrides: возвращает ОТНОСИТЕЛЬНЫЕ пути
        // (напр. "ets2_overrides\\targets.json"), кроме load_order.txt.
        private static List<string> EnumerateOverrideFilesRecursive()
        {
            var result = new List<string>();
            try
            {
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                foreach (var full in Directory.EnumerateFiles(AppDataPaths.MapOverridesDirectory, "*.json", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(AppDataPaths.MapOverridesDirectory, full);
                    if (rel.Equals("load_order.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(rel);
                }
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка рекурсивного списка файлов: " + ex.Message);
            }
            return result;
        }

        // Абсолютный путь по относительному имени (с подкаталогом).
        private static string OverrideFileAbsolutePath(string rel)
        {
            rel = (rel ?? "").Replace('/', '\\').TrimStart('\\');
            var abs = Path.GetFullPath(Path.Combine(AppDataPaths.MapOverridesDirectory, rel));
            var root = Path.GetFullPath(AppDataPaths.MapOverridesDirectory);
            if (!abs.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return abs;
        }

        private static void WriteLoadOrder(List<string> order)
        {
            Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
            File.WriteAllLines(AppDataPaths.MapOverridesLoadOrderFile, order, new UTF8Encoding(false));
        }

        // Сравнение имени файла с записью load_order: запись может быть с путём или без —
        // сверяем и полный относительный путь, и голое имя (устаревшие load_order).
        private static bool LoadOrderMatches(string entry, string rel)
        {
            if (string.Equals(entry, rel, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(Path.GetFileName(entry), Path.GetFileName(rel), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileInLoadOrder(string file)
        {
            var rel = ResolveOverrideRelativePath(file);
            if (string.IsNullOrEmpty(rel)) return false;
            return ReadLoadOrder().Any(f => LoadOrderMatches(f, rel));
        }

        private static void EnsureFileInLoadOrder(string file)
        {
            var rel = ResolveOverrideRelativePath(file);
            if (string.IsNullOrWhiteSpace(rel)) return;
            if (ReadLoadOrder().Any(f => LoadOrderMatches(f, rel)))
            {
                // Голое имя в load_order, а файл найден в подкаталоге — обновляем запись на путь.
                var order = ReadLoadOrder();
                int idx = order.FindIndex(f => LoadOrderMatches(f, rel));
                if (idx >= 0 && !string.Equals(order[idx], rel, StringComparison.OrdinalIgnoreCase))
                {
                    string before = order[idx];
                    order[idx] = rel;
                    WriteLoadOrder(order);
                    Logger.Current?.Workflow($"[MAP2OVR] load_order обновлён: '{before}' -> '{rel}'");
                }
                return;
            }
            var order2 = ReadLoadOrder();
            order2.Add(rel);
            WriteLoadOrder(order2);
        }

        // ==== СПИСОК ФАЙЛОВ + ДАННЫЕ OVERRIDES ДЛЯ JS ====

        // Список всех *.json в map_overdims (РЕКУРСИВНО, с подкаталогами):
        // [{name,pos}] — name = ОТНОСИТЕЛЬНЫЙ путь (напр. "ets2_overrides\\targets.json"),
        // pos=0 -> префикс * в UI (файл вне load_order).
        private JArray BuildOverrideFileList()
        {
            var result = new JArray();
            try
            {
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                var order = ReadLoadOrder();
                foreach (var rel in EnumerateOverrideFilesRecursive())
                {
                    int pos = order.FindIndex(f => LoadOrderMatches(f, rel)) + 1;
                    result.Add(new JObject { ["name"] = rel, ["pos"] = pos });
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка списка файлов overrides: " + ex.Message);
            }
            return result;
        }

        private async Task SendOverrideFilesAsync()
        {
            if (!_pageReady || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var files = BuildOverrideFileList();
            var json = JsonConvert.SerializeObject(files.ToString(Newtonsoft.Json.Formatting.None));
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SetOverrideFiles(JSON.parse({json}));"); }
            catch { }
        }

        // Данные overrides для JS: файлы в ПОРЯДКЕ ПРИМЕНЕНИЯ (первый = высший приоритет,
        // т.е. первая строка load_order). JS применяет их по очереди; первый файл задаёт
        // значение, последующие НЕ переопределяют его поля (см. MapEditor2ApplyOverrides).
        // Файлы ВНЕ load_order идут с inOrder=false: JS НЕ применяет их на карту, только
        // показывает точки в «Сохранённых» (курсив + звёздочка, клик = создание новой точки).
        private JArray BuildOverrideDataForJs()
        {
            var result = new JArray();
            try
            {
                var order = ReadLoadOrder();
                foreach (var f in order)
                {
                    // Запись может быть голым именем — резолвим в относительный путь.
                    var rel = ResolveOverrideRelativePath(f);
                    var path = string.IsNullOrEmpty(rel) ? null : OverrideFileAbsolutePath(rel);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        Logger.Current?.Data($"[MAP2OVR] файл из load_order не найден: {f}");
                        continue;
                    }
                    try
                    {
                        var list = JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray;
                        if (list == null || list.Count == 0) continue;
                        result.Add(new JObject { ["name"] = rel, ["inOrder"] = true, ["points"] = list.DeepClone() });
                    }
                    catch (Exception ex)
                    {
                        Logger.Current?.Warning($"[MAP2OVR] Ошибка чтения {f}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка сборки данных overrides: " + ex.Message);
            }
            return result;
        }

        // Данные ТОЧЕК из файлов ВНЕ load_order (inOrder=false): применяются ТОЛЬКО
        // в списке «Сохранённые» (курсив + звёздочка, на карте скрыты, клик = новая точка).
        private JArray BuildExcludedOverrideDataForJs()
        {
            var result = new JArray();
            try
            {
                var order = ReadLoadOrder();
                foreach (var rel in EnumerateOverrideFilesRecursive())
                {
                    bool inOrder = order.Any(f => LoadOrderMatches(f, rel));
                    if (inOrder) continue;
                    var path = OverrideFileAbsolutePath(rel);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    try
                    {
                        var list = JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray;
                        if (list == null || list.Count == 0) continue;
                        result.Add(new JObject { ["name"] = rel, ["inOrder"] = false, ["points"] = list.DeepClone() });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка сборки данных неактивных overrides: " + ex.Message);
            }
            return result;
        }

        private async Task SendOverridesDataAsync()
        {
            if (!_pageReady || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var data = BuildOverrideDataForJs();
            var excluded = BuildExcludedOverrideDataForJs();
            foreach (var ex in excluded) data.Add(ex);
            var json = JsonConvert.SerializeObject(data.ToString(Newtonsoft.Json.Formatting.None));
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2SetOverrideFiles && window.MapEditor2SetOverrideFiles(JSON.parse({JsonConvert.SerializeObject(BuildOverrideFileList().ToString(Newtonsoft.Json.Formatting.None))}));");
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2ApplyOverrides && window.MapEditor2ApplyOverrides(JSON.parse({json}));");
            }
            catch { }
        }

        /// <summary>Вызывается из MapEditor2Form при map2-ready и map2-data-ready.</summary>
        internal async Task SendOverridesToEditorAsync()
        {
            try
            {
                await SendOverrideFilesAsync();
                await SendOverridesDataAsync();
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка рассылки overrides в редактор: " + ex.Message);
            }
        }
    }
}