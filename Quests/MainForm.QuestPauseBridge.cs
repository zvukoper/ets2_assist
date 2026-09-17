using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ETS2_Assist_GUI
{
    public partial class MainForm
    {
        // A single UI timer is sufficient because MainForm is a singleton. Keeping
        // the timer static also avoids referencing instance members from a field
        // initializer, which is illegal during object construction.
        private static readonly System.Windows.Forms.Timer _questPauseBridgeTimer = CreateQuestPauseBridgeTimer();
        private int _questPauseBridgeBusy;
        private bool? _questPauseBridgeLast;

        private static System.Windows.Forms.Timer CreateQuestPauseBridgeTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += async (_, _) =>
            {
                var current = Current;
                if (current != null && !current.IsDisposed)
                {
                    await current.SyncQuestPauseStateAsync().ConfigureAwait(true);
                    Quests.QuestDiagnostics.Tick();
                }
            };
            timer.Start();
            return timer;
        }

        private async Task<bool> ReadQuestPauseStateAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
                int currentPort = TruckTelemetry.Port;
                int[] ports = currentPort == 8080 ? new[] { 8080, 8081 } : new[] { currentPort, 8080 };
                foreach (int port in ports)
                {
                    try
                    {
                        string text = (await client.GetStringAsync($"http://localhost:{port}/api/rest/single/frame/paused").ConfigureAwait(true)).Trim();
                        if (bool.TryParse(text, out bool value))
                            return value;

                        var token = JToken.Parse(text);
                        if (token.Type == JTokenType.Boolean)
                            return token.Value<bool>();
                        if (token["paused"]?.Type == JTokenType.Boolean)
                            return token["paused"]!.Value<bool>();
                        if (token["value"]?.Type == JTokenType.Boolean)
                            return token["value"]!.Value<bool>();
                    }
                    catch { }
                }
            }
            catch { }

            return _pausedIntent;
        }

        private async Task SyncQuestPauseStateAsync()
        {
            if (Interlocked.Exchange(ref _questPauseBridgeBusy, 1) != 0) return;
            try
            {
                bool paused = await ReadQuestPauseStateAsync().ConfigureAwait(true);

                var runtime = Quests.QuestRuntime.Current;
                if (runtime == null)
                {
                    Logger.Current?.Workflow("[QUEST][PAUSE_BRIDGE] runtime=missing");
                    _questPauseBridgeLast = paused;
                    return;
                }

                // QuestRuntime has its own polling path. The bridge is authoritative for
                // pause detection on this host because the running TruckTel endpoint is
                // discovered dynamically (in the test environment it is port 8081).
                // Keep the runtime field synchronized on EVERY tick, not only when the
                // observed state changes, so a legacy 8080 reader cannot overwrite it.
                var type = typeof(Quests.QuestRuntime);
                var pausedField = type.GetField("_paused", BindingFlags.Instance | BindingFlags.NonPublic);
                bool runtimePausedBefore = pausedField?.GetValue(runtime) as bool? ?? paused;
                pausedField?.SetValue(runtime, paused);

                if (_questPauseBridgeLast.HasValue && _questPauseBridgeLast.Value == paused)
                {
                    if (runtimePausedBefore != paused)
                        Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] runtime pause corrected {(runtimePausedBefore ? "true" : "false")} -> {(paused ? "true" : "false")} port={TruckTelemetry.Port}");
                    return;
                }

                bool? previous = _questPauseBridgeLast;
                _questPauseBridgeLast = paused;
                Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] state={(paused ? "PAUSED" : "RUNNING")} previous={(previous.HasValue ? (previous.Value ? "PAUSED" : "RUNNING") : "UNKNOWN")} intent={_pausedIntent} port={TruckTelemetry.Port}");
                Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] runtime._paused={(paused ? "true" : "false")}");

                try
                {
                    var update = type.GetMethod("UpdateOverlayAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (update?.Invoke(runtime, null) is Task task)
                        await task.ConfigureAwait(true);
                    Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] overlay sync requested paused={(paused ? "true" : "false")}");
                }
                catch (Exception ex)
                {
                    Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] overlay sync failed: {ex.Message}");
                }

                try
                {
                    var broadcast = type.GetMethod("BroadcastState", BindingFlags.Instance | BindingFlags.NonPublic);
                    broadcast?.Invoke(runtime, new object?[] { true, null, null, null });
                    Logger.Current?.Workflow("[QUEST][PAUSE_BRIDGE] state broadcasted");
                }
                catch (Exception ex)
                {
                    Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] broadcast failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] check failed: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _questPauseBridgeBusy, 0);
            }
        }
    }
}
