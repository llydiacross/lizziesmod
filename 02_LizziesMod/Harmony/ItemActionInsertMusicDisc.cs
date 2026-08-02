namespace LizziesMod
{
    public class ItemActionInsertMusicDisc : ItemAction
    {
        public override void ExecuteAction(ItemActionData actionData, bool released)
        {
            if (released) return;

            EntityPlayerLocal player = actionData.invData.holdingEntity as EntityPlayerLocal;
            if (player == null || actionData.invData.itemValue == null || actionData.invData.itemValue.ItemClass == null) return;

            string trackId;
            if (!actionData.invData.itemValue.ItemClass.Properties.Values.TryGetValue("TrackName", out trackId) ||
                string.IsNullOrEmpty(trackId))
            {
                GameManager.ShowTooltip(player, "[FF0000]Music disc data is missing.[-]");
                return;
            }

            WorldRayHitInfo hit = GetExecuteActionTarget(actionData);
            if (!hit.bHitValid)
            {
                GameManager.ShowTooltip(player, "[FF0000]Aim at a powered Jukebox to insert this disc.[-]");
                return;
            }

            JukeboxManager.RequestInsertDisc(player, hit.lastBlockPos, actionData.invData.slotIdx, trackId);
        }
    }
}