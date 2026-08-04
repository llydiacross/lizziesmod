using System;

namespace LizziesMod
{
    public class ItemActionPlayCassette : ItemAction
    {
        private const string WalkmanItemName = "itemWalkman";

        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
            if (_bReleased) return;

            EntityPlayerLocal player = _actionData.invData.holdingEntity as EntityPlayerLocal;
            if (player == null || _actionData.invData.itemValue == null || _actionData.invData.itemValue.ItemClass == null) return;

            ItemClass walkmanClass = ItemClass.GetItemClass(WalkmanItemName, false);
            if (walkmanClass == null || player.inventory.GetItemCount(new ItemValue(walkmanClass.Id), true) < 1)
            {
                GameManager.ShowTooltip(player, "[FF0000]A Walkman is required to play cassettes.[-]");
                player.PlayOneShot("ui_denied");
                return;
            }

            string trackName;
            if (!_actionData.invData.itemValue.ItemClass.Properties.Values.TryGetValue("TrackName", out trackName) ||
                string.IsNullOrEmpty(trackName))
            {
                GameManager.ShowTooltip(player, "[FF0000]Cassette data is missing.[-]");
                return;
            }

            if (CustomAudioManager.Instance != null)
            {
                CustomAudioManager.Instance.ToggleWalkmanTrack(trackName, player);
            }
        }
    }

    public class ItemActionWalkman : ItemActionPlayCassette { }
}