using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LizziesMod
{
    public enum UserXmlDefinitionType
    {
        Item,
        Block,
        Recipe
    }

    public sealed class UserXmlDefinitionSummary
    {
        public UserXmlDefinitionType Type;
        public string Name;
        public bool IsUserDefinition;
    }

    public sealed class UserXmlDefinitionField
    {
        public string Name;
        public string Value;
    }

    public sealed class UserXmlDefinitionDetails
    {
        public UserXmlDefinitionType Type;
        public string Name;
        public bool IsUserDefinition;
        public string BaseDefinition;
        public string RecipeOutputCount;
        public readonly List<UserXmlDefinitionField> Fields = new List<UserXmlDefinitionField>();
    }

    internal sealed class UserXmlDefinitionRecord
    {
        public UserXmlDefinitionType Type;
        public string Name;
        public string Xml;
    }

    public static class UserXmlContentManager
    {
        public const string GeneratedNamePrefix = "lizziesUser_";
        private const string DefinitionsFileName = "UserXmlDefinitions.xml";
        private const string RootElementName = "UserXmlDefinitions";
        public const int MaximumEditableFields = 24;
        private static readonly object Sync = new object();
        private static readonly Regex DefinitionNamePattern = new Regex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly UserXmlDefinitionType[] SupportedTypes =
        {
            UserXmlDefinitionType.Item,
            UserXmlDefinitionType.Block,
            UserXmlDefinitionType.Recipe
        };

        private static readonly Dictionary<UserXmlDefinitionType, List<UserXmlDefinitionRecord>> UserDefinitions =
            new Dictionary<UserXmlDefinitionType, List<UserXmlDefinitionRecord>>();
        private static readonly Dictionary<UserXmlDefinitionType, List<UserXmlDefinitionSummary>> CapturedDefinitions =
            new Dictionary<UserXmlDefinitionType, List<UserXmlDefinitionSummary>>();
        private static readonly Dictionary<UserXmlDefinitionType, Dictionary<string, XElement>> CapturedDefinitionElements =
            new Dictionary<UserXmlDefinitionType, Dictionary<string, XElement>>();

        private static string coreModPath = "";
        private static bool isLoaded;

        static UserXmlContentManager()
        {
            foreach (UserXmlDefinitionType type in SupportedTypes)
            {
                UserDefinitions[type] = new List<UserXmlDefinitionRecord>();
                CapturedDefinitions[type] = new List<UserXmlDefinitionSummary>();
                CapturedDefinitionElements[type] = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static string DefinitionsPath
        {
            get
            {
                lock (Sync)
                {
                    return GetDefinitionsPath();
                }
            }
        }

        public static void Initialize(Mod coreMod)
        {
            lock (Sync)
            {
                coreModPath = coreMod != null ? coreMod.Path ?? "" : "";
                isLoaded = false;
                EnsureLoaded();
            }
        }

        public static List<UserXmlDefinitionSummary> GetDefinitions(UserXmlDefinitionType type)
        {
            lock (Sync)
            {
                EnsureLoaded();
                EnsureDefinitionCache(type);
                return CapturedDefinitions[type]
                    .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(definition => new UserXmlDefinitionSummary
                    {
                        Type = definition.Type,
                        Name = definition.Name,
                        IsUserDefinition = definition.IsUserDefinition
                    })
                    .ToList();
            }
        }

        public static UserXmlDefinitionDetails GetDefinitionDetails(UserXmlDefinitionType type, string definitionName)
        {
            lock (Sync)
            {
                EnsureLoaded();
                EnsureDefinitionCache(type);

                definitionName = (definitionName ?? "").Trim();
                if (string.IsNullOrEmpty(definitionName)) return null;

                UserXmlDefinitionRecord userDefinition = UserDefinitions[type].FirstOrDefault(record =>
                    record.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase));
                if (userDefinition != null)
                {
                    try
                    {
                        return CreateDefinitionDetails(type, XElement.Parse(userDefinition.Xml), true);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning("[UserXml] Could not parse generated " + GetDisplayName(type).ToLowerInvariant() +
                            " '" + definitionName + "' for editing: " + exception.Message);
                        return null;
                    }
                }

                XElement capturedDefinition;
                if (!CapturedDefinitionElements[type].TryGetValue(definitionName, out capturedDefinition)) return null;
                return CreateDefinitionDetails(type, new XElement(capturedDefinition), false);
            }
        }

        public static bool TryCreateDefinition(
            UserXmlDefinitionType type,
            string definitionName,
            string baseOrIngredient,
            string countText,
            out string message)
        {
            return TryCreateDefinition(type, definitionName, baseOrIngredient, countText, null, out message);
        }

        public static bool TryCreateDefinition(
            UserXmlDefinitionType type,
            string definitionName,
            string baseOrIngredient,
            string countText,
            IEnumerable<UserXmlDefinitionField> fields,
            out string message)
        {
            lock (Sync)
            {
                EnsureLoaded();
                EnsureDefinitionCache(type);

                definitionName = (definitionName ?? "").Trim();
                if (!TryNormalizeGeneratedName(definitionName, out definitionName))
                {
                    message = "Names must begin with a letter, use only letters, numbers, or underscores, and include text after '" + GeneratedNamePrefix + "'.";
                    return false;
                }

                if (CapturedDefinitions[type].Any(summary => summary.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase)))
                {
                    message = "A " + GetDisplayName(type).ToLowerInvariant() + " named '" + definitionName + "' already exists.";
                    return false;
                }

                XElement definition;
                if (!TryBuildDefinition(type, definitionName, baseOrIngredient, countText, fields, out definition, out message)) return false;

                UserDefinitions[type].Add(new UserXmlDefinitionRecord
                {
                    Type = type,
                    Name = definitionName,
                    Xml = definition.ToString(SaveOptions.DisableFormatting)
                });

                try
                {
                    SaveDefinitions();
                }
                catch (Exception exception)
                {
                    UserDefinitions[type].RemoveAll(definitionRecord =>
                        definitionRecord.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase));
                    message = "Could not save generated content: " + exception.Message;
                    Logger.Error("[UserXml] " + message);
                    return false;
                }

                CapturedDefinitions[type].Add(new UserXmlDefinitionSummary
                {
                    Type = type,
                    Name = definitionName,
                    IsUserDefinition = true
                });
                message = GetDisplayName(type) + " '" + definitionName + "' was saved. Restart the client before using it.";
                Logger.Info("[UserXml] " + message);
                return true;
            }
        }

        public static bool TryUpdateDefinition(
            UserXmlDefinitionType type,
            string definitionName,
            string baseDefinition,
            string countText,
            IEnumerable<UserXmlDefinitionField> fields,
            out string message)
        {
            lock (Sync)
            {
                EnsureLoaded();
                UserXmlDefinitionRecord definition = UserDefinitions[type].FirstOrDefault(record =>
                    record.Name.Equals(definitionName ?? "", StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    message = "Only generated definitions can be updated here.";
                    return false;
                }

                XElement updatedDefinition;
                if (!TryBuildDefinition(type, definition.Name, baseDefinition, countText, fields, out updatedDefinition, out message)) return false;

                string originalXml = definition.Xml;
                definition.Xml = updatedDefinition.ToString(SaveOptions.DisableFormatting);
                try
                {
                    SaveDefinitions();
                }
                catch (Exception exception)
                {
                    definition.Xml = originalXml;
                    message = "Could not save generated content: " + exception.Message;
                    Logger.Error("[UserXml] " + message);
                    return false;
                }

                message = GetDisplayName(type) + " '" + definition.Name + "' was updated. Restart the client before using it.";
                Logger.Info("[UserXml] " + message);
                return true;
            }
        }

        public static bool TryDeleteDefinition(UserXmlDefinitionType type, string definitionName, out string message)
        {
            lock (Sync)
            {
                EnsureLoaded();
                UserXmlDefinitionRecord definition = UserDefinitions[type].FirstOrDefault(record =>
                    record.Name.Equals(definitionName ?? "", StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    message = "Only generated definitions can be removed here.";
                    return false;
                }

                UserDefinitions[type].Remove(definition);
                try
                {
                    SaveDefinitions();
                }
                catch (Exception exception)
                {
                    UserDefinitions[type].Add(definition);
                    message = "Could not save generated content: " + exception.Message;
                    Logger.Error("[UserXml] " + message);
                    return false;
                }

                CapturedDefinitions[type].RemoveAll(summary =>
                    summary.IsUserDefinition && summary.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));
                CapturedDefinitionElements[type].Remove(definition.Name);
                message = GetDisplayName(type) + " '" + definition.Name + "' was removed. Restart the client before testing the change.";
                Logger.Info("[UserXml] " + message);
                return true;
            }
        }

        public static void InjectDefinitions(XmlFile xmlFile)
        {
            if (xmlFile == null || xmlFile.XmlDoc == null || xmlFile.XmlDoc.Root == null) return;

            UserXmlDefinitionType type;
            if (!TryGetDefinitionType(xmlFile.XmlDoc.Root.Name.LocalName, out type)) return;

            lock (Sync)
            {
                EnsureLoaded();
                XElement root = xmlFile.XmlDoc.Root;
                string elementName = GetElementName(type);
                foreach (UserXmlDefinitionRecord definition in UserDefinitions[type])
                {
                    bool collidesWithExistingDefinition = root.Elements(elementName).Any(element =>
                        string.Equals((string)element.Attribute("name"), definition.Name, StringComparison.OrdinalIgnoreCase));
                    if (collidesWithExistingDefinition)
                    {
                        Logger.Warning("[UserXml] Skipped generated " + GetDisplayName(type).ToLowerInvariant() +
                            " '" + definition.Name + "' because the active XML already defines that name.");
                        continue;
                    }

                    try
                    {
                        root.Add(XElement.Parse(definition.Xml));
                    }
                    catch (Exception exception)
                    {
                        Logger.Error("[UserXml] Could not inject generated " + GetDisplayName(type).ToLowerInvariant() +
                            " '" + definition.Name + "': " + exception.Message);
                    }
                }
            }
        }

        public static void CaptureDefinitions(XmlFile xmlFile)
        {
            if (xmlFile == null || xmlFile.XmlDoc == null || xmlFile.XmlDoc.Root == null) return;

            UserXmlDefinitionType type;
            if (!TryGetDefinitionType(xmlFile.XmlDoc.Root.Name.LocalName, out type)) return;

            lock (Sync)
            {
                EnsureLoaded();
                CaptureDefinitions(type, xmlFile.XmlDoc.Root);
            }
        }

        private static void EnsureLoaded()
        {
            if (isLoaded) return;

            isLoaded = true;
            foreach (UserXmlDefinitionType type in SupportedTypes)
            {
                UserDefinitions[type].Clear();
                CapturedDefinitions[type].Clear();
                CapturedDefinitionElements[type].Clear();
            }

            string definitionsPath = GetDefinitionsPath();
            if (string.IsNullOrEmpty(definitionsPath) || !File.Exists(definitionsPath))
            {
                Logger.Info("[UserXml] No generated content file found. New definitions will be stored in '" + DefinitionsFileName + "'.");
                return;
            }

            try
            {
                XmlReaderSettings readerSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit
                };

                XDocument document;
                using (XmlReader reader = XmlReader.Create(definitionsPath, readerSettings))
                {
                    document = XDocument.Load(reader);
                }

                if (document.Root == null || !document.Root.Name.LocalName.Equals(RootElementName, StringComparison.Ordinal))
                {
                    Logger.Error("[UserXml] '" + definitionsPath + "' must use a '" + RootElementName + "' root element.");
                    return;
                }

                int loadedCount = 0;
                foreach (UserXmlDefinitionType type in SupportedTypes)
                {
                    XElement container = document.Root.Element(GetContainerName(type));
                    if (container == null) continue;

                    string elementName = GetElementName(type);
                    foreach (XElement element in container.Elements(elementName))
                    {
                        string name = ((string)element.Attribute("name") ?? "").Trim();
                        if (!IsGeneratedDefinitionName(name))
                        {
                            Logger.Warning("[UserXml] Ignored generated " + GetDisplayName(type).ToLowerInvariant() +
                                " with an invalid or unnamespaced name in '" + DefinitionsFileName + "'.");
                            continue;
                        }

                        if (UserDefinitions[type].Any(record => record.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            Logger.Warning("[UserXml] Ignored duplicate generated " + GetDisplayName(type).ToLowerInvariant() + " '" + name + "'.");
                            continue;
                        }

                        UserDefinitions[type].Add(new UserXmlDefinitionRecord
                        {
                            Type = type,
                            Name = name,
                            Xml = element.ToString(SaveOptions.DisableFormatting)
                        });
                        loadedCount++;
                    }
                }

                Logger.Info("[UserXml] Loaded " + loadedCount + " generated definition(s) from '" + definitionsPath + "'.");
            }
            catch (Exception exception)
            {
                Logger.Error("[UserXml] Failed to load '" + definitionsPath + "': " + exception.Message);
            }
        }

        private static void EnsureDefinitionCache(UserXmlDefinitionType type)
        {
            if (CapturedDefinitions[type].Count > 0) return;

            try
            {
                string configPath = GameIO.GetGameDir("Data/Config/" + GetConfigFileName(type));
                if (!File.Exists(configPath)) return;

                XDocument document = XDocument.Load(configPath);
                if (document.Root != null)
                {
                    CaptureDefinitions(type, document.Root);
                }
            }
            catch (Exception exception)
            {
                Logger.Warning("[UserXml] Could not read the fallback " + GetConfigFileName(type) + " catalog: " + exception.Message);
            }
        }

        private static void CaptureDefinitions(UserXmlDefinitionType type, XElement root)
        {
            HashSet<string> generatedNames = new HashSet<string>(
                UserDefinitions[type].Select(definition => definition.Name),
                StringComparer.OrdinalIgnoreCase);
            List<UserXmlDefinitionSummary> summaries = new List<UserXmlDefinitionSummary>();
            HashSet<string> capturedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, XElement> capturedElements = CapturedDefinitionElements[type];
            capturedElements.Clear();
            string elementName = GetElementName(type);

            foreach (XElement element in root.Elements(elementName))
            {
                string name = ((string)element.Attribute("name") ?? "").Trim();
                if (string.IsNullOrEmpty(name) || !capturedNames.Add(name)) continue;

                capturedElements[name] = new XElement(element);

                summaries.Add(new UserXmlDefinitionSummary
                {
                    Type = type,
                    Name = name,
                    IsUserDefinition = generatedNames.Contains(name)
                });
            }

            CapturedDefinitions[type] = summaries;
        }

        private static bool TryBuildDefinition(
            UserXmlDefinitionType type,
            string definitionName,
            string baseOrIngredient,
            string countText,
            IEnumerable<UserXmlDefinitionField> fields,
            out XElement definition,
            out string message)
        {
            definition = null;
            message = "";
            baseOrIngredient = (baseOrIngredient ?? "").Trim();

            List<UserXmlDefinitionField> normalizedFields;
            if (!TryNormalizeDefinitionFields(type, fields, out normalizedFields, out message)) return false;

            int count = 1;
            if (type == UserXmlDefinitionType.Recipe)
            {
                if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1 || count > 9999)
                {
                    message = "Recipe output count must be a whole number between 1 and 9999.";
                    return false;
                }

                EnsureDefinitionCache(UserXmlDefinitionType.Item);
                foreach (UserXmlDefinitionField ingredient in normalizedFields)
                {
                    ingredient.Name = ResolveReferenceName(UserXmlDefinitionType.Item, ingredient.Name);
                    if (!CapturedDefinitions[UserXmlDefinitionType.Item].Any(summary =>
                        summary.Name.Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        message = "No loaded item named '" + ingredient.Name + "' was found.";
                        return false;
                    }
                }

                if (!CapturedDefinitions[UserXmlDefinitionType.Item].Any(summary =>
                    summary.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase)))
                {
                    message = "Create the generated item '" + definitionName + "' before adding its recipe.";
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(baseOrIngredient) || !DefinitionNamePattern.IsMatch(baseOrIngredient))
                {
                    message = "Base definitions must use only letters, numbers, or underscores.";
                    return false;
                }

                EnsureDefinitionCache(type);
                baseOrIngredient = ResolveReferenceName(type, baseOrIngredient);
                if (!CapturedDefinitions[type].Any(summary => summary.Name.Equals(baseOrIngredient, StringComparison.OrdinalIgnoreCase)))
                {
                    message = "No loaded " + GetDisplayName(type).ToLowerInvariant() + " named '" + baseOrIngredient + "' was found.";
                    return false;
                }
            }

            definition = BuildDefinition(type, definitionName, baseOrIngredient, count, normalizedFields);
            return true;
        }

        private static bool TryNormalizeDefinitionFields(
            UserXmlDefinitionType type,
            IEnumerable<UserXmlDefinitionField> fields,
            out List<UserXmlDefinitionField> normalizedFields,
            out string message)
        {
            normalizedFields = new List<UserXmlDefinitionField>();
            message = "";
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UserXmlDefinitionField field in fields ?? Enumerable.Empty<UserXmlDefinitionField>())
            {
                string fieldName = (field?.Name ?? "").Trim();
                string fieldValue = (field?.Value ?? "").Trim();
                if (string.IsNullOrEmpty(fieldName) && string.IsNullOrEmpty(fieldValue)) continue;

                if (string.IsNullOrEmpty(fieldName) || !DefinitionNamePattern.IsMatch(fieldName))
                {
                    message = type == UserXmlDefinitionType.Recipe
                        ? "Ingredient names must use only letters, numbers, or underscores."
                        : "Property names must use only letters, numbers, or underscores.";
                    return false;
                }

                if (type != UserXmlDefinitionType.Recipe && fieldName.Equals("Extends", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Set Extends through the base " + GetDisplayName(type).ToLowerInvariant() + " field.";
                    return false;
                }

                if (!usedNames.Add(fieldName))
                {
                    message = "Each " + (type == UserXmlDefinitionType.Recipe ? "ingredient" : "property") + " can appear only once.";
                    return false;
                }

                if (type == UserXmlDefinitionType.Recipe)
                {
                    int ingredientCount;
                    if (!int.TryParse(fieldValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out ingredientCount) || ingredientCount < 1 || ingredientCount > 9999)
                    {
                        message = "Ingredient counts must be whole numbers between 1 and 9999.";
                        return false;
                    }

                    fieldValue = ingredientCount.ToString(CultureInfo.InvariantCulture);
                }

                normalizedFields.Add(new UserXmlDefinitionField { Name = fieldName, Value = fieldValue });
                if (normalizedFields.Count > MaximumEditableFields)
                {
                    message = "A definition can have at most " + MaximumEditableFields + " editable " +
                        (type == UserXmlDefinitionType.Recipe ? "ingredients." : "properties.");
                    return false;
                }
            }

            if (type == UserXmlDefinitionType.Recipe && normalizedFields.Count == 0)
            {
                message = "Recipes need at least one ingredient.";
                return false;
            }

            return true;
        }

        private static UserXmlDefinitionDetails CreateDefinitionDetails(UserXmlDefinitionType type, XElement definition, bool isUserDefinition)
        {
            UserXmlDefinitionDetails details = new UserXmlDefinitionDetails
            {
                Type = type,
                Name = ((string)definition.Attribute("name") ?? "").Trim(),
                IsUserDefinition = isUserDefinition,
                RecipeOutputCount = ((string)definition.Attribute("count") ?? "1").Trim()
            };

            if (type == UserXmlDefinitionType.Recipe)
            {
                foreach (XElement ingredient in definition.Elements("ingredient"))
                {
                    string ingredientName = ((string)ingredient.Attribute("name") ?? "").Trim();
                    if (string.IsNullOrEmpty(ingredientName)) continue;

                    details.Fields.Add(new UserXmlDefinitionField
                    {
                        Name = ingredientName,
                        Value = ((string)ingredient.Attribute("count") ?? "1").Trim()
                    });
                }

                return details;
            }

            foreach (XElement property in definition.Elements("property"))
            {
                string propertyName = ((string)property.Attribute("name") ?? "").Trim();
                if (string.IsNullOrEmpty(propertyName)) continue;

                string propertyValue = ((string)property.Attribute("value") ?? "").Trim();
                if (propertyName.Equals("Extends", StringComparison.OrdinalIgnoreCase))
                {
                    details.BaseDefinition = propertyValue;
                    continue;
                }

                details.Fields.Add(new UserXmlDefinitionField { Name = propertyName, Value = propertyValue });
            }

            return details;
        }

        private static XElement BuildDefinition(
            UserXmlDefinitionType type,
            string definitionName,
            string baseOrIngredient,
            int count,
            IEnumerable<UserXmlDefinitionField> fields)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item:
                    XElement item = new XElement("item",
                        new XAttribute("name", definitionName),
                        new XElement("property", new XAttribute("name", "Extends"), new XAttribute("value", baseOrIngredient)));
                    foreach (UserXmlDefinitionField property in fields)
                    {
                        item.Add(new XElement("property", new XAttribute("name", property.Name), new XAttribute("value", property.Value)));
                    }

                    return item;
                case UserXmlDefinitionType.Block:
                    XElement block = new XElement("block",
                        new XAttribute("name", definitionName),
                        new XElement("property", new XAttribute("name", "Extends"), new XAttribute("value", baseOrIngredient)));
                    foreach (UserXmlDefinitionField property in fields)
                    {
                        block.Add(new XElement("property", new XAttribute("name", property.Name), new XAttribute("value", property.Value)));
                    }

                    return block;
                default:
                    XElement recipe = new XElement("recipe",
                        new XAttribute("name", definitionName),
                        new XAttribute("count", count.ToString(CultureInfo.InvariantCulture)));
                    foreach (UserXmlDefinitionField ingredient in fields)
                    {
                        recipe.Add(new XElement("ingredient", new XAttribute("name", ingredient.Name), new XAttribute("count", ingredient.Value)));
                    }

                    return recipe;
            }
        }

        private static void SaveDefinitions()
        {
            string definitionsPath = GetDefinitionsPath();
            if (string.IsNullOrEmpty(definitionsPath)) throw new InvalidOperationException("The LizziesMod folder could not be resolved.");

            Directory.CreateDirectory(Path.GetDirectoryName(definitionsPath));
            XElement root = new XElement(RootElementName, new XAttribute("version", "1"));
            foreach (UserXmlDefinitionType type in SupportedTypes)
            {
                XElement container = new XElement(GetContainerName(type));
                foreach (UserXmlDefinitionRecord definition in UserDefinitions[type].OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase))
                {
                    container.Add(XElement.Parse(definition.Xml));
                }

                root.Add(container);
            }

            string temporaryPath = definitionsPath + ".tmp";
            XDocument document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            document.Save(temporaryPath);
            File.Copy(temporaryPath, definitionsPath, true);
            File.Delete(temporaryPath);
        }

        private static string GetDefinitionsPath()
        {
            return string.IsNullOrEmpty(coreModPath)
                ? ""
                : Path.Combine(coreModPath, DefinitionsFileName);
        }

        private static bool TryNormalizeGeneratedName(string name, out string normalizedName)
        {
            normalizedName = (name ?? "").Trim();
            if (!DefinitionNamePattern.IsMatch(normalizedName)) return false;

            if (!normalizedName.StartsWith(GeneratedNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedName = GeneratedNamePrefix + normalizedName;
            }

            return IsGeneratedDefinitionName(normalizedName);
        }

        private static bool IsGeneratedDefinitionName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.Length > GeneratedNamePrefix.Length &&
                   name.StartsWith(GeneratedNamePrefix, StringComparison.OrdinalIgnoreCase) &&
                   DefinitionNamePattern.IsMatch(name);
        }

        private static string ResolveReferenceName(UserXmlDefinitionType referenceType, string referenceName)
        {
            if (CapturedDefinitions[referenceType].Any(summary => summary.Name.Equals(referenceName, StringComparison.OrdinalIgnoreCase)))
            {
                return referenceName;
            }

            string generatedReferenceName;
            if (TryNormalizeGeneratedName(referenceName, out generatedReferenceName) &&
                CapturedDefinitions[referenceType].Any(summary => summary.Name.Equals(generatedReferenceName, StringComparison.OrdinalIgnoreCase)))
            {
                return generatedReferenceName;
            }

            return referenceName;
        }

        private static bool TryGetDefinitionType(string rootName, out UserXmlDefinitionType type)
        {
            if (string.Equals(rootName, "items", StringComparison.OrdinalIgnoreCase))
            {
                type = UserXmlDefinitionType.Item;
                return true;
            }

            if (string.Equals(rootName, "blocks", StringComparison.OrdinalIgnoreCase))
            {
                type = UserXmlDefinitionType.Block;
                return true;
            }

            if (string.Equals(rootName, "recipes", StringComparison.OrdinalIgnoreCase))
            {
                type = UserXmlDefinitionType.Recipe;
                return true;
            }

            type = UserXmlDefinitionType.Item;
            return false;
        }

        private static string GetContainerName(UserXmlDefinitionType type)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item: return "items";
                case UserXmlDefinitionType.Block: return "blocks";
                default: return "recipes";
            }
        }

        private static string GetElementName(UserXmlDefinitionType type)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item: return "item";
                case UserXmlDefinitionType.Block: return "block";
                default: return "recipe";
            }
        }

        private static string GetConfigFileName(UserXmlDefinitionType type)
        {
            return GetContainerName(type) + ".xml";
        }

        private static string GetDisplayName(UserXmlDefinitionType type)
        {
            switch (type)
            {
                case UserXmlDefinitionType.Item: return "Item";
                case UserXmlDefinitionType.Block: return "Block";
                default: return "Recipe";
            }
        }
    }
}