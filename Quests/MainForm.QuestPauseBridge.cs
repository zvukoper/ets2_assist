using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ETS2_Assist_GUI
{
    internal sealed partial class MainForm
    {
        private readonly Timer _questPauseBridgeTimer = CreateQuestPauseBridgeTimer();
        private int _questPauseBridgeBusy;
        private bool? _questPauseBridgeLast;

        private Timer CreateQuestPauseBridgeTimer()
        {
            var timer = new Timer { Interval = 300 };
            timer.Tick += async (_, _) => await SyncQuestPauseStateAsync();
            timer.Start();
            return timer;
        }

        private async Task SyncQuestPauseStateAsync()
        {
            if (Interlocked.Exchange(ref _questPauseBridgeBusy, 1) != 0) return;
            try
            {
                bool paused = await IsGamePausedAsync().ConfigureAwait(true);
                if (_questPauseBridgeLast.HasValue && _questPauseBridgeLast.Value == paused) return;

                bool? previous = _questPauseBridgeLast;
                _questPauseBridgeLast = paused;
                Logger.Current?.Workflow($"[QUEST][PAUSE_BRIDGE] state={(paused ? "PAUSED" : "RUNNING")} previous={(previous.HasValue ? (previous.Value ? "PAUSED" : "RUNNING") : "UNKNOWN")} intent={_pausedIntent}");

                var runtime = Quests.QuestRuntime.Current;
                if (runtime == null)
                {
                    Logger.Current?.Workflow("[QUEST][PAUSE_BRIDGE] runtime=missing");
                    return;
                }

                var type = typeof(Quests.QuestRuntime);
                var pausedField = type.GetField("_paused", BindingFlags.Instance | BindingFlags.NonPublic);
                pausedField?.SetValue(runtime, paused);
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
