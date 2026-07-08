using HarmonyLib;
using System.Collections.Generic;

namespace LizziesMod
{
 

    public class ModSettingsUIController : XUiController
    {
        public static string PreviousMenu = "";

        private string selectedMod = "";
        private XUiController modListGrid;
        private XUiController settingsGrid;

        private XUiV_Label lblSelectedModTitle;
        private XUiV_Label lblModAuthor;
        private XUiV_Label lblModVersion;
        private XUiV_Label lblModWebsite;
        private XUiV_Texture imgBanner;

        public override void Init()
        {
            base.Init();

            modListGrid = GetChildById("modListGrid");
            settingsGrid = GetChildById("settingsGrid");

            lblModAuthor = GetChildById("lblModAuthor")?.viewComponent as XUiV_Label;
            lblModVersion = GetChildById("lblModVersion")?.viewComponent as XUiV_Label;
            lblSelectedModTitle = GetChildById("lblSelectedModTitle")?.viewComponent as XUiV_Label;
            lblModWebsite = GetChildById("lblModWebsite")?.viewComponent as XUiV_Label;

            imgBanner = GetChildById("imgBanner")?.viewComponent as XUiV_Texture;


            XUiController closeBtn = GetChildById("btnClose");
            if (closeBtn != null)
            {
                XUiController clickable = closeBtn.GetChildById("clickable") ?? closeBtn;
                clickable.OnPress += HandleClose;
            }
        }


        public override void OnOpen()
        {
        
            ModController.ShowDisabledMods = true;
            base.OnOpen();
            PopulateModList(); 
            ModController.ShowDisabledMods = false;

            List<string> modNames = new List<string>(ModSettingsManager.AllModSettings.Keys);
            selectedMod = "";

            if (ModSettingsManager.AllModSettings.ContainsKey("LizziesMod"))
            {
                SelectMod("LizziesMod");
            }

            else if (modNames.Count > 0)
            {
                SelectMod(modNames[0]);
            }
            else
            {
                PopulateSettingsList();
            }
        }

        public void PopulateModList()
        {
            List<string> modNames = new List<string>(ModSettingsManager.AllModSettings.Keys);
            int i = 0;

            foreach (var child in modListGrid.Children)
            {
                if (child is ModEntryController entry)
                {
                    if (i < modNames.Count)
                        entry.SetMod(modNames[i], this);
                    else
                        entry.Clear();
                    i++;
                }
            }
        }

        public void SelectMod(string modName)
        {
            SaveCurrentSettingsUI();
            selectedMod = modName;

            Mod targetMod = null;
            foreach (var m in global::ModManager.GetLoadedMods())
            {
                if (m.Name == modName) { targetMod = m; break; }
            }

            if (targetMod != null)
            {

                string title = targetMod.DisplayName ?? targetMod.Name;
                string author = targetMod.Author ?? "Unknown";
                string version = targetMod.VersionString ?? "Unknown";
                string website = targetMod.Website ?? "None";

                if (lblSelectedModTitle != null) lblSelectedModTitle.Text = title;
                if (lblModAuthor != null) lblModAuthor.Text = $"Author: {author}";
                if (lblModVersion != null) lblModVersion.Text = $"Version: {version}";
                if (lblModWebsite != null) lblModWebsite.Text = $"Website: {website}";

                if (imgBanner != null)
                {
                    string bannerPath = System.IO.Path.Combine(targetMod.Path, "banner.png");
                    if (System.IO.File.Exists(bannerPath))
                    {
                        try
                        {
                            byte[] fileData = System.IO.File.ReadAllBytes(bannerPath);
                            UnityEngine.Texture2D tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
                            UnityEngine.ImageConversion.LoadImage(tex, fileData);

                            imgBanner.Texture = tex;
                            imgBanner.IsVisible = true;
                        }
                        catch
                        {
                            imgBanner.IsVisible = false;
                        }
                    }
                    else
                    {
                        imgBanner.IsVisible = false;
                    }
                }
            }

            PopulateSettingsList();
        }

        public void PopulateSettingsList()
        {
            if (string.IsNullOrEmpty(selectedMod) || !ModSettingsManager.AllModSettings.ContainsKey(selectedMod))
            {
                foreach (var child in settingsGrid.Children)
                {
                    if (child is SettingEntryController entry) entry.Clear();
                }
                return;
            }

            var visibleSettings = ModSettingsManager.AllModSettings[selectedMod]
                                    .FindAll(s => !s.Hidden);

            int i = 0;
            foreach (var child in settingsGrid.Children)
            {
                if (child is SettingEntryController entry)
                {
                    if (i < visibleSettings.Count)
                        entry.SetSetting(visibleSettings[i]);
                    else
                        entry.Clear();
                    i++;
                }
            }
        }

        private void SaveCurrentSettingsUI()
        {
            if (string.IsNullOrEmpty(selectedMod)) return;

            foreach (var child in settingsGrid.Children)
            {
                if (child is SettingEntryController entry && entry.CurrentSetting != null)
                {
                    entry.CurrentSetting.SetValue(entry.GetValue());
                }
            }
        }

        private void HandleClose(XUiController _sender, int _mouseButton)
        {
            ModController.ShowDisabledMods = true;
            SaveCurrentSettingsUI();
            foreach (var mod in ModSettingsManager.AllModSettings.Keys)
            {
                ModSettingsManager.SaveModSettings(mod);
            }

            xui.playerUI.windowManager.Close("windowModSettings");
            ModController.ShowDisabledMods = false;

            if (ModSettingsManager.PendingRestart)
            {
                xui.playerUI.windowManager.Open("windowModSettingsRestartPrompt", true);
            }
            else
            {
                if (!string.IsNullOrEmpty(PreviousMenu))
                    xui.playerUI.windowManager.Open(PreviousMenu, true);
            }
        }
    }

    public class RestartPromptUIController : XUiController
    {
        public override void Init()
        {
            base.Init();

            XUiController btnYes = GetChildById("btnYes");
            if (btnYes != null)
            {
                XUiController clickable = btnYes.GetChildById("clickable") ?? btnYes;
                clickable.OnPress += (s, e) =>
                {
                    UnityEngine.Application.Quit();
                };
            }

            XUiController btnNo = GetChildById("btnNo");
            if (btnNo != null)
            {
                XUiController clickable = btnNo.GetChildById("clickable") ?? btnNo;
                clickable.OnPress += (s, e) =>
                {
                    ModSettingsManager.PendingRestart = false;

                    xui.playerUI.windowManager.Close("windowModSettingsRestartPrompt");

                    if (!string.IsNullOrEmpty(ModSettingsUIController.PreviousMenu))
                        xui.playerUI.windowManager.Open(ModSettingsUIController.PreviousMenu, true);
                };
            }
        }
    }

    public class ModEntryController : XUiController
    {
        private string modName;
        private ModSettingsUIController mainController;
        private XUiV_Label lblModName;
        private XUiV_Texture imgModIcon;
        private XUiController btnEnableToggle;
        private XUiV_Sprite sprEnableCheck;

        public override void Init()
        {
            base.Init();
            lblModName = GetChildById("lblModName")?.viewComponent as XUiV_Label;
            imgModIcon = GetChildById("imgModIcon")?.viewComponent as XUiV_Texture;

            btnEnableToggle = GetChildById("btnEnableToggle");
            sprEnableCheck = GetChildById("sprEnableCheck")?.viewComponent as XUiV_Sprite;

            if (btnEnableToggle != null)
            {
                btnEnableToggle.OnPress += HandleBtnEnableTogglePress;
            }

            XUiController clickable = GetChildById("clickable");
            if (clickable != null) clickable.OnPress += HandlePress;
        }

        public void SetMod(string name, ModSettingsUIController main)
        {
            modName = name;
            mainController = main;

            if (lblModName != null)
            {
                lblModName.Text = name;
                bool hasSettings = ModSettingsManager.AllModSettings.ContainsKey(name) && ModSettingsManager.AllModSettings[name].Count > 0;
                lblModName.Color = hasSettings ? UnityEngine.Color.white : new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f);
            }

            bool isProtectedMod = name.Equals("TFP_Harmony", System.StringComparison.OrdinalIgnoreCase) || name.Equals("LizziesMod", System.StringComparison.OrdinalIgnoreCase);

            if (sprEnableCheck != null)
            {
                sprEnableCheck.IsVisible = ModSettingsManager.GetSetting(this.modName, "Enabled", true);
            }

            btnEnableToggle.viewComponent.IsVisible = !isProtectedMod; // hide the toggle button
            
            if (imgModIcon != null)
            {
                Mod targetMod = null;
                foreach (var m in global::ModManager.GetLoadedMods())
                {
                    if (m.Name == name) { targetMod = m; break; }
                }

                if (targetMod != null)
                {
                    string iconPath = System.IO.Path.Combine(targetMod.Path, "atlas.png");
                    if (!System.IO.File.Exists(iconPath))
                    {
                        iconPath = System.IO.Path.Combine(targetMod.Path, "icon.png");
                    }

                    if (System.IO.File.Exists(iconPath))
                    {
                        try
                        {
                            byte[] fileData = System.IO.File.ReadAllBytes(iconPath);
                            UnityEngine.Texture2D tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
                            UnityEngine.ImageConversion.LoadImage(tex, fileData);

                            imgModIcon.Texture = tex;
                            imgModIcon.IsVisible = true;
                        }
                        catch (System.Exception ex)
                        {
                            Logger.Error($"Failed to process menu icon for mod '{name}': {ex.Message}");
                            imgModIcon.IsVisible = false;
                        }
                    }
                    else
                    {
                        imgModIcon.IsVisible = false;
                    }
                }
            }

            viewComponent.IsVisible = true;
        }


        public void Clear()
        {
            modName = "";
            viewComponent.IsVisible = false;
        }

        private void HandleBtnEnableTogglePress(XUiController _sender, int _mouseButton)
        {

            if (this.modName.Equals("TFP_Harmony", System.StringComparison.OrdinalIgnoreCase) || this.modName.Equals("LizziesMod", System.StringComparison.OrdinalIgnoreCase)) return;

            bool currentState = ModSettingsManager.GetSetting(this.modName, "Enabled", true);
            bool newState = !currentState;
            ModSettingsManager.SetSetting(this.modName, "Enabled", newState);
            ModSettingsManager.SaveModSettings(this.modName);

            if (sprEnableCheck != null)
            {
                sprEnableCheck.IsVisible = newState;
            }
        }

        private void HandlePress(XUiController _sender, int _mouseButton)
        {
            if (!string.IsNullOrEmpty(modName) && mainController != null)
            {

                if (!ModController.IsModEnabled(modName))
                    mainController.GetChildById("settingsGrid").viewComponent.isVisible = false;
                 else
                    mainController.GetChildById("settingsGrid").viewComponent.isVisible = true;

                mainController.SelectMod(modName);
            }
        }
    }

    public class SettingEntryController : XUiController
    {
        public ModSetting CurrentSetting;
        private XUiV_Label lblSettingName;
        private XUiC_TextInput txtSettingValue;
        private XUiController chkSettingValue;
        private XUiController sprCheck;

        private bool isBoolSetting = false;
        private bool boolValue = false;

        public override void Init()
        {
            base.Init();
            lblSettingName = GetChildById("lblSettingName")?.viewComponent as XUiV_Label;
            txtSettingValue = GetChildById("txtSettingValue") as XUiC_TextInput;
            chkSettingValue = GetChildById("chkSettingValue");
            sprCheck = chkSettingValue?.GetChildById("sprCheck");

            XUiController clickable = chkSettingValue?.GetChildById("clickable") ?? chkSettingValue;
            if (clickable != null)
            {
                clickable.OnPress += (s, e) =>
                {
                    if (CurrentSetting != null && CurrentSetting.requiresRestart && Main.IsPlayerInGame())
                    {
                        xui.mPlayerUI?.localPlayer?.entityPlayerLocal?.PlayOneShot("ui_denied");
                        return;
                    }

                    if (!isBoolSetting) return;
                    boolValue = !boolValue;
                    if (sprCheck != null)
                    {
                        sprCheck.viewComponent.IsVisible = boolValue;
                    }
                };
            }
        }

        public void SetSetting(ModSetting setting)
        {
            CurrentSetting = setting;

            bool isLocked = setting.requiresRestart && Main.IsPlayerInGame();

            if (lblSettingName != null)
            {
                lblSettingName.Text = isLocked ? $"{setting.Name} (Requires World Restart)" : setting.Name;
                lblSettingName.Color = isLocked ? new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f) : UnityEngine.Color.white;
            }

            isBoolSetting = setting.Type.Equals("bool", System.StringComparison.OrdinalIgnoreCase);

            if (isBoolSetting)
            {
                boolValue = setting.Value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                if (txtSettingValue != null) txtSettingValue.viewComponent.IsVisible = false;
                if (chkSettingValue != null) chkSettingValue.viewComponent.IsVisible = true;
                if (sprCheck != null) sprCheck.viewComponent.IsVisible = boolValue;
            }
            else
            {
                if (txtSettingValue != null)
                {
                    txtSettingValue.Text = setting.Value;
                    txtSettingValue.viewComponent.IsVisible = true;
                }
                if (chkSettingValue != null) chkSettingValue.viewComponent.IsVisible = false;
            }

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            CurrentSetting = null;
            isBoolSetting = false;
            viewComponent.IsVisible = false;
        }

        public string GetValue()
        {
            if (CurrentSetting != null && CurrentSetting.requiresRestart && Main.IsPlayerInGame())
            {
                return CurrentSetting.Value;
            }

            if (isBoolSetting)
            {
                return boolValue ? "true" : "false";
            }
            return txtSettingValue != null ? txtSettingValue.Text : "";
        }
    }

    [HarmonyPatch(typeof(XUiC_InGameMenuWindow), "Init")]
    public class PauseMenu_Init_Patch
    {
        public static void Postfix(XUiC_InGameMenuWindow __instance)
        {
            XUiController btn = __instance.GetChildById("btnModSettings");
            if (btn != null)
            {
                XUiController clickable = btn.GetChildById("clickable") ?? btn;
                clickable.OnPress += (s, e) =>
                {
                    ModSettingsUIController.PreviousMenu = __instance.WindowGroup.Id;
                    __instance.xui.playerUI.windowManager.Close(__instance.WindowGroup.Id);
                    __instance.xui.playerUI.windowManager.Open("windowModSettings", true);
                };
            }
        }
    }


    [HarmonyPatch(typeof(XUiC_MainMenuButtons), "Init")]
    public class MainMenuButtons_Init_Patch
    {
        public static void Postfix(XUiC_MainMenuButtons __instance)
        {

            XUiController btnSettings = __instance.GetChildById("btnModSettings");
            if (btnSettings != null)
            {
                XUiController clickable = btnSettings.GetChildById("clickable") ?? btnSettings;
                clickable.OnPress += (s, e) =>
                {
                    ModSettingsUIController.PreviousMenu = "mainMenu";
                    __instance.xui.playerUI.windowManager.Close("mainMenu");
                    __instance.xui.playerUI.windowManager.Open("windowModSettings", true);
                };
            }

            XUiController btnLibrary = __instance.GetChildById("btnModLibrary");
            if (btnLibrary != null)
            {
                XUiController clickable = btnLibrary.GetChildById("clickable") ?? btnLibrary;
                clickable.OnPress += (s, e) =>
                {

                    ModLibraryUIController.PreviousMenu = "mainMenu";
                    __instance.xui.playerUI.windowManager.Close("mainMenu");
                    __instance.xui.playerUI.windowManager.Open("windowModLibrary", true);
                };
            }
        }
    }
}