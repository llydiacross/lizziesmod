using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LizziesMod
{
    public static class DimensionManager
    {
        public const string OverworldDimensionId = "Overworld";
        private const float ChunkLoadTimeoutSeconds = 15f;
        private const float ChunkCollisionTimeoutSeconds = 30f;
        private const int ProviderSearchDepth = 3;
        private const int ProviderSearchDiagnosticLimit = 24;

        private static string activeDimensionId = OverworldDimensionId;
        private static bool transitionInProgress;
        private static volatile bool regionStorageRebindInProgress;
        private static ChunkProviderGenerateWorld storageBindingProvider;
        private static volatile RegionFileManager activeGeneratedRegionFileManager;
        private static readonly object chunkGenerationGate = new object();
        private static readonly Dictionary<string, RegionStorageBinding> regionStorageBindings =
            new Dictionary<string, RegionStorageBinding>(StringComparer.OrdinalIgnoreCase);
            private static string loggedWorldBoundaryOverrideDimensionId = "";

        public static string ActiveDimensionId
        {
            get { return activeDimensionId; }
        }

        public static bool IsOverworld(string dimensionId)
        {
            return OverworldDimensionId.Equals(dimensionId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTransitionInProgress
        {
            get { return transitionInProgress; }
        }

        public static bool TryEnterChunkGeneration()
        {
            System.Threading.Monitor.Enter(chunkGenerationGate);
            if (!regionStorageRebindInProgress) return true;

            System.Threading.Monitor.Exit(chunkGenerationGate);
            return false;
        }

        public static void ExitChunkGeneration()
        {
            System.Threading.Monitor.Exit(chunkGenerationGate);
        }

        public static bool IsActiveGenerator(string generatorId)
        {
            return !IsOverworld(activeDimensionId) && DimensionRegistry.UsesGenerator(activeDimensionId, generatorId);
        }

            public static bool IsWorldBoundaryDisabledForActiveDimension()
            {
                if (IsOverworld(activeDimensionId)) return false;

                DimensionDefinition definition;
                if (!DimensionRegistry.TryGet(activeDimensionId, out definition) || !definition.DisableWorldBoundary)
                {
                    return false;
                }

                if (!loggedWorldBoundaryOverrideDimensionId.Equals(activeDimensionId, StringComparison.OrdinalIgnoreCase))
                {
                    loggedWorldBoundaryOverrideDimensionId = activeDimensionId;
                    Logger.Info($"[DimensionManager] World boundary and biome radiation are disabled for '{activeDimensionId}'.");
                }

                return true;
            }

            public static float FilterWorldBoundsPercent(float worldBoundsPercent)
            {
                return IsWorldBoundaryDisabledForActiveDimension() ? 1f : worldBoundsPercent;
            }

            public static bool FilterWorldBoundsAdjustment(bool needsBoundsAdjustment)
            {
                return IsWorldBoundaryDisabledForActiveDimension() ? false : needsBoundsAdjustment;
            }

            public static float FilterBiomeRadiation(float radiation)
            {
                return IsWorldBoundaryDisabledForActiveDimension() ? 0f : radiation;
            }

        public static bool IsProviderBoundToActiveGeneratedDimension(ChunkProviderGenerateWorld provider)
        {
            return provider != null && IsActiveGeneratedDimension() &&
                ReferenceEquals(provider.m_RegionFileManager, activeGeneratedRegionFileManager);
        }

        public static string GetPortalActivationText()
        {
            return GetDimensionActivationText(DimensionRegistry.DefaultDimensionId);
        }

        public static string GetDimensionActivationText(string dimensionId)
        {
            if (transitionInProgress) return "Dimension Transition In Progress";
            if (!IsOverworld(activeDimensionId)) return "Return to Overworld";

            DimensionDefinition definition;
            return !string.IsNullOrEmpty(dimensionId) && DimensionRegistry.TryGet(dimensionId, out definition)
                ? "Enter " + definition.DisplayName
                : "Enter Dimension";
        }

        public static bool TrySetActiveDimension(string dimensionId)
        {
            DimensionDefinition definition;
            if (!IsOverworld(dimensionId) && (!DimensionRegistry.TryGet(dimensionId, out definition) || !definition.IsSupported))
            {
                Logger.Error($"[DimensionManager] Rejected unsupported dimension '{dimensionId}'.");
                return false;
            }

            string nextDimensionId = IsOverworld(dimensionId) ? OverworldDimensionId : dimensionId;
            if (string.Equals(activeDimensionId, nextDimensionId, StringComparison.OrdinalIgnoreCase)) return true;

            string previousDimensionId = activeDimensionId;
            DimensionGeneratorRegistry.NotifyDimensionDeactivated(previousDimensionId);
            activeDimensionId = nextDimensionId;
                loggedWorldBoundaryOverrideDimensionId = "";
            Logger.Info($"[DimensionManager] Active dimension set to '{activeDimensionId}'.");
            DimensionGeneratorRegistry.NotifyDimensionActivated(activeDimensionId);
            return true;
        }

        public static bool TryStartConfiguredDimension(EntityPlayerLocal player)
        {
            return TryStartDimension(player, DimensionRegistry.DefaultDimensionId);
        }

        public static bool TryStartDimension(EntityPlayerLocal player, string dimensionId)
        {
            if (player == null) return false;

            if (!ModSettingsManager.GetSetting<bool>("LizziesMod", "ExperimentalFeatures"))
            {
                GameManager.ShowTooltip(player, "Enable Experimental Features before testing dimensions.");
                player.PlayOneShot("ui_denied");
                return false;
            }

            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null || !connectionManager.IsServer || connectionManager.ClientCount() > 0)
            {
                GameManager.ShowTooltip(player, "The save snapshot test is single-player only.");
                player.PlayOneShot("ui_denied");
                return false;
            }

            if (transitionInProgress)
            {
                GameManager.ShowTooltip(player, "A dimension transition is already in progress.");
                player.PlayOneShot("ui_denied");
                return false;
            }

            bool enteringDimension = IsOverworld(activeDimensionId);
            DimensionDefinition definition = null;
            if (enteringDimension &&
                (string.IsNullOrEmpty(dimensionId) || !DimensionRegistry.TryGet(dimensionId, out definition) || !definition.IsSupported))
            {
                string displayName = definition != null ? definition.DisplayName : dimensionId;
                GameManager.ShowTooltip(player, $"The '{displayName}' generator is not implemented yet.");
                player.PlayOneShot("ui_denied");
                return false;
            }

            string targetDimension = enteringDimension
                ? definition.Id
                : OverworldDimensionId;

            GameManager.Instance.StartCoroutine(SwitchDimension(player, player.position, targetDimension));
            return true;
        }

        private static IEnumerator SwitchDimension(EntityPlayerLocal player, Vector3 destination, string targetDimension)
        {
            string previousDimension = activeDimensionId;
            Vector3 previousPosition = player.position;
            ChunkCluster chunkCache = null;
            IChunkProvider chunkProvider = null;
            ChunkProviderRebuildContext providerRebuildContext = null;
            vp_FPController playerController = null;
            bool playerControllerWasEnabled = false;
            bool changedDimension = false;
            string error = "";

            transitionInProgress = true;
            player.Buffs.AddBuff("buffFluxTeleporting");
            GameManager.ShowTooltip(player, "Preparing dimension transition...");

            try
            {
                chunkCache = GameManager.Instance.World != null ? GameManager.Instance.World.ChunkCache : null;
                if (chunkCache == null)
                {
                    error = "The world chunk cache is unavailable.";
                }
                else
                {
                    chunkProvider = GetActiveChunkProvider(chunkCache);
                    if (chunkProvider == null)
                    {
                        error = "The active world did not expose a chunk provider. No chunks were changed.";
                    }
                    else if (!TryCreateProviderRebuildContext(chunkCache, chunkProvider, out providerRebuildContext, out error))
                    {
                        Logger.Error("[DimensionManager] " + error);
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Logger.Error($"[DimensionManager] Could not prepare the chunk provider rebuild: {exception}");
            }

            try
            {
                if (string.IsNullOrEmpty(error)) GameManager.Instance.SaveWorld();
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Logger.Error($"[DimensionManager] Could not save before the save snapshot transition: {exception}");
            }

            if (string.IsNullOrEmpty(error)) yield return null;

            if (string.IsNullOrEmpty(error))
            {
                if (IsOverworld(previousDimension) && !IsOverworld(targetDimension))
                {
                    string overworldSaveDirectory = GameIO.GetSaveGameDir();
                    DimensionDefinition targetDefinition;
                    DimensionGeneratorDefinition targetGenerator;
                    if (!DimensionRegistry.TryGet(targetDimension, out targetDefinition) ||
                        !DimensionGeneratorRegistry.TryGet(targetDefinition.GeneratorId, out targetGenerator))
                    {
                        error = "The target dimension definition is unavailable.";
                    }
                    else if (!DimensionStorage.HasSaveSnapshot(overworldSaveDirectory, targetDimension))
                    {
                        if (!ModSaveManager.BackupSaveDirectory(overworldSaveDirectory, "Before save snapshot dimension test"))
                        {
                            error = "Could not create a safety backup before the save snapshot test.";
                        }
                        else if (!(targetGenerator.SaveMode == DimensionSaveMode.Generated
                            ? DimensionStorage.TryCreateGeneratedDimensionSave(overworldSaveDirectory, targetDimension, out error)
                            : DimensionStorage.TryCreateSaveSnapshot(overworldSaveDirectory, targetDimension, out error)))
                        {
                            error = "Could not create the dimension save: " + error;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(error))
            {
                try
                {
                    playerController = player.m_vp_FPController;
                    if (playerController != null)
                    {
                        playerControllerWasEnabled = playerController.enabled;
                        playerController.enabled = false;
                    }
                    player.SetControllable(false);

                    BeginRegionStorageRebind();
                    if (!TryUnloadActiveChunks(chunkCache, out error))
                    {
                    }
                    else if (!TrySetActiveDimension(targetDimension))
                    {
                        error = "The target dimension ID was rejected.";
                    }
                    else
                    {
                        changedDimension = true;
                    }
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    Logger.Error($"[DimensionManager] Could not initialize the destination save snapshot: {exception}");
                }
            }

            if (string.IsNullOrEmpty(error) && changedDimension)
            {
                yield return RebuildChunkCache(providerRebuildContext);
                if (!string.IsNullOrEmpty(providerRebuildContext.Error))
                {
                    error = providerRebuildContext.Error;
                }
                else
                {
                    try
                    {
                        chunkProvider = providerRebuildContext.ChunkProvider;
                        Vector3 destinationPosition = GetDestinationPosition(targetDimension, destination);
                        player.SetPosition(destinationPosition, true);

                        int chunkX = World.toChunkXZ(Mathf.FloorToInt(destinationPosition.x));
                        int chunkZ = World.toChunkXZ(Mathf.FloorToInt(destinationPosition.z));
                        RequestChunk(chunkProvider, chunkX, chunkZ);
                    }
                    catch (Exception exception)
                    {
                        error = exception.Message;
                        Logger.Error($"[DimensionManager] Could not request the destination chunk: {exception}");
                    }
                }
            }

            if (string.IsNullOrEmpty(error) && chunkCache != null)
            {
                Vector3 destinationPosition = GetDestinationPosition(targetDimension, destination);
                int chunkX = World.toChunkXZ(Mathf.FloorToInt(destinationPosition.x));
                int chunkZ = World.toChunkXZ(Mathf.FloorToInt(destinationPosition.z));
                float timeoutAt = Time.realtimeSinceStartup + ChunkLoadTimeoutSeconds;
                Chunk destinationChunk = null;
                while ((destinationChunk = chunkCache.GetChunkSync(chunkX, chunkZ)) == null && Time.realtimeSinceStartup < timeoutAt)
                {
                    yield return null;
                }

                if (destinationChunk == null)
                {
                    error = "The destination chunk did not load before the timeout.";
                }
                else
                {
                    timeoutAt = Time.realtimeSinceStartup + ChunkCollisionTimeoutSeconds;
                    while ((!destinationChunk.IsCollisionMeshGenerated || destinationChunk.NeedsRegeneration) &&
                           Time.realtimeSinceStartup < timeoutAt)
                    {
                        yield return null;
                    }

                    if (!destinationChunk.IsCollisionMeshGenerated || destinationChunk.NeedsRegeneration)
                    {
                        error = "The destination chunk did not build collision before the timeout.";
                    }
                }
            }

            if (!string.IsNullOrEmpty(error) && changedDimension)
            {
                bool rollbackChunkRequested = false;
                int rollbackChunkX = 0;
                int rollbackChunkZ = 0;
                TrySetActiveDimension(previousDimension);
                yield return RebuildChunkCache(providerRebuildContext);
                if (string.IsNullOrEmpty(providerRebuildContext.Error))
                {
                    try
                    {
                        chunkProvider = providerRebuildContext.ChunkProvider;
                        player.SetPosition(previousPosition, true);

                        rollbackChunkX = World.toChunkXZ(Mathf.FloorToInt(previousPosition.x));
                        rollbackChunkZ = World.toChunkXZ(Mathf.FloorToInt(previousPosition.z));
                        RequestChunk(chunkProvider, rollbackChunkX, rollbackChunkZ);
                        rollbackChunkRequested = true;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error($"[DimensionManager] Could not request the rollback chunk: {exception}");
                    }
                }
                else
                {
                    Logger.Error($"[DimensionManager] Could not rebuild the Overworld provider during rollback: {providerRebuildContext.Error}");
                }

                if (rollbackChunkRequested)
                {
                    float rollbackTimeoutAt = Time.realtimeSinceStartup + ChunkLoadTimeoutSeconds;
                    while (chunkCache.GetChunkSync(rollbackChunkX, rollbackChunkZ) == null && Time.realtimeSinceStartup < rollbackTimeoutAt)
                    {
                        yield return null;
                    }

                    if (chunkCache.GetChunkSync(rollbackChunkX, rollbackChunkZ) == null)
                    {
                        Logger.Error("[DimensionManager] The Overworld rollback chunk did not load before the timeout.");
                    }
                }
            }

            if (!changedDimension && regionStorageRebindInProgress)
            {
                CompleteRegionStorageRebind();
            }

            if (playerController != null) playerController.enabled = playerControllerWasEnabled;
            player.SetControllable(true);
            player.Buffs.RemoveBuff("buffFluxTeleporting");
            transitionInProgress = false;

            if (string.IsNullOrEmpty(error))
            {
                player.PlayOneShot("weapon_electric_charge");
                string message = IsOverworld(activeDimensionId)
                    ? "Returned to the Overworld save."
                    : GetActiveDimensionDisplayName() + " loaded. Realm blocks and world entities are isolated.";
                GameManager.ShowTooltip(player, message);
            }
            else
            {
                player.PlayOneShot("ui_denied");
                GameManager.ShowTooltip(player, "Dimension transition failed: " + error);
            }
        }

        private static bool TryCreateProviderRebuildContext(ChunkCluster chunkCache, IChunkProvider chunkProvider, out ChunkProviderRebuildContext rebuildContext, out string error)
        {
            rebuildContext = null;
            error = "";

            ChunkProviderGenerateWorld generatedWorldProvider = chunkProvider as ChunkProviderGenerateWorld;
            if (generatedWorldProvider == null || chunkProvider.GetProviderId() == EnumChunkProviderId.FlatWorld)
            {
                error = "This world provider cannot rebuild save-backed chunks. Use a disposable normal world, not Playtesting.";
                return false;
            }

            if (generatedWorldProvider.worldLocation == null)
            {
                error = "The active world provider did not expose its world location.";
                return false;
            }

            if (!ReferenceEquals(storageBindingProvider, generatedWorldProvider))
            {
                regionStorageBindings.Clear();
                storageBindingProvider = generatedWorldProvider;
                activeGeneratedRegionFileManager = null;
            }

            regionStorageBindings[activeDimensionId] = new RegionStorageBinding(
                generatedWorldProvider.m_RegionFileManager,
                generatedWorldProvider.eventPrefabs);
            rebuildContext = new ChunkProviderRebuildContext(chunkCache, generatedWorldProvider);
            return true;
        }

        private static IEnumerator RebuildChunkCache(ChunkProviderRebuildContext rebuildContext)
        {
            rebuildContext.Error = "";
            rebuildContext.ChunkProvider = null;

            try
            {
                RegionStorageBinding storageBinding;
                if (!regionStorageBindings.TryGetValue(activeDimensionId, out storageBinding))
                {
                    World world = GameManager.Instance.World;
                    string regionDirectory = GameIO.GetSaveGameRegionDir();
                    if (string.IsNullOrEmpty(regionDirectory))
                    {
                        rebuildContext.Error = "The destination region directory is unavailable.";
                        yield break;
                    }

                    RegionFileManager regionFileManager = new RegionFileManager(
                        regionDirectory,
                        regionDirectory,
                        0,
                        !world.IsEditor());
                    storageBinding = new RegionStorageBinding(
                        regionFileManager,
                        new EventPrefabs(world, rebuildContext.GeneratedWorldProvider.prefabDecorator, regionFileManager));
                    regionStorageBindings[activeDimensionId] = storageBinding;
                }

                MultiBlockManager.Instance.Cleanup();
                rebuildContext.GeneratedWorldProvider.m_RegionFileManager = storageBinding.RegionFileManager;
                rebuildContext.GeneratedWorldProvider.eventPrefabs = storageBinding.EventPrefabs;
                rebuildContext.GeneratedWorldProvider.bDecorationsEnabled = !IsActiveGeneratedDimension();
                activeGeneratedRegionFileManager = IsActiveGeneratedDimension()
                    ? storageBinding.RegionFileManager
                    : null;
                MultiBlockManager.Instance.Initialize(storageBinding.RegionFileManager);
                rebuildContext.GeneratedWorldProvider.ReloadAllChunks();
                rebuildContext.ChunkProvider = rebuildContext.GeneratedWorldProvider;
                CompleteRegionStorageRebind();
                Logger.Info($"[DimensionManager] Rebound region storage for '{activeDimensionId}' through '{rebuildContext.ChunkProvider.GetType().Name}'.");
            }
            catch (Exception exception)
            {
                rebuildContext.Error = "The active region storage could not be rebound: " + exception.Message;
                yield break;
            }

            yield break;
        }

        private static Vector3 GetDestinationPosition(string dimensionId, Vector3 defaultPosition)
        {
            DimensionDefinition definition;
            DimensionGeneratorDefinition generator;
            if (!DimensionRegistry.TryGet(dimensionId, out definition) ||
                !DimensionGeneratorRegistry.TryGet(definition.GeneratorId, out generator) ||
                generator.GetEntryPosition == null)
            {
                return defaultPosition;
            }

            try
            {
                return generator.GetEntryPosition(definition, defaultPosition);
            }
            catch (Exception exception)
            {
                Logger.Error($"[DimensionManager] Generator '{generator.Id}' could not resolve its entry position: {exception}");
                return defaultPosition;
            }
        }

        private static bool IsActiveGeneratedDimension()
        {
            DimensionDefinition definition;
            DimensionGeneratorDefinition generator;
            return !IsOverworld(activeDimensionId) &&
                DimensionRegistry.TryGet(activeDimensionId, out definition) &&
                DimensionGeneratorRegistry.TryGet(definition.GeneratorId, out generator) &&
                generator.SaveMode == DimensionSaveMode.Generated;
        }

        private static string GetActiveDimensionDisplayName()
        {
            DimensionDefinition definition;
            return DimensionRegistry.TryGet(activeDimensionId, out definition)
                ? definition.DisplayName
                : activeDimensionId;
        }

        private static bool TryUnloadActiveChunks(ChunkCluster chunkCache, out string error)
        {
            error = "";

            try
            {
                World world = GameManager.Instance.World;
                List<Chunk> activeChunks = new List<Chunk>(chunkCache.GetChunkArrayCopySync());
                int forcedEntityUnloads = 0;
                foreach (Chunk chunk in activeChunks)
                {
                    chunkCache.RemoveChunk(chunk);
                    forcedEntityUnloads += UnloadRemainingNonPlayerEntities(world, chunk);
                    chunkCache.UnloadChunk(chunk);
                }

                world.m_ChunkManager.ClearChunksForAllObservers(chunkCache);
                ClearDisplayedChunkGameObjects(chunkCache);
                Logger.Info($"[DimensionManager] Unloaded {activeChunks.Count} active chunks and {forcedEntityUnloads} remaining non-player entities before rebinding region storage.");
                return true;
            }
            catch (Exception exception)
            {
                error = "The active chunks could not be unloaded: " + exception.Message;
                Logger.Error($"[DimensionManager] Could not unload active chunks: {exception}");
                return false;
            }
        }

        private static int UnloadRemainingNonPlayerEntities(World world, Chunk unloadedChunk)
        {
            int entityUnloads = 0;
            List<Entity> activeEntities = new List<Entity>(world.Entities.list);
            foreach (Entity entity in activeEntities)
            {
                if (entity is EntityPlayer || !entity.addedToChunk ||
                    entity.chunkPosAddedEntityTo.x != unloadedChunk.X ||
                    entity.chunkPosAddedEntityTo.z != unloadedChunk.Z) continue;

                if (world.RemoveEntity(entity.entityId, EnumRemoveEntityReason.Unloaded) != null)
                {
                    entityUnloads++;
                }
            }

            return entityUnloads;
        }

        private static void ClearDisplayedChunkGameObjects(ChunkCluster chunkCache)
        {
            ChunkManager chunkManager = GameManager.Instance.World.m_ChunkManager;
            lock (chunkCache.DisplayedChunkGameObjects)
            {
                long[] displayedChunkKeys = new long[chunkCache.DisplayedChunkGameObjects.Count];
                chunkCache.DisplayedChunkGameObjects.Dict.CopyKeysTo(displayedChunkKeys);
                foreach (long chunkKey in displayedChunkKeys)
                {
                    _ = chunkCache.DisplayedChunkGameObjects[chunkKey];
                    chunkManager.FreeChunkGameObject(chunkCache, chunkKey);
                }
                chunkCache.DisplayedChunkGameObjects.Clear();
            }
        }

        public static void CleanupInactiveRegionStorage()
        {
            RegionFileManager activeRegionFileManager = storageBindingProvider != null
                ? storageBindingProvider.m_RegionFileManager
                : null;

            foreach (RegionStorageBinding storageBinding in regionStorageBindings.Values)
            {
                if (storageBinding.RegionFileManager == null ||
                    ReferenceEquals(storageBinding.RegionFileManager, activeRegionFileManager)) continue;

                try
                {
                    storageBinding.RegionFileManager.Cleanup();
                }
                catch (Exception exception)
                {
                    Logger.Error($"[DimensionManager] Could not close inactive region storage: {exception}");
                }
            }

            regionStorageBindings.Clear();
            storageBindingProvider = null;
            activeGeneratedRegionFileManager = null;
            regionStorageRebindInProgress = false;
        }

        private static void BeginRegionStorageRebind()
        {
            regionStorageRebindInProgress = true;
            lock (chunkGenerationGate)
            {
            }
        }

        private static void CompleteRegionStorageRebind()
        {
            regionStorageRebindInProgress = false;
        }

        private static void RequestChunk(IChunkProvider chunkProvider, int chunkX, int chunkZ)
        {
            chunkProvider.RequestChunk(chunkX, chunkZ);
            Logger.Info($"[DimensionManager] Requested destination chunk ({chunkX}, {chunkZ}) through '{chunkProvider.GetType().Name}'.");
        }

        private static IChunkProvider GetActiveChunkProvider(ChunkCluster chunkCache)
        {
            object world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world == null) return null;

            List<string> inspectedPaths = new List<string>();
            IChunkProvider provider = FindChunkProvider(GameManager.Instance, "GameManager", ProviderSearchDepth, inspectedPaths);
            if (provider == null)
            {
                provider = FindChunkProvider(world, "World", ProviderSearchDepth, inspectedPaths);
            }

            if (provider == null && chunkCache != null)
            {
                provider = FindChunkProvider(chunkCache, "ChunkCache", ProviderSearchDepth, inspectedPaths);
            }

            if (provider != null)
            {
                Logger.Info($"[DimensionManager] Located chunk provider '{provider.GetType().Name}'.");
                return provider;
            }

            string inspectedPathSummary = inspectedPaths.Count == 0
                ? "No chunk-related members could be read."
                : string.Join(" | ", inspectedPaths.ToArray());
            Logger.Error($"[DimensionManager] No IChunkProvider was found on world type '{world.GetType().FullName}'. Inspected: {inspectedPathSummary}");
            return null;
        }

        private static IChunkProvider FindChunkProvider(object root, string rootPath, int maxDepth, List<string> inspectedPaths)
        {
            Queue<ProviderSearchCandidate> candidates = new Queue<ProviderSearchCandidate>();
            HashSet<object> visited = new HashSet<object>();
            candidates.Enqueue(new ProviderSearchCandidate(root, rootPath, 0));

            while (candidates.Count > 0)
            {
                ProviderSearchCandidate candidate = candidates.Dequeue();
                if (candidate.Value == null || !visited.Add(candidate.Value)) continue;

                IChunkProvider provider = candidate.Value as IChunkProvider;
                if (provider != null) return provider;
                if (candidate.Depth >= maxDepth) continue;

                Type candidateType = candidate.Value.GetType();
                foreach (PropertyInfo property in candidateType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!property.CanRead || !IsChunkRelatedMember(property.Name, property.PropertyType)) continue;
                    TryQueueChunkRelatedValue(candidates, candidate.Path + "." + property.Name, candidate.Depth, inspectedPaths, () => property.GetValue(candidate.Value, null));
                }

                for (Type currentType = candidateType; currentType != null; currentType = currentType.BaseType)
                {
                    foreach (FieldInfo field in currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!IsChunkRelatedMember(field.Name, field.FieldType)) continue;
                        TryQueueChunkRelatedValue(candidates, candidate.Path + "." + field.Name, candidate.Depth, inspectedPaths, () => field.GetValue(candidate.Value));
                    }
                }
            }

            return null;
        }

        private static bool IsChunkRelatedMember(string memberName, Type memberType)
        {
            return typeof(IChunkProvider).IsAssignableFrom(memberType) ||
                   memberName.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("provider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("region", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberType.Name.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberType.Name.IndexOf("provider", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryQueueChunkRelatedValue(Queue<ProviderSearchCandidate> candidates, string path, int depth, List<string> inspectedPaths, Func<object> getValue)
        {
            try
            {
                object value = getValue();
                if (value == null) return;

                if (inspectedPaths.Count < ProviderSearchDiagnosticLimit)
                {
                    inspectedPaths.Add(path + " (" + value.GetType().Name + ")");
                }

                candidates.Enqueue(new ProviderSearchCandidate(value, path, depth + 1));
            }
            catch (Exception)
            {
            }
        }

        private sealed class ProviderSearchCandidate
        {
            public object Value { get; }
            public string Path { get; }
            public int Depth { get; }

            public ProviderSearchCandidate(object value, string path, int depth)
            {
                Value = value;
                Path = path;
                Depth = depth;
            }
        }

        private sealed class ChunkProviderRebuildContext
        {
            public ChunkCluster ChunkCache { get; }
            public ChunkProviderGenerateWorld GeneratedWorldProvider { get; }
            public IChunkProvider ChunkProvider { get; set; }
            public string Error { get; set; }

            public ChunkProviderRebuildContext(ChunkCluster chunkCache, ChunkProviderGenerateWorld generatedWorldProvider)
            {
                ChunkCache = chunkCache;
                GeneratedWorldProvider = generatedWorldProvider;
                Error = "";
            }
        }

        private sealed class RegionStorageBinding
        {
            public RegionFileManager RegionFileManager { get; }
            public EventPrefabs EventPrefabs { get; }

            public RegionStorageBinding(RegionFileManager regionFileManager, EventPrefabs eventPrefabs)
            {
                RegionFileManager = regionFileManager;
                EventPrefabs = eventPrefabs;
            }
        }
    }

    [HarmonyPatch(typeof(World), "Cleanup")]
    public class World_Cleanup_DimensionStoragePatch
    {
        public static void Prefix()
        {
            DimensionManager.CleanupInactiveRegionStorage();
        }
    }
}