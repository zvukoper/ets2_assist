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
                    OpenOverridesFolder();
                    return;
                }
                if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return;
                var cmd = JObject.Parse(message);
                switch ((string?)cmd["type"])
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

        // ==== ЗАПИСЬ ГРЯЗНЫХ ПОЛЕЙ ТОЧКИ В OVERRIDE-ФАЙЛ ====

        private async Task SavePointToOverrideFileAsync(JObject cmd)
        {
            var file = (string?)cmd["file"] ?? "";
            var gn = (string?)cmd["gameName"] ?? "";
            var fields = cmd["fields"] as JObject;
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(gn) || fields == null)
            {
                await SendSaveResultAsync(false, file, "Не указан файл или системное имя");
                return;
            }
            // Защита: не даём писать в test_targets.json (это цели тестовых кнопок, не overrides точек).
            if (file.Equals("test_targets.json", StringComparison.OrdinalIgnoreCase))
            {
                await SendSaveResultAsync(false, file, "test_targets.json недоступен для сохранения точек");
                return;
            }

            try
            {
                AppDataPaths.EnsureUserData();
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                string path = Path.Combine(AppDataPaths.MapOverridesDirectory, file);
                // Безопасность пути: только имя файла внутри map_overrides.
                if (Path.GetFullPath(path) != Path.GetFullPath(Path.Combine(AppDataPaths.MapOverridesDirectory, Path.GetFileName(file))))
                {
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

                // Файл мог быть не в load_order — регистрируем (в конец, низший приоритет).
                EnsureFileInLoadOrder(file);

                bool inList = IsFileInLoadOrder(file);
                var files = BuildOverrideFileList();
                Logger.Current?.Workflow($"[MAP2OVR] Сохранено '{gn}' -> {file} ({(updated ? "перезаписано" : "добавлено")}, полей={fields.Properties().Count()})");
                await SendSaveResultAsync(true, file, null, updated, files);
                // Повторная рассылка данных: точка в редакторе получает метки override
                // (обводка полей, имя файла у label, звёздочка в имени).
                await SendOverridesDataAsync();
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2OVR] Ошибка сохранения в " + file + ": " + ex.Message);
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
                    string path = Path.Combine(AppDataPaths.MapOverridesDirectory, f);
                    if (!File.Exists(path)) continue;
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
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["file"] = file ?? "", ["error"] = error ?? "", ["remaining"] = remaining };
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2DeleteOverrideResult && window.MapEditor2DeleteOverrideResult(JSON.parse({json}));"); }
            catch { }
        }

        // Сколько override-файлов (по load_order) содержат запись точки.
        private int CountFilesContainingPoint(string gn)
        {
            int count = 0;
            foreach (var f in ReadLoadOrder())
            {
                string path = Path.Combine(AppDataPaths.MapOverridesDirectory, f);
                if (!File.Exists(path)) continue;
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
            // Валидация имени: только латиница, цифры, подчёркивание (+ .json).
            var baseName = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, file.Length - 5)
                : file;
            if (string.IsNullOrWhiteSpace(baseName) || !System.Text.RegularExpressions.Regex.IsMatch(baseName, "^[A-Za-z0-9_]+$"))
            {
                await SendCreateResultAsync(false, null, "Имя файла: только латиница, цифры и подчёркивание");
                return;
            }
            var fname = baseName + ".json";
            try
            {
                AppDataPaths.EnsureUserData();
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                string path = Path.Combine(AppDataPaths.MapOverridesDirectory, fname);
                if (File.Exists(path))
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
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["file"] = file, ["updated"] = updated };
            if (files != null) res["files"] = files;
            var json = JsonConvert.SerializeObject(res.ToString(Newtonsoft.Json.Formatting.None));
            try { await _webView.CoreWebView2.ExecuteScriptAsync($"window.MapEditor2SaveOverrideResult && window.MapEditor2SaveOverrideResult(JSON.parse({json}));"); }
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
                string? cur = order.FirstOrDefault(f => f.Equals(file, StringComparison.OrdinalIgnoreCase));
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
                    order.Insert(Math.Min(target, order.Count), file);
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
                        .Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
                    : new List<string>();
            }
            catch { return new List<string>(); }
        }

        private static void WriteLoadOrder(List<string> order)
        {
            Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
            File.WriteAllLines(AppDataPaths.MapOverridesLoadOrderFile, order, new UTF8Encoding(false));
        }

        private static bool IsFileInLoadOrder(string file) => ReadLoadOrder().Any(f => f.Equals(file, StringComparison.OrdinalIgnoreCase));

        private static void EnsureFileInLoadOrder(string file)
        {
            if (IsFileInLoadOrder(file)) return;
            var order = ReadLoadOrder();
            order.Add(file);
            WriteLoadOrder(order);
        }

        // ==== СПИСОК ФАЙЛОВ + ДАННЫЕ OVERRIDES ДЛЯ JS ====

        // Список всех *.json в map_overrides: [{name,pos}] (pos=0 -> префикс * в UI).
        private JArray BuildOverrideFileList()
        {
            var result = new JArray();
            try
            {
                Directory.CreateDirectory(AppDataPaths.MapOverridesDirectory);
                var order = ReadLoadOrder();
                var files = Directory.EnumerateFiles(AppDataPaths.MapOverridesDirectory, "*.json")
                    .Select(Path.GetFileName)
                    .Where(n => n != null && !n.Equals("load_order.txt", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                foreach (var name in files)
                {
                    int pos = order.FindIndex(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)) + 1;
                    result.Add(new JObject { ["name"] = name, ["pos"] = pos });
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
        private JArray BuildOverrideDataForJs()
        {
            var result = new JArray();
            try
            {
                var order = ReadLoadOrder();
                foreach (var f in order)
                {
                    var path = Path.Combine(AppDataPaths.MapOverridesDirectory, f);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var list = JObject.Parse(File.ReadAllText(path))["customTargets"] as JArray;
                        if (list == null || list.Count == 0) continue;
                        result.Add(new JObject { ["name"] = f, ["points"] = list.DeepClone() });
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

        private async Task SendOverridesDataAsync()
        {
            if (!_pageReady || _webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var data = BuildOverrideDataForJs();
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