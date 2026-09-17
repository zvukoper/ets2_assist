using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(700) };
        private readonly Dictionary<string, QuestPointCoord> _pointCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inside = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeDialogue = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QuestPointSnapshot> _nearby = new();
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
        private bool _overlayVisible;
        private Process? _overlayProcess;
        private DateTime _lastStateSentUtc = DateTime.MinValue;
        private JObject? _lastState;
        private double _lastTruckX, _lastTruckY, _lastTruckZ;

        private sealed class QuestPointCoord
        {
            public string Uid = "";
            public string Category = "";
            public double X, Y, Z;
        }

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
            public void SendJson(JObject payload)
            {
                try { Send(payload.ToString(Formatting.None)); } catch { }
            }
        }

        private QuestRuntime(MainForm host)
        {
            _host = host;
            _store = new QuestStore();
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
                _menuEnabled.Click += (_, _) => { _store.Settings.Enabled = _menuEnabled.Checked; _store.SaveSettings(); EnforceArPointDebugMode(); BroadcastState(true); };
                _menuDebug.Click += (_, _) => { _store.Settings.DebugShowAllPoints = _menuDebug.Checked; _store.SaveSettings(); EnforceArPointDebugMode(); BroadcastState(true); };
                _menuSettings.Click += (_, _) =>
                {
                    using var dlg = new QuestSettingsForm(_store.Settings, () => { _store.SaveSettings(); RestartTickTimer(); EnforceArPointDebugMode(); BroadcastState(true); });
                    dlg.ShowDialog(_host);
                };
                _menuReset.Click += (_, _) =>
                {
                    if (MessageBox.Show(_host, "Сбросить состояние тестового квеста и тестовый инвентарь?", "Квесты", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    _store.ResetQuest("special_marinated_shashlik");
                    _inside.Clear(); _activeDialogue.Clear(); _nearby.Clear();
                    ForceArRebuild();
                    BroadcastState(true);
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
            _paused = await ReadPauseAsync().ConfigureAwait(true);
            if (!TruckTelemetry.TryGetSnapshot(out var truck, out _, out bool haveSample) || !haveSample)
            {
                _nearby.Clear();
                BroadcastState(false);
                await UpdateOverlayAsync().ConfigureAwait(true);
                EnforceArPointDebugMode();
                return;
            }

            _lastTruckX = truck.X;
            _lastTruckY = truck.Y;
            _lastTruckZ = truck.Z;
            _nearby.Clear();
            _nearby.AddRange(GetAvailableInteractions(truck.X, truck.Y, truck.Z));
            HandleTriggers(_nearby);
            if (_paused || _nearby.Count > 0 || DateTime.UtcNow - _lastStateSentUtc > TimeSpan.FromSeconds(1)) BroadcastState(_paused || _nearby.Count > 0);
            await UpdateOverlayAsync().ConfigureAwait(true);
            EnforceArPointDebugMode();
        }

        private async Task<bool> ReadPauseAsync()
        {
            try
            {
                string text = (await _http.GetStringAsync("http://localhost:8080/api/rest/single/frame/paused").ConfigureAwait(true)).Trim();
                if (bool.TryParse(text, out bool b)) return b;
                var token = JToken.Parse(text);
                if (token.Type == JTokenType.Boolean) return token.Value<bool>();
                return token["paused"]?.Value<bool>() ?? token["value"]?.Value<bool>() ?? false;
            }
            catch { return _paused; }
        }

        private List<QuestPointSnapshot> GetAvailableInteractions(double x, double y, double z)
        {
            var result = new List<QuestPointSnapshot>();
            if (!_store.Settings.Enabled) return result;
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
                string key = p.QuestId + ":" + p.InteractionId;
                if (!_inside.Add(key)) continue;
                bool noMore = p.Marker == "none";
                Broadcast(new JObject
                {
                    ["command"] = "quest_notify",
                    ["notification"] = new JObject
                    {
                        ["title"] = noMore ? "" : "Рядом доступно задание",
                        ["text"] = noMore ? "Заданий пока нет. Возвращайтесь позже. (в разработке)" : "Выйдите в меню или поставьте игру на паузу, чтобы узнать подробности",
                        ["icon"] = p.Marker
                    }
                });
            }
            _inside.RemoveWhere(k => !now.Contains(k));
        }

        private bool IsQuestAvailable(QuestDefinition def)
        {
            if (!_store.Settings.Enabled) return false;
            var progress = GetProgress(def.Id);
            if (progress.Status == QuestStatus.Completed && def.Interactions.All(i => !i.ShowWhenQuestCompleted)) return false;
            if (def.Requirements != null && !EvaluateRequirement(def.Requirements)) return false;
            foreach (string exclude in def.Excludes)
                if (GetProgress(exclude).Status == QuestStatus.Active) return false;
            return true;
        }

        private bool TryBuildInteraction(QuestDefinition def, QuestInteractionDefinition interaction, out QuestPointSnapshot point)
        {
            point = new QuestPointSnapshot();
            if (!TryGetPoint(interaction.Source.Category, interaction.Source.Uid, out var coord)) return false;
            var progress = GetProgress(def.Id);
            string key = def.Id + ":" + interaction.Id;
            bool permanentlyNamed = _store.State.PermanentInteractionNames.ContainsKey(key);
            if (!permanentlyNamed && interaction.RequiredQuestStatus.Length > 0 && !string.Equals(progress.Status.ToString(), interaction.RequiredQuestStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (!permanentlyNamed && interaction.RequiredQuestStep.Length > 0 && !string.Equals(progress.Step, interaction.RequiredQuestStep, StringComparison.OrdinalIgnoreCase)) return false;

            string marker = interaction.DefaultMarker;
            string dialogue = interaction.InitialDialogue;
            if (progress.Status == QuestStatus.Active)
            {
                marker = interaction.ActiveMarker;
                dialogue = interaction.ActiveDialogue;
                if (def.Id == "special_marinated_shashlik" && interaction.Id == "ruslan" && progress.Step == "return_to_ruslan") marker = "yellow_question";
                if (permanentlyNamed && interaction.Id == "gosha") marker = "none";
            }
            else if (progress.Status == QuestStatus.Completed)
            {
                marker = interaction.CompletedMarker;
                dialogue = interaction.CompletedDialogue;
            }
            else if (progress.ReturnOffer && !string.IsNullOrWhiteSpace(interaction.CancelledDialogue))
            {
                marker = interaction.DefaultMarker;
                dialogue = interaction.CancelledDialogue;
            }
            if (permanentlyNamed && interaction.Id == "gosha" && progress.Step == "return_to_ruslan") marker = "none";

            bool completedRuslan = progress.Status == QuestStatus.Completed && interaction.Id == "ruslan" && interaction.ShowWhenQuestCompleted;
            bool ordinaryPermanent = permanentlyNamed && marker == "none";
            bool visible = interaction.MinimapVisible || interaction.ArVisible || interaction.PermanentName || ordinaryPermanent || completedRuslan;
            if (!visible) return false;

            point = new QuestPointSnapshot
            {
                QuestId = def.Id,
                InteractionId = interaction.Id,
                Uid = coord.Uid,
                Category = coord.Category,
                Name = GetPermanentName(def.Id, interaction.Id, interaction.Name),
                Marker = marker,
                X = coord.X, Y = coord.Y, Z = coord.Z,
                MinimapVisible = interaction.MinimapVisible || interaction.PermanentName || ordinaryPermanent || completedRuslan,
                ArVisible = interaction.ArVisible && marker != "none",
                Interactive = marker != "none" || completedRuslan,
                PermanentName = interaction.PermanentName || ordinaryPermanent || completedRuslan,
                TriggerRadiusM = interaction.TriggerRadiusM > 0 ? interaction.TriggerRadiusM : _store.Settings.TriggerRadiusM
            };
            if (progress.Status == QuestStatus.Completed && !string.IsNullOrWhiteSpace(interaction.CompletedName)) point.Name = interaction.CompletedName;
            _activeDialogue[key] = dialogue;
            return true;
        }

        private string GetPermanentName(string questId, string interactionId, string fallback)
        {
            return _store.State.PermanentInteractionNames.TryGetValue(questId + ":" + interactionId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
        }

        private bool TryGetPoint(string category, string uid, out QuestPointCoord point)
        {
            string key = category + ":" + uid;
            if (_pointCache.TryGetValue(key, out point!)) return true;
            point = null!;
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(uid)) return false;
            string path = Path.Combine(AppDataPaths.StaticDataDirectory, "editor_static_data", "model_" + category + ".json");
            try
            {
                if (!File.Exists(path)) return false;
                var root = JObject.Parse(File.ReadAllText(path));
                foreach (var token in root["objects"] as JArray ?? new JArray())
                {
                    if (!string.Equals(token["uid"]?.Value<string>(), uid, StringComparison.OrdinalIgnoreCase)) continue;
                    point = new QuestPointCoord { Uid = uid, Category = category, X = token["x"]?.Value<double>() ?? 0, Y = token["y"]?.Value<double>() ?? 0, Z = token["z"]?.Value<double>() ?? 0 };
                    _pointCache[key] = point;
                    return true;
                }
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] point load {category}/{uid}: {ex.Message}"); }
            return false;
        }

        private QuestProgress GetProgress(string id)
        {
            if (!_store.State.Quests.TryGetValue(id, out var progress))
            {
                progress = new QuestProgress();
                _store.State.Quests[id] = progress;
            }
            return progress;
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
                var p = GetProgress(c.QuestId);
                if (!string.IsNullOrWhiteSpace(c.QuestStatus) && !string.Equals(p.Status.ToString(), c.QuestStatus, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.IsNullOrWhiteSpace(c.QuestStep) && !string.Equals(p.Step, c.QuestStep, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (!string.IsNullOrWhiteSpace(c.Item) && Amount(_store.State.Inventory, c.Item) < Math.Max(1, c.Amount)) return false;
            if (!string.IsNullOrWhiteSpace(c.Reputation) && Amount(_store.State.Reputation, c.Reputation) < c.MinValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Stat) && Amount(_store.State.Stats, c.Stat) < c.MinStatValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Flag))
            {
                bool value = false;
                if (!string.IsNullOrWhiteSpace(c.QuestId) && _store.State.Quests.TryGetValue(c.QuestId, out var p)) p.Flags.TryGetValue(c.Flag, out value);
                else value = _store.State.Quests.Values.Any(p => p.Flags.TryGetValue(c.Flag, out var v) && v);
                if (value != c.FlagValue) return false;
            }
            return true;
        }

        private void OnQuestClientOpen(QuestSocketBehavior client) { client.SendJson(BuildStatePayload()); }

        private void OnQuestMessage(JObject data)
        {
            string command = data["command"]?.Value<string>() ?? "";
            switch (command)
            {
                case "quest_select_interaction":
                    BeginInvokeUi(() => SelectInteraction(data["questId"]?.Value<string>() ?? "special_marinated_shashlik", data["id"]?.Value<string>() ?? ""));
                    break;
                case "quest_dialog_option":
                    BeginInvokeUi(() => ApplyDialogOption(data["questId"]?.Value<string>() ?? "special_marinated_shashlik", data["interaction"]?.Value<string>() ?? "", data["index"]?.Value<int>() ?? -1));
                    break;
                case "quest_reset":
                    BeginInvokeUi(() =>
                    {
                        _store.ResetQuest(data["id"]?.Value<string>() ?? "special_marinated_shashlik");
                        _inside.Clear(); _activeDialogue.Clear(); _nearby.Clear(); ForceArRebuild(); BroadcastState(true);
                    });
                    break;
            }
        }

        private void SelectInteraction(string questId, string interactionId)
        {
            if (!_paused) { SendError("Интерактив доступен только на паузе игры."); return; }
            if (!_store.Definitions.TryGetValue(questId, out var def)) return;
            var interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null || !TryBuildInteraction(def, interaction, out _)) return;
            string key = questId + ":" + interactionId;
            string dialogue = _activeDialogue.TryGetValue(key, out var d) ? d : interaction.InitialDialogue;
            BroadcastState(true, questId, interactionId, dialogue);
        }

        private void ApplyDialogOption(string questId, string interactionId, int index)
        {
            if (!_paused) { SendError("Взаимодействие разрешено только на паузе игры."); return; }
            if (!_store.Definitions.TryGetValue(questId, out var def)) return;
            var interaction = def.Interactions.FirstOrDefault(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null || !TryBuildInteraction(def, interaction, out _)) return;
            string key = questId + ":" + interactionId;
            string current = _activeDialogue.TryGetValue(key, out var d) ? d : interaction.InitialDialogue;
            if (!def.Dialogues.TryGetValue(current, out var node) || index < 0 || index >= node.Options.Count) return;
            var option = node.Options[index];
            if (option.Requirements != null && !EvaluateRequirement(option.Requirements)) { SendError("Условия варианта не выполнены."); return; }
            ApplyEffects(def, option.Effects);
            string next = option.Next;
            if (!string.IsNullOrWhiteSpace(next)) _activeDialogue[key] = next;
            ForceArRebuild();
            BroadcastState(true, option.Close ? null : questId, option.Close ? null : interactionId, option.Close ? null : next);
        }

        private void ApplyEffects(QuestDefinition def, IEnumerable<QuestEffect> effects)
        {
            foreach (var effect in effects)
            {
                var progress = GetProgress(def.Id);
                if (!string.IsNullOrWhiteSpace(effect.SetQuestStatus) && Enum.TryParse(effect.SetQuestStatus, true, out QuestStatus status))
                {
                    var old = progress.Status;
                    progress.Status = status;
                    progress.ChangedUtc = DateTime.UtcNow;
                    if (status == QuestStatus.Completed && old != QuestStatus.Completed) GrantRewards(def);
                    if (status == QuestStatus.Available && old == QuestStatus.Active) progress.ReturnOffer = true;
                }
                if (!string.IsNullOrWhiteSpace(effect.SetStep)) progress.Step = effect.SetStep;
                if (!string.IsNullOrWhiteSpace(effect.Item))
                {
                    if (effect.AddItem != 0) AddAmount(_store.State.Inventory, effect.Item, effect.AddItem);
                    if (effect.RemoveItem != 0) AddAmount(_store.State.Inventory, effect.Item, -effect.RemoveItem);
                }
                if (!string.IsNullOrWhiteSpace(effect.Reputation) && effect.AddReputation != 0) AddAmount(_store.State.Reputation, effect.Reputation, effect.AddReputation);
                if (!string.IsNullOrWhiteSpace(effect.Stat) && effect.AddStat != 0) AddAmount(_store.State.Stats, effect.Stat, effect.AddStat);
                if (!string.IsNullOrWhiteSpace(effect.Flag))
                {
                    progress.Flags[effect.Flag] = effect.SetFlag ?? false;
                    if (effect.Flag.Equals("returnOffer", StringComparison.OrdinalIgnoreCase)) progress.ReturnOffer = effect.SetFlag ?? false;
                }
                if (!string.IsNullOrWhiteSpace(effect.RenameInteraction) && !string.IsNullOrWhiteSpace(effect.RenameTo))
                    _store.State.PermanentInteractionNames[def.Id + ":" + effect.RenameInteraction] = effect.RenameTo;
                if (!string.IsNullOrWhiteSpace(effect.NotifyText))
                    Broadcast(new JObject { ["command"] = "quest_notify", ["notification"] = new JObject { ["title"] = effect.NotifyTitle ?? "", ["text"] = effect.NotifyText, ["icon"] = "" } });
            }
            _store.SaveState();
        }

        private void GrantRewards(QuestDefinition def)
        {
            var p = GetProgress(def.Id);
            if (p.Flags.TryGetValue("__rewardsGranted", out bool granted) && granted) return;
            var itemLines = new List<string>();
            var repLines = new List<string>();
            foreach (var reward in def.Rewards)
            {
                if (reward.Type.Equals("item", StringComparison.OrdinalIgnoreCase)) { AddAmount(_store.State.Inventory, reward.Id, reward.Amount); itemLines.Add($"Получен предмет: {reward.Display} x{reward.Amount}"); }
                else if (reward.Type.Equals("reputation", StringComparison.OrdinalIgnoreCase)) { AddAmount(_store.State.Reputation, reward.Id, reward.Amount); repLines.Add($"{reward.Id}: +{reward.Amount}"); }
                else if (reward.Type.Equals("stat", StringComparison.OrdinalIgnoreCase)) AddAmount(_store.State.Stats, reward.Id, reward.Amount);
            }
            p.Flags["__rewardsGranted"] = true;
            if (def.Id == "special_marinated_shashlik")
            {
                Broadcast(new JObject { ["command"] = "quest_notify", ["notification"] = new JObject { ["title"] = "", ["text"] = "Предмет отдан:\nМясо в спецмаринаде x1\n" + string.Join("\n", itemLines), ["icon"] = "", ["accent"] = "reward" } });
                if (repLines.Count > 0) Broadcast(new JObject { ["command"] = "quest_notify", ["notification"] = new JObject { ["title"] = "Получена репутация:", ["text"] = string.Join("\n", repLines), ["icon"] = "", ["accent"] = "reputation" } });
            }
        }

        private static int Amount(Dictionary<string, int> dict, string id) => dict.TryGetValue(id, out var v) ? v : 0;
        private static void AddAmount(Dictionary<string, int> dict, string id, int delta) { int n = Amount(dict, id) + delta; if (n <= 0) dict.Remove(id); else dict[id] = n; }

        private JObject BuildStatePayload(string? selectedQuest = null, string? selectedInteraction = null, string? explicitDialogue = null)
        {
            var points = new JArray();
            foreach (var def in _store.Definitions.Values)
                foreach (var interaction in def.Interactions)
                    if (TryBuildInteraction(def, interaction, out var point)) points.Add(JObject.FromObject(point));

            var active = new JArray(); var archive = new JArray();
            foreach (var def in _store.Definitions.Values)
            {
                var p = GetProgress(def.Id);
                var q = new JObject { ["id"] = def.Id, ["title"] = def.Title, ["description"] = def.Description, ["status"] = p.Status.ToString(), ["step"] = p.Step, ["rewards"] = JArray.FromObject(def.Rewards), ["returnOffer"] = p.ReturnOffer };
                if (p.Status == QuestStatus.Active) active.Add(q); else if (p.Status != QuestStatus.Available) archive.Add(q);
            }
            var inventory = new JArray();
            foreach (var item in _store.State.Inventory) inventory.Add(new JObject { ["id"] = item.Key, ["name"] = DisplayItemName(item.Key), ["amount"] = item.Value });
            var nearby = new JArray();
            foreach (var p in _nearby) nearby.Add(new JObject { ["QuestId"] = p.QuestId, ["InteractionId"] = p.InteractionId, ["Name"] = p.Name, ["Marker"] = p.Marker, ["distance"] = Math.Sqrt(DistanceSquared(p.X, p.Y, p.Z, _lastTruckX, _lastTruckY, _lastTruckZ)) });

            var payload = new JObject
            {
                ["command"] = "quest_state", ["paused"] = _paused, ["enabled"] = _store.Settings.Enabled,
                ["points"] = points, ["nearby"] = nearby, ["activeQuests"] = active, ["archiveQuests"] = archive,
                ["inventory"] = inventory, ["settings"] = JObject.FromObject(_store.Settings)
            };
            if (!string.IsNullOrWhiteSpace(selectedQuest) && !string.IsNullOrWhiteSpace(selectedInteraction))
            {
                if (explicitDialogue == null) _activeDialogue.TryGetValue(selectedQuest + ":" + selectedInteraction, out explicitDialogue);
                if (!string.IsNullOrWhiteSpace(explicitDialogue) && _store.Definitions.TryGetValue(selectedQuest, out var def) && def.Dialogues.TryGetValue(explicitDialogue, out var node))
                {
                    payload["selectedQuest"] = selectedQuest; payload["selectedInteraction"] = selectedInteraction;
                    payload["dialogue"] = BuildDialoguePayload(def, node);
                }
            }
            return payload;
        }

        private JObject BuildDialoguePayload(QuestDefinition def, QuestDialogueNode node)
        {
            var arr = new JArray();
            foreach (var option in node.Options)
                arr.Add(new JObject { ["text"] = option.Text, ["enabled"] = option.Requirements == null || EvaluateRequirement(option.Requirements), ["close"] = option.Close });
            return new JObject { ["speaker"] = node.Speaker, ["text"] = node.Text, ["image"] = node.Image, ["options"] = arr };
        }

        private static string DisplayItemName(string id) => id switch
        {
            "special_marinade_meat" => "Мясо в спецмаринаде",
            "legendary_shashlik" => "Легендарный шашлык от Руслана",
            _ => id
        };

        private void BroadcastState(bool force, string? selectedQuest = null, string? selectedInteraction = null, string? explicitDialogue = null)
        {
            var payload = BuildStatePayload(selectedQuest, selectedInteraction, explicitDialogue);
            if (!force && JToken.DeepEquals(payload, _lastState)) return;
            _lastState = payload; _lastStateSentUtc = DateTime.UtcNow; Broadcast(payload);
        }

        private void Broadcast(JObject payload) { try { _server?.WebSocketServices["/"]?.Sessions.Broadcast(payload.ToString(Formatting.None)); } catch { } }
        private void SendError(string text) => Broadcast(new JObject { ["command"] = "quest_error", ["text"] = text });

        private async Task UpdateOverlayAsync()
        {
            bool shouldShow = _paused && _store.Settings.Enabled;
            if (!shouldShow) { if (_overlayVisible) { _overlayVisible = false; HideOverlay(); } return; }
            if (!_overlayVisible) { _overlayVisible = true; await EnsureOverlayAsync().ConfigureAwait(true); }
            FocusOverlay();
        }

        private async Task EnsureOverlayAsync()
        {
            if (_overlayProcess != null && !_overlayProcess.HasExited) return;
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            if (!File.Exists(exe)) { Logger.Current?.Data("[QUEST] WebOverlay.exe not found: " + exe); return; }
            try
            {
                _overlayProcess = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "http://localhost:8082/web_quests.html", UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
                try { await Task.Delay(250).ConfigureAwait(true); } catch { }
            }
            catch (Exception ex) { Logger.Current?.Data("[QUEST] overlay start: " + ex.Message); }
        }

        private void FocusOverlay()
        {
            if (!_paused || !_overlayVisible) return;
            try
            {
                var p = _overlayProcess; if (p == null || p.HasExited) return; p.Refresh(); var h = p.MainWindowHandle; if (h == IntPtr.Zero) return;
                ShowWindow(h, SwShow); BringWindowToTop(h); SetForegroundWindow(h);
            }
            catch { }
        }

        private void HideOverlay()
        {
            try { var p = _overlayProcess; if (p == null || p.HasExited) return; p.Refresh(); if (p.MainWindowHandle != IntPtr.Zero) ShowWindow(p.MainWindowHandle, SwHide); } catch { }
        }

        private void EnforceArPointDebugMode()
        {
            try
            {
                AppSettings.ArDisplayRadiusM = _store.Settings.DebugShowAllPoints ? (int)Math.Clamp(_store.Settings.DebugRadiusM, 5, 5000) : 0;
                if (_host.IsHandleCreated) _host.BeginInvoke(new Action(ForceArRebuild));
            }
            catch { }
        }

        private void ForceArRebuild()
        {
            try
            {
                typeof(MainForm).GetMethod("RefreshArModel", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_host, null);
                _host.ForceArDataResend("quest-state-change");
            }
            catch { }
        }

        internal IReadOnlyList<QuestPointSnapshot> GetQuestPointsForEditor()
        {
            var result = new List<QuestPointSnapshot>();
            foreach (var def in _store.Definitions.Values)
                foreach (var interaction in def.Interactions)
                    if (TryBuildInteraction(def, interaction, out var point) && point.Interactive) result.Add(point);
            return result;
        }

        private static double DistanceSquared(double x, double y, double z, double tx, double ty, double tz)
        {
            double dx = x - tx, dy = y - ty, dz = z - tz; return dx * dx + dy * dy + dz * dz;
        }

        private void BeginInvokeUi(Action action)
        {
            try { if (_host.IsDisposed) return; if (_host.InvokeRequired) _host.BeginInvoke(action); else action(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _tickTimer?.Dispose(); } catch { }
            try { _server?.Stop(); } catch { }
            _server = null;
            HideOverlay();
            try { if (_overlayProcess != null && !_overlayProcess.HasExited) _overlayProcess.CloseMainWindow(); } catch { }
            try { _overlayProcess?.Dispose(); } catch { }
            _overlayProcess = null;
            try { TruckTelemetry.Stop(); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}