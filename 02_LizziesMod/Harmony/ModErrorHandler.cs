using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LizziesMod
{
    public static class ModErrorHandler
    {
        public static List<string> ModErrors = new List<string>();
        public static List<string> ModWarnings = new List<string>();
        public static HashSet<string> ProblematicMods = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();
        private static readonly Regex XmlPatchFailurePattern = new Regex(
            @"^XML loader: (?:Loading XML patch file|Patching) '.*' from mod '([^']+)' failed:",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex XmlPatchModPattern = new Regex(
            @"\bfrom mod '([^']+)'",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex XmlConfigPathPattern = new Regex(
            @"(?:^|[\\/])Mods[\\/]([^\\/]+)[\\/]Config[\\/]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static int acknowledgedErrorCount;
        internal static bool bypassStartGameWarning;

        public static void AddError(string error)
        {
            if (string.IsNullOrEmpty(error)) return;

            lock (_lock)
            {
                if (!ModErrors.Contains(error))
                {
                    ModErrors.Add(error);
                }
            }
        }

        public static void AddWarning(string warning)
        {
            if (string.IsNullOrEmpty(warning)) return;

            lock (_lock)
            {
                if (!ModWarnings.Contains(warning))
                {
                    ModWarnings.Add(warning);
                }
            }
        }

        public static void ReportXmlError(string modName, string condition)
        {
            string sourceLabel = string.IsNullOrEmpty(modName) ? "[Unknown Mod]" : "[" + modName + "]";
            AddError($"[FF3333][XML ERROR][-] {sourceLabel}\n{condition}");

            if (!string.IsNullOrEmpty(modName))
            {
                lock (_lock)
                {
                    ProblematicMods.Add(modName);
                }
            }
        }

        public static void ReportXmlWarning(string modName, string condition)
        {
            string sourceLabel = string.IsNullOrEmpty(modName) ? "[Unknown Mod]" : "[" + modName + "]";
            AddWarning($"[FFCC33][XML WARNING][-] {sourceLabel}\n{condition}");
        }

            public static bool HasUnacknowledgedErrors()
            {
                lock (_lock)
                {
                    return ModErrors.Count > acknowledgedErrorCount;
                }
            }

            public static bool HasErrors()
            {
                lock (_lock)
                {
                    return ModErrors.Count > 0;
                }
            }

            public static bool HasDiagnostics()
            {
                lock (_lock)
                {
                    return ModErrors.Count > 0 || ModWarnings.Count > 0;
                }
            }

            public static void GetDiagnosticCounts(out int errorCount, out int warningCount)
            {
                lock (_lock)
                {
                    errorCount = ModErrors.Count;
                    warningCount = ModWarnings.Count;
                }
            }

            public static bool HasProblematicMods()
            {
                lock (_lock)
                {
                    return ProblematicMods.Count > 0;
                }
            }

            public static string GetErrorReport()
            {
                return GetDiagnosticReport();
            }

            public static string GetDiagnosticReport()
            {
                lock (_lock)
                {
                    List<string> sections = new List<string>();
                    if (ModErrors.Count > 0)
                    {
                        sections.Add("[FF6666]XML ERRORS[-]\n\n" + string.Join("\n\n", ModErrors));
                    }

                    if (ModWarnings.Count > 0)
                    {
                        sections.Add("[FFCC33]XML WARNINGS[-]\n\n" + string.Join("\n\n", ModWarnings));
                    }

                    string diagnosticText = sections.Count == 0
                        ? "No mod XML diagnostics were detected."
                        : string.Join("\n\n", sections);
                    return diagnosticText.Length > 2500 ? diagnosticText.Substring(0, 2497) + "..." : diagnosticText;
                }
            }

            public static List<string> GetProblematicMods()
            {
                lock (_lock)
                {
                    return new List<string>(ProblematicMods);
                }
            }

            public static void AcknowledgeErrors()
            {
                lock (_lock)
                {
                    acknowledgedErrorCount = ModErrors.Count;
                }
            }

            public static void ClearErrors()
            {
                lock (_lock)
                {
                    ModErrors.Clear();
                    ModWarnings.Clear();
                    ProblematicMods.Clear();
                    acknowledgedErrorCount = 0;
                }
            }

            public static void ProceedWithGameStart(GameManager gameManager)
            {
                if (gameManager == null) return;

                bypassStartGameWarning = true;
                ClearErrors();

                bool isOffline = SingletonMonoBehaviour<ConnectionManager>.Instance.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
                gameManager.StartGame(isOffline);
            }

            public static bool DisableProblematicMods()
            {
                bool disabledAny = false;
                foreach (string modName in GetProblematicMods())
                {
                    if (ModPatcher.IsModEnabled(modName))
                    {
                        Logger.Info($"[ModErrorHandler] Auto-disabling problematic mod: {modName}");
                        ModSettingsManager.SetSetting(modName, "Enabled", false, true);
                        ModSettingsManager.SaveModSettings(modName);
                        disabledAny = true;
                    }
                }

                if (disabledAny)
                {
                    ModSettingsManager.PendingRestart = true;
                }

                ClearErrors();
                SingletonMonoBehaviour<ConnectionManager>.Instance.Disconnect();
                return disabledAny;
            }

        public static void LogCallback(string condition, string stackTrace, LogType type)
        {
                if (string.IsNullOrEmpty(condition)) return;
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Warning) return;

                string diagnosticText = string.IsNullOrEmpty(stackTrace)
                    ? condition
                    : condition + "\n" + stackTrace;
                if (!IsXmlDiagnostic(diagnosticText)) return;

                Match patchFailure = XmlPatchFailurePattern.Match(condition);
                string modName = patchFailure.Success
                    ? patchFailure.Groups[1].Value
                    : FindSourceMod(diagnosticText);
                if (type == LogType.Warning)
                {
                    ReportXmlWarning(modName, condition);
                    return;
                }

                ReportXmlError(modName, condition);
        }

        private static bool IsXmlDiagnostic(string diagnosticText)
        {
            if (string.IsNullOrEmpty(diagnosticText)) return false;

            return diagnosticText.IndexOf("XML loader:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                diagnosticText.IndexOf("XML.Patch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (diagnosticText.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (diagnosticText.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    diagnosticText.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    diagnosticText.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    diagnosticText.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string FindSourceMod(string diagnosticText)
        {
            Match modMatch = XmlPatchModPattern.Match(diagnosticText);
            if (modMatch.Success) return modMatch.Groups[1].Value;

            Match configPathMatch = XmlConfigPathPattern.Match(diagnosticText);
            if (configPathMatch.Success) return configPathMatch.Groups[1].Value;

            return "";
        }
    }

    [HarmonyPatch(typeof(GameManager), "StartGame")]
    public class ModError_StartGame_Patch
    {
        public static bool Prefix(GameManager __instance)
        {
            if (ModErrorHandler.bypassStartGameWarning)
            {
                ModErrorHandler.bypassStartGameWarning = false;
                return true;
            }

            if (ModErrorHandler.HasErrors())
            {
                ModErrorWindowUIController.ShowGameStartErrors(__instance);
                return false;
            }

            return true;
        }
    }
}