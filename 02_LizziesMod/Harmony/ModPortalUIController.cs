using System;
using System.Collections.Generic;
using HarmonyLib;

namespace LizziesMod
{
    public class ModPortalUIController : XUiController
    {
        public const string WindowName = "windowModPortal";
        private const int PackagesPerPage = 50;

        public static string PreviousMenu = "";
        public static string RequestedPackageId = "";
        public static string RequestedPackageVersion = "";

        private XUiController packageGrid;
        private XUiC_TextInput searchField;
        private XUiV_Label packageTitleLabel;
        private XUiV_Label packageMetaLabel;
        private XUiV_Label packageAuthorLabel;
        private XUiV_Label packageDescriptionLabel;
        private XUiV_Label portalStatusLabel;
        private XUiV_Label pageStatusLabel;
        private XUiController installButton;
        private XUiController previousPageButton;
        private XUiController nextPageButton;
        private readonly List<ModPortalPackage> allPackages = new List<ModPortalPackage>();
        private string selectedPackageId = "";
        private string previousSearchText = "";
        private string previousPortalStatus = "";
        private bool previousPortalWorking;
        private int currentPageIndex;
        private bool ownsGamePause;

        public override void Init()
        {
            base.Init();
            packageGrid = GetChildById("portalPackageGrid");
            searchField = GetChildById("txtPortalSearch") as XUiC_TextInput;
            packageTitleLabel = GetChildById("lblPortalPackageTitle")?.viewComponent as XUiV_Label;
            packageMetaLabel = GetChildById("lblPortalPackageMeta")?.viewComponent as XUiV_Label;
            packageAuthorLabel = GetChildById("lblPortalPackageAuthor")?.viewComponent as XUiV_Label;
            packageDescriptionLabel = GetChildById("lblPortalPackageDescription")?.viewComponent as XUiV_Label;
            portalStatusLabel = GetChildById("lblPortalStatus")?.viewComponent as XUiV_Label;
            pageStatusLabel = GetChildById("lblPortalPageStatus")?.viewComponent as XUiV_Label;
            installButton = GetChildById("btnInstallPortalPackage");
            previousPageButton = GetChildById("btnPrevPortalPackages");
            nextPageButton = GetChildById("btnNextPortalPackages");

            BindButton("btnRefreshPortal", HandleRefresh);
            BindButton("btnInstallPortalPackage", HandleInstall);
            BindButton("btnPrevPortalPackages", (sender, mouseButton) => ChangePage(-1));
            BindButton("btnNextPortalPackages", (sender, mouseButton) => ChangePage(1));
            BindButton("btnClose", HandleClose);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();

            string requestedPackageId = RequestedPackageId;
            string requestedPackageVersion = RequestedPackageVersion;
            RequestedPackageId = "";
            RequestedPackageVersion = "";
            selectedPackageId = "";
            currentPageIndex = 0;
            if (searchField != null) searchField.Text = requestedPackageId;
            previousSearchText = GetSearchText();
            previousPortalStatus = ModPortalManager.Status;
            previousPortalWorking = ModPortalManager.IsWorking;
            RefreshPackages();

            ModPortalPackage requestedPackage = FindPackage(requestedPackageId, requestedPackageVersion);
            if (requestedPackage != null)
            {
                SelectPackage(requestedPackage);
            }
            else
            {
                List<ModPortalPackage> filteredPackages = GetFilteredPackages();
                if (filteredPackages.Count > 0) SelectPackage(filteredPackages[0]);
            }

            if (!string.IsNullOrEmpty(requestedPackageId) && requestedPackage == null)
            {
                SetStatus("No exact portal package was found for '" + requestedPackageId + "'.");
            }
            else
            {
                SetStatus(previousPortalStatus);
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;

            string previousMenu = PreviousMenu;
            PreviousMenu = "";
            if (string.IsNullOrEmpty(previousMenu) && !Main.IsPlayerInGame())
            {
                previousMenu = "mainMenu";
            }

            if (!string.IsNullOrEmpty(previousMenu))
            {
                xui.playerUI.windowManager.Open(previousMenu, true);
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            string searchText = GetSearchText();
            if (!searchText.Equals(previousSearchText, StringComparison.Ordinal))
            {
                previousSearchText = searchText;
                currentPageIndex = 0;
                PopulatePackageList();
            }

            string portalStatus = ModPortalManager.Status;
            bool portalWorking = ModPortalManager.IsWorking;
            if (!portalStatus.Equals(previousPortalStatus, StringComparison.Ordinal) || portalWorking != previousPortalWorking)
            {
                previousPortalStatus = portalStatus;
                previousPortalWorking = portalWorking;
                SetStatus(portalStatus);
                if (!portalWorking) RefreshPackages();
                UpdateInstallButton();
            }
        }

        public void SelectPackage(ModPortalPackage package)
        {
            if (package == null) return;

            selectedPackageId = package.Id;
            UpdatePackageDetails();
            PopulatePackageList();
        }

        private void RefreshPackages()
        {
            allPackages.Clear();
            allPackages.AddRange(ModPortalManager.GetPackages());
            if (FindPackage(selectedPackageId, "") == null) selectedPackageId = "";
            UpdatePackageDetails();
            PopulatePackageList();
        }

        private void PopulatePackageList()
        {
            List<ModPortalPackage> filteredPackages = GetFilteredPackages();
            int totalPages = Math.Max(1, (filteredPackages.Count + PackagesPerPage - 1) / PackagesPerPage);
            currentPageIndex = Math.Max(0, Math.Min(currentPageIndex, totalPages - 1));
            int startIndex = currentPageIndex * PackagesPerPage;
            int endIndex = Math.Min(startIndex + PackagesPerPage, filteredPackages.Count);

            if (packageGrid != null)
            {
                int packageIndex = startIndex;
                foreach (XUiController child in packageGrid.Children)
                {
                    ModPortalPackageEntryController entry = child as ModPortalPackageEntryController;
                    if (entry == null) continue;

                    if (packageIndex < endIndex)
                    {
                        ModPortalPackage package = filteredPackages[packageIndex];
                        entry.SetPackage(package, this, package.Id.Equals(selectedPackageId, StringComparison.OrdinalIgnoreCase));
                        packageIndex++;
                    }
                    else
                    {
                        entry.Clear();
                    }
                }
            }

            if (pageStatusLabel != null)
            {
                pageStatusLabel.Text = filteredPackages.Count == 0
                    ? "NO PACKAGES FOUND"
                    : "SHOWING " + (startIndex + 1) + "-" + endIndex + " OF " + filteredPackages.Count;
            }

            if (previousPageButton?.viewComponent != null) previousPageButton.viewComponent.IsVisible = currentPageIndex > 0;
            if (nextPageButton?.viewComponent != null) nextPageButton.viewComponent.IsVisible = currentPageIndex < totalPages - 1;
        }

        private void UpdatePackageDetails()
        {
            ModPortalPackage package = FindPackage(selectedPackageId, "");
            if (package == null)
            {
                if (packageTitleLabel != null) packageTitleLabel.Text = "NO PACKAGE SELECTED";
                if (packageMetaLabel != null) packageMetaLabel.Text = "";
                if (packageAuthorLabel != null) packageAuthorLabel.Text = "";
                if (packageDescriptionLabel != null) packageDescriptionLabel.Text = "";
                UpdateInstallButton();
                return;
            }

            if (packageTitleLabel != null) packageTitleLabel.Text = GetDisplayName(package);
            if (packageMetaLabel != null)
            {
                packageMetaLabel.Text = "VERSION " + package.Version +
                    (string.IsNullOrEmpty(package.GameVersion) ? "" : "    GAME " + package.GameVersion) +
                    "    XML FILES " + package.Files.Count;
            }

            if (packageAuthorLabel != null)
            {
                packageAuthorLabel.Text = string.IsNullOrEmpty(package.Author) ? "" : "BY " + package.Author;
            }

            if (packageDescriptionLabel != null) packageDescriptionLabel.Text = package.Description ?? "";
            UpdateInstallButton();
        }

        private void UpdateInstallButton()
        {
            if (installButton?.viewComponent != null)
            {
                installButton.viewComponent.IsVisible = FindPackage(selectedPackageId, "") != null && !ModPortalManager.IsWorking;
            }
        }

        private void HandleRefresh(XUiController sender, int mouseButton)
        {
            string message;
            ModPortalManager.BeginRefresh(out message);
            previousPortalStatus = message;
            previousPortalWorking = ModPortalManager.IsWorking;
            SetStatus(message);
            UpdateInstallButton();
        }

        private void HandleInstall(XUiController sender, int mouseButton)
        {
            string message;
            ModPortalManager.BeginInstall(selectedPackageId, out message);
            previousPortalStatus = message;
            previousPortalWorking = ModPortalManager.IsWorking;
            SetStatus(message);
            UpdateInstallButton();
        }

        private void ChangePage(int direction)
        {
            currentPageIndex += direction;
            PopulatePackageList();
        }

        private void HandleClose(XUiController sender, int mouseButton)
        {
            xui.playerUI.windowManager.Close(WindowName);
        }

        private List<ModPortalPackage> GetFilteredPackages()
        {
            string searchText = GetSearchText();
            if (string.IsNullOrEmpty(searchText)) return new List<ModPortalPackage>(allPackages);

            List<ModPortalPackage> filteredPackages = new List<ModPortalPackage>();
            foreach (ModPortalPackage package in allPackages)
            {
                if (package.Id.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    GetDisplayName(package).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (package.Description ?? "").IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredPackages.Add(package);
                }
            }

            return filteredPackages;
        }

        private ModPortalPackage FindPackage(string packageId, string packageVersion)
        {
            foreach (ModPortalPackage package in allPackages)
            {
                if (package.Id.Equals(packageId ?? "", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(packageVersion) || package.Version.Equals(packageVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    return package;
                }
            }

            return null;
        }

        private void SetStatus(string message)
        {
            if (portalStatusLabel != null) portalStatusLabel.Text = message ?? "";
        }

        private string GetSearchText()
        {
            return searchField == null ? "" : (searchField.Text ?? "").Trim();
        }

        private static string GetDisplayName(ModPortalPackage package)
        {
            return string.IsNullOrEmpty(package.DisplayName) ? package.Id : package.DisplayName;
        }

        private void BindButton(string buttonId, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(buttonId);
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += handler;
        }
    }

    public class ModPortalPackageEntryController : XUiController
    {
        private ModPortalPackage package;
        private ModPortalUIController mainController;
        private XUiV_Label packageNameLabel;
        private XUiV_Label packageVersionLabel;
        private XUiV_Sprite selectedSprite;

        public override void Init()
        {
            base.Init();
            packageNameLabel = GetChildById("lblPortalPackageName")?.viewComponent as XUiV_Label;
            packageVersionLabel = GetChildById("lblPortalPackageVersion")?.viewComponent as XUiV_Label;
            selectedSprite = GetChildById("sprSelected")?.viewComponent as XUiV_Sprite;
            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += HandlePress;
        }

        public void SetPackage(ModPortalPackage newPackage, ModPortalUIController controller, bool isSelected)
        {
            package = newPackage;
            mainController = controller;
            if (packageNameLabel != null) packageNameLabel.Text = string.IsNullOrEmpty(newPackage.DisplayName) ? newPackage.Id : newPackage.DisplayName;
            if (packageVersionLabel != null) packageVersionLabel.Text = "VERSION " + newPackage.Version;
            if (selectedSprite != null) selectedSprite.IsVisible = isSelected;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            package = null;
            mainController = null;
            if (selectedSprite != null) selectedSprite.IsVisible = false;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (package != null && mainController != null) mainController.SelectPackage(package);
        }
    }

    [HarmonyPatch(typeof(XUiC_InGameMenuWindow), "Init")]
    public class ModPortalPauseMenuPatch
    {
        public static void Postfix(XUiC_InGameMenuWindow __instance)
        {
            XUiController button = __instance.GetChildById("btnModPortal");
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += (sender, mouseButton) =>
            {
                ModPortalUIController.PreviousMenu = __instance.WindowGroup.Id;
                ModPortalUIController.RequestedPackageId = "";
                ModPortalUIController.RequestedPackageVersion = "";
                __instance.xui.playerUI.windowManager.Close(__instance.WindowGroup.Id);
                __instance.xui.playerUI.windowManager.Open(ModPortalUIController.WindowName, true);
            };
        }
    }

    [HarmonyPatch(typeof(XUiC_MainMenuButtons), "Init")]
    public class ModPortalMainMenuPatch
    {
        public static void Postfix(XUiC_MainMenuButtons __instance)
        {
            XUiController button = __instance.GetChildById("btnModPortal");
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += (sender, mouseButton) =>
            {
                ModPortalUIController.PreviousMenu = "mainMenu";
                ModPortalUIController.RequestedPackageId = "";
                ModPortalUIController.RequestedPackageVersion = "";
                __instance.xui.playerUI.windowManager.Close("mainMenu");
                __instance.xui.playerUI.windowManager.Open(ModPortalUIController.WindowName, true);
            };
        }
    }
}