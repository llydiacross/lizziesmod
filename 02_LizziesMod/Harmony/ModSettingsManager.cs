using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System;
using UnityEngine;

namespace LizziesMod
{

    public class ModSetting
    {
        public string ModName;
        public string Name;
        public string Value;
        public string PersistedValue;
            public string DefaultValue;
        public string Type;
            public ModSettingPresentation Presentation = new ModSettingPresentation();
        public Action<string> OnValueChanged;
        public bool requiresRestart;
        public ModSettingRestartScope RestartScope = ModSettingRestartScope.None;
        public bool Hidden;
        public bool ServerOnly;
        public bool inMenuOnly;
        public bool Warning;
        private string developerOverrideValue;
        private bool developerDefined;

        public bool IsDeveloperOverridden
        {
            get { return developerOverrideValue != null; }
        }

        public bool IsDeveloperDefined
        {
            get { return developerDefined; }
        }

            public ModSettingControl EffectiveControl
            {
                get { return Presentation.GetEffectiveControl(Type); }
            }

            public string DisplayName
            {
                get { return string.IsNullOrEmpty(Presentation.DisplayName) ? Name : Presentation.DisplayName; }
            }

        public string ValueForPersistence
        {
            get { return PersistedValue ?? Value; }
        }

            public List<ModSettingOption> GetSelectorOptions()
            {
                if (EffectiveControl != ModSettingControl.Selector)
                {
                    return new List<ModSettingOption>();
                }

                if (Presentation.Options.Count > 0)
                {
                    return Presentation.Options;
                }

                if (string.Equals(Type, "int", StringComparison.OrdinalIgnoreCase))
                {
                    return GetIntegerSelectorOptions();
                }

                if (string.Equals(Type, "float", StringComparison.OrdinalIgnoreCase))
                {
                    return GetFloatSelectorOptions();
                }

                return new List<ModSettingOption>();
            }

            public bool TryNormalizeValue(string value, out string normalizedValue)
            {
                normalizedValue = "";
                string candidate = value ?? "";

                if (EffectiveControl == ModSettingControl.Selector)
                {
                    List<ModSettingOption> options = GetSelectorOptions();
                    ModSettingOption matchingOption = options.Find(option =>
                        option.Value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                    if (matchingOption == null) return false;
                    candidate = matchingOption.Value;
                }

                if (EffectiveControl == ModSettingControl.Switch &&
                    (!string.IsNullOrEmpty(Presentation.LeftValue) || !string.IsNullOrEmpty(Presentation.RightValue)))
                {
                    if (candidate.Equals(Presentation.LeftValue, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedValue = Presentation.LeftValue;
                        return true;
                    }

                    if (candidate.Equals(Presentation.RightValue, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedValue = Presentation.RightValue;
                        return true;
                    }

                    return false;
                }

                if (EffectiveControl == ModSettingControl.Color ||
                    string.Equals(Type, "color", StringComparison.OrdinalIgnoreCase))
                {
                    return TryNormalizeColor(candidate, out normalizedValue);
                }

                if (string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase))
                {
                    bool boolValue;
                    if (!bool.TryParse(candidate, out boolValue)) return false;
                    normalizedValue = boolValue.ToString().ToLowerInvariant();
                    return true;
                }

                if (string.Equals(Type, "int", StringComparison.OrdinalIgnoreCase))
                {
                    int intValue;
                    if (!int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue) ||
                        !IsInIntegerRange(intValue)) return false;
                    normalizedValue = intValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                if (string.Equals(Type, "float", StringComparison.OrdinalIgnoreCase))
                {
                    float floatValue;
                    if (!float.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue) ||
                        float.IsNaN(floatValue) || float.IsInfinity(floatValue) || !IsInFloatRange(floatValue)) return false;
                    normalizedValue = floatValue.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                normalizedValue = candidate;
                return true;
            }

                public bool TryGetAdjacentValue(string currentValue, int direction, out string nextValue)
                {
                    nextValue = currentValue;
                    if (direction == 0) return false;

                    if (EffectiveControl == ModSettingControl.Selector)
                    {
                        List<ModSettingOption> options = GetSelectorOptions();
                        if (options.Count == 0) return false;

                        int currentIndex = options.FindIndex(option =>
                            option.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase));
                        if (currentIndex < 0) currentIndex = 0;

                        int nextIndex = currentIndex + (direction < 0 ? -1 : 1);
                        if (nextIndex < 0 || nextIndex >= options.Count)
                        {
                            if (!Presentation.Wrap) return false;
                            nextIndex = nextIndex < 0 ? options.Count - 1 : 0;
                        }

                        nextValue = options[nextIndex].Value;
                        return true;
                    }

                    if (EffectiveControl != ModSettingControl.Slider && EffectiveControl != ModSettingControl.Selector)
                    {
                        return false;
                    }

                    if (string.Equals(Type, "int", StringComparison.OrdinalIgnoreCase))
                    {
                        int current;
                        if (!int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out current)) return false;

                        int step;
                        if (!int.TryParse(Presentation.Step, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step <= 0)
                        {
                            step = 1;
                        }

                        long candidate = (long)current + (direction < 0 ? -step : step);
                        int minimum;
                        if (int.TryParse(Presentation.Minimum, NumberStyles.Integer, CultureInfo.InvariantCulture, out minimum) && candidate < minimum)
                        {
                            candidate = minimum;
                        }

                        int maximum;
                        if (int.TryParse(Presentation.Maximum, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) && candidate > maximum)
                        {
                            candidate = maximum;
                        }

                        if (candidate < int.MinValue || candidate > int.MaxValue) return false;
                        return TryNormalizeValue(((int)candidate).ToString(CultureInfo.InvariantCulture), out nextValue) &&
                            !string.Equals(currentValue, nextValue, StringComparison.Ordinal);
                    }

                    if (string.Equals(Type, "float", StringComparison.OrdinalIgnoreCase))
                    {
                        float current;
                        if (!float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out current)) return false;

                        float step;
                        if (!float.TryParse(Presentation.Step, NumberStyles.Float, CultureInfo.InvariantCulture, out step) || step <= 0f)
                        {
                            step = 0.1f;
                        }

                        float candidate = current + (direction < 0 ? -step : step);
                        float minimum;
                        if (float.TryParse(Presentation.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) && candidate < minimum)
                        {
                            candidate = minimum;
                        }

                        float maximum;
                        if (float.TryParse(Presentation.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) && candidate > maximum)
                        {
                            candidate = maximum;
                        }

                        return TryNormalizeValue(candidate.ToString(CultureInfo.InvariantCulture), out nextValue) &&
                            !string.Equals(currentValue, nextValue, StringComparison.Ordinal);
                    }

                    return false;
                }

                public bool TryGetToggledValue(string currentValue, out string nextValue)
                {
                    nextValue = currentValue;
                    if (EffectiveControl != ModSettingControl.Switch) return false;

                    if (!string.IsNullOrEmpty(Presentation.LeftValue) || !string.IsNullOrEmpty(Presentation.RightValue))
                    {
                        string candidate = currentValue.Equals(Presentation.LeftValue, StringComparison.OrdinalIgnoreCase)
                            ? Presentation.RightValue
                            : Presentation.LeftValue;
                        return TryNormalizeValue(candidate, out nextValue);
                    }

                    bool value;
                    if (!bool.TryParse(currentValue, out value)) return false;
                    nextValue = (!value).ToString().ToLowerInvariant();
                    return true;
                }

                public string GetDisplayValue(string value)
                {
                    string currentValue = value ?? "";
                    if (EffectiveControl == ModSettingControl.Selector)
                    {
                        ModSettingOption option = Presentation.Options.Find(candidate =>
                            candidate.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase));
                        if (option != null && !string.IsNullOrEmpty(option.Label)) return option.Label;
                    }

                    if (EffectiveControl == ModSettingControl.Switch)
                    {
                        if (currentValue.Equals(Presentation.LeftValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return string.IsNullOrEmpty(Presentation.LeftLabel) ? Presentation.LeftValue : Presentation.LeftLabel;
                        }

                        if (currentValue.Equals(Presentation.RightValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return string.IsNullOrEmpty(Presentation.RightLabel) ? Presentation.RightValue : Presentation.RightLabel;
                        }

                        bool boolValue;
                        if (bool.TryParse(currentValue, out boolValue)) return boolValue ? "ON" : "OFF";
                    }

                    if (!string.IsNullOrEmpty(Presentation.Format))
                    {
                        int intValue;
                        if (int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        {
                            try { return intValue.ToString(Presentation.Format, CultureInfo.InvariantCulture); }
                            catch (FormatException) { }
                        }

                        float floatValue;
                        if (float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        {
                            try { return floatValue.ToString(Presentation.Format, CultureInfo.InvariantCulture); }
                            catch (FormatException) { }
                        }
                    }

                    return currentValue;
                }

            public bool SetValue(string newValue)
        {
                if (IsDeveloperOverridden) return false;

                string normalizedValue;
                if (!TryNormalizeValue(newValue, out normalizedValue))
                {
                    Logger.Warning($"[ModSettings] Rejected invalid value '{newValue ?? ""}' for '{ModName}.{Name}'.");
                    return false;
                }

                bool valueChanged = !string.Equals(Value, normalizedValue, StringComparison.Ordinal);
            if (Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase) &&
                bool.TryParse(Value, out bool currentEnabled) &&
                    bool.TryParse(normalizedValue, out bool requestedEnabled))
            {
                valueChanged = currentEnabled != requestedEnabled;
                    normalizedValue = requestedEnabled.ToString().ToLowerInvariant();
            }

            if (valueChanged)
            {
                    Value = normalizedValue;
                    PersistedValue = normalizedValue;
                    Logger.Info($"Setting '{Name}' for mod '{ModName}' changed to: {normalizedValue} {(OnValueChanged != null ? "INVOKABLE" : "NON-INVOKABLE") }");
 
                ModSettingRestartScope restartScope = Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                    ? ModSettingRestartScope.Game
                    : RestartScope;
                if (restartScope != ModSettingRestartScope.None)
                {
                    ModSettingsManager.RequestRestart(restartScope);
                }

                    OnValueChanged?.Invoke(normalizedValue);
            }

                return true;
        }

            public bool SetPersistedValue(string value)
        {
                string normalizedValue;
                if (!TryNormalizeValue(value, out normalizedValue)) return false;

                PersistedValue = normalizedValue;
                if (!IsDeveloperOverridden) Value = normalizedValue;
                return true;
        }

        public void ApplyDeveloperOverride(string value)
        {
            developerOverrideValue = value;
            Value = value;
        }

        public void InitializeDeveloperSetting(string value, string type)
        {
            developerDefined = true;
            Type = type;
                DefaultValue = value;
            PersistedValue = value;
            ApplyDeveloperOverride(value);
        }

            private bool IsInIntegerRange(int value)
            {
                int minimum;
                if (!string.IsNullOrEmpty(Presentation.Minimum) &&
                    (!int.TryParse(Presentation.Minimum, NumberStyles.Integer, CultureInfo.InvariantCulture, out minimum) || value < minimum))
                {
                    return false;
                }

                int maximum;
                if (!string.IsNullOrEmpty(Presentation.Maximum) &&
                    (!int.TryParse(Presentation.Maximum, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) || value > maximum))
                {
                    return false;
                }

                return true;
            }

            private bool IsInFloatRange(float value)
            {
                float minimum;
                if (!string.IsNullOrEmpty(Presentation.Minimum) &&
                    (!float.TryParse(Presentation.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) || value < minimum))
                {
                    return false;
                }

                float maximum;
                if (!string.IsNullOrEmpty(Presentation.Maximum) &&
                    (!float.TryParse(Presentation.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) || value > maximum))
                {
                    return false;
                }

                return true;
            }

            private static bool TryNormalizeColor(string value, out string normalizedValue)
            {
                normalizedValue = "";
                string[] components = value.Split(',');
                if (components.Length != 3) return false;

                int red;
                int green;
                int blue;
                if (!int.TryParse(components[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out red) ||
                    !int.TryParse(components[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out green) ||
                    !int.TryParse(components[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out blue) ||
                    red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255)
                {
                    return false;
                }

                normalizedValue = red.ToString(CultureInfo.InvariantCulture) + "," +
                    green.ToString(CultureInfo.InvariantCulture) + "," +
                    blue.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            private List<ModSettingOption> GetIntegerSelectorOptions()
            {
                int minimum;
                int maximum;
                int step;
                if (!int.TryParse(Presentation.Minimum, NumberStyles.Integer, CultureInfo.InvariantCulture, out minimum) ||
                    !int.TryParse(Presentation.Maximum, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) ||
                    !int.TryParse(Presentation.Step, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) ||
                    minimum > maximum || step <= 0)
                {
                    return new List<ModSettingOption>();
                }

                long optionCount = ((long)maximum - minimum) / step + 1;
                if (optionCount > 128) return new List<ModSettingOption>();

                List<ModSettingOption> options = new List<ModSettingOption>();
                for (long value = minimum; value <= maximum; value += step)
                {
                    string optionValue = value.ToString(CultureInfo.InvariantCulture);
                    options.Add(new ModSettingOption { Value = optionValue, Label = GetDisplayValue(optionValue) });
                }

                return options;
            }

            private List<ModSettingOption> GetFloatSelectorOptions()
            {
                float minimum;
                float maximum;
                float step;
                if (!float.TryParse(Presentation.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) ||
                    !float.TryParse(Presentation.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) ||
                    !float.TryParse(Presentation.Step, NumberStyles.Float, CultureInfo.InvariantCulture, out step) ||
                    minimum > maximum || step <= 0f)
                {
                    return new List<ModSettingOption>();
                }

                int optionCount = Mathf.FloorToInt((maximum - minimum) / step) + 1;
                if (optionCount <= 0 || optionCount > 128) return new List<ModSettingOption>();

                List<ModSettingOption> options = new List<ModSettingOption>();
                for (int index = 0; index < optionCount; index++)
                {
                    float value = minimum + index * step;
                    string optionValue = value.ToString("0.########", CultureInfo.InvariantCulture);
                    options.Add(new ModSettingOption { Value = optionValue, Label = GetDisplayValue(optionValue) });
                }

                return options;
            }
    }

    public class ModProfileInfo
    {
        public string Name;
        public List<string> EnabledMods = new List<string>();
        public List<string> DisabledMods = new List<string>();
        public int TotalSettingsModified = 0;
    }

    public class MissingProfileModInfo
    {
        public string Name;
        public string Version;
    }

    public static class ModSettingsManager
    {
        private const string DeveloperModeEnvironmentVariable = "LIZZIESMOD_DEV_MODE";
        private const string DeveloperSettingsFileName = "DevSettings.xml";
           private const string ModSettingsFileName = "ModSettings.xml";
           private const string ModSettingsConfigDirectoryName = "Config";
        public static Dictionary<string, List<ModSetting>> AllModSettings = new Dictionary<string, List<ModSetting>>();
        public static ModSettingRestartScope PendingRestartScope { get; private set; } = ModSettingRestartScope.None;
        public static bool PendingRestart
        {
            get { return PendingRestartScope != ModSettingRestartScope.None; }
            set { PendingRestartScope = value ? ModSettingRestartScope.Game : ModSettingRestartScope.None; }
        }
        public static List<MissingProfileModInfo> LastMissingProfileMods = new List<MissingProfileModInfo>();

        public static void RequestRestart(ModSettingRestartScope restartScope)
        {
            if (restartScope > PendingRestartScope)
            {
                PendingRestartScope = restartScope;
            }
        }

        public static void RestorePendingRestartScope(ModSettingRestartScope restartScope)
        {
            PendingRestartScope = restartScope;
        }

        public static bool IsDeveloperMode
        {
            get { return string.Equals(Environment.GetEnvironmentVariable(DeveloperModeEnvironmentVariable), "1", StringComparison.Ordinal); }
        }

        public static void LoadAllModSettings()
        {
              Logger.Info("Scanning for Config/ModSettings.xml across all loaded mods...");

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {

                if (!AllModSettings.ContainsKey(mod.Name))
                {
                    AllModSettings[mod.Name] = new List<ModSetting>();
                }

                string settingsPath = GetModSettingsPath(mod);

                if (File.Exists(settingsPath))
                {
                    Logger.Info($"Found Config/ModSettings.xml for: {mod.Name}");

                    List<ModSetting> currentSettings = AllModSettings[mod.Name];
                    List<ModSetting> updatedSettings = new List<ModSetting>();

                    try
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(settingsPath);

                        foreach (XmlNode node in xmlDoc.DocumentElement.ChildNodes)
                        {
                                if (node.Name != "Setting") continue;

                                string settingName = node.Attributes["name"]?.Value ?? "Unknown";
                                string configuredValue = node.Attributes["value"]?.Value ?? "";
                                ModSetting setting = currentSettings.Find(candidate =>
                                    candidate.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase));
                                if (setting == null)
                                {
                                    setting = new ModSetting
                                    {
                                        ModName = mod.Name,
                                        Name = settingName
                                    };
                                }

                                ConfigureSettingFromXml(setting, node);
                                setting.DefaultValue = node.Attributes["defaultValue"]?.Value ?? configuredValue;

                                string normalizedValue;
                                if (!setting.TryNormalizeValue(configuredValue, out normalizedValue))
                                {
                                    if (!setting.TryNormalizeValue(setting.DefaultValue, out normalizedValue))
                                    {
                                        Logger.Warning($"[ModSettings] Ignored invalid definition for '{mod.Name}.{settingName}'.");
                                        continue;
                                    }

                                    Logger.Warning($"[ModSettings] Restored invalid value for '{mod.Name}.{settingName}' to its default.");
                                }

                                setting.SetPersistedValue(normalizedValue);
                                updatedSettings.Add(setting);
                        }

                        AllModSettings[mod.Name] = updatedSettings;
                    }
                    catch (System.Exception e)
                    {
                        Logger.Error($"Failed to parse Config/ModSettings.xml for {mod.Name}: {e.Message}");
                    }
                }
            }

            ApplyDeveloperSettingsOverrides();
        }

        private static string GetModSettingsPath(Mod mod)
        {
            return Path.Combine(mod.Path, ModSettingsConfigDirectoryName, ModSettingsFileName);
        }

            private static void ConfigureSettingFromXml(ModSetting setting, XmlNode node)
            {
                setting.Type = node.Attributes["type"]?.Value ?? "string";
                setting.RestartScope = ParseRestartScope(node);
                setting.requiresRestart = setting.RestartScope != ModSettingRestartScope.None;
                setting.Hidden = GetBooleanAttribute(node, "hidden");
                setting.ServerOnly = GetBooleanAttribute(node, "serverOnly");
                setting.inMenuOnly = GetBooleanAttribute(node, "menuOnly");
                setting.Warning = GetBooleanAttribute(node, "warning");

                ModSettingPresentation presentation = new ModSettingPresentation();
                presentation.Control = ParseSettingControl(node.Attributes["control"]?.Value, setting);
                presentation.DisplayName = node.Attributes["displayName"]?.Value ?? "";
                presentation.Tooltip = node.Attributes["tooltip"]?.Value ?? "";
                presentation.Minimum = node.Attributes["min"]?.Value ?? "";
                presentation.Maximum = node.Attributes["max"]?.Value ?? "";
                presentation.Step = node.Attributes["step"]?.Value ?? "";
                presentation.Format = node.Attributes["format"]?.Value ?? "";
                presentation.LeftValue = node.Attributes["leftValue"]?.Value ?? "";
                presentation.RightValue = node.Attributes["rightValue"]?.Value ?? "";
                presentation.LeftLabel = node.Attributes["leftLabel"]?.Value ?? "";
                presentation.RightLabel = node.Attributes["rightLabel"]?.Value ?? "";
                    presentation.Wrap = GetBooleanAttribute(node, "wrap");

                string inlineOptions = node.Attributes["options"]?.Value;
                if (!string.IsNullOrEmpty(inlineOptions))
                {
                    foreach (string value in inlineOptions.Split('|'))
                    {
                        AddSettingOption(presentation, value, value);
                    }
                }

                foreach (XmlNode childNode in node.ChildNodes)
                {
                    if (!childNode.Name.Equals("Option", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = childNode.Attributes["value"]?.Value;
                    string label = childNode.Attributes["label"]?.Value ?? value;
                    AddSettingOption(presentation, value, label);
                }

                setting.Presentation = presentation;
            }

            private static bool GetBooleanAttribute(XmlNode node, string attributeName)
            {
                bool value;
                return node.Attributes[attributeName] != null &&
                    bool.TryParse(node.Attributes[attributeName].Value, out value) && value;
            }

            private static ModSettingRestartScope ParseRestartScope(XmlNode node)
            {
                string value = node.Attributes["restartScope"]?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    switch (value.Trim().ToLowerInvariant())
                    {
                        case "world": return ModSettingRestartScope.World;
                        case "game": return ModSettingRestartScope.Game;
                        case "none": return ModSettingRestartScope.None;
                        default:
                            Logger.Warning("[ModSettings] Unknown restart scope '" + value + "'; using none.");
                            return ModSettingRestartScope.None;
                    }
                }

                return GetBooleanAttribute(node, "requiresRestart")
                    ? ModSettingRestartScope.Game
                    : ModSettingRestartScope.None;
            }

            private static ModSettingControl ParseSettingControl(string value, ModSetting setting)
            {
                if (string.IsNullOrWhiteSpace(value)) return ModSettingControl.Auto;

                switch (value.Trim().ToLowerInvariant())
                {
                    case "text": return ModSettingControl.Text;
                    case "switch": return ModSettingControl.Switch;
                    case "selector": return ModSettingControl.Selector;
                    case "slider": return ModSettingControl.Slider;
                    case "color": return ModSettingControl.Color;
                    case "auto": return ModSettingControl.Auto;
                    default:
                        Logger.Warning($"[ModSettings] '{setting.ModName}.{setting.Name}' uses unknown control '{value}'; using automatic selection.");
                        return ModSettingControl.Auto;
                }
            }

            private static void AddSettingOption(ModSettingPresentation presentation, string value, string label)
            {
                if (string.IsNullOrWhiteSpace(value)) return;

                string trimmedValue = value.Trim();
                if (presentation.Options.Exists(option => option.Value.Equals(trimmedValue, StringComparison.OrdinalIgnoreCase))) return;

                presentation.Options.Add(new ModSettingOption
                {
                    Value = trimmedValue,
                    Label = string.IsNullOrEmpty(label) ? trimmedValue : label.Trim()
                });
            }

        private static void ApplyDeveloperSettingsOverrides()
        {
            if (!IsDeveloperMode) return;

            Mod coreMod = global::ModManager.GetLoadedMods().Find(mod => mod.Name.Equals("LizziesMod", StringComparison.OrdinalIgnoreCase));
            if (coreMod == null)
            {
                Logger.Warning("[DevSettings] The core LizziesMod folder was unavailable; no developer overrides were applied.");
                return;
            }

            string settingsPath = Path.Combine(coreMod.Path, DeveloperSettingsFileName);
            if (!File.Exists(settingsPath))
            {
                Logger.Info($"[DevSettings] Developer mode is enabled, but '{DeveloperSettingsFileName}' was not found.");
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(settingsPath);
                XmlElement root = document.DocumentElement;
                if (root == null || !root.Name.Equals("DevSettings", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error($"[DevSettings] '{settingsPath}' must use a DevSettings root element.");
                    return;
                }

                int appliedCount = 0;
                foreach (XmlNode modNode in root.ChildNodes)
                {
                    if (modNode.Name != "Mod") continue;

                    string modName = modNode.Attributes?["name"]?.Value;
                    if (string.IsNullOrEmpty(modName) || !AllModSettings.TryGetValue(modName, out List<ModSetting> settings))
                    {
                        Logger.Warning($"[DevSettings] Ignored an override for unavailable mod '{modName ?? "<missing>"}'.");
                        continue;
                    }

                    foreach (XmlNode settingNode in modNode.ChildNodes)
                    {
                        if (settingNode.Name != "Setting") continue;

                        string settingName = settingNode.Attributes?["name"]?.Value;
                        string settingValue = settingNode.Attributes?["value"]?.Value;
                        if (string.IsNullOrEmpty(settingName) || settingValue == null)
                        {
                            Logger.Warning($"[DevSettings] Ignored incomplete override '{modName}.{settingName ?? "<missing>"}'.");
                            continue;
                        }

                        ModSetting setting = settings.Find(candidate => candidate.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase));
                        if (setting == null)
                        {
                            string settingType = GetDeveloperSettingType(settingNode, settingValue);
                            setting = new ModSetting
                            {
                                ModName = modName,
                                Name = settingName,
                                Value = settingValue,
                                PersistedValue = settingValue,
                                Type = settingType
                            };
                            setting.InitializeDeveloperSetting(settingValue, settingType);
                            settings.Add(setting);
                            Logger.Info($"[DevSettings] Registered developer setting '{modName}.{settingName}' ({settingType}) with default '{settingValue}'.");
                        }

                        string normalizedValue;
                        if (!setting.TryNormalizeValue(settingValue, out normalizedValue))
                        {
                            Logger.Warning($"[DevSettings] Ignored invalid value for '{modName}.{settingName}'.");
                            continue;
                        }

                        setting.ApplyDeveloperOverride(normalizedValue);
                        appliedCount++;
                    }
                }

                Logger.Info($"[DevSettings] Applied {appliedCount} local developer setting override(s).");
            }
            catch (Exception exception)
            {
                Logger.Error($"[DevSettings] Failed to load '{settingsPath}': {exception.Message}");
            }
        }

        private static string GetDeveloperSettingType(XmlNode settingNode, string value)
        {
            string declaredType = settingNode.Attributes?["type"]?.Value;
            if (!string.IsNullOrWhiteSpace(declaredType))
            {
                return declaredType.Trim().ToLowerInvariant();
            }

            bool boolValue;
            if (bool.TryParse(value, out boolValue)) return "bool";

            int intValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return "int";

            float floatValue;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue)) return "float";

            return "string";
        }

        private static string GetTargetSaveProfilePath()
        {

            Mod lizziesMod = global::ModManager.GetLoadedMods().Find(m => m.Name == "LizziesMod");
            if (lizziesMod != null)
            {
                 return Path.Combine(lizziesMod.Path, "Config", "ModProfiles.xml");
            }
              return Path.Combine("Config", "ModProfiles.xml");
        }

        private static List<string> GetAllProfilePaths()
        {
            List<string> paths = new List<string>();
            string lizziesPath = null;

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                 string path = Path.Combine(mod.Path, "Config", "ModProfiles.xml");
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

        private static string GetProfileModLabel(XmlNode modNode, string modName)
        {
            Mod installedMod = ModManager.GetMod(modName);
            if (installedMod == null)
            {
                string savedVersion = modNode.Attributes["version"]?.Value;
                return string.IsNullOrEmpty(savedVersion)
                    ? modName + " [missing]"
                    : modName + " [missing, profile " + savedVersion + "]";
            }

            return string.IsNullOrEmpty(installedMod.VersionString)
                ? modName
                : modName + " [" + installedMod.VersionString + "]";
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
                                            info.EnabledMods.Add(GetProfileModLabel(modNode, modName));
                                        else
                                            info.DisabledMods.Add(GetProfileModLabel(modNode, modName));
                                    }
                                }
                            }

                            if (!foundEnabledSetting) info.EnabledMods.Add(GetProfileModLabel(modNode, modName));
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
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
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
                    if (setting.IsDeveloperDefined) continue;

                    if (setting.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        foundEnabled = true;
                    }

                    XmlElement settingNode = xmlDoc.CreateElement("Setting");
                    settingNode.SetAttribute("name", setting.Name);
                    settingNode.SetAttribute("value", setting.ValueForPersistence);
                    settingNode.SetAttribute("type", setting.Type);
                    if (setting.ServerOnly) settingNode.SetAttribute("serverOnly", "true");
                    if (setting.Hidden || setting.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) settingNode.SetAttribute("hidden", "true");
                    if (setting.inMenuOnly) settingNode.SetAttribute("menuOnly", "true");
                    if (setting.Warning) settingNode.SetAttribute("warning", "true");
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

            LastMissingProfileMods = new List<MissingProfileModInfo>();

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

                    List<MissingProfileModInfo> missingMods = new List<MissingProfileModInfo>();
                    Dictionary<string, string> desiredEnabledStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    HashSet<string> modsToSave = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var mod in AllModSettings.Keys)
                    {
                        desiredEnabledStates[mod] = "false";
                    }

                    foreach (XmlNode modNode in profileNode.ChildNodes)
                    {
                        if (modNode.Name != "Mod") continue;
                        string modName = modNode.Attributes["name"]?.Value;
                        if (string.IsNullOrEmpty(modName)) continue;

                        if (!AllModSettings.ContainsKey(modName))
                        {
                            missingMods.Add(new MissingProfileModInfo
                            {
                                Name = modName,
                                Version = modNode.Attributes["version"]?.Value
                            });
                            continue;
                        }

                        bool hasEnabledSetting = false;
                        foreach (XmlNode settingNode in modNode.ChildNodes)
                        {
                            if (settingNode.Name != "Setting") continue;
                            string sName = settingNode.Attributes["name"]?.Value;
                            string sValue = settingNode.Attributes["value"]?.Value;

                            if (sName != null && sName.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                            {
                                desiredEnabledStates[modName] = sValue ?? "true";
                                hasEnabledSetting = true;
                                continue;
                            }

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

                                ModSettingRestartScope restartScope = ParseRestartScope(settingNode);

                                bool bHidden = false;
                                if (settingNode.Attributes["hidden"] != null)
                                    bool.TryParse(settingNode.Attributes["hidden"].Value, out bHidden);

                                bool bMenuOnly = false;
                                if (settingNode.Attributes["menuOnly"] != null)
                                    bool.TryParse(settingNode.Attributes["menuOnly"].Value, out bMenuOnly);

                                bool bWarning = false;
                                if (settingNode.Attributes["warning"] != null)
                                    bool.TryParse(settingNode.Attributes["warning"].Value, out bWarning);

                                ModSetting newSetting = new ModSetting
                                {
                                    ModName = modName,
                                    Name = sName,
                                    Value = sValue ?? "",
                                    PersistedValue = sValue ?? "",
                                    Type = sType,
                                    RestartScope = restartScope,
                                    requiresRestart = restartScope != ModSettingRestartScope.None,
                                    Hidden = bHidden,
                                    ServerOnly = bServerOnly,
                                    inMenuOnly = bMenuOnly,
                                    Warning = bWarning
                                };
                                AllModSettings[modName].Add(newSetting);
                                newSetting.OnValueChanged?.Invoke(sValue);
                            }
                        }

                        if (!hasEnabledSetting)
                        {
                            desiredEnabledStates[modName] = "true";
                        }

                        modsToSave.Add(modName);
                    }

                    foreach (var enabledState in desiredEnabledStates)
                    {
                        string modName = enabledState.Key;
                        string desiredValue = enabledState.Value;
                        ModSetting enabledSetting = AllModSettings[modName].Find(s => s.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase));

                        if (enabledSetting != null)
                        {
                            if (!string.Equals(enabledSetting.Value, desiredValue, StringComparison.OrdinalIgnoreCase))
                            {
                                enabledSetting.SetValue(desiredValue);
                                modsToSave.Add(modName);
                            }
                        }
                        else
                        {
                            bool desiredEnabled;
                            if (!bool.TryParse(desiredValue, out desiredEnabled))
                            {
                                desiredEnabled = true;
                            }

                            SetSetting(modName, "Enabled", desiredEnabled, true);
                            modsToSave.Add(modName);
                        }
                    }

                    foreach (string modName in modsToSave)
                    {
                        SaveModSettings(modName);
                    }

                    LastMissingProfileMods = missingMods;
                    if (missingMods.Count > 0)
                    {
                        Logger.Warning($"[ModProfiles] The profile '{profileName}' was loaded with missing mods: {string.Join(", ", missingMods.ConvertAll(mod => mod.Name))}");
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
                    RequestRestart(ModSettingRestartScope.Game);
                    bHidden = true;
                }

                ModSetting newSetting = new ModSetting
                {
                    ModName = modName,
                    Name = settingName,
                    Value = newValueString,
                    PersistedValue = newValueString,
                    DefaultValue = newValueString,
                    Type = inferredType,
                    RestartScope = settingName.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                        ? ModSettingRestartScope.Game
                        : ModSettingRestartScope.None,
                    requiresRestart = settingName.Equals("Enabled", StringComparison.OrdinalIgnoreCase),
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

        public static Color GetSettingColor(string modName, string settingName, Color defaultValue)
        {
            string value = GetSetting<string>(modName, settingName, "");
            string[] components = value.Split(',');
            if (components.Length != 3) return defaultValue;

            int red;
            int green;
            int blue;
            if (!int.TryParse(components[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out red) ||
                !int.TryParse(components[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out green) ||
                !int.TryParse(components[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out blue))
            {
                return defaultValue;
            }

            return new Color(
                Mathf.Clamp(red, 0, 255) / 255f,
                Mathf.Clamp(green, 0, 255) / 255f,
                Mathf.Clamp(blue, 0, 255) / 255f,
                1f);
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

                string settingsPath = GetModSettingsPath(targetMod);
                string settingsDirectory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(settingsDirectory))
                {
                    Directory.CreateDirectory(settingsDirectory);
                }

            XmlDocument xmlDoc = new XmlDocument();
            XmlElement root = xmlDoc.CreateElement("ModSettings");
            xmlDoc.AppendChild(root);

            foreach (var setting in AllModSettings[modName])
            {
                if (setting.IsDeveloperDefined) continue;

                XmlElement node = xmlDoc.CreateElement("Setting");
                node.SetAttribute("name", setting.Name);
                node.SetAttribute("value", setting.ValueForPersistence);
                node.SetAttribute("type", setting.Type);
                if (setting.RestartScope != ModSettingRestartScope.None)
                {
                    node.SetAttribute("restartScope", setting.RestartScope.ToString().ToLowerInvariant());
                }
                    if (!string.IsNullOrEmpty(setting.DefaultValue)) node.SetAttribute("defaultValue", setting.DefaultValue);
                if (setting.Hidden) node.SetAttribute("hidden", "true");
                    if (setting.ServerOnly) node.SetAttribute("serverOnly", "true");
                    if (setting.inMenuOnly) node.SetAttribute("menuOnly", "true");
                if (setting.Warning) node.SetAttribute("warning", "true");
                    WriteSettingPresentation(node, setting.Presentation);

                root.AppendChild(node);
            }

            xmlDoc.Save(settingsPath);
            Logger.Info($"Saved changes to Config/ModSettings.xml for {modName}");
        }

            private static void WriteSettingPresentation(XmlElement node, ModSettingPresentation presentation)
            {
                if (presentation == null) return;

                if (presentation.Control != ModSettingControl.Auto)
                {
                    node.SetAttribute("control", presentation.Control.ToString().ToLowerInvariant());
                }

                SetOptionalAttribute(node, "displayName", presentation.DisplayName);
                SetOptionalAttribute(node, "tooltip", presentation.Tooltip);
                SetOptionalAttribute(node, "min", presentation.Minimum);
                SetOptionalAttribute(node, "max", presentation.Maximum);
                SetOptionalAttribute(node, "step", presentation.Step);
                SetOptionalAttribute(node, "format", presentation.Format);
                SetOptionalAttribute(node, "leftValue", presentation.LeftValue);
                SetOptionalAttribute(node, "rightValue", presentation.RightValue);
                SetOptionalAttribute(node, "leftLabel", presentation.LeftLabel);
                SetOptionalAttribute(node, "rightLabel", presentation.RightLabel);
                    if (presentation.Wrap) node.SetAttribute("wrap", "true");

                foreach (ModSettingOption option in presentation.Options)
                {
                    XmlElement optionNode = node.OwnerDocument.CreateElement("Option");
                    optionNode.SetAttribute("value", option.Value);
                    if (!string.IsNullOrEmpty(option.Label) && !option.Label.Equals(option.Value, StringComparison.Ordinal))
                    {
                        optionNode.SetAttribute("label", option.Label);
                    }
                    node.AppendChild(optionNode);
                }
            }

            private static void SetOptionalAttribute(XmlElement node, string name, string value)
            {
                if (!string.IsNullOrEmpty(value)) node.SetAttribute(name, value);
            }
    }
}