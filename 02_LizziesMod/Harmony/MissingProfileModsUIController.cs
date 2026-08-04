using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class MissingProfileModsUIController : XUiController
    {
        private const string WindowName = "windowProfileMissingMods";

        private XUiController missingModsListGrid;
        private XUiController findInPortalButton;
        private bool isRestarting;
        private bool isOpeningPortal;
        private bool ownsGamePause;
        private string previousPortalStatus = "";

        public override void Init()
        {
            base.Init();
            missingModsListGrid = GetChildById("missingModsListGrid");

            XUiController cancelButton = GetChildById("btnCancel");
            if (cancelButton != null)
            {
                XUiController clickable = cancelButton.GetChildById("clickable") ?? cancelButton;
                clickable.OnPress += (s, e) => xui.playerUI.windowManager.Close(WindowName);
            }

            XUiController restartButton = GetChildById("btnRestart");
            if (restartButton != null)
            {
                XUiController clickable = restartButton.GetChildById("clickable") ?? restartButton;
                clickable.OnPress += (s, e) =>
                {
                    isRestarting = true;
                    Application.Quit();
                };
            }

            findInPortalButton = GetChildById("btnFindInPortal");
            if (findInPortalButton != null)
            {
                XUiController clickable = findInPortalButton.GetChildById("clickable") ?? findInPortalButton;
                clickable.OnPress += (s, e) => OpenMatchingPortalPackage();
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();
            PopulateMissingMods();
            previousPortalStatus = ModPortalManager.Status;
            UpdatePortalButton();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            string portalStatus = ModPortalManager.Status;
            if (!portalStatus.Equals(previousPortalStatus, System.StringComparison.Ordinal))
            {
                previousPortalStatus = portalStatus;
                UpdatePortalButton();
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;

            if (isOpeningPortal)
            {
                isOpeningPortal = false;
                return;
            }

            if (!isRestarting)
            {
                xui.playerUI.windowManager.Open("windowProfileSelector", true);
            }
        }

        private void PopulateMissingMods()
        {
            if (missingModsListGrid == null) return;

            List<MissingProfileModInfo> missingMods = ModSettingsManager.LastMissingProfileMods;
            int index = 0;

            foreach (XUiController child in missingModsListGrid.Children)
            {
                MissingProfileModEntryController entry = child as MissingProfileModEntryController;
                if (entry == null) continue;

                if (index < missingMods.Count)
                {
                    MissingProfileModInfo missingMod = missingMods[index];
                    entry.SetMod(missingMod.Name, missingMod.Version);
                    index++;
                }
                else
                {
                    entry.Clear();
                }
            }
        }

        private void UpdatePortalButton()
        {
            if (findInPortalButton?.viewComponent != null)
            {
                findInPortalButton.viewComponent.IsVisible = FindMatchingPortalPackage() != null;
            }
        }

        private ModPortalPackage FindMatchingPortalPackage()
        {
            foreach (MissingProfileModInfo missingMod in ModSettingsManager.LastMissingProfileMods)
            {
                ModPortalPackage package = ModPortalManager.FindPackage(missingMod.Name, missingMod.Version);
                if (package != null) return package;
            }

            return null;
        }

        private void OpenMatchingPortalPackage()
        {
            ModPortalPackage package = FindMatchingPortalPackage();
            if (package == null) return;

            isOpeningPortal = true;
            ModPortalUIController.PreviousMenu = WindowName;
            ModPortalUIController.RequestedPackageId = package.Id;
            ModPortalUIController.RequestedPackageVersion = package.Version;
            xui.playerUI.windowManager.Close(WindowName);
            xui.playerUI.windowManager.Open(ModPortalUIController.WindowName, true);
        }
    }

    public class MissingProfileModEntryController : XUiController
    {
        private XUiV_Label modNameLabel;
        private XUiV_Label modVersionLabel;

        public override void Init()
        {
            base.Init();
            modNameLabel = GetChildById("lblModName")?.viewComponent as XUiV_Label;
            modVersionLabel = GetChildById("lblModVersion")?.viewComponent as XUiV_Label;
        }

        public void SetMod(string modName, string modVersion)
        {
            if (modNameLabel != null)
            {
                modNameLabel.Text = modName;
            }

            if (modVersionLabel != null)
            {
                modVersionLabel.Text = string.IsNullOrEmpty(modVersion)
                    ? "Required version: not recorded"
                    : "Required version: " + modVersion;
            }

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            viewComponent.IsVisible = false;
        }
    }
}