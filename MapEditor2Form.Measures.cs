using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    // Режим «Линейка» редактора карты 2: измерения (measurements.json) и маршруты
    // (waypoints.json) в %LocalAppData%\ETS2_Assist.
    // Файлы СВОИ, система overrides (map_overrides) не используется.
    //   - map2-measure-save   : добавить/обновить измерение или маршрут;
    //   - map2-measure-delete : удалить измерение или маршрут;
    // Рассылка в JS: MapEditor2SetMeasurements({measurements:[...],waypoints:[...]}).
    internal sealed partial class MapEditor2Form
    {
        private bool _mapEditor2MeasuresHooked;

        private void AttachMeasuresBridge()
        {
            if (_mapEditor2MeasuresHooked || _webView.CoreWebView2 == null) return;
            _mapEditor2MeasuresHooked = true;
        }

        internal async Task SendMeasuresToEditorAsync()
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            try
            {
                var payload = new JObject
                {
                    ["measurements"] = ReadMeasureFile(AppDataPaths.MeasurementsFile, "measure"),
                    ["waypoints"] = ReadMeasureFile(AppDataPaths.WaypointsFile, "route")
                };
                var json = JsonConvert.SerializeObject(payload.ToString(Formatting.None));
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2SetMeasurements && window.MapEditor2SetMeasurements(JSON.parse({json}));");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2MEAS] Ошибка рассылки измерений: " + ex.Message);
            }
        }

        // Читает файл измерений/маршрутов. Поддерживает обёртку {"items":[...]} и голый массив.
        private static JArray ReadMeasureFile(string path, string kind)
        {
            AppDataPaths.EnsureUserData();
            if (!File.Exists(path)) return new JArray();
            try
            {
                var token = JToken.Parse(File.ReadAllText(path));
                var arr = token as JArray
                          ?? token["items"] as JArray
                          ?? token["measurements"] as JArray
                          ?? token["waypoints"] as JArray
                          ?? new JArray();
                return NormalizeMeasures(arr, kind);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning($"[MAP2MEAS] Ошибка чтения {Path.GetFileName(path)}: {ex.Message}");
                return new JArray();
            }
        }

        // Приводит элементы к виду {id,name,kind,total,created,pts:[{x,y,z}]}.
        private static JArray NormalizeMeasures(JArray src, string kind)
        {
            var outArr = new JArray();
            foreach (var t in src.OfType<JObject>())
            {
                var pts = new JArray();
                if (t["pts"] is JArray rawPts)
                {
                    foreach (var p in rawPts.OfType<JObject>())
                        pts.Add(new JObject
                        {
                            ["x"] = p["x"]?.Value<double>() ?? 0d,
                            ["y"] = p["y"]?.Value<double>() ?? 0d,
                            ["z"] = p["z"]?.Value<double>() ?? 0d
                        });
                }
                if (pts.Count < 2) continue;
                var id = ((string?)t["id"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id)) id = "meas_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var name = ((string?)t["name"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) name = id;
                outArr.Add(new JObject
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["kind"] = kind,
                    ["total"] = t["total"]?.Value<double>() ?? ComputeTotal(pts),
                    ["created"] = (string?)t["created"] ?? "",
                    ["pts"] = pts
                });
            }
            return outArr;
        }

        private static double ComputeTotal(JArray pts)
        {
            double sum = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = (pts[i]?["x"]?.Value<double>() ?? 0d) - (pts[i - 1]?["x"]?.Value<double>() ?? 0d);
                double dz = (pts[i]?["z"]?.Value<double>() ?? 0d) - (pts[i - 1]?["z"]?.Value<double>() ?? 0d);
                sum += Math.Sqrt(dx * dx + dz * dz);
            }
            return Math.Round(sum, 2);
        }

        private async Task SaveMeasureAsync(JObject cmd)
        {
            try
            {
                var kind = string.Equals((string?)cmd["kind"], "route", StringComparison.Ordinal) ? "route" : "measure";
                var item = cmd["item"] as JObject;
                if (item == null) { await SendMeasureResultAsync(false, kind, "", "", false, "Нет данных измерения"); return; }

                var path = kind == "route" ? AppDataPaths.WaypointsFile : AppDataPaths.MeasurementsFile;
                AppDataPaths.EnsureUserData();

                var existing = ReadMeasureFile(path, kind);
                var id = ((string?)item["id"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id)) id = "meas_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var name = ((string?)item["name"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name)) name = kind == "route" ? "Новый маршрут" : "Новое измерение";

                var pts = item["pts"] as JArray ?? new JArray();
                bool renameOnly = cmd["renameOnly"]?.Value<bool>() == true;
                if (pts.Count < 2 && !renameOnly)
                {
                    await SendMeasureResultAsync(false, kind, id, name, false, "Нужно минимум 2 точки");
                    return;
                }

                var entry = new JObject
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["kind"] = kind,
                    ["total"] = item["total"]?.Value<double>() ?? ComputeTotal(pts),
                    ["created"] = (string?)item["created"] ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["pts"] = pts.DeepClone()
                };

                bool updated = false;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] is JObject e && string.Equals(((string?)e["id"] ?? "").Trim(), id, StringComparison.Ordinal))
                    {
                        // Переименование: сохраняем геометрию из файла, меняем только имя.
                        if (pts.Count < 2)
                        {
                            entry["pts"] = e["pts"]?.DeepClone() ?? new JArray();
                            entry["total"] = e["total"]?.Value<double>() ?? ComputeTotal(entry["pts"] as JArray ?? new JArray());
                        }                        existing[i] = entry;
                        updated = true;
                        break;
                    }
                }
                if (!updated) existing.Add(entry);

                WriteMeasureFile(path, existing);
                Logger.Current?.Workflow($"[MAP2MEAS] {(updated ? "Обновлено" : "Добавлено")} ({kind}) '{name}' -> {path}");
                await SendMeasureResultAsync(true, kind, id, name, updated, null);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2MEAS] Ошибка сохранения измерения: " + ex.Message);
                await SendMeasureResultAsync(false, (string?)cmd["kind"] ?? "measure", "", "", false, ex.Message);
            }
        }

        private async Task DeleteMeasureAsync(JObject cmd)
        {
            try
            {
                var kind = string.Equals((string?)cmd["kind"], "route", StringComparison.Ordinal) ? "route" : "measure";
                var item = cmd["item"] as JObject;
                var id = ((string?)item?["id"] ?? (string?)cmd["id"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id)) { await SendMeasureResultAsync(false, kind, id, "", false, "Нет id"); return; }

                var path = kind == "route" ? AppDataPaths.WaypointsFile : AppDataPaths.MeasurementsFile;
                var existing = ReadMeasureFile(path, kind);
                int before = existing.Count;
                for (int i = existing.Count - 1; i >= 0; i--)
                    if (existing[i] is JObject e && string.Equals(((string?)e["id"] ?? "").Trim(), id, StringComparison.Ordinal))
                        existing.RemoveAt(i);
                if (existing.Count == before)
                {
                    await SendMeasureResultAsync(false, kind, id, "", false, "Измерение не найдено в файле");
                    return;
                }
                WriteMeasureFile(path, existing);
                Logger.Current?.Workflow($"[MAP2MEAS] Удалено ({kind}) id='{id}' из {path}");
                await SendMeasureDeleteResultAsync(true, kind, id, null);
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning("[MAP2MEAS] Ошибка удаления измерения: " + ex.Message);
                await SendMeasureDeleteResultAsync(false, (string?)cmd["kind"] ?? "measure", (string?)cmd["id"] ?? "", ex.Message);
            }
        }

        private static void WriteMeasureFile(string path, JArray items)
        {
            var root = new JObject { ["items"] = items };
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private async Task SendMeasureResultAsync(bool ok, string kind, string id, string name, bool updated, string? error)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["kind"] = kind, ["id"] = id, ["name"] = name, ["updated"] = updated };
            if (!string.IsNullOrEmpty(error)) res["error"] = error;
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2MeasureSaved && window.MapEditor2MeasureSaved({res.ToString(Formatting.None)});");
                await SendMeasuresToEditorAsync();
            }
            catch { }
        }

        private async Task SendMeasureDeleteResultAsync(bool ok, string kind, string id, string? error)
        {
            if (_webView.IsDisposed || _webView.CoreWebView2 == null) return;
            var res = new JObject { ["ok"] = ok, ["kind"] = kind, ["id"] = id };
            if (!string.IsNullOrEmpty(error)) res["error"] = error;
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.MapEditor2MeasureDeleted && window.MapEditor2MeasureDeleted({res.ToString(Formatting.None)});");
                await SendMeasuresToEditorAsync();
            }
            catch { }
        }
    }
}
