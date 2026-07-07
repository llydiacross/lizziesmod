using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HarmonyLib;

namespace LizziesMod
{
    public static class XmlTokenReplacer
    {
        private static readonly Regex tokenRegex = new Regex(@"#([^#.]+)\.([^#]+)#", RegexOptions.Compiled);

        public static void ProcessXml(XDocument xmlDoc)
        {
            if (xmlDoc == null || xmlDoc.Root == null) return;
            ProcessNode(xmlDoc.Root);
        }

        private static void ProcessNode(XNode node)
        {
            if (node == null) return;

            if (node is XElement element)
            {
                if (element.HasAttributes)
                {
                    foreach (XAttribute attr in element.Attributes())
                    {
                        if (attr.Value.Contains("#"))
                        {
                            attr.Value = ReplaceTokens(attr.Value);
                        }
                    }
                }

                foreach (XNode child in element.Nodes())
                {
                    ProcessNode(child);
                }
            }
            else if (node is XText textNode)
            {
                if (textNode.Value.Contains("#"))
                {
                    textNode.Value = ReplaceTokens(textNode.Value);
                }
            }
            else if (node is XCData cdataNode)
            {
                if (cdataNode.Value.Contains("#"))
                {
                    cdataNode.Value = ReplaceTokens(cdataNode.Value);
                }
            }
        }

        private static string ReplaceTokens(string input)
        {
            return tokenRegex.Replace(input, match =>
            {
                string modName = match.Groups[1].Value;
                string settingName = match.Groups[2].Value;

                string value = ModSettingsManager.GetSetting<string>(modName, settingName, null);

                if (value != null)
                {
                    Logger.Info($"[XmlTokenReplacer] Injected token {match.Value} -> {value}");
                    return value;
                }

                Logger.Warning($"[XmlTokenReplacer] Token {match.Value} found, but setting is missing from ModSettings!");
                return match.Value;
            });
        }
    }

  

    [HarmonyPatch]
    public class UniversalXml_Patch
    {

        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] targetClasses = new Type[]
            {
                typeof(BlocksFromXml),
                typeof(ItemClassesFromXml),
                typeof(MaterialsFromXml),
                typeof(ItemModificationsFromXml), 
                typeof(LootFromXml),
                typeof(RecipesFromXml),
                typeof(BuffsFromXml),
                typeof(EntityClassesFromXml),
                typeof(ProgressionFromXml),
                typeof(TradersFromXml),
                typeof(QuestsFromXml),
                typeof(XUiFromXml)
            };

            foreach (Type targetType in targetClasses)
            {
                if (targetType == null) continue;

                MethodInfo method = targetType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        (m.Name.StartsWith("Create") || m.Name.StartsWith("Load") || m.Name.StartsWith("Parse") || m.Name.StartsWith("Init")) &&
                        m.GetParameters().Length >= 1 &&
                        m.GetParameters()[0].ParameterType == typeof(XmlFile));

                if (method != null)
                {
                    yield return method;
                }
                else
                {
                    Logger.Warning($"[XmlTokenReplacer] Failed to find a valid XML loading method for {targetType.Name}!");
                }
            }
        }

        public static void Prefix(XmlFile __0)
        {
            if (__0 != null && __0.XmlDoc != null)
            {
                XmlTokenReplacer.ProcessXml(__0.XmlDoc);
            }
        }
    }
}