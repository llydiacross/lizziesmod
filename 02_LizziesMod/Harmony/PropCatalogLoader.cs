using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace LizziesMod
{
    public static class PropCatalogLoader
    {
        public static void ExportBaseGameCandidates()
        {
            try
            {
                Mod propSpawnerMod = global::ModManager.GetLoadedMods().Find(mod =>
                    mod.Name.Equals(PropSpawnerManager.ModName, StringComparison.OrdinalIgnoreCase));
                if (propSpawnerMod == null) return;

                string sourcePath = GameIO.GetGameDir("Data/Config/blocks.xml");
                string outputPath = Path.Combine(propSpawnerMod.Path, "SpawnableProps.Candidates.xml");
                XmlDocument sourceDocument = new XmlDocument();
                sourceDocument.Load(sourcePath);

                List<string> blockNames = FindModelEntityBlocks(sourceDocument);
                using (XmlWriter writer = XmlWriter.Create(outputPath, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("SpawnableProps");
                    writer.WriteStartElement("Categories");
                    writer.WriteStartElement("Category");
                    writer.WriteAttributeString("id", "unreviewed");
                    writer.WriteAttributeString("name", "Unreviewed Candidates");
                    writer.WriteAttributeString("order", "999");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteStartElement("Props");

                    foreach (string blockName in blockNames)
                    {
                        writer.WriteStartElement("Prop");
                        writer.WriteAttributeString("id", "candidate." + blockName);
                        writer.WriteAttributeString("block", blockName);
                        writer.WriteAttributeString("category", "unreviewed");
                        writer.WriteAttributeString("displayName", blockName);
                        writer.WriteAttributeString("tags", "candidate");
                        writer.WriteAttributeString("mass", "10");
                        writer.WriteAttributeString("thumbnail", blockName);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                Logger.Info($"[PropSpawner] Exported {blockNames.Count} model candidates to '{outputPath}'. Review entries before moving them into SpawnableProps.xml.");
            }
            catch (Exception exception)
            {
                Logger.Error($"[PropSpawner] Failed to export model candidates: {exception.Message}");
            }
        }

        private static List<string> FindModelEntityBlocks(XmlDocument sourceDocument)
        {
            List<string> blockNames = new List<string>();
            XmlNodeList blockNodes = sourceDocument.SelectNodes("/blocks/block");
            if (blockNodes == null) return blockNames;

            foreach (XmlNode blockNode in blockNodes)
            {
                string blockName = blockNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(blockName)) continue;

                bool isModelEntity = false;
                bool hasModel = false;
                foreach (XmlNode propertyNode in blockNode.SelectNodes("property"))
                {
                    string propertyName = propertyNode.Attributes?["name"]?.Value;
                    string propertyValue = propertyNode.Attributes?["value"]?.Value;

                    if (propertyName == "Shape" && propertyValue == "ModelEntity") isModelEntity = true;
                    if (propertyName == "Model" && !string.IsNullOrEmpty(propertyValue)) hasModel = true;
                }

                if (isModelEntity && hasModel)
                {
                    blockNames.Add(blockName);
                }
            }

            blockNames.Sort(StringComparer.OrdinalIgnoreCase);
            return blockNames;
        }
    }
}