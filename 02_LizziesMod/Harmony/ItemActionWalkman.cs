using System;

namespace LizziesMod
{
    public class ItemActionWalkman : ItemAction
    {
        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
            if (_bReleased) return;

            string trackName = "";
            if (_actionData.invData.itemValue.ItemClass.Properties.Values.ContainsKey("TrackName"))
            {
                trackName = _actionData.invData.itemValue.ItemClass.Properties.Values["TrackName"];
            }
            else
            {
                string itemName = _actionData.invData.itemValue.ItemClass.GetItemName();
                trackName = itemName.Replace("musicDisc_", "");
            }

            EntityPlayerLocal player = _actionData.invData.holdingEntity as EntityPlayerLocal;

            if (CustomAudioManager.Instance != null && player != null)
            {
           
                CustomAudioManager.Instance.ToggleWalkmanTrack(trackName, player);
            }
        }
    }
}