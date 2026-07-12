using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LizziesMod
{
    public static class ModPatcher
    {
        public static bool ShowDisabledMods = false;

        public static void Initialize(HarmonyLib.Harmony harmony)
        {

            MethodInfo getLoadedMods = typeof(global::ModManager).GetMethod("GetLoadedMods", BindingFlags.Public | BindingFlags.Static);
            MethodInfo getLoadedModsPostfix = typeof(ModPatcher).GetMethod(nameof(GetLoadedMods_Postfix), BindingFlags.Public | BindingFlags.Static);

            if (getLoadedMods != null && getLoadedModsPostfix != null)
            {
                harmony.Patch(getLoadedMods, postfix: new HarmonyMethod(getLoadedModsPostfix));
            }

            Type modApiInterface = typeof(IModApi);
            MethodInfo initModPrefix = typeof(ModPatcher).GetMethod(nameof(InitMod_Prefix), BindingFlags.Public | BindingFlags.Static);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
           
                        if (modApiInterface.IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                        {
                            if (type == typeof(LizziesMod.Main.Init)) continue;

   
                            MethodInfo initModMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m => m.Name == "InitMod" &&
                                                     m.GetParameters().Length == 1 &&
                                                     m.GetParameters()[0].ParameterType == typeof(Mod));

                            if (initModMethod != null)
                            {
                                try
                                {
                                    harmony.Patch(initModMethod, prefix: new HarmonyMethod(initModPrefix));
                                    Logger.Info($"[ModManager] Successfully patched InitMod for {type.Name}");
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warning($"[ModManager] Harmony failed to patch InitMod for {type.Name}. Error: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (ReflectionTypeLoadException) { }
            }
        }

        public static bool InitMod_Prefix([HarmonyArgument(0)] Mod modInstance)
        {
            if (modInstance == null) return true;

            if (!IsModEnabled(modInstance.Name))
            {
                Logger.Info($"[ModManager] Blocked: {modInstance.Name}");
                return false;
            }
            return true;
        }

        public static void GetLoadedMods_Postfix(ref List<Mod> __result)
        {
            if (ShowDisabledMods) return;

            __result = __result.Where(mod => IsModEnabled(mod.Name)).ToList();
        }

        public static bool IsModEnabled(string modName)
        {
            if (modName.Equals("LizziesMod", StringComparison.OrdinalIgnoreCase)) return true;

            if (!ModSettingsManager.AllModSettings.ContainsKey(modName)) return true;

            var enabledSetting = ModSettingsManager.AllModSettings[modName]
                .Find(s => s.Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase));

            if (enabledSetting != null)
            {
                if (bool.TryParse(enabledSetting.Value, out bool isEnabled))
                {
                    return isEnabled;
                }
            }

            return true;
        }
    }
}