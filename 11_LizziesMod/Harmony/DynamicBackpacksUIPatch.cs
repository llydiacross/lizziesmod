using HarmonyLib;
using LizziesMod;
using UnityEngine;

// TODO: Move to its own binary
namespace DynamicBackpacks
{


    [HarmonyPatch(typeof(XUiC_BackpackWindow), "OnOpen")]
    public class BackpackWindow_OnOpen_Patch
    {
        public static void Postfix(XUiC_BackpackWindow __instance)
        {
       
            int maxSize = ModSettingsManager.GetSetting("LizziesMod_DynamicBackpacks", "BackpackMaxSize", 45);
    
            if (maxSize <= 0) maxSize = 45;


            int slotsPerPage = 45;
            int neededPages = Mathf.CeilToInt((float)maxSize / slotsPerPage);

    
            if (neededPages < 1) neededPages = 1;

            XUiController tabsHeader = __instance.GetChildById("tabsHeader");
            XUiController tabButtonsGrid = tabsHeader?.GetChildById("tabButtons");

            if (tabButtonsGrid != null)
            {
                for (int i = 0; i < tabButtonsGrid.Children.Count; i++)
                {
                    XUiController tab = tabButtonsGrid.Children[i];
                    if (tab.viewComponent != null)
                    {
                        tab.viewComponent.IsVisible = (i < neededPages);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(EffectManager), "GetValue")]
    public class EffectManager_GetValue_CarryCapacity_Patch
    {
        public static void Postfix(PassiveEffects _passiveEffect, ref float __result)
        {
            if (_passiveEffect == PassiveEffects.CarryCapacity)
            {
                bool disableEncumbrance = ModSettingsManager.GetSetting("LizziesMod_DynamicBackpacks", "DisableEncumbrance", false);

                if (disableEncumbrance)
                {
                    int maxSize = ModSettingsManager.GetSetting("LizziesMod_DynamicBackpacks", "BackpackMaxSize", 45);
                    if (maxSize <= 0) maxSize = 45;

                    __result = maxSize;
                }
            }
        }
    }
}