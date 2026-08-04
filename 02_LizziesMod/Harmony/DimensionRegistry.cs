using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace LizziesMod
{
    public sealed class DimensionDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string GeneratorId { get; }

        public bool IsSupported
        {
            get { return DimensionGeneratorRegistry.IsRegistered(GeneratorId); }
        }

        public DimensionDefinition(string id, string displayName, string generatorId)
        {
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
            GeneratorId = string.IsNullOrEmpty(generatorId) ? DimensionRegistry.SaveSnapshotGeneratorId : generatorId;
        }
    }

    public static class DimensionRegistry
    {
        public const string BuiltInSnapshotDimensionId = "SaveSnapshotTest";
        public const string SaveSnapshotGeneratorId = "save-snapshot";

        private static readonly Dictionary<string, DimensionDefinition> definitions =
            new Dictionary<string, DimensionDefinition>(StringComparer.OrdinalIgnoreCase);
        private static string defaultDimensionId = BuiltInSnapshotDimensionId;
        private static int defaultDimensionPriority;
        private static string defaultDimensionSource = "core fallback";

        static DimensionRegistry()
        {
            AddBuiltInSnapshotDefinition();
        }

        public static string DefaultDimensionId
        {
            get { return defaultDimensionId; }
        }

        public static DimensionDefinition GetDefaultDefinition()
        {
            DimensionDefinition definition;
            if (definitions.TryGetValue(defaultDimensionId, out definition)) return definition;

            AddBuiltInSnapshotDefinition();
            return definitions[BuiltInSnapshotDimensionId];
        }

        public static bool TryGet(string dimensionId, out DimensionDefinition definition)
        {
            return definitions.TryGetValue(dimensionId, out definition);
        }

        public static bool UsesGenerator(string dimensionId, string generatorId)
        {
            DimensionDefinition definition;
            return TryGet(dimensionId, out definition) &&
                definition.GeneratorId.Equals(generatorId, StringComparison.OrdinalIgnoreCase);
        }

        public static void Load(Mod modInstance)
        {
            LoadDefinitions(modInstance);
        }

        public static void LoadDefinitions(Mod modInstance)
        {
            if (modInstance == null)
            {
                Logger.Warning("[DimensionRegistry] Mod path is unavailable; no dimension definitions were loaded.");
                return;
            }

            string configPath = Path.Combine(modInstance.Path, "Config", "Dimensions.xml");
            if (!File.Exists(configPath))
            {
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(configPath);
                XmlElement root = document.DocumentElement;
                if (root == null || !root.Name.Equals("Dimensions", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("[DimensionRegistry] Dimensions.xml must have a Dimensions root element.");
                    return;
                }

                int loadedDefinitionCount = 0;
                string sourceId = GetSourceId(modInstance);
                foreach (XmlNode node in root.ChildNodes)
                {
                    if (!node.Name.Equals("Dimension", StringComparison.OrdinalIgnoreCase)) continue;

                    string id = node.Attributes["id"]?.Value;
                    string displayName = node.Attributes["displayName"]?.Value;
                    string generatorId = node.Attributes["generator"]?.Value;
                    if (!DimensionStorage.IsValidDimensionId(id) || DimensionManager.IsOverworld(id))
                    {
                        Logger.Warning($"[DimensionRegistry] Ignored dimension with invalid ID '{id ?? ""}'.");
                        continue;
                    }

                    if (definitions.ContainsKey(id))
                    {
                        Logger.Warning($"[DimensionRegistry] Ignored duplicate dimension '{id}' from '{sourceId}'.");
                        continue;
                    }

                    definitions[id] = new DimensionDefinition(id, displayName, generatorId);
                    loadedDefinitionCount++;
                }

                string configuredDefault = root.Attributes["default"]?.Value;
                DimensionDefinition configuredDefinition;
                if (!string.IsNullOrEmpty(configuredDefault) && definitions.TryGetValue(configuredDefault, out configuredDefinition))
                {
                    int configuredPriority;
                    if (!int.TryParse(root.Attributes["defaultPriority"]?.Value, out configuredPriority))
                    {
                        configuredPriority = 100;
                    }

                    TrySetDefault(configuredDefinition.Id, configuredPriority, sourceId);
                }
                else if (!string.IsNullOrEmpty(configuredDefault))
                {
                    Logger.Warning($"[DimensionRegistry] Default dimension '{configuredDefault}' from '{sourceId}' is not defined; using '{defaultDimensionId}'.");
                }

                Logger.Info($"[DimensionRegistry] Loaded {loadedDefinitionCount} definition(s) from '{sourceId}'; portal default is '{defaultDimensionId}'.");
            }
            catch (Exception exception)
            {
                Logger.Error($"[DimensionRegistry] Failed to load '{configPath}': {exception.Message}");
            }
        }

        private static void TrySetDefault(string dimensionId, int priority, string sourceId)
        {
            if (priority < defaultDimensionPriority) return;
            if (priority == defaultDimensionPriority &&
                string.Compare(sourceId, defaultDimensionSource, StringComparison.OrdinalIgnoreCase) >= 0) return;

            defaultDimensionId = dimensionId;
            defaultDimensionPriority = priority;
            defaultDimensionSource = sourceId;
            Logger.Info($"[DimensionRegistry] '{sourceId}' selected '{dimensionId}' as portal default at priority {priority}.");
        }

        private static string GetSourceId(Mod modInstance)
        {
            if (!string.IsNullOrEmpty(modInstance.Name)) return modInstance.Name;
            if (!string.IsNullOrEmpty(modInstance.FolderName)) return modInstance.FolderName;
            return modInstance.Path;
        }

        private static void AddBuiltInSnapshotDefinition()
        {
            definitions[BuiltInSnapshotDimensionId] = new DimensionDefinition(
                BuiltInSnapshotDimensionId,
                "Save Snapshot Test",
                SaveSnapshotGeneratorId);
        }
    }
}