using HarmonyLib;
using System;

namespace LizziesMod
{
    [HarmonyPatch(typeof(GameIO), "GetSaveGameDir", new Type[0])]
    public class GameIO_GetSaveGameDir_Patch
    {
        public static void Postfix(ref string __result)
        {

            if (!LizziesMod.ModSettingsManager.GetSetting<bool>("LizziesMod", "ExperimentalFeatures") ||
                DimensionManager.IsOverworld(DimensionManager.ActiveDimensionId))
            {
                return;
            }

            string dimensionSaveDirectory;
            if (DimensionStorage.TryGetDimensionSaveDirectory(
                __result,
                DimensionManager.ActiveDimensionId,
                out dimensionSaveDirectory))
            {
                __result = dimensionSaveDirectory;
                return;
            }

            Logger.Error($"[DimensionManager] No verified save snapshot exists for '{DimensionManager.ActiveDimensionId}'. Keeping the Overworld save directory.");
        }
    }
}