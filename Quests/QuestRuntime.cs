using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ETS2_Assist_GUI.Quests
{
    internal sealed class QuestRuntime : IDisposable
    {
        private const int QuestWsPort = 8085;
        private const int SwHide = 0;
        private const int SwShow = 5;
        private static readonly object Sync = new();
        private static QuestRuntime? _current;

        private readonly MainForm _host;
        private readonly QuestStore _store;
        private readonly QuestPointResolver _resolver;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(700) };
        private readonly HashSet<string> _inside = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeDialogue = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QuestPointSnapshot> _nearby = new();
        // Уведомление «Рядом доступно задание» держится 12 с: его нужно успеть
        // прочитать, не отвлекаясь от управления грузовиком.
        private const int RadiusNotificationMs = 12000;
        private readonly ToolStripMenuItem _menuRoot = new("Квесты");
        private readonly ToolStripMenuItem _menuEnabled = new("Система включена");
        private readonly ToolStripMenuItem _menuDebug = new("Отладочный показ всех точек");
        private readonly ToolStripMenuItem _menuSettings = new("Настройки видимости…");
        private readonly ToolStripMenuItem _menuReset = new("Сбросить тестовый квест");

        private WebSocketServer? _server;
        private System.Threading.Timer? _tickTimer;
        private int _tickBusy;
        private bool _disposed;
        private bool _paused;

        // UI-состояние интерактива и текущее выделение принадлежат QuestRuntime.
        // Они должны существовать независимо от наличия dialogue в конкретном
        // quest_state, иначе следующий периодический тик не может сохранить выбор.
        private bool _interactiveVisible;
        private string _selectedQuestId = "";
        private string _selectedInteractionId = "";

        private DateTime _lastStateSentUtc = DateTime.MinValue;
        private DateTime _lastWsDiagUtc = DateTime.MinValue;
        private string _lastWsDiagKey = "";

        private JObject? _lastState;
        private double _lastTruckX, _lastTruckY, _lastTruckZ;
        private bool? _lastDebugShow;
        private int _lastDebugRadius;

        private sealed class QuestSocketBehavior : WebSocketBehavior
        {
            private readonly QuestRuntime _owner;
            public QuestSocketBehavior(QuestRuntime owner) => _owner = owner;
            protected override void OnOpen() => _owner.OnQuestClientOpen(this);
            protected override void OnClose(CloseEventArgs e) { }
            protected override void OnMessage(MessageEventArgs e)
            {
                try { _owner.OnQuestMessage(JObject.Parse(e.Data)); }
                catch (Exception ex) { Logger.Current?.Data($"[QUEST][WS] invalid message: {ex.Message}"); }
            }
            public void SendJson(JObject payload) { try { Send(payload.ToString(Formatting.None)); } catch { } }
        }

        private QuestRuntime(MainForm host)
        {
            _host = host;
            _store = new QuestStore();
            _resolver = new QuestPointResolver(_store);
            InstallMenu();
            EnforceArPointDebugMode();
            TruckTelemetry.Start();
            _tickTimer = new System.Threading.Timer(_ => QueueTick(), null, 300, Math.Max(100, _store.Settings.PollIntervalMs));
            try
            {
                _server = new WebSocketServer(QuestWsPort);
                _server.AddWebSocketService("/", () => new QuestSocketBehavior(this));
                _server.Start();
                Logger.Current?.Workflow($"[QUEST] WebSocket server started on {QuestWsPort}.");
            }
            catch (Exception ex) { Logger.Current?.Warning($"[QUEST] WebSocket server start failed: {ex.Message}"); }
            Application.ApplicationExit += (_, _) => Dispose();
        }

        public static void Attach(MainForm host)
        {
            lock (Sync)
            {
                _current?.Dispose();
                _current = new QuestRuntime(host);
            }
        }

        internal static QuestRuntime? Current { get { lock (Sync) return _current; } }
        internal bool DebugShowAllPoints => _store.Settings.DebugShowAllPoints;

        private void InstallMenu()
        {
            try
            {
                var menu = _host.Controls.OfType<MenuStrip>().FirstOrDefault();
                if (menu == null) return;
                _menuEnabled.CheckOnClick = true;
                _menuDebug.CheckOnClick = true;
                _menuEnabled.Checked = _store.Settings.Enabled;
                _menuDebug.Checked = _store.Settings.DebugShowAllPoints;
                _menuEnabled.Click += (_, _) =>
                {
                    _store.Settings.Enabled = _menuEnabled.Checked;
                    _store.SaveSettings();
                    _activeDialogue.Clear(); _inside.Clear();
                    EnforceArPointDebugMode(); BroadcastState(true);
                };
                _menuDebug.Click += (_, _) =>
                {
                    _store.Settings.DebugShowAllPoints = _menuDebug.Checked;
                    _store.SaveSettings(); EnforceArPointDebugMode(); BroadcastState(true);
                };
                _menuSettings.Click += (_, _) =>
                {
                    using var dlg = new QuestSettingsForm(_store.Settings, () =>
                    {
                        _store.SaveSettings(); RestartTickTimer(); EnforceArPointDebugMode(); BroadcastState(true);
                    });
                    dlg.ShowDialog(_host);
                };
                _menuReset.Click += (_, _) =>
                {
                    if (MessageBox.Show(_host, "Сбросить состояние тестового квеста и тестовый инвентарь?", "Квесты", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    _store.ResetQuest("special_marinated_shashlik", clearInventory: true);
                    _inside.Clear(); _activeDialogue.Clear(); _nearby.Clear(); ForceArRebuild(); BroadcastState(true);
                };
                _menuRoot.DropDownItems.Add(_menuEnabled);
                _menuRoot.DropDownItems.Add(_menuDebug);
                _menuRoot.DropDownItems.Add(new ToolStripSeparator());
                _menuRoot.DropDownItems.Add(_menuSettings);
                _menuRoot.DropDownItems.Add(_menuReset);
                menu.Items.Add(_menuRoot);
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] menu error: {ex.Message}"); }
        }

        private void RestartTickTimer()
        {
            try { _tickTimer?.Dispose(); _tickTimer = new System.Threading.Timer(_ => QueueTick(), null, 150, Math.Max(100, _store.Settings.PollIntervalMs)); } catch { }
        }

        private void QueueTick()
        {
            if (Interlocked.CompareExchange(ref _tickBusy, 1, 0) != 0) return;
            try
            {
                if (_disposed || _host.IsDisposed || !_host.IsHandleCreated) { Volatile.Write(ref _tickBusy, 0); return; }
                _host.BeginInvoke(new Action(async () =>
                {
                    try { await TickAsync().ConfigureAwait(true); }
                    catch (Exception ex) { Logger.Current?.Data("[QUEST] tick: " + ex.Message); }
                    finally { Volatile.Write(ref _tickBusy, 0); }
                }));
            }
            catch { Volatile.Write(ref _tickBusy, 0); }
        }

        private async Task TickAsync()
        {
            if (_disposed) return;
            ProcessInstantActivations();
            if (!TruckTelemetry.TryGetSnapshot(out var truck, out _, out bool haveSample) || !haveSample)
            {
                _nearby.Clear(); _host.SetQuestInteractiveSignal(false); BroadcastState(_paused); await UpdateOverlayAsync().ConfigureAwait(true); EnforceArPointDebugMode(); return;
            }
            _lastTruckX = truck.X; _lastTruckY = truck.Y; _lastTruckZ = truck.Z;
            _nearby.Clear(); _nearby.AddRange(GetAvailableInteractions(truck.X, truck.Y, truck.Z)); HandleTriggers(_nearby);
            // Закладка «Квесты» пульсирует только когда рядом есть интерактив.
            _host.SetQuestInteractiveSignal(_nearby.Any(p => !string.IsNullOrWhiteSpace(p.Marker) && p.Marker != "none"));
            if (_paused || _nearby.Count > 0 || DateTime.UtcNow - _lastStateSentUtc > TimeSpan.FromSeconds(1)) BroadcastState(_paused || _nearby.Count > 0);
            await UpdateOverlayAsync().ConfigureAwait(true); EnforceArPointDebugMode();
        }

        private List<QuestPointSnapshot> GetAvailableInteractions(double x, double y, double z)
        {
            var result = new List<QuestPointSnapshot>(); if (!_store.Settings.Enabled) return result;
            foreach (var def in _store.Definitions.Values)
            {
                if (!IsQuestAvailable(def)) continue;
                foreach (var interaction in def.Interactions)
                {
                    if (!TryBuildInteraction(def, interaction, out var point) || !point.Interactive) continue;
                    double dist = Math.Sqrt(DistanceSquared(point.X, point.Y, point.Z, x, y, z));
                    if (dist <= point.TriggerRadiusM) result.Add(point);
                }
            }
            return result.OrderBy(p => DistanceSquared(p.X, p.Y, p.Z, x, y, z)).ToList();
        }

        private void HandleTriggers(List<QuestPointSnapshot> nearby)
        {
            var now = new HashSet<string>(nearby.Select(p => p.QuestId + ":" + p.InteractionId), StringComparer.OrdinalIgnoreCase);
            foreach (var p in nearby)
            {
                string key = p.QuestId + ":" + p.InteractionId; if (!_inside.Add(key)) continue;
                if (p.Marker == "none" && !p.TriggerWhenNoMarker) continue;
                bool noMore = p.Marker == "none";
                Broadcast(new JObject { ["command"] = "quest_notify", ["notification"] = new JObject
                    {
                        ["title"] = noMore ? "" : "Рядом доступно задание",
                        ["text"] = noMore ? "Заданий пока нет. Возвращайтесь позже. (в разработке)" : "Выйдите в меню или поставьте игру на паузу, чтобы узнать подробности",
                        ["icon"] = p.Marker,
                        // Уведомление о появлении интерактива показывается дольше
                        // обычного: игрок видит его периферийным зрением за рулём.
                        ["durationMs"] = RadiusNotificationMs
                    }});
            }
            _inside.RemoveWhere(k => !now.Contains(k));
        }

        private bool IsQuestAvailable(QuestDefinition def)
        {
            if (!_store.Settings.Enabled || !ScheduleAllows(def)) return false;
            var progress = GetProgress(def.Id);
            if (def.ActivationsPerPlayer > 0 && ActivationCount(def.Id) >= def.ActivationsPerPlayer && progress.Status != QuestStatus.Active) return false;
            if (progress.Status == QuestStatus.Completed && def.Interactions.All(i => !i.ShowWhenQuestCompleted)) return false;
            if (def.Requirements != null && !EvaluateRequirement(def.Requirements)) return false;
            foreach (string id in def.RequiredQuests.Where(s => !string.IsNullOrWhiteSpace(s))) if (GetProgress(id).Status != QuestStatus.Completed) return false;
            foreach (string id in def.Excludes.Concat(def.IncompatibleQuests).Where(s => !string.IsNullOrWhiteSpace(s))) if (GetProgress(id).Status == QuestStatus.Active) return false;
            if (!string.IsNullOrWhiteSpace(def.ExclusiveGroup))
                foreach (var other in _store.Definitions.Values)
                    if (!ReferenceEquals(other, def) && string.Equals(other.ExclusiveGroup, def.ExclusiveGroup, StringComparison.OrdinalIgnoreCase) && GetProgress(other.Id).Status == QuestStatus.Active) return false;
            return true;
        }

        private void ApplyEditorOverride(string key, ref QuestResolvedPoint coord, ref string name, ref bool minimapVisible, ref bool arVisible)
        {
            if (!_store.State.EditorPointOverrides.TryGetValue(key, out QuestEditorPointOverride? edit) || edit == null) return;
            if (edit.X.HasValue) coord.X = edit.X.Value;
            if (edit.Y.HasValue) coord.Y = edit.Y.Value;
            if (edit.Z.HasValue) coord.Z = edit.Z.Value;
            if (!string.IsNullOrWhiteSpace(edit.Name)) name = edit.Name;
            if (edit.MinimapVisible.HasValue) minimapVisible = edit.MinimapVisible.Value;
            if (edit.ArVisible.HasValue) arVisible = edit.ArVisible.Value;
        }

        private bool TryBuildInteraction(QuestDefinition def, QuestInteractionDefinition interaction, out QuestPointSnapshot point)
        {
            point = new QuestPointSnapshot();
            string key = def.Id + ":" + interaction.Id;
            bool generatedKnown = _store.State.GeneratedPoints.TryGetValue(key, out QuestGeneratedPoint? generated) && generated != null && generated.Known;
            bool permanent = _store.State.PermanentInteractionNames.ContainsKey(key);
            var progress = GetProgress(def.Id);
            bool knownPermanent = generatedKnown || permanent;
            if (!knownPermanent && !string.IsNullOrWhiteSpace(interaction.RequiredQuestStatus) && !string.Equals(progress.Status.ToString(), interaction.RequiredQuestStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (!knownPermanent && !string.IsNullOrWhiteSpace(interaction.RequiredQuestStep) && !string.Equals(progress.Step, interaction.RequiredQuestStep, StringComparison.OrdinalIgnoreCase)) return false;
            if (!_resolver.TryResolve(def, interaction, out QuestResolvedPoint coord)) return false;

            string marker = interaction.DefaultMarker;
            if (progress.Status == QuestStatus.Active) marker = interaction.ActiveMarker;
            else if (progress.Status == QuestStatus.Completed) marker = interaction.CompletedMarker;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.MarkerByStep.TryGetValue(progress.Step, out string? stepMarker)) marker = stepMarker;

            bool minimapVisible = interaction.MinimapVisible;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.MinimapVisibleByStep.TryGetValue(progress.Step, out bool mapStep)) minimapVisible = mapStep;
            bool arVisible = interaction.ArVisible;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.ArVisibleByStep.TryGetValue(progress.Step, out bool arStep)) arVisible = arStep;

            string name = GetPermanentName(def.Id, interaction.Id, interaction.Name);
            if (progress.Status == QuestStatus.Completed && !string.IsNullOrWhiteSpace(interaction.CompletedName)) name = interaction.CompletedName;
            ApplyEditorOverride(key, ref coord, ref name, ref minimapVisible, ref arVisible);

            if (progress.Status == QuestStatus.Completed && !interaction.ShowWhenQuestCompleted && !generatedKnown && !permanent) return false;
            bool visible = minimapVisible || (arVisible && marker != "none") || interaction.PermanentName || knownPermanent;
            if (!visible) return false;

            point = new QuestPointSnapshot
            {
                QuestId = def.Id, InteractionId = interaction.Id, Uid = coord.Uid, OriginalUid = coord.OriginalUid, Category = coord.Category, Name = name,
                Marker = marker, X = coord.X, Y = coord.Y, Z = coord.Z, MinimapVisible = minimapVisible,
                ArVisible = arVisible && marker != "none", Interactive = marker != "none" || interaction.TriggerWhenNoMarker,
                PermanentName = interaction.PermanentName || knownPermanent, IsGenerated = coord.IsGenerated,
                ArOffscreenPointer = interaction.ArOffscreenPointer && marker != "none", TriggerWhenNoMarker = interaction.TriggerWhenNoMarker,
                TriggerRadiusM = interaction.TriggerRadiusM > 0 ? interaction.TriggerRadiusM : _store.Settings.TriggerRadiusM
            };
            return true;
        }

        private bool TryBuildEditorInteraction(QuestDefinition def, QuestInteractionDefinition interaction, out QuestPointSnapshot point)
        {
            point = new QuestPointSnapshot();
            string key = def.Id + ":" + interaction.Id;
            if (!_resolver.TryResolve(def, interaction, out QuestResolvedPoint coord)) return false;
            var progress = GetProgress(def.Id);

            string marker = interaction.DefaultMarker;
            if (progress.Status == QuestStatus.Active) marker = interaction.ActiveMarker;
            else if (progress.Status == QuestStatus.Completed) marker = interaction.CompletedMarker;
            else if (progress.ReturnOffer && !string.IsNullOrWhiteSpace(interaction.CancelledDialogue)) marker = interaction.DefaultMarker;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.MarkerByStep.TryGetValue(progress.Step, out string? stepMarker)) marker = stepMarker;

            bool minimapVisible = interaction.MinimapVisible;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.MinimapVisibleByStep.TryGetValue(progress.Step, out bool mapStep)) minimapVisible = mapStep;
            bool arVisible = interaction.ArVisible;
            if (!string.IsNullOrWhiteSpace(progress.Step) && interaction.ArVisibleByStep.TryGetValue(progress.Step, out bool arStep)) arVisible = arStep;

            string name = GetPermanentName(def.Id, interaction.Id, interaction.Name);
            if (progress.Status == QuestStatus.Completed && !string.IsNullOrWhiteSpace(interaction.CompletedName)) name = interaction.CompletedName;
            ApplyEditorOverride(key, ref coord, ref name, ref minimapVisible, ref arVisible);

            point = new QuestPointSnapshot
            {
                QuestId = def.Id,
                InteractionId = interaction.Id,
                Uid = coord.Uid,
                OriginalUid = coord.OriginalUid,
                Category = coord.Category,
                Name = name,
                Marker = marker,
                X = coord.X,
                Y = coord.Y,
                Z = coord.Z,
                MinimapVisible = minimapVisible,
                ArVisible = arVisible && marker != "none",
                Interactive = true,
                PermanentName = interaction.PermanentName,
                IsGenerated = coord.IsGenerated,
                ArOffscreenPointer = interaction.ArOffscreenPointer && marker != "none",
                TriggerWhenNoMarker = interaction.TriggerWhenNoMarker,
                TriggerRadiusM = interaction.TriggerRadiusM > 0 ? interaction.TriggerRadiusM : _store.Settings.TriggerRadiusM
            };
            return true;
        }

        private string GetPermanentName(string questId, string interactionId, string fallback) =>
            _store.State.PermanentInteractionNames.TryGetValue(questId + ":" + interactionId, out string? name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;

        private QuestProgress GetProgress(string id)
        {
            if (!_store.State.Quests.TryGetValue(id, out QuestProgress? progress) || progress == null)
            { progress = new QuestProgress(); _store.State.Quests[id] = progress; }
            progress.Flags ??= new(StringComparer.OrdinalIgnoreCase); return progress;
        }

        private bool EvaluateRequirement(QuestRequirement req)
        {
            if (req.All.Any(c => !EvaluateCondition(c))) return false;
            if (req.Any.Count > 0 && !req.Any.Any(EvaluateCondition)) return false;
            if (req.None.Any(EvaluateCondition)) return false;
            return true;
        }

        private bool EvaluateCondition(QuestCondition c)
        {
            if (!string.IsNullOrWhiteSpace(c.QuestId))
            {
                QuestProgress p = GetProgress(c.QuestId);
                if (!string.IsNullOrWhiteSpace(c.QuestStatus) && !string.Equals(p.Status.ToString(), c.QuestStatus, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(c.QuestStep) && !string.Equals(p.Step, c.QuestStep, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (!string.IsNullOrWhiteSpace(c.Item) && Amount(_store.State.Inventory, c.Item) < Math.Max(1, c.Amount)) return false;
            if (!string.IsNullOrWhiteSpace(c.Reputation) && Amount(_store.State.Reputation, c.Reputation) < c.MinValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Stat) && Amount(_store.State.Stats, c.Stat) < c.MinStatValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Flag))
            {
                bool value = false;
                if (!string.IsNullOrWhiteSpace(c.QuestId) && _store.State.Quests.TryGetValue(c.QuestId, out QuestProgress? qp)) qp.Flags.TryGetValue(c.Flag, out value);
                else value = _store.State.Quests.Values.Any(p => p.Flags.TryGetValue(c.Flag, out bool v) && v);
                if (value != c.FlagValue) return false;
            }
            return true;
        }

        private void OnQuestClientOpen(QuestSocketBehavior client)
        {
            var payload = BuildStatePayload();
            LogQuestStateWire("client-open", payload, true);
            client.SendJson(payload);
        }

        /* Shared 8084 fallback for commands originating from the interactive quest page.
           The normal quest-state socket remains 8085; critical user actions are also
           accepted here so they cannot be lost when the page's state WS is transient. */
        internal void HandleSharedChannelCommand(JObject data)
        {            string command = data["command"]?.Value<string>() ?? "";
            Logger.Current?.Workflow(
                $"[QUEST-DIAG][WS8084-IN] command={command} paused={_paused} interactive={_interactiveVisible} raw={data.ToString(Formatting.None)}");
            switch (command)
            {
                case "quest_state_request":
                    BeginInvokeUi(() => BroadcastState(true));
                    break;
                case "quest_select_interaction":
                    BeginInvokeUi(() => SelectInteraction(
                        data["questId"]?.Value<string>() ?? "",
                        data["id"]?.Value<string>() ?? ""));
                    break;
                case "quest_dialogue_start":
                    BeginInvokeUi(() => StartDialogue(
                        data["questId"]?.Value<string>() ?? "",
                        data["id"]?.Value<string>() ?? ""));
                    break;
                case "quest_dialogue_end":
                    BeginInvokeUi(EndDialogue);
                    break;
                case "quest_clear_selection":
                    BeginInvokeUi(ClearSelection);
                    break;
                case "quest_dialog_option":
                    BeginInvokeUi(() => ApplyDialogOption(
                        data["questId"]?.Value<string>() ?? "",
                        data["interaction"]?.Value<string>() ?? "",
                        data["index"]?.Value<int>() ?? -1));
                    break;
                case "inventory_item_seen":
                    BeginInvokeUi(() => MarkInventoryItemSeen(data["id"]?.Value<string>() ?? ""));
                    break;
            }
        }

        private void OnQuestMessage(JObject data)
        {
            string command = data["command"]?.Value<string>() ?? "";
            // ДИАГНОСТИКА ВВОДА (временно): отделяем «мышь не приходит» от
            // «мышь приходит, но action не доходит до C#». Поведение не меняется.
            Logger.Current?.Workflow(
                $"[QUEST-DIAG][WS-IN] command={command} paused={_paused} " +
                $"clients={GetQuestClientCount()} rawBytes={Encoding.UTF8.GetByteCount(data.ToString(Formatting.None))} " +
                $"raw={data.ToString(Formatting.None)}");
            switch (command)
            {
                case "quest_state_request":
                    BroadcastState(true);
                    break;
                case "quest_select_interaction": BeginInvokeUi(() => SelectInteraction(data["questId"]?.Value<string>() ?? "", data["id"]?.Value<string>() ?? "")); break;
                case "quest_dialogue_start": BeginInvokeUi(() => StartDialogue(data["questId"]?.Value<string>() ?? "", data["id"]?.Value<string>() ?? "")); break;
                case "quest_dialogue_end": BeginInvokeUi(EndDialogue); break;
                case "quest_clear_selection": BeginInvokeUi(ClearSelection); break;
                case "quest_window_state": BeginInvokeUi(() => _host.OnQuestWindowCollapsedChanged(data["collapsed"]?.Value<bool>() ?? false)); break;
                case "quest_dialog_option": BeginInvokeUi(() => ApplyDialogOption(data["questId"]?.Value<string>() ?? "", data["interaction"]?.Value<string>() ?? "", data["index"]?.Value<int>() ?? -1)); break;
                case "inventory_item_seen": BeginInvokeUi(() => MarkInventoryItemSeen(data["id"]?.Value<string>() ?? "")); break;
                case "quest_editor_point_save": BeginInvokeUi(() => SaveEditorPoint(data)); break;
                case "quest_reset": BeginInvokeUi(() => { _store.ResetQuest(data["id"]?.Value<string>() ?? "special_marinated_shashlik", true); _inside.Clear(); _activeDialogue.Clear(); _nearby.Clear(); ForceArRebuild(); BroadcastState(true); }); break;
            }
        }

        private void SaveEditorPoint(JObject data)
        {
            string questId = data["questId"]?.Value<string>()?.Trim() ?? "";
            string interactionId = data["interactionId"]?.Value<string>()?.Trim() ?? "";
            if (!_store.Definitions.TryGetValue(questId, out QuestDefinition? def)) { SendError("Квестовая точка: квест не найден."); return; }
            QuestInteractionDefinition? interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null) { SendError("Квестовая точка: взаимодействие не найдено."); return; }

            double x = data["x"]?.Value<double>() ?? double.NaN;
            double y = data["y"]?.Value<double>() ?? double.NaN;
            double z = data["z"]?.Value<double>() ?? double.NaN;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) { SendError("Квестовая точка: некорректные координаты."); return; }
            if (!_resolver.TryResolve(def, interaction, out _)) { SendError("Квестовая точка: исходная точка не разрешилась."); return; }

            string name = data["name"]?.Value<string>()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) name = interaction.Name;
            bool minimapVisible = data["minimapVisible"]?.Value<bool>() ?? true;
            bool arVisible = data["arVisible"]?.Value<bool>() ?? true;
            string key = questId + ":" + interactionId;
            _store.State.EditorPointOverrides[key] = new QuestEditorPointOverride
            {
                Name = name,
                X = x,
                Y = y,
                Z = z,
                MinimapVisible = minimapVisible,
                ArVisible = arVisible
            };
            _store.SaveState();
            ForceArRebuild();
            BroadcastState(true);
            Broadcast(new JObject { ["command"] = "quest_editor_point_saved", ["questId"] = questId, ["interactionId"] = interactionId });
        }

        private void SelectInteraction(string questId, string interactionId)
        {
            Logger.Current?.Workflow($"[QUEST-DIAG][WS-IN] select_interaction questId={questId} id={interactionId} paused={_paused}");
            if (!_paused) { SendError("Интерактив доступен только на паузе игры."); return; }
            if (!_store.Definitions.TryGetValue(questId, out QuestDefinition? def)) return;
            QuestInteractionDefinition? interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null || !TryBuildInteraction(def, interaction, out _)) return;

            /* Выбор интерактива в сайдбаре = КАРТОЧКА КВЕСТА.
               Диалог НЕ начинается: игрок видит описание, служебную информацию и
               награды, а реплики НПЦ и варианты ответа грузятся только после
               нажатия кнопки инициации (quest_dialogue_start). Поэтому
               _activeDialogue очищается — иначе карточка сразу показала бы
               прошлую реплику. */
            string key = questId + ":" + interactionId;
            _selectedQuestId = questId;
            _selectedInteractionId = interactionId;
            _activeDialogue.Remove(key);
            SendQuestState("select-interaction");
        }

        /* Инициация события: кнопка «Поговорить» (текст из actionName квеста).
           Только здесь загружается входная реплика НПЦ и варианты ответа. */
        private void StartDialogue(string questId, string interactionId)
        {
            Logger.Current?.Workflow($"[QUEST-DIAG][WS-IN] dialogue_start questId={questId} id={interactionId} paused={_paused} interactive={_interactiveVisible}");
            if (!_paused) { SendError("Интерактив доступен только на паузе игры."); return; }
            if (!_store.Definitions.TryGetValue(questId, out QuestDefinition? def)) return;
            QuestInteractionDefinition? interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null || !TryBuildInteraction(def, interaction, out _)) return;

            string key = questId + ":" + interactionId;
            _selectedQuestId = questId;
            _selectedInteractionId = interactionId;
            string entryDialogue = ResolveEntryDialogue(def, interaction);
            if (!string.IsNullOrWhiteSpace(entryDialogue)) _activeDialogue[key] = entryDialogue;
            else _activeDialogue.Remove(key);
            SendQuestState("dialogue-start");
        }

        /* Выход из диалога обратно к карточке квеста (кнопка «Отмена» в диалоге).
           Выделение СОХРАНЯЕТСЯ, сбрасывается только активная реплика — иначе
           следующий broadcast вернул бы диалог обратно. */
        private void EndDialogue()
        {
            Logger.Current?.Workflow($"[QUEST-DIAG][WS-IN] dialogue_end quest={_selectedQuestId}/{_selectedInteractionId}");
            _activeDialogue.Clear();
            SendQuestState("dialogue-end");
        }

        /* Снятие выделения: кнопка «Назад» (или «Отмена» в карточке квеста).
           Без явной команды периодический BroadcastState возвращал бы квест
           обратно в UI сразу после локальной очистки. */
        private void ClearSelection()
        {
            Logger.Current?.Workflow($"[QUEST-DIAG][WS-IN] clear_selection quest={_selectedQuestId}/{_selectedInteractionId}");
            _selectedQuestId = "";
            _selectedInteractionId = "";
            _activeDialogue.Clear();
            SendQuestState("clear-selection");
        }

        /* Единая отправка состояния: broadcast всем клиентам + прямая доставка
           оверлею. Раньше эти два вызова дублировались в каждом действии. */
        private void SendQuestState(string reason)
        {
            BroadcastState(true);
            try { _host.PushQuestStateToOverlay(BuildStatePayload(), reason); } catch { }
        }

        private string ResolveEntryDialogue(QuestDefinition def, QuestInteractionDefinition interaction)
        {
            QuestProgress progress = GetProgress(def.Id);
            if (progress.ReturnOffer && !string.IsNullOrWhiteSpace(interaction.CancelledDialogue)) return interaction.CancelledDialogue;
            if (progress.Status == QuestStatus.Active && !string.IsNullOrWhiteSpace(interaction.ActiveDialogue)) return interaction.ActiveDialogue;
            if (progress.Status == QuestStatus.Completed && !string.IsNullOrWhiteSpace(interaction.CompletedDialogue)) return interaction.CompletedDialogue;
            return interaction.InitialDialogue;
        }

        private void ApplyDialogOption(string questId, string interactionId, int index)
        {
            Logger.Current?.Workflow($"[QUEST-DIAG][WS-IN] dialog_option questId={questId} interaction={interactionId} index={index} paused={_paused}");
            if (!_paused || !_interactiveVisible) { SendError("Взаимодействие разрешено только в подтверждённом окне ESC-паузы."); return; }
            if (!_store.Definitions.TryGetValue(questId, out QuestDefinition? def)) return;
            QuestInteractionDefinition? interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null || !TryBuildInteraction(def, interaction, out _)) return;
            string key = questId + ":" + interactionId;
            string current = _activeDialogue.TryGetValue(key, out string? d) && !string.IsNullOrWhiteSpace(d)
                ? d : ResolveEntryDialogue(def, interaction);
            if (!def.Dialogues.TryGetValue(current, out QuestDialogueNode? node) || index < 0 || index >= node.Options.Count) return;
            QuestDialogueOption option = node.Options[index];
            if (option.Requirements != null && !EvaluateRequirement(option.Requirements)) { SendError("Условия варианта не выполнены."); return; }
            ApplyEffects(def, option.Effects);
            if (!string.IsNullOrWhiteSpace(option.Next)) _activeDialogue[key] = option.Next; else _activeDialogue.Remove(key);
            if (option.Close)
            {
                _selectedQuestId = "";
                _selectedInteractionId = "";
            }
            else
            {
                _selectedQuestId = questId;
                _selectedInteractionId = interactionId;
            }
            ForceArRebuild();
            SendQuestState("dialog-option");
        }

        private void ApplyEffects(QuestDefinition def, IEnumerable<QuestEffect> effects)
        {
            QuestProgress progress = GetProgress(def.Id);
            foreach (QuestEffect effect in effects)
            {
                if (!string.IsNullOrWhiteSpace(effect.SetQuestStatus) && Enum.TryParse(effect.SetQuestStatus, true, out QuestStatus status))
                {
                    QuestStatus old = progress.Status; progress.Status = status; progress.ChangedUtc = DateTime.UtcNow;
                    if (status == QuestStatus.Active && old != QuestStatus.Active) { IncrementActivation(def.Id); ApplyQuestResets(def); }
                    if (status == QuestStatus.Completed && old != QuestStatus.Completed) GrantRewards(def);
                    if (status == QuestStatus.Available && old == QuestStatus.Active) { progress.ReturnOffer = true; progress.Flags["returnOffer"] = true; }
                }
                if (!string.IsNullOrWhiteSpace(effect.SetStep)) progress.Step = effect.SetStep;
                if (!string.IsNullOrWhiteSpace(effect.Item)) { if (effect.AddItem != 0) AddInventoryAmount(effect.Item, effect.AddItem); if (effect.RemoveItem != 0) AddInventoryAmount(effect.Item, -effect.RemoveItem); }
                if (!string.IsNullOrWhiteSpace(effect.Reputation) && effect.AddReputation != 0) AddAmount(_store.State.Reputation, effect.Reputation, effect.AddReputation);
                if (!string.IsNullOrWhiteSpace(effect.Stat) && effect.AddStat != 0) AddAmount(_store.State.Stats, effect.Stat, effect.AddStat);
                if (!string.IsNullOrWhiteSpace(effect.Flag)) { progress.Flags[effect.Flag] = effect.SetFlag ?? false; if (effect.Flag.Equals("returnOffer", StringComparison.OrdinalIgnoreCase)) progress.ReturnOffer = effect.SetFlag ?? false; }
                if (!string.IsNullOrWhiteSpace(effect.RenameInteraction) && !string.IsNullOrWhiteSpace(effect.RenameTo))
                {
                    string renameKey = def.Id + ":" + effect.RenameInteraction; _store.State.PermanentInteractionNames[renameKey] = effect.RenameTo;
                    if (_store.State.GeneratedPoints.TryGetValue(renameKey, out QuestGeneratedPoint? generatedPoint) && generatedPoint != null) { generatedPoint.Known = true; generatedPoint.Name = effect.RenameTo; }
                }
                foreach (string resetId in effect.ResetQuests ?? new List<string>()) _store.ResetQuestStateOnly(resetId);
                if (!string.IsNullOrWhiteSpace(effect.NotifyText)) Broadcast(new JObject { ["command"]="quest_notify", ["notification"]=new JObject { ["title"]=effect.NotifyTitle ?? "", ["text"]=effect.NotifyText, ["icon"]="", ["position"]=(effect.NotifyPosition ?? "top") } });
            }
            _store.SaveState();
        }

        private void ApplyQuestResets(QuestDefinition def)
        {
            foreach (string id in def.ResetQuests ?? new List<string>()) if (!string.Equals(id, def.Id, StringComparison.OrdinalIgnoreCase)) _store.ResetQuestStateOnly(id);
        }

        private void GrantRewards(QuestDefinition def)
        {
            QuestProgress p = GetProgress(def.Id); if (p.Flags.TryGetValue("__rewardsGranted", out bool granted) && granted) return;
            var rewardItems = new JArray(); var repLines = new List<string>();
            foreach (QuestReward reward in def.Rewards)
            {
                if (reward.Type.Equals("item", StringComparison.OrdinalIgnoreCase))
                {
                    AddInventoryAmount(reward.Id, reward.Amount);
                    rewardItems.Add(new JObject { ["display"]=reward.Display, ["amount"]=reward.Amount, ["color"]=reward.Color ?? "" });
                }
                else if (reward.Type.Equals("reputation", StringComparison.OrdinalIgnoreCase)) { AddAmount(_store.State.Reputation, reward.Id, reward.Amount); repLines.Add($"{reward.Id}: +{reward.Amount}"); }
                else if (reward.Type.Equals("stat", StringComparison.OrdinalIgnoreCase)) AddAmount(_store.State.Stats, reward.Id, reward.Amount);
            }
            p.Flags["__rewardsGranted"] = true;
            Broadcast(new JObject { ["command"]="quest_notify", ["notification"] = new JObject { ["title"]="", ["text"]="Предмет отдан:\nМясо в спецмаринаде x1", ["icon"]="", ["rewardItems"]=rewardItems } });
            if (repLines.Count > 0) Broadcast(new JObject { ["command"]="quest_notify", ["notification"] = new JObject { ["title"]="Получена репутация:", ["text"]=string.Join("\n", repLines), ["icon"]="", ["accent"]="reputation" } });
        }

        private JObject BuildStatePayload(string? selectedQuest = null, string? selectedInteraction = null, string? explicitDialogue = null)
        {
            var points = new JArray();
            foreach (QuestDefinition def in _store.Definitions.Values)
                foreach (QuestInteractionDefinition interaction in def.Interactions)
                    if (TryBuildInteraction(def, interaction, out QuestPointSnapshot point))
                        points.Add(JObject.FromObject(point));

            var editorPoints = new JArray();
            foreach (QuestDefinition def in _store.Definitions.Values)
                foreach (QuestInteractionDefinition interaction in def.Interactions)
                    if (TryBuildEditorInteraction(def, interaction, out QuestPointSnapshot point))
                        editorPoints.Add(JObject.FromObject(point));

            var available = new JArray();
            var active = new JArray();
            var archive = new JArray();

            foreach (QuestDefinition def in _store.Definitions.Values)
            {
                QuestProgress p = GetProgress(def.Id);
                var q = new JObject
                {
                    ["id"] = def.Id,
                    ["title"] = def.Title,
                    ["description"] = def.Description,
                    ["status"] = p.Status.ToString(),
                    ["step"] = p.Step,
                    ["stepDescription"] = def.Steps.TryGetValue(p.Step ?? "", out QuestStepDefinition? step) ? step.Description : "",
                    /* Текст кнопки инициации события. Пользователь задаёт его в
                       свойствах квеста; по умолчанию — «Поговорить». */
                    ["actionName"] = string.IsNullOrWhiteSpace(def.ActionName) ? "Поговорить" : def.ActionName,
                    /* ВАЖНО: весь остальной quest_state отдаёт camelCase, а
                       JArray.FromObject(def.Rewards) сериализовал модель
                       PascalCase (Type/Id/Amount/Display/…). web_quests.html
                       читает r.display/r.amount, поэтому награды приходили
                       пустыми («×1» и «—»). Строим объекты вручную. */
                    ["rewards"] = BuildRewardsPayload(def.Rewards),
                    ["returnOffer"] = p.ReturnOffer
                };

                if (p.Status == QuestStatus.Available && IsQuestAvailable(def))
                    available.Add(q);
                else if (p.Status == QuestStatus.Active)
                    active.Add(q);
                else if (p.Status != QuestStatus.Available)
                    archive.Add(q);
            }

            var inventory = new JArray();
            foreach (var item in _store.State.Inventory)
            {
                inventory.Add(new JObject
                {
                    ["id"] = item.Key,
                    ["name"] = DisplayItemName(item.Key),
                    ["amount"] = item.Value,
                    ["new_item"] = _store.State.NewItems.TryGetValue(item.Key, out bool isNew) && isNew
                });
            }

            // Тестовые предметы только для UI: сохранённое состояние игры не меняем.
            if (!inventory.Any(x => x["id"]?.Value<string>() == "driver_license"))
                inventory.Add(new JObject { ["id"] = "driver_license", ["name"] = "Водительские права", ["amount"] = 1, ["test"] = true, ["new_item"] = false });
            if (!inventory.Any(x => x["id"]?.Value<string>() == "pts"))
                inventory.Add(new JObject { ["id"] = "pts", ["name"] = "ПТС", ["amount"] = 1, ["test"] = true, ["new_item"] = true });

            var nearby = new JArray();
            foreach (var p in _nearby)
            {
                nearby.Add(new JObject
                {
                    ["QuestId"] = p.QuestId,
                    ["InteractionId"] = p.InteractionId,
                    ["Name"] = p.Name,
                    ["Marker"] = p.Marker,
                    ["ArVisible"] = p.ArVisible,
                    ["ArOffscreenPointer"] = p.ArOffscreenPointer,
                    ["distance"] = Math.Sqrt(DistanceSquared(p.X, p.Y, p.Z, _lastTruckX, _lastTruckY, _lastTruckZ))
                });
            }

            var payload = new JObject
            {
                ["command"] = "quest_state",
                ["paused"] = _paused,
                ["interactive"] = _interactiveVisible,
                ["enabled"] = _store.Settings.Enabled,
                ["points"] = points,
                ["editorPoints"] = editorPoints,
                ["nearby"] = nearby,
                ["availableQuests"] = available,
                ["activeQuests"] = active,
                ["archiveQuests"] = archive,
                ["inventory"] = inventory,
                ["settings"] = JObject.FromObject(_store.Settings)
            };

            string? sq = string.IsNullOrWhiteSpace(selectedQuest) ? _selectedQuestId : selectedQuest;
            string? si = string.IsNullOrWhiteSpace(selectedInteraction) ? _selectedInteractionId : selectedInteraction;

            /* selectedQuest/selectedInteraction — часть СОСТОЯНИЯ выбора, а не
               побочный эффект наличия dialogue. Раньше после SelectInteraction
               следующий периодический BroadcastState не находил _activeDialogue
               и поэтому вообще не отправлял выбранные ID; UI принимал такой
               пакет за сброс выделения примерно через один тик (~1 с). */
            payload["selectedQuest"] = sq ?? "";
            payload["selectedInteraction"] = si ?? "";

            if (!string.IsNullOrWhiteSpace(sq) && !string.IsNullOrWhiteSpace(si))
            {
                if (explicitDialogue == null)
                    _activeDialogue.TryGetValue(sq + ":" + si, out explicitDialogue);

                if (!string.IsNullOrWhiteSpace(explicitDialogue) &&
                    _store.Definitions.TryGetValue(sq, out QuestDefinition? def) &&
                    def.Dialogues.TryGetValue(explicitDialogue, out QuestDialogueNode? node))
                {
                    payload["dialogue"] = BuildDialoguePayload(def, node);
                }
            }

            return payload;
        }

        /* Награды квеста для UI. Отдаём camelCase-поля, потому что страница
           интерактива читает r.display/r.amount/r.serviceText/r.color. */
        private static JArray BuildRewardsPayload(List<QuestReward> rewards)
        {
            var list = new JArray();
            foreach (QuestReward r in rewards ?? (IEnumerable<QuestReward>)Array.Empty<QuestReward>())
            {
                list.Add(new JObject
                {
                    ["type"] = r.Type ?? "",
                    ["id"] = r.Id ?? "",
                    ["amount"] = r.Amount,
                    ["display"] = r.Display ?? "",
                    ["serviceText"] = r.ServiceText ?? "",
                    ["color"] = r.Color ?? ""
                });
            }
            return list;
        }

        private JObject BuildDialoguePayload(QuestDefinition def, QuestDialogueNode node)
        {
            var options=new JArray();
            foreach(QuestDialogueOption option in node.Options)
            {
                bool enabled=option.Requirements==null||EvaluateRequirement(option.Requirements);
                string requirementText=DescribeRequirements(option.Requirements);
                options.Add(new JObject
                {
                    ["text"]=option.Text,
                    ["serviceText"]=option.ServiceText ?? "",
                    ["enabled"]=enabled,
                    ["close"]=option.Close,
                    ["requirements"]=requirementText,
                    ["requirementsMet"]=enabled,
                    ["reason"]=enabled?"":"Условие не выполнено"
                });
            }
            return new JObject { ["speaker"]=node.Speaker,["text"]=node.Text,["serviceText"]=node.ServiceText ?? "",["image"]=node.Image,["options"]=options };
        }

        private string DescribeRequirements(QuestRequirement? req)
        {
            if(req==null)return "";
            var parts=new List<string>();
            foreach(QuestCondition c in req.All)
            {
                if(!string.IsNullOrWhiteSpace(c.Stat))parts.Add($"[{c.Stat} {c.MinStatValue}]");
                else if(!string.IsNullOrWhiteSpace(c.Item))parts.Add($"[требуется: {DisplayItemName(c.Item)} x{Math.Max(1,c.Amount)}]");
                else if(!string.IsNullOrWhiteSpace(c.Reputation))parts.Add($"[репутация {c.Reputation} {c.MinValue}]");
                else if(!string.IsNullOrWhiteSpace(c.QuestId))parts.Add($"[квест {c.QuestId}{(string.IsNullOrWhiteSpace(c.QuestStep)?"":" / "+c.QuestStep)}]");
            }
            return string.Join(" ",parts);
        }

        private string DisplayItemName(string id) => id switch
        {
            "special_marinade_meat" => "Мясо в спецмаринаде",
            "legendary_shashlik" => "Легендарный шашлык от Руслана",
            "driver_license" => "Водительские права",
            "pts" => "ПТС",
            _ => id
        };

        internal void SetHostPauseState(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            BroadcastState(true);
        }

        internal void SetInteractiveVisible(bool visible, bool resetSelection)
        {
            _interactiveVisible = visible;
            if (resetSelection)
            {
                _selectedQuestId = "";
                _selectedInteractionId = "";
                _activeDialogue.Clear();
            }
            BroadcastState(true);
            if (visible)
            {
                try { _host.PushQuestStateToOverlay(BuildStatePayload(), "interactive-visible"); } catch { }
            }
        }
        private void BroadcastState(bool force,string? selectedQuest=null,string? selectedInteraction=null,string? explicitDialogue=null)
        {
            JObject payload=BuildStatePayload(selectedQuest,selectedInteraction,explicitDialogue);
            if(!force&&JToken.DeepEquals(payload,_lastState))return;
            _lastState=payload;
            _lastStateSentUtc=DateTime.UtcNow;
            LogQuestStateWire(force ? "broadcast-force" : "broadcast-change", payload, force);
            Broadcast(payload);
        }

        private void Broadcast(JObject payload)
        {
            string json = payload.ToString(Formatting.None);
            try { _server?.WebSocketServices["/"]?.Sessions.Broadcast(json); }
            catch(Exception ex)
            {
                Logger.Current?.Data($"[QUEST][DIAG][WS-OUT] broadcast error={ex.Message}");
            }
        }

        private int GetQuestClientCount()
        {
            try { return _server?.WebSocketServices["/"]?.Sessions.Count ?? 0; }
            catch { return -1; }
        }

        private void LogQuestStateWire(string reason, JObject payload, bool force)
        {
            try
            {
                string key =
                    (string?)payload["paused"] + "|" +
                    (string?)payload["interactive"] + "|" +
                    ((payload["availableQuests"] as JArray)?.Count ?? 0) + "|" +
                    ((payload["activeQuests"] as JArray)?.Count ?? 0) + "|" +
                    ((payload["archiveQuests"] as JArray)?.Count ?? 0) + "|" +
                    ((payload["inventory"] as JArray)?.Count ?? 0) + "|" +
                    ((payload["nearby"] as JArray)?.Count ?? 0) + "|" +
                    (string?)payload["selectedQuest"] + "|" +
                    (string?)payload["selectedInteraction"] + "|" +
                    (payload["dialogue"] == null ? "0" : "1");
                var now = DateTime.UtcNow;
                if(key==_lastWsDiagKey && now-_lastWsDiagUtc < TimeSpan.FromMilliseconds(1500))
                    return;
                _lastWsDiagKey=key;
                _lastWsDiagUtc=now;
                string json=payload.ToString(Formatting.None);
                var dialogue=payload["dialogue"] as JObject;
                int dialogueOptions=(dialogue?["options"] as JArray)?.Count ?? 0;
                Logger.Current?.Workflow(
                    $"[QUEST][DIAG][WS-OUT] reason={reason} clients={GetQuestClientCount()} " +
                    $"paused={(bool?)payload["paused"] ?? false} interactive={(bool?)payload["interactive"] ?? false} " +
                    $"counts=available:{((payload["availableQuests"] as JArray)?.Count ?? 0)}," +
                    $"active:{((payload["activeQuests"] as JArray)?.Count ?? 0)}," +
                    $"archive:{((payload["archiveQuests"] as JArray)?.Count ?? 0)}," +
                    $"inventory:{((payload["inventory"] as JArray)?.Count ?? 0)}," +
                    $"nearby:{((payload["nearby"] as JArray)?.Count ?? 0)} " +
                    $"points={((payload["points"] as JArray)?.Count ?? 0)} " +
                    $"selected={(string?)payload["selectedQuest"] ?? ""}/{(string?)payload["selectedInteraction"] ?? ""} " +
                    $"dialogue={(dialogue!=null)} dialogueOptions={dialogueOptions} " +
                    $"jsonBytes={Encoding.UTF8.GetByteCount(json)} force={force}");
            }
            catch(Exception ex)
            {
                Logger.Current?.Data($"[QUEST][DIAG][WS-OUT] diagnostics error={ex.Message}");
            }
        }
        private void SendError(string text)=>Broadcast(new JObject { ["command"]="quest_error",["text"]=text });

        private Task UpdateOverlayAsync()
        {
            return Task.CompletedTask;
        }
        private void EnforceArPointDebugMode(){try{bool debug=_store.Settings.DebugShowAllPoints;int radius=(int)Math.Clamp(_store.Settings.DebugRadiusM,5,5000);if(_lastDebugShow.HasValue&&_lastDebugShow.Value==debug&&_lastDebugRadius==radius)return;_lastDebugShow=debug;_lastDebugRadius=radius;if(debug){AppSettings.ArDisplayRadiusM=radius;}else if(AppSettings.ArDisplayRadiusM<5){AppSettings.ArDisplayRadiusM=50;}if(_host.IsHandleCreated)_host.BeginInvoke(new Action(ForceArRebuild));}catch{}}
        private void ForceArRebuild(){try{typeof(MainForm).GetMethod("RefreshArModel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.Invoke(_host,null);_host.ForceArDataResend("quest-state-change");_resolver.ClearCaches();}catch{}}
        internal IReadOnlyList<QuestPointSnapshot> GetQuestPointsForEditor(){var result=new List<QuestPointSnapshot>();foreach(QuestDefinition def in _store.Definitions.Values)foreach(QuestInteractionDefinition interaction in def.Interactions)if(TryBuildEditorInteraction(def,interaction,out QuestPointSnapshot point))result.Add(point);return result;}
        private void ProcessInstantActivations(){if(!_store.Settings.Enabled)return;foreach(QuestDefinition def in _store.Definitions.Values){if(!def.InstantActivation)continue;QuestProgress p=GetProgress(def.Id);if(p.Status!=QuestStatus.Available||(def.ActivationsPerPlayer>0&&ActivationCount(def.Id)>=def.ActivationsPerPlayer)||!IsQuestAvailable(def))continue;string firstStep=def.Steps.Keys.FirstOrDefault(k=>!k.Equals("available",StringComparison.OrdinalIgnoreCase))??"active";ApplyEffects(def,new[]{new QuestEffect{SetQuestStatus="Active",SetStep=firstStep}});}}

        private bool ScheduleAllows(QuestDefinition def)
        {
            QuestScheduleDefinition? s=def.Schedule;if(s==null||!s.Enabled)return true;DateTime now=DateTime.Now;
            if(s.TimeAvailable.Count>0&&!s.TimeAvailable.Any(v=>TimeInRange(now.TimeOfDay,v)))return false;
            if(s.WeekAvailable.Count>0&&!WeekMatches(now.DayOfWeek,s.WeekAvailable))return false;
            if(s.DateAvailable.Count>0&&!DateMatches(now.Date,s.DateAvailable))return false;
            return true;
        }
        private static bool TimeInRange(TimeSpan now,string expression)
        {
            string[] parts=expression.Split('-','…'); if(parts.Length!=2)parts=expression.Split(new[]{".."},StringSplitOptions.None);
            if(parts.Length!=2||!TimeSpan.TryParse(parts[0].Trim(),CultureInfo.InvariantCulture,out TimeSpan from)||!TimeSpan.TryParse(parts[1].Trim(),CultureInfo.InvariantCulture,out TimeSpan to))return false;
            return from<=to?now>=from&&now<=to:now>=from||now<=to;
        }
        private static bool WeekMatches(DayOfWeek day,IEnumerable<string> values)
        {
            int numeric=(int)day;string shortName=day.ToString()[..3];string ruShort=day switch{DayOfWeek.Monday=>"пн",DayOfWeek.Tuesday=>"вт",DayOfWeek.Wednesday=>"ср",DayOfWeek.Thursday=>"чт",DayOfWeek.Friday=>"пт",DayOfWeek.Saturday=>"сб",_=>"вс"};
            foreach(string raw in values){string v=raw.Trim();if(v.Contains('-')){string[] r=v.Split('-',2);if(int.TryParse(r[0],out int a)&&int.TryParse(r[1],out int b)&&a<=numeric&&numeric<=b)return true;}if(v.Equals(numeric.ToString(),StringComparison.OrdinalIgnoreCase)||v.Equals(shortName,StringComparison.OrdinalIgnoreCase)||v.Equals(ruShort,StringComparison.OrdinalIgnoreCase)||v.StartsWith(day.ToString(),StringComparison.OrdinalIgnoreCase))return true;}return false;
        }
        private static bool DateMatches(DateTime date,IEnumerable<string> values)
        {
            string iso=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),ru=date.ToString("dd.MM.yyyy",CultureInfo.InvariantCulture);foreach(string raw in values){string v=raw.Trim();string[] parts=v.Split(new[]{".."},StringSplitOptions.None);if(parts.Length==2&&TryParseQuestDate(parts[0],out DateTime a)&&TryParseQuestDate(parts[1],out DateTime b)&&date.Date>=a.Date&&date.Date<=b.Date)return true;if(v.Equals(iso,StringComparison.OrdinalIgnoreCase)||v.Equals(ru,StringComparison.OrdinalIgnoreCase)||(DateTime.TryParse(v,CultureInfo.InvariantCulture,DateTimeStyles.None,out DateTime exact)&&exact.Date==date.Date))return true;}return false;
        }
        private static bool TryParseQuestDate(string text,out DateTime date)=>DateTime.TryParseExact(text.Trim(),new[]{"yyyy-MM-dd","dd.MM.yyyy","MM-dd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
        private void IncrementActivation(string id){_store.State.ActivationCounts.TryGetValue(id,out int count);_store.State.ActivationCounts[id]=count+1;}
        private int ActivationCount(string id)=>_store.State.ActivationCounts.TryGetValue(id,out int value)?value:0;
        private static int Amount(Dictionary<string,int> dict,string id)=>dict.TryGetValue(id,out int v)?v:0;
        private static void AddAmount(Dictionary<string,int> dict,string id,int delta){int value=Amount(dict,id)+delta;if(value<=0)dict.Remove(id);else dict[id]=value;}
        private void AddInventoryAmount(string id,int delta)
        {
            if(string.IsNullOrWhiteSpace(id)||delta==0)return;
            AddAmount(_store.State.Inventory,id,delta);
            if(delta>0)
            {
                _store.State.NewItems[id]=true;
                _host.TriggerInventoryBookmarkBeacon();
            }
            else if(!_store.State.Inventory.ContainsKey(id)) _store.State.NewItems.Remove(id);
        }
        private void MarkInventoryItemSeen(string id)
        {
            id=id?.Trim()??"";
            if(string.IsNullOrWhiteSpace(id))return;
            if(!_store.State.Inventory.ContainsKey(id))return;
            if(!_store.State.NewItems.Remove(id))return;
            _store.SaveState();
            BroadcastState(true);
        }
        private static double DistanceSquared(double x,double y,double z,double tx,double ty,double tz){double dx=x-tx,dy=y-ty,dz=z-tz;return dx*dx+dy*dy+dz*dz;}
        private void BeginInvokeUi(Action action){try{if(_host.IsDisposed)return;if(_host.InvokeRequired)_host.BeginInvoke(action);else action();}catch{}}
        public void Dispose(){if(_disposed)return;_disposed=true;try{_tickTimer?.Dispose();}catch{}try{_server?.Stop();}catch{} _server=null;try{TruckTelemetry.Stop();}catch{}}
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);
    }
}


