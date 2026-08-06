using Audio;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    internal static class InGameUiPause
    {
        private static int pauseOwners;

        public static bool Acquire()
        {
            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (GameManager.Instance == null || GameManager.Instance.World == null ||
                connectionManager == null || !connectionManager.IsServer || connectionManager.ClientCount() > 0)
            {
                return false;
            }

            if (pauseOwners == 0)
            {
                GameManager.Instance.Pause(_bOn: true);
            }

            pauseOwners++;
            return true;
        }

        public static void Release(bool ownsPause)
        {
            if (!ownsPause || pauseOwners <= 0) return;

            pauseOwners--;
            if (pauseOwners == 0 && GameManager.Instance != null)
            {
                GameManager.Instance.Pause(_bOn: false);
            }
        }
    }

    public class ModSettingsUIController : XUiController
    {
        public static string PreviousMenu = "";
        public static string RequestedModName = "";

        private string selectedMod = "";
        public string SelectedMod => selectedMod;

        private XUiController modListGrid;
        private XUiController settingsGrid;
        private XUiV_Label lblSelectedModTitle;
        private XUiV_Label lblModAuthor;
        private XUiV_Label lblModVersion;
        private XUiV_Label lblModWebsite;
        private XUiV_Texture imgBanner;
        private XUiC_TextInput txtProfileName;
        private XUiController btnLoadProfile;
        private XUiController btnSaveProfile;
        private XUiController btnEditInputs;
        private XUiController btnOpenReadme;
        private XUiController btnOpenModPortal;
        public static string LastLoadedProfile = "";
        private bool isTransitioning = false;
        private bool ownsGamePause;

        private sealed class PendingSettingChange
        {
            public SettingEntryController Entry;
            public ModSetting Setting;
            public string Value;
        }

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
            txtProfileName = GetChildById("txtProfileName") as XUiC_TextInput;
            btnLoadProfile = GetChildById("btnLoadProfile");

            if (btnLoadProfile != null)
            {
                XUiController clickable = btnLoadProfile.GetChildById("clickable") ?? btnLoadProfile;
                clickable.OnPress += (s, e) =>
                {
                    isTransitioning = true;
                    ProfileSelectorUIController.PreviousMenu = "windowModSettings";
                    xui.playerUI.windowManager.Close("windowModSettings");
                    xui.playerUI.windowManager.Open("windowProfileSelector", true);
                };
            }
            btnSaveProfile = GetChildById("btnSaveProfile");
            if (btnSaveProfile != null)
            {
                XUiController clickable = btnSaveProfile.GetChildById("clickable") ?? btnSaveProfile;
                clickable.OnPress += (s, e) =>
                {
                    if (txtProfileName != null && !string.IsNullOrEmpty(txtProfileName.Text))
                    {
                        string profileName = txtProfileName.Text;
                        SaveCurrentSettingsUI(() => ModSettingsManager.SaveProfile(profileName));
                    }
                };
            }
            btnEditInputs = GetChildById("btnEditInputs");
            if (btnEditInputs != null)
            {
                XUiController clickable = btnEditInputs.GetChildById("clickable") ?? btnEditInputs;
                clickable.OnPress += (s, e) => OpenSelectedModInputs();
            }
            btnOpenReadme = GetChildById("btnOpenReadme");
            if (btnOpenReadme != null)
            {
                XUiController clickable = btnOpenReadme.GetChildById("clickable") ?? btnOpenReadme;
                clickable.OnPress += (s, e) => OpenSelectedModReadme();
            }
            btnOpenModPortal = GetChildById("btnOpenModPortal");
            if (btnOpenModPortal != null)
            {
                XUiController clickable = btnOpenModPortal.GetChildById("clickable") ?? btnOpenModPortal;
                clickable.OnPress += (s, e) => OpenModPortal();
            }
            XUiController closeBtn = GetChildById("btnClose");
            if (closeBtn != null)
            {
                XUiController clickable = closeBtn.GetChildById("clickable") ?? closeBtn;
                clickable.OnPress += (s, e) => xui.playerUI.windowManager.Close("windowModSettings");
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;

            if (isTransitioning)
            {
                isTransitioning = false;
                return;
            }

            string profileName = txtProfileName != null ? txtProfileName.Text : "";
            SaveCurrentSettingsUI(() => FinalizeSettingsClose(profileName));
        }

        public override void OnOpen()
        {
            ModPatcher.ShowDisabledMods = true;
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();
            PopulateModList();
            ModPatcher.ShowDisabledMods = false;

            string savedProfile = ModSettingsManager.GetSetting("LizziesMod", "LastProfileName", "");
            if (txtProfileName != null)
            {
                if (!string.IsNullOrEmpty(LastLoadedProfile))
                    txtProfileName.Text = LastLoadedProfile;
                else if (!string.IsNullOrEmpty(savedProfile))
                    txtProfileName.Text = savedProfile;
                else
                    txtProfileName.Text = "Custom";
            }

            List<string> modNames = new List<string>(ModSettingsManager.AllModSettings.Keys);
            selectedMod = "";

            if (txtProfileName != null && !string.IsNullOrEmpty(LastLoadedProfile))
            {
                txtProfileName.Text = LastLoadedProfile;
            }

            string requestedModName = RequestedModName;
            RequestedModName = "";
            if (!string.IsNullOrEmpty(requestedModName) && ModSettingsManager.AllModSettings.ContainsKey(requestedModName))
            {
                SelectMod(requestedModName);
            }
            else if (ModSettingsManager.AllModSettings.ContainsKey("LizziesMod"))
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
            SaveCurrentSettingsUI(() => SelectModAfterSaving(modName));
        }

        private void SelectModAfterSaving(string modName)
        {
            selectedMod = modName;

            ModPatcher.ShowDisabledMods = true;
            Mod targetMod = global::ModManager.GetLoadedMods().Find(m => m.Name == modName);
            ModPatcher.ShowDisabledMods = false;

            if (targetMod != null)
            {
                string title = targetMod.DisplayName ?? targetMod.Name;


                if (title == targetMod.Name)
                {
                    int separatorIdx = title.IndexOfAny(new char[] { '_', '-' });
                    if (separatorIdx > 0 && separatorIdx < title.Length - 1)
                    {
                        string extensionKey = title.Substring(0, separatorIdx);
                        string actualModName = title.Substring(separatorIdx + 1);
                        title = $"{actualModName} [a252ff]({extensionKey})[-]";
                    }
                }

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
            else
            {
                string titleFallback = modName;
                int separatorIdx = titleFallback.IndexOfAny(new char[] { '_', '-' });
                if (separatorIdx > 0 && separatorIdx < titleFallback.Length - 1)
                {
                    string extensionKey = titleFallback.Substring(0, separatorIdx);
                    string actualModName = titleFallback.Substring(separatorIdx + 1);
                    titleFallback = $"{actualModName} [a2i52ff]({extensionKey})[-]";
                }


                if (lblSelectedModTitle != null) lblSelectedModTitle.Text = titleFallback;
                if (lblModAuthor != null) lblModAuthor.Text = "Author: Unknown";
                if (lblModVersion != null) lblModVersion.Text = "Version: Unknown";
                if (lblModWebsite != null) lblModWebsite.Text = "Website: None";
                if (imgBanner != null) imgBanner.IsVisible = false;
            }

            PopulateSettingsList();
            UpdateEditInputsButton();
            UpdateReadmeButton();
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

            var visibleSettings = ModSettingsManager.AllModSettings[selectedMod].FindAll(s => !s.Hidden);

            if (!ModPatcher.IsModEnabled(selectedMod))
            {
                visibleSettings.Clear();
            }

            int i = 0;
            foreach (var child in settingsGrid.Children)
            {
                if (child is SettingEntryController entry)
                {
                    if (i < visibleSettings.Count)
                            entry.SetSetting(visibleSettings[i], this);
                    else
                        entry.Clear();
                    i++;
                }
            }
        }

        private void SaveCurrentSettingsUI(Action onComplete = null)
        {
            List<PendingSettingChange> changes = new List<PendingSettingChange>();
            if (!string.IsNullOrEmpty(selectedMod))
            {
                foreach (var child in settingsGrid.Children)
                {
                    if (child is SettingEntryController entry && entry.CurrentSetting != null)
                    {
                            string value;
                            if (!entry.TryGetValue(out value)) continue;

                        changes.Add(new PendingSettingChange
                        {
                            Entry = entry,
                            Setting = entry.CurrentSetting,
                                Value = value
                        });
                    }
                }
            }

            ApplySettingChanges(changes, 0, onComplete);
        }

        private void ApplySettingChanges(List<PendingSettingChange> changes, int index, Action onComplete)
        {
            if (index >= changes.Count)
            {
                onComplete?.Invoke();
                return;
            }

            PendingSettingChange change = changes[index];
            if (string.Equals(change.Setting.Value, change.Value, StringComparison.Ordinal))
            {
                ApplySettingChanges(changes, index + 1, onComplete);
                return;
            }

            if (!change.Setting.Warning)
            {
                change.Setting.SetValue(change.Value);
                ApplySettingChanges(changes, index + 1, onComplete);
                return;
            }

            string warningText = "Changing '" + change.Setting.Name + "' for '" + change.Setting.ModName +
                "' can make your save incompatible or unstable.\n\nBack up your save before continuing. Do you want to apply this setting?";
            XUiC_MessageBoxWindowGroup.ShowOkCancel(
                xui,
                "SETTING WARNING",
                warningText,
                "",
                () =>
                {
                    change.Setting.SetValue(change.Value);
                    ApplySettingChanges(changes, index + 1, onComplete);
                },
                () =>
                {
                    change.Entry.SetSetting(change.Setting);
                    ApplySettingChanges(changes, index + 1, onComplete);
                });
        }

        private void FinalizeSettingsClose(string profileName)
        {
            ModPatcher.ShowDisabledMods = true;

            if (!string.IsNullOrEmpty(profileName))
            {
                ModSettingsManager.SetSetting("LizziesMod", "LastProfileName", profileName, true);
            }

            foreach (var mod in ModSettingsManager.AllModSettings.Keys)
            {
                ModSettingsManager.SaveModSettings(mod);
            }

            ModPatcher.ShowDisabledMods = false;

            if (ModSettingsManager.PendingRestart)
            {
                xui.playerUI.windowManager.Open("windowModSettingsRestartPrompt", true);
            }
            else
            {
                string returnMenu = ConsumeReturnMenu();
                if (!string.IsNullOrEmpty(returnMenu))
                {
                    xui.playerUI.windowManager.Open(returnMenu, true);
                }
            }
        }

        internal static string ConsumeReturnMenu()
        {
            string previousMenu = PreviousMenu;
            PreviousMenu = "";
            if (string.IsNullOrEmpty(previousMenu) && !Main.IsPlayerInGame())
            {
                return "mainMenu";
            }

            return previousMenu;
        }

        private void UpdateEditInputsButton()
        {
            if (btnEditInputs?.viewComponent == null) return;

            btnEditInputs.viewComponent.IsVisible = !string.IsNullOrEmpty(selectedMod) &&
                CustomInputManager.GetInputsForMod(selectedMod).Count > 0;
        }

        private void UpdateReadmeButton()
        {
            if (btnOpenReadme?.viewComponent == null) return;

            btnOpenReadme.viewComponent.IsVisible = GetSelectedReadme() != null;
        }

        private ModBook GetSelectedReadme()
        {
            if (string.IsNullOrEmpty(selectedMod)) return null;

            foreach (ModBook book in ModManualManager.AllBooks.Values)
            {
                if (book.IsReadme && string.Equals(book.ModSource, selectedMod, StringComparison.OrdinalIgnoreCase))
                {
                    return book;
                }
            }

            return null;
        }

        private void OpenSelectedModInputs()
        {
            if (string.IsNullOrEmpty(selectedMod) || CustomInputManager.GetInputsForMod(selectedMod).Count == 0) return;

            SaveCurrentSettingsUI(() =>
            {
                isTransitioning = true;
                RequestedModName = selectedMod;
                CustomInputBindingsUIController.RequestedModName = selectedMod;
                CustomInputBindingsUIController.PreviousMenu = "windowModSettings";
                xui.playerUI.windowManager.Close("windowModSettings");
                xui.playerUI.windowManager.Open(CustomInputBindingsUIController.WindowName, true);
            });
        }

        private void OpenSelectedModReadme()
        {
            ModBook readme = GetSelectedReadme();
            if (readme == null) return;

            SaveCurrentSettingsUI(() =>
            {
                isTransitioning = true;
                ModLibraryUIController.PreviousMenu = "windowModSettings";
                ModLibraryUIController.RequestedBookId = readme.ID;
                xui.playerUI.windowManager.Close("windowModSettings");
                xui.playerUI.windowManager.Open("windowModLibrary", true);
            });
        }

            internal void OpenColorPicker(SettingEntryController entry)
            {
                if (entry == null || entry.CurrentSetting == null) return;

                SaveCurrentSettingsUI(() =>
                {
                    isTransitioning = true;
                    ModSettingColorPickerUIController.Request(
                        entry.CurrentSetting,
                        entry.GetPendingValue(),
                        selectedMod);
                    xui.playerUI.windowManager.Close("windowModSettings");
                    xui.playerUI.windowManager.Open(ModSettingColorPickerUIController.WindowName, true);
                });
            }

        private void OpenModPortal()
        {
            SaveCurrentSettingsUI(() =>
            {
                isTransitioning = true;
                ModPortalUIController.PreviousMenu = "windowModSettings";
                ModPortalUIController.RequestedPackageId = "";
                ModPortalUIController.RequestedPackageVersion = "";
                xui.playerUI.windowManager.Close("windowModSettings");
                xui.playerUI.windowManager.Open(ModPortalUIController.WindowName, true);
            });
        }
    }

    public class RestartPromptUIController : XUiController
    {
        private bool isQuitting = false;
        private bool ownsGamePause;

        public override void Init()
        {
            base.Init();
            XUiController btnYes = GetChildById("btnYes");
            if (btnYes != null)
            {
                XUiController clickable = btnYes.GetChildById("clickable") ?? btnYes;
                clickable.OnPress += (s, e) =>
                {
                    isQuitting = true;
                    UnityEngine.Application.Quit();
                };
            }
            XUiController btnNo = GetChildById("btnNo");
            if (btnNo != null)
            {
                XUiController clickable = btnNo.GetChildById("clickable") ?? btnNo;
                clickable.OnPress += (s, e) => xui.playerUI.windowManager.Close("windowModSettingsRestartPrompt");
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;
            if (isQuitting) return;

            ModSettingsManager.PendingRestart = false;
            string returnMenu = ModSettingsUIController.ConsumeReturnMenu();
            if (!string.IsNullOrEmpty(returnMenu))
                xui.playerUI.windowManager.Open(returnMenu, true);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();
        }
    }

    public class ModEntryController : XUiController
    {
        private string modName;
        private ModSettingsUIController mainController;
        private XUiV_Label lblModName;
            private XUiV_Label lblModVersion;
        private XUiV_Texture imgModIcon;
        private XUiController btnEnableToggle;
        private XUiV_Sprite sprEnableCheck;

        public override void Init()
        {
            base.Init();
            lblModName = GetChildById("lblModName")?.viewComponent as XUiV_Label;
                lblModVersion = GetChildById("lblModVersion")?.viewComponent as XUiV_Label;
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

                Mod targetMod = global::ModManager.GetLoadedMods().Find(mod => mod.Name == name);
                string displayTitle = targetMod?.DisplayName ?? name;
                if (displayTitle == name)
                {
                    int separatorIdx = name.IndexOfAny(new char[] { '_', '-' });
                    if (separatorIdx > 0 && separatorIdx < name.Length - 1)
                    {
                        string extensionKey = name.Substring(0, separatorIdx);
                        string actualModName = name.Substring(separatorIdx + 1);
                        displayTitle = $"{actualModName} [a252ff]({extensionKey})[-]";
                    }
                }

            if (lblModName != null)
            {
                lblModName.Text = displayTitle;
                bool hasSettings = ModSettingsManager.AllModSettings.ContainsKey(name) && ModSettingsManager.AllModSettings[name].Count > 0;
                lblModName.Color = hasSettings ? UnityEngine.Color.white : new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f);
            }

                if (lblModVersion != null)
                {
                    string version = targetMod?.VersionString;
                    lblModVersion.Text = "Version " + (string.IsNullOrEmpty(version) ? "Unknown" : version);
                }

            bool isProtectedMod = name.Equals("TFP_Harmony", System.StringComparison.OrdinalIgnoreCase) || name.Equals("LizziesMod", System.StringComparison.OrdinalIgnoreCase);

            if (sprEnableCheck != null)
            {
                sprEnableCheck.IsVisible = ModSettingsManager.GetSetting(this.modName, "Enabled", true);
            }

            if (btnEnableToggle?.viewComponent != null) btnEnableToggle.viewComponent.IsVisible = !isProtectedMod;

            if (imgModIcon != null)
            {
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
                else
                {
                    imgModIcon.IsVisible = false;
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

            ModSettingsManager.SetSetting(this.modName, "Enabled", newState, true);
            ModSettingsManager.SaveModSettings(this.modName);

            if (sprEnableCheck != null)
            {
                sprEnableCheck.IsVisible = newState;
            }

            if (mainController != null && mainController.SelectedMod == this.modName)
            {
                mainController.PopulateSettingsList();
            }
        }

        private void HandlePress(XUiController _sender, int _mouseButton)
        {
            if (!string.IsNullOrEmpty(modName) && mainController != null)
            {
                mainController.SelectMod(modName);
            }
        }
    }

    public class SettingEntryController : XUiController
    {
        private ModSetting setting;
        private ModSettingsUIController mainController;
        private XUiV_Label lblSettingName;
        private XUiC_TextInput txtSettingValue;
        private XUiController switchSettingValue;
        private XUiV_Label lblSwitchValue;
        private XUiV_Sprite sprSwitchTrackOn;
        private XUiV_Sprite sprSwitchTrackOff;
        private XUiV_Sprite sprSwitchKnobOn;
        private XUiV_Sprite sprSwitchKnobOff;
        private XUiController selectorSettingValue;
        private XUiV_Label lblSelectorValue;
        private XUiController sliderSettingValue;
        private XUiV_Label lblSliderValue;
        private XUiController colorSettingValue;
        private XUiV_Label lblColorValue;
        private XUiV_Sprite sprColorSwatch;
        private bool isLocked;
        private string pendingValue = "";

        public ModSetting CurrentSetting => setting;

        public override void Init()
        {
            base.Init();
            lblSettingName = GetChildById("lblSettingName")?.viewComponent as XUiV_Label;
            txtSettingValue = GetChildById("txtSettingValue") as XUiC_TextInput;
            switchSettingValue = GetChildById("switchSettingValue");
            lblSwitchValue = GetChildById("lblSwitchValue")?.viewComponent as XUiV_Label;
            sprSwitchTrackOn = GetChildById("sprSwitchTrackOn")?.viewComponent as XUiV_Sprite;
            sprSwitchTrackOff = GetChildById("sprSwitchTrackOff")?.viewComponent as XUiV_Sprite;
            sprSwitchKnobOn = GetChildById("sprSwitchKnobOn")?.viewComponent as XUiV_Sprite;
            sprSwitchKnobOff = GetChildById("sprSwitchKnobOff")?.viewComponent as XUiV_Sprite;
            selectorSettingValue = GetChildById("selectorSettingValue");
            lblSelectorValue = GetChildById("lblSelectorValue")?.viewComponent as XUiV_Label;
            sliderSettingValue = GetChildById("sliderSettingValue");
            lblSliderValue = GetChildById("lblSliderValue")?.viewComponent as XUiV_Label;
            colorSettingValue = GetChildById("colorSettingValue");
            lblColorValue = GetChildById("lblColorValue")?.viewComponent as XUiV_Label;
            sprColorSwatch = GetChildById("sprColorSwatch")?.viewComponent as XUiV_Sprite;

            BindPress("btnSwitchValue", HandleSwitchPress);
            BindPress("btnSelectorPrevious", (sender, mouseButton) => HandleAdjacentPress(-1));
            BindPress("btnSelectorNext", (sender, mouseButton) => HandleAdjacentPress(1));
            BindPress("btnSliderPrevious", (sender, mouseButton) => HandleAdjacentPress(-1));
            BindPress("btnSliderNext", (sender, mouseButton) => HandleAdjacentPress(1));
            BindPress("btnColorValue", HandleColorPress);
        }

        public void SetSetting(ModSetting _setting)
        {
            SetSetting(_setting, mainController);
        }

        public void SetSetting(ModSetting _setting, ModSettingsUIController owner)
        {
            setting = _setting;
            mainController = owner;
            if (setting == null)
            {
                Clear();
                return;
            }

            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            bool inMultiplayerAsClient = connectionManager != null && connectionManager.IsClient && !connectionManager.IsServer;
            isLocked = setting.IsDeveloperOverridden || (setting.ServerOnly && inMultiplayerAsClient);
            pendingValue = setting.Value;

            if (lblSettingName != null)
            {
                string label = setting.DisplayName;
                if (setting.IsDeveloperOverridden) label += " [F8C45A](Dev Override)[-]";
                else if (isLocked) label += " [FF3333](Locked)[-]";
                else if (setting.inMenuOnly) label += " [FF3333](Menu Only)[-]";
                lblSettingName.Text = label;
            }

            SetControlVisible(txtSettingValue, false);
            SetControlVisible(switchSettingValue, false);
            SetControlVisible(selectorSettingValue, false);
            SetControlVisible(sliderSettingValue, false);
            SetControlVisible(colorSettingValue, false);

            switch (setting.EffectiveControl)
            {
                case ModSettingControl.Switch:
                    SetControlVisible(switchSettingValue, true);
                    break;
                case ModSettingControl.Selector:
                    SetControlVisible(selectorSettingValue, true);
                    break;
                case ModSettingControl.Slider:
                    SetControlVisible(sliderSettingValue, true);
                    break;
                case ModSettingControl.Color:
                    SetControlVisible(colorSettingValue, true);
                    break;
                default:
                    SetControlVisible(txtSettingValue, true);
                    if (txtSettingValue != null) txtSettingValue.Text = pendingValue;
                    break;
            }

            RefreshControlValue();
            viewComponent.IsVisible = !setting.Hidden;
        }

        public void Clear()
        {
            setting = null;
            isLocked = false;
            pendingValue = "";
            viewComponent.IsVisible = false;
        }

        public string GetValue()
        {
            string value;
            return TryGetValue(out value) ? value : setting?.Value ?? "";
        }

        public bool TryGetValue(out string value)
        {
            value = "";
            if (setting == null) return false;
            if (isLocked)
            {
                value = setting.Value;
                return true;
            }

            string rawValue = setting.EffectiveControl == ModSettingControl.Text && txtSettingValue != null
                ? txtSettingValue.Text
                : pendingValue;
            if (setting.TryNormalizeValue(rawValue, out value)) return true;

            Logger.Warning($"[ModSettings] Restored invalid input for '{setting.ModName}.{setting.Name}'.");
            SetSetting(setting);
            return false;
        }

        internal string GetPendingValue()
        {
            return pendingValue;
        }

        internal void SetColorValue(string value)
        {
            SetPendingValue(value);
        }

        private void BindPress(string controlName, XUiEvent_OnPressEventHandler handler)
        {
            XUiController control = GetChildById(controlName);
            if (control == null) return;

            XUiController clickable = control.GetChildById("clickable") ?? control;
            clickable.OnPress += handler;
        }

        private void HandleSwitchPress(XUiController sender, int mouseButton)
        {
            if (!CanEdit()) return;

            string nextValue;
            if (setting.TryGetToggledValue(pendingValue, out nextValue)) SetPendingValue(nextValue);
        }

        private void HandleAdjacentPress(int direction)
        {
            if (!CanEdit()) return;

            string nextValue;
            if (setting.TryGetAdjacentValue(pendingValue, direction, out nextValue)) SetPendingValue(nextValue);
        }

        private void HandleColorPress(XUiController sender, int mouseButton)
        {
            if (!CanEdit()) return;

            if (mainController != null)
            {
                mainController.OpenColorPicker(this);
            }
        }

        private bool CanEdit()
        {
            if (setting == null || !isLocked) return setting != null;

            Manager.PlayInsidePlayerHead("ui_denied");
            return false;
        }

        private void SetPendingValue(string value)
        {
            if (setting == null) return;

            string normalizedValue;
            if (!setting.TryNormalizeValue(value, out normalizedValue)) return;

            pendingValue = normalizedValue;
            RefreshControlValue();
        }

        private void RefreshControlValue()
        {
            if (setting == null) return;

            string displayValue = setting.GetDisplayValue(pendingValue);
            if (lblSwitchValue != null) lblSwitchValue.Text = displayValue;
            if (lblSelectorValue != null) lblSelectorValue.Text = displayValue;
            if (lblSliderValue != null) lblSliderValue.Text = displayValue;
            if (lblColorValue != null) lblColorValue.Text = displayValue;

            RefreshSwitchPresentation();

            if (sprColorSwatch != null)
            {
                Color color;
                if (TryParseColor(pendingValue, out color))
                {
                    sprColorSwatch.Color = color;
                }
            }
        }

        private void RefreshSwitchPresentation()
        {
            if (setting == null || setting.EffectiveControl != ModSettingControl.Switch) return;

            bool isOn = IsSwitchOn();
            SetSpriteVisible(sprSwitchTrackOn, isOn);
            SetSpriteVisible(sprSwitchKnobOn, isOn);
            SetSpriteVisible(sprSwitchTrackOff, !isOn);
            SetSpriteVisible(sprSwitchKnobOff, !isOn);
        }

        private bool IsSwitchOn()
        {
            if (!string.IsNullOrEmpty(setting.Presentation.RightValue))
            {
                return pendingValue.Equals(setting.Presentation.RightValue, StringComparison.OrdinalIgnoreCase);
            }

            bool boolValue;
            return bool.TryParse(pendingValue, out boolValue) && boolValue;
        }

        private static void SetControlVisible(XUiController control, bool visible)
        {
            if (control?.viewComponent != null) control.viewComponent.IsVisible = visible;
        }

        private static void SetSpriteVisible(XUiV_Sprite sprite, bool visible)
        {
            if (sprite != null) sprite.IsVisible = visible;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = Color.white;
            string[] components = (value ?? "").Split(',');
            if (components.Length != 3) return false;

            int red;
            int green;
            int blue;
            if (!int.TryParse(components[0].Trim(), out red) ||
                !int.TryParse(components[1].Trim(), out green) ||
                !int.TryParse(components[2].Trim(), out blue))
            {
                return false;
            }

            color = new Color(Mathf.Clamp(red, 0, 255) / 255f, Mathf.Clamp(green, 0, 255) / 255f, Mathf.Clamp(blue, 0, 255) / 255f, 1f);
            return true;
        }
    }

    public class ModSettingColorPickerUIController : XUiController
    {
        public const string WindowName = "windowModSettingColorPicker";
        private const int ColorGridResolution = 192;
        private const int HueStripResolution = 192;
        private static ModSetting requestedSetting;
        private static string requestedValue = "";
        private static string requestedModName = "";

        private ModSetting targetSetting;
        private string returnModName = "";
        private XUiV_Texture colorGrid;
        private XUiV_Texture hueStrip;
        private XUiV_Sprite selectedColorSprite;
        private XUiV_Label rgbLabel;
        private XUiV_Label hexLabel;
        private Texture2D colorGridTexture;
        private Texture2D hueStripTexture;
        private Color pendingColor = Color.white;
        private float hue;
        private float saturation;
        private float brightness;
        private bool refreshPickerVisuals;

        public static void Request(ModSetting setting, string value, string modName)
        {
            requestedSetting = setting;
            requestedValue = value ?? "";
            requestedModName = modName ?? "";
        }

        public override void Init()
        {
            base.Init();
            colorGrid = GetChildById("texColorGrid")?.viewComponent as XUiV_Texture;
            hueStrip = GetChildById("texHueStrip")?.viewComponent as XUiV_Texture;
            selectedColorSprite = GetChildById("sprSelectedColor")?.viewComponent as XUiV_Sprite;
            rgbLabel = GetChildById("lblColorRgb")?.viewComponent as XUiV_Label;
            hexLabel = GetChildById("lblColorHex")?.viewComponent as XUiV_Label;

            BindPress("btnApply", HandleApply);
            BindPress("btnCancel", HandleCancel);
            BindPickerInput("texColorGrid", UpdateColorGridFromMouse);
            BindPickerInput("texHueStrip", UpdateHueFromMouse);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            targetSetting = requestedSetting;
            returnModName = requestedModName;
            pendingColor = ParseColor(requestedValue);
            Color.RGBToHSV(pendingColor, out hue, out saturation, out brightness);
            requestedSetting = null;
            requestedValue = "";
            requestedModName = "";
            refreshPickerVisuals = true;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!refreshPickerVisuals) return;

            EnsurePickerTextures();
            RefreshPickerVisuals();
            refreshPickerVisuals = false;
        }

        public override void OnClose()
        {
            base.OnClose();
            targetSetting = null;
            refreshPickerVisuals = false;
            DestroyPickerTextures();
        }

        private void BindPress(string controlName, XUiEvent_OnPressEventHandler handler)
        {
            XUiController control = GetChildById(controlName);
            if (control == null) return;

            XUiController clickable = control.GetChildById("clickable") ?? control;
            clickable.OnPress += handler;
        }

        private void BindPickerInput(string controlName, Action update)
        {
            XUiController control = GetChildById(controlName);
            if (control == null) return;

            control.OnPress += (sender, mouseButton) => update();
            control.OnDrag += (sender, dragType, mousePositionDelta) => update();
        }

        private void UpdateColorGridFromMouse()
        {
            Vector2 relativePosition;
            if (!TryGetRelativeMousePosition(colorGrid, out relativePosition)) return;

            saturation = Mathf.Clamp01(relativePosition.x);
            brightness = Mathf.Clamp01(relativePosition.y);
            pendingColor = Color.HSVToRGB(hue, saturation, brightness);
            RefreshPickerVisuals();
        }

        private void UpdateHueFromMouse()
        {
            Vector2 relativePosition;
            if (!TryGetRelativeMousePosition(hueStrip, out relativePosition)) return;

            hue = Mathf.Clamp01(relativePosition.x);
            pendingColor = Color.HSVToRGB(hue, saturation, brightness);
            RefreshPickerVisuals();
        }

        private bool TryGetRelativeMousePosition(XUiV_Texture texture, out Vector2 relativePosition)
        {
            relativePosition = Vector2.zero;
            if (texture == null) return false;

            Rect textureRect = texture.GetXUiRect();
            if (textureRect.width <= 0f || textureRect.height <= 0f) return false;

            Vector2 mousePosition = xui.GetMouseXUiPosition().AsVector2();
            relativePosition = (mousePosition - textureRect.min) / textureRect.size;
            return true;
        }

        private void EnsurePickerTextures()
        {
            if (colorGridTexture == null)
            {
                colorGridTexture = CreatePickerTexture(ColorGridResolution, ColorGridResolution);
            }

            if (hueStripTexture == null)
            {
                hueStripTexture = CreatePickerTexture(HueStripResolution, 24);
            }

            if (colorGrid != null) colorGrid.Texture = colorGridTexture;
            if (hueStrip != null) hueStrip.Texture = hueStripTexture;
        }

        private static Texture2D CreatePickerTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private void RefreshPickerVisuals()
        {
            RenderColorGrid();
            RenderHueStrip();

            if (selectedColorSprite != null) selectedColorSprite.Color = pendingColor;

            int red = Mathf.Clamp(Mathf.RoundToInt(pendingColor.r * 255f), 0, 255);
            int green = Mathf.Clamp(Mathf.RoundToInt(pendingColor.g * 255f), 0, 255);
            int blue = Mathf.Clamp(Mathf.RoundToInt(pendingColor.b * 255f), 0, 255);
            if (rgbLabel != null) rgbLabel.Text = "R " + red + "   G " + green + "   B " + blue;
            if (hexLabel != null) hexLabel.Text = "#" + red.ToString("X2") + green.ToString("X2") + blue.ToString("X2");
        }

        private void RenderColorGrid()
        {
            if (colorGridTexture == null) return;

            Color[] pixels = new Color[ColorGridResolution * ColorGridResolution];
            for (int y = 0; y < ColorGridResolution; y++)
            {
                float valueAtRow = (float)y / (ColorGridResolution - 1);
                for (int x = 0; x < ColorGridResolution; x++)
                {
                    float saturationAtColumn = (float)x / (ColorGridResolution - 1);
                    pixels[y * ColorGridResolution + x] = Color.HSVToRGB(hue, saturationAtColumn, valueAtRow);
                }
            }

            colorGridTexture.SetPixels(pixels);
            colorGridTexture.Apply(false, false);
        }

        private void RenderHueStrip()
        {
            if (hueStripTexture == null) return;

            Color[] pixels = new Color[HueStripResolution * hueStripTexture.height];
            for (int y = 0; y < hueStripTexture.height; y++)
            {
                for (int x = 0; x < HueStripResolution; x++)
                {
                    pixels[y * HueStripResolution + x] = Color.HSVToRGB((float)x / (HueStripResolution - 1), 1f, 1f);
                }
            }

            hueStripTexture.SetPixels(pixels);
            hueStripTexture.Apply(false, false);
        }

        private void DestroyPickerTextures()
        {
            if (colorGridTexture != null)
            {
                UnityEngine.Object.Destroy(colorGridTexture);
                colorGridTexture = null;
            }

            if (hueStripTexture != null)
            {
                UnityEngine.Object.Destroy(hueStripTexture);
                hueStripTexture = null;
            }
        }

        private void HandleApply(XUiController sender, int mouseButton)
        {
            if (targetSetting == null)
            {
                ReturnToSettings();
                return;
            }

            if (!targetSetting.Warning)
            {
                ApplyColorAndReturn();
                return;
            }

            string warningText = "Changing '" + targetSetting.Name + "' for '" + targetSetting.ModName +
                "' can make your save incompatible or unstable.\n\nBack up your save before continuing. Do you want to apply this setting?";
            XUiC_MessageBoxWindowGroup.ShowOkCancel(
                xui,
                "SETTING WARNING",
                warningText,
                "",
                ApplyColorAndReturn,
                () => { });
        }

        private void HandleCancel(XUiController sender, int mouseButton)
        {
            ReturnToSettings();
        }

        private void ApplyColorAndReturn()
        {
            if (targetSetting != null)
            {
                targetSetting.SetValue(ToRgbValue(pendingColor));
                ModSettingsManager.SaveModSettings(targetSetting.ModName);
            }

            ReturnToSettings();
        }

        private void ReturnToSettings()
        {
            ModSettingsUIController.RequestedModName = returnModName;
            xui.playerUI.windowManager.Close(WindowName);
            xui.playerUI.windowManager.Open("windowModSettings", true);
        }

        private static Color ParseColor(string value)
        {
            string[] components = (value ?? "").Split(',');
            int red;
            int green;
            int blue;
            if (components.Length != 3 ||
                !int.TryParse(components[0].Trim(), out red) ||
                !int.TryParse(components[1].Trim(), out green) ||
                !int.TryParse(components[2].Trim(), out blue))
            {
                return Color.white;
            }

            return new Color(Mathf.Clamp(red, 0, 255) / 255f, Mathf.Clamp(green, 0, 255) / 255f, Mathf.Clamp(blue, 0, 255) / 255f, 1f);
        }

        private static string ToRgbValue(Color color)
        {
            return Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255) + "," +
                Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255) + "," +
                Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
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

            XUiController btnLibrary = __instance.GetChildById("btnModLibrary");
            if (btnLibrary != null)
            {
                XUiController clickable = btnLibrary.GetChildById("clickable") ?? btnLibrary;
                clickable.OnPress += (s, e) =>
                {
                    ModLibraryUIController.PreviousMenu = __instance.WindowGroup.Id;
                    __instance.xui.playerUI.windowManager.Close(__instance.WindowGroup.Id);
                    __instance.xui.playerUI.windowManager.Open("windowModLibrary", true);
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

                XmlValidationRunner.Start(__instance.xui);
        }
    }
}