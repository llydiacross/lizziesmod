using UnityEngine;

namespace LizziesMod
{
    public class ModErrorWindowUIController : XUiController
    {
        private const string WindowName = "windowModLoadErrors";

        private static GameManager pendingGameManager;
        private static bool isGameStartPrompt;

        private XUiV_Label errorTextLabel;
        private XUiController proceedButton;
        private XUiController disableModsButton;

        public static void ShowValidationErrors(XUi xui)
        {
            pendingGameManager = null;
            isGameStartPrompt = false;
            Open(xui);
        }

        public static void ShowGameStartErrors(GameManager gameManager)
        {
            if (LocalPlayerUI.primaryUI == null)
            {
                Logger.Error("[ModErrorHandler] Cannot show mod load errors because the menu UI is unavailable.");
                return;
            }

            pendingGameManager = gameManager;
            isGameStartPrompt = true;
            Open(LocalPlayerUI.primaryUI.xui);
        }

        private static void Open(XUi xui)
        {
            if (xui == null || xui.playerUI == null)
            {
                Logger.Error("[ModErrorHandler] Cannot show mod load errors because the menu window manager is unavailable.");
                return;
            }

            xui.playerUI.windowManager.Open(WindowName, true);
        }

        public override void Init()
        {
            base.Init();
            errorTextLabel = GetChildById("lblErrorText")?.viewComponent as XUiV_Label;
            proceedButton = GetChildById("btnProceed");
            disableModsButton = GetChildById("btnDisableMods");

            if (proceedButton != null)
            {
                XUiController clickable = proceedButton.GetChildById("clickable") ?? proceedButton;
                clickable.OnPress += (sender, mouseButton) => HandleProceed();
            }

            if (disableModsButton != null)
            {
                XUiController clickable = disableModsButton.GetChildById("clickable") ?? disableModsButton;
                clickable.OnPress += (sender, mouseButton) => HandleDisableMods();
            }

            XUiController closeButton = GetChildById("btnClose");
            if (closeButton != null)
            {
                XUiController clickable = closeButton.GetChildById("clickable") ?? closeButton;
                clickable.OnPress += (sender, mouseButton) => xui.playerUI.windowManager.Close(WindowName);
            }
        }

        public override void OnOpen()
        {
            base.OnOpen();

            if (errorTextLabel != null)
            {
                errorTextLabel.Text = ModErrorHandler.GetDiagnosticReport() + GetActionText();
            }

            SetVisible(proceedButton, isGameStartPrompt);
            SetVisible(disableModsButton, ModErrorHandler.HasProblematicMods());
        }

        public override void OnClose()
        {
            base.OnClose();
            ModErrorHandler.AcknowledgeErrors();
            pendingGameManager = null;
        }

        private void HandleProceed()
        {
            GameManager gameManager = pendingGameManager;
            xui.playerUI.windowManager.Close(WindowName);

            Logger.Info("[ModErrorHandler] User chose to continue despite mod loading errors.");
            ModErrorHandler.ProceedWithGameStart(gameManager);
        }

        private void HandleDisableMods()
        {
            bool disabledAny = ModErrorHandler.DisableProblematicMods();
            xui.playerUI.windowManager.Close(WindowName);

            if (disabledAny)
            {
                xui.playerUI.windowManager.Open("windowModSettingsRestartPrompt", true);
            }
        }

        private static string GetActionText()
        {
            if (isGameStartPrompt)
            {
                return "\n\n[FFCC33]Choose CONTINUE to start anyway, or DISABLE MODS to disable the affected mods and restart.[-]";
            }

            return "\n\n[FFCC33]Fix the affected mod files and restart before starting a world.[-]";
        }

        private static void SetVisible(XUiController controller, bool isVisible)
        {
            if (controller?.viewComponent != null)
            {
                controller.viewComponent.IsVisible = isVisible;
            }
        }
    }

    public class ModDiagnosticsHUDController : XUiController
    {
        private XUiV_Label statusLabel;
        private XUiV_Label detailsLabel;
        private int displayedErrorCount = -1;
        private int displayedWarningCount = -1;

        public override void Init()
        {
            base.Init();
            statusLabel = GetChildById("lblModDiagnosticStatus")?.viewComponent as XUiV_Label;
            detailsLabel = GetChildById("lblModDiagnosticDetails")?.viewComponent as XUiV_Label;
            RefreshDiagnostics();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            ModErrorHandler.GetDiagnosticCounts(out int errorCount, out int warningCount);
            if (errorCount != displayedErrorCount || warningCount != displayedWarningCount)
            {
                RefreshDiagnostics();
            }
        }

        private void RefreshDiagnostics()
        {
            ModErrorHandler.GetDiagnosticCounts(out int errorCount, out int warningCount);
            displayedErrorCount = errorCount;
            displayedWarningCount = warningCount;

            bool hasDiagnostics = errorCount > 0 || warningCount > 0;
            if (viewComponent != null) viewComponent.IsVisible = hasDiagnostics;
            if (!hasDiagnostics) return;

            string report = ModErrorHandler.GetDiagnosticReport();
            if (statusLabel != null)
            {
                statusLabel.Text = errorCount > 0 ? "MOD XML ERRORS" : "MOD XML WARNINGS";
                statusLabel.Color = errorCount > 0
                    ? new Color32(190, 35, 35, 255)
                    : new Color32(175, 105, 0, 255);
                statusLabel.ToolTip = report;
            }

            if (detailsLabel != null)
            {
                detailsLabel.Text = errorCount + " error" + (errorCount == 1 ? "" : "s") +
                    " | " + warningCount + " warning" + (warningCount == 1 ? "" : "s");
                detailsLabel.ToolTip = report;
            }
        }
    }
}