using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System;

namespace LizziesMod
{

    public class ModSetting
    {
        public string ModName;
        public string Name;
        public string Value;
        public string Type;
        public Action<string> OnValueChanged;
        public bool requiresRestart;
        public bool Hidden;
        public bool ServerOnly;
        public bool inMenuOnly;

        public void SetValue(string newValue)
        {
            if (Value != newValue)
            {
                Value = newValue;
                Logger.Info($"Setting '{Name}' for mod '{ModName}' changed to: {newValue} {(OnValueChanged != null ? "INVOKABLE" : "NON-INVOKABLE") }");
 
                if (requiresRestart || Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    ModSettingsManager.PendingRestart = true;
                }

                OnValueChanged?.Invoke(newValue);
            }
        }
    }

    public class ModProfileInfo
    {
        public string Name;
        public List<string> EnabledMods = new List<string>();
        public List<string> DisabledMods = new List<string>();
        public int TotalSettingsModified = 0;
    }

    public static class ModSettingsManager
    {

        public static Dictionary<string, List<ModSetting>> AllModSettings = new Dictionary<string, List<ModSetting>>();
        public static bool PendingRestart = false;
        public static void LoadAllModSettings()
        {
            Logger.Info("Scanning for ModSettings.xml across all loaded mods...");

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {

                if (!AllModSettings.ContainsKey(mod.Name))
                {
                    AllModSettings[mod.Name] = new List<ModSetting>();
                }

                string settingsPath = Path.Combine(mod.Path, "ModSettings.xml");

                if (File.Exists(settingsPath))
                {
                    Logger.Info($"Found ModSettings.xml for: {mod.Name}");

                    List<ModSetting> currentSettings = AllModSettings[mod.Name];
                    List<ModSetting> updatedSettings = new List<ModSetting>();

                    try
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(settingsPath);

                        foreach (XmlNode node in xmlDoc.DocumentElement.ChildNodes)
                        {
                            if (node.Name == "Setting")
                            {
                                string sName = node.Attributes["name"]?.Value ?? "Unknown";
                                string sValue = node.Attributes["value"]?.Value ?? "";
                                string sType = node.Attributes["type"]?.Value ?? "string";

                                bool bRequiresRestart = false;
                                if (node.Attributes["requiresRestart"] != null)
                                    bool.TryParse(node.Attributes["requiresRestart"].Value, out bRequiresRestart);

                                bool bHidden = false;
                                if (node.Attributes["hidden"] != null)
                                    bool.TryParse(node.Attributes["hidden"].Value, out bHidden);

                                bool bServerOnly = false;
                                if (node.Attributes["serverOnly"] != null)
                                    bool.TryParse(node.Attributes["serverOnly"].Value, out bServerOnly);

                                bool bMenuOnly = false;
                                if (node.Attributes["menuOnly"] != null)
                                    bool.TryParse(node.Attributes["menuOnly"].Value, out bServerOnly);

                                ModSetting existingSetting = currentSettings.Find(s => s.Name.Equals(sName, StringComparison.OrdinalIgnoreCase));
                                if (existingSetting != null)
                                {
                                    existingSetting.Value = sValue;
                                    existingSetting.Type = sType;
                                    existingSetting.requiresRestart = bRequiresRestart;
                                    existingSetting.Hidden = bHidden;
                                    existingSetting.ServerOnly = bServerOnly;
                                    existingSetting.inMenuOnly = bMenuOnly;
                                    updatedSettings.Add(existingSetting);
                                }
                                else
                                {
                                    updatedSettings.Add(new ModSetting
                                    {
                                        ModName = mod.Name,
                                        Name = sName,
                                        Value = sValue,
                                        Type = sType,
                                        requiresRestart = bRequiresRestart,
                                        Hidden = bHidden,
                                        inMenuOnly = bMenuOnly,
                                        ServerOnly = bServerOnly
                                    });
                                }
                            }
                        }

                        AllModSettings[mod.Name] = updatedSettings;
                    }
                    catch (System.Exception e)
                    {
                        Logger.Error($"Failed to parse ModSettings.xml for {mod.Name}: {e.Message}");
                    }
                }
            }
        }


        private static string GetTargetSaveProfilePath()
        {

            Mod lizziesMod = global::ModManager.GetLoadedMods().Find(m => m.Name == "LizziesMod");
            if (lizziesMod != null)
            {
                return Path.Combine(lizziesMod.Path, "ModProfiles.xml");
            }
            return "ModProfiles.xml"; 
        }

        private static List<string> GetAllProfilePaths()
        {
            List<string> paths = new List<string>();
            string lizziesPath = null;

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                string path = Path.Combine(mod.Path, "ModProfiles.xml");
                if (File.Exists(path))
                {
                    if (mod.Name.Equals("LizziesMod", StringComparison.OrdinalIgnoreCase))
                        lizziesPath = path;
                    else
                        paths.Add(path);
                }
            }

            if (lizziesPath != null)
            {
                paths.Add(lizziesPath);
            }

            return paths;
        }
        public static List<ModProfileInfo> GetAvailableProfiles()
        {
 
            Dictionary<string, ModProfileInfo> profilesDict = new Dictionary<string, ModProfileInfo>(StringComparer.OrdinalIgnoreCase);

            List<string> allPaths = GetAllProfilePaths();

            foreach (string path in allPaths)
            {
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(path);

                    XmlNode root = xmlDoc.DocumentElement;
                    if (root == null) continue;

                    foreach (XmlNode profileNode in root.ChildNodes)
                    {
                        if (profileNode.Name != "Profile") continue;

                        string pName = profileNode.Attributes["name"]?.Value;
                        if (string.IsNullOrEmpty(pName)) continue;

                        ModProfileInfo info = new ModProfileInfo { Name = pName };

                        foreach (XmlNode modNode in profileNode.ChildNodes)
                        {
                            if (modNode.Name != "Mod") continue;

                            string modName = modNode.Attributes["name"]?.Value;
                            if (string.IsNullOrEmpty(modName)) continue;

                            bool foundEnabledSetting = false;
                            foreach (XmlNode settingNode in modNode.ChildNodes)
                            {
                                if (settingNode.Name == "Setting")
                                {
                                    info.TotalSettingsModified++;
                                    string sName = settingNode.Attributes["name"]?.Value;
                                    string sValue = settingNode.Attributes["value"]?.Value;

                                    if (sName != null && sName.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundEnabledSetting = true;
                                        if (sValue != null && sValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                                            info.EnabledMods.Add(modName + $" [{ModManager.GetMod(modName).VersionString}]");
                                        else
                                            info.DisabledMods.Add(modName + $" [{ModManager.GetMod(modName).VersionString}]");
                                    }
                                }
                            }

                            if (!foundEnabledSetting) info.EnabledMods.Add(modName + $" [{ModManager.GetMod(modName).VersionString}]");
                        }

                        // Add or overwrite the profile in our dictionary
                        profilesDict[pName] = info;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[ModProfiles] Failed to read profiles from {path}: {e.Message}");
                }
            }

            return new List<ModProfileInfo>(profilesDict.Values);
        }

        public static void SaveProfile(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return;

            string path = GetTargetSaveProfilePath();
            XmlDocument xmlDoc = new XmlDocument();

            if (File.Exists(path))
            {
                try { xmlDoc.Load(path); }
                catch { xmlDoc.AppendChild(xmlDoc.CreateElement("ModProfiles")); }
            }
            else
            {
                xmlDoc.AppendChild(xmlDoc.CreateElement("ModProfiles"));
            }

            XmlNode root = xmlDoc.DocumentElement;
            if (root == null)
            {
                root = xmlDoc.CreateElement("ModProfiles");
                xmlDoc.AppendChild(root);
            }

            XmlNode existingProfile = root.SelectSingleNode($"Profile[@name='{profileName}']");
            if (existingProfile != null) root.RemoveChild(existingProfile);

            XmlElement profileNode = xmlDoc.CreateElement("Profile");
            profileNode.SetAttribute("name", profileName);

            foreach (var modKvp in AllModSettings)
            {
                string modName = modKvp.Key;
                XmlElement modNode = xmlDoc.CreateElement("Mod");
                modNode.SetAttribute("name", modName);

                Mod targetMod = global::ModManager.GetLoadedMods().Find(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
                if (targetMod != null && !string.IsNullOrEmpty(targetMod.VersionString))
                {
                    modNode.SetAttribute("version", targetMod.VersionString);
                }

                bool foundEnabled = false;

                foreach (var setting in modKvp.Value)
                {
                    if (setting.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        foundEnabled = true;
                    }

                    XmlElement settingNode = xmlDoc.CreateElement("Setting");
                    settingNode.SetAttribute("name", setting.Name);
                    settingNode.SetAttribute("value", setting.Value);
                    settingNode.SetAttribute("type", setting.Type);
                    if (setting.ServerOnly) settingNode.SetAttribute("serverOnly", "true");
                    if (setting.Hidden || setting.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) settingNode.SetAttribute("hidden", "true");
                    if (setting.inMenuOnly) settingNode.SetAttribute("menuOnly", "true");
                    modNode.AppendChild(settingNode);
                }

                if (!foundEnabled)
                {
                    XmlElement enabledNode = xmlDoc.CreateElement("Setting");
                    enabledNode.SetAttribute("name", "Enabled");
                    enabledNode.SetAttribute("value", "true");
                    enabledNode.SetAttribute("hidden", "true");
                    enabledNode.SetAttribute("type", "bool");
                    modNode.AppendChild(enabledNode);
                }

                profileNode.AppendChild(modNode);
            }

            root.AppendChild(profileNode);
            xmlDoc.Save(path);
            Logger.Info($"[ModProfiles] Saved profile '{profileName}'");
        }
        public static bool LoadProfile(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return false;

            List<string> allPaths = GetAllProfilePaths();
            allPaths.Reverse();

            foreach (string path in allPaths)
            {
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(path);
                    XmlNode profileNode = xmlDoc.DocumentElement?.SelectSingleNode($"Profile[@name='{profileName}']");
                    if (profileNode == null) continue;

                    foreach (var mod in AllModSettings.Keys)
                    {
                        ModSetting enabledSetting = AllModSettings[mod].Find(s => s.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase));
                        if (enabledSetting != null)
                        {
                            enabledSetting.SetValue("false");
                        }
                    }

                    foreach (XmlNode modNode in profileNode.ChildNodes)
                    {
                        if (modNode.Name != "Mod") continue;
                        string modName = modNode.Attributes["name"]?.Value;
                        if (string.IsNullOrEmpty(modName)) continue;

                        if (!AllModSettings.ContainsKey(modName)) continue;

                        foreach (XmlNode settingNode in modNode.ChildNodes)
                        {
                            if (settingNode.Name != "Setting") continue;
                            string sName = settingNode.Attributes["name"]?.Value;
                            string sValue = settingNode.Attributes["value"]?.Value;

                            ModSetting existing = AllModSettings[modName].Find(s => s.Name.Equals(sName, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                existing.SetValue(sValue ?? "");
                            }
                            else
                            {

                                string sType = settingNode.Attributes["type"]?.Value ?? "string";
                                bool bServerOnly = false;
                                if (settingNode.Attributes["serverOnly"] != null)
                                    bool.TryParse(settingNode.Attributes["serverOnly"].Value, out bServerOnly);

                                bool bRequiresRestart = false;
                                if (settingNode.Attributes["requiresRestart"] != null)
                                    bool.TryParse(settingNode.Attributes["requiresRestart"].Value, out bRequiresRestart);

                                bool bHidden = false;
                                if (settingNode.Attributes["hidden"] != null)
                                    bool.TryParse(settingNode.Attributes["hidden"].Value, out bHidden);

                                bool bMenuOnly = false;
                                if (settingNode.Attributes["menuOnly"] != null)
                                    bool.TryParse(settingNode.Attributes["menuOnly"].Value, out bServerOnly);

                                ModSetting newSetting = new ModSetting
                                {
                                    ModName = modName,
                                    Name = sName,
                                    Value = sValue ?? "",
                                    Type = sType,
                                    requiresRestart = bRequiresRestart,
                                    Hidden = bHidden,
                                    ServerOnly = bServerOnly,
                                    inMenuOnly = bMenuOnly
                                };
                                AllModSettings[modName].Add(newSetting);
                                newSetting.OnValueChanged?.Invoke(sValue);
                            }
                        }
                        SaveModSettings(modName);
                    }

                    Logger.Info($"[ModProfiles] Successfully applied profile '{profileName}'");
                    return true;
                }
                catch (Exception e)
                {
                    Logger.Error($"[ModProfiles] Error loading profile '{profileName}': {e.Message}");
                }
            }
            return false;
        }

        public static void SetSetting<T>(string modName, string settingName, T value, bool isHidden = false, bool isServerOnly = false)
        {
            if (!AllModSettings.ContainsKey(modName))
            {
                AllModSettings[modName] = new List<ModSetting>();
            }

            List<ModSetting> settings = AllModSettings[modName];
            ModSetting setting = settings.Find(s => s.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase));

            string newValueString;
            if (typeof(T) == typeof(bool))
            {
                newValueString = value.ToString().ToLower();
            }
            else
            {
                newValueString = value?.ToString() ?? "";
            }

            if (setting != null)
            {
                setting.SetValue(newValueString);
                if (isHidden) setting.Hidden = true;
            }
            else
            {
                string inferredType = "string";
                if (typeof(T) == typeof(bool)) inferredType = "bool";
                else if (typeof(T) == typeof(int)) inferredType = "int";
                else if (typeof(T) == typeof(float)) inferredType = "float";

                bool bHidden = isHidden;

                if (settingName.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && newValueString == "false")
                {
                    PendingRestart = true;
                    bHidden = true;
                }

                ModSetting newSetting = new ModSetting
                {
                    ModName = modName,
                    Name = settingName,
                    Value = newValueString,
                    Type = inferredType,
                    requiresRestart = PendingRestart,
                    Hidden = bHidden,
                    ServerOnly = isServerOnly
                };

                settings.Add(newSetting);
                Logger.Info($"[ModSettingsManager] Created setting '{settingName}' ({inferredType}) for mod '{modName}' initialized to: {newValueString}");

                newSetting.OnValueChanged?.Invoke(newValueString);
            }
        }

        public static T GetSetting<T>(string modName, string settingName, T defaultValue = default)
        {
            if (AllModSettings.TryGetValue(modName, out List<ModSetting> settings))
            {
                foreach (ModSetting setting in settings)
                {
                    if (setting.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (typeof(T) == typeof(string))
                            {
                                return (T)(object)setting.Value;
                            }
                            return (T)Convert.ChangeType(setting.Value, typeof(T));
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Failed to convert setting '{settingName}' ({setting.Value}) for [{modName}] to type {typeof(T).Name}: {e.Message}");
                            return defaultValue;
                        }
                    }
                }
            }

   
            return defaultValue;
        }

        public static bool RegisterCallback(string modName, string settingName, Action<string> callback)
        {
            if (AllModSettings.TryGetValue(modName, out List<ModSetting> settings))
            {
                foreach (ModSetting setting in settings)
                {
                    if (setting.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase))
                    {
                        setting.OnValueChanged += callback;
                        Logger.Info($"Successfully registered callback for [{modName}] -> '{settingName}'");
                        callback?.Invoke(setting.Value);
                        return true;
                    }
                }
            }

            Logger.Warning($"Failed to register callback: Setting '{settingName}' not found for mod '{modName}'.");
            return false;
        }
    

        public static void SaveModSettings(string modName)
        {

            ModPatcher.ShowDisabledMods = true;
            List<Mod> allMods = global::ModManager.GetLoadedMods();
            ModPatcher.ShowDisabledMods = false;

            Mod targetMod = null;
            foreach (var m in allMods)
            {
                if (m.Name == modName) { targetMod = m; break; }
            }

            if (targetMod == null || !AllModSettings.ContainsKey(modName)) return;

            string settingsPath = Path.Combine(targetMod.Path, "ModSettings.xml");

            XmlDocument xmlDoc = new XmlDocument();
            XmlElement root = xmlDoc.CreateElement("ModSettings");
            xmlDoc.AppendChild(root);

            foreach (var setting in AllModSettings[modName])
            {
                XmlElement node = xmlDoc.CreateElement("Setting");
                node.SetAttribute("name", setting.Name);
                node.SetAttribute("value", setting.Value);
                node.SetAttribute("type", setting.Type);
                node.SetAttribute("requiresRestart", setting.requiresRestart.ToString().ToLower());
                if (setting.Hidden) node.SetAttribute("hidden", "true");

                root.AppendChild(node);
            }

            xmlDoc.Save(settingsPath);
            Logger.Info($"Saved changes to ModSettings.xml for {modName}");
        }
    }
}