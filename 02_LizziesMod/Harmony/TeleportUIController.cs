using System.Collections;
using UnityEngine;

namespace LizziesMod
{
    public class TeleportUIController : XUiController
    {
        public float defaultTeleportDelayTime = 30f;
        public Vector3? previousTeleport;
        public Vector3? initialPosition;

        private bool usesWaypoints;

        public override void Init()
        {
            base.Init();

            if (xui == null) return;


            for (int i = 1; i <= 5; i++)
            {
                XUiController btnCtrl = GetChildById($"btnTeleport{i}")?.GetChildById("clickable");
                if (btnCtrl != null) btnCtrl.OnPress += HandleTeleportClick;
            }

            XUiController closeBtn = GetChildById("btnClose")?.GetChildById("clickable");
            if (closeBtn != null) closeBtn.OnPress += HandleCloseClick;

            XUiController cancelBtn = GetChildById("btnCancelTeleport")?.GetChildById("clickable");
            if (cancelBtn != null) cancelBtn.OnPress += HandleCancelTeleportClick;

            XUiController prevBtn = GetChildById("btnPreviousTeleport")?.GetChildById("clickable");
            if (prevBtn != null) prevBtn.OnPress += HandlePreviousTeleportClick;

        }

        public override void OnOpen()
        {
            base.OnOpen();

            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;

            ItemValue heldItem = player.inventory.holdingItemItemValue;
            string itemName = heldItem != null ? heldItem.ItemClass.GetItemName() : "";
            usesWaypoints = itemName == "crystalFluxTeleporter";

            XUiController headerLabel = GetChildById("lblHeader");
            XUiController upgradeText = GetChildById("lblUpgradeText");

            SetTimeControlsVisible(false);

            if (!usesWaypoints)
            {
                if (headerLabel?.viewComponent is XUiV_Label hLabel)
                {
                    hLabel.Text = "SIMPLE FLUX TELEPORTER";
                    hLabel.Color = new Color32(80, 180, 255, 255);
                }
                if (upgradeText?.viewComponent is XUiV_Label uLabel)
                {
                    uLabel.Text = "BEDROLL TELEPORTATION READY";
                    uLabel.Color = new Color32(80, 180, 255, 255);
                }
            }
            else
            {
                if (headerLabel?.viewComponent is XUiV_Label hLabel)
                {
                    hLabel.Text = "CRYSTAL FLUX TELEPORTER";
                    hLabel.Color = new Color32(255, 100, 255, 255);
                }
                if (upgradeText?.viewComponent is XUiV_Label uLabel)
                {
                    uLabel.Text = previousTeleport == null ? "WAYPOINT TELEPORTATION READY" : "RETURN JUMP READY";
                    uLabel.Color = new Color32(255, 100, 255, 255);
                }
            }

            SetTimeLabelsVisible(false);


            for (int i = 0; i < 5; i++)
            {
                XUiController btnRow = GetChildById($"btnTeleport{i + 1}");
                if (btnRow != null)
                {
                    bool active = i < ItemActionTeleport.teleportLocations.Count;
                    btnRow.viewComponent.IsVisible = active;

                    if (active)
                    {
                        Vector3 target = ItemActionTeleport.teleportLocations[i];
                        float distance = Vector3.Distance(player.position, target);
                        string distStr = distance >= 1000f ? $"{(distance / 1000f):F1} km" : $"{distance:F0} m";
                        string coords = $"X: {(int)target.x}, Z: {(int)target.z}";

                        string locationName = usesWaypoints ? "Waypoint" : "Bedroll";
                        string finalText = $"{locationName}: {distStr} Away  |  [{coords}]";

                        XUiController labelCtrl = btnRow.GetChildById("btnText");
                        if (labelCtrl != null && labelCtrl.viewComponent is XUiV_Label labelView)
                        {
                            labelView.Text = finalText;
                        }
                    }
                }
            }
        }

        private void SetTimeControlsVisible(bool visible)
        {
            SetVisible("btnDayUp", visible);
            SetVisible("btnDayDown", visible);
            SetVisible("btnTimeUp", visible);
            SetVisible("btnTimeDown", visible);
            SetVisible("btnYearUp", visible);
            SetVisible("btnYearDown", visible);
        }

        private void SetTimeLabelsVisible(bool visible)
        {
            SetVisible("lblDay", visible);
            SetVisible("lblTime", visible);
            SetVisible("lblYear", visible);
        }

        private void SetVisible(string controlId, bool visible)
        {
            XUiController control = GetChildById(controlId);
            if (control != null && control.viewComponent != null) control.viewComponent.IsVisible = visible;
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;

            XUiController cancelBtn = GetChildById("btnCancelTeleport");
            if (cancelBtn != null && player != null) cancelBtn.viewComponent.IsVisible = player.Buffs.HasBuff("buffFluxTeleporting");

            XUiController previousTeleportBtn = GetChildById("btnPreviousTeleport");
            if (previousTeleportBtn != null && player != null) previousTeleportBtn.viewComponent.IsVisible = (previousTeleport != null);
        }

        private void HandleTeleportClick(XUiController _sender, int _mouseButton)
        {
            if (xui == null || xui.playerUI == null) return;

            XUiController parent = _sender.Parent;
            if (parent == null) return;

            string id = parent.viewComponent.ID;
            string numberPart = id.Replace("btnTeleport", "");

            if (int.TryParse(numberPart, out int index))
            {
                index -= 1;
                if (index >= 0 && index < ItemActionTeleport.teleportLocations.Count)
                {
                    Vector3 target = ItemActionTeleport.teleportLocations[index];
                    EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;

                    if (player != null)
                    {
                        ItemValue heldItem = player.inventory.holdingItemItemValue;
                        if (heldItem != null)
                        {
                            if (heldItem.UseTimes >= heldItem.MaxUseTimes)
                            {
                                GameManager.ShowTooltip(player, "Not enough juice!");
                                player.PlayOneShot("weapon_jam");
                            }
                            else
                            {
                                initialPosition = player.position;
                                player.playerUI.windowManager.Open("windowFluxTeleportTimer", false);
                                GameManager.Instance.StartCoroutine(TeleportSequence(player, target, heldItem));
                            }
                        }
                    }
                    player.inventory.onInventoryChanged();
                    xui.playerUI.windowManager.Close("windowTeleportSelector");
                }
            }
        }

        private void HandlePreviousTeleportClick(XUiController _sender, int _mouseButton)
        {
            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;
            if (player != null && previousTeleport != null)
            {
                ItemValue heldItem = player.inventory.holdingItemItemValue;
                if (heldItem != null && heldItem.UseTimes < heldItem.MaxUseTimes)
                {
                    player.playerUI.windowManager.Open("windowFluxTeleportTimer", false);
                    GameManager.Instance.StartCoroutine(TeleportSequence(player, (Vector3)previousTeleport, heldItem, true, true));
                }
            }
            player.inventory.onInventoryChanged();
            xui.playerUI.windowManager.Close("windowTeleportSelector");
        }

        private IEnumerator TeleportSequence(EntityPlayerLocal player, Vector3 targetPos, ItemValue heldItem, bool noDuration = false, bool clearPreviousTeleport = false)
        {
            float duration = EffectManager.GetValue(PassiveEffects.MagazineSize, heldItem, defaultTeleportDelayTime, player, null, FastTags<TagGroup.Global>.Parse("teleportTime"));
            float elapsed = 0f;
            if (noDuration) duration = 1f;

            XUiWindowGroup timerGroup = (XUiWindowGroup)player.playerUI.windowManager.GetWindow("windowFluxTeleportTimer");
            XUiController labelCtrl = timerGroup?.Controller?.GetChildById("lblTimer");
            XUiV_Label lblView = labelCtrl?.viewComponent as XUiV_Label;

            player.Buffs.AddBuff("buffFluxTeleporting");
            GameManager.ShowTooltip(player, "Preparing to teleport!");

            try
            {
                while (elapsed < duration)
                {
                    if (lblView != null) lblView.Text = $"FLUX JUMP IN T-MINUS {Mathf.CeilToInt(duration - elapsed)}";
                    if (!player.Buffs.HasBuff("buffFluxTeleporting"))
                    {
                        GameManager.ShowTooltip(player, "Teleport aborted!");
                        player.PlayOneShot("alarm1_oneshot");
                        yield break;
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                if (player.playerUI != null && player.playerUI.windowManager != null)
                {
                    player.playerUI.windowManager.Close("windowFluxTeleportTimer");
                }
            }

            player.Buffs.RemoveBuff("buffFluxTeleporting");

            if (heldItem.UseTimes >= heldItem.MaxUseTimes)
            {
                player.PlayOneShot("alarm1_oneshot");
                yield break;
            }

            if (!clearPreviousTeleport)
            {
                previousTeleport = initialPosition;
            }
            else
            {
                previousTeleport = null;
            }

            heldItem.UseTimes += 100f;
            player.inventory.onInventoryChanged();

            player.SetPosition(targetPos, true);
            Rigidbody playerRb = player.RootTransform.GetComponent<Rigidbody>();
            if (playerRb != null) playerRb.isKinematic = true;

            yield return null;

            if (playerRb != null) playerRb.isKinematic = false;

            player.PlayOneShot("weapon_electric_charge");
            GameManager.ShowTooltip(player, "Teleport complete!");
        }
    
        private void HandleCloseClick(XUiController _sender, int _mouseButton) => xui.playerUI.windowManager.Close("windowTeleportSelector");
        private void HandleCancelTeleportClick(XUiController _sender, int _mouseButton)
        {
            xui.mPlayerUI.localPlayer.entityPlayerLocal.Buffs.RemoveBuff("buffFluxTeleporting");
            xui.playerUI.windowManager.Close("windowTeleportSelector");
        }
    }
}