using System;
using System.Collections.Generic;
using LizziesMod;
using UnityEngine;

namespace LizziesMod.Backrooms
{
    public static class BackroomsChunkGenerator
    {
        public const string GeneratorId = "backrooms";
        private const string SettingsModName = "LizziesMod_Backrooms";
        private const int DefaultFloorY = 59;
        private const int DefaultStoreyHeight = 7;
        private const int DefaultPitDepth = 5;
        private const int DefaultBasementCorridorHeight = 4;
        private const int DefaultSplitLevelDepth = 4;

        private const int MacroSize = 32;
        private const int DoorHalfWidth = 1;
        private const int TemplateCount = 8;
        private const int MinimumDoorHeight = 3;
        private const int DoorHeightVariants = 3;
        private const int DoorWest = 1;
        private const int DoorEast = 2;
        private const int DoorNorth = 4;
        private const int DoorSouth = 8;
        private const int ArchitectureNone = 0;
        private const int ArchitectureHalfWall = 1;
        private const int ArchitectureCounter = 2;
        private const int WallPaintId = 176;
        private const int FloorPaintId = 26;
        private const int CeilingPaintId = 106;
        private const int PaintChunksPerUpdate = 1;

        private static BlockValue floorBlock;
        private static BlockValue wallBlock;
        private static BlockValue counterBlock;
        private static BlockValue ceilingLightBlock;
        private static BlockValue officeChairBlock;
        private static BlockValue computerBlock;
        private static BlockValue deskLampBlock;
        private static bool paletteResolved;
        private static bool firstGeneratedChunkLogged;
        private static bool firstPaintedChunkLogged;
        private static bool paintFailureLogged;
        private static bool layoutInitialized;
        private static int configuredFloorY;
        private static int configuredStoreyHeight;
        private static int configuredPitDepth;
        private static int configuredBasementCorridorHeight;
        private static int configuredSplitLevelDepth;
        private static readonly Queue<ChunkPaintRequest> paintQueue = new Queue<ChunkPaintRequest>();
        private static readonly HashSet<long> queuedPaintChunks = new HashSet<long>();

        public static int FloorY { get { EnsureLayout(); return configuredFloorY; } }
        private static int CeilingY { get { return FloorY + StoreyHeight; } }
        private static int StoreyHeight { get { EnsureLayout(); return configuredStoreyHeight; } }
        private static int PitDepth { get { EnsureLayout(); return configuredPitDepth; } }
        private static int SplitLevelDepth { get { EnsureLayout(); return configuredSplitLevelDepth; } }
        private static int PitFloorY { get { return FloorY - PitDepth; } }
        private static int BasementDoorTopY { get { return PitFloorY + MinimumDoorHeight; } }
        private static int BasementCorridorCeilingY { get { return PitFloorY + configuredBasementCorridorHeight + 1; } }
        private static int StoryFloorY { get { return CeilingY; } }
        private static int StoryCeilingY { get { return StoryFloorY + StoreyHeight; } }
        private static int StoryRoofTopY { get { return StoryCeilingY + 1; } }

        private struct ChunkPaintRequest
        {
            public int X;
            public int Z;

            public ChunkPaintRequest(int x, int z)
            {
                X = x;
                Z = z;
            }
        }

        private static void EnsureLayout()
        {
            if (layoutInitialized) return;

            configuredFloorY = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "MainFloorY", DefaultFloorY),
                16,
                200);
            configuredStoreyHeight = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "StoreyHeight", DefaultStoreyHeight),
                MinimumDoorHeight + 1,
                7);
            configuredPitDepth = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "PitDepth", DefaultPitDepth),
                4,
                Math.Min(12, configuredFloorY - 4));
            configuredBasementCorridorHeight = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "BasementCorridorHeight", DefaultBasementCorridorHeight),
                MinimumDoorHeight,
                configuredPitDepth - 1);
            configuredSplitLevelDepth = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "SplitLevelDepth", DefaultSplitLevelDepth),
                1,
                4);
            layoutInitialized = true;

            Logger.Info(
                $"[Backrooms] Layout: floor Y={configuredFloorY}, storey height={configuredStoreyHeight}, " +
                $"pit depth={configuredPitDepth}, basement clearance={configuredBasementCorridorHeight}, " +
                $"split level depth={configuredSplitLevelDepth}.");
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        public static Vector3 GetEntryPosition(DimensionDefinition definition, Vector3 defaultPosition)
        {
            return new Vector3(12.5f, FloorY + 1f, 12.5f);
        }

        public static bool Generate(Chunk chunk)
        {
            if (!TryResolvePalette()) return false;

            if (!firstGeneratedChunkLogged)
            {
                firstGeneratedChunkLogged = true;
                Logger.Info($"[Backrooms] Generating stitched macro-room terrain from chunk ({chunk.X}, {chunk.Z}).");
            }

            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    if (IsPitStoreyTemplate(worldX, worldZ))
                    {
                        GeneratePitStoreyColumn(chunk, localX, localZ, worldX, worldZ);
                        continue;
                    }

                    if (IsTwoStoreyTemplate(worldX, worldZ))
                    {
                        GenerateTwoStoreyColumn(chunk, localX, localZ, worldX, worldZ);
                        continue;
                    }

                    int floorY = GetFloorY(worldX, worldZ);
                    for (int y = 0; y <= floorY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, floorBlock);
                    }

                    for (int y = floorY + 1; y <= FloorY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
                    }

                    if (IsBasementCorridor(worldX, worldZ))
                    {
                        BuildBasementCorridorColumn(chunk, localX, localZ);
                    }

                    bool wall = IsWall(worldX, worldZ);
                    bool doorFrame = wall && IsDoorFrame(worldX, worldZ);
                    int ceilingStartY = GetCeilingStartY(worldX, worldZ);
                    int ceilingTopY = ceilingStartY + 1;
                    int architecture = wall || IsPit(worldX, worldZ) ? ArchitectureNone : GetArchitectureKind(worldX, worldZ);
                    for (int y = ceilingStartY; y <= ceilingTopY; y++)
                    {
                        bool light = y == ceilingStartY && !wall && ShouldPlaceCeilingLight(worldX, worldZ);
                        chunk.SetBlockRaw(localX, y, localZ, light ? ceilingLightBlock : wallBlock);
                    }

                    for (int y = floorY + 1; y < ceilingStartY; y++)
                    {
                        if (wall)
                        {
                            chunk.SetBlockRaw(localX, y, localZ, doorFrame ? counterBlock : wallBlock);
                            continue;
                        }

                        if (y == FloorY + 1)
                        {
                            BlockValue architecturalBlock = GetArchitectureBlock(architecture);
                            if (architecturalBlock.Block != null)
                            {
                                chunk.SetBlockRaw(localX, y, localZ, architecturalBlock);
                                continue;
                            }

                            BlockValue prop = GetPropBlock(worldX, worldZ);
                            if (prop.Block != null)
                            {
                                chunk.SetBlockRaw(localX, y, localZ, prop);
                            }
                        }
                    }
                }
            }

            QueuePaint(chunk.X, chunk.Z);
            chunk.ResetStabilityToBottomMost();
            for (int localX = 0; localX < 16; localX++)
            {
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldX = (chunk.X << 4) + localX;
                    int worldZ = (chunk.Z << 4) + localZ;
                    if (IsPitStoreyTemplate(worldX, worldZ))
                    {
                        SetPitStoreyStability(chunk, localX, localZ);
                        continue;
                    }

                    if (IsTwoStoreyTemplate(worldX, worldZ))
                    {
                        SetTwoStoreyStability(chunk, localX, localZ);
                        continue;
                    }

                    int ceilingStartY = GetCeilingStartY(worldX, worldZ);
                    chunk.SetStability(localX, ceilingStartY, localZ, 15);
                    chunk.SetStability(localX, ceilingStartY + 1, localZ, 15);
                    if (IsBasementCorridor(worldX, worldZ))
                    {
                        SetBasementCorridorStability(chunk, localX, localZ);
                    }
                }
            }

            chunk.ResetLights(byte.MaxValue);
            chunk.isModified = true;
            chunk.NeedsDecoration = false;
            chunk.NeedsLightCalculation = true;
            chunk.NeedsRegeneration = true;
            return true;
        }

        public static void ProcessDeferredPainting()
        {
            GameManager gameManager = GameManager.Instance;
            World world = gameManager != null ? gameManager.World : null;
            ChunkCluster chunkCache = world != null ? world.ChunkCache : null;
            if (chunkCache == null) return;

            for (int processed = 0; processed < PaintChunksPerUpdate; processed++)
            {
                ChunkPaintRequest request;
                long key;
                lock (paintQueue)
                {
                    if (paintQueue.Count == 0) return;

                    request = paintQueue.Dequeue();
                    key = GetChunkKey(request.X, request.Z);
                }

                Chunk chunk = chunkCache.GetChunkSync(request.X, request.Z);
                if (chunk == null)
                {
                    lock (paintQueue)
                    {
                        paintQueue.Enqueue(request);
                    }

                    return;
                }

                if (PaintChunk(gameManager, chunk))
                {
                    lock (paintQueue)
                    {
                        queuedPaintChunks.Remove(key);
                    }
                }
            }
        }

        private static void QueuePaint(int chunkX, int chunkZ)
        {
            long key = GetChunkKey(chunkX, chunkZ);
            lock (paintQueue)
            {
                if (!queuedPaintChunks.Add(key)) return;

                paintQueue.Enqueue(new ChunkPaintRequest(chunkX, chunkZ));
            }
        }

        private static bool PaintChunk(GameManager gameManager, Chunk chunk)
        {
            if (gameManager == null) return false;

            try
            {
                PaintStructuralConcreteFallback(gameManager, chunk);

                for (int localX = 0; localX < 16; localX++)
                {
                    int worldX = (chunk.X << 4) + localX;
                    for (int localZ = 0; localZ < 16; localZ++)
                    {
                        int worldZ = (chunk.Z << 4) + localZ;
                        if (IsPitStoreyTemplate(worldX, worldZ))
                        {
                            PaintPitStoreyColumn(gameManager, worldX, worldZ);
                            continue;
                        }

                        if (IsTwoStoreyTemplate(worldX, worldZ))
                        {
                            PaintTwoStoreyColumn(gameManager, worldX, worldZ);
                            continue;
                        }

                        int floorY = GetFloorY(worldX, worldZ);
                        PaintBlockFace(gameManager, worldX, floorY, worldZ, BlockFace.Top, FloorPaintId);
                        PaintRetainingWalls(gameManager, worldX, worldZ, floorY);
                        if (IsBasementCorridor(worldX, worldZ))
                        {
                            PaintBasementCorridorColumn(gameManager, worldX, worldZ);
                        }
                        else
                        {
                            PaintBasementCorridorBoundary(gameManager, worldX, worldZ);
                        }

                        bool wall = IsWall(worldX, worldZ);
                        bool doorFrame = wall && IsDoorFrame(worldX, worldZ);
                        int ceilingStartY = GetCeilingStartY(worldX, worldZ);
                        int ceilingTopY = ceilingStartY + 1;
                        if (wall && !doorFrame)
                        {
                            for (int y = floorY + 1; y < ceilingStartY; y++)
                            {
                                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                            }
                        }
                        else if (doorFrame)
                        {
                            for (int y = floorY + 1; y < ceilingStartY; y++)
                            {
                                PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                            }
                        }
                        else if (!IsPit(worldX, worldZ) && GetArchitectureKind(worldX, worldZ) != ArchitectureNone)
                        {
                            int architecture = GetArchitectureKind(worldX, worldZ);
                            if (architecture == ArchitectureHalfWall)
                            {
                                PaintBlockAllFaces(gameManager, worldX, floorY + 1, worldZ, WallPaintId);
                            }

                            PaintBlockFace(gameManager, worldX, floorY + 1, worldZ, BlockFace.Top, FloorPaintId);
                        }

                        for (int y = ceilingStartY; y <= ceilingTopY; y++)
                        {
                            bool light = y == ceilingStartY && !wall && ShouldPlaceCeilingLight(worldX, worldZ);
                            if (light) continue;

                            PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                            if (y == ceilingStartY)
                            {
                                PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Bottom, CeilingPaintId);
                            }
                        }
                    }
                }

                chunk.isModified = true;
                chunk.NeedsRegeneration = true;
                if (!firstPaintedChunkLogged)
                {
                    firstPaintedChunkLogged = true;
                    Logger.Info($"[Backrooms] Painted generated chunk ({chunk.X}, {chunk.Z}) after it entered the world cache.");
                }

                return true;
            }
            catch (Exception exception)
            {
                if (!paintFailureLogged)
                {
                    paintFailureLogged = true;
                    Logger.Error($"[Backrooms] Could not paint generated chunk ({chunk.X}, {chunk.Z}): {exception}");
                }

                return false;
            }
        }

        private static void PaintBlockAllFaces(GameManager gameManager, int worldX, int worldY, int worldZ, int paintId)
        {
            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.None, paintId);
        }

        private static void PaintBlockFace(GameManager gameManager, int worldX, int worldY, int worldZ, BlockFace face, int paintId)
        {
            gameManager.SetBlockTextureClient(new BlockValueRef(worldX, worldY, worldZ), face, paintId, 0);
        }

        private static void PaintStructuralConcreteFallback(GameManager gameManager, Chunk chunk)
        {
            World world = gameManager.World;
            if (world == null || floorBlock.Block == null) return;

            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    for (int y = PitFloorY; y <= StoryRoofTopY; y++)
                    {
                        BlockValue block = world.GetBlock(new Vector3i(worldX, y, worldZ));
                        if (block.Block != floorBlock.Block) continue;

                        PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                    }
                }
            }
        }

        private static void PaintDoorFrameFaces(GameManager gameManager, int worldX, int worldY, int worldZ)
        {
            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.North, WallPaintId);
            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.South, WallPaintId);
            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.East, WallPaintId);
            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.West, WallPaintId);
        }

        private static void PaintRetainingWalls(GameManager gameManager, int worldX, int worldZ, int floorY)
        {
            PaintRetainingWall(gameManager, worldX, worldZ, worldX, worldZ - 1, floorY, BlockFace.North);
            PaintRetainingWall(gameManager, worldX, worldZ, worldX + 1, worldZ, floorY, BlockFace.East);
            PaintRetainingWall(gameManager, worldX, worldZ, worldX, worldZ + 1, floorY, BlockFace.South);
            PaintRetainingWall(gameManager, worldX, worldZ, worldX - 1, worldZ, floorY, BlockFace.West);
        }

        private static void PaintRetainingWall(
            GameManager gameManager,
            int worldX,
            int worldZ,
            int neighborX,
            int neighborZ,
            int floorY,
            BlockFace face)
        {
            int neighborFloorY = GetFloorY(neighborX, neighborZ);
            if (neighborFloorY >= floorY) return;

            for (int y = neighborFloorY + 1; y <= floorY; y++)
            {
                PaintBlockFace(gameManager, worldX, y, worldZ, face, WallPaintId);
            }
        }

        private static bool TryResolvePalette()
        {
            if (paletteResolved)
            {
                return floorBlock.Block != null && wallBlock.Block != null && ceilingLightBlock.Block != null
                    && officeChairBlock.Block != null && computerBlock.Block != null && deskLampBlock.Block != null;
            }

            floorBlock = Block.GetBlockValue("concreteMaster", false);
            wallBlock = Block.GetBlockValue("concreteMaster", false);
            counterBlock = Block.GetBlockValue("woodMaster", false);
            ceilingLightBlock = Block.GetBlockValue("ceilingLight02", false);
            officeChairBlock = Block.GetBlockValue("officeChair01", false);
            computerBlock = Block.GetBlockValue("decoComputerDeskTopPC", false);
            deskLampBlock = Block.GetBlockValue("deskLampLight02Yellow", false);
            paletteResolved = true;

            if (floorBlock.Block != null && wallBlock.Block != null && counterBlock.Block != null && ceilingLightBlock.Block != null
                && officeChairBlock.Block != null && computerBlock.Block != null && deskLampBlock.Block != null)
            {
                return true;
            }

            Logger.Error("[Backrooms] Required structural, lighting, or prop blocks are unavailable.");
            return false;
        }

        private static bool IsWall(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return false;

            if (localX == 0 && !IsDoorwayOnWestEdge(macroX, macroZ, localZ)) return true;
            if (localX == MacroSize - 1 && !IsDoorwayOnEastEdge(macroX, macroZ, localZ)) return true;
            if (localZ == 0 && !IsDoorwayOnNorthEdge(macroX, macroZ, localX)) return true;
            if (localZ == MacroSize - 1 && !IsDoorwayOnSouthEdge(macroX, macroZ, localX)) return true;

            return IsInteriorWall(GetTemplate(macroX, macroZ), localX, localZ);
        }

        private static int GetCeilingStartY(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);

            bool lowCeiling = template == 1 && localX >= 5 && localX <= 12 && localZ >= 20 && localZ <= 27;
            lowCeiling |= template == 3 && localX >= 19 && localX <= 26 && localZ >= 5 && localZ <= 12;
            return lowCeiling ? CeilingY - 2 : CeilingY;
        }

        private static bool IsInteriorWall(int template, int localX, int localZ)
        {
            switch (template)
            {
                case 0:
                    return IsRoomOutline(localX, localZ, 10, 10, 21, 21, DoorNorth | DoorSouth);
                case 1:
                    return IsRoomOutline(localX, localZ, 5, 6, 13, 18, DoorEast | DoorSouth)
                        || IsRoomOutline(localX, localZ, 18, 13, 26, 25, DoorWest | DoorNorth);
                case 2:
                    return IsRoomOutline(localX, localZ, 5, 5, 12, 14, DoorEast | DoorSouth)
                        || IsRoomOutline(localX, localZ, 19, 18, 27, 27, DoorWest | DoorNorth)
                        || IsWallBandWithDoorway(localX, localZ, 12, 15, 24, 17, true);
                case 3:
                    return IsCrossWall(localX, localZ);
                case 5:
                    return false;
                case 6:
                    return false;
                case 7:
                    return false;
                default:
                    return IsRoomOutline(localX, localZ, 6, 6, 12, 25, DoorNorth | DoorSouth)
                        || IsRoomOutline(localX, localZ, 19, 6, 25, 25, DoorNorth | DoorSouth)
                        || IsRoomOutline(localX, localZ, 12, 13, 19, 18, DoorWest | DoorEast);
            }
        }

        private static bool IsRoomOutline(int x, int z, int minX, int minZ, int maxX, int maxZ, int doorwayMask)
        {
            if (!IsRectangleOutline(x, z, minX, minZ, maxX, maxZ)) return false;
            return !IsRoomDoorway(x, z, minX, minZ, maxX, maxZ, doorwayMask);
        }

        private static bool IsRectangleOutline(int x, int z, int minX, int minZ, int maxX, int maxZ)
        {
            return IsInRectangle(x, z, minX, minZ, maxX, maxZ)
                && (x == minX || x == maxX || z == minZ || z == maxZ);
        }

        private static bool IsRoomDoorway(int x, int z, int minX, int minZ, int maxX, int maxZ, int doorwayMask)
        {
            int centerX = (minX + maxX) / 2;
            int centerZ = (minZ + maxZ) / 2;
            return ((doorwayMask & DoorWest) != 0 && x == minX && IsAtDoorCenter(z, centerZ))
                || ((doorwayMask & DoorEast) != 0 && x == maxX && IsAtDoorCenter(z, centerZ))
                || ((doorwayMask & DoorNorth) != 0 && z == minZ && IsAtDoorCenter(x, centerX))
                || ((doorwayMask & DoorSouth) != 0 && z == maxZ && IsAtDoorCenter(x, centerX));
        }

        private static bool IsWallBandWithDoorway(int x, int z, int minX, int minZ, int maxX, int maxZ, bool horizontal)
        {
            if (!IsInRectangle(x, z, minX, minZ, maxX, maxZ)) return false;
            int center = horizontal ? (minX + maxX) / 2 : (minZ + maxZ) / 2;
            int position = horizontal ? x : z;
            return !IsAtDoorCenter(position, center);
        }

        private static bool IsCrossWall(int x, int z)
        {
            bool vertical = IsInRectangle(x, z, 14, 6, 17, 25);
            bool horizontal = IsInRectangle(x, z, 6, 14, 25, 17);
            if (!vertical && !horizontal) return false;

            bool verticalDoorway = vertical && (z >= 9 && z <= 11 || z >= 21 && z <= 23);
            bool horizontalDoorway = horizontal && (x >= 9 && x <= 11 || x >= 21 && x <= 23);
            return !verticalDoorway && !horizontalDoorway;
        }

        private static bool IsInRectangle(int x, int z, int minX, int minZ, int maxX, int maxZ)
        {
            return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
        }

        private static int GetFloorY(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return FloorY;
            if (GetTemplate(macroX, macroZ) == 5 && IsPit(worldX, worldZ)) return FloorY - PitDepth;
            if (GetTemplate(macroX, macroZ) != 6) return FloorY;

            if (localX >= 12 && localX <= 19)
            {
                if (localZ >= 4 && localZ <= 8) return FloorY - Math.Min(localZ - 4, SplitLevelDepth);
                if (localZ >= 23 && localZ <= 27) return FloorY - Math.Min(27 - localZ, SplitLevelDepth);
            }

            return IsInRectangle(localX, localZ, 9, 9, 22, 22)
                ? FloorY - SplitLevelDepth
                : FloorY;
        }

        private static bool IsTwoStoreyTemplate(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return GetTemplate(macroX, macroZ) == 7;
        }

        private static bool IsPitStoreyTemplate(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return GetTemplate(macroX, macroZ) == 5;
        }

        private static void GenerateTwoStoreyColumn(Chunk chunk, int chunkLocalX, int chunkLocalZ, int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            for (int y = 0; y <= FloorY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
            }

            if (IsBasementCorridor(worldX, worldZ))
            {
                BuildBasementCorridorColumn(chunk, chunkLocalX, chunkLocalZ);
            }

            for (int y = FloorY + 1; y <= StoryRoofTopY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, BlockValue.Air);
            }

            bool lowerDoorway = IsLowerDoorway(worldX, worldZ);
            int lowerDoorTopY = lowerDoorway ? GetDoorTopY(worldX, worldZ, FloorY, StoryFloorY, 23011) : FloorY;
            int lowerFrameTopY = FloorY;
            bool lowerDoorFrame = !lowerDoorway && TryGetLowerDoorFrameTopY(worldX, worldZ, out lowerFrameTopY);
            bool lowerWall = IsWall(worldX, worldZ);
            if (lowerDoorway || lowerWall)
            {
                for (int y = FloorY + 1; y < StoryFloorY; y++)
                {
                    bool header = lowerDoorway && y > lowerDoorTopY;
                    bool frame = lowerWall && lowerDoorFrame && y <= lowerFrameTopY;
                    if (header || lowerWall)
                    {
                        chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, frame || header ? counterBlock : wallBlock);
                    }
                }
            }

            int stairTopY;
            bool stair = TryGetStoryStairTopY(localX, localZ, out stairTopY);
            if (stair)
            {
                for (int y = FloorY + 1; y <= stairTopY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
                }
            }
            else
            {
                chunk.SetBlockRaw(chunkLocalX, StoryFloorY, chunkLocalZ, floorBlock);
            }

            if (!lowerWall && !stair && ShouldPlaceCeilingLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(chunkLocalX, StoryFloorY - 1, chunkLocalZ, ceilingLightBlock);
            }

            bool upperDoorway = IsTwoStoreyUpperDoorway(worldX, worldZ);
            int upperDoorTopY = upperDoorway ? GetDoorTopY(worldX, worldZ, StoryFloorY, StoryCeilingY, 24019) : StoryFloorY;
            int upperFrameTopY = StoryFloorY;
            bool upperDoorFrame = !upperDoorway && TryGetUpperDoorFrameTopY(worldX, worldZ, out upperFrameTopY);
            bool upperWall = IsTwoStoreyUpperWall(worldX, worldZ);
            if (upperDoorway || upperWall)
            {
                for (int y = StoryFloorY + 1; y < StoryCeilingY; y++)
                {
                    bool header = upperDoorway && y > upperDoorTopY;
                    bool frame = upperWall && upperDoorFrame && y <= upperFrameTopY;
                    if (header || upperWall)
                    {
                        chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, frame || header ? counterBlock : wallBlock);
                    }
                }
            }

            for (int y = StoryCeilingY; y <= StoryRoofTopY; y++)
            {
                bool light = y == StoryCeilingY && !upperWall && ShouldPlaceCeilingLight(worldX, worldZ);
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, light ? ceilingLightBlock : wallBlock);
            }
        }

        private static bool TryGetStoryStairTopY(int localX, int localZ, out int stairTopY)
        {
            if (localX >= 12 && localX <= 19)
            {
                if (localZ >= 4 && localZ <= 10)
                {
                    stairTopY = FloorY + Math.Min(localZ - 3, StoreyHeight);
                    return true;
                }

                if (localZ >= 21 && localZ <= 27)
                {
                    stairTopY = FloorY + Math.Min(28 - localZ, StoreyHeight);
                    return true;
                }
            }

            stairTopY = FloorY;
            return false;
        }

        private static bool IsTwoStoreyOuterWall(int localX, int localZ)
        {
            return localX == 0 || localX == MacroSize - 1 || localZ == 0 || localZ == MacroSize - 1;
        }

        private static bool IsTwoStoreyUpperWall(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            if (localZ == 15 || localZ == 16)
            {
                return !IsTwoStoreyUpperDoorway(worldX, worldZ);
            }

            if (!IsTwoStoreyOuterWall(localX, localZ)) return false;
            return !IsUpperStoreyEdgeDoorway(macroX, macroZ, localX, localZ);
        }

        private static bool IsTwoStoreyUpperDoorway(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if ((localZ == 15 || localZ == 16) && localX >= 13 && localX <= 18)
            {
                return true;
            }

            return IsTwoStoreyOuterWall(localX, localZ)
                && IsUpperStoreyEdgeDoorway(macroX, macroZ, localX, localZ);
        }

        private static bool IsLowerDoorway(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return false;

            if (localX == 0) return IsDoorwayOnWestEdge(macroX, macroZ, localZ);
            if (localX == MacroSize - 1) return IsDoorwayOnEastEdge(macroX, macroZ, localZ);
            if (localZ == 0) return IsDoorwayOnNorthEdge(macroX, macroZ, localX);
            if (localZ == MacroSize - 1) return IsDoorwayOnSouthEdge(macroX, macroZ, localX);

            return IsInteriorDoorway(GetTemplate(macroX, macroZ), localX, localZ);
        }

        private static bool IsInteriorDoorway(int template, int localX, int localZ)
        {
            switch (template)
            {
                case 0:
                    return IsRoomDoorway(localX, localZ, 10, 10, 21, 21, DoorNorth | DoorSouth);
                case 1:
                    return IsRoomDoorway(localX, localZ, 5, 6, 13, 18, DoorEast | DoorSouth)
                        || IsRoomDoorway(localX, localZ, 18, 13, 26, 25, DoorWest | DoorNorth);
                case 2:
                    return IsRoomDoorway(localX, localZ, 5, 5, 12, 14, DoorEast | DoorSouth)
                        || IsRoomDoorway(localX, localZ, 19, 18, 27, 27, DoorWest | DoorNorth)
                        || IsInRectangle(localX, localZ, 12, 15, 24, 17) && IsAtDoorCenter(localX, 18);
                case 4:
                    return IsRoomDoorway(localX, localZ, 6, 6, 12, 25, DoorNorth | DoorSouth)
                        || IsRoomDoorway(localX, localZ, 19, 6, 25, 25, DoorNorth | DoorSouth)
                        || IsRoomDoorway(localX, localZ, 12, 13, 19, 18, DoorWest | DoorEast);
                default:
                    return false;
            }
        }

        private static bool TryGetLowerDoorFrameTopY(int worldX, int worldZ, out int frameTopY)
        {
            return TryGetAdjacentDoorTopY(worldX, worldZ, FloorY, StoryFloorY, 23011, IsLowerDoorway, out frameTopY);
        }

        private static bool TryGetUpperDoorFrameTopY(int worldX, int worldZ, out int frameTopY)
        {
            return TryGetAdjacentDoorTopY(worldX, worldZ, StoryFloorY, StoryCeilingY, 24019, IsTwoStoreyUpperDoorway, out frameTopY);
        }

        private static bool TryGetAdjacentDoorTopY(
            int worldX,
            int worldZ,
            int floorY,
            int ceilingY,
            int salt,
            Func<int, int, bool> isDoorway,
            out int frameTopY)
        {
            if (isDoorway(worldX, worldZ - 1))
            {
                frameTopY = GetDoorTopY(worldX, worldZ - 1, floorY, ceilingY, salt);
                return true;
            }

            if (isDoorway(worldX + 1, worldZ))
            {
                frameTopY = GetDoorTopY(worldX + 1, worldZ, floorY, ceilingY, salt);
                return true;
            }

            if (isDoorway(worldX, worldZ + 1))
            {
                frameTopY = GetDoorTopY(worldX, worldZ + 1, floorY, ceilingY, salt);
                return true;
            }

            if (isDoorway(worldX - 1, worldZ))
            {
                frameTopY = GetDoorTopY(worldX - 1, worldZ, floorY, ceilingY, salt);
                return true;
            }

            frameTopY = floorY;
            return false;
        }

        private static int GetDoorTopY(int worldX, int worldZ, int floorY, int ceilingY, int salt)
        {
            int maximumHeight = ceilingY - floorY - 1;
            int heightVariants = Math.Min(DoorHeightVariants, maximumHeight - MinimumDoorHeight + 1);
            int doorwayHeight = heightVariants > 1
                ? MinimumDoorHeight + PositiveModulo(GetDoorwayHash(worldX, worldZ, salt), heightVariants)
                : maximumHeight;
            return floorY + doorwayHeight;
        }

        private static int GetDoorwayHash(int worldX, int worldZ, int salt)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (localX == 0) return Hash(macroX - 1, macroZ, salt + 1);
            if (localX == MacroSize - 1) return Hash(macroX, macroZ, salt + 1);
            if (localZ == 0) return Hash(macroX, macroZ - 1, salt + 2);
            if (localZ == MacroSize - 1) return Hash(macroX, macroZ, salt + 2);
            return Hash(macroX, macroZ, salt + (localZ == 15 || localZ == 16 ? 3 : 4));
        }

        private static bool IsUpperStoreyEdgeDoorway(int macroX, int macroZ, int localX, int localZ)
        {
            if (localX == 0 && HasTwoStoreyMacro(macroX - 1, macroZ))
            {
                return IsAtDoorCenter(localZ, GetVerticalDoorCenter(macroX - 1, macroZ));
            }

            if (localX == MacroSize - 1 && HasTwoStoreyMacro(macroX + 1, macroZ))
            {
                return IsAtDoorCenter(localZ, GetVerticalDoorCenter(macroX, macroZ));
            }

            if (localZ == 0 && HasTwoStoreyMacro(macroX, macroZ - 1))
            {
                return IsAtDoorCenter(localX, GetHorizontalDoorCenter(macroX, macroZ - 1));
            }

            return localZ == MacroSize - 1 && HasTwoStoreyMacro(macroX, macroZ + 1)
                && IsAtDoorCenter(localX, GetHorizontalDoorCenter(macroX, macroZ));
        }

        private static bool HasTwoStoreyMacro(int macroX, int macroZ)
        {
            return GetTemplate(macroX, macroZ) == 7;
        }

        private static void SetTwoStoreyStability(Chunk chunk, int localX, int localZ)
        {
            if (IsBasementCorridor((chunk.X << 4) + localX, (chunk.Z << 4) + localZ))
            {
                SetBasementCorridorStability(chunk, localX, localZ);
            }

            for (int y = FloorY + 1; y <= StoryRoofTopY; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        private static void PaintTwoStoreyColumn(GameManager gameManager, int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
            if (IsBasementCorridor(worldX, worldZ))
            {
                PaintBasementCorridorColumn(gameManager, worldX, worldZ);
            }
            else
            {
                PaintBasementCorridorBoundary(gameManager, worldX, worldZ);
            }

            bool lowerDoorway = IsLowerDoorway(worldX, worldZ);
            int lowerDoorTopY = lowerDoorway ? GetDoorTopY(worldX, worldZ, FloorY, StoryFloorY, 23011) : FloorY;
            int lowerFrameTopY = FloorY;
            bool lowerDoorFrame = !lowerDoorway && TryGetLowerDoorFrameTopY(worldX, worldZ, out lowerFrameTopY);
            bool lowerWall = IsWall(worldX, worldZ);
            if (lowerDoorway || lowerWall)
            {
                for (int y = FloorY + 1; y < StoryFloorY; y++)
                {
                    bool header = lowerDoorway && y > lowerDoorTopY;
                    bool frame = lowerWall && lowerDoorFrame && y <= lowerFrameTopY;
                    if (header || frame)
                    {
                        PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                    }
                    else if (lowerWall)
                    {
                        PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                    }
                }
            }

            int stairTopY;
            if (TryGetStoryStairTopY(localX, localZ, out stairTopY))
            {
                for (int y = FloorY + 1; y <= stairTopY; y++)
                {
                    PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                }

                PaintBlockFace(gameManager, worldX, stairTopY, worldZ, BlockFace.Top, FloorPaintId);
            }
            else
            {
                PaintBlockAllFaces(gameManager, worldX, StoryFloorY, worldZ, WallPaintId);
                PaintBlockFace(gameManager, worldX, StoryFloorY, worldZ, BlockFace.Top, FloorPaintId);
                PaintBlockFace(gameManager, worldX, StoryFloorY, worldZ, BlockFace.Bottom, CeilingPaintId);
            }

            bool upperDoorway = IsTwoStoreyUpperDoorway(worldX, worldZ);
            int upperDoorTopY = upperDoorway ? GetDoorTopY(worldX, worldZ, StoryFloorY, StoryCeilingY, 24019) : StoryFloorY;
            int upperFrameTopY = StoryFloorY;
            bool upperDoorFrame = !upperDoorway && TryGetUpperDoorFrameTopY(worldX, worldZ, out upperFrameTopY);
            bool upperWall = IsTwoStoreyUpperWall(worldX, worldZ);
            if (upperDoorway || upperWall)
            {
                for (int y = StoryFloorY + 1; y < StoryCeilingY; y++)
                {
                    bool header = upperDoorway && y > upperDoorTopY;
                    bool frame = upperWall && upperDoorFrame && y <= upperFrameTopY;
                    if (header || frame)
                    {
                        PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                    }
                    else if (upperWall)
                    {
                        PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                    }
                }
            }

            for (int y = StoryCeilingY; y <= StoryRoofTopY; y++)
            {
                bool light = y == StoryCeilingY && !IsTwoStoreyUpperWall(worldX, worldZ) && ShouldPlaceCeilingLight(worldX, worldZ);
                if (light) continue;

                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                if (y == StoryCeilingY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Bottom, CeilingPaintId);
                }
            }
        }

        private static void GeneratePitStoreyColumn(Chunk chunk, int chunkLocalX, int chunkLocalZ, int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            for (int y = 0; y <= PitFloorY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
            }

            for (int y = PitFloorY + 1; y <= CeilingY + 1; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, BlockValue.Air);
            }

            bool corridorOpening = IsPitLowerStoreyWall(localX, localZ) && IsBasementCorridor(worldX, worldZ);
            if (IsPitLowerStoreyWall(localX, localZ))
            {
                for (int y = PitFloorY; y < FloorY; y++)
                {
                    if (corridorOpening && y < BasementCorridorCeilingY) continue;

                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                }
            }

            int stairTopY;
            bool pitStair = TryGetPitStairTopY(localX, localZ, out stairTopY);
            if (pitStair)
            {
                for (int y = PitFloorY + 1; y <= stairTopY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
                }
            }
            else if (!IsPit(worldX, worldZ))
            {
                if (IsPitRetainingWall(worldX, worldZ))
                {
                    for (int y = PitFloorY + 1; y < FloorY; y++)
                    {
                        chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                    }
                }

                chunk.SetBlockRaw(chunkLocalX, FloorY, chunkLocalZ, floorBlock);
            }

            bool basementFeatureDoorway = !pitStair && IsBasementFeatureDoorway(worldX, worldZ);
            bool basementFeatureWall = !pitStair && IsBasementFeatureWall(worldX, worldZ);
            if (basementFeatureDoorway)
            {
                for (int y = BasementDoorTopY + 1; y < FloorY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, counterBlock);
                }
            }
            else if (basementFeatureWall)
            {
                for (int y = PitFloorY; y < FloorY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                }
            }
            else if (!pitStair)
            {
                BlockValue basementFeature = GetBasementFeatureBlock(worldX, worldZ);
                if (basementFeature.Block != null)
                {
                    chunk.SetBlockRaw(chunkLocalX, PitFloorY + 1, chunkLocalZ, basementFeature);
                }
            }

            bool upperWall = IsWall(worldX, worldZ);
            bool doorFrame = upperWall && IsDoorFrame(worldX, worldZ);
            if (upperWall)
            {
                for (int y = FloorY + 1; y < CeilingY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, doorFrame ? counterBlock : wallBlock);
                }
            }

            for (int y = CeilingY; y <= CeilingY + 1; y++)
            {
                bool light = y == CeilingY && !upperWall && ShouldPlaceCeilingLight(worldX, worldZ);
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, light ? ceilingLightBlock : wallBlock);
            }
        }

        private static bool IsPitLowerStoreyWall(int localX, int localZ)
        {
            return localX == 0 || localX == MacroSize - 1 || localZ == 0 || localZ == MacroSize - 1;
        }

        private static bool IsBasementCorridor(int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return localX >= 14 && localX <= 17 || localZ >= 14 && localZ <= 17;
        }

        private static void BuildBasementCorridorColumn(Chunk chunk, int localX, int localZ)
        {
            for (int y = PitFloorY + 1; y < BasementCorridorCeilingY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
            }

            for (int y = BasementCorridorCeilingY; y <= FloorY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, floorBlock);
            }
        }

        private static void SetBasementCorridorStability(Chunk chunk, int localX, int localZ)
        {
            for (int y = BasementCorridorCeilingY; y <= FloorY; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        private static void PaintBasementCorridorColumn(GameManager gameManager, int worldX, int worldZ)
        {
            for (int y = BasementCorridorCeilingY; y <= FloorY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
            }

            PaintBlockFace(gameManager, worldX, PitFloorY, worldZ, BlockFace.Top, FloorPaintId);
            PaintBlockFace(gameManager, worldX, BasementCorridorCeilingY, worldZ, BlockFace.Bottom, CeilingPaintId);
            PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
        }

        private static void PaintBasementCorridorBoundary(GameManager gameManager, int worldX, int worldZ)
        {
            PaintBasementCorridorBoundaryFace(gameManager, worldX, worldZ, worldX, worldZ - 1, BlockFace.North);
            PaintBasementCorridorBoundaryFace(gameManager, worldX, worldZ, worldX + 1, worldZ, BlockFace.East);
            PaintBasementCorridorBoundaryFace(gameManager, worldX, worldZ, worldX, worldZ + 1, BlockFace.South);
            PaintBasementCorridorBoundaryFace(gameManager, worldX, worldZ, worldX - 1, worldZ, BlockFace.West);
        }

        private static void PaintBasementCorridorBoundaryFace(
            GameManager gameManager,
            int worldX,
            int worldZ,
            int neighborX,
            int neighborZ,
            BlockFace face)
        {
            if (!IsBasementCorridor(neighborX, neighborZ)) return;

            for (int y = PitFloorY; y < BasementCorridorCeilingY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                if (y == PitFloorY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Top, FloorPaintId);
                }
            }
        }

        private static int GetBasementFeatureVariant(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return PositiveModulo(Hash(macroX, macroZ, 27011), 4);
        }

        private static bool IsBasementFeatureWall(int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return IsBasementFeatureRoomBoundary(GetBasementFeatureVariant(worldX, worldZ), localX, localZ)
                && !IsBasementFeatureDoorway(worldX, worldZ);
        }

        private static bool IsBasementFeatureDoorway(int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int variant = GetBasementFeatureVariant(worldX, worldZ);

            switch (variant)
            {
                case 0:
                    return IsRoomDoorway(localX, localZ, 3, 3, 10, 10, DoorSouth);
                case 1:
                    return IsRoomDoorway(localX, localZ, 21, 3, 28, 10, DoorWest);
                case 2:
                    return IsRoomDoorway(localX, localZ, 3, 21, 10, 28, DoorNorth);
                default:
                    return IsRoomDoorway(localX, localZ, 21, 21, 28, 28, DoorEast);
            }
        }

        private static bool IsBasementFeatureRoomBoundary(int variant, int localX, int localZ)
        {
            switch (variant)
            {
                case 0:
                    return IsRectangleOutline(localX, localZ, 3, 3, 10, 10);
                case 1:
                    return IsRectangleOutline(localX, localZ, 21, 3, 28, 10);
                case 2:
                    return IsRectangleOutline(localX, localZ, 3, 21, 10, 28);
                default:
                    return IsRectangleOutline(localX, localZ, 21, 21, 28, 28);
            }
        }

        private static BlockValue GetBasementFeatureBlock(int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            switch (GetBasementFeatureVariant(worldX, worldZ))
            {
                case 0:
                    if (localX == 6 && localZ == 6) return counterBlock;
                    if (localX == 7 && localZ == 6) return computerBlock;
                    if (localX == 5 && localZ == 7) return officeChairBlock;
                    if (localX == 8 && localZ == 7) return deskLampBlock;
                    break;
                case 1:
                    if (localX == 24 && localZ == 6) return counterBlock;
                    if (localX == 25 && localZ == 6) return computerBlock;
                    if (localX == 23 && localZ == 7) return officeChairBlock;
                    if (localX == 26 && localZ == 7) return deskLampBlock;
                    break;
                case 2:
                    if (localX == 6 && localZ == 24) return counterBlock;
                    if (localX == 7 && localZ == 24) return computerBlock;
                    if (localX == 5 && localZ == 25) return officeChairBlock;
                    if (localX == 8 && localZ == 25) return deskLampBlock;
                    break;
                default:
                    if (localX == 24 && localZ == 24) return counterBlock;
                    if (localX == 25 && localZ == 24) return computerBlock;
                    if (localX == 23 && localZ == 25) return officeChairBlock;
                    if (localX == 26 && localZ == 25) return deskLampBlock;
                    break;
            }

            return default(BlockValue);
        }

        private static bool TryGetPitStairTopY(int localX, int localZ, out int stairTopY)
        {
            if (localX >= 13 && localX <= 18 && localZ >= 13 && localZ <= 18)
            {
                stairTopY = PitFloorY + 1 + localZ - 13;
                return true;
            }

            stairTopY = PitFloorY;
            return false;
        }

        private static void SetPitStoreyStability(Chunk chunk, int localX, int localZ)
        {
            for (int y = PitFloorY + 1; y <= CeilingY + 1; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        private static void PaintPitStoreyColumn(GameManager gameManager, int worldX, int worldZ)
        {
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            PaintPitFloorBlock(gameManager, worldX, worldZ);

            bool corridorOpening = IsPitLowerStoreyWall(localX, localZ) && IsBasementCorridor(worldX, worldZ);
            if (IsPitLowerStoreyWall(localX, localZ))
            {
                for (int y = PitFloorY; y < FloorY; y++)
                {
                    if (corridorOpening && y < BasementCorridorCeilingY) continue;

                    PaintBasementWallBlock(gameManager, worldX, y, worldZ);
                }
            }

            int stairTopY;
            bool pitStair = TryGetPitStairTopY(localX, localZ, out stairTopY);
            if (pitStair)
            {
                for (int y = PitFloorY + 1; y <= stairTopY; y++)
                {
                    PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                }

                PaintBlockFace(gameManager, worldX, stairTopY, worldZ, BlockFace.Top, FloorPaintId);
            }
            else if (!IsPit(worldX, worldZ))
            {
                PaintBlockAllFaces(gameManager, worldX, FloorY, worldZ, WallPaintId);
                PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
                PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Bottom, CeilingPaintId);
                PaintPitUpperFloorEdges(gameManager, worldX, worldZ);
            }

            bool basementFeatureDoorway = !pitStair && IsBasementFeatureDoorway(worldX, worldZ);
            bool basementFeatureWall = !pitStair && IsBasementFeatureWall(worldX, worldZ);
            if (basementFeatureDoorway)
            {
                for (int y = BasementDoorTopY + 1; y < FloorY; y++)
                {
                    PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                }
            }
            else if (basementFeatureWall)
            {
                for (int y = PitFloorY; y < FloorY; y++)
                {
                    PaintBasementWallBlock(gameManager, worldX, y, worldZ);
                }
            }

            bool upperWall = IsWall(worldX, worldZ);
            bool doorFrame = upperWall && IsDoorFrame(worldX, worldZ);
            if (upperWall)
            {
                for (int y = FloorY + 1; y < CeilingY; y++)
                {
                    if (doorFrame)
                    {
                        PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                    }
                    else
                    {
                        PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                    }
                }
            }

            for (int y = CeilingY; y <= CeilingY + 1; y++)
            {
                bool light = y == CeilingY && !upperWall && ShouldPlaceCeilingLight(worldX, worldZ);
                if (light) continue;

                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                if (y == CeilingY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Bottom, CeilingPaintId);
                }
            }
        }

        private static void PaintBasementWallBlock(GameManager gameManager, int worldX, int worldY, int worldZ)
        {
            PaintBlockAllFaces(gameManager, worldX, worldY, worldZ, WallPaintId);
            if (worldY != PitFloorY) return;

            PaintBlockFace(gameManager, worldX, worldY, worldZ, BlockFace.Top, FloorPaintId);
            PaintDoorFrameFaces(gameManager, worldX, worldY, worldZ);
        }

        private static void PaintPitFloorBlock(GameManager gameManager, int worldX, int worldZ)
        {
            PaintBlockAllFaces(gameManager, worldX, PitFloorY, worldZ, WallPaintId);
            PaintBlockFace(gameManager, worldX, PitFloorY, worldZ, BlockFace.Top, FloorPaintId);
        }

        private static void PaintPitUpperFloorEdges(GameManager gameManager, int worldX, int worldZ)
        {
            PaintPitUpperFloorEdge(gameManager, worldX, worldZ, worldX, worldZ - 1, BlockFace.North);
            PaintPitUpperFloorEdge(gameManager, worldX, worldZ, worldX + 1, worldZ, BlockFace.East);
            PaintPitUpperFloorEdge(gameManager, worldX, worldZ, worldX, worldZ + 1, BlockFace.South);
            PaintPitUpperFloorEdge(gameManager, worldX, worldZ, worldX - 1, worldZ, BlockFace.West);
        }

        private static void PaintPitUpperFloorEdge(
            GameManager gameManager,
            int worldX,
            int worldZ,
            int neighborX,
            int neighborZ,
            BlockFace face)
        {
            if (!IsPit(neighborX, neighborZ)) return;

            PaintBlockFace(gameManager, worldX, PitFloorY, worldZ, face, WallPaintId);
            for (int y = PitFloorY + 1; y < FloorY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
            }

            PaintBlockFace(gameManager, worldX, FloorY, worldZ, face, WallPaintId);
        }

        private static bool IsPit(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (GetTemplate(macroX, macroZ) != 5 || IsEntrySpace(macroX, macroZ, localX, localZ)) return false;

            return IsInRectangle(localX, localZ, 3, 3, 8, 8)
                || IsInRectangle(localX, localZ, 13, 3, 18, 8)
                || IsInRectangle(localX, localZ, 23, 3, 28, 8)
                || IsInRectangle(localX, localZ, 3, 13, 8, 18)
                || IsInRectangle(localX, localZ, 13, 13, 18, 18)
                || IsInRectangle(localX, localZ, 23, 13, 28, 18)
                || IsInRectangle(localX, localZ, 3, 23, 8, 28)
                || IsInRectangle(localX, localZ, 13, 23, 18, 28)
                || IsInRectangle(localX, localZ, 23, 23, 28, 28);
        }

        private static bool IsPitRetainingWall(int worldX, int worldZ)
        {
            return !IsPit(worldX, worldZ)
                && (IsPit(worldX, worldZ - 1)
                    || IsPit(worldX + 1, worldZ)
                    || IsPit(worldX, worldZ + 1)
                    || IsPit(worldX - 1, worldZ));
        }

        private static bool IsDoorFrame(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            if (localX == 0 && UsesDoorFrame(macroX - 1, macroZ, 10037))
            {
                return IsDoorFrameSide(localZ, GetVerticalDoorCenter(macroX - 1, macroZ));
            }

            if (localX == MacroSize - 1 && UsesDoorFrame(macroX, macroZ, 10037))
            {
                return IsDoorFrameSide(localZ, GetVerticalDoorCenter(macroX, macroZ));
            }

            if (localZ == 0 && UsesDoorFrame(macroX, macroZ - 1, 11003))
            {
                return IsDoorFrameSide(localX, GetHorizontalDoorCenter(macroX, macroZ - 1));
            }

            if (localZ == MacroSize - 1 && UsesDoorFrame(macroX, macroZ, 11003))
            {
                return IsDoorFrameSide(localX, GetHorizontalDoorCenter(macroX, macroZ));
            }

            if (!UsesDoorFrame(macroX, macroZ, 12011)) return false;
            return IsInteriorDoorFrame(GetTemplate(macroX, macroZ), localX, localZ);
        }

        private static bool IsInteriorDoorFrame(int template, int localX, int localZ)
        {
            switch (template)
            {
                case 0:
                    return IsRoomDoorFrame(localX, localZ, 10, 10, 21, 21, DoorNorth | DoorSouth);
                case 1:
                    return IsRoomDoorFrame(localX, localZ, 5, 6, 13, 18, DoorEast | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 18, 13, 26, 25, DoorWest | DoorNorth);
                case 2:
                    return IsRoomDoorFrame(localX, localZ, 5, 5, 12, 14, DoorEast | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 19, 18, 27, 27, DoorWest | DoorNorth);
                case 4:
                    return IsRoomDoorFrame(localX, localZ, 6, 6, 12, 25, DoorNorth | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 19, 6, 25, 25, DoorNorth | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 12, 13, 19, 18, DoorWest | DoorEast);
                default:
                    return false;
            }
        }

        private static bool IsRoomDoorFrame(int x, int z, int minX, int minZ, int maxX, int maxZ, int doorwayMask)
        {
            if (!IsRectangleOutline(x, z, minX, minZ, maxX, maxZ)) return false;

            int centerX = (minX + maxX) / 2;
            int centerZ = (minZ + maxZ) / 2;
            return ((doorwayMask & DoorWest) != 0 && x == minX && IsDoorFrameSide(z, centerZ))
                || ((doorwayMask & DoorEast) != 0 && x == maxX && IsDoorFrameSide(z, centerZ))
                || ((doorwayMask & DoorNorth) != 0 && z == minZ && IsDoorFrameSide(x, centerX))
                || ((doorwayMask & DoorSouth) != 0 && z == maxZ && IsDoorFrameSide(x, centerX));
        }

        private static bool IsDoorFrameSide(int position, int center)
        {
            return Math.Abs(position - center) == DoorHalfWidth + 1;
        }

        private static bool UsesDoorFrame(int first, int second, int salt)
        {
            return PositiveModulo(Hash(first, second, salt), 3) != 0;
        }

        private static int GetArchitectureKind(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);
            int variant = PositiveModulo(Hash(macroX, macroZ, 9001), 3);
            bool architecture;

            switch (template)
            {
                case 0:
                    architecture = IsInRectangle(localX, localZ, 4, 7, 8, 21)
                        || IsInRectangle(localX, localZ, 23, 10, 27, 24);
                    break;
                case 1:
                    architecture = IsInRectangle(localX, localZ, 14, 4, 17, 10)
                        || IsInRectangle(localX, localZ, 7, 21, 16, 24);
                    break;
                case 2:
                    architecture = IsInRectangle(localX, localZ, 14, 5, 18, 9)
                        || IsInRectangle(localX, localZ, 4, 20, 12, 23);
                    break;
                case 3:
                    architecture = IsInRectangle(localX, localZ, 6, 7, 11, 10)
                        || IsInRectangle(localX, localZ, 20, 21, 25, 24);
                    break;
                case 5:
                    architecture = false;
                    break;
                case 6:
                    architecture = false;
                    break;
                case 7:
                    architecture = false;
                    break;
                default:
                    architecture = IsInRectangle(localX, localZ, 4, 11, 10, 15)
                        || IsInRectangle(localX, localZ, 21, 16, 27, 20);
                    break;
            }

            if (!architecture) return ArchitectureNone;
            return variant == 0 ? ArchitectureCounter : ArchitectureHalfWall;
        }

        private static BlockValue GetArchitectureBlock(int architecture)
        {
            switch (architecture)
            {
                case ArchitectureHalfWall:
                    return wallBlock;
                case ArchitectureCounter:
                    return counterBlock;
                default:
                    return default(BlockValue);
            }
        }

        private static bool IsDoorwayOnWestEdge(int macroX, int macroZ, int localZ)
        {
            return IsAtDoorCenter(localZ, GetVerticalDoorCenter(macroX - 1, macroZ));
        }

        private static bool IsDoorwayOnEastEdge(int macroX, int macroZ, int localZ)
        {
            return IsAtDoorCenter(localZ, GetVerticalDoorCenter(macroX, macroZ));
        }

        private static bool IsDoorwayOnNorthEdge(int macroX, int macroZ, int localX)
        {
            return IsAtDoorCenter(localX, GetHorizontalDoorCenter(macroX, macroZ - 1));
        }

        private static bool IsDoorwayOnSouthEdge(int macroX, int macroZ, int localX)
        {
            return IsAtDoorCenter(localX, GetHorizontalDoorCenter(macroX, macroZ));
        }

        private static bool IsAtDoorCenter(int position, int center)
        {
            return Math.Abs(position - center) <= DoorHalfWidth;
        }

        private static int GetVerticalDoorCenter(int leftMacroX, int macroZ)
        {
            return 5 + PositiveModulo(Hash(leftMacroX, macroZ, 1009), MacroSize - 10);
        }

        private static int GetHorizontalDoorCenter(int macroX, int northMacroZ)
        {
            return 5 + PositiveModulo(Hash(macroX, northMacroZ, 2017), MacroSize - 10);
        }

        private static int GetTemplate(int macroX, int macroZ)
        {
            if (macroX == 0 && macroZ == 0) return 7;
            return PositiveModulo(Hash(macroX, macroZ, 3079), TemplateCount);
        }

        private static bool ShouldPlaceCeilingLight(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int offsetX = PositiveModulo(Hash(macroX, macroZ, 4001), 7);
            int offsetZ = PositiveModulo(Hash(macroX, macroZ, 5003), 7);

            return PositiveModulo(localX - offsetX, 7) == 0
                && PositiveModulo(localZ - offsetZ, 7) == 0
                && PositiveModulo(Hash(worldX, worldZ, 6007), 5) != 0;
        }

        private static BlockValue GetPropBlock(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return default(BlockValue);

            int propSeed = Hash(worldX, worldZ, 7001);
            if (PositiveModulo(propSeed, 151) != 0) return default(BlockValue);

            switch (PositiveModulo(Hash(worldX, worldZ, 8009), 3))
            {
                case 0:
                    return officeChairBlock;
                case 1:
                    return computerBlock;
                default:
                    return deskLampBlock;
            }
        }

        private static bool IsEntrySpace(int macroX, int macroZ, int localX, int localZ)
        {
            return macroX == 0 && macroZ == 0
                && localX >= 9 && localX <= 15
                && localZ >= 9 && localZ <= 15;
        }

        private static int Hash(int first, int second, int salt)
        {
            unchecked
            {
                int hash = salt;
                hash = (hash * 486187739) ^ first;
                hash = (hash * 16777619) ^ second;
                hash ^= hash >> 16;
                hash *= -2048144789;
                hash ^= hash >> 13;
                return hash & int.MaxValue;
            }
        }

        private static long GetChunkKey(int chunkX, int chunkZ)
        {
            return ((long)chunkX << 32) ^ (uint)chunkZ;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}