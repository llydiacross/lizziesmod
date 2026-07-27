using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LizziesMod
{
    public class XmlValidationRunner : MonoBehaviour
    {
        private static bool hasStarted;

        private XUi xui;

        public static void Start(XUi mainMenuXui)
        {
            if (hasStarted || mainMenuXui == null) return;

            hasStarted = true;
            GameObject validationRunner = new GameObject("LizziesXmlValidationRunner");
            XmlValidationRunner runner = validationRunner.AddComponent<XmlValidationRunner>();
            runner.xui = mainMenuXui;
        }

        private void Start()
        {
            StartCoroutine(ValidateXml());
        }

        private IEnumerator ValidateXml()
        {
            List<string> configNames = GetConfigNames();
            Logger.Info($"[XmlValidation] Validating {configNames.Count} modded XML configuration file(s).");

            foreach (string configName in configNames)
            {
                yield return StartCoroutine(XmlPatcher.LoadAndPatchConfig(configName, ValidatePatchedXml));
            }

            if (ModErrorHandler.HasUnacknowledgedErrors())
            {
                Logger.Warning("[XmlValidation] Mod loading errors were found. Showing the validation report.");
                XUiC_MessageBoxWindowGroup.ShowOk(
                    xui,
                    "MOD XML ERRORS DETECTED",
                    ModErrorHandler.GetErrorReport() + "\n\n[FFCC33]Fix the affected mod files and restart before starting a world.[-]",
                    "",
                    ModErrorHandler.AcknowledgeErrors,
                    false,
                    false,
                    false);
            }
            else
            {
                Logger.Info("[XmlValidation] No mod XML errors found.");
            }

            Destroy(gameObject);
        }

        private static void ValidatePatchedXml(XmlFile xmlFile)
        {
            XmlTokenReplacer.ProcessXml(xmlFile.XmlDoc);
        }

        private static List<string> GetConfigNames()
        {
            HashSet<string> configNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string baseConfigDirectory = GameIO.GetGameDir("Data/Config");

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                string modConfigDirectory = Path.Combine(mod.Path, "Config");
                if (!Directory.Exists(modConfigDirectory)) continue;

                foreach (string filePath in Directory.GetFiles(modConfigDirectory, "*.xml", SearchOption.AllDirectories))
                {
                    string configName = GetConfigName(modConfigDirectory, filePath);
                    string baseConfigPath = Path.Combine(
                        baseConfigDirectory,
                        configName.Replace('/', Path.DirectorySeparatorChar) + ".xml");

                    if (SdFile.Exists(baseConfigPath))
                    {
                        configNames.Add(configName);
                    }
                }
            }

            return new List<string>(configNames);
        }

        private static string GetConfigName(string configDirectory, string filePath)
        {
            string relativePath = filePath.Substring(configDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.ChangeExtension(relativePath, null).Replace('\\', '/');
        }
    }
    }