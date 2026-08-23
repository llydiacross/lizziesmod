using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LizziesMod
{
    [HarmonyPatch(typeof(EntityPlayerLocal), "WorldBoundsUpdate")]
    public static class EntityPlayerLocal_WorldBoundsUpdate_DimensionPatch
    {
        private static readonly MethodInfo FilterMethod = AccessTools.Method(
            typeof(DimensionManager),
            nameof(DimensionManager.FilterWorldBoundsPercent));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int filterCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                if (!Calls(instruction, typeof(World), "InBoundsForPlayersPercent")) continue;

                filterCount++;
                yield return new CodeInstruction(OpCodes.Call, FilterMethod);
            }

            if (filterCount != 2)
            {
                Logger.Warning($"[DimensionManager] Expected 2 player world-bound checks but patched {filterCount}.");
            }
                else
                {
                    Logger.Info("[DimensionManager] Patched 2 player world-bound checks.");
                }
        }

        private static bool Calls(CodeInstruction instruction, Type declaringType, string methodName)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            return method != null && method.DeclaringType == declaringType && method.Name == methodName;
        }
    }

    [HarmonyPatch(typeof(EntityVehicle), "CheckForOutOfWorld")]
    public static class EntityVehicle_CheckForOutOfWorld_DimensionPatch
    {
        private static readonly MethodInfo FilterMethod = AccessTools.Method(
            typeof(DimensionManager),
            nameof(DimensionManager.FilterWorldBoundsAdjustment));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int filterCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                if (!Calls(instruction, typeof(World), "AdjustBoundsForPlayers")) continue;

                filterCount++;
                yield return new CodeInstruction(OpCodes.Call, FilterMethod);
            }

            if (filterCount != 1)
            {
                Logger.Warning($"[DimensionManager] Expected 1 vehicle world-bound check but patched {filterCount}.");
            }
                else
                {
                    Logger.Info("[DimensionManager] Patched the vehicle world-bound check.");
                }
        }

        private static bool Calls(CodeInstruction instruction, Type declaringType, string methodName)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            return method != null && method.DeclaringType == declaringType && method.Name == methodName;
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), "OnUpdateLive")]
    public static class EntityPlayerLocal_OnUpdateLive_DimensionRadiationPatch
    {
        private static readonly MethodInfo FilterMethod = AccessTools.Method(
            typeof(DimensionManager),
            nameof(DimensionManager.FilterBiomeRadiation));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int filterCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;

                if (!Calls(instruction, typeof(IBiomeProvider), "GetRadiationAt")) continue;

                filterCount++;
                yield return new CodeInstruction(OpCodes.Call, FilterMethod);
            }

            if (filterCount != 1)
            {
                Logger.Warning($"[DimensionManager] Expected 1 player biome-radiation check but patched {filterCount}.");
            }
                else
                {
                    Logger.Info("[DimensionManager] Patched the player biome-radiation check.");
                }
        }

        private static bool Calls(CodeInstruction instruction, Type declaringType, string methodName)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            return method != null && method.DeclaringType == declaringType && method.Name == methodName;
        }
    }
}