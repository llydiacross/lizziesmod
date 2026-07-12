using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class ProfileSelectorUIController : XUiController
    {

        public static string PreviousMenu = "";

        private XUiController profileListGrid;
        private XUiV_Label lblProfileTitle;
        private XUiV_Label lblEnabledMods;
        private XUiV_Label lblDisabledMods;
        private XUiV_Label lblSettingsCount;

        private string selectedProfileName = "";

        public override void Init()
        {
            base.Init();

            profileListGrid = GetChildById("profileListGrid");
            lblProfileTitle = GetChildById("lblProfileTitle")?.viewComponent as XUiV_Label;
            lblEnabledMods = GetChildById("lblEnabledMods")?.viewComponent as XUiV_Label;
            lblDisabledMods = GetChildById("lblDisabledMods")?.viewComponent as XUiV_Label;
            lblSettingsCount = GetChildById("lblSettingsCount")?.viewComponent as XUiV_Label;

            XUiController btnLoad = GetChildById("btnLoad");
            if (btnLoad != null)
            {
                XUiController clickable = btnLoad.GetChildById("clickable") ?? btnLoad;
                clickable.OnPress += HandleLoadProfile;
            }

            XUiController btnCancel = GetChildById("btnCancel");
            if (btnCancel != null)
            {
                XUiController clickable = btnCancel.GetChildById("clickable") ?? btnCancel;
                clickable.OnPress += (s, e) => xui.playerUI.windowManager.Close("windowProfileSelector");
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            selectedProfileName = "";
            PopulateProfileList();
        }

        private void PopulateProfileList()
        {
            if (profileListGrid == null) return;

            List<ModProfileInfo> profiles = ModSettingsManager.GetAvailableProfiles();
            int i = 0;

            foreach (var child in profileListGrid.Children)
            {
                if (child is ProfileEntryController entry)
                {
                    if (i < profiles.Count)
                    {
                        entry.SetProfile(profiles[i], this);
                        if (i == 0) SelectProfile(profiles[i]);
                    }
                    else
                    {
                        entry.Clear();
                    }
                    i++;
                }
            }

            if (profiles.Count == 0) ClearSelection();
        }

        public void SelectProfile(ModProfileInfo info)
        {
            selectedProfileName = info.Name;

            if (lblProfileTitle != null) lblProfileTitle.Text = info.Name;
            if (lblSettingsCount != null) lblSettingsCount.Text = $"Total Tweaks: {info.TotalSettingsModified}";

            if (lblEnabledMods != null)
            {
                lblEnabledMods.Text = info.EnabledMods.Count > 0
                    ? string.Join(", ", info.EnabledMods)
                    : "None";
            }

            if (lblDisabledMods != null)
            {
                lblDisabledMods.Text = info.DisabledMods.Count > 0
                    ? string.Join(", ", info.DisabledMods)
                    : "None";
            }
        }

        private void ClearSelection()
        {
            selectedProfileName = "";
            if (lblProfileTitle != null) lblProfileTitle.Text = "NO PROFILES SAVED";
            if (lblEnabledMods != null) lblEnabledMods.Text = "";
            if (lblDisabledMods != null) lblDisabledMods.Text = "";
            if (lblSettingsCount != null) lblSettingsCount.Text = "";
        }

        private void HandleLoadProfile(XUiController _sender, int _mouseButton)
        {
            if (!string.IsNullOrEmpty(selectedProfileName))
            {
                bool success = ModSettingsManager.LoadProfile(selectedProfileName);
                if (success)
                {
                    ModSettingsUIController.LastLoadedProfile = selectedProfileName;
                }
            }

            xui.playerUI.windowManager.Close("windowProfileSelector");
        }
        public override void OnClose()
        {
            base.OnClose();
            if (!string.IsNullOrEmpty(PreviousMenu))
                xui.playerUI.windowManager.Open(PreviousMenu, true);
        }
    }

    public class ProfileEntryController : XUiController
    {
      
        private ModProfileInfo profileInfo;
        private ProfileSelectorUIController mainController;
        private XUiV_Label lblName;

        public override void Init()
        {
            base.Init();
            lblName = GetChildById("lblName")?.viewComponent as XUiV_Label;
            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += (s, e) => mainController?.SelectProfile(profileInfo);
        }

        public void SetProfile(ModProfileInfo info, ProfileSelectorUIController main)
        {
            profileInfo = info;
            mainController = main;
            if (lblName != null) lblName.Text = info.Name;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            profileInfo = null;
            viewComponent.IsVisible = false;
        }
    }
}