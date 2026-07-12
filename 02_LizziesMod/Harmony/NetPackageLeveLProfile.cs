using System;
using System.Collections.Generic;

namespace LizziesMod
{
  
    public class SyncModInfo
    {
        public string Version;
        public List<ModSetting> Settings = new List<ModSetting>();
    }

    public class NetPackageLevelProfile : NetPackage
    {
        private Dictionary<string, SyncModInfo> serverMods = new Dictionary<string, SyncModInfo>(StringComparer.OrdinalIgnoreCase);

        public NetPackageLevelProfile Setup(Dictionary<string, SyncModInfo> activeMods)
        {
            this.serverMods = activeMods;
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            int modCount = _reader.ReadInt32();
            serverMods.Clear();
            for (int i = 0; i < modCount; i++)
            {
                string name = _reader.ReadString();
                string version = _reader.ReadString();

                SyncModInfo info = new SyncModInfo { Version = version };

                int settingCount = _reader.ReadInt32();
                for (int j = 0; j < settingCount; j++)
                {
                    info.Settings.Add(new ModSetting
                    {
                        Name = _reader.ReadString(),
                        Value = _reader.ReadString(),
                        Type = _reader.ReadString()
                    });
                }

                serverMods[name] = info;
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            _writer.Write(serverMods.Count);
            foreach (var kvp in serverMods)
            {
                _writer.Write(kvp.Key);
                _writer.Write(kvp.Value.Version);

                _writer.Write(kvp.Value.Settings.Count);
                foreach (var setting in kvp.Value.Settings)
                {
                    _writer.Write(setting.Name);
                    _writer.Write(setting.Value);
                    _writer.Write(setting.Type);
                }
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer) return;

            List<Mod> localMods = global::ModManager.GetLoadedMods();
            List<string> missing = new List<string>();
            List<string> mismatched = new List<string>();

            foreach (var kvp in serverMods)
            {
                string name = kvp.Key;
                string expectedVersion = kvp.Value.Version;

                Mod currentMod = localMods.Find(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (currentMod == null)
                {
                    missing.Add($"- {name} (Expected v{expectedVersion})");
                }
                else if (currentMod.VersionString != expectedVersion)
                {
                    mismatched.Add($"- {name}: Server has v{expectedVersion}, you have v{currentMod.VersionString}");
                }
            }

            if (missing.Count > 0 || mismatched.Count > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("You do not have the required mods to play on this server!\n");

                if (missing.Count > 0)
                {
                    sb.AppendLine("[FF3333]Missing Mods:[-]");
                    foreach (string s in missing) sb.AppendLine(s);
                    sb.AppendLine();
                }

                if (mismatched.Count > 0)
                {
                    sb.AppendLine("[FFCC33]Version Mismatches:[-]");
                    foreach (string s in mismatched) sb.AppendLine(s);
                    sb.AppendLine();
                }

                sb.AppendLine("You have been disconnected.");

                GUIWindowManager windowManager = LocalPlayerUI.primaryUI.windowManager;

                XUiC_MessageBoxWindowGroup.ShowOk(
                    windowManager.playerUI.xui,
                    "SERVER MOD MISMATCH",
                    sb.ToString(),
                    "",
                    () =>
                    {
                        SingletonMonoBehaviour<ConnectionManager>.Instance.Disconnect();
                    }
                );
            }
            else
            {

                Logger.Info("[LevelProfileManager] Server mod check passed. Syncing settings and generating 'ServerProfile'.");

                foreach (Mod localMod in localMods)
                {
                    bool isRequiredByServer = serverMods.ContainsKey(localMod.Name);
                    ModSettingsManager.SetSetting(localMod.Name, "Enabled", isRequiredByServer);

                    if (isRequiredByServer)
                    {
                        // Overwrite the client's settings with the ones provided by the server
                        foreach (var serverSetting in serverMods[localMod.Name].Settings)
                        {
                            ModSettingsManager.SetSetting(localMod.Name, serverSetting.Name, serverSetting.Value);
                        }
                    }
                }

                ModSettingsManager.SaveProfile("ServerProfile");
                ModSettingsManager.SetSetting("LizziesMod", "LastProfileName", "ServerProfile", true);

                foreach (string modName in new List<string>(ModSettingsManager.AllModSettings.Keys))
                {
                    ModSettingsManager.SaveModSettings(modName);
                }
            }
        }

        public override int GetLength()
        {
            int len = 4;
            foreach (var kvp in serverMods)
            {
                len += kvp.Key.Length + kvp.Value.Version.Length + 2;
                len += 4;
                foreach (var setting in kvp.Value.Settings)
                {
                    len += setting.Name.Length + setting.Value.Length + setting.Type.Length + 6;
                }
            }
            return len;
        }

        internal NetPackage Setup(Dictionary<string, string> activeServerMods)
        {
            throw new NotImplementedException();
        }
    }
}