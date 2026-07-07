using Audio;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class ItemActionTeleport : ItemAction
    {
     
        public static List<Vector3> teleportLocations = new List<Vector3>();

        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
            EntityPlayerLocal player = _actionData.invData.holdingEntity as EntityPlayerLocal;
            if (player == null) return;

            ItemValue itemValue = _actionData.invData.itemValue;
            string itemName = itemValue != null ? itemValue.ItemClass.GetItemName() : "";


            if (!ModSettingsManager.GetSetting<bool>("LizziesMod", "EnableTimeParadox"))
            {
                GameManager.ShowTooltip(player, "Time Travel paradoxes are disabled!");
                player.PlayOneShot("ui_denied");
                return;
            }

            if (itemValue.UseTimes >= itemValue.MaxUseTimes)
            {
                GameManager.ShowTooltip(player, "Device out of charge!");
                player.PlayOneShot("ui_denied");
                return;
            }

            teleportLocations.Clear();

            if (itemName == "crystalFluxTeleporter")
            {

                if (player.Waypoints != null && player.Waypoints.Collection != null)
                {
                    foreach (Waypoint w in player.Waypoints.Collection.list)
                    {
                        teleportLocations.Add(w.pos.ToVector3());
                    }
                }
            }
            else
            {

                if (player.spawnPoints != null)
                {
                    for (int i = 0; i < player.spawnPoints.Count; i++)
                    {
                        teleportLocations.Add(player.spawnPoints[i]);
                    }
                }
            }

            if (teleportLocations.Count > 0)
            {
                LocalPlayerUI.GetUIForPlayer(player).windowManager.Open("windowTeleportSelector", true);
            }
            else
            {
                string msg = itemName == "crystalFluxTeleporter" ? "No Waypoints set on map!" : "No bedroll set!";
                GameManager.ShowTooltip(player, msg);
                player.PlayOneShot("ui_denied");
            }
        }
    }
}