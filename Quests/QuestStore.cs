using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace ETS2_Assist_GUI.Quests
{
    internal sealed class QuestStore
    {
        private const string StateMagic = "ETSA-QUEST-STATE-1";
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
                    Definitions[def.Id] = def;
                    EnsureQuestProgress(def);
                }
                catch (Exception ex)
                {
                    Logger.Current?.Data($"[QUEST] Не удалось загрузить '{file}': {ex.Message}");
                }
            }
            SaveState();
        }

        public void ResetQuest(string id)
        {
            if (!Definitions.TryGetValue(id, out var def)) return;
            State.Quests[id] = new QuestProgress
            {
                Status = QuestStatus.Available,
                Step = def.Steps.ContainsKey("available") ? "available" : "",
                ReturnOffer = false,
                ChangedUtc = DateTime.UtcNow
            };

            foreach (string key in State.PermanentInteractionNames.Keys.Where(k => k.StartsWith(id + ":", StringComparison.OrdinalIgnoreCase)).ToList())
                State.PermanentInteractionNames.Remove(key);

            State.Inventory.Clear();
            SaveState();
        }

        public void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                byte[] plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(State, Formatting.None));
                byte[] protectedBytes = Dpapi.Protect(plain);

                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    bw.Write(StateMagic);
                    bw.Write(1);
                    bw.Write(protectedBytes.Length);
                    bw.Write(protectedBytes);
                }
                File.WriteAllBytes(_statePath, ms.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] Ошибка сохранения состояния: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] Ошибка сохранения настроек: {ex.Message}");
            }
        }

        private void EnsureQuestProgress(QuestDefinition def)
        {
            if (State.Quests.ContainsKey(def.Id)) return;
            State.Quests[def.Id] = new QuestProgress
            {
                Status = QuestStatus.Available,
                Step = def.Steps.ContainsKey("available") ? "available" : "",
                ChangedUtc = DateTime.UtcNow
            };
        }

        private QuestPersistentState LoadState()
        {
            try
            {
                if (!File.Exists(_statePath)) return new QuestPersistentState();
                byte[] raw = File.ReadAllBytes(_statePath);
                using var ms = new MemoryStream(raw);
                using var br = new BinaryReader(ms, Encoding.UTF8, true);
                if (!string.Equals(br.ReadString(), StateMagic, StringComparison.Ordinal)) return new QuestPersistentState();
                _ = br.ReadInt32();
                int length = br.ReadInt32();
                if (length < 1 || length > ms.Length - ms.Position) return new QuestPersistentState();
                byte[] protectedBytes = br.ReadBytes(length);
                byte[] plain = Dpapi.Unprotect(protectedBytes);
                return JsonConvert.DeserializeObject<QuestPersistentState>(Encoding.UTF8.GetString(plain)) ?? new QuestPersistentState();
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] Не удалось прочитать quest_state.bin: {ex.Message}");
                return new QuestPersistentState();
            }
        }

        private QuestSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return new QuestSettings();
                return JsonConvert.DeserializeObject<QuestSettings>(File.ReadAllText(_settingsPath, Encoding.UTF8)) ?? new QuestSettings();
            }
            catch (Exception ex)
            {
                Logger.Current?.Data($"[QUEST] Не удалось прочитать quest_settings.json: {ex.Message}");
                return new QuestSettings();
            }
        }

        private static class Dpapi
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct DATA_BLOB
            {
                public int cbData;
                public IntPtr pbData;
            }

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptProtectData(
                ref DATA_BLOB pDataIn,
                string? szDataDescr,
                IntPtr pOptionalEntropy,
                IntPtr pvReserved,
                IntPtr pPromptStruct,
                int dwFlags,
                out DATA_BLOB pDataOut);

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptUnprotectData(
                ref DATA_BLOB pDataIn,
                IntPtr ppszDataDescr,
                IntPtr pOptionalEntropy,
                IntPtr pvReserved,
                IntPtr pPromptStruct,
                int dwFlags,
                out DATA_BLOB pDataOut);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr LocalFree(IntPtr hMem);

            public static byte[] Protect(byte[] data)
            {
                return Transform(data, protect: true);
            }

            public static byte[] Unprotect(byte[] data)
            {
                return Transform(data, protect: false);
            }

            private static byte[] Transform(byte[] input, bool protect)
            {
                if (input.Length == 0) return Array.Empty<byte>();
                var inputBlob = new DATA_BLOB { cbData = input.Length, pbData = Marshal.AllocHGlobal(input.Length) };
                try
                {
                    Marshal.Copy(input, 0, inputBlob.pbData, input.Length);
                    bool ok = protect
                        ? CryptProtectData(ref inputBlob, "ETS2 Assist quest state", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outputBlob)
                        : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
                    if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                    try
                    {
                        byte[] output = new byte[outputBlob.cbData];
                        Marshal.Copy(outputBlob.pbData, output, 0, output.Length);
                        return output;
                    }
                    finally
                    {
                        if (outputBlob.pbData != IntPtr.Zero) LocalFree(outputBlob.pbData);
                    }
                }
                finally
                {
                    if (inputBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inputBlob.pbData);
                }
            }
        }
    }
}