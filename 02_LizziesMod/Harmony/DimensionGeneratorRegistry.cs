using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LizziesMod
{
    public enum DimensionSaveMode
    {
        Snapshot,
        Generated
    }

    public sealed class DimensionGeneratorDefinition
    {
        public string Id { get; }
        public DimensionSaveMode SaveMode { get; }
        public Func<DimensionDefinition, Vector3, Vector3> GetEntryPosition { get; }
        public Func<Chunk, bool> GenerateChunk { get; }
        public Action ProcessMainThread { get; }
        public Action OnDimensionActivated { get; }
        public Action OnDimensionDeactivated { get; }

        public DimensionGeneratorDefinition(
            string id,
            DimensionSaveMode saveMode,
            Func<DimensionDefinition, Vector3, Vector3> getEntryPosition = null,
            Func<Chunk, bool> generateChunk = null,
            Action processMainThread = null,
            Action onDimensionActivated = null,
            Action onDimensionDeactivated = null)
        {
            Id = id;
            SaveMode = saveMode;
            GetEntryPosition = getEntryPosition;
            GenerateChunk = generateChunk;
            ProcessMainThread = processMainThread;
            OnDimensionActivated = onDimensionActivated;
            OnDimensionDeactivated = onDimensionDeactivated;
        }
    }

    public static class DimensionGeneratorRegistry
    {
        private static readonly Dictionary<string, DimensionGeneratorDefinition> generators =
            new Dictionary<string, DimensionGeneratorDefinition>(StringComparer.OrdinalIgnoreCase);

        static DimensionGeneratorRegistry()
        {
            Register(new DimensionGeneratorDefinition(
                DimensionRegistry.SaveSnapshotGeneratorId,
                DimensionSaveMode.Snapshot));
        }

        public static bool Register(IDimensionGenerator generator)
        {
            if (generator == null)
            {
                Logger.Error("[DimensionGenerators] Rejected a null generator instance.");
                return false;
            }

            IDimensionGeneratorLifecycle lifecycle = generator as IDimensionGeneratorLifecycle;
            return Register(new DimensionGeneratorDefinition(
                generator.Id,
                generator.SaveMode,
                generator.GetEntryPosition,
                generator.Generate,
                generator.HasMainThreadWork ? new Action(generator.ProcessMainThread) : null,
                lifecycle != null ? new Action(lifecycle.OnDimensionActivated) : null,
                lifecycle != null ? new Action(lifecycle.OnDimensionDeactivated) : null));
        }

            public static bool RegisterAndLoadDefinitions(Mod modInstance, IDimensionGenerator generator)
            {
                if (!Register(generator)) return false;

                DimensionRegistry.LoadDefinitions(modInstance);
                return true;
            }

        public static bool Register(DimensionGeneratorDefinition generator)
        {
            if (generator == null || string.IsNullOrEmpty(generator.Id))
            {
                Logger.Error("[DimensionGenerators] Rejected a generator registration without an ID.");
                return false;
            }

            if (generator.SaveMode == DimensionSaveMode.Generated && generator.GenerateChunk == null)
            {
                Logger.Error($"[DimensionGenerators] Generated generator '{generator.Id}' must provide a chunk callback.");
                return false;
            }

            lock (generators)
            {
                if (generators.ContainsKey(generator.Id))
                {
                    Logger.Error($"[DimensionGenerators] Generator '{generator.Id}' is already registered.");
                    return false;
                }

                generators.Add(generator.Id, generator);
            }

            Logger.Info($"[DimensionGenerators] Registered '{generator.Id}' as {generator.SaveMode}.");
            return true;
        }

        public static bool TryGet(string generatorId, out DimensionGeneratorDefinition generator)
        {
            if (string.IsNullOrEmpty(generatorId))
            {
                generator = null;
                return false;
            }

            lock (generators)
            {
                return generators.TryGetValue(generatorId, out generator);
            }
        }

        public static bool IsRegistered(string generatorId)
        {
            DimensionGeneratorDefinition generator;
            return TryGet(generatorId, out generator);
        }

        public static List<DimensionGeneratorDefinition> GetRegisteredGenerators()
        {
            lock (generators)
            {
                List<DimensionGeneratorDefinition> result = new List<DimensionGeneratorDefinition>(generators.Values);
                result.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
                return result;
            }
        }

        public static void ProcessActiveGeneratorMainThread()
        {
            DimensionDefinition dimension;
            if (!DimensionRegistry.TryGet(DimensionManager.ActiveDimensionId, out dimension)) return;

            DimensionGeneratorDefinition generator;
            if (!TryGet(dimension.GeneratorId, out generator) || generator.ProcessMainThread == null) return;

            try
            {
                generator.ProcessMainThread();
            }
            catch (Exception exception)
            {
                Logger.Error($"[DimensionGenerators] '{generator.Id}' main-thread processing failed: {exception}");
            }
        }

        public static void NotifyDimensionActivated(string dimensionId)
        {
            NotifyDimensionLifecycle(dimensionId, true);
        }

        public static void NotifyDimensionDeactivated(string dimensionId)
        {
            NotifyDimensionLifecycle(dimensionId, false);
        }

        private static void NotifyDimensionLifecycle(string dimensionId, bool activated)
        {
            DimensionDefinition dimension;
            if (!DimensionRegistry.TryGet(dimensionId, out dimension)) return;

            DimensionGeneratorDefinition generator;
            if (!TryGet(dimension.GeneratorId, out generator)) return;

            Action callback = activated ? generator.OnDimensionActivated : generator.OnDimensionDeactivated;
            if (callback == null) return;

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                string eventName = activated ? "activation" : "deactivation";
                Logger.Error($"[DimensionGenerators] '{generator.Id}' {eventName} callback failed: {exception}");
            }
        }
    }

    [HarmonyPatch(typeof(ChunkProviderGenerateWorld), "generateTerrain", new Type[] { typeof(World), typeof(Chunk), typeof(GameRandom) })]
    public class ChunkProviderGenerateWorld_DimensionTerrainPatch
    {
        public static bool Prefix(ChunkProviderGenerateWorld __instance, Chunk _chunk)
        {
            if (!DimensionManager.IsProviderBoundToActiveGeneratedDimension(__instance)) return true;

            DimensionDefinition dimension;
            if (!DimensionRegistry.TryGet(DimensionManager.ActiveDimensionId, out dimension)) return true;

            DimensionGeneratorDefinition generator;
            if (!DimensionGeneratorRegistry.TryGet(dimension.GeneratorId, out generator) || generator.GenerateChunk == null)
            {
                return true;
            }

            try
            {
                if (generator.GenerateChunk(_chunk)) return false;

                Logger.Error($"[DimensionGenerators] '{generator.Id}' declined chunk ({_chunk.X}, {_chunk.Z}); using normal terrain.");
            }
            catch (Exception exception)
            {
                Logger.Error($"[DimensionGenerators] '{generator.Id}' failed to generate chunk ({_chunk.X}, {_chunk.Z}): {exception}");
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ChunkProviderGenerateWorld), "Update")]
    public class ChunkProviderGenerateWorld_DimensionUpdatePatch
    {
        public static void Postfix()
        {
            DimensionGeneratorRegistry.ProcessActiveGeneratorMainThread();
        }
    }

    [HarmonyPatch(typeof(ChunkProviderGenerateWorld), "GenerateChunksThread")]
    public class ChunkProviderGenerateWorld_DimensionGenerationGatePatch
    {
        public static bool Prefix(ref bool __state, ref int __result)
        {
            __state = false;
            if (!DimensionManager.TryEnterChunkGeneration())
            {
                __result = 15;
                return false;
            }

            __state = true;
            return true;
        }

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) DimensionManager.ExitChunkGeneration();
            return __exception;
        }
    }
}