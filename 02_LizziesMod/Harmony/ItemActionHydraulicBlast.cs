using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Webserver.WebAPI.APIs.WorldState;

namespace LizziesMod
{
    public class ItemActionHydraulicBlastData : ItemActionDynamicMelee.ItemActionDynamicMeleeData
    {
        public float SteamPressure = 0f; // Current Steam (0 to 100)
        public float MaxCharge = 200f;
        public float ChargeRate = 10f; // Steam per second while charging

        public ItemActionHydraulicBlastData(ItemInventoryData _invData, int _indexInEntityOfAction)
            : base(_invData, _indexInEntityOfAction) { }
    }

    public class ItemActionHydraulicBlast : ItemActionDynamicMelee
    {

        public override void OnHoldingUpdate(ItemActionData _actionData)
        {
            base.OnHoldingUpdate(_actionData);
            ItemActionHydraulicBlastData data = _actionData as ItemActionHydraulicBlastData;
            if (data == null) return;
            var player = _actionData.invData.holdingEntity as EntityPlayer;

       
            if (data.SteamPressure < data.MaxCharge)
            {

                if (!player.Buffs.HasBuff("buffSteamPressure"))
                    player.Buffs.AddBuff("buffSteamPressure");

                if (player.Buffs.HasBuff("buffSteamPressure"))
                {
                    data.SteamPressure += data.ChargeRate * Time.deltaTime;
                    player.Buffs.GetBuff("buffSteamPressure").DurationInTicks = (uint)data.SteamPressure;
                }
            }
            else
            {
                if (player.Buffs.HasBuff("buffSteamPressure"))
                    player.Buffs.RemoveBuff("buffSteamPressure");
            }
        }

        public override ItemActionData CreateModifierData(ItemInventoryData _invData, int _indexInEntityOfAction)
        {
            return new ItemActionHydraulicBlastData(_invData, _indexInEntityOfAction);
        }

        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {

            ItemActionHydraulicBlastData data = _actionData as ItemActionHydraulicBlastData;

            if (data != null)
            {
                if (data.SteamPressure >= 20f)
                {
  
                    float staminaBefore = _actionData.invData.holdingEntity.Stats.Stamina.Value;
                    data.SteamPressure -= 20f;
                    base.ExecuteAction(_actionData, _bReleased);

                    // play a noise
                    _actionData.invData.holdingEntity.PlayOneShot("junksledge_fire");

                    float staminaAfter = _actionData.invData.holdingEntity.Stats.Stamina.Value;
                    float staminaLost = staminaBefore - staminaAfter;

                    if (staminaLost > 0)
                    {
                        _actionData.invData.holdingEntity.Stats.Stamina.Value += staminaLost;
                    }
                } 
                else
                {
                    Logger.Info("No steam?");
                }
            }
            else
            {
                base.ExecuteAction(_actionData, _bReleased);
            }
        }

        public override void hitTarget(ItemActionData _actionData, WorldRayHitInfo _hitInfo, bool _isGrazingHit = false)
        {

            base.hitTarget(_actionData, _hitInfo, _isGrazingHit);
            // Ensure we have a valid hit target and an attacking entity
            if (_hitInfo == null || _hitInfo.tag == null || _actionData == null) return;
            EntityAlive attacker = _actionData.invData.holdingEntity;
            if (attacker == null) return;

            // dont interact with terrain
            if (_hitInfo.tag != null && GameUtils.IsBlockOrTerrain(_hitInfo.tag))
            {
                attacker.PlayOneShot("flametrap_end");
                return;
            }

            EntityAlive targetEntity = _hitInfo.transform.GetComponent<EntityAlive>();
            if (targetEntity != null && targetEntity.IsAlive())
            {

                Vector3 pushDirection = (targetEntity.position - attacker.position).normalized;
                pushDirection.y += 0.4f;
                float forceMultiplier = 64.0f;

                targetEntity.Buffs.AddBuff("buffRagdoll"); // ragdoll the enemy instantly
                targetEntity.AddVelocity(pushDirection * forceMultiplier);
                targetEntity.PlayOneShot("flametrap_start");
            }
        }
    }
}