using System;
using System.Collections.Generic;
using LizziesMod;
using UnityEngine;

namespace LizziesMod.Backrooms
{
    /// <summary>
    /// Builds the Backrooms as an infinite, deterministic grid of 32 x 32 block macro-rooms.
    ///
    /// Generation happens in three deliberately separate stages:
    /// 1. <see cref="GenerateChunk"/> writes the raw blocks while 7 Days to Die is generating terrain.
    /// 2. <see cref="ProcessDeferredPainting"/> applies paint after the chunk reaches the world cache;
    ///    texture writes must happen on the main thread.
    /// 3. The raw generator assigns stability so the constructed ceilings, floors, and stairs do not
    ///    collapse when the game evaluates structural integrity.
    ///
    /// To make neighboring chunks agree without storing any world state, every layout choice comes
    /// from <see cref="Hash"/> using world or macro coordinates. A future generator can use another
    /// layout algorithm, but it must retain that coordinate-only determinism at shared chunk edges.
    /// </summary>
    public sealed class BackroomsChunkGenerator : GeneratedDimensionGeneratorBase, IDimensionGeneratorLifecycle
    {
        public const string GeneratorId = "backrooms";
        private const string SettingsModName = "LizziesMod_Backrooms";
        private const int WorldHeight = 256;

        // Defaults are intentionally conservative. EnsureLayout clamps the player-facing settings
        // again before any terrain is generated, so malformed ModSettings values cannot produce an
        // invalid vertical layout.
        private const int DefaultFloorY = 59;
        private const int DefaultStoreyHeight = 8;
        private const int DefaultPitDepth = 5;
        private const int DefaultBasementCorridorHeight = 4;
        private const int DefaultSplitLevelDepth = 4;
        private const int DefaultLayoutSeed = 0;
        private const int DefaultAmbientPropDensity = 2;
        private const int DefaultCeilingLightSpacing = 5;
        private const int DefaultBasementLightSpacing = 6;

        // A macro-room is larger than the engine's 16 x 16 terrain chunk. This is the key to the
        // layout: a room normally spans four chunks, while the helpers derive its local coordinate
        // from world position. That prevents seams at either chunk or macro-room boundaries.
        private const int MacroSize = 32;
        private const int DoorHalfWidth = 1;
        private const int TemplateCount = 17;
        private const int MinimumDoorHeight = 3;
        private const int PitStairDoorHeight = 2;
        private const int DoorHeightVariants = 3;
        private const int PitLayoutCount = 4;
        private const int BasementFeatureStyleCount = 4;
        private const int PitDoorVariantSalt = 28019;
        private const int CrossroadsSupportPillarChanceSalt = 26103;
        private const int CrossroadsSupportPillarLocationSalt = 26111;
        private const int PitPillarChanceSalt = 26079;
        private const int PitPillarLocationSalt = 26087;

        // Door masks let each room template describe its allowed exits compactly.
        private const int DoorWest = 1;
        private const int DoorEast = 2;
        private const int DoorNorth = 4;
        private const int DoorSouth = 8;

        // Architectural blocks occupy the first air block above a normal floor. Keep the integer
        // values private; callers should use GetArchitectureKind and GetArchitectureBlock instead.
        private const int ArchitectureNone = 0;
        private const int ArchitectureHalfWall = 1;
        private const int ArchitectureCounter = 2;

        // These are 7 Days to Die paint IDs, not block IDs. Painting is deferred because raw chunk
        // generation is not a safe place to call the client-side texture API.
        private const int WallPaintId = 176;
        private const int FloorPaintId = 26;
        private const int CeilingPaintId = 106;
        private const int BasementJunctionVariantSalt = 30983;
        private const int BasementLightSalt = 31001;
        private const int BasementStationLocationSalt = 31019;
        private const int BasementStationStyleSalt = 31037;
        private const int LowerChamberVariantSalt = 32011;
        private const int LowerChamberAccessSalt = 32019;
        private const int LowerChamberLayoutSalt = 32027;
        private const int LowerChamberPropSalt = 32033;
        private const int LowerChamberLightSpacing = 4;
        private const int LowerChamberAccessOpen = 0;
        private const int LowerChamberAccessGated = 1;
        private const int LowerChamberAccessSealed = 2;
        private const int PaintChunksPerUpdate = 1;
        private const int MaximumPaintRetries = 3;

        // Every entry is a 1 x 2 x 1 native composite door, so any selected model fits a single
        // retaining-wall cell. The stable hash in GetPitDoorBlock makes their variety persistent.
        private static readonly string[] PitDoorBlockNames =
        {
            "oldWoodDoor",
            "interiorDoorOldWhite",
            "interiorHouseDoorWhite",
            "exteriorHouseDoorOldWhite",
            "jailDoorWhite",
            "bathroomStallDoor",
            "commercialDoorV1White",
            "commercialDoorV3White",
            "trailerDoorWhite"
        };

        // Keep the ordinary rooms recognizably abandoned office/service spaces without placing
        // multi-block scenery that could crowd the deterministic walking routes.
        private static readonly string[] AmbientPropBlockNames =
        {
            "officeChair01",
            "decoComputerDeskTopPC",
            "decoComputerMonitorKeyboardMousePC",
            "decoComputerMonitorKeyboardMousePCScreen2",
            "decoComputerMonitorKeyboardMouse",
            "decoComputerMonitorKeyboardMouseScreen2",
            "decoComputerMonitorKeyboardMouse2",
            "decoComputerMonitorKeyboardMouse2Screen2",
            "decoComputerMonitor",
            "decoComputerMonitorScreen2",
            "deskLampLight02Yellow",
            "deskLampLight02White",
            "deskLampLight02Brown",
            "tableLampLight01White",
            "tableLampLight01Yellow",
            "tableLampLight01TippedOverWhite",
            "endTable",
            "endTableLamp",
            "radioHam",
            "projectorTableTop",
            "plantAloePottedWhite",
            "plantAloePottedYellow",
            "plantHousePottedWhite",
            "plantHousePottedYellow",
            "cntUtilityCartEmptyGrey",
            "cntJanitorCartEmpty",
            "cntshelfSupplyOfficeEmptyWhite",
            "cntFootlockerClosedGrey",
            "cntWaterCoolerFull",
            "cntBinTrashPlasticEmptyWhite",
            "cntDomedTrashCanEmpty",
            "cntTrashPile01"
        };

        // Basement stations sit at fixed corridor alcoves, so this palette can use larger native
        // workstation models without competing with the ordinary-room ambient props.
        private static readonly string[] BasementStationBlockNames =
        {
            "controlPanelBase01",
            "cntUtilityCartEmptyGrey",
            "cntFootlockerClosedGrey",
            "cntLootCrateMoPowerElectronics",
            "cntCollapsedWorkbenchEmpty",
            "cntCollapsedChemistryStation",
            "workbench",
            "chemistryStation"
        };

        // Resolve every block by name at runtime. A missing dependency causes this generator to
        // decline the chunk instead of silently emitting invalid BlockValues.
        private static readonly string[] RequiredBlockNames = BuildRequiredBlockNames();

        private static string[] BuildRequiredBlockNames()
        {
            List<string> names = new List<string>
            {
                "concreteMaster",
                "woodMaster",
                "lightPanelLEDWhite",
                "concreteShapes:ramp"
            };
            names.AddRange(AmbientPropBlockNames);
            names.AddRange(BasementStationBlockNames);
            names.AddRange(PitDoorBlockNames);
            return names.ToArray();
        }

        // BlockValues are cached once palette resolution succeeds. They remain static because the
        // generator instance may be recreated while the world generation contract stays the same.
        private static BlockValue floorBlock;
        private static BlockValue wallBlock;
        private static BlockValue counterBlock;
        private static BlockValue ceilingLightBlock;
        private static BlockValue basementLightBlock;
        private static BlockValue officeChairBlock;
        private static BlockValue computerBlock;
        private static BlockValue deskLampBlock;
        private static BlockValue lootCrateBlock;
        private static BlockValue controlPanelBlock;
        private static BlockValue utilityCartBlock;
        private static BlockValue footlockerBlock;
        private static BlockValue pitRampBlock;
        private static BlockValue[] ambientPropBlocks;
        private static BlockValue[] basementStationBlocks;
        private static BlockValue[] pitDoorBlocks;
        private bool paletteResolved;
        private static bool firstGeneratedChunkLogged;
        private static bool firstPaintedChunkLogged;
        private static bool paintFailureLogged;
        // Settings are read lazily so the mod settings manager is fully initialized first. They are
        // then frozen for this session: changing a terrain layout setting requires regenerated land.
        private static bool layoutInitialized;
        private static int configuredFloorY;
        private static int configuredStoreyHeight;
        private static int configuredPitDepth;
        private static int configuredBasementCorridorHeight;
        private static int configuredSplitLevelDepth;
        private static int configuredLayoutSeed;
        private static int configuredAmbientPropDensity;
        private static int configuredCeilingLightSpacing;
        private static int configuredBasementLightSpacing;
        // Terrain threads enqueue requests; the main thread consumes them. The companion hash set
        // de-duplicates requests because a chunk can be visited more than once during generation.
        private static readonly Queue<ChunkPaintRequest> paintQueue = new Queue<ChunkPaintRequest>();
        private static readonly HashSet<long> queuedPaintChunks = new HashSet<long>();

        public override string Id { get { return GeneratorId; } }
        public override bool HasMainThreadWork { get { return true; } }

        // Vertical reference points. Keeping every height derived from FloorY makes the special
        // templates move together when a mod author changes the configured main-floor elevation.
        // Pit floors sit exactly PitDepth below the main floor so their retaining walls meet solid floor.
        public static int FloorY { get { EnsureLayout(); return configuredFloorY; } }
        private static int CeilingY { get { return FloorY + StoreyHeight; } }
        private static int StoreyHeight { get { EnsureLayout(); return configuredStoreyHeight; } }
        private static int PitDepth { get { EnsureLayout(); return configuredPitDepth; } }
        private static int SplitLevelDepth { get { EnsureLayout(); return configuredSplitLevelDepth; } }
        private static int PitFloorY { get { return FloorY - PitDepth; } }
        // Retaining walls begin immediately above their solid pit floor.
        private static int PitInteriorWallBottomY { get { return PitFloorY + 1; } }
        // Native 1 x 2 doors anchor at their visible lower edge.
        private static int NativeDoorOriginY { get { return PitInteriorWallBottomY; } }
        private static int BasementDoorTopY { get { return PitFloorY + MinimumDoorHeight; } }
        private static int BasementCorridorCeilingY { get { return PitFloorY + configuredBasementCorridorHeight + 1; } }
        private static int StoryFloorY { get { return CeilingY; } }
        private static int StoryCeilingY { get { return StoryFloorY + StoreyHeight; } }
        private static int StoryRoofTopY { get { return StoryCeilingY + 1; } }

        private struct ChunkPaintRequest
        {
            // Chunk coordinates, not block coordinates. PaintChunk expands this to its 16 x 16
            // columns after the chunk is available from the live world cache.
            public int X;
            public int Z;
            public int RetryCount;

            public ChunkPaintRequest(int x, int z, int retryCount = 0)
            {
                X = x;
                Z = z;
                RetryCount = retryCount;
            }

            public ChunkPaintRequest WithRetry()
            {
                return new ChunkPaintRequest(X, Z, RetryCount + 1);
            }
        }

        private static void EnsureLayout()
        {
            if (layoutInitialized) return;

            // Each upper bound protects a lower derived level. For example, a pit must leave room
            // below the main floor, and a corridor cannot be as tall as the pit it sits inside.
            configuredFloorY = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "MainFloorY", DefaultFloorY),
                16,
                200);
            configuredStoreyHeight = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "StoreyHeight", DefaultStoreyHeight),
                MinimumDoorHeight + 1,
                8);
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
            configuredLayoutSeed = ModSettingsManager.GetSetting<int>(
                SettingsModName,
                "LayoutSeed",
                DefaultLayoutSeed);
            configuredAmbientPropDensity = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "AmbientPropDensity", DefaultAmbientPropDensity),
                0,
                20);
            configuredCeilingLightSpacing = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "CeilingLightSpacing", DefaultCeilingLightSpacing),
                3,
                12);
            configuredBasementLightSpacing = Clamp(
                ModSettingsManager.GetSetting<int>(SettingsModName, "BasementLightSpacing", DefaultBasementLightSpacing),
                3,
                12);
            layoutInitialized = true;

            Logger.Info(
                $"[Backrooms] Layout: floor Y={configuredFloorY}, storey height={configuredStoreyHeight}, " +
                $"pit depth={configuredPitDepth}, basement clearance={configuredBasementCorridorHeight}, " +
                $"split level depth={configuredSplitLevelDepth}, seed={configuredLayoutSeed}, " +
                $"ambient props={configuredAmbientPropDensity}%, ceiling light spacing={configuredCeilingLightSpacing}, " +
                $"basement light spacing={configuredBasementLightSpacing}.");
        }

        protected override void Initialize()
        {
            // The base class handles shared generator registration and required-block machinery.
            // This derived generator only owns the Backrooms-specific vertical layout.
            EnsureLayout();
        }

        protected override Vector3 GetEntryPositionCore(DimensionDefinition definition, Vector3 defaultPosition)
        {
            // Spawn inside the reserved empty zone of macro-room (0, 0), one block above its floor.
            return new Vector3(12.5f, FloorY + 1f, 12.5f);
        }

        protected override bool GenerateChunk(Chunk chunk)
        {
            if (!TryResolvePalette()) return false;

            if (!firstGeneratedChunkLogged)
            {
                firstGeneratedChunkLogged = true;
                Logger.Info($"[Backrooms] Generating stitched macro-room terrain from chunk ({chunk.X}, {chunk.Z}).");
            }

            // A terrain chunk is 16 x 16 columns. Convert each local position to world coordinates
            // before asking layout helpers anything; all layout decisions must use world space.
            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    // Templates 5, 7, 10, 14, and 15 have different vertical stacks, so they
                    // entirely own their columns. Normal rooms continue below through the shared floor/wall algorithm.
                    if (IsPitStoreyTemplate(worldX, worldZ))
                    {
                        InitializeTerrainColumn(chunk, localX, localZ, PitFloorY);
                        GeneratePitStoreyColumn(chunk, localX, localZ, worldX, worldZ);
                        continue;
                    }

                    if (IsTwoStoreyTemplate(worldX, worldZ))
                    {
                        InitializeTerrainColumn(chunk, localX, localZ, FloorY);
                        GenerateTwoStoreyColumn(chunk, localX, localZ, worldX, worldZ);
                        continue;
                    }

                    if (IsTallEmptyTemplate(worldX, worldZ))
                    {
                        InitializeTerrainColumn(chunk, localX, localZ, FloorY);
                        GenerateTallEmptyColumn(chunk, localX, localZ, worldX, worldZ);
                        continue;
                    }

                    // First establish solid terrain up to this column's floor. Split-level rooms
                    // return a lower floorY, which naturally creates retaining walls later on.
                    int floorY = GetFloorY(worldX, worldZ);
                    InitializeTerrainColumn(chunk, localX, localZ, floorY);
                    for (int y = 0; y <= floorY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, floorBlock);
                    }

                    for (int y = floorY + 1; y <= FloorY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
                    }

                    int splitLevelSlopeY;
                    BlockFace splitLevelRiseDirection;
                    bool splitLevelSlope = TryGetSplitLevelSlope(
                        worldX,
                        worldZ,
                        out splitLevelSlopeY,
                        out splitLevelRiseDirection);
                    if (splitLevelSlope)
                    {
                        chunk.SetBlockRaw(localX, splitLevelSlopeY, localZ, GetSlopeBlock(splitLevelRiseDirection));
                    }

                    // Carving the lower route after the floor fill keeps every macro connection continuous.
                    if (IsLowerChamberCell(worldX, worldZ))
                    {
                        BuildLowerChamberColumn(chunk, localX, localZ, worldX, worldZ);
                    }
                    else if (IsBasementCorridor(worldX, worldZ))
                    {
                        BuildBasementCorridorColumn(chunk, localX, localZ, worldX, worldZ);
                    }
                    else if (TouchesBasementLowerSpace(worldX, worldZ))
                    {
                        PrepareBasementCorridorTerrain(chunk, localX, localZ);
                    }

                    // The visible room is composed from walls, door frames, a two-block ceiling,
                    // optional lights, and a sparse architecture/prop pass in open floor cells.
                    bool wall = IsWall(worldX, worldZ);
                    bool doorFrame = wall && IsDoorFrame(worldX, worldZ);
                    int ceilingStartY = GetCeilingStartY(worldX, worldZ);
                    int ceilingTopY = ceilingStartY + 1;
                    int architecture = wall || IsPit(worldX, worldZ) ? ArchitectureNone : GetArchitectureKind(worldX, worldZ);
                    for (int y = ceilingStartY; y <= ceilingTopY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, wallBlock);
                    }

                    if (!wall && ShouldPlaceCeilingLight(worldX, worldZ))
                    {
                        chunk.SetBlockRaw(localX, ceilingStartY - 1, localZ, ceilingLightBlock);
                    }

                    for (int y = floorY + 1; y < ceilingStartY; y++)
                    {
                        if (wall)
                        {
                            chunk.SetBlockRaw(localX, y, localZ, doorFrame ? counterBlock : wallBlock);
                            continue;
                        }

                        if (!splitLevelSlope && y == FloorY + 1)
                        {
                            BlockValue architecturalBlock = GetArchitectureBlock(architecture);
                            if (architecturalBlock.Block != null)
                            {
                                chunk.SetBlockRaw(localX, y, localZ, architecturalBlock);
                                continue;
                            }

                            BlockValue prop = GetPropBlock(worldX, worldZ, 7001);
                            if (prop.Block != null)
                            {
                                chunk.SetBlockRaw(localX, y, localZ, prop);
                            }
                        }
                    }
                }
            }

            // Chunk terrain writes and texture writes use different game APIs. Queue the latter for
            // ProcessMainThread, then finish the raw chunk's stability before it becomes playable.
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
                        SealPitRetainingWallFooting(chunk, localX, localZ, worldX, worldZ);
                        if (IsPitLowerCorridor(worldX, worldZ))
                        {
                            if (!IsPitRetainingWall(worldX, worldZ))
                            {
                                RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
                            }

                            PlaceBasementCorridorDetails(chunk, localX, localZ, worldX, worldZ);
                        }
                        SetPitStoreyStability(chunk, localX, localZ);
                        continue;
                    }

                    if (IsTwoStoreyTemplate(worldX, worldZ) || IsTallEmptyTemplate(worldX, worldZ))
                    {
                        if (IsLowerChamberCell(worldX, worldZ))
                        {
                            RestoreLowerChamberColumn(chunk, localX, localZ, worldX, worldZ);
                        }
                        else if (IsBasementCorridor(worldX, worldZ))
                        {
                            RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
                            PlaceBasementCorridorDetails(chunk, localX, localZ, worldX, worldZ);
                        }
                        SetTallRoomStability(chunk, localX, localZ);
                        continue;
                    }

                    // Stability is applied to unsupported construction, especially ceilings above
                    // air. A value of 15 is the maximum support strength used by this generator.
                    int ceilingStartY = GetCeilingStartY(worldX, worldZ);
                    chunk.SetStability(localX, ceilingStartY, localZ, 15);
                    chunk.SetStability(localX, ceilingStartY + 1, localZ, 15);
                    if (IsLowerChamberCell(worldX, worldZ))
                    {
                        RestoreLowerChamberColumn(chunk, localX, localZ, worldX, worldZ);
                        SetBasementCorridorStability(chunk, localX, localZ);
                    }
                    else if (IsBasementCorridor(worldX, worldZ))
                    {
                        RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
                        PlaceBasementCorridorDetails(chunk, localX, localZ, worldX, worldZ);
                        SetBasementCorridorStability(chunk, localX, localZ);
                    }
                }
            }

            StitchLowerLevelSeams(chunk);
            FinalizeGeneratedChunk(chunk);
            return true;
        }

        private static void InitializeTerrainColumn(Chunk chunk, int localX, int localZ, int floorY)
        {
            // Treat the block below the visible floor as terrain and the floor itself as a normal
            // concrete block. This gives the engine correct height/density data while preserving the
            // sharp block face needed for painted floors, pits, and split-level retaining walls.
            int terrainHeight = Math.Max(0, floorY - 1);
            SetTerrainHeights(chunk, localX, localZ, terrainHeight);
            FillTerrainColumn(chunk, localX, localZ, terrainHeight, floorBlock);
            chunk.SetDensity(localX, floorY, localZ, MarchingCubes.DensityAir);
            ClearAirColumn(chunk, localX, localZ, floorY + 1, WorldHeight);
        }

        public override void ProcessMainThread()
        {
            // GeneratedDimensionGeneratorBase calls this from a safe game/UI thread.
            ProcessDeferredPainting();
        }

        public void OnDimensionActivated()
        {
            // The static queue can outlive a previous visit to this dimension. Start each visit with
            // only work generated for its current chunk cache, and allow fresh errors to be logged.
            ClearPendingPaintRequests();
            paintFailureLogged = false;
        }

        public void OnDimensionDeactivated()
        {
            // Deferred client-side texture writes are meaningful only for the active region cache.
            ClearPendingPaintRequests();
        }

        public static void ProcessDeferredPainting()
        {
            // SetBlockTextureClient is client/main-thread work. Processing one chunk at a time
            // avoids a visible frame spike while the player moves through newly generated rooms.
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

                // A queued chunk can leave the cache before its turn. Requeue it rather than losing
                // its paint request; another main-thread update will try it again.
                Chunk chunk = chunkCache.GetChunkSync(request.X, request.Z);
                if (chunk == null)
                {
                    DeferPaintRequest(request, key);
                    continue;
                }

                if (PaintChunk(gameManager, chunk))
                {
                    lock (paintQueue)
                    {
                        queuedPaintChunks.Remove(key);
                    }
                }
                else
                {
                    RetryPaintRequest(request, key, "painting failed");
                }
            }
        }

        private static void DeferPaintRequest(ChunkPaintRequest request, long key)
        {
            lock (paintQueue)
            {
                // A cache miss is expected while terrain streams around a moving player. Keep its
                // retry count intact so only an actual paint failure consumes the retry budget.
                if (queuedPaintChunks.Contains(key))
                {
                    paintQueue.Enqueue(request);
                }
            }
        }

        private static void QueuePaint(int chunkX, int chunkZ)
        {
            // The queue is shared between generation and main-thread painting, so both the set and
            // queue must be accessed under the same lock.
            long key = GetChunkKey(chunkX, chunkZ);
            lock (paintQueue)
            {
                if (!queuedPaintChunks.Add(key)) return;

                paintQueue.Enqueue(new ChunkPaintRequest(chunkX, chunkZ));
            }
        }

        private static void RetryPaintRequest(ChunkPaintRequest request, long key, string reason)
        {
            bool discarded = false;
            lock (paintQueue)
            {
                // A dimension transition may have cleared the request while this chunk was being
                // inspected. In that case it must not be revived into the next dimension visit.
                if (!queuedPaintChunks.Contains(key)) return;

                if (request.RetryCount >= MaximumPaintRetries)
                {
                    queuedPaintChunks.Remove(key);
                    discarded = true;
                }
                else
                {
                    paintQueue.Enqueue(request.WithRetry());
                }
            }

            if (discarded)
            {
                Logger.Warning(
                    $"[Backrooms] Dropped paint request for chunk ({request.X}, {request.Z}) after " +
                    $"{request.RetryCount + 1} attempt(s): {reason}.");
            }
        }

        private static void ClearPendingPaintRequests()
        {
            int discardedCount;
            lock (paintQueue)
            {
                discardedCount = paintQueue.Count;
                paintQueue.Clear();
                queuedPaintChunks.Clear();
            }

            if (discardedCount > 0)
            {
                Logger.Info($"[Backrooms] Cleared {discardedCount} deferred paint request(s) for a dimension transition.");
            }
        }

        private static bool PaintChunk(GameManager gameManager, Chunk chunk)
        {
            if (gameManager == null) return false;

            try
            {
                // Concrete is used as the neutral structural block. Paint it first as a fallback,
                // then give floors, ceilings, doorway faces, and feature blocks their final faces.
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

                        if (IsTallEmptyTemplate(worldX, worldZ))
                        {
                            PaintTallEmptyColumn(gameManager, worldX, worldZ);
                            continue;
                        }

                        // This mirrors the normal-column raw generation order. Keeping raw and
                        // paint passes structurally parallel makes it easier to add new templates.
                        int floorY = GetFloorY(worldX, worldZ);
                        PaintBlockFace(gameManager, worldX, floorY, worldZ, BlockFace.Top, FloorPaintId);
                        int splitLevelSlopeY;
                        BlockFace splitLevelRiseDirection;
                        if (TryGetSplitLevelSlope(worldX, worldZ, out splitLevelSlopeY, out splitLevelRiseDirection))
                        {
                            PaintBlockAllFaces(gameManager, worldX, splitLevelSlopeY, worldZ, FloorPaintId);
                        }

                        PaintRetainingWalls(gameManager, worldX, worldZ, floorY);
                        if (IsLowerChamberCell(worldX, worldZ))
                        {
                            PaintLowerChamberColumn(gameManager, worldX, worldZ);
                        }
                        else if (IsBasementCorridor(worldX, worldZ))
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
                        else
                        {
                            int architecture = IsPit(worldX, worldZ)
                                ? ArchitectureNone
                                : GetArchitectureKind(worldX, worldZ);
                            if (architecture != ArchitectureNone)
                            {
                                if (architecture == ArchitectureHalfWall)
                                {
                                    PaintBlockAllFaces(gameManager, worldX, floorY + 1, worldZ, WallPaintId);
                                }

                                PaintBlockFace(gameManager, worldX, floorY + 1, worldZ, BlockFace.Top, FloorPaintId);
                            }
                        }

                        for (int y = ceilingStartY; y <= ceilingTopY; y++)
                        {
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
            // Some raw blocks can be shared by multiple roles (floor, wall, stair, or ceiling).
            // Paint every structural concrete block first so any unhandled face remains intentional.
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

        private bool TryResolvePalette()
        {
            if (paletteResolved) return true;
            if (!TryResolveRequiredBlocks(RequiredBlockNames)) return false;

            // Concrete is deliberately reused for both generic floor and generic wall geometry;
            // deferred paint supplies the Backrooms' distinct floor/wall/ceiling appearance.
            floorBlock = GetRequiredBlock("concreteMaster");
            wallBlock = floorBlock;
            counterBlock = GetRequiredBlock("woodMaster");
            ceilingLightBlock = GetRequiredBlock("lightPanelLEDWhite");
            basementLightBlock = ceilingLightBlock;
            officeChairBlock = GetRequiredBlock("officeChair01");
            computerBlock = GetRequiredBlock("decoComputerDeskTopPC");
            deskLampBlock = GetRequiredBlock("deskLampLight02Yellow");
            lootCrateBlock = GetRequiredBlock("cntLootCrateMoPowerElectronics");
            controlPanelBlock = GetRequiredBlock("controlPanelBase01");
            utilityCartBlock = GetRequiredBlock("cntUtilityCartEmptyGrey");
            footlockerBlock = GetRequiredBlock("cntFootlockerClosedGrey");
            pitRampBlock = GetRequiredBlock("concreteShapes:ramp");
            ambientPropBlocks = new BlockValue[AmbientPropBlockNames.Length];
            for (int index = 0; index < AmbientPropBlockNames.Length; index++)
            {
                ambientPropBlocks[index] = GetRequiredBlock(AmbientPropBlockNames[index]);
            }
            basementStationBlocks = new BlockValue[BasementStationBlockNames.Length];
            for (int index = 0; index < BasementStationBlockNames.Length; index++)
            {
                basementStationBlocks[index] = GetRequiredBlock(BasementStationBlockNames[index]);
            }
            pitDoorBlocks = new BlockValue[PitDoorBlockNames.Length];
            for (int index = 0; index < PitDoorBlockNames.Length; index++)
            {
                pitDoorBlocks[index] = GetRequiredBlock(PitDoorBlockNames[index]);
            }
            paletteResolved = true;
            return true;
        }

        private static bool IsWall(int worldX, int worldZ)
        {
            // Convert a world block into its containing macro-room and its position inside that room.
            // Use FloorDivide/PositiveModulo instead of / and % directly: C# truncates division toward
            // zero, which would otherwise make the negative side of an infinite world asymmetric.
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            // The spawn zone is intentionally clear even though macro-room (0, 0) uses the regular
            // two-storey template. It gives the entry position a predictable, obstruction-free cell.
            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return false;

            // Outer doorways are calculated from the shared edge, not from the current room alone.
            // West/north use the macro on the other side of the edge; east/south use this macro.
            // That ownership rule guarantees both rooms carve exactly the same doorway at an edge.
            if (localX == 0 && !IsDoorwayOnWestEdge(macroX, macroZ, localZ)) return true;
            if (localX == MacroSize - 1 && !IsDoorwayOnEastEdge(macroX, macroZ, localZ)) return true;
            if (localZ == 0 && !IsDoorwayOnNorthEdge(macroX, macroZ, localX)) return true;
            if (localZ == MacroSize - 1 && !IsDoorwayOnSouthEdge(macroX, macroZ, localX)) return true;

            int template = GetTemplate(macroX, macroZ);
            if (template == 6 && IsCrossroadsSupportPillar(macroX, macroZ, localX, localZ)) return true;
            return IsInteriorWall(template, localX, localZ);
        }

        private static int GetCeilingStartY(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);

            // A couple of templates include lower ceiling pockets. This changes only the ceiling
            // stack; the floor and wall helpers still use the normal coordinate system.
            bool lowCeiling = template == 1 && localX >= 5 && localX <= 12 && localZ >= 20 && localZ <= 27;
            lowCeiling |= template == 3 && localX >= 19 && localX <= 26 && localZ >= 5 && localZ <= 12;
            return lowCeiling ? CeilingY - 2 : CeilingY;
        }

        private static bool IsInteriorWall(int template, int localX, int localZ)
        {
            // Template IDs are a compact grammar for ordinary macro-rooms:
            // 0 is a central room; 1 and 2 are paired/segmented rooms; 3 is a cross; 4 is a
            // multi-room layout; 6 is a pillar hall; 11 is an inner-box loop; 12 is a staggered
            // gallery; and 13 is a four-room cluster. IDs 5, 7, 10, 14, and 15 are special
            // vertical templates, while 8, 9, and 16 use no interior walls.
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
                    return IsCrossroadsCorePillar(localX, localZ);
                case 7:
                    return false;
                case 8:
                case 9:
                case 10:
                case 14:
                case 15:
                case 16:
                    return false;
                case 11:
                    return IsRoomOutline(
                        localX,
                        localZ,
                        5,
                        5,
                        26,
                        26,
                        DoorWest | DoorEast | DoorNorth | DoorSouth);
                case 12:
                    return IsStaggeredGalleryWall(localX, localZ);
                case 13:
                    return IsRoomOutline(localX, localZ, 4, 4, 12, 12, DoorEast | DoorSouth)
                        || IsRoomOutline(localX, localZ, 19, 4, 27, 12, DoorWest | DoorSouth)
                        || IsRoomOutline(localX, localZ, 4, 19, 12, 27, DoorEast | DoorNorth)
                        || IsRoomOutline(localX, localZ, 19, 19, 27, 27, DoorWest | DoorNorth);
                default:
                    return IsRoomOutline(localX, localZ, 6, 6, 12, 25, DoorNorth | DoorSouth)
                        || IsRoomOutline(localX, localZ, 19, 6, 25, 25, DoorNorth | DoorSouth)
                        || IsRoomOutline(localX, localZ, 12, 13, 19, 18, DoorWest | DoorEast);
            }
        }

        private static bool IsCrossroadsCorePillar(int localX, int localZ)
        {
            return IsInRectangle(localX, localZ, 12, 12, 19, 19);
        }

        private static bool IsCrossroadsSupportPillar(int macroX, int macroZ, int localX, int localZ)
        {
            // One third of pillar halls receive an additional off-center support. It keeps a
            // familiar macro from reading identically without affecting exterior door routes.
            if (PositiveModulo(Hash(macroX, macroZ, CrossroadsSupportPillarChanceSalt), 3) != 0) return false;

            switch (PositiveModulo(Hash(macroX, macroZ, CrossroadsSupportPillarLocationSalt), 4))
            {
                case 0:
                    return IsInRectangle(localX, localZ, 4, 4, 7, 7);
                case 1:
                    return IsInRectangle(localX, localZ, 24, 4, 27, 7);
                case 2:
                    return IsInRectangle(localX, localZ, 4, 24, 7, 27);
                default:
                    return IsInRectangle(localX, localZ, 24, 24, 27, 27);
            }
        }

        private static bool IsRoomOutline(int x, int z, int minX, int minZ, int maxX, int maxZ, int doorwayMask)
        {
            // Make a rectangular wall, then punch its requested doorway openings through it. This
            // simple composition is more maintainable than baking doorway conditions into every
            // template definition.
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
            // A band is a thicker divider than a rectangle outline. Its central three cells become
            // a doorway, using the same DoorHalfWidth convention as every other opening.
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

        private static bool IsStaggeredGalleryWall(int localX, int localZ)
        {
            // Two offset partitions turn this room into a long gallery. Their alternating openings
            // create a readable route through the room instead of a dead-end office maze.
            bool westPartition = localX == 10 && localZ >= 4 && localZ <= 27;
            if (westPartition)
            {
                return !IsAtDoorCenter(localZ, 10) && !IsAtDoorCenter(localZ, 22);
            }

            bool eastPartition = localX == 21 && localZ >= 4 && localZ <= 27;
            return eastPartition && !IsAtDoorCenter(localZ, 16);
        }

        private static bool IsStaggeredGalleryDoorFrame(int localX, int localZ)
        {
            return (localX == 10 && (IsDoorFrameSide(localZ, 10) || IsDoorFrameSide(localZ, 22)))
                || (localX == 21 && IsDoorFrameSide(localZ, 16));
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
            // Template 5 contains nine sunken pits. Returning a lower floor here causes the normal
            // generator to create retaining faces between pit and non-pit cells.
            if (IsPitStoreyTemplate(worldX, worldZ) && IsPit(worldX, worldZ)) return PitFloorY;
            if (!IsSplitLevelTemplate(worldX, worldZ)) return FloorY;

            // Template 16 is a split-level room. Two narrow bands act as ramps into a lower center.
            // Math.Min prevents those ramps from descending further than the configured depth.
            if (localX >= 12 && localX <= 19)
            {
                if (localZ >= 4 && localZ <= 8) return FloorY - Math.Min(localZ - 4, SplitLevelDepth);
                if (localZ >= 23 && localZ <= 27) return FloorY - Math.Min(27 - localZ, SplitLevelDepth);
            }

            return IsInRectangle(localX, localZ, 9, 9, 22, 22)
                ? FloorY - SplitLevelDepth
                : FloorY;
        }

        private static bool TryGetSplitLevelSlope(
            int worldX,
            int worldZ,
            out int slopeBaseY,
            out BlockFace riseDirection)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (IsSplitLevelTemplate(worldX, worldZ) && localX >= 12 && localX <= 19)
            {
                if (localZ >= 5 && localZ <= 8)
                {
                    slopeBaseY = GetFloorY(worldX, worldZ) + 1;
                    riseDirection = BlockFace.North;
                    return true;
                }

                if (localZ >= 23 && localZ <= 26)
                {
                    slopeBaseY = GetFloorY(worldX, worldZ) + 1;
                    riseDirection = BlockFace.South;
                    return true;
                }
            }

            slopeBaseY = FloorY;
            riseDirection = BlockFace.None;
            return false;
        }

        private static bool IsTwoStoreyTemplate(int worldX, int worldZ)
        {
            // Template 7 is isolated because it needs a full second floor, stairs, and independent
            // upper-storey walls instead of the normal single-ceiling column stack.
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return GetTemplate(macroX, macroZ) == 7;
        }

        private static bool IsTallEmptyTemplate(int worldX, int worldZ)
        {
            // Template 10 is a continuous two-storey-high room: no intermediate slab, ramps, or
            // interior walls, just the large empty box requested for long-range Backrooms sightlines.
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return GetTemplate(macroX, macroZ) == 10;
        }

        private static bool IsSplitLevelTemplate(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return GetTemplate(macroX, macroZ) == 16;
        }

        private static bool IsPitStoreyTemplate(int worldX, int worldZ)
        {
            // Template 5 uses varied pits, while 14 and 15 are deliberate main-floor descents.
            // All three need a separate basement stack and native ramp writer.
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);
            return template == 5 || template == 14 || template == 15;
        }

        private static bool IsBasementAccessTemplate(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);
            return template == 14 || template == 15;
        }

        private static void GenerateTwoStoreyColumn(Chunk chunk, int chunkLocalX, int chunkLocalZ, int worldX, int worldZ)
        {
            // Vertical order for a two-storey macro-room:
            // solid terrain -> lower room -> optional stairs / upper floor -> upper room -> roof.
            // Keeping that whole stack in one function avoids accidental overlaps with normal rooms.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);

            for (int y = 0; y <= FloorY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
            }

            if (IsLowerChamberCell(worldX, worldZ))
            {
                BuildLowerChamberColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
            }
            else if (IsBasementCorridor(worldX, worldZ))
            {
                BuildBasementCorridorColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
            }
            else if (TouchesBasementLowerSpace(worldX, worldZ))
            {
                PrepareBasementCorridorTerrain(chunk, chunkLocalX, chunkLocalZ);
            }

            for (int y = FloorY + 1; y <= StoryRoofTopY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, BlockValue.Air);
            }

            // Door headers vary deterministically by doorway. Adjacent wall columns query the same
            // height through TryGetLowerDoorFrameTopY, creating a continuous visible door frame.
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

            // A pair of continuous native slopes connects the lower floor to the upper floor.
            // Every non-slope column receives the upper slab at StoryFloorY instead.
            int stairSlopeY;
            BlockFace stairRiseDirection;
            bool stair = TryGetStoryStairSlope(localX, localZ, out stairSlopeY, out stairRiseDirection);
            if (stair)
            {
                for (int y = FloorY + 1; y < stairSlopeY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
                }

                chunk.SetBlockRaw(chunkLocalX, stairSlopeY, chunkLocalZ, GetSlopeBlock(stairRiseDirection));
            }
            else
            {
                chunk.SetBlockRaw(chunkLocalX, StoryFloorY, chunkLocalZ, floorBlock);
            }

            if (!lowerWall && !stair && ShouldPlaceCeilingLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(chunkLocalX, StoryFloorY - 1, chunkLocalZ, ceilingLightBlock);
            }

            if (!lowerWall && !lowerDoorway && !stair)
            {
                BlockValue lowerProp = GetPropBlock(worldX, worldZ, 7013);
                if (lowerProp.Block != null)
                {
                    chunk.SetBlockRaw(chunkLocalX, FloorY + 1, chunkLocalZ, lowerProp);
                }
            }

            // The upper floor repeats the doorway/header logic with its own salt and height range.
            // Separate salts keep upper and lower doors visually varied without non-determinism.
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

            if (!upperWall && !upperDoorway && !stair)
            {
                BlockValue upperProp = GetPropBlock(worldX, worldZ, 7027);
                if (upperProp.Block != null)
                {
                    chunk.SetBlockRaw(chunkLocalX, StoryFloorY + 1, chunkLocalZ, upperProp);
                }
            }

            for (int y = StoryCeilingY; y <= StoryRoofTopY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
            }

            if (!upperWall && ShouldPlaceCeilingLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(chunkLocalX, StoryCeilingY - 1, chunkLocalZ, ceilingLightBlock);
            }
        }

        private static void GenerateTallEmptyColumn(Chunk chunk, int chunkLocalX, int chunkLocalZ, int worldX, int worldZ)
        {
            // Keep the lower service route, but leave the room above it as one uninterrupted
            // two-storey volume from the main floor to the roof.
            for (int y = 0; y <= FloorY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
            }

            if (IsLowerChamberCell(worldX, worldZ))
            {
                BuildLowerChamberColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
            }
            else if (IsBasementCorridor(worldX, worldZ))
            {
                BuildBasementCorridorColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
            }
            else if (TouchesBasementLowerSpace(worldX, worldZ))
            {
                PrepareBasementCorridorTerrain(chunk, chunkLocalX, chunkLocalZ);
            }

            for (int y = FloorY + 1; y <= StoryRoofTopY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, BlockValue.Air);
            }

            bool doorway = IsLowerDoorway(worldX, worldZ);
            int doorTopY = doorway ? GetDoorTopY(worldX, worldZ, FloorY, StoryCeilingY, 25013) : FloorY;
            int frameTopY = FloorY;
            bool frame = !doorway && TryGetTallDoorFrameTopY(worldX, worldZ, out frameTopY);
            bool wall = IsWall(worldX, worldZ);
            if (doorway || wall)
            {
                for (int y = FloorY + 1; y < StoryCeilingY; y++)
                {
                    bool header = doorway && y > doorTopY;
                    bool framePost = wall && frame && y <= frameTopY;
                    if (header || wall)
                    {
                        chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, framePost || header ? counterBlock : wallBlock);
                    }
                }
            }

            for (int y = StoryCeilingY; y <= StoryRoofTopY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
            }

            if (!wall && !doorway && ShouldPlaceCeilingLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(chunkLocalX, StoryCeilingY - 1, chunkLocalZ, ceilingLightBlock);
            }
        }

        private static bool TryGetStoryStairSlope(
            int localX,
            int localZ,
            out int slopeBaseY,
            out BlockFace riseDirection)
        {
            // Two opposing ramps use their low edge as the current cell's base and meet the upper
            // floor at its top edge. Returning that base keeps raw and paint paths in lockstep.
            if (localX >= 12 && localX <= 19)
            {
                if (localZ >= 4 && localZ <= 11)
                {
                    slopeBaseY = FloorY + Math.Min(localZ - 3, StoreyHeight);
                    riseDirection = BlockFace.South;
                    return true;
                }

                if (localZ >= 20 && localZ <= 27)
                {
                    slopeBaseY = FloorY + Math.Min(28 - localZ, StoreyHeight);
                    riseDirection = BlockFace.North;
                    return true;
                }
            }

            slopeBaseY = FloorY;
            riseDirection = BlockFace.None;
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

            // The upper storey has a central two-cell divider with a doorway through its middle.
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

            // Follow the same edge-door ownership rule as IsWall. Interior doorways are selected
            // by the active room template after the outer-edge cases have been handled.
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

        private static bool TryGetTallDoorFrameTopY(int worldX, int worldZ, out int frameTopY)
        {
            return TryGetAdjacentDoorTopY(worldX, worldZ, FloorY, StoryCeilingY, 25013, IsLowerDoorway, out frameTopY);
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
            // Doorway cells themselves contain open air below their headers. Neighboring wall cells
            // need the adjacent doorway's top height so their side posts stop at the right elevation.
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
            // Choose a height inside the available room clearance. The hash makes the result stable
            // across reloading and generation order while the clamped variant count avoids invalid
            // door heights in short configured rooms.
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
            // Just like doorway placement, edge height belongs to one shared macro-edge. Both sides
            // therefore feed identical coordinates and salt into Hash for the same physical opening.
            if (localX == 0) return Hash(macroX - 1, macroZ, salt + 1);
            if (localX == MacroSize - 1) return Hash(macroX, macroZ, salt + 1);
            if (localZ == 0) return Hash(macroX, macroZ - 1, salt + 2);
            if (localZ == MacroSize - 1) return Hash(macroX, macroZ, salt + 2);
            return Hash(macroX, macroZ, salt + (localZ == 15 || localZ == 16 ? 3 : 4));
        }

        private static bool IsUpperStoreyEdgeDoorway(int macroX, int macroZ, int localX, int localZ)
        {
            // Upper-storey exterior exits are permitted only when the neighbor is also two-storey.
            // This prevents an upper door opening directly onto the roof of a one-storey room.
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

        private static void SetTallRoomStability(Chunk chunk, int localX, int localZ)
        {
            // A tall room's roof spans air, so mark the complete constructed stack as supported.
            if (IsBasementLowerSpace((chunk.X << 4) + localX, (chunk.Z << 4) + localZ))
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
            // Keep paint decisions in lockstep with GenerateTwoStoreyColumn. Do not add a block to
            // the raw pass without deciding which visible faces should be painted here as well.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
            if (IsLowerChamberCell(worldX, worldZ))
            {
                PaintLowerChamberColumn(gameManager, worldX, worldZ);
            }
            else if (IsBasementCorridor(worldX, worldZ))
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

            int stairSlopeY;
            BlockFace stairRiseDirection;
            if (TryGetStoryStairSlope(localX, localZ, out stairSlopeY, out stairRiseDirection))
            {
                for (int y = FloorY + 1; y < stairSlopeY; y++)
                {
                    PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                }

                PaintBlockAllFaces(gameManager, worldX, stairSlopeY, worldZ, FloorPaintId);
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
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                if (y == StoryCeilingY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Bottom, CeilingPaintId);
                }
            }
        }

        private static void PaintTallEmptyColumn(GameManager gameManager, int worldX, int worldZ)
        {
            PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
            if (IsLowerChamberCell(worldX, worldZ))
            {
                PaintLowerChamberColumn(gameManager, worldX, worldZ);
            }
            else if (IsBasementCorridor(worldX, worldZ))
            {
                PaintBasementCorridorColumn(gameManager, worldX, worldZ);
            }
            else
            {
                PaintBasementCorridorBoundary(gameManager, worldX, worldZ);
            }

            bool doorway = IsLowerDoorway(worldX, worldZ);
            int doorTopY = doorway ? GetDoorTopY(worldX, worldZ, FloorY, StoryCeilingY, 25013) : FloorY;
            int frameTopY = FloorY;
            bool frame = !doorway && TryGetTallDoorFrameTopY(worldX, worldZ, out frameTopY);
            bool wall = IsWall(worldX, worldZ);
            if (doorway || wall)
            {
                for (int y = FloorY + 1; y < StoryCeilingY; y++)
                {
                    bool header = doorway && y > doorTopY;
                    bool framePost = wall && frame && y <= frameTopY;
                    if (header || framePost)
                    {
                        PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                    }
                    else if (wall)
                    {
                        PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                    }
                }
            }

            for (int y = StoryCeilingY; y <= StoryRoofTopY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
                if (y == StoryCeilingY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Bottom, CeilingPaintId);
                }
            }
        }

        private static void GeneratePitStoreyColumn(Chunk chunk, int chunkLocalX, int chunkLocalZ, int worldX, int worldZ)
        {
            // Template 5 combines three vertical spaces in one macro-room: a lower pit floor, the
            // main floor around it, and the normal upper Backrooms room above the main floor.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            bool pit = IsPit(worldX, worldZ);
            for (int y = 0; y <= PitFloorY; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
            }

            for (int y = PitFloorY + 1; y <= CeilingY + 1; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, BlockValue.Air);
            }

            // A native pit door belongs to the lower service route, not the main-floor rim.
            // Keep that route at the pit-floor elevation on the non-pit side of the cross.
            if (IsPitLowerCorridor(worldX, worldZ))
            {
                BuildBasementCorridorColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
            }

            // The basement corridor crosses every macro-room. On this template, leave an opening in
            // the otherwise solid lower outer wall so that cross remains traversable from the pit.
            bool corridorOpening = IsPitLowerStoreyWall(localX, localZ) && IsBasementCorridor(worldX, worldZ);
            if (IsPitLowerStoreyWall(localX, localZ))
            {
                for (int y = PitFloorY; y < FloorY; y++)
                {
                    if (corridorOpening && y < BasementCorridorCeilingY) continue;

                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                }
            }

            // A compact stair exits the central pit. Outside pit cells receive a main-floor slab;
            // access pits extend that slab into an upper landing that reaches the outer rim.
            int stairTopY;
            bool pitStair = TryGetPitStairTopY(localX, localZ, out stairTopY);
            bool basementAccessLanding = IsBasementAccessLanding(worldX, worldZ);
            bool pitCorridorDoorway = IsPitCorridorDoorway(worldX, worldZ);
            if (pitStair)
            {
                for (int y = PitFloorY + 1; y <= stairTopY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, floorBlock);
                }

                if (stairTopY < FloorY)
                {
                    chunk.SetBlockRaw(chunkLocalX, stairTopY + 1, chunkLocalZ, GetSlopeBlock(BlockFace.South));
                }
            }
            else if (!pit || basementAccessLanding)
            {
                if (IsPitRetainingWall(worldX, worldZ))
                {
                    if (pitCorridorDoorway)
                    {
                        BuildPitCorridorDoorwayColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ);
                    }
                    else
                    {
                        for (int y = PitInteriorWallBottomY; y < FloorY; y++)
                        {
                            chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                        }
                    }
                }

                chunk.SetBlockRaw(chunkLocalX, FloorY, chunkLocalZ, floorBlock);
            }

            // Access-pit macros reserve the entire lower level for traversal. The varied pit macro
            // still receives one deterministic basement feature room and small desk vignette.
            bool hasBasementFeature = !IsBasementAccessTemplate(worldX, worldZ);
            bool basementFeatureDoorway = hasBasementFeature && !pitStair && IsBasementFeatureDoorway(worldX, worldZ);
            bool basementFeatureWall = hasBasementFeature && !pitStair && IsBasementFeatureWall(worldX, worldZ);
            if (basementFeatureDoorway)
            {
                for (int y = BasementDoorTopY + 1; y < FloorY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, counterBlock);
                }
            }
            else if (basementFeatureWall)
            {
                for (int y = PitInteriorWallBottomY; y < FloorY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
                }
            }
            else if (hasBasementFeature && !pitStair)
            {
                BlockValue basementFeature = GetBasementFeatureBlock(worldX, worldZ);
                if (basementFeature.Block != null)
                {
                    chunk.SetBlockRaw(chunkLocalX, PitFloorY + 1, chunkLocalZ, basementFeature);
                }
            }

            if (IsPitSupportPillar(worldX, worldZ))
            {
                for (int y = PitInteriorWallBottomY; y < CeilingY; y++)
                {
                    chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
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

            if (!upperWall && !pitStair && !pit)
            {
                BlockValue upperProp = GetPropBlock(worldX, worldZ, 7039);
                if (upperProp.Block != null)
                {
                    chunk.SetBlockRaw(chunkLocalX, FloorY + 1, chunkLocalZ, upperProp);
                }
            }

            for (int y = CeilingY; y <= CeilingY + 1; y++)
            {
                chunk.SetBlockRaw(chunkLocalX, y, chunkLocalZ, wallBlock);
            }

            if (!upperWall && ShouldPlaceCeilingLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(chunkLocalX, CeilingY - 1, chunkLocalZ, ceilingLightBlock);
            }
        }

        private static bool IsPitLowerStoreyWall(int localX, int localZ)
        {
            return localX == 0 || localX == MacroSize - 1 || localZ == 0 || localZ == MacroSize - 1;
        }

        private static bool IsBasementCorridor(int worldX, int worldZ)
        {
            // Every macro keeps the same four edge-to-edge lanes so lower traversal never breaks
            // at a macro boundary. The interior widens into a deterministic threshold or alcove
            // layout, avoiding the visibly repeated cross at the center of every room.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (localX >= 14 && localX <= 17 || localZ >= 14 && localZ <= 17) return true;

            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            switch (PositiveModulo(Hash(macroX, macroZ, BasementJunctionVariantSalt), 4))
            {
                case 1:
                    // A broad, stable transition room inspired by Level 0's archway spaces.
                    return IsInRectangle(localX, localZ, 10, 10, 21, 21);
                case 2:
                    // Opposed side alcoves turn the horizontal junction into a wide service bay.
                    return IsInRectangle(localX, localZ, 9, 12, 13, 19)
                        || IsInRectangle(localX, localZ, 18, 12, 22, 19);
                case 3:
                    // The rotated treatment makes a north-south threshold instead of another cross.
                    return IsInRectangle(localX, localZ, 12, 9, 19, 13)
                        || IsInRectangle(localX, localZ, 12, 18, 19, 22);
                default:
                    return false;
            }
        }

        private static bool IsPitLowerCorridor(int worldX, int worldZ)
        {
            return !IsPit(worldX, worldZ) && IsBasementCorridor(worldX, worldZ);
        }

        private static bool TouchesBasementCorridor(int worldX, int worldZ)
        {
            return IsBasementCorridor(worldX, worldZ - 1)
                || IsBasementCorridor(worldX + 1, worldZ)
                || IsBasementCorridor(worldX, worldZ + 1)
                || IsBasementCorridor(worldX - 1, worldZ);
        }

        private static void PrepareBasementCorridorTerrain(Chunk chunk, int localX, int localZ)
        {
            // Corridor walls are direct blocks. Leaving terrain density here makes the marching-
            // cubes pass create an indestructible rock surface over the opening or wall face.
            SetTerrainHeights(chunk, localX, localZ, Math.Max(0, PitFloorY - 1));
            for (int y = PitFloorY; y <= FloorY; y++)
            {
                chunk.SetDensity(localX, y, localZ, MarchingCubes.DensityAir);
            }
        }

        private static void BuildBasementCorridorColumn(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            BuildBasementLowerSpaceColumn(chunk, localX, localZ);
            PlaceBasementCorridorDetails(chunk, localX, localZ, worldX, worldZ);
        }

        private static void BuildBasementLowerSpaceColumn(Chunk chunk, int localX, int localZ)
        {
            // Lower corridors and chambers share one vertical contract: a pit-floor slab, clear
            // travel space, and an intact main-floor ceiling above them.
            PrepareBasementCorridorTerrain(chunk, localX, localZ);
            RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
        }

        private static void RestoreBasementLowerSpaceColumn(Chunk chunk, int localX, int localZ)
        {
            // Post-stability restoration deliberately reasserts the visible floor before any native
            // door is written. A composite root must never inherit a partially reset lower column.
            chunk.SetBlockRaw(localX, PitFloorY, localZ, floorBlock);
            for (int y = PitFloorY + 1; y < BasementCorridorCeilingY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, BlockValue.Air);
            }

            for (int y = BasementCorridorCeilingY; y <= FloorY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, floorBlock);
            }
        }

        private static bool IsBasementPassageColumn(int worldX, int worldZ)
        {
            if (!IsBasementCorridor(worldX, worldZ)) return false;
            if (!IsPitStoreyTemplate(worldX, worldZ)) return true;

            return !IsPit(worldX, worldZ) && !IsPitRetainingWall(worldX, worldZ);
        }

        private static void PlaceBasementCorridorDetails(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            if (!IsBasementPassageColumn(worldX, worldZ)) return;

            if (ShouldPlaceBasementLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(localX, BasementCorridorCeilingY - 1, localZ, basementLightBlock);
            }

            BlockValue station = GetBasementStationBlock(worldX, worldZ);
            if (station.Block != null)
            {
                chunk.SetBlockRaw(localX, PitFloorY + 1, localZ, station);
            }
        }

        private static bool ShouldPlaceBasementLight(int worldX, int worldZ)
        {
            if (!IsBasementPassageColumn(worldX, worldZ)) return false;

            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int offset = PositiveModulo(Hash(macroX, macroZ, BasementLightSalt), configuredBasementLightSpacing);
            bool verticalFixture = localX == 15 && PositiveModulo(localZ - offset, configuredBasementLightSpacing) == 0;
            bool horizontalFixture = localZ == 15 && PositiveModulo(localX - offset, configuredBasementLightSpacing) == 0;
            return verticalFixture || horizontalFixture;
        }

        private static BlockValue GetBasementStationBlock(int worldX, int worldZ)
        {
            if (!IsBasementPassageColumn(worldX, worldZ)) return default(BlockValue);

            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            switch (PositiveModulo(Hash(macroX, macroZ, BasementStationLocationSalt), 4))
            {
                case 0:
                    if (localX != 14 || localZ != 8) return default(BlockValue);
                    break;
                case 1:
                    if (localX != 17 || localZ != 23) return default(BlockValue);
                    break;
                case 2:
                    if (localX != 8 || localZ != 14) return default(BlockValue);
                    break;
                default:
                    if (localX != 23 || localZ != 17) return default(BlockValue);
                    break;
            }

            return basementStationBlocks[PositiveModulo(
                Hash(macroX, macroZ, BasementStationStyleSalt),
                basementStationBlocks.Length)];
        }

        private static bool IsBasementLowerSpace(int worldX, int worldZ)
        {
            return IsBasementCorridor(worldX, worldZ) || IsLowerChamberCell(worldX, worldZ);
        }

        private static bool TouchesBasementLowerSpace(int worldX, int worldZ)
        {
            return IsBasementLowerSpace(worldX, worldZ - 1)
                || IsBasementLowerSpace(worldX + 1, worldZ)
                || IsBasementLowerSpace(worldX, worldZ + 1)
                || IsBasementLowerSpace(worldX - 1, worldZ);
        }

        private static bool IsLowerChamberCell(int worldX, int worldZ)
        {
            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return IsInRectangle(localX, localZ, minX, minZ, maxX, maxZ);
        }

        private static bool TryGetLowerChamberBounds(
            int worldX,
            int worldZ,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);
            if (template == 5 || template == 14 || template == 15 || template == 16)
            {
                minX = 0;
                minZ = 0;
                maxX = 0;
                maxZ = 0;
                return false;
            }

            switch (PositiveModulo(Hash(macroX, macroZ, LowerChamberVariantSalt), 4))
            {
                case 0:
                    minX = 4;
                    minZ = 4;
                    maxX = 13;
                    maxZ = 12;
                    break;
                case 1:
                    minX = 18;
                    minZ = 4;
                    maxX = 27;
                    maxZ = 12;
                    break;
                case 2:
                    minX = 4;
                    minZ = 19;
                    maxX = 13;
                    maxZ = 27;
                    break;
                default:
                    minX = 18;
                    minZ = 19;
                    maxX = 27;
                    maxZ = 27;
                    break;
            }

            return true;
        }

        private static bool IsLowerChamberBoundary(int worldX, int worldZ)
        {
            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return IsInRectangle(localX, localZ, minX, minZ, maxX, maxZ)
                && (localX == minX || localX == maxX || localZ == minZ || localZ == maxZ);
        }

        private static bool IsLowerChamberEntrance(int worldX, int worldZ)
        {
            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return false;

            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int centerZ = (minZ + maxZ) / 2;
            int variant = PositiveModulo(Hash(macroX, macroZ, LowerChamberVariantSalt), 4);
            return localZ == centerZ && (variant == 0 || variant == 2 ? localX == maxX : localX == minX);
        }

        private static BlockFace GetLowerChamberDoorFacing(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int variant = PositiveModulo(Hash(macroX, macroZ, LowerChamberVariantSalt), 4);
            return variant == 0 || variant == 2 ? BlockFace.West : BlockFace.East;
        }

        private static int GetLowerChamberAccess(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int roll = PositiveModulo(Hash(macroX, macroZ, LowerChamberAccessSalt), 4);
            return roll == 3
                ? LowerChamberAccessSealed
                : roll == 2 ? LowerChamberAccessGated : LowerChamberAccessOpen;
        }

        private static bool UsesLowerChamberDivider(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return PositiveModulo(Hash(macroX, macroZ, LowerChamberLayoutSalt), 3) == 0;
        }

        private static bool IsLowerChamberDivider(int worldX, int worldZ)
        {
            if (!UsesLowerChamberDivider(worldX, worldZ)) return false;

            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return localX == (minX + maxX) / 2 && localZ > minZ && localZ < maxZ;
        }

        private static bool IsLowerChamberDividerDoor(int worldX, int worldZ)
        {
            if (!IsLowerChamberDivider(worldX, worldZ)) return false;

            int minX;
            int minZ;
            int maxX;
            int maxZ;
            TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ);
            return PositiveModulo(worldZ, MacroSize) == (minZ + maxZ) / 2;
        }

        private static void BuildLowerChamberColumn(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            BuildBasementLowerSpaceColumn(chunk, localX, localZ);
            ApplyLowerChamberStructure(chunk, localX, localZ, worldX, worldZ);
        }

        private static void RestoreLowerChamberColumn(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
            ApplyLowerChamberStructure(chunk, localX, localZ, worldX, worldZ);
        }

        private static void ApplyLowerChamberStructure(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            if (IsLowerChamberBoundary(worldX, worldZ))
            {
                if (IsLowerChamberEntrance(worldX, worldZ))
                {
                    int access = GetLowerChamberAccess(worldX, worldZ);
                    if (access == LowerChamberAccessSealed)
                    {
                        BuildLowerChamberWall(chunk, localX, localZ);
                    }
                    else if (access == LowerChamberAccessGated)
                    {
                        BuildLowerChamberDoor(chunk, localX, localZ, worldX, worldZ);
                    }
                    else
                    {
                        BuildLowerChamberOpening(chunk, localX, localZ);
                    }
                }
                else
                {
                    BuildLowerChamberWall(chunk, localX, localZ);
                }

                return;
            }

            if (IsLowerChamberDivider(worldX, worldZ))
            {
                if (IsLowerChamberDividerDoor(worldX, worldZ))
                {
                    BuildLowerChamberDoor(chunk, localX, localZ, worldX, worldZ);
                }
                else
                {
                    BuildLowerChamberWall(chunk, localX, localZ);
                }

                return;
            }

            if (ShouldPlaceLowerChamberLight(worldX, worldZ))
            {
                chunk.SetBlockRaw(localX, BasementCorridorCeilingY - 1, localZ, basementLightBlock);
            }

            BlockValue prop = GetLowerChamberPropBlock(worldX, worldZ);
            if (prop.Block != null)
            {
                chunk.SetBlockRaw(localX, PitFloorY + 1, localZ, prop);
            }
        }

        private static void BuildLowerChamberWall(Chunk chunk, int localX, int localZ)
        {
            for (int y = PitFloorY + 1; y < BasementCorridorCeilingY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, wallBlock);
            }
        }

        private static void BuildLowerChamberOpening(Chunk chunk, int localX, int localZ)
        {
            for (int y = PitFloorY + PitStairDoorHeight + 1; y < BasementCorridorCeilingY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, wallBlock);
            }
        }

        private static void BuildLowerChamberDoor(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            BlockFace facing = GetLowerChamberDoorFacing(worldX, worldZ);
            BuildNativeLowerDoorwayColumn(chunk, localX, localZ, worldX, worldZ, facing);
        }

        private static bool ShouldPlaceLowerChamberLight(int worldX, int worldZ)
        {
            if (IsLowerChamberBoundary(worldX, worldZ) || IsLowerChamberDivider(worldX, worldZ)) return false;

            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            return PositiveModulo(localX - minX - 2, LowerChamberLightSpacing) == 0
                && PositiveModulo(localZ - minZ - 2, LowerChamberLightSpacing) == 0;
        }

        private static BlockValue GetLowerChamberPropBlock(int worldX, int worldZ)
        {
            if (IsLowerChamberBoundary(worldX, worldZ) || IsLowerChamberDivider(worldX, worldZ)) return default(BlockValue);

            int minX;
            int minZ;
            int maxX;
            int maxZ;
            if (!TryGetLowerChamberBounds(worldX, worldZ, out minX, out minZ, out maxX, out maxZ)) return default(BlockValue);

            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int variant = PositiveModulo(Hash(macroX, macroZ, LowerChamberVariantSalt), 4);
            int targetX = variant == 0 || variant == 2 ? minX + 2 : maxX - 2;
            int targetZ = PositiveModulo(Hash(macroX, macroZ, LowerChamberPropSalt), 2) == 0 ? minZ + 2 : maxZ - 2;
            if (localX != targetX || localZ != targetZ) return default(BlockValue);

            switch (PositiveModulo(Hash(macroX, macroZ, LowerChamberPropSalt + 1), 4))
            {
                case 0:
                    return controlPanelBlock;
                case 1:
                    return utilityCartBlock;
                case 2:
                    return footlockerBlock;
                default:
                    return lootCrateBlock;
            }
        }

        private static void SetBasementCorridorStability(Chunk chunk, int localX, int localZ)
        {
            // The corridor ceiling supports the main-floor construction above this carved void.
            for (int y = PitFloorY; y <= FloorY; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        private static void StitchLowerLevelSeams(Chunk chunk)
        {
            // Stability reset can discard direct wall blocks next to carved lower spaces. Restore a
            // continuous lower-wall footing after all corridor/chamber writers have run; this closes
            // visible T-junction seams without placing blocks in a traversable lower-space column.
            for (int localX = 0; localX < 16; localX++)
            {
                int worldX = (chunk.X << 4) + localX;
                for (int localZ = 0; localZ < 16; localZ++)
                {
                    int worldZ = (chunk.Z << 4) + localZ;
                    if (IsPitStoreyTemplate(worldX, worldZ) ||
                        IsBasementLowerSpace(worldX, worldZ) ||
                        !TouchesBasementLowerSpace(worldX, worldZ)) continue;

                    for (int y = PitInteriorWallBottomY; y < BasementCorridorCeilingY; y++)
                    {
                        chunk.SetBlockRaw(localX, y, localZ, wallBlock);
                        chunk.SetStability(localX, y, localZ, 15);
                    }
                }
            }
        }

        private static void PaintBasementCorridorColumn(GameManager gameManager, int worldX, int worldZ)
        {
            // The same concrete stack acts as corridor wall, ceiling, and upper floor. Painting
            // individual top/bottom faces distinguishes those materials without extra block types.
            for (int y = BasementCorridorCeilingY; y <= FloorY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
            }

            PaintBlockFace(gameManager, worldX, PitFloorY, worldZ, BlockFace.Top, FloorPaintId);
            PaintBlockFace(gameManager, worldX, BasementCorridorCeilingY, worldZ, BlockFace.Bottom, CeilingPaintId);
            PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
        }

        private static void PaintLowerChamberColumn(GameManager gameManager, int worldX, int worldZ)
        {
            PaintBasementCorridorColumn(gameManager, worldX, worldZ);
            if (IsLowerChamberBoundary(worldX, worldZ))
            {
                if (IsLowerChamberEntrance(worldX, worldZ))
                {
                    int access = GetLowerChamberAccess(worldX, worldZ);
                    if (access == LowerChamberAccessSealed)
                    {
                        PaintLowerChamberWall(gameManager, worldX, worldZ);
                    }
                    else
                    {
                        PaintLowerChamberOpening(gameManager, worldX, worldZ);
                    }
                }
                else
                {
                    PaintLowerChamberWall(gameManager, worldX, worldZ);
                }

                return;
            }

            if (IsLowerChamberDivider(worldX, worldZ))
            {
                if (IsLowerChamberDividerDoor(worldX, worldZ))
                {
                    PaintLowerChamberOpening(gameManager, worldX, worldZ);
                }
                else
                {
                    PaintLowerChamberWall(gameManager, worldX, worldZ);
                }
            }
        }

        private static void PaintLowerChamberWall(GameManager gameManager, int worldX, int worldZ)
        {
            for (int y = PitFloorY + 1; y < BasementCorridorCeilingY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
            }
        }

        private static void PaintLowerChamberOpening(GameManager gameManager, int worldX, int worldZ)
        {
            for (int y = PitFloorY + PitStairDoorHeight + 1; y < BasementCorridorCeilingY; y++)
            {
                PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
            }
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
            // A non-corridor cell beside a corridor must paint its exposed underground face; without
            // this neighbor test, the corridor would show unpainted concrete at its boundary.
            if (!IsBasementLowerSpace(neighborX, neighborZ)) return;

            for (int y = PitFloorY; y < BasementCorridorCeilingY; y++)
            {
                PaintBlockFace(gameManager, worldX, y, worldZ, face, WallPaintId);
                if (y == PitFloorY)
                {
                    PaintBlockFace(gameManager, worldX, y, worldZ, BlockFace.Top, FloorPaintId);
                }
            }
        }

        private static int GetBasementFeatureVariant(int worldX, int worldZ)
        {
            // Pick one corner feature per macro-room. Macro coordinates intentionally make every
            // column within that room agree on the same variant.
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return PositiveModulo(Hash(macroX, macroZ, 27011), 4);
        }

        private static int GetBasementFeatureStyle(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            return PositiveModulo(Hash(macroX, macroZ, 27019), BasementFeatureStyleCount);
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
            // Each pit room hides one lower service nook. Its corner and encounter style are both
            // macro-coordinate decisions, so the layout survives reloads and chunk-order changes.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int corner = GetBasementFeatureVariant(worldX, worldZ);
            int anchorX = corner == 0 || corner == 2 ? 6 : 24;
            int anchorZ = corner == 0 || corner == 1 ? 6 : 24;
            int directionX = corner == 0 || corner == 2 ? 1 : -1;
            int directionZ = corner == 0 || corner == 1 ? 1 : -1;

            switch (GetBasementFeatureStyle(worldX, worldZ))
            {
                case 0:
                    if (localX == anchorX && localZ == anchorZ) return counterBlock;
                    if (localX == anchorX + directionX && localZ == anchorZ) return computerBlock;
                    if (localX == anchorX - directionX && localZ == anchorZ + directionZ) return officeChairBlock;
                    if (localX == anchorX + 2 * directionX && localZ == anchorZ + directionZ) return deskLampBlock;
                    break;
                case 1:
                    if (localX == anchorX && localZ == anchorZ) return lootCrateBlock;
                    if (localX == anchorX + directionX && localZ == anchorZ) return counterBlock;
                    if (localX == anchorX - directionX && localZ == anchorZ + directionZ) return deskLampBlock;
                    if (localX == anchorX && localZ == anchorZ + 2 * directionZ) return officeChairBlock;
                    break;
                case 2:
                    if (localX == anchorX && localZ == anchorZ) return counterBlock;
                    if (localX == anchorX + directionX && localZ == anchorZ) return computerBlock;
                    if (localX == anchorX && localZ == anchorZ + directionZ) return officeChairBlock;
                    if (localX == anchorX + 2 * directionX && localZ == anchorZ + 2 * directionZ) return deskLampBlock;
                    break;
                default:
                    if (localX == anchorX && localZ == anchorZ) return lootCrateBlock;
                    if (localX == anchorX + directionX && localZ == anchorZ) return officeChairBlock;
                    if (localX == anchorX && localZ == anchorZ + directionZ) return deskLampBlock;
                    break;
            }

            return default(BlockValue);
        }

        private static bool IsPitCorridorDoorway(int worldX, int worldZ)
        {
            if (!IsPitRetainingWall(worldX, worldZ) || !IsBasementCorridor(worldX, worldZ)) return false;

            BlockFace facing;
            if (!TryGetPitDoorFacing(worldX, worldZ, out facing)) return false;
            if (!HasPitDoorLanding(worldX, worldZ, facing)) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int tangentPosition = facing == BlockFace.North || facing == BlockFace.South ? localX : localZ;
            // A native entity door occupies one block cell. Use one stable center cell for each
            // corridor threshold; every neighboring retaining-wall cell remains solid.
            return tangentPosition == 15;
        }

        private static bool TryGetPitDoorFacing(int worldX, int worldZ, out BlockFace facing)
        {
            facing = BlockFace.None;
            int pitNeighborCount = 0;
            if (IsPit(worldX, worldZ - 1))
            {
                facing = BlockFace.South;
                pitNeighborCount++;
            }

            if (IsPit(worldX + 1, worldZ))
            {
                facing = BlockFace.West;
                pitNeighborCount++;
            }

            if (IsPit(worldX, worldZ + 1))
            {
                facing = BlockFace.North;
                pitNeighborCount++;
            }

            if (IsPit(worldX - 1, worldZ))
            {
                facing = BlockFace.East;
                pitNeighborCount++;
            }

            return pitNeighborCount == 1;
        }

        private static bool HasPitDoorLanding(int worldX, int worldZ, BlockFace facing)
        {
            int pitX = worldX;
            int pitZ = worldZ;
            switch (facing)
            {
                case BlockFace.North:
                    pitZ++;
                    break;
                case BlockFace.East:
                    pitX--;
                    break;
                case BlockFace.South:
                    pitZ--;
                    break;
                case BlockFace.West:
                    pitX++;
                    break;
                default:
                    return false;
            }

            int localX = PositiveModulo(pitX, MacroSize);
            int localZ = PositiveModulo(pitZ, MacroSize);
            int stairTopY;
            return !TryGetPitStairTopY(localX, localZ, out stairTopY)
                || stairTopY == PitFloorY;
        }

        private static void BuildPitCorridorDoorwayColumn(
            Chunk chunk,
            int chunkLocalX,
            int chunkLocalZ,
            int worldX,
            int worldZ)
        {
            BlockFace facing;
            if (!TryGetPitDoorFacing(worldX, worldZ, out facing)) return;

            BuildNativeLowerDoorwayColumn(chunk, chunkLocalX, chunkLocalZ, worldX, worldZ, facing);
        }

        private static void BuildNativeLowerDoorwayColumn(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ,
            BlockFace facing)
        {
            // Composite doors need a fully established lower doorway before their root is written.
            // Keep the floor, aperture, and header deterministic across pit and chamber writers.
            chunk.SetBlockRaw(localX, PitFloorY, localZ, floorBlock);
            chunk.SetBlockRaw(localX, NativeDoorOriginY, localZ, BlockValue.Air);
            chunk.SetBlockRaw(localX, NativeDoorOriginY + 1, localZ, BlockValue.Air);
            for (int y = NativeDoorOriginY + 2; y < BasementCorridorCeilingY; y++)
            {
                chunk.SetBlockRaw(localX, y, localZ, wallBlock);
            }

            chunk.SetBlockRaw(localX, NativeDoorOriginY, localZ, GetPitDoorBlock(worldX, worldZ, facing));
            chunk.SetStability(localX, PitFloorY, localZ, 15);
            chunk.SetStability(localX, NativeDoorOriginY, localZ, 15);
        }

        private static BlockValue GetSlopeBlock(BlockFace riseDirection)
        {
            BlockValue ramp = pitRampBlock;
            // Native ramp rotation is a 0..3 yaw index; BlockFace values above 3 flip the model.
            switch (riseDirection)
            {
                case BlockFace.North:
                    ramp.rotation = 0;
                    break;
                case BlockFace.East:
                    ramp.rotation = 3;
                    break;
                case BlockFace.South:
                    ramp.rotation = 2;
                    break;
                case BlockFace.West:
                    ramp.rotation = 1;
                    break;
            }

            return ramp;
        }

        private static BlockValue GetPitDoorBlock(int worldX, int worldZ, BlockFace facing)
        {
            int doorIndex = PositiveModulo(Hash(worldX, worldZ, PitDoorVariantSalt), pitDoorBlocks.Length);
            BlockValue door = pitDoorBlocks[doorIndex];
            door.rotation = (byte)facing;
            return door;
        }

        private static bool TryGetPitStairTopY(int localX, int localZ, out int stairTopY)
        {
            // The first stair cell shares the pit floor and the final cell reaches the main floor.
            // Returning the top block makes the raw generation and paint paths share its geometry.
            if (localX >= 13 && localX <= 18 && localZ >= 13 && localZ <= 18)
            {
                stairTopY = PitFloorY + localZ - 13;
                return true;
            }

            stairTopY = PitFloorY;
            return false;
        }

        private static void SetPitStoreyStability(Chunk chunk, int localX, int localZ)
        {
            // Pit floors, stairs, the upper main floor, and the ceiling all span carved space.
            for (int y = PitFloorY + 1; y <= CeilingY + 1; y++)
            {
                chunk.SetStability(localX, y, localZ, 15);
            }
        }

        private static void SealPitRetainingWallFooting(
            Chunk chunk,
            int localX,
            int localZ,
            int worldX,
            int worldZ)
        {
            if (IsPit(worldX, worldZ) ||
                !IsPitRetainingWall(worldX, worldZ)) return;

            if (IsPitCorridorDoorway(worldX, worldZ))
            {
                // Doors are not terrain-backed, so the stability reset removes their raw blocks.
                // Restore the full lower-space column before reapplying its composite doorway.
                RestoreBasementLowerSpaceColumn(chunk, localX, localZ);
                BuildPitCorridorDoorwayColumn(chunk, localX, localZ, worldX, worldZ);
                return;
            }

            // ResetStabilityToBottomMost runs after raw terrain writes. Reapply the first wall
            // block afterward so a rim never keeps an air seam below its visible retaining wall.
            int footingY = PitInteriorWallBottomY;
            chunk.SetBlockRaw(localX, footingY, localZ, wallBlock);
            chunk.SetStability(localX, footingY, localZ, 15);
        }

        private static void PaintPitStoreyColumn(GameManager gameManager, int worldX, int worldZ)
        {
            // Paint counterpart of GeneratePitStoreyColumn. It intentionally follows the same order:
            // lower floor/walls, stairs or main slab, basement feature, then upper room and ceiling.
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            PaintPitFloorBlock(gameManager, worldX, worldZ);

            if (IsPitLowerCorridor(worldX, worldZ))
            {
                PaintBasementCorridorColumn(gameManager, worldX, worldZ);
            }

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

                if (stairTopY < FloorY)
                {
                    PaintBlockAllFaces(gameManager, worldX, stairTopY + 1, worldZ, FloorPaintId);
                }
                else
                {
                    PaintBlockFace(gameManager, worldX, stairTopY, worldZ, BlockFace.Top, FloorPaintId);
                }
            }
            else if (!IsPit(worldX, worldZ) || IsBasementAccessLanding(worldX, worldZ))
            {
                PaintBlockAllFaces(gameManager, worldX, FloorY, worldZ, WallPaintId);
                PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Top, FloorPaintId);
                PaintBlockFace(gameManager, worldX, FloorY, worldZ, BlockFace.Bottom, CeilingPaintId);
                if (!IsPitCorridorDoorway(worldX, worldZ))
                {
                    PaintPitUpperFloorEdges(gameManager, worldX, worldZ);
                }
            }

            bool hasBasementFeature = !IsBasementAccessTemplate(worldX, worldZ);
            bool basementFeatureDoorway = hasBasementFeature && !pitStair && IsBasementFeatureDoorway(worldX, worldZ);
            bool basementFeatureWall = hasBasementFeature && !pitStair && IsBasementFeatureWall(worldX, worldZ);
            if (basementFeatureDoorway)
            {
                for (int y = BasementDoorTopY + 1; y < FloorY; y++)
                {
                    PaintDoorFrameFaces(gameManager, worldX, y, worldZ);
                }
            }
            else if (basementFeatureWall)
            {
                for (int y = PitInteriorWallBottomY; y < FloorY; y++)
                {
                    PaintBasementWallBlock(gameManager, worldX, y, worldZ);
                }
            }

            if (IsPitSupportPillar(worldX, worldZ))
            {
                for (int y = PitInteriorWallBottomY; y < CeilingY; y++)
                {
                    PaintBlockAllFaces(gameManager, worldX, y, worldZ, WallPaintId);
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
            // Only create a vertical painted face where this upper-floor cell actually borders a pit.
            // This is the texture equivalent of a retaining wall, not an additional terrain block.
            if (!IsPit(neighborX, neighborZ)) return;

            PaintBlockFace(gameManager, worldX, PitFloorY, worldZ, face, WallPaintId);
            for (int y = PitInteriorWallBottomY; y < FloorY; y++)
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
            int template = GetTemplate(macroX, macroZ);
            if ((template != 5 && template != 14 && template != 15) ||
                IsEntrySpace(macroX, macroZ, localX, localZ)) return false;

            // These deliberate access macros share the pit ramp, retaining walls, and lower-door
            // machinery with template 5, but carve one large reachable floor cut instead of wells.
            if (template == 14)
            {
                return IsInRectangle(localX, localZ, 9, 9, 22, 22);
            }

            if (template == 15)
            {
                return IsInRectangle(localX, localZ, 11, 4, 20, 27);
            }

            switch (PositiveModulo(Hash(macroX, macroZ, 26003), PitLayoutCount))
            {
                case 0:
                    return IsInRectangle(localX, localZ, 3, 3, 8, 8)
                        || IsInRectangle(localX, localZ, 13, 3, 18, 8)
                        || IsInRectangle(localX, localZ, 23, 3, 28, 8)
                        || IsInRectangle(localX, localZ, 3, 13, 8, 18)
                        || IsInRectangle(localX, localZ, 13, 13, 18, 18)
                        || IsInRectangle(localX, localZ, 23, 13, 28, 18)
                        || IsInRectangle(localX, localZ, 3, 23, 8, 28)
                        || IsInRectangle(localX, localZ, 13, 23, 18, 28)
                        || IsInRectangle(localX, localZ, 23, 23, 28, 28);
                case 1:
                    return IsInRectangle(localX, localZ, 3, 4, 9, 27)
                        || IsInRectangle(localX, localZ, 13, 13, 18, 18)
                        || IsInRectangle(localX, localZ, 22, 4, 28, 27);
                case 2:
                    return IsInRectangle(localX, localZ, 13, 13, 18, 18)
                        || (IsInRectangle(localX, localZ, 3, 3, 28, 28)
                            && !IsInRectangle(localX, localZ, 12, 3, 19, 28)
                            && !IsInRectangle(localX, localZ, 3, 12, 28, 19));
                default:
                    return IsInRectangle(localX, localZ, 13, 13, 18, 18)
                        || IsInRectangle(localX, localZ, 3, 3, 11, 11)
                        || IsInRectangle(localX, localZ, 20, 3, 28, 11)
                        || IsInRectangle(localX, localZ, 3, 20, 11, 28)
                        || IsInRectangle(localX, localZ, 20, 20, 28, 28);
            }
        }

        private static bool IsBasementAccessLanding(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int template = GetTemplate(macroX, macroZ);
            if (template != 14 && template != 15) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (localX < 13 || localX > 18 || localZ < 19) return false;

            // The landing begins at the ramp's full-height final step and reaches the south rim.
            return template == 14 ? localZ <= 22 : localZ <= 27;
        }

        private static bool IsPitSupportPillar(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            if (GetTemplate(macroX, macroZ) != 5 ||
                PositiveModulo(Hash(macroX, macroZ, PitPillarChanceSalt), 3) != 0) return false;

            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            int stairTopY;
            if (!IsPit(worldX, worldZ) || TryGetPitStairTopY(localX, localZ, out stairTopY)) return false;

            int layout = PositiveModulo(Hash(macroX, macroZ, 26003), PitLayoutCount);
            int location = PositiveModulo(Hash(macroX, macroZ, PitPillarLocationSalt), 8);
            int centerX;
            int centerZ;
            switch (layout)
            {
                case 0:
                    switch (location)
                    {
                        case 0:
                            centerX = 5;
                            centerZ = 5;
                            break;
                        case 1:
                            centerX = 15;
                            centerZ = 5;
                            break;
                        case 2:
                            centerX = 25;
                            centerZ = 5;
                            break;
                        case 3:
                            centerX = 5;
                            centerZ = 15;
                            break;
                        case 4:
                            centerX = 25;
                            centerZ = 15;
                            break;
                        case 5:
                            centerX = 5;
                            centerZ = 25;
                            break;
                        case 6:
                            centerX = 15;
                            centerZ = 25;
                            break;
                        default:
                            centerX = 25;
                            centerZ = 25;
                            break;
                    }
                    break;
                case 1:
                    centerX = location % 2 == 0 ? 5 : 25;
                    centerZ = location % 4 < 2 ? 10 : 22;
                    break;
                default:
                    centerX = location % 2 == 0 ? 6 : 24;
                    centerZ = location % 4 < 2 ? 6 : 24;
                    break;
            }

            return IsInRectangle(localX, localZ, centerX - 1, centerZ - 1, centerX + 1, centerZ + 1);
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

            // Frame selection follows the same shared-edge ownership as doors. The neighboring macro
            // must make the same random decision, otherwise a doorway would have a frame on one side.
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
                case 11:
                    return IsRoomDoorFrame(
                        localX,
                        localZ,
                        5,
                        5,
                        26,
                        26,
                        DoorWest | DoorEast | DoorNorth | DoorSouth);
                case 12:
                    return IsStaggeredGalleryDoorFrame(localX, localZ);
                case 13:
                    return IsRoomDoorFrame(localX, localZ, 4, 4, 12, 12, DoorEast | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 19, 4, 27, 12, DoorWest | DoorSouth)
                        || IsRoomDoorFrame(localX, localZ, 4, 19, 12, 27, DoorEast | DoorNorth)
                        || IsRoomDoorFrame(localX, localZ, 19, 19, 27, 27, DoorWest | DoorNorth);
                default:
                    return false;
            }
        }

        private static bool IsRoomDoorFrame(int x, int z, int minX, int minZ, int maxX, int maxZ, int doorwayMask)
        {
            // A frame is one wall cell to either side of a doorway. It is deliberately separate from
            // IsRoomDoorway because the doorway itself remains air and only its posts use wood.
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
            // Two thirds of doors receive visible frames. The fixed salt makes this a stable styling
            // choice rather than a random roll each time a chunk is generated.
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

            // Each ordinary template reserves a few open rectangles for low partitions or counters.
            // Special vertical templates return no architecture because they use their own geometry.
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
                case 16:
                    architecture = false;
                    break;
                case 7:
                    architecture = false;
                    break;
                case 8:
                case 9:
                case 10:
                case 14:
                case 15:
                    architecture = false;
                    break;
                case 11:
                case 12:
                case 13:
                    architecture = false;
                    break;
                default:
                    architecture = IsInRectangle(localX, localZ, 4, 11, 10, 15)
                        || IsInRectangle(localX, localZ, 21, 16, 27, 20);
                    break;
            }

            // The macro-level variant selects one of two block treatments for all its architecture
            // rectangles, giving visual variety without any stateful random-number generator.
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
            // The west doorway belongs to the macro immediately to the west. See GetVerticalDoorCenter
            // for the matching east-edge calculation used by that neighboring room.
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
            // Restrict door centers to the interior of an edge so corners stay structurally solid.
            return 5 + PositiveModulo(Hash(leftMacroX, macroZ, 1009), MacroSize - 10);
        }

        private static int GetHorizontalDoorCenter(int macroX, int northMacroZ)
        {
            return 5 + PositiveModulo(Hash(macroX, northMacroZ, 2017), MacroSize - 10);
        }

        private static int GetTemplate(int macroX, int macroZ)
        {
            // Pin the spawn macro-room to template 7 so the entry coordinates always land in the
            // known two-storey structure. All other macro-rooms choose one of seventeen stable templates.
            if (macroX == 0 && macroZ == 0) return 7;
            return PositiveModulo(Hash(macroX, macroZ, 3079), TemplateCount);
        }

        private static bool ShouldPlaceCeilingLight(int worldX, int worldZ)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            // Shift a fluorescent grid per macro-room while the hashed origin avoids an obvious
            // global seam. The spacing setting controls the grid density without altering walls.
            int offsetX = PositiveModulo(Hash(macroX, macroZ, 4001), configuredCeilingLightSpacing);
            int offsetZ = PositiveModulo(Hash(macroX, macroZ, 5003), configuredCeilingLightSpacing);

            return PositiveModulo(localX - offsetX, configuredCeilingLightSpacing) == 0
                && PositiveModulo(localZ - offsetZ, configuredCeilingLightSpacing) == 0;
        }

        private static BlockValue GetPropBlock(int worldX, int worldZ, int salt)
        {
            int macroX = FloorDivide(worldX, MacroSize);
            int macroZ = FloorDivide(worldZ, MacroSize);
            int localX = PositiveModulo(worldX, MacroSize);
            int localZ = PositiveModulo(worldZ, MacroSize);
            if (IsEntrySpace(macroX, macroZ, localX, localZ)) return default(BlockValue);
            int template = GetTemplate(macroX, macroZ);
            if (template == 8 || template == 9 || template == 10) return default(BlockValue);
            if (localX < 3 || localX > MacroSize - 4 || localZ < 3 || localZ > MacroSize - 4)
            {
                return default(BlockValue);
            }

            // Prop placement stays stable across loads while the palette makes ordinary rooms feel
            // like varied abandoned office and service spaces instead of repeating a few models.
            int propSeed = Hash(worldX, worldZ, salt);
            if (PositiveModulo(propSeed, 100) >= configuredAmbientPropDensity) return default(BlockValue);
            return ambientPropBlocks[PositiveModulo(
                Hash(worldX, worldZ, salt + 1009),
                ambientPropBlocks.Length)];
        }

        private static bool IsEntrySpace(int macroX, int macroZ, int localX, int localZ)
        {
            // Reserve a 7 x 7 clear landing area around the hard-coded entry position.
            return macroX == 0 && macroZ == 0
                && localX >= 9 && localX <= 15
                && localZ >= 9 && localZ <= 15;
        }

        private static int Hash(int first, int second, int salt)
        {
            // Small deterministic integer mixer. It has no shared state and works for negative input,
            // which is essential for terrain that must reproduce identically after unload/reload.
            // The layout seed roots every stream, while different salts keep feature decisions separate.
            unchecked
            {
                int hash = salt ^ configuredLayoutSeed;
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
            // Pack two signed 32-bit chunk coordinates into a collision-free queue key.
            return ((long)chunkX << 32) ^ (uint)chunkZ;
        }

        private static int FloorDivide(int value, int divisor)
        {
            // Mathematical floor division, unlike C# integer division for negative non-multiples.
            int quotient = value / divisor;
            return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            // Pair with FloorDivide so local coordinates always fall in [0, divisor), including west
            // and north of the world origin.
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}