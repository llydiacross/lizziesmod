using Platform;
using System;
using System.Collections;
using UnityEngine;

namespace LizziesMod
{
    public class TeleportUIController : XUiController
    {
        public float defaultTeleportDelayTime = 30f;
        public Vector3? previousTeleport;
        public Vector3? initialPosition;

        private int targetDay = 1;
        private int targetHours = 12;
        private int targetMinutes = 0;
        private int targetYear = 0;
        private int previousYear = 0;
        private bool isCrystalActive = false;

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

            XUiController btnDayUp = GetChildById("btnDayUp")?.GetChildById("clickable");
            if (btnDayUp != null )
                btnDayUp.OnPress += (s, e) => AdjustTime("day", 1);
            XUiController btnDayDown = GetChildById("btnDayDown")?.GetChildById("clickable");
            if(btnDayDown != null)
              btnDayDown.OnPress += (s, e) => AdjustTime("day", -1);

            XUiController btnTimeUp = GetChildById("btnTimeUp")?.GetChildById("clickable");
            if (btnTimeUp != null)
                btnTimeUp.OnPress += (s, e) => AdjustTime("time", 1);
            XUiController btnTimeDown = GetChildById("btnTimeDown")?.GetChildById("clickable");
            if (btnTimeDown != null)
                btnTimeDown.OnPress += (s, e) => AdjustTime("time", -1);

            XUiController btnYearUp = GetChildById("btnYearUp")?.GetChildById("clickable");
            if (btnYearUp != null)
                btnYearUp.OnPress += (s, e) => AdjustTime("year", 1);

            XUiController btnYearDown = GetChildById("btnYearDown")?.GetChildById("clickable");
            if (btnYearDown != null)
                btnYearDown.OnPress += (s, e) => AdjustTime("year", -1);
        }

        public override void OnOpen()
        {
            base.OnOpen();

            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;

            ItemValue heldItem = player.inventory.holdingItemItemValue;
            string itemName = heldItem != null ? heldItem.ItemClass.GetItemName() : "";
            isCrystalActive = (itemName == "crystalFluxTeleporter");

 
            ulong worldTime = GameManager.Instance.World.worldTime;
            long totalDays = GameUtils.WorldTimeToDays(worldTime);

            targetYear = 0;
            targetDay = (int)(totalDays % 357) + 1;
            targetHours = GameUtils.WorldTimeToHours(worldTime);
            targetMinutes = GameUtils.WorldTimeToMinutes(worldTime);

            XUiController headerLabel = GetChildById("lblHeader");
            XUiController upgradeText = GetChildById("lblUpgradeText");

            GetChildById("btnDayUp").viewComponent.IsVisible = isCrystalActive;
            GetChildById("btnDayDown").viewComponent.IsVisible = isCrystalActive;
            GetChildById("btnTimeUp").viewComponent.IsVisible = isCrystalActive;
            GetChildById("btnTimeDown").viewComponent.IsVisible = isCrystalActive;
            GetChildById("btnYearUp").viewComponent.IsVisible = isCrystalActive;
            GetChildById("btnYearDown").viewComponent.IsVisible = isCrystalActive;

            if (!isCrystalActive)
            {
                if (headerLabel?.viewComponent is XUiV_Label hLabel)
                {
                    hLabel.Text = "SIMPLE FLUX TELEPORTER";
                    hLabel.Color = new Color32(80, 180, 255, 255);
                }
                if (upgradeText?.viewComponent is XUiV_Label uLabel)
                {
                    uLabel.Text = "TIME MANIPULATION INACTIVE";
                    uLabel.Color = new Color32(200, 50, 50, 255);
                }
            }
            else
            {
                if (headerLabel?.viewComponent is XUiV_Label hLabel)
                {
                    hLabel.Text = "ADVANCED FLUX TELEPORTER";
                    hLabel.Color = new Color32(255, 100, 255, 255);
                }
                if (upgradeText?.viewComponent is XUiV_Label uLabel)
                {

                    if (previousTeleport == null)
                    {
                        uLabel.Text = "TIME MANIPULATION ACTIVE";
                        uLabel.Color = new Color32(50, 200, 50, 255);
                    }
                    else
                    {
                        uLabel.Text = "PREVIOUS TIME SELECTED";
                        uLabel.Color = new Color32(128, 0, 128, 255);
                    }
                }
            }

            UpdateTimeDisplay();


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

                        string locationName = isCrystalActive ? "Waypoint" : "Bedroll";
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

        private void AdjustTime(string type, int amount)
        {
            if (!isCrystalActive) return; 

            xui.mPlayerUI.localPlayer.entityPlayerLocal.PlayOneShot("weapon_click");

            if (type == "day")
            {
                targetDay += amount;
                if (targetDay < 1)
                {
                    targetYear -= 1;
                    targetDay = 357;
                }
                if (targetDay > 357)
                {
                    targetYear += 1;
                    targetDay = 1;
                }
            }
            else if (type == "time")
            {
                targetHours += amount;
                if (targetHours < 0)
                {
                    targetDay -= 1;
                    targetHours = 23;
                }
                if (targetHours > 23)
                {
                    targetDay += 1;
                    targetHours = 0;
                }
            }
            else if (type == "year")
            {
                targetYear += amount;
            }

            targetYear = Math.Max(targetYear, 0);

            UpdateTimeDisplay();
        }

        private void UpdateTimeDisplay()
        {
            if (!isCrystalActive)
            {
                if (GetChildById("lblDay")?.viewComponent is XUiV_Label dL) { dL.Text = "---"; dL.Color = new Color32(80, 80, 80, 255); }
                if (GetChildById("lblTime")?.viewComponent is XUiV_Label tL) { tL.Text = "--:--"; tL.Color = new Color32(80, 80, 80, 255); }
                if (GetChildById("lblYear")?.viewComponent is XUiV_Label yL) { yL.Text = "----"; yL.Color = new Color32(80, 80, 80, 255); }
            }
            else
            {
                // to normalize it to the games day setting which counts from zero
                if (GetChildById("lblDay")?.viewComponent is XUiV_Label dL) { dL.Text = (targetDay - 1).ToString("000"); dL.Color = new Color32(0, 255, 200, 255); }
                if (GetChildById("lblTime")?.viewComponent is XUiV_Label tL) { tL.Text = $"{targetHours:00}:{targetMinutes:00}"; tL.Color = new Color32(0, 255, 200, 255); }
                if (GetChildById("lblYear")?.viewComponent is XUiV_Label yL) { yL.Text = (TimeManager.GetStartingYear() + targetYear).ToString("0000"); yL.Color = new Color32(0, 255, 200, 255); }
            }
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

        private ulong? CalculateTargetWorldTime()
        {

            uint year = (uint)targetYear * 357;
            long totalDays = year + (targetDay - 1);

            ulong newWorldTime = (ulong)(totalDays * 24000L)
                               + (ulong)(targetHours * 1000L)
                               + (ulong)((targetMinutes * 1000L) / 60L);

            return newWorldTime;
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
                                ulong? timeToSet = CalculateTargetWorldTime();
                                initialPosition = player.position;
                                player.playerUI.windowManager.Open("windowFluxTeleportTimer", false);
                                GameManager.Instance.StartCoroutine(TeleportSequence(player, target, heldItem, timeToSet));
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
                    ulong? timeToSet = CalculateTargetWorldTime();
                    player.playerUI.windowManager.Open("windowFluxTeleportTimer", false);
                    GameManager.Instance.StartCoroutine(TeleportSequence(player, (Vector3)previousTeleport, heldItem, timeToSet, true, true));
                }
            }
            player.inventory.onInventoryChanged();
            xui.playerUI.windowManager.Close("windowTeleportSelector");
        }

        private IEnumerator TeleportSequence(EntityPlayerLocal player, Vector3 targetPos, ItemValue heldItem, ulong? newWorldTime = null, bool noDuration = false, bool clearPreviousTeleport = false)
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
            }
            else
            {
                if (!clearPreviousTeleport)
                {
                    previousTeleport = initialPosition;
                    previousYear = targetYear;
                }
                else
                {
                    previousTeleport = null;
                    previousYear = 0;
                }

                heldItem.UseTimes += 100f;
                player.inventory.onInventoryChanged();

                if (newWorldTime.HasValue) GameManager.Instance.World.worldTime = newWorldTime.Value;
                TimeManager.UpdateCurrentYear();

                player.SetPosition(targetPos, true);
                player.PlayOneShot("weapon_electric_charge");
                GameManager.ShowTooltip(player, "Jump complete!");
            }
        }

        private void HandleCloseClick(XUiController _sender, int _mouseButton) => xui.playerUI.windowManager.Close("windowTeleportSelector");
        private void HandleCancelTeleportClick(XUiController _sender, int _mouseButton)
        {
            xui.mPlayerUI.localPlayer.entityPlayerLocal.Buffs.RemoveBuff("buffFluxTeleporting");
            xui.playerUI.windowManager.Close("windowTeleportSelector");
        }
    }
}