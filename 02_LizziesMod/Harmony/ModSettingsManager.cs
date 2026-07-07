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
        public bool OnlyInMainMenu;
        public bool Hidden; 

        public void SetValue(string newValue)
        {
            if (Value != newValue)
            {
                Value = newValue;
                Logger.Info($"Setting '{Name}' for mod '{ModName}' changed to: {newValue} {(OnValueChanged != null ? "INVOKABLE" : "NON-INVOKABLE") }");
                OnValueChanged?.Invoke(newValue);
            }
        }
    }

    public static class ModSettingsManager
    {

        public static Dictionary<string, List<ModSetting>> AllModSettings = new Dictionary<string, List<ModSetting>>();

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

                                bool bOnlyInMainMenu = false;
                                if (node.Attributes["onlyInMainMenu"] != null)
                                {
                                    bool.TryParse(node.Attributes["onlyInMainMenu"].Value, out bOnlyInMainMenu);
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
                                    existingSetting.OnlyInMainMenu = bOnlyInMainMenu;
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
                                        OnlyInMainMenu = bOnlyInMainMenu,
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
            Mod targetMod = null;
            foreach (var m in ModManager.GetLoadedMods())
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
                node.SetAttribute("onlyInMainMenu", setting.OnlyInMainMenu.ToString().ToLower());
                root.AppendChild(node);
            }

            xmlDoc.Save(settingsPath);
            Logger.Info($"Saved changes to ModSettings.xml for {modName}");
        }
    }
}