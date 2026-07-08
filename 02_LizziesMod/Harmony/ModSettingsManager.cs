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

    public static class ModSettingsManager
    {

        public static Dictionary<string, List<ModSetting>> AllModSettings = new Dictionary<string, List<ModSetting>>();
        public static bool PendingRestart = false;
        public static void LoadAllModSettings()
        {
            Logger.Info("Scanning for ModSettings.xml across all loaded mods...");

            foreach (Mod mod in ModManager.GetLoadedMods())
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
                                {
                                    bool.TryParse(node.Attributes["requiresRestart"].Value, out bRequiresRestart);
                                }

                                bool bHidden = false;
                                if (node.Attributes["hidden"] != null)
                                {
                                    bool.TryParse(node.Attributes["hidden"].Value, out bHidden);
                                }

                                ModSetting existingSetting = currentSettings.Find(s => s.Name.Equals(sName, StringComparison.OrdinalIgnoreCase));

                                if (existingSetting != null)
                                {
                                    existingSetting.Value = sValue;
                                    existingSetting.Type = sType;
                                    existingSetting.requiresRestart = bRequiresRestart;
                                    existingSetting.Hidden = bHidden;
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
                                        Hidden = bHidden
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

        public static void SetSetting<T>(string modName, string settingName, T value)
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
            }
            else
            {
           
                string inferredType = "string";
                if (typeof(T) == typeof(bool)) inferredType = "bool";
                else if (typeof(T) == typeof(int)) inferredType = "int";
                else if (typeof(T) == typeof(float)) inferredType = "float";


                bool bHidden = false;
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
                    Hidden = bHidden
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

            ModBlocker.BypassingFilter = true;
            List<Mod> allMods = ModManager.GetLoadedMods();
            ModBlocker.BypassingFilter = false;

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