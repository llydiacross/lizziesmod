using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace LizziesMod
{
    public enum SpawnMenuTab
    {
        Props,
        Ragdolls,
        Entities
    }

    public enum SpawnMenuEntryType
    {
        Prop,
        Ragdoll,
        Entity
    }

    public class SpawnMenuCategory
    {
        public string Id;
        public string DisplayName;
        public int Order;
    }

    public class SpawnMenuEntryDefinition
    {
        public string Id;
        public SpawnMenuEntryType EntryType;
        public string CategoryId;
        public string DisplayName;
        public string Tags;
        public string Thumbnail;
    }

    public class SpawnablePropDefinition : SpawnMenuEntryDefinition
    {
        public string BlockName;
        public float Mass;

        public BlockValue GetBlockValue()
        {
            return Block.GetBlockValue(BlockName, false);
        }

        public ItemStack GetIconStack()
        {
            return new ItemStack(GetBlockValue().ToItemValue(), 1);
        }
    }

    public class SpawnableEntityDefinition : SpawnMenuEntryDefinition
    {
        public int EntityClassId;
        public string EntityClassName;
        public bool IsEnemy;
        public bool IsAnimal;
        public bool SupportsRagdoll;
    }

    internal class SpawnMenuEntityOverride
    {
        public string DisplayName;
        public string CategoryId;
        public string RagdollCategoryId;
        public string Tags;
        public string Thumbnail;
    }

    public static class SpawnCatalog
    {
        private const string DefaultEntryIconItem = "resourceWood";
        private static readonly Dictionary<string, SpawnablePropDefinition> propsById =
            new Dictionary<string, SpawnablePropDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SpawnMenuEntryDefinition> entriesById =
            new Dictionary<string, SpawnMenuEntryDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SpawnablePropDefinition> props = new List<SpawnablePropDefinition>();
        private static readonly List<SpawnableEntityDefinition> entities = new List<SpawnableEntityDefinition>();
        private static readonly List<SpawnableEntityDefinition> ragdolls = new List<SpawnableEntityDefinition>();
        private static readonly Dictionary<SpawnMenuTab, Dictionary<string, SpawnMenuCategory>> categoriesByTab =
            new Dictionary<SpawnMenuTab, Dictionary<string, SpawnMenuCategory>>();
        private static readonly Dictionary<SpawnMenuTab, List<SpawnMenuCategory>> categoriesByTabList =
            new Dictionary<SpawnMenuTab, List<SpawnMenuCategory>>();
        private static readonly Dictionary<string, SpawnMenuEntityOverride> entityOverridesByClassName =
            new Dictionary<string, SpawnMenuEntityOverride>(StringComparer.OrdinalIgnoreCase);

        private static bool isLoaded;

        public static List<SpawnMenuCategory> GetCategories(SpawnMenuTab tab)
        {
            EnsureLoaded();
            return new List<SpawnMenuCategory>(GetCategoryList(tab));
        }

        public static void EnsureLoaded()
        {
            if (isLoaded) return;

            isLoaded = true;
            propsById.Clear();
            entriesById.Clear();
            props.Clear();
            entities.Clear();
            ragdolls.Clear();
            categoriesByTab.Clear();
            categoriesByTabList.Clear();
            entityOverridesByClassName.Clear();

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                string catalogPath = Path.Combine(mod.Path, "Config", "SpawnableProps.xml");
                if (File.Exists(catalogPath)) LoadPropCatalog(catalogPath, mod.Name);

                string spawnMenuPath = Path.Combine(mod.Path, "Config", "SpawnMenu.xml");
                if (File.Exists(spawnMenuPath)) LoadSpawnMenuMetadata(spawnMenuPath, mod.Name);
            }

            LoadMenuEntities();

            foreach (List<SpawnMenuCategory> categories in categoriesByTabList.Values)
            {
                categories.Sort((left, right) =>
                {
                    int orderComparison = left.Order.CompareTo(right.Order);
                    return orderComparison != 0
                        ? orderComparison
                        : string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                });
            }

            props.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            entities.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            ragdolls.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            Logger.Info($"[SpawnMenu] Loaded {props.Count} prop(s), {entities.Count} entity(ies), and {ragdolls.Count} ragdoll(s).");

            if (ModSettingsManager.GetSetting<bool>(SpawnMenuManager.ModName, "GenerateCandidateCatalog", false))
            {
                SpawnMenuManager.ExportBaseGameCandidates();
            }
        }

        public static bool TryGetProp(string propId, out SpawnablePropDefinition definition)
        {
            EnsureLoaded();
            return propsById.TryGetValue(propId ?? "", out definition);
        }

        public static bool TryGetEntry(string entryId, out SpawnMenuEntryDefinition definition)
        {
            EnsureLoaded();
            return entriesById.TryGetValue(entryId ?? "", out definition);
        }

        public static ItemStack GetIconStack(SpawnMenuEntryDefinition definition)
        {
            SpawnablePropDefinition propDefinition = definition as SpawnablePropDefinition;
            if (propDefinition != null) return propDefinition.GetIconStack();

            if (definition != null && !string.IsNullOrEmpty(definition.Thumbnail))
            {
                Block iconBlock = Block.GetBlockByName(definition.Thumbnail, false);
                if (iconBlock != null)
                {
                    return new ItemStack(Block.GetBlockValue(definition.Thumbnail, false).ToItemValue(), 1);
                }

                ItemClass iconItem = ItemClass.GetItemClass(definition.Thumbnail, false);
                if (iconItem != null) return new ItemStack(new ItemValue(iconItem.Id), 1);
            }

            ItemClass fallbackItem = ItemClass.GetItemClass(DefaultEntryIconItem, false);
            return fallbackItem != null ? new ItemStack(new ItemValue(fallbackItem.Id), 1) : null;
        }

        public static List<SpawnablePropDefinition> GetProps(string categoryId, string searchText)
        {
            List<SpawnablePropDefinition> result = new List<SpawnablePropDefinition>();
            foreach (SpawnMenuEntryDefinition entry in GetEntries(SpawnMenuTab.Props, categoryId, searchText))
            {
                SpawnablePropDefinition definition = entry as SpawnablePropDefinition;
                if (definition != null) result.Add(definition);
            }

            return result;
        }

        public static List<SpawnMenuEntryDefinition> GetEntries(SpawnMenuTab tab, string categoryId, string searchText)
        {
            EnsureLoaded();

            string category = categoryId ?? "all";
            string search = (searchText ?? "").Trim();
            List<SpawnMenuEntryDefinition> source = GetEntriesForTab(tab);
            List<SpawnMenuEntryDefinition> result = new List<SpawnMenuEntryDefinition>();

            foreach (SpawnMenuEntryDefinition definition in source)
            {
                if (!category.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !definition.CategoryId.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(search) &&
                    definition.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    definition.Tags.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    definition.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                result.Add(definition);
            }

            return result;
        }

        private static List<SpawnMenuEntryDefinition> GetEntriesForTab(SpawnMenuTab tab)
        {
            List<SpawnMenuEntryDefinition> result = new List<SpawnMenuEntryDefinition>();
            if (tab == SpawnMenuTab.Props)
            {
                foreach (SpawnablePropDefinition prop in props) result.Add(prop);
            }
            else if (tab == SpawnMenuTab.Entities)
            {
                foreach (SpawnableEntityDefinition entity in entities) result.Add(entity);
            }
            else if (tab == SpawnMenuTab.Ragdolls)
            {
                foreach (SpawnableEntityDefinition ragdoll in ragdolls) result.Add(ragdoll);
            }

            return result;
        }

        private static void LoadPropCatalog(string catalogPath, string modName)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(catalogPath);

                foreach (XmlNode categoryNode in document.SelectNodes("/SpawnableProps/Categories/Category"))
                {
                    AddCategory(categoryNode, SpawnMenuTab.Props);
                }

                foreach (XmlNode propNode in document.SelectNodes("/SpawnableProps/Props/Prop"))
                {
                    AddProp(propNode, modName);
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[SpawnMenu] Failed to load '{catalogPath}': {exception.Message}");
            }
        }

        private static void LoadSpawnMenuMetadata(string spawnMenuPath, string modName)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(spawnMenuPath);

                foreach (XmlNode categoryNode in document.SelectNodes("/SpawnMenu/Categories/Category"))
                {
                    SpawnMenuTab tab;
                    if (!TryParseTab(categoryNode.Attributes?["type"]?.Value, out tab))
                    {
                        Logger.Warning($"[SpawnMenu] Ignoring category in '{spawnMenuPath}': type must be props, ragdolls, or entities.");
                        continue;
                    }

                    AddCategory(categoryNode, tab);
                }

                foreach (XmlNode entityNode in document.SelectNodes("/SpawnMenu/EntityOverrides/Entity | /SpawnMenu/Entities/Entity"))
                {
                    AddEntityOverride(entityNode, spawnMenuPath, modName);
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[SpawnMenu] Failed to load '{spawnMenuPath}': {exception.Message}");
            }
        }

        private static void AddCategory(XmlNode categoryNode, SpawnMenuTab tab)
        {
            string id = categoryNode.Attributes?["id"]?.Value;
            if (string.IsNullOrEmpty(id)) return;

            int.TryParse(categoryNode.Attributes?["order"]?.Value, out int order);
            EnsureCategory(tab, id, categoryNode.Attributes?["name"]?.Value ?? id, order);
        }

        private static void EnsureCategory(SpawnMenuTab tab, string id, string displayName, int order)
        {
            Dictionary<string, SpawnMenuCategory> categoriesById = GetCategoryMap(tab);
            if (categoriesById.ContainsKey(id)) return;

            SpawnMenuCategory category = new SpawnMenuCategory
            {
                Id = id,
                DisplayName = displayName,
                Order = order
            };

            categoriesById.Add(category.Id, category);
            GetCategoryList(tab).Add(category);
        }

        private static void AddProp(XmlNode propNode, string modName)
        {
            string id = propNode.Attributes?["id"]?.Value;
            string blockName = propNode.Attributes?["block"]?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(blockName) || propsById.ContainsKey(id)) return;

            if (Block.GetBlockByName(blockName, false) == null)
            {
                Logger.Warning($"[SpawnMenu] Ignoring '{id}' from '{modName}': block '{blockName}' was not found.");
                return;
            }

            string categoryId = propNode.Attributes?["category"]?.Value ?? "other";
            EnsureCategory(SpawnMenuTab.Props, categoryId, categoryId, int.MaxValue);

            float mass = 10f;
            string massText = propNode.Attributes?["mass"]?.Value;
            if (!string.IsNullOrEmpty(massText))
            {
                float.TryParse(massText, NumberStyles.Float, CultureInfo.InvariantCulture, out mass);
            }

            SpawnablePropDefinition definition = new SpawnablePropDefinition
            {
                Id = id,
                EntryType = SpawnMenuEntryType.Prop,
                BlockName = blockName,
                CategoryId = categoryId,
                DisplayName = propNode.Attributes?["displayName"]?.Value ?? blockName,
                Tags = propNode.Attributes?["tags"]?.Value ?? "",
                Thumbnail = propNode.Attributes?["thumbnail"]?.Value ?? "",
                Mass = mass
            };

            propsById.Add(definition.Id, definition);
            props.Add(definition);
            AddEntry(definition, modName);
        }

        private static void AddEntityOverride(XmlNode entityNode, string spawnMenuPath, string modName)
        {
            string className = entityNode.Attributes?["entity_class"]?.Value ?? entityNode.Attributes?["class"]?.Value;
            if (string.IsNullOrEmpty(className))
            {
                Logger.Warning($"[SpawnMenu] Ignoring entity override in '{spawnMenuPath}': entity_class is required.");
                return;
            }

            if (entityOverridesByClassName.ContainsKey(className))
            {
                Logger.Warning($"[SpawnMenu] Ignoring duplicate entity override '{className}' from '{modName}'.");
                return;
            }

            entityOverridesByClassName.Add(className, new SpawnMenuEntityOverride
            {
                DisplayName = entityNode.Attributes?["displayName"]?.Value ?? "",
                CategoryId = entityNode.Attributes?["category"]?.Value ?? "",
                RagdollCategoryId = entityNode.Attributes?["ragdoll_category"]?.Value ?? "",
                Tags = entityNode.Attributes?["tags"]?.Value ?? "",
                Thumbnail = entityNode.Attributes?["thumbnail"]?.Value ?? ""
            });
        }

        private static void LoadMenuEntities()
        {
            if (EntityClass.list == null || EntityClass.list.Dict == null)
            {
                Logger.Warning("[SpawnMenu] Entity classes are not available; no NPC entries were loaded.");
                return;
            }

            foreach (EntityClass entityClass in EntityClass.list.Dict.Values)
            {
                if (entityClass == null || entityClass.userSpawnType != EntityClass.UserSpawnType.Menu) continue;
                if (string.IsNullOrEmpty(entityClass.entityClassName)) continue;

                SpawnMenuEntityOverride metadata;
                entityOverridesByClassName.TryGetValue(entityClass.entityClassName, out metadata);

                string displayName = metadata != null && !string.IsNullOrEmpty(metadata.DisplayName)
                    ? metadata.DisplayName
                    : CreateDisplayName(entityClass.entityClassName);
                string categoryId = metadata != null && !string.IsNullOrEmpty(metadata.CategoryId)
                    ? metadata.CategoryId
                    : GetDefaultEntityCategory(entityClass);
                string tags = metadata != null && !string.IsNullOrEmpty(metadata.Tags)
                    ? metadata.Tags
                    : GetDefaultEntityTags(entityClass);
                string thumbnail = metadata != null ? metadata.Thumbnail : "";

                EnsureCategory(SpawnMenuTab.Entities, categoryId, CreateDisplayName(categoryId), GetDefaultCategoryOrder(categoryId));
                SpawnableEntityDefinition entityDefinition = new SpawnableEntityDefinition
                {
                    Id = "entity:" + entityClass.entityClassName,
                    EntryType = SpawnMenuEntryType.Entity,
                    EntityClassId = EntityClass.GetId(entityClass.entityClassName),
                    EntityClassName = entityClass.entityClassName,
                    CategoryId = categoryId,
                    DisplayName = displayName,
                    Tags = tags,
                    Thumbnail = thumbnail,
                    IsEnemy = entityClass.bIsEnemyEntity,
                    IsAnimal = entityClass.bIsAnimalEntity,
                    SupportsRagdoll = entityClass.HasRagdoll || entityClass.RagdollOnDeathChance > 0f
                };

                if (entityDefinition.EntityClassId < 0)
                {
                    Logger.Warning($"[SpawnMenu] Ignoring '{entityDefinition.EntityClassName}': it has no valid entity class id.");
                    continue;
                }

                entities.Add(entityDefinition);
                AddEntry(entityDefinition, "EntityClass");

                if (!entityDefinition.SupportsRagdoll) continue;

                string ragdollCategoryId = metadata != null && !string.IsNullOrEmpty(metadata.RagdollCategoryId)
                    ? metadata.RagdollCategoryId
                    : categoryId;
                EnsureCategory(SpawnMenuTab.Ragdolls, ragdollCategoryId, CreateDisplayName(ragdollCategoryId), GetDefaultCategoryOrder(ragdollCategoryId));
                SpawnableEntityDefinition ragdollDefinition = new SpawnableEntityDefinition
                {
                    Id = "ragdoll:" + entityClass.entityClassName,
                    EntryType = SpawnMenuEntryType.Ragdoll,
                    EntityClassId = entityDefinition.EntityClassId,
                    EntityClassName = entityDefinition.EntityClassName,
                    CategoryId = ragdollCategoryId,
                    DisplayName = entityDefinition.DisplayName,
                    Tags = entityDefinition.Tags,
                    Thumbnail = entityDefinition.Thumbnail,
                    IsEnemy = entityDefinition.IsEnemy,
                    IsAnimal = entityDefinition.IsAnimal,
                    SupportsRagdoll = true
                };

                ragdolls.Add(ragdollDefinition);
                AddEntry(ragdollDefinition, "EntityClass");
            }
        }

        private static void AddEntry(SpawnMenuEntryDefinition definition, string source)
        {
            if (entriesById.ContainsKey(definition.Id))
            {
                Logger.Warning($"[SpawnMenu] Ignoring duplicate spawn entry '{definition.Id}' from '{source}'.");
                return;
            }

            entriesById.Add(definition.Id, definition);
        }

        private static Dictionary<string, SpawnMenuCategory> GetCategoryMap(SpawnMenuTab tab)
        {
            Dictionary<string, SpawnMenuCategory> result;
            if (!categoriesByTab.TryGetValue(tab, out result))
            {
                result = new Dictionary<string, SpawnMenuCategory>(StringComparer.OrdinalIgnoreCase);
                categoriesByTab.Add(tab, result);
            }

            return result;
        }

        private static List<SpawnMenuCategory> GetCategoryList(SpawnMenuTab tab)
        {
            List<SpawnMenuCategory> result;
            if (!categoriesByTabList.TryGetValue(tab, out result))
            {
                result = new List<SpawnMenuCategory>();
                categoriesByTabList.Add(tab, result);
            }

            return result;
        }

        private static bool TryParseTab(string value, out SpawnMenuTab tab)
        {
            if (string.Equals(value, "props", StringComparison.OrdinalIgnoreCase))
            {
                tab = SpawnMenuTab.Props;
                return true;
            }

            if (string.Equals(value, "ragdolls", StringComparison.OrdinalIgnoreCase))
            {
                tab = SpawnMenuTab.Ragdolls;
                return true;
            }

            if (string.Equals(value, "entities", StringComparison.OrdinalIgnoreCase))
            {
                tab = SpawnMenuTab.Entities;
                return true;
            }

            tab = SpawnMenuTab.Props;
            return false;
        }

        private static string GetDefaultEntityCategory(EntityClass entityClass)
        {
            if (entityClass.bIsAnimalEntity) return "animals";
            if (entityClass.bIsEnemyEntity) return "hostile";
            return "other";
        }

        private static string GetDefaultEntityTags(EntityClass entityClass)
        {
            if (entityClass.bIsAnimalEntity) return "animal";
            if (entityClass.bIsEnemyEntity) return "hostile,zombie";
            return "entity";
        }

        private static int GetDefaultCategoryOrder(string categoryId)
        {
            if (string.Equals(categoryId, "hostile", StringComparison.OrdinalIgnoreCase)) return 10;
            if (string.Equals(categoryId, "animals", StringComparison.OrdinalIgnoreCase)) return 20;
            return 100;
        }

        private static string CreateDisplayName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Unknown";

            StringBuilder builder = new StringBuilder(value.Length + 8);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '_' || current == '-')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ') builder.Append(' ');
                    continue;
                }

                if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1])) builder.Append(' ');
                builder.Append(current);
            }

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Trim());
        }
    }
}