using HarmonyLib;
using System.IO;

namespace LizziesMod
{
    [HarmonyPatch(typeof(GameIO), "GetSaveGameRegionDir")]
    public class GameIO_GetSaveGameRegionDir_Patch
    {
        public static void Postfix(ref string __result)
        {

            if (TimeManager.currentYear != 0 || TimeManager.currentDimension != "Overworld")
            {
                string yearStr = TimeManager.GetGameYear().ToString();
                string dimStr = TimeManager.currentDimension;

                __result = __result + "_" + dimStr + "_" + yearStr;

                if (!Directory.Exists(__result))
                {
                    Directory.CreateDirectory(__result);
                    Logger.Info($"[DimensionManager] Timeline/Dimension diverging. Created new region directory: {__result}");
                }
            }
        }
    }
}