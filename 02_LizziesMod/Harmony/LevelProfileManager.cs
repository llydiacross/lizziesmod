using HarmonyLib;
using System;
using System.IO;
using System.Xml;

namespace LizziesMod
{
    public static class LevelProfileManager
    {
        public static void SaveLevelProfile()
        {
            if (SingletonMonoBehaviour<ConnectionManager>.Instance == null || !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
                return;

            string saveDir = GameIO.GetSaveGameDir();
            if (string.IsNullOrEmpty(saveDir)) return;

            string path = Path.Combine(saveDir, "LevelModProfile.xml");

            string currentProfileName = ModSettingsManager.GetSetting("LizziesMod", "LastProfileName", "Default");

            XmlDocument xmlDoc = new XmlDocument();
            XmlElement root = xmlDoc.CreateElement("LevelProfile");
            root.SetAttribute("profileName", currentProfileName);
            xmlDoc.AppendChild(root);

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                XmlElement modNode = xmlDoc.CreateElement("Mod");
                modNode.SetAttribute("name", mod.Name);
                modNode.SetAttribute("version", mod.VersionString ?? "Unknown");

                bool isEnabled = ModController.IsModEnabled(mod.Name);
                modNode.SetAttribute("enabled", isEnabled.ToString().ToLower());

                root.AppendChild(modNode);
            }

            xmlDoc.Save(path);
            Logger.Info($"[LevelProfileManager] Saved active profile '{currentProfileName}' to world save directory.");
        }

        public static void BackupSaveDirectory(string saveDir)
        {
            try
            {
                string backupRoot = Path.Combine(saveDir, "LizziesMod_Backups");
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string targetBackupDir = Path.Combine(backupRoot, "Backup_" + timestamp);

                if (!Directory.Exists(targetBackupDir))
                {
                    Directory.CreateDirectory(targetBackupDir);
                }

                // Copy the critical state files before any modification happens
                string[] filesToBackup = new string[] { "main.ttw", "players.xml", "LevelModProfile.xml" };
                foreach (string fileName in filesToBackup)
                {
                    string sourceFile = Path.Combine(saveDir, fileName);
                    if (File.Exists(sourceFile))
                    {
                        File.Copy(sourceFile, Path.Combine(targetBackupDir, fileName), true);
                    }
                }
                Logger.Info($"[LevelProfileManager] Created safety backup at: {targetBackupDir}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[LevelProfileManager] Failed to create safety backup: {ex.Message}");
            }
        }

        public static bool VerifyLevelProfile(string path, out string warningText)
        {
            warningText = "";
            if (!File.Exists(path)) return false;

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(path);

                XmlNode root = xmlDoc.DocumentElement;
                if (root == null) return false;

                System.Collections.Generic.List<Mod> activeMods = global::ModManager.GetLoadedMods();
                System.Collections.Generic.List<string> missing = new System.Collections.Generic.List<string>();
                System.Collections.Generic.List<string> mismatched = new System.Collections.Generic.List<string>();

                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node.Name != "Mod") continue;

                    string name = node.Attributes["name"]?.Value;
                    string savedVersion = node.Attributes["version"]?.Value ?? "Unknown";
                    string savedEnabledAttr = node.Attributes["enabled"]?.Value ?? "true";
                    bool savedEnabled = savedEnabledAttr.Equals("true", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(name) || !savedEnabled) continue;

                    Mod currentMod = activeMods.Find(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (currentMod == null)
                    {
                        missing.Add($"- {name} (Expected v{savedVersion})");
                    }
                    else if (currentMod.VersionString != savedVersion)
                    {
                        mismatched.Add($"- {name}: Expected v{savedVersion}, running v{currentMod.VersionString}");
                    }
                }

                if (missing.Count > 0 || mismatched.Count > 0)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("The current mod configuration does not match this world save!\n");

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

                    sb.AppendLine("Loading anyway can drop items or corrupt your save.\n\nProceed and create a safe folder backup?");
                    warningText = sb.ToString();
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[LevelProfileManager] Error verifying profile: {e.Message}");
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "StartGame")]
    public class GameManager_StartGame_Patch
    {
        private static bool bypassWarning = false;

        public static bool Prefix(GameManager __instance, out bool __state)
        {
            __state = false;

            if (bypassWarning)
            {
                bypassWarning = false;
                __state = true;
                return true;
            }

            if (SingletonMonoBehaviour<ConnectionManager>.Instance != null && !SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                __state = true;
                return true;
            }

            string saveDir = GameIO.GetSaveGameDir();
            if (string.IsNullOrEmpty(saveDir)) return true;

            string profilePath = Path.Combine(saveDir, "LevelModProfile.xml");
            string ttwPath = Path.Combine(saveDir, "main.ttw");

            if (File.Exists(profilePath))
            {
                if (LevelProfileManager.VerifyLevelProfile(profilePath, out string mismatchWarning))
                {
                    XUiC_MessageBoxWindowGroup.ShowOkCancel(
                        LocalPlayerUI.primaryUI.mXUi,
                        "MOD MISMATCH DETECTED",
                        mismatchWarning,
                        "",
                        () =>
                        {
                            Logger.Info("[LevelProfileManager] Mismatch bypassed. Creating folder backup.");
                            LevelProfileManager.BackupSaveDirectory(saveDir);
                            bypassWarning = true;

                   
                            bool isOffline = SingletonMonoBehaviour<ConnectionManager>.Instance.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
                            __instance.StartGame(isOffline);
                        },
                        () =>
                        {
                            Logger.Info("[LevelProfileManager] Load aborted due to mismatch profile. Quitting to desktop.");
                            UnityEngine.Application.Quit();
                        }
                    );
                    return false;
                }

                __state = true;
                return true;
            }

            if (!File.Exists(ttwPath))
            {
                __state = true;
                return true;
            }

            string warningTitle = "MOD WARNING";
            string warningText = "You are attempting to load a save file that does not have a Level Mod Profile attached to it.\n\n" +
                                 "Loading a previously un-modded save with active mods may cause unpredictable behavior, missing items, or corruption.\n\n" +
                                 "Do you wish to proceed and inject your current mod profile into this save?";

            XUiC_MessageBoxWindowGroup.ShowOkCancel(
                LocalPlayerUI.primaryUI.xui,
                warningTitle,
                warningText,
                "",
                () =>
                {
                    Logger.Info("[LevelProfileManager] Injecting configuration into legacy save. Creating backup.");
                    LevelProfileManager.BackupSaveDirectory(saveDir);
                    bypassWarning = true;

                    // Re-calculate the offline status directly from the ConnectionManager
                    bool isOffline = SingletonMonoBehaviour<ConnectionManager>.Instance.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
                    __instance.StartGame(isOffline);
                },
                () =>
                {
                    Logger.Info("[LevelProfileManager] Aborted load sequence. Quitting to desktop.");
                    UnityEngine.Application.Quit();
                }
            );

            return false;
        }

        public static void Postfix(bool __state)
        {
            if (__state)
            {
                LevelProfileManager.SaveLevelProfile();
            }
        }
    }


    [HarmonyPatch(typeof(GameManager), "RequestToSpawnPlayer")]
    public class GameManager_RequestToSpawnPlayer_Patch
    {
        public static void Postfix(ClientInfo _cInfo)
        {
            if (_cInfo != null && SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
            {
                System.Collections.Generic.Dictionary<string, SyncModInfo> activeServerMods = new System.Collections.Generic.Dictionary<string, SyncModInfo>();

                foreach (Mod mod in global::ModManager.GetLoadedMods())
                {
                    if (ModController.IsModEnabled(mod.Name))
                    {
                        SyncModInfo info = new SyncModInfo();
                        info.Version = mod.VersionString ?? "Unknown";

             
                        if (ModSettingsManager.AllModSettings.TryGetValue(mod.Name, out var settings))
                        {
                            foreach (var setting in settings)
                            {

                                if (!setting.ServerOnly && !setting.Hidden && !setting.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                                {
                                    info.Settings.Add(setting);
                                }
                            }
                        }

                        activeServerMods.Add(mod.Name, info);
                    }
                }

                _cInfo.SendPackage(NetPackageManager.GetPackage<NetPackageLevelProfile>().Setup(activeServerMods));
                Logger.Info($"[LevelProfileManager] Sent active mod profile and synced settings to connecting player: {_cInfo.playerName}");
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), "SaveWorld")]
    public class GameManager_SaveWorld_Patch
    {
        public static void Postfix()
        {
            LevelProfileManager.SaveLevelProfile();
        }
    }
}