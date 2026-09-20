using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ETS2_Assist_GUI.Quests
{
    internal sealed class QuestStore
    {
        private const string StateMagic = "ETSA-QUEST-STATE-2";
        private readonly string _statePath;
        private readonly string _settingsPath;
        private readonly string _definitionsPath;

        public QuestPersistentState State { get; private set; }
        public QuestSettings Settings { get; private set; }
        public Dictionary<string, QuestDefinition> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public QuestStore()
        {
            string root = AppDataPaths.UserDataDirectory;
            _statePath = Path.Combine(root, "quest_state.bin");
            _settingsPath = Path.Combine(root, "quest_settings.json");
            _definitionsPath = Path.Combine(AppDataPaths.StaticDataDirectory, "quests");
            State = LoadState();
            Settings = LoadSettings();
            LoadDefinitions();
        }

        public void LoadDefinitions()
        {
            Definitions.Clear();
            Directory.CreateDirectory(_definitionsPath);
            foreach (string file in Directory.EnumerateFiles(_definitionsPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var def = JsonConvert.DeserializeObject<QuestDefinition>(File.ReadAllText(file, Encoding.UTF8));
                    if (def == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                    NormalizeDefinition(def);
                    Definitions[def.Id] = def;
                    EnsureQuestProgress(def);
                }
                catch (Exception ex) { Logger.Current?.Data($"[QUEST] Не удалось загрузить '{file}': {ex.Message}"); }
            }
            NormalizeState();
            SaveState();
        }

        private static void NormalizeDefinition(QuestDefinition def)
        {
            def.RequiredQuests ??= new List<string>();
            def.Excludes ??= new List<string>();
            def.IncompatibleQuests ??= new List<string>();
            def.ResetQuests ??= new List<string>();
            def.Interactions ??= new List<QuestInteractionDefinition>();
            def.Dialogues ??= new Dictionary<string, QuestDialogueNode>(StringComparer.OrdinalIgnoreCase);
            def.Steps ??= new Dictionary<string, QuestStepDefinition>(StringComparer.OrdinalIgnoreCase);
            def.Rewards ??= new List<QuestReward>();
            foreach (var i in def.Interactions)
            {
                i.Source ??= new QuestPointSource();
                i.MarkerByStep ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                i.MinimapVisibleByStep ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                i.ArVisibleByStep ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            ValidateIrreversibleOptions(def);
        }

        /* Невозвратный ответ обязан вести в существующий узел: без Next диалог
           упирается в тупик и после перезахода с середины продолжить его нечем.
           Такая комбинация несовместима — помечаем её и отключаем флаг, чтобы
           игрок не потерял диалог. */
        private static void ValidateIrreversibleOptions(QuestDefinition def)
        {
            foreach (var node in def.Dialogues)
            {
                foreach (QuestDialogueOption option in node.Value.Options ?? new List<QuestDialogueOption>())
                {
                    if (!option.Irreversible) continue;
                    if (!string.IsNullOrWhiteSpace(option.Next) && def.Dialogues.ContainsKey(option.Next)) continue;
                    Logger.Current?.Data(
                        $"[QUEST] '{def.Id}': узел '{node.Key}' — невозвратный ответ '{option.Text}' " +
                        $"не ведёт в существующий узел (next='{option.Next}'). Флаг невозвратности снят: " +
                        "диалог упёрся бы в тупик после перезахода.");
                    option.Irreversible = false;
                }
            }
        }

        /* Закрепить узел продолжения диалога после невозвратного ответа. */
        public void SetDialogueAnchor(string questId, string interactionId, string node)
        {
            if (!State.Quests.TryGetValue(questId, out QuestProgress? progress) || progress == null) return;
            progress.DialogueAnchor ??= new(StringComparer.OrdinalIgnoreCase);
            string key = questId + ":" + interactionId;
            if (string.IsNullOrWhiteSpace(node)) progress.DialogueAnchor.Remove(key);
            else progress.DialogueAnchor[key] = node;
            SaveState();
        }

        public string GetDialogueAnchor(string questId, string interactionId)
        {
            if (!State.Quests.TryGetValue(questId, out QuestProgress? progress) || progress == null) return "";
            return progress.DialogueAnchor != null &&
                   progress.DialogueAnchor.TryGetValue(questId + ":" + interactionId, out string? node)
                ? node ?? "" : "";
        }

        public void ResetQuest(string id, bool clearInventory = false)
        {
            if (!Definitions.TryGetValue(id, out var def)) return;
            ResetQuestStateOnly(id);
            if (clearInventory) State.Inventory.Clear();
            SaveState();
        }

        public void ResetQuestStateOnly(string id)
        {
            if (Definitions.TryGetValue(id, out var def))
            {
                State.Quests[id] = NewProgress(def);
            }
            else
            {
                State.Quests[id] = new QuestProgress();
            }

            foreach (string key in State.PermanentInteractionNames.Keys.Where(k => k.StartsWith(id + ":", StringComparison.OrdinalIgnoreCase)).ToList())
                State.PermanentInteractionNames.Remove(key);
            /* Полный сброс квеста снимает и закреплённые невозвратные узлы:
               иначе после reset диалог продолжался бы с середины. */
            State.Quests[id].DialogueAnchor?.Clear();
            foreach (string key in State.GeneratedPoints.Keys.Where(k => k.StartsWith(id + ":", StringComparison.OrdinalIgnoreCase)).ToList())
                State.GeneratedPoints.Remove(key);
            State.ActivationCounts.Remove(id);
        }

        public void SaveState()
        {
            try
            {
                NormalizeState();
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(State, Formatting.None));
                byte[] protectedBytes = Dpapi.Protect(plain);
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    bw.Write(StateMagic);
                    bw.Write(2);
                    bw.Write(protectedBytes.Length);
                    bw.Write(protectedBytes);
                }
                File.WriteAllBytes(_statePath, ms.ToArray());
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] Ошибка сохранения состояния: {ex.Message}"); }
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] Ошибка сохранения настроек: {ex.Message}"); }
        }

        private QuestProgress NewProgress(QuestDefinition def) => new()
        {
            Status = QuestStatus.Available,
            Step = def.Steps.ContainsKey("available") ? "available" : "",
            ReturnOffer = false,
            ChangedUtc = DateTime.UtcNow
        };

        private void EnsureQuestProgress(QuestDefinition def)
        {
            if (State.Quests == null) State.Quests = new(StringComparer.OrdinalIgnoreCase);
            if (!State.Quests.ContainsKey(def.Id)) State.Quests[def.Id] = NewProgress(def);
        }

        private void NormalizeState()
        {
            State ??= new QuestPersistentState();
            State.Quests ??= new(StringComparer.OrdinalIgnoreCase);
            State.Inventory ??= new(StringComparer.OrdinalIgnoreCase);
            State.NewItems ??= new(StringComparer.OrdinalIgnoreCase);
            State.Reputation ??= new(StringComparer.OrdinalIgnoreCase);
            State.Stats ??= new(StringComparer.OrdinalIgnoreCase);
            State.PermanentInteractionNames ??= new(StringComparer.OrdinalIgnoreCase);
            State.GeneratedPoints ??= new(StringComparer.OrdinalIgnoreCase);
            State.EditorPointOverrides ??= new(StringComparer.OrdinalIgnoreCase);
            State.ActivationCounts ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var progress in State.Quests.Values)
            {
                progress.Flags ??= new(StringComparer.OrdinalIgnoreCase);
                progress.DialogueAnchor ??= new(StringComparer.OrdinalIgnoreCase);
            }
        }

        private QuestPersistentState LoadState()
        {
            try
            {
                if (!File.Exists(_statePath)) return new QuestPersistentState();
                byte[] raw = File.ReadAllBytes(_statePath);
                using var ms = new MemoryStream(raw);
                using var br = new BinaryReader(ms, Encoding.UTF8, true);
                string magic = br.ReadString();
                int version = br.ReadInt32();
                if ((magic != StateMagic && magic != "ETSA-QUEST-STATE-1") || (version < 1 || version > 2)) return new QuestPersistentState();
                int length = br.ReadInt32();
                if (length < 1 || length > ms.Length - ms.Position) return new QuestPersistentState();
                byte[] protectedBytes = br.ReadBytes(length);
                byte[] plain = Dpapi.Unprotect(protectedBytes);
                var loaded = JsonConvert.DeserializeObject<QuestPersistentState>(Encoding.UTF8.GetString(plain)) ?? new QuestPersistentState();
                NormalizeLoadedState(loaded);
                return loaded;
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] Не удалось прочитать quest_state.bin: {ex.Message}"); return new QuestPersistentState(); }
        }

        private static void NormalizeLoadedState(QuestPersistentState state)
        {
            state.Quests ??= new(StringComparer.OrdinalIgnoreCase);
            state.Inventory ??= new(StringComparer.OrdinalIgnoreCase);
            state.NewItems ??= new(StringComparer.OrdinalIgnoreCase);
            state.Reputation ??= new(StringComparer.OrdinalIgnoreCase);
            state.Stats ??= new(StringComparer.OrdinalIgnoreCase);
            state.PermanentInteractionNames ??= new(StringComparer.OrdinalIgnoreCase);
            state.GeneratedPoints ??= new(StringComparer.OrdinalIgnoreCase);
            state.EditorPointOverrides ??= new(StringComparer.OrdinalIgnoreCase);
            state.ActivationCounts ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var p in state.Quests.Values)
            {
                p.Flags ??= new(StringComparer.OrdinalIgnoreCase);
                p.DialogueAnchor ??= new(StringComparer.OrdinalIgnoreCase);
            }
        }

        private QuestSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return new QuestSettings();
                return JsonConvert.DeserializeObject<QuestSettings>(File.ReadAllText(_settingsPath, Encoding.UTF8)) ?? new QuestSettings();
            }
            catch (Exception ex) { Logger.Current?.Data($"[QUEST] Не удалось прочитать quest_settings.json: {ex.Message}"); return new QuestSettings(); }
        }

        private static class Dpapi
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct DATA_BLOB { public int cbData; public IntPtr pbData; }
            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);
            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr hMem);

            public static byte[] Protect(byte[] data) => Transform(data, true);
            public static byte[] Unprotect(byte[] data) => Transform(data, false);
            private static byte[] Transform(byte[] input, bool protect)
            {
                if (input.Length == 0) return Array.Empty<byte>();
                var inputBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
                try
                {
                    Marshal.Copy(input, 0, inputBlob.pbData, input.Length);
                    DATA_BLOB outputBlob;
                    bool ok = protect
                        ? CryptProtectData(ref inputBlob, "ETS2 Assist quest state", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob)
                        : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
                    if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    try
                    {
                        byte[] output = new byte[outputBlob.cbData];
                        Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                        return output;
                    }
                    finally { if (outputBlob.pbData != IntPtr.Zero) LocalFree(outputBlob.pbData); }
                }
                finally { if (inputBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inputBlob.pbData); }
            }
        }
    }
}
