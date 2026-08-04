using System;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public enum JukeboxCommand : byte
    {
        RequestLibrary,
        InsertDisc,
        PlayTrack,
        SetPrice
    }

    public static class JukeboxManager
    {
        public const string ModName = "LizziesMod_JukeboxAndWalkman";
        public const int MaxPrice = 10000;
        private const string JukeboxBlockName = "blockJukebox";
        private const string CoinItemName = "casinoCoin";
        private const string DiscMediaType = "disc";
        private const float MaxUseDistance = 6f;

        public static void RequestLibrary(EntityPlayerLocal player, Vector3 position)
        {
            SendCommand(player, JukeboxCommand.RequestLibrary, ToBlockPosition(position), "", -1, 0);
        }

        public static void RequestInsertDisc(EntityPlayerLocal player, Vector3i position, int slotIndex, string trackId)
        {
            SendCommand(player, JukeboxCommand.InsertDisc, position, trackId, slotIndex, 0);
        }

        public static void RequestPlayTrack(EntityPlayerLocal player, Vector3 position, string trackId)
        {
            SendCommand(player, JukeboxCommand.PlayTrack, ToBlockPosition(position), trackId, -1, 0);
        }

        public static void RequestSetPrice(EntityPlayerLocal player, Vector3 position, int price)
        {
            SendCommand(player, JukeboxCommand.SetPrice, ToBlockPosition(position), "", -1, price);
        }

        public static void ProcessServerCommand(
            World world,
            EntityPlayer player,
            JukeboxCommand command,
            Vector3i position,
            string trackId,
            int slotIndex,
            int price)
        {
            TileEntityPowered jukebox;
            if (!TryGetPoweredJukebox(world, player, position, out jukebox))
            {
                SendFeedback(player, "[FF0000]A powered Jukebox must be nearby.[-]", true);
                return;
            }

            switch (command)
            {
                case JukeboxCommand.RequestLibrary:
                    SendLibrarySnapshot(player, position);
                    break;
                case JukeboxCommand.InsertDisc:
                    InsertDisc(player, position, slotIndex, trackId);
                    break;
                case JukeboxCommand.PlayTrack:
                    PlayTrack(player, position, trackId);
                    break;
                case JukeboxCommand.SetPrice:
                    if (JukeboxLibraryManager.TrySetPrice(position, GetPersistentPlayerId(player), price))
                    {
                        SendLibrarySnapshot(player, position);
                        SendFeedback(player, "Jukebox price updated.", false);
                    }
                    else SendFeedback(player, "[FF0000]Only the Jukebox owner can set a price between 0 and " + MaxPrice + ".[-]", true);
                    break;
            }
        }

        private static void SendCommand(
            EntityPlayerLocal player,
            JukeboxCommand command,
            Vector3i position,
            string trackId,
            int slotIndex,
            int price)
        {
            if (player == null) return;

            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null) return;

            if (connectionManager.IsServer)
            {
                ProcessServerCommand(GameManager.Instance.World, player, command, position, trackId, slotIndex, price);
                return;
            }

            connectionManager.SendToServer(
                NetPackageManager.GetPackage<NetPackageJukeboxCommand>().Setup(command, position, trackId, slotIndex, price),
                false);
        }

        private static bool TryGetPoweredJukebox(World world, EntityPlayer player, Vector3i position, out TileEntityPowered jukebox)
        {
            jukebox = null;
            if (world == null || player == null || !IsFeatureEnabled()) return false;

            BlockValue blockValue = world.GetBlock(position);
            if (blockValue.Block == null ||
                !blockValue.Block.GetBlockName().Equals(JukeboxBlockName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if ((player.position - position.ToVector3()).sqrMagnitude > MaxUseDistance * MaxUseDistance)
            {
                Logger.Warning($"[Jukebox] Rejected out-of-range request from player {player.entityId}.");
                return false;
            }

            jukebox = world.GetTileEntity(position) as TileEntityPowered;
            if (jukebox == null || !jukebox.IsPowered) return false;

            return true;
        }

        private static void InsertDisc(EntityPlayer player, Vector3i position, int slotIndex, string requestedTrackId)
        {
            if (!TryGetDiscTrack(player, slotIndex, requestedTrackId, out ItemStack discStack))
            {
                SendFeedback(player, "[FF0000]That disc is not valid for this Jukebox.[-]", true);
                return;
            }
            if (CustomAudioManager.Instance == null || !CustomAudioManager.Instance.HasTrack(requestedTrackId))
            {
                SendFeedback(player, "[FF0000]The disc's track is not installed.[-]", true);
                return;
            }

            JukeboxLibraryState library = JukeboxLibraryManager.GetLibrary(position);
            if (library.UnlockedTrackIds.Contains(requestedTrackId))
            {
                SendLibrarySnapshot(player, position);
                SendFeedback(player, "[FFCC33]That track is already unlocked.[-]", true);
                return;
            }

            if (player.inventory.DecItem(discStack.itemValue, 1, false, null) <= 0)
            {
                SendFeedback(player, "[FF0000]The disc could not be removed from your inventory.[-]", true);
                return;
            }

            player.inventory.onInventoryChanged();
            if (!JukeboxLibraryManager.TryUnlockTrack(position, GetPersistentPlayerId(player), requestedTrackId)) return;

            Logger.Info($"[Jukebox] Player {player.entityId} unlocked '{requestedTrackId}'.");
            SendLibrarySnapshot(player, position);
            SendFeedback(player, "[00FF00]Track unlocked.[-]", false);
        }

        private static void PlayTrack(EntityPlayer player, Vector3i position, string trackId)
        {
            if (string.IsNullOrEmpty(trackId) || CustomAudioManager.Instance == null || !CustomAudioManager.Instance.HasTrack(trackId)) return;

            JukeboxLibraryState library = JukeboxLibraryManager.GetLibrary(position);
            if (!library.UnlockedTrackIds.Contains(trackId))
            {
                Logger.Warning($"[Jukebox] Rejected locked track '{trackId}' for player {player.entityId}.");
                SendFeedback(player, "[FF0000]That track has not been unlocked.[-]", true);
                return;
            }

            if (!JukeboxLibraryManager.IsOwner(position, GetPersistentPlayerId(player)) &&
                !TryChargePlayer(player, library.Price))
            {
                SendFeedback(player, "[FF0000]You do not have enough Dukes for this Jukebox.[-]", true);
                return;
            }

            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null) return;

            Vector3 worldPosition = position.ToVector3();
            connectionManager.SendPackage(
                NetPackageManager.GetPackage<NetPackageJukeboxPlay>().Setup(worldPosition, trackId),
                false,
                -1,
                -1,
                -1,
                null);

            if (!GameManager.IsDedicatedServer && CustomAudioManager.Instance != null)
            {
                CustomAudioManager.Instance.PlayJukeboxTrack(worldPosition, trackId);
            }
        }

        private static void SendLibrarySnapshot(EntityPlayer player, Vector3i position)
        {
            JukeboxLibraryState library = JukeboxLibraryManager.GetLibrary(position);
            List<string> trackIds = JukeboxLibraryManager.GetUnlockedTrackIds(position);
            bool isOwner = JukeboxLibraryManager.IsOwner(position, GetPersistentPlayerId(player));
            Vector3 worldPosition = position.ToVector3();

            if (player is EntityPlayerLocal)
            {
                JukeboxUIController.ReceiveLibrarySnapshot(worldPosition, trackIds, library.Price, isOwner);
            }

            ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (connectionManager == null) return;

            connectionManager.SendPackage(
                NetPackageManager.GetPackage<NetPackageJukeboxLibrary>().Setup(worldPosition, trackIds, library.Price, isOwner),
                true,
                player.entityId,
                -1,
                -1,
                null);
        }

            private static void SendFeedback(EntityPlayer player, string message, bool denied)
            {
                if (player is EntityPlayerLocal)
                {
                    JukeboxUIController.ShowFeedback(message, denied);
                }

                ConnectionManager connectionManager = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (connectionManager == null) return;

                connectionManager.SendPackage(
                NetPackageManager.GetPackage<NetPackageJukeboxFeedback>().Setup(message, denied),
                true,
                player.entityId,
                -1,
                -1,
                null);
            }

        private static bool TryGetDiscTrack(EntityPlayer player, int slotIndex, string requestedTrackId, out ItemStack discStack)
        {
            discStack = new ItemStack();
            if (player == null || slotIndex < 0 || string.IsNullOrEmpty(requestedTrackId)) return false;

            ItemStack stack = player.inventory.GetItemStack(slotIndex);
            if (stack == null || stack.count <= 0 || stack.itemValue == null || stack.itemValue.ItemClass == null) return false;

            Dictionary<string, string> properties = stack.itemValue.ItemClass.Properties.Values;
            string mediaType;
            string trackId;
            if (!properties.TryGetValue("MediaType", out mediaType) ||
                !properties.TryGetValue("TrackName", out trackId) ||
                !DiscMediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase) ||
                !requestedTrackId.Equals(trackId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            discStack = stack;
            return true;
        }

        private static bool TryChargePlayer(EntityPlayer player, int price)
        {
            if (price <= 0) return true;

            ItemClass coinClass = ItemClass.GetItemClass(CoinItemName, false);
            if (coinClass == null) return false;

            ItemValue coinValue = new ItemValue(coinClass.Id);
            if (player.inventory.GetItemCount(coinValue, true) < price) return false;

            return player.inventory.DecItem(coinValue, price, false, null) >= price;
        }

        private static bool IsFeatureEnabled()
        {
            return global::ModManager.GetMod(ModName) != null &&
                   ModPatcher.IsModEnabled(ModName) &&
                   ModSettingsManager.GetSetting<bool>(ModName, "Enabled", true);
        }

        private static string GetPersistentPlayerId(EntityPlayer player)
        {
            if (player != null && player.PersistentPlayerData != null && player.PersistentPlayerData.PrimaryId != null)
            {
                return player.PersistentPlayerData.PrimaryId.ToString();
            }

            return player == null ? "" : "entity:" + player.entityId;
        }

        private static Vector3i ToBlockPosition(Vector3 position)
        {
            return new Vector3i(
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.y),
                Mathf.RoundToInt(position.z));
        }
    }
}