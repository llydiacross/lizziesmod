using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LizziesMod
{
    public static class AddonManager
    {
        public static bool BypassingFilter = false;

        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            Logger.Info("[AddonManager] Initializing Universal Mod Blocker...");

            MethodInfo getLoadedMods = typeof(ModManager).GetMethod("GetLoadedMods", BindingFlags.Public | BindingFlags.Static);
            MethodInfo getLoadedModsPostfix = typeof(AddonManager).GetMethod(nameof(GetLoadedMods_Postfix), BindingFlags.Public | BindingFlags.Static);

            if (getLoadedMods != null && getLoadedModsPostfix != null)
            {
                harmony.Patch(getLoadedMods, postfix: new HarmonyMethod(getLoadedModsPostfix));
            }

            Type modApiInterface = typeof(IModApi);
            MethodInfo initModPrefix = typeof(AddonManager).GetMethod(nameof(InitMod_Prefix), BindingFlags.Public | BindingFlags.Static);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("Unity") || assembly.FullName.StartsWith("mscorlib") || assembly.FullName.StartsWith("Assembly-CSharp"))
                    continue;

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
                                harmony.Patch(initModMethod, prefix: new HarmonyMethod(initModPrefix));
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
                Logger.Info($"[AddonManager] Blocked: {_modInstance.Name}");
                return false;
            }
            return true;
        }

        public static void GetLoadedMods_Postfix(ref List<Mod> __result)
        {
            if (BypassingFilter) return;

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