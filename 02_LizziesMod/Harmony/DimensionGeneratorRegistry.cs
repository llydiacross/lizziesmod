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

        public DimensionGeneratorDefinition(
            string id,
            DimensionSaveMode saveMode,
            Func<DimensionDefinition, Vector3, Vector3> getEntryPosition = null,
            Func<Chunk, bool> generateChunk = null,
            Action processMainThread = null)
        {
            Id = id;
            SaveMode = saveMode;
            GetEntryPosition = getEntryPosition;
            GenerateChunk = generateChunk;
            ProcessMainThread = processMainThread;
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