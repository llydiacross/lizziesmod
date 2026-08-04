using LizziesMod;
using UnityEngine;

namespace LizziesMod.PocketDimension
{
    public static class PocketDimensionGenerator
    {
        public const string GeneratorId = "pocket-dimension";

        private const string SettingsModName = "LizziesMod_PocketDimension";
        private const int DefaultFloorY = 65;
        private const int MinimumFloorY = 16;
        private const int MaximumFloorY = 240;
        private const int WorldHeight = 256;
        private static BlockValue foundationBlock;
        private static BlockValue floorBlock;
        private static BlockValue portalBlock;
        private static int generatedChunkLogCount;
        private static bool layoutInitialized;
        private static int configuredFloorY;

        private static int FloorY { get { EnsureLayout(); return configuredFloorY; } }
        private static int FoundationTopY { get { return FloorY - 1; } }

        public static Vector3 GetEntryPosition(DimensionDefinition definition, Vector3 defaultPosition)
        {
            return new Vector3(8.5f, FloorY + 1f, 8.5f);
        }

        public static bool Generate(Chunk chunk)
        {
            if (!TryResolveBlocks()) return false;

            if (generatedChunkLogCount < 12)
            {
                generatedChunkLogCount++;
                Logger.Info($"[PocketDimension] Generating flat terrain from chunk ({chunk.X}, {chunk.Z}).");
            }

            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    chunk.SetTerrainHeight(localX, localZ, (byte)FoundationTopY);
                    chunk.SetHeight(localX, localZ, (byte)FoundationTopY);

                    for (int y = 0; y < FloorY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, foundationBlock);
                        chunk.SetDensity(localX, y, localZ, MarchingCubes.DensityTerrain);
                    }

                    chunk.SetBlockRaw(localX, FloorY, localZ, floorBlock);
                    chunk.SetDensity(localX, FloorY, localZ, MarchingCubes.DensityAir);

                    for (int y = FloorY + 1; y < WorldHeight; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
                        chunk.SetDensity(localX, y, localZ, MarchingCubes.DensityAir);
                    }

                    if (worldX == 0 && worldZ == 0)
                    {
                        chunk.SetBlockRaw(localX, FloorY + 1, localZ, portalBlock);
                        chunk.SetDensity(localX, FloorY + 1, localZ, MarchingCubes.DensityAir);
                    }
                }
            }

            chunk.ResetStabilityToBottomMost();
            SetStability(chunk);
            chunk.ResetLights(byte.MaxValue);
            chunk.isModified = true;
            chunk.NeedsDecoration = false;
            chunk.NeedsLightCalculation = true;
            chunk.NeedsRegeneration = true;
            return true;
        }

        private static void EnsureLayout()
        {
            if (layoutInitialized) return;

            configuredFloorY = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "FloorY", DefaultFloorY),
                MinimumFloorY,
                MaximumFloorY);
            layoutInitialized = true;
            Logger.Info($"[PocketDimension] Floor Y={configuredFloorY}.");
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            return value > maximum ? maximum : value;
        }

        private static void SetStability(Chunk chunk)
        {
            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    for (int y = 0; y < FloorY; y++)
                    {
                        chunk.SetStability(localX, y, localZ, 15);
                    }

                    chunk.SetStability(localX, FloorY, localZ, 15);

                    if (worldX == 0 && worldZ == 0)
                    {
                        chunk.SetStability(localX, FloorY + 1, localZ, 15);
                    }
                }
            }
        }

        private static bool TryResolveBlocks()
        {
            if (foundationBlock.Block != null && floorBlock.Block != null && portalBlock.Block != null) return true;

            foundationBlock = Block.GetBlockValue("terrainFiller", false);
            floorBlock = Block.GetBlockValue("concreteMaster", false);
            portalBlock = Block.GetBlockValue("pocketDimensionPortal", false);
            if (foundationBlock.Block != null && floorBlock.Block != null && portalBlock.Block != null) return true;

            Logger.Error("[PocketDimension] Could not resolve the foundation, floor, or return portal block.");
            return false;
        }
    }
}