using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class MissingProfileModsUIController : XUiController
    {
        private const string WindowName = "windowProfileMissingMods";

        private XUiController missingModsListGrid;
        private bool isRestarting;

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
        }

        public override void OnOpen()
        {
            base.OnOpen();
            PopulateMissingMods();
        }

        public override void OnClose()
        {
            base.OnClose();

            if (!isRestarting)
            {
                xui.playerUI.windowManager.Open("windowProfileSelector", true);
            }
        }

        private void PopulateMissingMods()
        {
            if (missingModsListGrid == null) return;

            List<string> missingMods = ModSettingsManager.LastMissingProfileMods;
            int index = 0;

            foreach (XUiController child in missingModsListGrid.Children)
            {
                MissingProfileModEntryController entry = child as MissingProfileModEntryController;
                if (entry == null) continue;

                if (index < missingMods.Count)
                {
                    entry.SetModName(missingMods[index]);
                    index++;
                }
                else
                {
                    entry.Clear();
                }
            }
        }
    }

    public class MissingProfileModEntryController : XUiController
    {
        private XUiV_Label modNameLabel;

        public override void Init()
        {
            base.Init();
            modNameLabel = GetChildById("lblModName")?.viewComponent as XUiV_Label;
        }

        public void SetModName(string modName)
        {
            if (modNameLabel != null)
            {
                modNameLabel.Text = modName;
            }

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            viewComponent.IsVisible = false;
        }
    }
}