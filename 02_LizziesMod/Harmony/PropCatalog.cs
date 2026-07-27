using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace LizziesMod
{
    public class SpawnablePropCategory
    {
        public string Id;
        public string DisplayName;
        public int Order;
    }

    public class SpawnablePropDefinition
    {
        public string Id;
        public string BlockName;
        public string CategoryId;
        public string DisplayName;
        public string Tags;
        public string Thumbnail;
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

    public static class PropCatalog
    {
        private static readonly Dictionary<string, SpawnablePropDefinition> propsById =
            new Dictionary<string, SpawnablePropDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SpawnablePropCategory> categoriesById =
            new Dictionary<string, SpawnablePropCategory>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SpawnablePropCategory> categories = new List<SpawnablePropCategory>();
        private static readonly List<SpawnablePropDefinition> props = new List<SpawnablePropDefinition>();

        private static bool isLoaded;

        public static List<SpawnablePropCategory> Categories
        {
            get
            {
                EnsureLoaded();
                return new List<SpawnablePropCategory>(categories);
            }
        }

        public static void EnsureLoaded()
        {
            if (isLoaded) return;

            isLoaded = true;
            propsById.Clear();
            categoriesById.Clear();
            categories.Clear();
            props.Clear();

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                string catalogPath = Path.Combine(mod.Path, "Config", "SpawnableProps.xml");
                if (!File.Exists(catalogPath)) continue;

                LoadCatalog(catalogPath, mod.Name);
            }

            categories.Sort((left, right) => left.Order.CompareTo(right.Order));
            props.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            Logger.Info($"[PropSpawner] Loaded {props.Count} spawnable prop(s) in {categories.Count} categor(ies).");

            if (ModSettingsManager.GetSetting<bool>(PropSpawnerManager.ModName, "GenerateCandidateCatalog", false))
            {
                PropCatalogLoader.ExportBaseGameCandidates();
            }
        }

        public static bool TryGetProp(string propId, out SpawnablePropDefinition definition)
        {
            EnsureLoaded();
            return propsById.TryGetValue(propId ?? "", out definition);
        }

        public static List<SpawnablePropDefinition> GetProps(string categoryId, string searchText)
        {
            EnsureLoaded();

            string category = categoryId ?? "all";
            string search = (searchText ?? "").Trim();
            List<SpawnablePropDefinition> result = new List<SpawnablePropDefinition>();

            foreach (SpawnablePropDefinition definition in props)
            {
                if (!category.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !definition.CategoryId.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(search) &&
                    definition.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    definition.Tags.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                result.Add(definition);
            }

            return result;
        }

        private static void LoadCatalog(string catalogPath, string modName)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(catalogPath);

                foreach (XmlNode categoryNode in document.SelectNodes("/SpawnableProps/Categories/Category"))
                {
                    AddCategory(categoryNode);
                }

                foreach (XmlNode propNode in document.SelectNodes("/SpawnableProps/Props/Prop"))
                {
                    AddProp(propNode, modName);
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[PropSpawner] Failed to load '{catalogPath}': {exception.Message}");
            }
        }

        private static void AddCategory(XmlNode categoryNode)
        {
            string id = categoryNode.Attributes?["id"]?.Value;
            if (string.IsNullOrEmpty(id) || categoriesById.ContainsKey(id)) return;

            int.TryParse(categoryNode.Attributes?["order"]?.Value, out int order);
            SpawnablePropCategory category = new SpawnablePropCategory
            {
                Id = id,
                DisplayName = categoryNode.Attributes?["name"]?.Value ?? id,
                Order = order
            };

            categoriesById.Add(category.Id, category);
            categories.Add(category);
        }

        private static void AddProp(XmlNode propNode, string modName)
        {
            string id = propNode.Attributes?["id"]?.Value;
            string blockName = propNode.Attributes?["block"]?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(blockName) || propsById.ContainsKey(id)) return;

            if (Block.GetBlockByName(blockName, false) == null)
            {
                Logger.Warning($"[PropSpawner] Ignoring '{id}' from '{modName}': block '{blockName}' was not found.");
                return;
            }

            string categoryId = propNode.Attributes?["category"]?.Value ?? "other";
            if (!categoriesById.ContainsKey(categoryId))
            {
                SpawnablePropCategory category = new SpawnablePropCategory
                {
                    Id = categoryId,
                    DisplayName = categoryId,
                    Order = int.MaxValue
                };
                categoriesById.Add(category.Id, category);
                categories.Add(category);
            }

            float mass = 10f;
            string massText = propNode.Attributes?["mass"]?.Value;
            if (!string.IsNullOrEmpty(massText))
            {
                float.TryParse(massText, NumberStyles.Float, CultureInfo.InvariantCulture, out mass);
            }

            SpawnablePropDefinition definition = new SpawnablePropDefinition
            {
                Id = id,
                BlockName = blockName,
                CategoryId = categoryId,
                DisplayName = propNode.Attributes?["displayName"]?.Value ?? blockName,
                Tags = propNode.Attributes?["tags"]?.Value ?? "",
                Thumbnail = propNode.Attributes?["thumbnail"]?.Value ?? "",
                Mass = mass
            };

            propsById.Add(definition.Id, definition);
            props.Add(definition);
        }
    }
}