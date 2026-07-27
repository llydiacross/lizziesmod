using System;
using System.Collections.Generic;

namespace LizziesMod
{
    public class PropSpawnerUIController : XUiController
    {
        public const string WindowName = "windowPropSpawner";

        public static bool IsPropSpawnerOpen { get; private set; }

        private XUiController categoryGrid;
        private XUiController propGrid;
        private XUiC_TextInput searchInput;
        private XUiV_Label statusLabel;
        private string selectedCategoryId = "all";
        private string lastSearchText = "";

        public override void Init()
        {
            base.Init();
            categoryGrid = GetChildById("categoryGrid");
            propGrid = GetChildById("propGrid");
            searchInput = GetChildById("txtSearch") as XUiC_TextInput;
            statusLabel = GetChildById("lblStatus")?.viewComponent as XUiV_Label;

            BindButton("btnUndo", HandleUndo);
            BindButton("btnClearOwned", HandleClearOwned);
            BindButton("btnClose", HandleClose);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            IsPropSpawnerOpen = true;
            PropCatalog.EnsureLoaded();
            selectedCategoryId = "all";

            if (searchInput != null) searchInput.Text = "";
            lastSearchText = "";
            PopulateCategories();
            PopulateProps();
        }

        public override void OnClose()
        {
            base.OnClose();
            IsPropSpawnerOpen = false;
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

        private void PopulateCategories()
        {
            if (categoryGrid == null) return;

            List<SpawnablePropCategory> categories = new List<SpawnablePropCategory>
            {
                new SpawnablePropCategory { Id = "all", DisplayName = "All Props", Order = 0 }
            };
            categories.AddRange(PropCatalog.Categories);

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

            List<SpawnablePropDefinition> props = PropCatalog.GetProps(selectedCategoryId, searchInput?.Text);
            if (statusLabel != null)
            {
                statusLabel.Text = props.Count == 1 ? "1 PROP" : props.Count + " PROPS";
            }

            int index = 0;
            foreach (XUiController child in propGrid.Children)
            {
                PropSpawnEntryController entry = child as PropSpawnEntryController;
                if (entry == null) continue;

                if (index < props.Count)
                {
                    entry.SetProp(props[index], this);
                    index++;
                }
                else
                {
                    entry.Clear();
                }
            }
        }

        public void Spawn(SpawnablePropDefinition definition)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return;

            PropSpawnerManager.RequestSpawn(player, definition.Id);
        }

        private void HandleUndo(XUiController sender, int mouseButton)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player != null) PropSpawnerManager.RequestUndo(player);
        }

        private void HandleClearOwned(XUiController sender, int mouseButton)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player != null) PropSpawnerManager.RequestClearOwned(player);
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
        private SpawnablePropCategory category;
        private PropSpawnerUIController mainController;
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

        public void SetCategory(SpawnablePropCategory value, PropSpawnerUIController controller, bool isSelected)
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
        private SpawnablePropDefinition definition;
        private PropSpawnerUIController mainController;
        private XUiC_ItemStack iconStack;
        private XUiView clickableView;

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
        }

        public void SetProp(SpawnablePropDefinition propDefinition, PropSpawnerUIController controller)
        {
            definition = propDefinition;
            mainController = controller;

            if (clickableView != null) clickableView.ToolTip = definition.DisplayName;

            if (iconStack != null)
            {
                iconStack.IsDragAndDrop = false;
                iconStack.AllowDropping = false;
                iconStack.setItemStack(definition.GetIconStack());
                iconStack.viewComponent.IsVisible = true;
                iconStack.viewComponent.ToolTip = definition.DisplayName;
            }

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            definition = null;
            mainController = null;
            if (iconStack?.viewComponent != null) iconStack.viewComponent.IsVisible = false;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (definition == null || mainController == null) return;

            mainController.Spawn(definition);
        }
    }
}