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
        public static HashSet<string> ProblematicMods = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();
        private static readonly Regex XmlPatchFailurePattern = new Regex(
            @"^XML loader: (?:Loading XML patch file|Patching) '.*' from mod '([^']+)' failed:",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

            public static bool HasProblematicMods()
            {
                lock (_lock)
                {
                    return ProblematicMods.Count > 0;
                }
            }

            public static string GetErrorReport()
            {
                lock (_lock)
                {
                    string errorText = "The following mod loading errors were detected:\n\n" + string.Join("\n\n", ModErrors);
                    return errorText.Length > 2500 ? errorText.Substring(0, 2497) + "..." : errorText;
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
                if (type != LogType.Error && type != LogType.Exception) return;
                if (string.IsNullOrEmpty(condition)) return;

                Match patchFailure = XmlPatchFailurePattern.Match(condition);
                if (patchFailure.Success)
            {
                    AddError($"[FF3333][XML ERROR][-]\n{condition}");

                    string modName = patchFailure.Groups[1].Value;
                    if (!string.IsNullOrEmpty(modName))
                {
                        lock (_lock)
                    {
                            ProblematicMods.Add(modName);
                    }
                }

                    return;
                }

                if (condition.StartsWith("XML loader: Loading base XML ", StringComparison.Ordinal) ||
                    condition.StartsWith("XML.Patch (", StringComparison.Ordinal))
                {
                    AddError($"[FF3333][XML ERROR][-]\n{condition}");
            }
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