using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Xml;

namespace LizziesMod
{
    public enum PropSpawnerCommand : byte
    {
        Spawn,
        Undo,
        ClearOwned,
        GrantItem
    }

    public static class SpawnMenuManager
    {
        public const string ModName = "LizziesMod_PropSpawner";

        private const string PhysicsPropEntityClass = "lmPhysicsProp";
        private const string PersistentRagdollBuff = "buffSpawnMenuRagdoll";
        private const float SpawnCooldownSeconds = 0.15f;
        private class SpawnedEntryRecord
        {
            public int EntityId;
            public SpawnMenuEntryType EntryType;
        }

        private static readonly Dictionary<int, List<SpawnedEntryRecord>> spawnedEntriesByOwner =
            new Dictionary<int, List<SpawnedEntryRecord>>();
        private static readonly Dictionary<int, float> lastSpawnTimesByPlayer = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> lastGrantTimesByPlayer = new Dictionary<int, float>();

        public static bool CanUse(EntityPlayer player)
        {
            if (player == null) return false;
            if (global::ModManager.GetMod(ModName) == null || !ModPatcher.IsModEnabled(ModName)) return false;
            if (!ModSettingsManager.GetSetting<bool>(ModName, "Enabled", true)) return false;

            return !ModSettingsManager.GetSetting<bool>(ModName, "AdminOnly", true) || player.IsAdmin;
        }

        public static void RequestSpawn(EntityPlayerLocal player, string entryId)
        {
            if (!CanUse(player))
            {
                GameManager.ShowTooltip(player, "Spawn menu access is restricted to admins.");
                return;
            }

            SendCommand(player, PropSpawnerCommand.Spawn, entryId);
        }

        public static void RequestUndo(EntityPlayerLocal player)
        {
            if (!CanUse(player)) return;
            SendCommand(player, PropSpawnerCommand.Undo, "");
        }

        public static void RequestClearOwned(EntityPlayerLocal player)
        {
            if (!CanUse(player)) return;
            SendCommand(player, PropSpawnerCommand.ClearOwned, "");
        }

        public static void RequestGrantItem(EntityPlayerLocal player, string entryId)
        {
            if (!CanUse(player)) return;
            SendCommand(player, PropSpawnerCommand.GrantItem, entryId);
        }

        public static void ProcessServerCommand(World world, EntityPlayer player, PropSpawnerCommand command, string entryId)
        {
            if (world == null || !CanUse(player)) return;

            switch (command)
            {
                case PropSpawnerCommand.Spawn:
                    SpawnEntry(world, player, entryId);
                    break;
                case PropSpawnerCommand.Undo:
                    UndoLastSpawn(world, player.entityId);
                    break;
                case PropSpawnerCommand.ClearOwned:
                    ClearOwnedSpawns(world, player.entityId);
                    break;
                case PropSpawnerCommand.GrantItem:
                    GrantPropItem(player, entryId);
                    break;
            }
        }

        private static void SendCommand(EntityPlayerLocal player, PropSpawnerCommand command, string entryId)
        {
            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null) return;

            if (connectionManager.IsServer)
            {
                ProcessServerCommand(GameManager.Instance.World, player, command, entryId);
                return;
            }

            connectionManager.SendToServer(
                NetPackageManager.GetPackage<NetPackagePhysicsPropCommand>().Setup(command, entryId),
                false);
        }

        private static void SpawnEntry(World world, EntityPlayer player, string entryId)
        {
            if (!SpawnCatalog.TryGetEntry(entryId, out SpawnMenuEntryDefinition definition))
            {
                Logger.Warning($"[SpawnMenu] Rejected unknown spawn entry '{entryId}'.");
                return;
            }

            float currentTime = Time.realtimeSinceStartup;
            if (lastSpawnTimesByPlayer.TryGetValue(player.entityId, out float lastSpawnTime) &&
                currentTime - lastSpawnTime < SpawnCooldownSeconds)
            {
                return;
            }

            lastSpawnTimesByPlayer[player.entityId] = currentTime;

            if (!CanSpawnEntryType(player, definition.EntryType)) return;
            if (IsSpawnLimitReached(world, player.entityId, definition.EntryType))
            {
                Logger.Warning($"[SpawnMenu] {definition.EntryType} spawn limit reached for player {player.entityId}.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(player);
            if (Physics.CheckSphere(spawnPosition, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                Logger.Warning($"[SpawnMenu] Rejected blocked spawn position for player {player.entityId}.");
                return;
            }

            SpawnablePropDefinition propDefinition = definition as SpawnablePropDefinition;
            SpawnableEntityDefinition entityDefinition = definition as SpawnableEntityDefinition;
            if (definition.EntryType == SpawnMenuEntryType.Prop && propDefinition != null)
            {
                SpawnProp(world, player, propDefinition, spawnPosition);
            }
            else if (definition.EntryType == SpawnMenuEntryType.Entity && entityDefinition != null)
            {
                SpawnEntity(world, player, entityDefinition, spawnPosition);
            }
            else if (definition.EntryType == SpawnMenuEntryType.Ragdoll && entityDefinition != null)
            {
                SpawnRagdoll(world, player, entityDefinition, spawnPosition);
            }
            else
            {
                Logger.Warning($"[SpawnMenu] Rejected invalid entry '{entryId}'.");
            }
        }

        private static void GrantPropItem(EntityPlayer player, string entryId)
        {
            SpawnablePropDefinition definition;
            if (!SpawnCatalog.TryGetProp(entryId, out definition))
            {
                Logger.Warning($"[SpawnMenu] Rejected non-prop inventory request '{entryId}'.");
                return;
            }

            float currentTime = Time.realtimeSinceStartup;
            if (lastGrantTimesByPlayer.TryGetValue(player.entityId, out float lastGrantTime) &&
                currentTime - lastGrantTime < SpawnCooldownSeconds)
            {
                return;
            }

            lastGrantTimesByPlayer[player.entityId] = currentTime;
            ItemStack itemStack = definition.GetIconStack();
            if (!player.inventory.AddItem(itemStack))
            {
                Logger.Warning($"[SpawnMenu] Could not add '{definition.Id}' to player {player.entityId}'s full inventory.");
                return;
            }

            player.inventory.onInventoryChanged();
            Logger.Info($"[SpawnMenu] Added prop item '{definition.Id}' to player {player.entityId}'s inventory.");
        }

        private static void SpawnProp(World world, EntityPlayer player, SpawnablePropDefinition definition, Vector3 spawnPosition)
        {
            int entityClassId = EntityClass.GetId(PhysicsPropEntityClass);
            EntityPhysicsProp prop = EntityFactory.CreateEntity(entityClassId, spawnPosition, new Vector3(0f, player.rotation.y, 0f)) as EntityPhysicsProp;
            if (prop == null)
            {
                Logger.Error($"[SpawnMenu] Failed to create prop entity class '{PhysicsPropEntityClass}'.");
                return;
            }

            prop.belongsPlayerId = player.entityId;
            prop.SetBlockValue(definition.GetBlockValue());
            prop.SetStartVelocity(Vector3.zero, 0f);
            world.SpawnEntityInWorld(prop);
            RecordSpawn(player.entityId, prop.entityId, SpawnMenuEntryType.Prop);
            Logger.Info($"[SpawnMenu] Spawned prop '{definition.Id}' for player {player.entityId}.");
        }

        private static void SpawnEntity(World world, EntityPlayer player, SpawnableEntityDefinition definition, Vector3 spawnPosition)
        {
            EntityAlive entity = EntityFactory.CreateEntity(definition.EntityClassId, spawnPosition, new Vector3(0f, player.rotation.y, 0f)) as EntityAlive;
            if (entity == null)
            {
                Logger.Error($"[SpawnMenu] Failed to create entity '{definition.EntityClassName}'.");
                return;
            }

            world.SpawnEntityInWorld(entity);
            RecordSpawn(player.entityId, entity.entityId, SpawnMenuEntryType.Entity);
            Logger.Info($"[SpawnMenu] Spawned entity '{definition.Id}' for player {player.entityId}.");
        }

        private static void SpawnRagdoll(World world, EntityPlayer player, SpawnableEntityDefinition definition, Vector3 spawnPosition)
        {
            EntityAlive entity = EntityFactory.CreateEntity(definition.EntityClassId, spawnPosition, new Vector3(0f, player.rotation.y, 0f)) as EntityAlive;
            if (entity == null)
            {
                Logger.Error($"[SpawnMenu] Failed to create ragdoll '{definition.EntityClassName}'.");
                return;
            }

            world.SpawnEntityInWorld(entity);
            entity.Buffs.AddBuff(PersistentRagdollBuff);
            RecordSpawn(player.entityId, entity.entityId, SpawnMenuEntryType.Ragdoll);
            Logger.Info($"[SpawnMenu] Spawned ragdoll '{definition.Id}' for player {player.entityId}.");
        }

        public static void ExportBaseGameCandidates()
        {
            try
            {
                Mod spawnMenuMod = global::ModManager.GetLoadedMods().Find(mod =>
                    mod.Name.Equals(ModName, StringComparison.OrdinalIgnoreCase));
                if (spawnMenuMod == null) return;

                string sourcePath = GameIO.GetGameDir("Data/Config/blocks.xml");
                string outputPath = Path.Combine(spawnMenuMod.Path, "SpawnableProps.Candidates.xml");
                XmlDocument sourceDocument = new XmlDocument();
                sourceDocument.Load(sourcePath);

                List<string> blockNames = FindModelEntityBlocks(sourceDocument);
                using (XmlWriter writer = XmlWriter.Create(outputPath, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("SpawnableProps");
                    writer.WriteStartElement("Categories");
                    writer.WriteStartElement("Category");
                    writer.WriteAttributeString("id", "unreviewed");
                    writer.WriteAttributeString("name", "Unreviewed Candidates");
                    writer.WriteAttributeString("order", "999");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteStartElement("Props");

                    foreach (string blockName in blockNames)
                    {
                        writer.WriteStartElement("Prop");
                        writer.WriteAttributeString("id", "candidate." + blockName);
                        writer.WriteAttributeString("block", blockName);
                        writer.WriteAttributeString("category", "unreviewed");
                        writer.WriteAttributeString("displayName", blockName);
                        writer.WriteAttributeString("tags", "candidate");
                        writer.WriteAttributeString("mass", "10");
                        writer.WriteAttributeString("thumbnail", blockName);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                Logger.Info($"[SpawnMenu] Exported {blockNames.Count} model candidates to '{outputPath}'. Review entries before moving them into SpawnableProps.xml.");
            }
            catch (Exception exception)
            {
                Logger.Error($"[SpawnMenu] Failed to export model candidates: {exception.Message}");
            }
        }

        private static List<string> FindModelEntityBlocks(XmlDocument sourceDocument)
        {
            List<string> blockNames = new List<string>();
            XmlNodeList blockNodes = sourceDocument.SelectNodes("/blocks/block");
            if (blockNodes == null) return blockNames;

            foreach (XmlNode blockNode in blockNodes)
            {
                string blockName = blockNode.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(blockName)) continue;

                bool isModelEntity = false;
                bool hasModel = false;
                foreach (XmlNode propertyNode in blockNode.SelectNodes("property"))
                {
                    string propertyName = propertyNode.Attributes?["name"]?.Value;
                    string propertyValue = propertyNode.Attributes?["value"]?.Value;

                    if (propertyName == "Shape" && propertyValue == "ModelEntity") isModelEntity = true;
                    if (propertyName == "Model" && !string.IsNullOrEmpty(propertyValue)) hasModel = true;
                }

                if (isModelEntity && hasModel) blockNames.Add(blockName);
            }

            blockNames.Sort(StringComparer.OrdinalIgnoreCase);
            return blockNames;
        }

        private static Vector3 GetSpawnPosition(EntityPlayer player)
        {
            float spawnDistance = Mathf.Clamp(ModSettingsManager.GetSetting<float>(ModName, "SpawnDistance", 8f), 2f, 20f);
            Vector3 origin = player.getHeadPosition();
            Vector3 direction = Quaternion.Euler(player.rotation) * Vector3.forward;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, spawnDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point + (hit.normal * 0.75f);
            }

            return origin + (direction * spawnDistance);
        }

        private static bool CanSpawnEntryType(EntityPlayer player, SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity && !ModSettingsManager.GetSetting<bool>(ModName, "AllowEntitySpawning", true))
            {
                Logger.Warning($"[SpawnMenu] Entity spawning is disabled for player {player.entityId}.");
                return false;
            }

            if (entryType == SpawnMenuEntryType.Ragdoll && !ModSettingsManager.GetSetting<bool>(ModName, "AllowRagdollSpawning", true))
            {
                Logger.Warning($"[SpawnMenu] Ragdoll spawning is disabled for player {player.entityId}.");
                return false;
            }

            return true;
        }

        private static bool IsSpawnLimitReached(World world, int ownerId, SpawnMenuEntryType entryType)
        {
            int maxWorld = Mathf.Max(1, ModSettingsManager.GetSetting<int>(ModName, GetWorldLimitSetting(entryType), GetDefaultWorldLimit(entryType)));
            int maxPlayer = Mathf.Max(1, ModSettingsManager.GetSetting<int>(ModName, GetPlayerLimitSetting(entryType), GetDefaultPlayerLimit(entryType)));
            return CountSpawnedEntries(world, entryType, -1) >= maxWorld || CountSpawnedEntries(world, entryType, ownerId) >= maxPlayer;
        }

        private static string GetWorldLimitSetting(SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity) return "MaxEntitiesWorld";
            if (entryType == SpawnMenuEntryType.Ragdoll) return "MaxRagdollsWorld";
            return "MaxPropsWorld";
        }

        private static string GetPlayerLimitSetting(SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity) return "MaxEntitiesPerPlayer";
            if (entryType == SpawnMenuEntryType.Ragdoll) return "MaxRagdollsPerPlayer";
            return "MaxPropsPerPlayer";
        }

        private static int GetDefaultWorldLimit(SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity) return 30;
            if (entryType == SpawnMenuEntryType.Ragdoll) return 30;
            return 150;
        }

        private static int GetDefaultPlayerLimit(SpawnMenuEntryType entryType)
        {
            if (entryType == SpawnMenuEntryType.Entity) return 10;
            if (entryType == SpawnMenuEntryType.Ragdoll) return 10;
            return 30;
        }

        private static int CountSpawnedEntries(World world, SpawnMenuEntryType entryType, int ownerId)
        {
            int count = 0;
            List<int> owners = new List<int>(spawnedEntriesByOwner.Keys);
            foreach (int trackedOwnerId in owners)
            {
                if (ownerId >= 0 && trackedOwnerId != ownerId) continue;
                List<SpawnedEntryRecord> records = spawnedEntriesByOwner[trackedOwnerId];
                for (int index = records.Count - 1; index >= 0; index--)
                {
                    SpawnedEntryRecord record = records[index];
                    if (world.GetEntity(record.EntityId) == null)
                    {
                        records.RemoveAt(index);
                        continue;
                    }

                    if (record.EntryType == entryType) count++;
                }

                if (records.Count == 0) spawnedEntriesByOwner.Remove(trackedOwnerId);
            }

            return count;
        }

        private static void RecordSpawn(int ownerId, int entityId, SpawnMenuEntryType entryType)
        {
            List<SpawnedEntryRecord> records;
            if (!spawnedEntriesByOwner.TryGetValue(ownerId, out records))
            {
                records = new List<SpawnedEntryRecord>();
                spawnedEntriesByOwner.Add(ownerId, records);
            }

            records.Add(new SpawnedEntryRecord
            {
                EntityId = entityId,
                EntryType = entryType
            });
        }

        private static void UndoLastSpawn(World world, int ownerId)
        {
            List<SpawnedEntryRecord> records;
            if (!spawnedEntriesByOwner.TryGetValue(ownerId, out records)) return;

            for (int index = records.Count - 1; index >= 0; index--)
            {
                SpawnedEntryRecord record = records[index];
                records.RemoveAt(index);

                if (world.GetEntity(record.EntityId) == null) continue;

                world.RemoveEntity(record.EntityId, EnumRemoveEntityReason.Despawned);
                return;
            }

            spawnedEntriesByOwner.Remove(ownerId);
        }

        private static void ClearOwnedSpawns(World world, int ownerId)
        {
            List<SpawnedEntryRecord> records;
            if (!spawnedEntriesByOwner.TryGetValue(ownerId, out records)) return;

            foreach (SpawnedEntryRecord record in records)
            {
                if (world.GetEntity(record.EntityId) != null)
                {
                    world.RemoveEntity(record.EntityId, EnumRemoveEntityReason.Despawned);
                }
            }

            spawnedEntriesByOwner.Remove(ownerId);
        }
    }
}