using System;
using System.Collections.Generic;
using HarmonyLib;

namespace LizziesMod
{
    public class XmlEditorUIController : XUiController
    {
        public const string WindowName = "windowXmlEditor";
        private const int DefinitionsPerPage = 50;

        public static string PreviousMenu = "";

        private XUiController definitionGrid;
        private XUiController propertyGrid;
        private XUiV_ScrollView propertyScrollView;
        private XUiC_TextInput searchField;
        private XUiC_TextInput nameField;
        private XUiC_TextInput baseField;
        private XUiC_TextInput recipeCountField;
        private XUiV_Label definitionTypeLabel;
        private XUiV_Label baseLabel;
        private XUiV_Label propertyHeadingLabel;
        private XUiV_Label propertyNameLabel;
        private XUiV_Label propertyValueLabel;
        private XUiV_Label selectedDefinitionLabel;
        private XUiV_Label statusLabel;
        private XUiV_Label pageStatusLabel;
        private XUiController baseDefinitionArea;
        private XUiController recipeCountArea;
        private XUiController createDefinitionButton;
        private XUiController saveDefinitionButton;
        private XUiController deleteDefinitionButton;
        private XUiController addPropertyButton;
        private XUiController previousPageButton;
        private XUiController nextPageButton;
        private XUiV_Sprite itemsSelectedSprite;
        private XUiV_Sprite blocksSelectedSprite;
        private XUiV_Sprite recipesSelectedSprite;
        private readonly List<UserXmlDefinitionSummary> allDefinitions = new List<UserXmlDefinitionSummary>();
        private readonly List<UserXmlDefinitionField> editableFields = new List<UserXmlDefinitionField>();
        private UserXmlDefinitionType selectedType;
        private string selectedDefinitionName = "";
        private bool selectedDefinitionIsUserDefinition;
        private string previousSearchText = "";
        private int currentPageIndex;
        private bool ownsGamePause;

        public override void Init()
        {
            base.Init();

            definitionGrid = GetChildById("definitionGrid");
            propertyGrid = GetChildById("propertyGrid");
            propertyScrollView = GetChildById("propertyScrollView")?.viewComponent as XUiV_ScrollView;
            searchField = GetChildById("txtDefinitionSearch") as XUiC_TextInput;
            nameField = GetChildById("txtDefinitionName") as XUiC_TextInput;
            baseField = GetChildById("txtBaseDefinition") as XUiC_TextInput;
            recipeCountField = GetChildById("txtRecipeCount") as XUiC_TextInput;
            definitionTypeLabel = GetChildById("lblDefinitionType")?.viewComponent as XUiV_Label;
            baseLabel = GetChildById("lblBaseDefinition")?.viewComponent as XUiV_Label;
            propertyHeadingLabel = GetChildById("lblPropertyHeading")?.viewComponent as XUiV_Label;
            propertyNameLabel = GetChildById("lblPropertyName")?.viewComponent as XUiV_Label;
            propertyValueLabel = GetChildById("lblPropertyValue")?.viewComponent as XUiV_Label;
            selectedDefinitionLabel = GetChildById("lblSelectedDefinition")?.viewComponent as XUiV_Label;
            statusLabel = GetChildById("lblEditorStatus")?.viewComponent as XUiV_Label;
            pageStatusLabel = GetChildById("lblPageStatus")?.viewComponent as XUiV_Label;
            baseDefinitionArea = GetChildById("baseDefinitionArea");
            recipeCountArea = GetChildById("recipeCountArea");
            createDefinitionButton = GetChildById("btnCreateDefinition");
            saveDefinitionButton = GetChildById("btnSaveDefinition");
            deleteDefinitionButton = GetChildById("btnDeleteDefinition");
            addPropertyButton = GetChildById("btnAddProperty");
            previousPageButton = GetChildById("btnPrevDefinitions");
            nextPageButton = GetChildById("btnNextDefinitions");
            itemsSelectedSprite = GetChildById("sprItemsSelected")?.viewComponent as XUiV_Sprite;
            blocksSelectedSprite = GetChildById("sprBlocksSelected")?.viewComponent as XUiV_Sprite;
            recipesSelectedSprite = GetChildById("sprRecipesSelected")?.viewComponent as XUiV_Sprite;

            BindButton("btnTypeItems", (sender, mouseButton) => SelectType(UserXmlDefinitionType.Item));
            BindButton("btnTypeBlocks", (sender, mouseButton) => SelectType(UserXmlDefinitionType.Block));
            BindButton("btnTypeRecipes", (sender, mouseButton) => SelectType(UserXmlDefinitionType.Recipe));
            BindButton("btnCreateDefinition", HandleCreateDefinition);
            BindButton("btnSaveDefinition", HandleSaveDefinition);
            BindButton("btnDeleteDefinition", HandleDeleteDefinition);
            BindButton("btnAddProperty", (sender, mouseButton) => AddPropertyField());
            BindButton("btnPrevDefinitions", (sender, mouseButton) => ChangePage(-1));
            BindButton("btnNextDefinitions", (sender, mouseButton) => ChangePage(1));
            BindButton("btnClose", HandleClose);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();
            selectedType = UserXmlDefinitionType.Item;
            selectedDefinitionName = "";
            selectedDefinitionIsUserDefinition = false;
            currentPageIndex = 0;
            if (searchField != null) searchField.Text = "";
            previousSearchText = "";
            ResetDefinitionForm();
            UpdateTypeControls();
            RefreshDefinitions();
            SetStatus("Generated definitions are saved locally and apply after restart.");
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
            if (searchText.Equals(previousSearchText, StringComparison.Ordinal)) return;

            previousSearchText = searchText;
            currentPageIndex = 0;
            PopulateDefinitionList();
        }

        public void SelectDefinition(UserXmlDefinitionSummary definition)
        {
            if (definition == null) return;

            selectedDefinitionName = definition.Name;
            selectedDefinitionIsUserDefinition = definition.IsUserDefinition;
            if (selectedDefinitionLabel != null)
            {
                selectedDefinitionLabel.Text = definition.Name + (definition.IsUserDefinition ? "  [GENERATED]" : "  [LOADED]");
            }

            LoadSelectedDefinitionDetails();
            PopulateDefinitionList();
        }

        private void SelectType(UserXmlDefinitionType type)
        {
            if (selectedType == type && allDefinitions.Count > 0) return;

            selectedType = type;
            currentPageIndex = 0;
            selectedDefinitionName = "";
            selectedDefinitionIsUserDefinition = false;
            ResetDefinitionForm();
            UpdateTypeControls();
            RefreshDefinitions();
        }

        private void RefreshDefinitions()
        {
            allDefinitions.Clear();
            allDefinitions.AddRange(UserXmlContentManager.GetDefinitions(selectedType));
            PopulateDefinitionList();
        }

        private void PopulateDefinitionList()
        {
            List<UserXmlDefinitionSummary> filteredDefinitions = GetFilteredDefinitions();
            int totalPages = Math.Max(1, (filteredDefinitions.Count + DefinitionsPerPage - 1) / DefinitionsPerPage);
            currentPageIndex = Math.Max(0, Math.Min(currentPageIndex, totalPages - 1));
            int startIndex = currentPageIndex * DefinitionsPerPage;
            int endIndex = Math.Min(startIndex + DefinitionsPerPage, filteredDefinitions.Count);

            if (definitionGrid != null)
            {
                int entryIndex = startIndex;
                foreach (XUiController child in definitionGrid.Children)
                {
                    XmlEditorDefinitionEntryController entry = child as XmlEditorDefinitionEntryController;
                    if (entry == null) continue;

                    if (entryIndex < endIndex)
                    {
                        UserXmlDefinitionSummary definition = filteredDefinitions[entryIndex];
                        entry.SetDefinition(definition, this, definition.Name.Equals(selectedDefinitionName, StringComparison.OrdinalIgnoreCase));
                        entryIndex++;
                    }
                    else
                    {
                        entry.Clear();
                    }
                }
            }

            if (pageStatusLabel != null)
            {
                pageStatusLabel.Text = filteredDefinitions.Count == 0
                    ? "NO MATCHING " + GetTypeDisplayName(selectedType).ToUpperInvariant() + "S"
                    : "SHOWING " + (startIndex + 1) + "-" + endIndex + " OF " + filteredDefinitions.Count;
            }

            if (previousPageButton?.viewComponent != null) previousPageButton.viewComponent.IsVisible = currentPageIndex > 0;
            if (nextPageButton?.viewComponent != null) nextPageButton.viewComponent.IsVisible = currentPageIndex < totalPages - 1;
            UpdateDeleteButton();
        }

        private List<UserXmlDefinitionSummary> GetFilteredDefinitions()
        {
            string searchText = GetSearchText();
            if (string.IsNullOrEmpty(searchText)) return new List<UserXmlDefinitionSummary>(allDefinitions);

            List<UserXmlDefinitionSummary> filteredDefinitions = new List<UserXmlDefinitionSummary>();
            foreach (UserXmlDefinitionSummary definition in allDefinitions)
            {
                if (definition.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredDefinitions.Add(definition);
                }
            }

            return filteredDefinitions;
        }

        private void UpdateTypeControls()
        {
            string typeLabel = GetTypeDisplayName(selectedType);
            if (definitionTypeLabel != null) definitionTypeLabel.Text = "CREATE " + typeLabel.ToUpperInvariant();
            if (baseLabel != null)
            {
                baseLabel.Text = selectedType == UserXmlDefinitionType.Recipe ? "INGREDIENT" : "BASE " + typeLabel.ToUpperInvariant();
            }

            if (baseDefinitionArea?.viewComponent != null) baseDefinitionArea.viewComponent.IsVisible = selectedType != UserXmlDefinitionType.Recipe;
            if (recipeCountArea?.viewComponent != null) recipeCountArea.viewComponent.IsVisible = selectedType == UserXmlDefinitionType.Recipe;
            if (propertyHeadingLabel != null) propertyHeadingLabel.Text = selectedType == UserXmlDefinitionType.Recipe ? "INGREDIENTS" : "PROPERTIES";
            if (propertyNameLabel != null) propertyNameLabel.Text = selectedType == UserXmlDefinitionType.Recipe ? "INGREDIENT" : "PROPERTY";
            if (propertyValueLabel != null) propertyValueLabel.Text = selectedType == UserXmlDefinitionType.Recipe ? "COUNT" : "VALUE";
            if (itemsSelectedSprite != null) itemsSelectedSprite.IsVisible = selectedType == UserXmlDefinitionType.Item;
            if (blocksSelectedSprite != null) blocksSelectedSprite.IsVisible = selectedType == UserXmlDefinitionType.Block;
            if (recipesSelectedSprite != null) recipesSelectedSprite.IsVisible = selectedType == UserXmlDefinitionType.Recipe;
            UpdateDefinitionEditControls();
        }

        private void ResetDefinitionForm()
        {
            if (nameField != null) nameField.Text = "";
            if (baseField != null) baseField.Text = GetDefaultReference(selectedType);
            if (recipeCountField != null) recipeCountField.Text = "1";
            if (selectedDefinitionLabel != null) selectedDefinitionLabel.Text = "NEW DEFINITION";
            editableFields.Clear();
            if (selectedType == UserXmlDefinitionType.Recipe)
            {
                editableFields.Add(new UserXmlDefinitionField { Name = "resourceWood", Value = "1" });
            }

            PopulatePropertyGrid();
            UpdateDefinitionEditControls();
        }

        private void HandleCreateDefinition(XUiController sender, int mouseButton)
        {
            string message;
            if (!UserXmlContentManager.TryCreateDefinition(
                selectedType,
                nameField != null ? nameField.Text : "",
                baseField != null ? baseField.Text : "",
                recipeCountField != null ? recipeCountField.Text : "1",
                GetEditedFields(),
                out message))
            {
                SetStatus(message);
                return;
            }

            selectedDefinitionName = "";
            selectedDefinitionIsUserDefinition = false;
            ResetDefinitionForm();
            RefreshDefinitions();
            SetStatus(message);
        }

        private void HandleSaveDefinition(XUiController sender, int mouseButton)
        {
            if (!selectedDefinitionIsUserDefinition || string.IsNullOrEmpty(selectedDefinitionName))
            {
                SetStatus("Select a generated definition to save it.");
                return;
            }

            string enteredName = nameField == null ? "" : (nameField.Text ?? "").Trim();
            if (!enteredName.Equals(selectedDefinitionName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Generated definition names cannot be renamed. Remove and recreate it instead.");
                return;
            }

            string message;
            if (!UserXmlContentManager.TryUpdateDefinition(
                selectedType,
                selectedDefinitionName,
                baseField != null ? baseField.Text : "",
                recipeCountField != null ? recipeCountField.Text : "1",
                GetEditedFields(),
                out message))
            {
                SetStatus(message);
                return;
            }

            RefreshDefinitions();
            LoadSelectedDefinitionDetails();
            SetStatus(message);
        }

        private void HandleDeleteDefinition(XUiController sender, int mouseButton)
        {
            if (!selectedDefinitionIsUserDefinition || string.IsNullOrEmpty(selectedDefinitionName))
            {
                SetStatus("Select a generated definition to remove it.");
                return;
            }

            string message;
            if (!UserXmlContentManager.TryDeleteDefinition(selectedType, selectedDefinitionName, out message))
            {
                SetStatus(message);
                return;
            }

            selectedDefinitionName = "";
            selectedDefinitionIsUserDefinition = false;
            ResetDefinitionForm();
            RefreshDefinitions();
            SetStatus(message);
        }
        public void RemovePropertyField(int fieldIndex)
        {
            SyncEditableFieldsFromGrid();
            if (fieldIndex < 0 || fieldIndex >= editableFields.Count) return;

            editableFields.RemoveAt(fieldIndex);
            PopulatePropertyGrid();
        }

        private void AddPropertyField()
        {
            SyncEditableFieldsFromGrid();
            if (editableFields.Count >= UserXmlContentManager.MaximumEditableFields)
            {
                SetStatus("A definition can have at most " + UserXmlContentManager.MaximumEditableFields + " editable " +
                    (selectedType == UserXmlDefinitionType.Recipe ? "ingredients." : "properties."));
                return;
            }

            editableFields.Add(new UserXmlDefinitionField());
            PopulatePropertyGrid();
        }

        private void ChangePage(int direction)
        {
            currentPageIndex += direction;
            PopulateDefinitionList();
        }

        private void HandleClose(XUiController sender, int mouseButton)
        {
            xui.playerUI.windowManager.Close(WindowName);
        }

        private void UpdateDeleteButton()
        {
            if (deleteDefinitionButton?.viewComponent != null)
            {
                deleteDefinitionButton.viewComponent.IsVisible = selectedDefinitionIsUserDefinition && !string.IsNullOrEmpty(selectedDefinitionName);
            }
        }

        private void UpdateDefinitionEditControls()
        {
            bool hasGeneratedSelection = selectedDefinitionIsUserDefinition && !string.IsNullOrEmpty(selectedDefinitionName);
            if (createDefinitionButton?.viewComponent != null) createDefinitionButton.viewComponent.IsVisible = !hasGeneratedSelection;
            if (saveDefinitionButton?.viewComponent != null) saveDefinitionButton.viewComponent.IsVisible = hasGeneratedSelection;
            if (addPropertyButton?.viewComponent != null) addPropertyButton.viewComponent.IsVisible = true;
            UpdateDeleteButton();
        }

        private void LoadSelectedDefinitionDetails()
        {
            UserXmlDefinitionDetails details = UserXmlContentManager.GetDefinitionDetails(selectedType, selectedDefinitionName);
            if (details == null)
            {
                SetStatus("Could not read this definition's editable fields.");
                return;
            }

            selectedDefinitionIsUserDefinition = details.IsUserDefinition;
            if (nameField != null) nameField.Text = details.Name;
            if (baseField != null) baseField.Text = string.IsNullOrEmpty(details.BaseDefinition) ? GetDefaultReference(selectedType) : details.BaseDefinition;
            if (recipeCountField != null) recipeCountField.Text = string.IsNullOrEmpty(details.RecipeOutputCount) ? "1" : details.RecipeOutputCount;

            editableFields.Clear();
            foreach (UserXmlDefinitionField field in details.Fields)
            {
                if (editableFields.Count >= UserXmlContentManager.MaximumEditableFields) break;
                editableFields.Add(new UserXmlDefinitionField { Name = field.Name, Value = field.Value });
            }

            UpdateTypeControls();
            PopulatePropertyGrid();
            SetStatus(details.IsUserDefinition
                ? "Edit generated fields and save. Changes apply after restart."
                : "Loaded definition fields are a template. Creating saves only a generated definition.");
        }

        private void PopulatePropertyGrid()
        {
            if (propertyGrid == null) return;

            int fieldIndex = 0;
            foreach (XUiController child in propertyGrid.Children)
            {
                XmlEditorPropertyEntryController entry = child as XmlEditorPropertyEntryController;
                if (entry == null) continue;

                if (fieldIndex < editableFields.Count)
                {
                    entry.SetField(editableFields[fieldIndex], this, fieldIndex, true);
                    fieldIndex++;
                }
                else
                {
                    entry.Clear();
                }
            }

            propertyScrollView?.UpdatePosition();
            propertyScrollView?.ResetPosition();
        }

        private List<UserXmlDefinitionField> GetEditedFields()
        {
            if (propertyGrid == null) return CloneFields(editableFields);

            List<UserXmlDefinitionField> fields = new List<UserXmlDefinitionField>();
            foreach (XUiController child in propertyGrid.Children)
            {
                XmlEditorPropertyEntryController entry = child as XmlEditorPropertyEntryController;
                if (entry == null) continue;

                UserXmlDefinitionField field = entry.GetField();
                if (!string.IsNullOrEmpty(field.Name) || !string.IsNullOrEmpty(field.Value))
                {
                    fields.Add(field);
                }
            }

            return fields;
        }

        private void SyncEditableFieldsFromGrid()
        {
            List<UserXmlDefinitionField> fields = GetEditedFields();
            editableFields.Clear();
            editableFields.AddRange(CloneFields(fields));
        }

        private static List<UserXmlDefinitionField> CloneFields(IEnumerable<UserXmlDefinitionField> fields)
        {
            List<UserXmlDefinitionField> clones = new List<UserXmlDefinitionField>();
            foreach (UserXmlDefinitionField field in fields)
            {
                clones.Add(new UserXmlDefinitionField
                {
                    Name = field?.Name ?? "",
                    Value = field?.Value ?? ""
                });
            }

            return clones;
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null) statusLabel.Text = message ?? "";
        }

        private string GetSearchText()
        {
            return searchField == null ? "" : (searchField.Text ?? "").Trim();
        }

        private static string GetDefaultReference(UserXmlDefinitionType type)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item: return "resourceWood";
                case UserXmlDefinitionType.Block: return "terrDirt";
                default: return "resourceWood";
            }
        }

        private static string GetTypeDisplayName(UserXmlDefinitionType type)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item: return "Item";
                case UserXmlDefinitionType.Block: return "Block";
                default: return "Recipe";
            }
        }

        private void BindButton(string buttonId, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(buttonId);
            if (button == null) return;

            XUiController clickable = button.GetChildById("clickable") ?? button;
            clickable.OnPress += handler;
        }
    }

    public class XmlEditorDefinitionEntryController : XUiController
    {
        private UserXmlDefinitionSummary definition;
        private XmlEditorUIController mainController;
        private XUiV_Label nameLabel;
        private XUiV_Label sourceLabel;
        private XUiV_Sprite selectedSprite;

        public override void Init()
        {
            base.Init();
            nameLabel = GetChildById("lblDefinitionName")?.viewComponent as XUiV_Label;
            sourceLabel = GetChildById("lblDefinitionSource")?.viewComponent as XUiV_Label;
            selectedSprite = GetChildById("sprSelected")?.viewComponent as XUiV_Sprite;

            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += HandlePress;
        }

        public void SetDefinition(UserXmlDefinitionSummary newDefinition, XmlEditorUIController controller, bool isSelected)
        {
            definition = newDefinition;
            mainController = controller;
            if (nameLabel != null) nameLabel.Text = newDefinition.Name;
            if (sourceLabel != null) sourceLabel.Text = newDefinition.IsUserDefinition ? "GENERATED" : "LOADED";
            if (selectedSprite != null) selectedSprite.IsVisible = isSelected;
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            definition = null;
            mainController = null;
            if (selectedSprite != null) selectedSprite.IsVisible = false;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController sender, int mouseButton)
        {
            if (definition != null && mainController != null)
            {
                mainController.SelectDefinition(definition);
            }
        }
    }

    public class XmlEditorPropertyEntryController : XUiController
    {
        private XUiC_TextInput nameField;
        private XUiC_TextInput valueField;
        private XUiController removeButton;
        private XmlEditorUIController mainController;
        private int fieldIndex;

        public override void Init()
        {
            base.Init();
            nameField = GetChildById("txtPropertyName") as XUiC_TextInput;
            valueField = GetChildById("txtPropertyValue") as XUiC_TextInput;
            removeButton = GetChildById("btnRemoveProperty");

            XUiController clickable = removeButton?.GetChildById("clickable") ?? removeButton;
            if (clickable != null) clickable.OnPress += HandleRemove;
        }

        public void SetField(UserXmlDefinitionField field, XmlEditorUIController controller, int index, bool canRemove)
        {
            mainController = controller;
            fieldIndex = index;
            if (nameField != null) nameField.Text = field?.Name ?? "";
            if (valueField != null) valueField.Text = field?.Value ?? "";
            if (removeButton?.viewComponent != null) removeButton.viewComponent.IsVisible = canRemove;
            viewComponent.IsVisible = true;
        }

        public UserXmlDefinitionField GetField()
        {
            return new UserXmlDefinitionField
            {
                Name = nameField == null ? "" : nameField.Text ?? "",
                Value = valueField == null ? "" : valueField.Text ?? ""
            };
        }

        public void Clear()
        {
            mainController = null;
            fieldIndex = -1;
            if (nameField != null) nameField.Text = "";
            if (valueField != null) valueField.Text = "";
            viewComponent.IsVisible = false;
        }

        private void HandleRemove(XUiController sender, int mouseButton)
        {
            if (mainController != null) mainController.RemovePropertyField(fieldIndex);
        }
    }

    [HarmonyPatch(typeof(XUiC_EditingTools), "Init")]
    public class XmlEditorEditingToolsPatch
    {
        public static void Postfix(XUiC_EditingTools __instance)
        {
            XUiController button = FindDescendantById(__instance, "xmlEditor")
                ?? FindDescendantById(__instance, "btnXmlEditor");
            if (button == null) return;

            XUiController clickable = FindDescendantById(button, "clickable") ?? button;
            clickable.OnPress += (sender, mouseButton) =>
            {
                XmlEditorUIController.PreviousMenu = "editingTools";
                __instance.xui.playerUI.windowManager.Close("editingTools");
                __instance.xui.playerUI.windowManager.Open(XmlEditorUIController.WindowName, true);
            };
        }

        private static XUiController FindDescendantById(XUiController parent, string id)
        {
            if (parent?.Children == null || string.IsNullOrEmpty(id)) return null;

            foreach (XUiController child in parent.Children)
            {
                if (string.Equals(child?.viewComponent?.ID, id, StringComparison.Ordinal)) return child;

                XUiController descendant = FindDescendantById(child, id);
                if (descendant != null) return descendant;
            }

            return null;
        }
    }
}