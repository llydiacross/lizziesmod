using System;
using System.Collections.Generic;

namespace LizziesMod
{
    public enum ModSettingRestartScope
    {
        None,
        World,
        Game
    }

    public enum ModSettingControl
    {
        Auto,
        Text,
        Switch,
        Selector,
        Slider,
        Color
    }

    public sealed class ModSettingOption
    {
        public string Value;
        public string Label;
    }

    public sealed class ModSettingPresentation
    {
        public ModSettingControl Control = ModSettingControl.Auto;
        public string DisplayName = "";
        public string Tooltip = "";
        public string Minimum = "";
        public string Maximum = "";
        public string Step = "";
        public string Format = "";
        public string LeftValue = "";
        public string RightValue = "";
        public string LeftLabel = "";
        public string RightLabel = "";
            public bool Wrap;
        public readonly List<ModSettingOption> Options = new List<ModSettingOption>();

        public ModSettingControl GetEffectiveControl(string settingType)
        {
            if (Control != ModSettingControl.Auto) return Control;
            if (string.Equals(settingType, "bool", StringComparison.OrdinalIgnoreCase)) return ModSettingControl.Switch;
            if (string.Equals(settingType, "color", StringComparison.OrdinalIgnoreCase)) return ModSettingControl.Color;
            return ModSettingControl.Text;
        }
    }
}