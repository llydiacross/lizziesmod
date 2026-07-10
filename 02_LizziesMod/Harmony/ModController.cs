using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LizziesMod
{
    public static class ModController
    {

        public static bool ShowDisabledMods = false;

        public static void Initialize(HarmonyLib.Harmony harmony)
        {

            MethodInfo getLoadedMods = typeof(global::ModManager).GetMethod("GetLoadedMods", BindingFlags.Public | BindingFlags.Static);
            MethodInfo getLoadedModsPostfix = typeof(ModController).GetMethod(nameof(GetLoadedMods_Postfix), BindingFlags.Public | BindingFlags.Static);

            if (getLoadedMods != null && getLoadedModsPostfix != null)
            {
                harmony.Patch(getLoadedMods, postfix: new HarmonyMethod(getLoadedModsPostfix));
            }

            Type modApiInterface = typeof(IModApi);
            MethodInfo initModPrefix = typeof(ModController).GetMethod(nameof(InitMod_Prefix), BindingFlags.Public | BindingFlags.Static);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {

                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (modApiInterface.IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                        {
                            if (type == typeof(LizziesMod.Main.Init)) continue;

                            MethodInfo initModMethod = type.GetMethod("InitMod", BindingFlags.Public | BindingFlags.Instance);
                            if (initModMethod != null)
                            {
                                try
                                {
                                    harmony.Patch(initModMethod, prefix: new HarmonyMethod(initModPrefix));
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warning($"[ModManager] Harmony failed to patch InitMod for {type.Name} in '{assembly.GetName().Name}'. Error: {ex.Message}");
                                }
                            }
                            else
                            {
                                Logger.Warning($"[ModManager] Warning: '{type.Name}' in '{assembly.GetName().Name}' implements IModApi but lacks a standard public InitMod method. This mod might still be enabled");
                            }
                        }
                    }
                }
                catch (ReflectionTypeLoadException) { }
            }
        }

        public static bool InitMod_Prefix(Mod _modInstance)
        {
            if (!IsModEnabled(_modInstance.Name))
            {
                Logger.Info($"[ModManager] Blocked: {_modInstance.Name}");
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