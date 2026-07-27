using System;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public enum PropSpawnerCommand : byte
    {
        Spawn,
        Undo,
        ClearOwned
    }

    public static class PropSpawnerManager
    {
        public const string ModName = "LizziesMod_PropSpawner";

        private const string PhysicsPropEntityClass = "lmPhysicsProp";
        private const float SpawnCooldownSeconds = 0.15f;
        private static readonly Dictionary<int, List<int>> spawnedPropIdsByOwner = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, float> lastSpawnTimesByPlayer = new Dictionary<int, float>();

        public static bool CanUse(EntityPlayer player)
        {
            if (player == null) return false;
            if (global::ModManager.GetMod(ModName) == null || !ModPatcher.IsModEnabled(ModName)) return false;
            if (!ModSettingsManager.GetSetting<bool>(ModName, "Enabled", true)) return false;

            return !ModSettingsManager.GetSetting<bool>(ModName, "AdminOnly", true) || player.IsAdmin;
        }

        public static void RequestSpawn(EntityPlayerLocal player, string propId)
        {
            if (!CanUse(player))
            {
                GameManager.ShowTooltip(player, "Prop spawning is restricted to admins.");
                return;
            }

            SendCommand(player, PropSpawnerCommand.Spawn, propId);
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

        public static void ProcessServerCommand(World world, EntityPlayer player, PropSpawnerCommand command, string propId)
        {
            if (world == null || !CanUse(player)) return;

            switch (command)
            {
                case PropSpawnerCommand.Spawn:
                    SpawnProp(world, player, propId);
                    break;
                case PropSpawnerCommand.Undo:
                    UndoLastProp(world, player.entityId);
                    break;
                case PropSpawnerCommand.ClearOwned:
                    ClearOwnedProps(world, player.entityId);
                    break;
            }
        }

        private static void SendCommand(EntityPlayerLocal player, PropSpawnerCommand command, string propId)
        {
            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null) return;

            if (connectionManager.IsServer)
            {
                ProcessServerCommand(GameManager.Instance.World, player, command, propId);
                return;
            }

            connectionManager.SendToServer(
                NetPackageManager.GetPackage<NetPackagePhysicsPropCommand>().Setup(command, propId),
                false);
        }

        private static void SpawnProp(World world, EntityPlayer player, string propId)
        {
            if (!PropCatalog.TryGetProp(propId, out SpawnablePropDefinition definition))
            {
                Logger.Warning($"[PropSpawner] Rejected unknown prop id '{propId}'.");
                return;
            }

            float currentTime = Time.realtimeSinceStartup;
            if (lastSpawnTimesByPlayer.TryGetValue(player.entityId, out float lastSpawnTime) &&
                currentTime - lastSpawnTime < SpawnCooldownSeconds)
            {
                return;
            }

            lastSpawnTimesByPlayer[player.entityId] = currentTime;

            int maxWorldProps = Mathf.Max(1, ModSettingsManager.GetSetting<int>(ModName, "MaxPropsWorld", 150));
            int maxPlayerProps = Mathf.Max(1, ModSettingsManager.GetSetting<int>(ModName, "MaxPropsPerPlayer", 30));
            if (CountProps(world, -1) >= maxWorldProps || CountProps(world, player.entityId) >= maxPlayerProps)
            {
                Logger.Warning($"[PropSpawner] Spawn limit reached for player {player.entityId}.");
                return;
            }

            Vector3 spawnPosition = GetSpawnPosition(player);
            if (Physics.CheckSphere(spawnPosition, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                Logger.Warning($"[PropSpawner] Rejected blocked spawn position for player {player.entityId}.");
                return;
            }

            int entityClassId = EntityClass.GetId(PhysicsPropEntityClass);
            EntityPhysicsProp prop = EntityFactory.CreateEntity(entityClassId, spawnPosition, new Vector3(0f, player.rotation.y, 0f)) as EntityPhysicsProp;
            if (prop == null)
            {
                Logger.Error($"[PropSpawner] Failed to create prop entity class '{PhysicsPropEntityClass}'.");
                return;
            }

            prop.belongsPlayerId = player.entityId;
            prop.SetBlockValue(definition.GetBlockValue());
            prop.SetStartVelocity(Vector3.zero, 0f);
            world.SpawnEntityInWorld(prop);

            if (!spawnedPropIdsByOwner.TryGetValue(player.entityId, out List<int> spawnedIds))
            {
                spawnedIds = new List<int>();
                spawnedPropIdsByOwner.Add(player.entityId, spawnedIds);
            }

            spawnedIds.Add(prop.entityId);
            Logger.Info($"[PropSpawner] Spawned '{definition.Id}' for player {player.entityId}.");
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

        private static int CountProps(World world, int ownerId)
        {
            int count = 0;
            foreach (Entity entity in world.Entities.list)
            {
                if (!(entity is EntityPhysicsProp)) continue;
                if (ownerId >= 0 && entity.belongsPlayerId != ownerId) continue;
                count++;
            }

            return count;
        }

        private static void UndoLastProp(World world, int ownerId)
        {
            if (!spawnedPropIdsByOwner.TryGetValue(ownerId, out List<int> spawnedIds)) return;

            for (int index = spawnedIds.Count - 1; index >= 0; index--)
            {
                int entityId = spawnedIds[index];
                EntityPhysicsProp prop = world.GetEntity(entityId) as EntityPhysicsProp;
                spawnedIds.RemoveAt(index);

                if (prop == null || prop.belongsPlayerId != ownerId) continue;

                world.RemoveEntity(entityId, EnumRemoveEntityReason.Despawned);
                return;
            }
        }

        private static void ClearOwnedProps(World world, int ownerId)
        {
            List<int> entityIds = new List<int>();
            foreach (Entity entity in world.Entities.list)
            {
                if (entity is EntityPhysicsProp && entity.belongsPlayerId == ownerId)
                {
                    entityIds.Add(entity.entityId);
                }
            }

            foreach (int entityId in entityIds)
            {
                world.RemoveEntity(entityId, EnumRemoveEntityReason.Despawned);
            }

            spawnedPropIdsByOwner.Remove(ownerId);
        }
    }
}