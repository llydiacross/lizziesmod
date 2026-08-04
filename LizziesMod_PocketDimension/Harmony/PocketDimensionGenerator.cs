using LizziesMod;
using UnityEngine;

namespace LizziesMod.PocketDimension
{
    public sealed class PocketDimensionGenerator : GeneratedDimensionGeneratorBase
    {
        public const string GeneratorId = "pocket-dimension";

        private const string SettingsModName = "LizziesMod_PocketDimension";
        private const int DefaultFloorY = 65;
        private const int MinimumFloorY = 16;
        private const int MaximumFloorY = 240;
        private const int WorldHeight = 256;
        private static readonly string[] RequiredBlockNames =
        {
            "terrainFiller",
            "concreteMaster",
            "pocketDimensionPortal"
        };

        private int generatedChunkLogCount;
        private int configuredFloorY;

        public override string Id { get { return GeneratorId; } }

        private int FloorY { get { EnsureInitialized(); return configuredFloorY; } }

        protected override void Initialize()
        {
            configuredFloorY = GetClampedSetting(
                SettingsModName,
                "FloorY",
                DefaultFloorY,
                MinimumFloorY,
                MaximumFloorY);
            Logger.Info($"[PocketDimension] Floor Y={configuredFloorY}.");
        }

        protected override Vector3 GetEntryPositionCore(DimensionDefinition definition, Vector3 defaultPosition)
        {
            return new Vector3(8.5f, FloorY + 1f, 8.5f);
        }

        protected override bool GenerateChunk(Chunk chunk)
        {
            if (!TryResolveRequiredBlocks(RequiredBlockNames)) return false;

            BlockValue foundationBlock = GetRequiredBlock("terrainFiller");
            BlockValue floorBlock = GetRequiredBlock("concreteMaster");
            BlockValue portalBlock = GetRequiredBlock("pocketDimensionPortal");
            int floorY = FloorY;
            int foundationTopY = floorY - 1;

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
                    SetTerrainHeights(chunk, localX, localZ, foundationTopY);
                    FillTerrainColumn(chunk, localX, localZ, foundationTopY, foundationBlock);
                    chunk.SetBlockRaw(localX, floorY, localZ, floorBlock);
                    chunk.SetDensity(localX, floorY, localZ, MarchingCubes.DensityAir);
                    ClearAirColumn(chunk, localX, localZ, floorY + 1, WorldHeight);

                    if (worldX == 0 && worldZ == 0)
                    {
                        chunk.SetBlockRaw(localX, floorY + 1, localZ, portalBlock);
                        chunk.SetDensity(localX, floorY + 1, localZ, MarchingCubes.DensityAir);
                    }
                }
            }

            chunk.ResetStabilityToBottomMost();
            SetStability(chunk, floorY);
            FinalizeGeneratedChunk(chunk);
            return true;
        }

        private static void SetStability(Chunk chunk, int floorY)
        {
            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    SetColumnStability(chunk, localX, localZ, 0, floorY);

                    if (worldX == 0 && worldZ == 0)
                    {
                        chunk.SetStability(localX, floorY + 1, localZ, 15);
                    }
                }
            }
        }
    }
}