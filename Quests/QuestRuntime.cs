using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        private static readonly object StaticSync = new();
        private static QuestRuntime? _current;

        private readonly MainForm _host;
        private readonly QuestStore _store;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(700) };
        private readonly Dictionary<string, QuestPointCoord> _pointCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inside = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeDialogue = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _lastQuestAvailability = new(StringComparer.OrdinalIgnoreCase);
        private readonly ToolStripMenuItem _menuRoot = new("Квесты");
        private readonly ToolStripMenuItem _menuEnabled = new("Система включена");
        private readonly ToolStripMenuItem _menuDebug = new("Отладочный показ всех точек");
        private readonly ToolStripMenuItem _menuSettings = new("Настройки видимости…");
        private readonly ToolStripMenuItem _menuReset = new("Сбросить тестовый квест");

        private WebSocketServer? _server;
        private QuestSocketBehavior? _behavior;
        private Timer? _tickTimer;
        private int _tickBusy;
        private bool _disposed;
        private bool _paused;
        private bool _overlayVisible;
        private Process? _overlayProcess;
        private DateTime _lastStateSentUtc = DateTime.MinValue;
        private JObject? _lastState;

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
            protected override void OnClose(CloseEventArgs e) => _owner.OnQuestClientClose(this);

            protected override void OnMessage(MessageEventArgs e)
            {
                try
                {
                    var obj = JObject.Parse(e.Data);
                    _owner.OnQuestMessage(obj);
                }
                catch (Exception ex)
                {
                    Logger.Current?.Data($"[QUEST][WS] invalid message: {ex.Message}");
                }
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
            AppDataPaths.EnsureUserData();
            InstallMenu();
            EnforceArPointDebugMode();

            TruckTelemetry.Start();
            _tickTimer = new Timer(_ => QueueTick(), null, 500, Math.Max(100, _store.Settings.PollIntervalMs));

            try
            {
                _server = new WebSocketServer(QuestWsPort);
                _behavior = new QuestSocketBehavior(this);
                _server.AddWebSocketService("/", () => _behavior = new QuestSocketBehavior(this));
                _server.Start();
                Logger.Current?.Workflow($"[QUEST] WebSocket server started on {QuestWsPort}.");
            }
            catch (Exception ex)
            {
                Logger.Current?.Warning($"[QUEST] WebSocket server start failed: {ex.Message}");
            }

            Application.ApplicationExit += (_, _) => Dispose();
        }

        public static void Attach(MainForm host)
        {
            lock (StaticSync)
            {
                _current?.Dispose();
                _current = new QuestRuntime(host);
            }
        }

        internal static QuestRuntime? Current
        {
            get { lock (StaticSync) return _current; }
        }

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
                    EnforceArPointDebugMode();
                    BroadcastState(force: true);
                };
                _menuDebug.Click += (_, _) =>
                {
                    _store.Settings.DebugShowAllPoints = _menuDebug.Checked;
                    _store.SaveSettings();
                    EnforceArPointDebugMode();
                    BroadcastState(force: true);
                };
                _menuSettings.Click += (_, _) =>
                {
                    using var dlg = new QuestSettingsForm(_store.Settings, () =>
                    {
                        _store.SaveSettings();
                        RestartTickTimer();
                        EnforceArPointDebugMode();
                        BroadcastState(force: true);
                    });
                    dlg.ShowDialog(_host);
                };
                _menuReset.Click += (_, _) =>
                {
                    if (MessageBox.Show(_host, "Сбросить состояние тестового квеста и тестовый инвентарь?", "Квесты", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        _store.ResetQuest("special_marinated_shashlik");
                        _inside.Clear();
                        _activeDialogue.Clear();
                        SendMapAndArRefresh();
                        BroadcastState(force: true);
                    }
                };

                _menuRoot.DropDownItems.Add(_menuEnabled);
                _menuRoot.DropDownItems.Add(_menuDebug);
                _menuRoot.DropDownItems.Add(new ToolStripSeparator());
                _menuRoot.DropDownItems.Add(_menuSettings);
                _menuRoot.DropDownItems.Add(_menuReset);
                menu.Items.Add(_menuRoot);
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] menu error: {ex.Message}");
            }
        }

        private void RestartTickTimer()
        {
            try
            {
                _tickTimer?.Dispose();
                _tickTimer = new Timer(_ => QueueTick(), null, 150, Math.Max(100, _store.Settings.PollIntervalMs));
            }
            catch { }
        }

        private void QueueTick()
        {
            if (Interlocked.CompareExchange(ref _tickBusy, 1, 0) != 0) return;
            try
            {
                if (_disposed || _host.IsDisposed || !_host.IsHandleCreated) return;
                _host.BeginInvoke(new Action(async () =>
                {
                    try { await TickAsync().ConfigureAwait(true); }
                    catch (Exception ex) { Logger.Current?.Data("[QUEST] tick: " + ex.Message); }
                    finally { Volatile.Write(ref _tickBusy, 0); }
                });
            }
            catch
            {
                Volatile.Write(ref _tickBusy, 0);
            }
        }

        private async Task TickAsync()
        {
            if (_disposed) return;
            bool paused = await ReadPauseAsync().ConfigureAwait(true);
            bool pauseChanged = paused != _paused;
            _paused = paused;

            if (!TruckTelemetry.TryGetSnapshot(out var truck, out _, out bool haveSample) || !haveSample)
            {
                await UpdateOverlayAsync(paused, Array.Empty<QuestPointSnapshot>()).ConfigureAwait(true);
                if (pauseChanged) BroadcastState(force: true);
                return;
            }

            var nearby = GetAvailableInteractions(truck.X, truck.Y, truck.Z);
            HandleTriggers(nearby);

            if (pauseChanged || nearby.Count > 0 || DateTime.UtcNow - _lastStateSentUtc > TimeSpan.FromSeconds(1))
                BroadcastState(force: pauseChanged || nearby.Count > 0);

            await UpdateOverlayAsync(paused, nearby).ConfigureAwait(true);
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
            catch
            {
                return _paused;
            }
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
                    if (!TryBuildInteraction(def, interaction, out var point)) continue;
                    double dx = point.X - x;
                    double dy = point.Y - y;
                    double dz = point.Z - z;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist <= point.TriggerRadiusM)
                    {
                        point.Name = GetPermanentName(def.Id, interaction.Id, point.Name);
                        result.Add(point);
                    }
                }
            }
            return result.OrderBy(p => DistanceSquared(p.X, p.Y, p.Z, x, y, z)).ToList();
        }

        private void HandleTriggers(List<QuestPointSnapshot> nearby)
        {
            var now = new HashSet<string>(nearby.Select(x => x.QuestId + ":" + x.InteractionId), StringComparer.OrdinalIgnoreCase);
            foreach (var p in nearby)
            {
                string key = p.QuestId + ":" + p.InteractionId;
                if (_inside.Add(key))
                {
                    var notification = p.Marker == "none"
                        ? new JObject
                        {
                            ["title"] = "Нет доступных заданий",
                            ["text"] = "Заданий пока нет. Возвращайтесь позже. (в разработке)",
                            ["icon"] = ""
                        }
                        : new JObject
                        {
                            ["title"] = "Рядом доступно задание",
                            ["text"] = "Выйдите в меню или поставьте игру на паузу, чтобы узнать подробности",
                            ["icon"] = p.Marker
                        };
                    Broadcast(new JObject { ["command"] = "quest_notify", ["notification"] = notification });
                }
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
            {
                if (GetProgress(exclude).Status == QuestStatus.Active) return false;
            }
            return true;
        }

        private bool TryBuildInteraction(QuestDefinition def, QuestInteractionDefinition interaction, out QuestPointSnapshot point)
        {
            point = new QuestPointSnapshot();
            if (!TryGetPoint(interaction.Source.Category, interaction.Source.Uid, out var coord)) return false;
            if (interaction.RequiredQuestStatus.Length > 0 && !string.Equals(GetProgress(def.Id).Status.ToString(), interaction.RequiredQuestStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (interaction.RequiredQuestStep.Length > 0 && !string.Equals(GetProgress(def.Id).Step, interaction.RequiredQuestStep, StringComparison.OrdinalIgnoreCase)) return false;

            var progress = GetProgress(def.Id);
            string marker = interaction.DefaultMarker;
            string dialogue = interaction.InitialDialogue;

            if (string.Equals(progress.Status.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
            {
                marker = interaction.ActiveMarker;
                dialogue = interaction.ActiveDialogue;
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

            bool ordinaryAfterCompletion = marker == "none" && progress.Status == QuestStatus.Completed;
            bool visible = interaction.MinimapVisible || interaction.ArVisible || interaction.PermanentName || ordinaryAfterCompletion;
            if (!visible) return false;

            point = new QuestPointSnapshot
            {
                QuestId = def.Id,
                InteractionId = interaction.Id,
                Uid = coord.Uid,
                Category = coord.Category,
                Name = interaction.Name,
                Marker = marker,
                X = coord.X,
                Y = coord.Y,
                Z = coord.Z,
                MinimapVisible = interaction.MinimapVisible || interaction.PermanentName || ordinaryAfterCompletion,
                ArVisible = interaction.ArVisible && marker != "none",
                Interactive = marker != "none",
                PermanentName = interaction.PermanentName || ordinaryAfterCompletion || _store.State.PermanentInteractionNames.ContainsKey(def.Id + ":" + interaction.Id),
                TriggerRadiusM = interaction.TriggerRadiusM,
            };
            if (progress.Status == QuestStatus.Completed && interaction.CompletedName.Length > 0)
            {
                point.Name = interaction.CompletedName;
                point.PermanentName = true;
            }
            _activeDialogue[def.Id + ":" + interaction.Id] = dialogue;
            return true;
        }

        private string GetPermanentName(string questId, string interactionId, string fallback)
        {
            return _store.State.PermanentInteractionNames.TryGetValue(questId + ":" + interactionId, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
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
                    point = new QuestPointCoord
                    {
                        Uid = uid,
                        Category = category,
                        X = token["x"]?.Value<double>() ?? 0,
                        Y = token["y"]?.Value<double>() ?? 0,
                        Z = token["z"]?.Value<double>() ?? 0
                    };
                    _pointCache[key] = point;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] point load {category}/{uid}: {ex.Message}");
            }
            return false;
        }

        private QuestProgress GetProgress(string questId)
        {
            if (!_store.State.Quests.TryGetValue(questId, out var p))
            {
                p = new QuestProgress();
                _store.State.Quests[questId] = p;
            }
            return p;
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
            if (!string.IsNullOrWhiteSpace(c.Item) && GetAmount(_store.State.Inventory, c.Item) < Math.Max(1, c.Amount)) return false;
            if (!string.IsNullOrWhiteSpace(c.Reputation) && GetAmount(_store.State.Reputation, c.Reputation) < c.MinValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Stat) && GetAmount(_store.State.Stats, c.Stat) < c.MinStatValue) return false;
            if (!string.IsNullOrWhiteSpace(c.Flag))
            {
                bool value = GetProgressForFlag(c.Flag, c.QuestId);
                if (value != c.FlagValue) return false;
            }
            return true;
        }

        private bool GetProgressForFlag(string flag, string questId)
        {
            if (!string.IsNullOrWhiteSpace(questId) && _store.State.Quests.TryGetValue(questId, out var p) && p.Flags.TryGetValue(flag, out bool direct)) return direct;
            return _store.State.Quests.Values.Any(p => p.Flags.TryGetValue(flag, out bool v) && v);
        }

        private static int GetAmount(Dictionary<string, int> dict, string key) => dict.TryGetValue(key, out var v) ? v : 0;

        private void OnQuestClientOpen(QuestSocketBehavior client)
        {
            if (_disposed) return;
            try { client.SendJson(BuildStatePayload()); } catch { }
        }

        private void OnQuestClientClose(QuestSocketBehavior client) { }

        private void OnQuestMessage(JObject data)
        {
            string command = data["command"]?.Value<string>() ?? "";
            try
            {
                if (command == "quest_select_interaction")
                {
                    string id = data["id"]?.Value<string>() ?? "";
                    string questId = data["questId"]?.Value<string>() ?? "";
                    if (questId.Length == 0) questId = "special_marinated_shashlik";
                    BeginInvokeUi(() => SelectInteraction(questId, id));
                }
                else if (command == "quest_dialog_option")
                {
                    string interactionId = data["interaction"]?.Value<string>() ?? "";
                    string questId = data["questId"]?.Value<string>() ?? "special_marinated_shashlik";
                    int index = data["index"]?.Value<int>() ?? -1;
                    BeginInvokeUi(() => ApplyDialogOption(questId, interactionId, index));
                }
                else if (command == "quest_select_quest")
                {
                    string questId = data["id"]?.Value<string>() ?? "";
                    BroadcastState(force: true, selectedQuest: questId);
                }
                else if (command == "quest_reset")
                {
                    string questId = data["id"]?.Value<string>() ?? "special_marinated_shashlik";
                    BeginInvokeUi(() =>
                    {
                        _store.ResetQuest(questId);
                        _activeDialogue.Remove(questId + ":ruslan");
                        SendMapAndArRefresh();
                        BroadcastState(force: true);
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST][WS] command={command}: {ex.Message}");
            }
        }

        private void SelectInteraction(string questId, string interactionId)
        {
            if (!_paused)
            {
                SendError("Интерактив доступен только на паузе игры.");
                return;
            }
            if (!_store.Definitions.TryGetValue(questId, out var def)) return;
            var interaction = def.Interactions.FirstOrDefault(i => string.Equals(i.Id, interactionId, StringComparison.OrdinalIgnoreCase));
            if (interaction == null) return;
            if (!TryBuildInteraction(def, interaction, out _)) return;

            string dialogueId = _activeDialogue.TryGetValue(questId + ":" + interactionId, out var d) ? d : interaction.InitialDialogue;
            BroadcastState(force: true, selectedInteraction: interactionId, selectedQuest: questId, explicitDialogue: dialogueId);
        }

        private void ApplyDialogOption(string questId, string interactionId, int index)
        {
            if (!_paused)
            {
                SendError("Взаимодействие разрешено только на паузе игры.");
                return;
            }
            if (!_store.Definitions.TryGetValue(questId, out var def)) return;
            if (!def.Interactions.Any(i => string.Equals(i.Id, interactionId, StringComparison.OrdinalIgnoreCase))) return;
            string current = _activeDialogue.TryGetValue(questId + ":" + interactionId, out var d) ? d : def.Interactions.First(i => i.Id.Equals(interactionId, StringComparison.OrdinalIgnoreCase)).InitialDialogue;
            if (!def.Dialogues.TryGetValue(current, out var node)) return;
            if (index < 0 || index >= node.Options.Count) return;
            var option = node.Options[index];
            if (option.Requirements != null && !EvaluateRequirement(option.Requirements))
            {
                SendError("Условия варианта не выполнены.");
                return;
            }

            ApplyEffects(def, option.Effects);
            string next = option.Next;
            if (next.Length > 0) _activeDialogue[questId + ":" + interactionId] = next;
            if (option.Close)
            {
                BroadcastState(force: true);
                SendMapAndArRefresh();
                return;
            }
            BroadcastState(force: true, selectedInteraction: interactionId, selectedQuest: questId, explicitDialogue: next);
            SendMapAndArRefresh();
        }

        private void ApplyEffects(QuestDefinition def, IEnumerable<QuestEffect> effects)
        {
            bool statusChanged = false;
            foreach (var effect in effects)
            {
                if (!string.IsNullOrWhiteSpace(effect.SetQuestStatus))
                {
                    var p = GetProgress(def.Id);
                    if (Enum.TryParse(effect.SetQuestStatus, true, out QuestStatus status))
                    {
                        QuestStatus before = p.Status;
                        p.Status = status;
                        p.ChangedUtc = DateTime.UtcNow;
                        statusChanged = before != status;
                        if (status == QuestStatus.Completed && before != QuestStatus.Completed)
                            GrantRewards(def);
                    }
                }
                if (!string.IsNullOrWhiteSpace(effect.SetStep)) GetProgress(def.Id).Step = effect.SetStep;
                if (!string.IsNullOrWhiteSpace(effect.Item))
                {
                    if (effect.AddItem != 0) AddAmount(_store.State.Inventory, effect.Item, effect.AddItem);
                    if (effect.RemoveItem != 0) AddAmount(_store.State.Inventory, effect.Item, -effect.RemoveItem);
                }
                if (!string.IsNullOrWhiteSpace(effect.Reputation) && effect.AddReputation != 0)
                    AddAmount(_store.State.Reputation, effect.Reputation, effect.AddReputation);
                if (!string.IsNullOrWhiteSpace(effect.Stat) && effect.AddStat != 0)
                    AddAmount(_store.State.Stats, effect.Stat, effect.AddStat);
                if (!string.IsNullOrWhiteSpace(effect.Flag))
                    GetProgress(def.Id).Flags[effect.Flag] = effect.SetFlag ?? false;
                if (!string.IsNullOrWhiteSpace(effect.RenameInteraction) && !string.IsNullOrWhiteSpace(effect.RenameTo))
                    _store.State.PermanentInteractionNames[def.Id + ":" + effect.RenameInteraction] = effect.RenameTo;
            }

            if (statusChanged && GetProgress(def.Id).Status == QuestStatus.Available && def.Id == "special_marinated_shashlik")
                _store.State.Quests[def.Id].ReturnOffer = true;
            _store.SaveState();
        }

        private void GrantRewards(QuestDefinition def)
        {
            var progress = GetProgress(def.Id);
            if (progress.Flags.TryGetValue("__rewardsGranted", out bool done) && done) return;
            var itemLines = new List<string>();
            var repLines = new List<string>();
            foreach (var reward in def.Rewards)
            {
                if (reward.Type.Equals("item", StringComparison.OrdinalIgnoreCase))
                {
                    AddAmount(_store.State.Inventory, reward.Id, reward.Amount);
                    itemLines.Add($"Получен предмет: {reward.Display} x{reward.Amount}");
                }
                else if (reward.Type.Equals("reputation", StringComparison.OrdinalIgnoreCase))
                {
                    AddAmount(_store.State.Reputation, reward.Id, reward.Amount);
                    repLines.Add($"{reward.Id}: +{reward.Amount}");
                }
                else if (reward.Type.Equals("stat", StringComparison.OrdinalIgnoreCase))
                {
                    AddAmount(_store.State.Stats, reward.Id, reward.Amount);
                }
            }
            progress.Flags["__rewardsGranted"] = true;
            if (def.Id == "special_marinated_shashlik")
            {
                var note1 = new List<string> { "Предмет отдан:", "Мясо в спецмаринаде x1" };
                note1.AddRange(itemLines);
                Broadcast(new JObject
                {
                    ["command"] = "quest_notify",
                    ["notification"] = new JObject { ["title"] = "", ["text"] = string.Join("\n", note1), ["icon"] = "", ["accent"] = "reward" }
                });
                if (repLines.Count > 0)
                {
                    Broadcast(new JObject
                    {
                        ["command"] = "quest_notify",
                        ["notification"] = new JObject { ["title"] = "Получена репутация:", ["text"] = string.Join("\n", repLines), ["icon"] = "", ["accent"] = "reputation" }
                    });
                }
            }
        }

        private static void AddAmount(Dictionary<string, int> dict, string key, int delta)
        {
            int next = GetAmount(dict, key) + delta;
            if (next <= 0) dict.Remove(key); else dict[key] = next;
        }

        private void BroadcastState(bool force, string? selectedInteraction = null, string? selectedQuest = null, string? explicitDialogue = null)
        {
            if (!_store.Settings.Enabled && !force) return;
            var payload = BuildStatePayload(selectedInteraction, selectedQuest, explicitDialogue);
            if (!force && JToken.DeepEquals(payload, _lastState)) return;
            _lastState = payload;
            _lastStateSentUtc = DateTime.UtcNow;
            Broadcast(payload);
        }

        private JObject BuildStatePayload(string? selectedInteraction = null, string? selectedQuest = null, string? explicitDialogue = null)
        {
            var points = new JArray();
            foreach (var def in _store.Definitions.Values)
            {
                foreach (var interaction in def.Interactions)
                {
                    if (!TryBuildInteraction(def, interaction, out var point)) continue;
                    points.Add(JObject.FromObject(point));
                }
            }

            var active = new JArray();
            var archive = new JArray();
            foreach (var def in _store.Definitions.Values)
            {
                var p = GetProgress(def.Id);
                var q = new JObject
                {
                    ["id"] = def.Id,
                    ["title"] = def.Title,
                    ["description"] = def.Description,
                    ["status"] = p.Status.ToString(),
                    ["step"] = p.Step,
                    ["rewards"] = JArray.FromObject(def.Rewards),
                    ["returnOffer"] = p.ReturnOffer
                };
                if (p.Status == QuestStatus.Active) active.Add(q); else if (p.Status != QuestStatus.Available) archive.Add(q);
            }

            var inventory = new JArray();
            foreach (var item in _store.State.Inventory)
            {
                inventory.Add(new JObject { ["id"] = item.Key, ["name"] = DisplayItemName(item.Key), ["amount"] = item.Value });
            }

            var settings = JObject.FromObject(_store.Settings);
            var payload = new JObject
            {
                ["command"] = "quest_state",
                ["paused"] = _paused,
                ["enabled"] = _store.Settings.Enabled,
                ["points"] = points,
                ["activeQuests"] = active,
                ["archiveQuests"] = archive,
                ["inventory"] = inventory,
                ["settings"] = settings
            };

            string qid = selectedQuest ?? "";
            string iid = selectedInteraction ?? "";
            if (qid.Length > 0 && iid.Length > 0)
            {
                if (explicitDialogue == null) _activeDialogue.TryGetValue(qid + ":" + iid, out explicitDialogue);
                if (explicitDialogue != null && _store.Definitions.TryGetValue(qid, out var def) && def.Dialogues.TryGetValue(explicitDialogue, out var node))
                {
                    payload["selectedQuest"] = qid;
                    payload["selectedInteraction"] = iid;
                    payload["dialogue"] = BuildDialoguePayload(def, node);
                }
            }
            return payload;
        }

        private static JObject BuildDialoguePayload(QuestDefinition def, QuestDialogueNode node)
        {
            var result = new JObject
            {
                ["speaker"] = node.Speaker,
                ["text"] = node.Text,
                ["image"] = node.Image,
                ["options"] = new JArray()
            };
            var arr = (JArray)result["options"]!;
            foreach (var option in node.Options)
            {
                arr.Add(new JObject
                {
                    ["text"] = option.Text,
                    ["enabled"] = option.Requirements == null,
                    ["close"] = option.Close
                });
            }
            return result;
        }

        private static string DisplayItemName(string id)
        {
            return id switch
            {
                "special_marinade_meat" => "Мясо в спецмаринаде",
                "legendary_shashlik" => "Легендарный шашлык от Руслана",
                _ => id
            };
        }

        private void SendError(string text)
        {
            Broadcast(new JObject { ["command"] = "quest_error", ["text"] = text });
        }

        private void Broadcast(JObject payload)
        {
            try
            {
                _server?.WebSocketServices["/"]?.Sessions.Broadcast(payload.ToString(Formatting.None));
            }
            catch { }
        }

        private async Task UpdateOverlayAsync(bool paused, IReadOnlyList<QuestPointSnapshot> nearby)
        {
            bool shouldShow = paused && _store.Settings.Enabled && (nearby.Count > 0 || GetProgress("special_marinated_shashlik").Status == QuestStatus.Active);
            if (shouldShow == _overlayVisible)
            {
                if (shouldShow) await FocusOverlayAsync().ConfigureAwait(true);
                return;
            }

            if (shouldShow)
            {
                _overlayVisible = true;
                await EnsureOverlayAsync().ConfigureAwait(true);
                FocusOverlay();
                BroadcastState(force: true);
            }
            else
            {
                _overlayVisible = false;
                HideOverlay();
            }
        }

        private async Task FocusOverlayAsync()
        {
            if (_overlayProcess == null || _overlayProcess.HasExited)
                await EnsureOverlayAsync().ConfigureAwait(true);
            FocusOverlay();
        }

        private async Task EnsureOverlayAsync()
        {
            if (_overlayProcess != null && !_overlayProcess.HasExited) return;
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "bin", "WebOverlay.exe");
            string url = "http://localhost:8082/web_quests.html";
            if (!File.Exists(exe))
            {
                Logger.Current?.Data("[QUEST] WebOverlay.exe not found: " + exe);
                return;
            }
            try
            {
                _overlayProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = url,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(exe)!
                    }
                };
                _overlayProcess.Start();
                try { await Task.Delay(250).ConfigureAwait(true); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Current?.Data("[QUEST] overlay start: " + ex.Message);
            }
        }

        private void FocusOverlay()
        {
            if (!_paused || !_overlayVisible) return;
            try
            {
                var proc = _overlayProcess;
                if (proc == null || proc.HasExited) return;
                proc.Refresh();
                IntPtr h = proc.MainWindowHandle;
                if (h == IntPtr.Zero) return;
                ShowWindow(h, SwShow);
                SetForegroundWindow(h);
                BringWindowToTop(h);
            }
            catch { }
        }

        private void HideOverlay()
        {
            try
            {
                var proc = _overlayProcess;
                if (proc == null || proc.HasExited) return;
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero) ShowWindow(proc.MainWindowHandle, SwHide);
            }
            catch { }
        }

        private void EnforceArPointDebugMode()
        {
            try
            {
                // Existing AR point pump treats this value as the legacy «show static points radius».
                // Zero means no legacy nearby-point contribution; quest AR overlay is independent.
                AppSettings.ArDisplayRadiusM = _store.Settings.DebugShowAllPoints ? (int)Math.Clamp(_store.Settings.DebugRadiusM, 5, 5000) : 0;
                if (_host.IsHandleCreated) _host.BeginInvoke(new Action(() => _host.Invalidate()));
            }
            catch { }
        }

        private void SendMapAndArRefresh()
        {
            try
            {
                var main = _host;
                main.SendCommandToMap("quest_state_changed", new JObject { ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            }
            catch { }
            BroadcastState(force: true);
        }

        private static double DistanceSquared(double x, double y, double z, double tx, double ty, double tz)
        {
            double dx = x - tx, dy = y - ty, dz = z - tz;
            return dx * dx + dy * dy + dz * dz;
        }

        internal IReadOnlyList<QuestPointSnapshot> GetQuestPointsForEditor()
        {
            var result = new List<QuestPointSnapshot>();
            foreach (var def in _store.Definitions.Values)
                foreach (var interaction in def.Interactions)
                    if (TryBuildInteraction(def, interaction, out var p) && p.Interactive) result.Add(p);
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _tickTimer?.Dispose(); } catch { }
            _tickTimer = null;
            try { _server?.Stop(); } catch { }
            try { _server?.Dispose(); } catch { }
            _server = null;
            HideOverlay();
            try { if (_overlayProcess != null && !_overlayProcess.HasExited) _overlayProcess.CloseMainWindow(); } catch { }
            _overlayProcess?.Dispose();
            _overlayProcess = null;
            try { TruckTelemetry.Stop(); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private void BeginInvokeUi(Action action)
        {
            try
            {
                if (_host.IsDisposed) return;
                if (_host.InvokeRequired) _host.BeginInvoke(action); else action();
            }
            catch { }
        }
    }
}