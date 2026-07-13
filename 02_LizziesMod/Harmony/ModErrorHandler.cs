using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public static class ModErrorHandler
    {
        public static List<string> ModErrors = new List<string>();
        public static HashSet<string> ProblematicMods = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();

        public static void AddError(string error)
        {
            lock (_lock)
            {
                if (!ModErrors.Contains(error))
                {
                    ModErrors.Add(error);
                }
            }
        }

        public static void LogCallback(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                if (condition.Contains("XML patch for mod") ||
                    condition.Contains("Failed loading XML") ||
                    condition.Contains("Patching failed") ||
                    condition.Contains("Exception thrown while patching"))
                {
                    AddError($"[FF3333][XML ERROR][-]\n{condition}");
 
                    string marker = "XML patch for mod ";
                    int idx = condition.IndexOf(marker);
                    if (idx != -1)
                    {
                        int start = idx + marker.Length;
                        int end = condition.IndexOf(' ', start);
                        if (end == -1) end = condition.Length;

                        // Clean up any trailing punctuation
                        string modName = condition.Substring(start, end - start).Trim().Trim(':', ',', '.', '\'');
                        if (!string.IsNullOrEmpty(modName))
                        {
                            lock (_lock)
                            {
                                ProblematicMods.Add(modName);
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), "StartGame")]
    public class ModError_StartGame_Patch
    {
        private static bool bypassErrorWarning = false;

        public static bool Prefix(GameManager __instance)
        {
            if (bypassErrorWarning)
            {
                bypassErrorWarning = false;
                return true;
            }

            if (ModErrorHandler.ModErrors.Count > 0)
            {
                string errorText = "The following errors occurred during game boot:\n\n" + string.Join("\n\n", ModErrorHandler.ModErrors);

                if (errorText.Length > 2500)
                {
                    errorText = errorText.Substring(0, 2497) + "...";
                }

                errorText += "\n\n[FFCC33]Do you still wish to proceed? If you select Cancel, the game will automatically disable the problematic mods.[-]";

                XUiC_MessageBoxWindowGroup.ShowOkCancel(
                    LocalPlayerUI.primaryUI.xui,
                    "MOD LOAD ERRORS DETECTED",
                    errorText,
                    "",
                    () =>
                    {
                        Logger.Info("[ModErrorHandler] User bypassed mod error warning. Proceeding to load game.");
                        bypassErrorWarning = true;
                        ModErrorHandler.ModErrors.Clear();
                        ModErrorHandler.ProblematicMods.Clear();

                        bool isOffline = SingletonMonoBehaviour<ConnectionManager>.Instance.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
                        __instance.StartGame(isOffline);
                    },
                    () =>
                    {
                        Logger.Info("[ModErrorHandler] User aborted game load. Quarantining broken mods.");

                        bool disabledAny = false;
                        foreach (string modName in ModErrorHandler.ProblematicMods)
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

                        ModErrorHandler.ModErrors.Clear();
                        ModErrorHandler.ProblematicMods.Clear();

                        SingletonMonoBehaviour<ConnectionManager>.Instance.Disconnect();
                    }
                );

                return false;
            }

            return true;
        }
    }
}