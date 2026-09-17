using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI.Quests
{
    /// <summary>
    /// Low-volume diagnostics for the quest/AR pipeline.
    /// Logged to the regular app_data log with a stable [QUEST][DIAG] prefix.
    /// The snapshot is emitted at most once per 5 seconds, plus immediately when
    /// the Ruslan trigger/AR eligibility state changes.
    /// </summary>
    internal static class QuestDiagnostics
    {
        private const string QuestId = "special_marinated_shashlik";
        private const string InteractionId = "ruslan";
        private static DateTime _lastSnapshot = DateTime.MinValue;
        private static string? _lastSignature;

        private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Tick()
        {
            try
            {
                var runtime = QuestRuntime.Current;
                var host = MainForm.Current;
                if (runtime == null || host == null || host.IsDisposed)
                    return;

                var store = GetField<QuestStore>(runtime, "_store");
                if (store == null)
                    return;

                bool enabled = store.Settings?.Enabled ?? false;
                QuestProgress? progress = store.State?.Quests != null && store.State.Quests.TryGetValue(QuestId, out var p) ? p : null;
                QuestDefinition? def = store.Definitions.TryGetValue(QuestId, out var d) ? d : null;
                QuestInteractionDefinition? interaction = def?.Interactions.Find(i => i.Id.Equals(InteractionId, StringComparison.OrdinalIgnoreCase));

                double truckX = GetField<double>(runtime, "_lastTruckX");
                double truckY = GetField<double>(runtime, "_lastTruckY");
                double truckZ = GetField<double>(runtime, "_lastTruckZ");
                bool paused = GetField<bool>(runtime, "_paused");

                object? builtPoint = null;
                bool built = false;
                if (def != null && interaction != null)
                    built = TryBuild(runtime, def, interaction, out builtPoint);

                string pointState = "not-built";
                double distance = double.NaN;
                double trigger = interaction?.TriggerRadiusM ?? double.NaN;
                bool arVisible = false;
                bool minimapVisible = false;
                bool interactive = false;
                string marker = "";
                string pointName = "";
                double px = double.NaN, py = double.NaN, pz = double.NaN;

                if (built && builtPoint != null)
                {
                    pointState = "built";
                    px = GetProperty<double>(builtPoint, "X");
                    py = GetProperty<double>(builtPoint, "Y");
                    pz = GetProperty<double>(builtPoint, "Z");
                    distance = Distance(px, py, pz, truckX, truckY, truckZ);
                    trigger = GetProperty<double>(builtPoint, "TriggerRadiusM");
                    arVisible = GetProperty<bool>(builtPoint, "ArVisible");
                    minimapVisible = GetProperty<bool>(builtPoint, "MinimapVisible");
                    interactive = GetProperty<bool>(builtPoint, "Interactive");
                    marker = GetProperty<string>(builtPoint, "Marker") ?? "";
                    pointName = GetProperty<string>(builtPoint, "Name") ?? "";
                }

                bool inside = double.IsFinite(distance) && double.IsFinite(trigger) && distance <= trigger;

                var overrides = store.State.EditorPointOverrides;
                QuestEditorPointOverride? edit = null;
                bool hasOverride = overrides != null && overrides.TryGetValue(QuestId + ":" + InteractionId, out edit) && edit != null;
                string overrideText = hasOverride
                    ? $"override[x={edit!.X?.ToString("F1") ?? "-"},y={edit.Y?.ToString("F1") ?? "-"},z={edit.Z?.ToString("F1") ?? "-"},map={edit.MinimapVisible?.ToString() ?? "-"},ar={edit.ArVisible?.ToString() ?? "-"}]"
                    : "override=none";

                var arState = ReadArHostState(host);
                var questState = BuildStateSnapshot(runtime);

                string signature = string.Join("|", new[]
                {
                    enabled.ToString(), progress?.Status.ToString() ?? "missing", progress?.Step ?? "", paused.ToString(),
                    pointState, arVisible.ToString(), minimapVisible.ToString(), interactive.ToString(), marker,
                    inside.ToString(), arState.cameraValid.ToString(), arState.truckKnown.ToString(),
                    arState.arRadius.ToString("F0"), questState.pointsHasRuslan.ToString(), questState.pointsCount.ToString()
                });

                bool changedToImportantState = _lastSignature != null && signature != _lastSignature && (inside || arVisible || questState.pointsHasRuslan);
                bool due = (DateTime.Now - _lastSnapshot).TotalSeconds >= 5;
                if (!due && !changedToImportantState)
                {
                    _lastSignature = signature;
                    return;
                }

                _lastSnapshot = DateTime.Now;
                _lastSignature = signature;

                Logger.Current?.Data(
                    $"[QUEST][DIAG] Ruslan: enabled={enabled} status={progress?.Status.ToString() ?? "missing"} step={progress?.Step ?? "-"} paused={paused} " +
                    $"build={pointState} marker={marker} map={minimapVisible} ar={arVisible} interactive={interactive} " +
                    $"point=({px:F1},{py:F1},{pz:F1}) truck=({truckX:F1},{truckY:F1},{truckZ:F1}) " +
                    $"dist={Format(distance)}m trigger={trigger:F1}m inside={inside} {overrideText}");

                Logger.Current?.Data(
                    $"[QUEST][DIAG] AR: truckKnown={arState.truckKnown} cameraValid={arState.cameraValid} " +
                    $"camera=({arState.camX:F1},{arState.camY:F1},{arState.camZ:F1}) " +
                    $"arDisplayRadius={arState.arRadius:F0} globalPoints={arState.globalPoints} nearSig={(arState.nearSig ?? "-")} " +
                    $"questStatePoints={questState.pointsCount} ruslanInQuestState={questState.pointsHasRuslan}");

                Logger.Current?.Data(
                    $"[QUEST][DIAG] PIPE: definitions={store.Definitions.Count} activeQuest={progress?.Status == QuestStatus.Active} " +
                    $"ruslanName={pointName}");
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST][DIAG] error: {ex.Message}");
            }
        }

        private static bool TryBuild(QuestRuntime runtime, QuestDefinition def, QuestInteractionDefinition interaction, out object? point)
        {
            point = null;
            try
            {
                var method = runtime.GetType().GetMethod("TryBuildInteraction", InstancePrivate);
                if (method == null)
                    return false;

                var args = new object?[] { def, interaction, null };
                bool ok = (bool)(method.Invoke(runtime, args) ?? false);
                point = args[2];
                return ok;
            }
            catch
            {
                return false;
            }
        }

        private static (bool truckKnown, bool cameraValid, double camX, double camY, double camZ, double arRadius, int globalPoints, string? nearSig) ReadArHostState(MainForm host)
        {
            bool truckKnown = GetField<bool>(host, "_arTruckKnown");
            bool cameraValid = GetField<bool>(host, "_arCameraPoseValid");
            var pose = GetFieldObject(host, "_arCameraPose");
            double camX = GetProperty<double>(pose, "X");
            double camY = GetProperty<double>(pose, "Y");
            double camZ = GetProperty<double>(pose, "Z");
            int globalPoints = 0;
            var points = GetFieldObject(host, "_arPoints") as IEnumerable;
            if (points != null)
            {
                foreach (var _ in points) globalPoints++;
            }
            string? nearSig = GetField<string>(host, "_arLastNearSig");
            double arRadius = 0;
            try { arRadius = AppSettings.ArDisplayRadiusM; } catch { }
            return (truckKnown, cameraValid, camX, camY, camZ, arRadius, globalPoints, nearSig);
        }

        private static (int pointsCount, bool pointsHasRuslan) BuildStateSnapshot(QuestRuntime runtime)
        {
            try
            {
                var method = runtime.GetType().GetMethod("BuildStatePayload", InstancePrivate);
                if (method == null) return (0, false);
                var payload = method.Invoke(runtime, new object?[] { null, null, null }) as JObject;
                var arr = payload?["points"] as JArray;
                if (arr == null) return (0, false);
                bool found = false;
                foreach (var token in arr)
                {
                    if (string.Equals((string?)token?["QuestId"], QuestId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string?)token?["InteractionId"], InteractionId, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                return (arr.Count, found);
            }
            catch
            {
                return (0, false);
            }
        }

        private static T GetField<T>(object instance, string name)
        {
            object? value = GetFieldObject(instance, name);
            if (value is T t) return t;
            return default!;
        }

        private static object? GetFieldObject(object instance, string name)
            => instance.GetType().GetField(name, InstancePrivate)?.GetValue(instance);

        private static T GetProperty<T>(object? instance, string name)
        {
            if (instance == null) return default!;
            object? value = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
            if (value is T t) return t;
            try
            {
                if (value != null && typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(value);
            }
            catch { }
            return default!;
        }

        private static double Distance(double x, double y, double z, double tx, double ty, double tz)
        {
            double dx = x - tx, dy = y - ty, dz = z - tz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string Format(double value) => double.IsFinite(value) ? value.ToString("F1") : "-";
    }
}
