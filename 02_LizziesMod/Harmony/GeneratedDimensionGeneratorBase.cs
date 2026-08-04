using System;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public interface IDimensionGenerator
    {
        string Id { get; }
        DimensionSaveMode SaveMode { get; }
        bool HasMainThreadWork { get; }
        Vector3 GetEntryPosition(DimensionDefinition definition, Vector3 defaultPosition);
        bool Generate(Chunk chunk);
        void ProcessMainThread();
    }

    public abstract class GeneratedDimensionGeneratorBase : IDimensionGenerator
    {
        private readonly object initializationLock = new object();
        private readonly object blockResolutionLock = new object();
        private readonly Dictionary<string, BlockValue> resolvedBlocks =
            new Dictionary<string, BlockValue>(StringComparer.OrdinalIgnoreCase);
        private bool initialized;
        private bool missingBlocksLogged;

        public abstract string Id { get; }

        public DimensionSaveMode SaveMode
        {
            get { return DimensionSaveMode.Generated; }
        }

        public virtual bool HasMainThreadWork
        {
            get { return false; }
        }

        public Vector3 GetEntryPosition(DimensionDefinition definition, Vector3 defaultPosition)
        {
            EnsureInitialized();
            return GetEntryPositionCore(definition, defaultPosition);
        }

        public bool Generate(Chunk chunk)
        {
            if (chunk == null)
            {
                Logger.Error($"[DimensionGenerators] '{Id}' was asked to generate a null chunk.");
                return false;
            }

            EnsureInitialized();
            return GenerateChunk(chunk);
        }

        public virtual void ProcessMainThread()
        {
        }

        protected virtual Vector3 GetEntryPositionCore(DimensionDefinition definition, Vector3 defaultPosition)
        {
            return defaultPosition;
        }

        protected abstract void Initialize();

        protected abstract bool GenerateChunk(Chunk chunk);

        protected void EnsureInitialized()
        {
            if (initialized) return;

            lock (initializationLock)
            {
                if (initialized) return;

                Initialize();
                initialized = true;
            }
        }

        protected int GetClampedSetting(string modName, string settingName, int defaultValue, int minimum, int maximum)
        {
            return Clamp(ModSettingsManager.GetSetting<int>(modName, settingName, defaultValue), minimum, maximum);
        }

        protected static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        protected bool TryResolveRequiredBlocks(params string[] blockNames)
        {
            if (blockNames == null || blockNames.Length == 0) return true;

            List<string> missingBlockNames = null;
            lock (blockResolutionLock)
            {
                foreach (string blockName in blockNames)
                {
                    if (string.IsNullOrEmpty(blockName)) continue;

                    BlockValue block;
                    if (resolvedBlocks.TryGetValue(blockName, out block) && block.Block != null) continue;

                    block = Block.GetBlockValue(blockName, false);
                    if (block.Block == null)
                    {
                        if (missingBlockNames == null) missingBlockNames = new List<string>();
                        missingBlockNames.Add(blockName);
                        continue;
                    }

                    resolvedBlocks[blockName] = block;
                }
            }

            if (missingBlockNames == null) return true;

            if (!missingBlocksLogged)
            {
                missingBlocksLogged = true;
                Logger.Error($"[DimensionGenerators] '{Id}' could not resolve required blocks: {string.Join(", ", missingBlockNames.ToArray())}.");
            }

            return false;
        }

        protected BlockValue GetRequiredBlock(string blockName)
        {
            BlockValue block;
            return resolvedBlocks.TryGetValue(blockName, out block) ? block : default(BlockValue);
        }

        protected static void SetTerrainHeights(Chunk chunk, int localX, int localZ, int terrainHeight)
        {
            chunk.SetTerrainHeight(localX, localZ, (byte)terrainHeight);
            chunk.SetHeight(localX, localZ, (byte)terrainHeight);
        }

        protected static void FillTerrainColumn(Chunk chunk, int localX, int localZ, int topY, BlockValue block)
        {
            for (int y = 0; y <= topY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, block);
                chunk.SetDensity(localX, y, localZ, MarchingCubes.DensityTerrain);
            }
        }

        protected static void ClearAirColumn(Chunk chunk, int localX, int localZ, int startY, int worldHeight)
        {
            for (int y = startY; y < worldHeight; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
                chunk.SetDensity(localX, y, localZ, MarchingCubes.DensityAir);
            }
        }

        protected static void SetColumnStability(Chunk chunk, int localX, int localZ, int startY, int endY)
        {
            for (int y = startY; y <= endY; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        protected static void FinalizeGeneratedChunk(Chunk chunk)
        {
            chunk.ResetLights(byte.MaxValue);
            chunk.isModified = true;
            chunk.NeedsDecoration = false;
            chunk.NeedsLightCalculation = true;
            chunk.NeedsRegeneration = true;
        }
    }
}