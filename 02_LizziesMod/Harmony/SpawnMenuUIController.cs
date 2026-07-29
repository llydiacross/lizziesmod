using System;
using System.Collections.Generic;

namespace LizziesMod
{
    public class SpawnMenuUIController : XUiController
    {
        public const string WindowName = "windowSpawnMenu";

        public static bool IsSpawnMenuOpen { get; private set; }

        private XUiController categoryGrid;
        private XUiController propGrid;
        private XUiC_TextInput searchInput;
        private XUiV_Label statusLabel;
        private XUiV_Label hintLabel;
        private XUiV_Sprite propsTabSelectedSprite;
        private XUiV_Sprite ragdollsTabSelectedSprite;
        private XUiV_Sprite entitiesTabSelectedSprite;
        private SpawnMenuTab selectedTab = SpawnMenuTab.Props;
        private string selectedCategoryId = "all";
        private string lastSearchText = "";

        public override void Init()
        {
            base.Init();
            categoryGrid = GetChildById("categoryGrid");
            propGrid = GetChildById("propGrid");
            searchInput = GetChildById("txtSearch") as XUiC_TextInput;
            statusLabel = GetChildById("lblStatus")?.viewComponent as XUiV_Label;
            hintLabel = GetChildById("lblHint")?.viewComponent as XUiV_Label;
            propsTabSelectedSprite = GetChildById("sprTabPropsSelected")?.viewComponent as XUiV_Sprite;
            ragdollsTabSelectedSprite = GetChildById("sprTabRagdollsSelected")?.viewComponent as XUiV_Sprite;
            entitiesTabSelectedSprite = GetChildById("sprTabEntitiesSelected")?.viewComponent as XUiV_Sprite;

            BindButton("btnTabProps", (sender, mouseButton) => SelectTab(SpawnMenuTab.Props));
            BindButton("btnTabRagdolls", (sender, mouseButton) => SelectTab(SpawnMenuTab.Ragdolls));
            BindButton("btnTabEntities", (sender, mouseButton) => SelectTab(SpawnMenuTab.Entities));
            BindButton("btnUndo", HandleUndo);
            BindButton("btnClearOwned", HandleClearOwned);
            BindButton("btnClose", HandleClose);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            IsSpawnMenuOpen = true;
            SpawnCatalog.EnsureLoaded();
            selectedTab = SpawnMenuTab.Props;
            selectedCategoryId = "all";

            if (searchInput != null) searchInput.Text = "";
            lastSearchText = "";
            PopulateCategories();
            PopulateProps();
        }

        public override void OnClose()
        {
            base.OnClose();
            IsSpawnMenuOpen = false;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            string searchText = searchInput?.Text ?? "";
            if (!string.Equals(searchText, lastSearchText, StringComparison.Ordinal))
            {
                lastSearchText = searchText;
                PopulateProps();
            }
        }

        public void SelectCategory(string categoryId)
        {
            selectedCategoryId = categoryId;
            PopulateCategories();
            PopulateProps();
        }

        public void SelectTab(SpawnMenuTab tab)
        {
            if (selectedTab == tab) return;

            selectedTab = tab;
            selectedCategoryId = "all";
            PopulateCategories();
            PopulateProps();
        }

        private void PopulateCategories()
        {
            if (categoryGrid == null) return;

            List<SpawnMenuCategory> categories = new List<SpawnMenuCategory>
            {
                new SpawnMenuCategory { Id = "all", DisplayName = "All " + GetTabLabel(selectedTab), Order = 0 }
            };
            categories.AddRange(SpawnCatalog.GetCategories(selectedTab));

            int index = 0;
            foreach (XUiController child in categoryGrid.Children)
            {
                PropSpawnCategoryEntryController entry = child as PropSpawnCategoryEntryController;
                if (entry == null) continue;

                if (index < categories.Count)
                {
                    entry.SetCategory(categories[index], this, categories[index].Id.Equals(selectedCategoryId, StringComparison.OrdinalIgnoreCase));
                    index++;
                }
                else
                {
                    entry.Clear();
                }
            }
        }

        private void PopulateProps()
        {
            if (propGrid == null) return;

            List<SpawnMenuEntryDefinition> entries = SpawnCatalog.GetEntries(selectedTab, selectedCategoryId, searchInput?.Text);
            if (statusLabel != null)
            {
                string label = entries.Count == 1 ? GetTabSingularLabel(selectedTab) : GetTabLabel(selectedTab);
                statusLabel.Text = entries.Count + " " + label.ToUpperInvariant();
            }
            if (hintLabel != null) hintLabel.Text = "SELECT TO SPAWN. SHIFT+SELECT A PROP TO ADD IT TO INVENTORY";
            UpdateTabSelection();

            int index = 0;
            foreach (XUiController child in propGrid.Children)
            {
                PropSpawnEntryController entry = child as PropSpawnEntryController;
                if (entry == null) continue;

                if (index < entries.Count)
                {
                    entry.SetEntry(entries[index], this);
                    index++;
                }
                else
                {
                    entry.Clear();
                }
            }
        }

        public void Spawn(SpawnMenuEntryDefinition definition)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return;

            SpawnMenuManager.RequestSpawn(player, definition.Id);
        }

        public void AddToInventory(SpawnMenuEntryDefinition definition)
        {
            if (!(definition is SpawnablePropDefinition)) return;

            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return;

            SpawnMenuManager.RequestGrantItem(player, definition.Id);
        }

        private static string GetTabLabel(SpawnMenuTab tab)
        {
            if (tab == SpawnMenuTab.Entities) return "Entities";
            if (tab == SpawnMenuTab.Ragdolls) return "Ragdolls";
            return "Props";
        }

        private static string GetTabSingularLabel(SpawnMenuTab tab)
        {
            if (tab == SpawnMenuTab.Entities) return "Entity";
            if (tab == SpawnMenuTab.Ragdolls) return "Ragdoll";
            return "Prop";
        }

        private void UpdateTabSelection()
        {
            if (propsTabSelectedSprite != null) propsTabSelectedSprite.IsVisible = selectedTab == SpawnMenuTab.Props;
            if (ragdollsTabSelectedSprite != null) ragdollsTabSelectedSprite.IsVisible = selectedTab == SpawnMenuTab.Ragdolls;
            if (entitiesTabSelectedSprite != null) entitiesTabSelectedSprite.IsVisible = selectedTab == SpawnMenuTab.Entities;
        }

        private void HandleUndo(XUiController sender, int mouseButton)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player != null) SpawnMenuManager.RequestUndo(player);
        }

        private void HandleClearOwned(XUiController sender, int mouseButton)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player != null) SpawnMenuManager.RequestClearOwned(player);
        }

        private void HandleClose(XUiController sender, int mouseButton)
        {
            xui.playerUI.windowManager.Close(WindowName);
        }

        private void BindButton(string buttonId, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(buttonId);
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += handler;
        }
    }

    public class PropSpawnCategoryEntryController : XUiController
    {
        private SpawnMenuCategory category;
        private SpawnMenuUIController mainController;
        private XUiV_Label nameLabel;
        private XUiV_Sprite selectedSprite;

        public override void Init()
        {
            base.Init();
            nameLabel = GetChildById("lblCategoryName")?.viewComponent as XUiV_Label;
            selectedSprite = GetChildById("sprSelected")?.viewComponent as XUiV_Sprite;

            XUiController clickable = GetChildById("clickable") ?? this;
            clickable.OnPress += HandlePress;
        }

        public void SetCategory(SpawnMenuCategory value, SpawnMenuUIController controller, bool isSelected)
        {
            category = value;
            mainController = controller;
            if (nameLabel != null) nameLabel.Text = category.DisplayName;
            if (selectedSprite != null) selectedSprite.IsVisible = isSelected;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            category = null;
            mainController = null;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (category == null || mainController == null) return;

            mainController.SelectCategory(category.Id);
        }
    }

    public class PropSpawnEntryController : XUiController
    {
        private SpawnMenuEntryDefinition definition;
        private SpawnMenuUIController mainController;
        private XUiC_ItemStack iconStack;
        private XUiView clickableView;
        private XUiV_Label entryNameLabel;
        private XUiV_Label entryTypeLabel;

        public override void Init()
        {
            base.Init();
            iconStack = GetChildById("propIcon") as XUiC_ItemStack;
            if (iconStack != null)
            {
                iconStack.SimpleClick = false;
                iconStack.OnPress += HandlePress;
            }

            XUiController clickable = GetChildById("clickable") ?? this;
            clickableView = clickable.viewComponent;
            clickable.OnPress += HandlePress;
            entryNameLabel = GetChildById("lblEntryName")?.viewComponent as XUiV_Label;
            entryTypeLabel = GetChildById("lblEntryType")?.viewComponent as XUiV_Label;
        }

        public void SetEntry(SpawnMenuEntryDefinition entryDefinition, SpawnMenuUIController controller)
        {
            definition = entryDefinition;
            mainController = controller;

            if (clickableView != null) clickableView.ToolTip = definition.DisplayName;
            if (entryNameLabel != null)
            {
                entryNameLabel.Text = definition.DisplayName;
                entryNameLabel.IsVisible = true;
            }

            if (iconStack != null)
            {
                iconStack.IsDragAndDrop = false;
                iconStack.AllowDropping = false;
                ItemStack icon = SpawnCatalog.GetIconStack(definition);
                if (icon != null)
                {
                    iconStack.setItemStack(icon);
                    iconStack.viewComponent.IsVisible = true;
                    iconStack.viewComponent.ToolTip = definition.DisplayName;
                }
                else
                {
                    iconStack.setItemStack(new ItemStack());
                    iconStack.viewComponent.IsVisible = false;
                }
            }

            if (entryTypeLabel != null)
            {
                entryTypeLabel.Text = GetEntryTypeLabel(definition.EntryType);
                entryTypeLabel.IsVisible = definition.EntryType != SpawnMenuEntryType.Prop;
            }

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            definition = null;
            mainController = null;
            if (iconStack != null)
            {
                iconStack.setItemStack(new ItemStack());
                iconStack.viewComponent.IsVisible = false;
            }
            if (entryNameLabel != null) entryNameLabel.IsVisible = false;
            if (entryTypeLabel != null) entryTypeLabel.IsVisible = false;
            viewComponent.IsVisible = false;
        }

        private static string GetEntryTypeLabel(SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity) return "NPC";
            if (entryType == SpawnMenuEntryType.Ragdoll) return "RAGDOLL";
            return "PROP";
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (definition == null || mainController == null) return;

            bool shiftHeld = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) ||
                             UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            if (mouseButton == 0 && shiftHeld)
            {
                mainController.AddToInventory(definition);
                return;
            }

            mainController.Spawn(definition);
        }
    }
}