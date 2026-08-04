using HarmonyLib;
using System;
using System.Collections.Generic;

namespace LizziesMod
{
    public class CustomInputBindingsUIController : XUiController
    {
        public const string WindowName = "windowCustomInputs";
        public static string PreviousMenu = "";
        public static string RequestedModName = "";

        private XUiController modListGrid;
        private XUiController inputListGrid;
        private XUiV_Label modTitleLabel;
        private XUiC_TextInput inputSearchField;
        private XUiV_Label captureStatusLabel;
        private XUiController cancelCaptureButton;
        private string selectedModName = "";
        private string previousCapturedInputId = "";
        private string previousSearchText = "";
        private bool ownsGamePause;

        public override void Init()
        {
            base.Init();
            modListGrid = GetChildById("modListGrid");
            inputListGrid = GetChildById("inputListGrid");
            modTitleLabel = GetChildById("lblModTitle")?.viewComponent as XUiV_Label;
            inputSearchField = GetChildById("txtInputSearch") as XUiC_TextInput;
            captureStatusLabel = GetChildById("lblCaptureStatus")?.viewComponent as XUiV_Label;
            cancelCaptureButton = GetChildById("btnCancelCapture");

            BindButton("btnClose", HandleClose);
            BindButton("btnCancelCapture", HandleCancelCapture);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();
            selectedModName = RequestedModName;
            RequestedModName = "";
            EnsureSelectedMod();
            previousSearchText = GetSearchText();
            PopulateModList();
            PopulateInputList();
            RefreshCaptureState();
        }

        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;
            CustomInputManager.CancelRebind();

            string previousMenu = PreviousMenu;
            PreviousMenu = "";
            if (!string.IsNullOrEmpty(previousMenu))
            {
                xui.playerUI.windowManager.Open(previousMenu, true);
            }
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            RefreshCaptureState();
            RefreshSearchFilter();
        }

        public void SelectMod(string modName)
        {
            if (string.IsNullOrEmpty(modName) ||
                CustomInputManager.GetInputsForMod(modName).Count == 0)
            {
                return;
            }

            selectedModName = modName;
            CustomInputManager.CancelRebind();
            PopulateModList();
            PopulateInputList();
        }

        public void BeginRebind(string inputId)
        {
            if (!CustomInputManager.BeginRebind(inputId)) return;

            RefreshCaptureState();
        }

        public void RestoreDefault(string inputId)
        {
            if (!CustomInputManager.RestoreDefault(inputId)) return;

            PopulateInputList();
        }

        private void EnsureSelectedMod()
        {
            if (!string.IsNullOrEmpty(selectedModName) &&
                CustomInputManager.GetInputsForMod(selectedModName).Count > 0)
            {
                return;
            }

            List<string> modNames = GetInputModNames();
            selectedModName = modNames.Count > 0 ? modNames[0] : "";
        }

        private void PopulateModList()
        {
            if (modListGrid == null) return;

            List<string> modNames = GetInputModNames();
            int index = 0;
            foreach (XUiController child in modListGrid.Children)
            {
                CustomInputModEntryController entry = child as CustomInputModEntryController;
                if (entry == null) continue;

                if (index < modNames.Count)
                {
                    string modName = modNames[index];
                    entry.SetMod(modName, this, modName.Equals(selectedModName, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    entry.Clear();
                }

                index++;
            }
        }

        private void PopulateInputList()
        {
            List<CustomInputDefinition> inputs = CustomInputManager.GetInputsForMod(selectedModName);
            string searchText = GetSearchText();
            if (!string.IsNullOrEmpty(searchText))
            {
                inputs = inputs.FindAll(input => MatchesSearch(input, searchText));
            }

            if (modTitleLabel != null)
            {
                modTitleLabel.Text = string.IsNullOrEmpty(selectedModName) ? "NO CUSTOM INPUTS" : selectedModName.ToUpperInvariant();
            }

            if (inputListGrid == null) return;

            int index = 0;
            foreach (XUiController child in inputListGrid.Children)
            {
                CustomInputEntryController entry = child as CustomInputEntryController;
                if (entry == null) continue;

                if (index < inputs.Count)
                {
                    entry.SetInput(inputs[index], this);
                }
                else
                {
                    entry.Clear();
                }

                index++;
            }
        }

        private void RefreshSearchFilter()
        {
            string searchText = GetSearchText();
            if (searchText.Equals(previousSearchText, StringComparison.Ordinal)) return;

            previousSearchText = searchText;
            PopulateInputList();
        }

        private string GetSearchText()
        {
            return inputSearchField == null ? "" : (inputSearchField.Text ?? "").Trim();
        }

        private static bool MatchesSearch(CustomInputDefinition input, string searchText)
        {
            return input.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   input.Description.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   input.Category.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   input.Chord.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   input.DefaultChord.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   input.Id.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshCaptureState()
        {
            string capturedInputId = CustomInputManager.CapturedInputId;
            bool isCapturing = CustomInputManager.IsCapturing;
            if (cancelCaptureButton != null) cancelCaptureButton.viewComponent.IsVisible = isCapturing;

            if (!capturedInputId.Equals(previousCapturedInputId, StringComparison.OrdinalIgnoreCase))
            {
                previousCapturedInputId = capturedInputId;
                PopulateInputList();
            }

            if (captureStatusLabel == null) return;

            if (isCapturing)
            {
                CustomInputDefinition definition = CustomInputManager.GetInput(capturedInputId);
                captureStatusLabel.Text = definition == null
                    ? "PRESS A KEY COMBINATION (ESC CANCELS)"
                    : "PRESS A KEY COMBINATION FOR " + definition.Name.ToUpperInvariant() + " (ESC CANCELS)";
                captureStatusLabel.IsVisible = true;
            }
            else
            {
                captureStatusLabel.Text = "";
                captureStatusLabel.IsVisible = false;
            }
        }

        private static List<string> GetInputModNames()
        {
            List<string> modNames = new List<string>();
            HashSet<string> knownModNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CustomInputDefinition definition in CustomInputManager.GetInputs())
            {
                if (knownModNames.Add(definition.ModName)) modNames.Add(definition.ModName);
            }

            modNames.Sort(StringComparer.OrdinalIgnoreCase);
            return modNames;
        }

        private void HandleClose(XUiController sender, int mouseButton)
        {
            xui.playerUI.windowManager.Close(WindowName);
        }

        private void HandleCancelCapture(XUiController sender, int mouseButton)
        {
            CustomInputManager.CancelRebind();
            RefreshCaptureState();
        }

        private void BindButton(string buttonId, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(buttonId);
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += handler;
        }
    }

    public class CustomInputModEntryController : XUiController
    {
        private string modName;
        private CustomInputBindingsUIController mainController;
        private XUiV_Label nameLabel;
        private XUiV_Label countLabel;
        private XUiV_Sprite selectedSprite;

        public override void Init()
        {
            base.Init();
            nameLabel = GetChildById("lblModName")?.viewComponent as XUiV_Label;
            countLabel = GetChildById("lblInputCount")?.viewComponent as XUiV_Label;
            selectedSprite = GetChildById("sprSelected")?.viewComponent as XUiV_Sprite;

            XUiController clickable = GetChildById("clickable") ?? this;
            clickable.OnPress += HandlePress;
        }

        public void SetMod(string value, CustomInputBindingsUIController controller, bool isSelected)
        {
            modName = value;
            mainController = controller;
            if (nameLabel != null) nameLabel.Text = modName;
            if (countLabel != null)
            {
                int inputCount = CustomInputManager.GetInputsForMod(modName).Count;
                countLabel.Text = inputCount == 1 ? "1 INPUT" : inputCount + " INPUTS";
            }

            if (selectedSprite != null) selectedSprite.IsVisible = isSelected;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            modName = "";
            mainController = null;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (mainController != null && !string.IsNullOrEmpty(modName)) mainController.SelectMod(modName);
        }
    }

    public class CustomInputEntryController : XUiController
    {
        private CustomInputDefinition definition;
        private CustomInputBindingsUIController mainController;
        private XUiV_Label descriptionLabel;
        private XUiV_Label categoryLabel;
        private XUiV_Label bindingLabel;
        private XUiV_Label defaultLabel;

        public override void Init()
        {
            base.Init();
            descriptionLabel = GetChildById("lblDescription")?.viewComponent as XUiV_Label;
            categoryLabel = GetChildById("lblCategory")?.viewComponent as XUiV_Label;
            bindingLabel = GetChildById("lblBinding")?.viewComponent as XUiV_Label;
            defaultLabel = GetChildById("lblDefault")?.viewComponent as XUiV_Label;
            BindButton("btnRebind", HandleRebind);
            BindButton("btnReset", HandleReset);
        }

        public void SetInput(CustomInputDefinition value, CustomInputBindingsUIController controller)
        {
            definition = value;
            mainController = controller;
            if (descriptionLabel != null) descriptionLabel.Text = definition.Description;
            if (categoryLabel != null) categoryLabel.Text = definition.Category;
            if (bindingLabel != null)
            {
                bindingLabel.Text = CustomInputManager.CapturedInputId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)
                    ? "LISTENING..."
                    : definition.Chord;
            }

            if (defaultLabel != null) defaultLabel.Text = "DEFAULT: " + definition.DefaultChord;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            definition = null;
            mainController = null;
            viewComponent.IsVisible = false;
        }

        private void HandleRebind(XUiController sender, int mouseButton)
        {
            if (definition != null && mainController != null) mainController.BeginRebind(definition.Id);
        }

        private void HandleReset(XUiController sender, int mouseButton)
        {
            if (definition != null && mainController != null) mainController.RestoreDefault(definition.Id);
        }

        private void BindButton(string buttonId, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(buttonId);
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += handler;
        }
    }

    [HarmonyPatch(typeof(XUiC_InGameMenuWindow), "Init")]
    public class CustomInputPauseMenuPatch
    {
        public static void Postfix(XUiC_InGameMenuWindow __instance)
        {
            XUiController button = __instance.GetChildById("btnCustomInputs");
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += (sender, mouseButton) =>
            {
                CustomInputBindingsUIController.PreviousMenu = __instance.WindowGroup.Id;
                CustomInputBindingsUIController.RequestedModName = "";
                __instance.xui.playerUI.windowManager.Close(__instance.WindowGroup.Id);
                __instance.xui.playerUI.windowManager.Open(CustomInputBindingsUIController.WindowName, true);
            };
        }
    }

    [HarmonyPatch(typeof(XUiC_MainMenuButtons), "Init")]
    public class CustomInputMainMenuPatch
    {
        public static void Postfix(XUiC_MainMenuButtons __instance)
        {
            XUiController button = __instance.GetChildById("btnCustomInputs");
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += (sender, mouseButton) =>
            {
                CustomInputBindingsUIController.PreviousMenu = "mainMenu";
                CustomInputBindingsUIController.RequestedModName = "";
                __instance.xui.playerUI.windowManager.Close("mainMenu");
                __instance.xui.playerUI.windowManager.Open(CustomInputBindingsUIController.WindowName, true);
            };
        }
    }

    [HarmonyPatch(typeof(XUiC_OptionsControls), "Init")]
    public class CustomInputOptionsControlsPatch
    {
        public static void Postfix(XUiC_OptionsControls __instance)
        {
            XUiController tabsHeader = __instance.GetChildById("tabsHeader");
            XUiController tabButtons = tabsHeader?.GetChildById("tabButtons");
            XUiController modsTab = tabButtons?.GetChildById("10");
            XUiController modsButton = modsTab?.GetChildById("headerbutton");
            if (modsButton == null) return;

            modsButton.OnPress += (sender, mouseButton) =>
            {
                CustomInputBindingsUIController.PreviousMenu = __instance.WindowGroup.Id;
                CustomInputBindingsUIController.RequestedModName = "";
                __instance.xui.playerUI.windowManager.Close(__instance.WindowGroup.Id);
                __instance.xui.playerUI.windowManager.Open(CustomInputBindingsUIController.WindowName, true);
            };
        }
    }
}