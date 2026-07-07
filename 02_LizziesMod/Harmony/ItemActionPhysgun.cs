using HarmonyLib;
using System;
using UnityEngine;

namespace LizziesMod
{
    public class ItemActionPhysgunData : ItemActionData
    {
        public Entity GrabbedEntity;
        public Rigidbody GrabbedRigidbody;
        public GameObject LaserObj;
        public LineRenderer LaserRenderer;
        public float GrabDistance;
        public bool isHolding;

        public ItemActionPhysgunData(ItemInventoryData _invData, int _indexInEntityOfAction) : base(_invData, _indexInEntityOfAction)
        {
        }
    }

    public class ItemActionPhysgun : ItemAction
    {
        public override ItemActionData CreateModifierData(ItemInventoryData _invData, int _indexInEntityOfAction)
        {
            return new ItemActionPhysgunData(_invData, _indexInEntityOfAction);
        }

        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
            ItemActionPhysgunData data = (ItemActionPhysgunData)_actionData;
            EntityPlayerLocal player = data.invData.holdingEntity as EntityPlayerLocal;

            if (player == null) return;

            if (!player.isAdmin && ModSettingsManager.GetSetting<bool>("LizziesMod_Physgun", "AdminOnly", true))
            {
                GameManager.ShowTooltip(player, "Sorry only admins can have fun it seems...");
                return;
            }

            if (!_bReleased)
            {
                if (data.GrabbedEntity == null)
                {
                    GrabEntity(player, data);
                }
            }
            else
            {
                ReleaseEntity(player, data, false);
            }
        }

        public override void CancelAction(ItemActionData _actionData)
        {
            base.CancelAction(_actionData);
            if (_actionData is ItemActionPhysgunData data)
            {
                EntityPlayerLocal player = data.invData != null ? data.invData.holdingEntity as EntityPlayerLocal : null;
                ReleaseEntity(player, data, false);
            }
        }

        public override void Cleanup(ItemActionData _actionData)
        {
            base.Cleanup(_actionData);
            ItemActionPhysgunData data = (ItemActionPhysgunData)_actionData;
            if (data != null && data.invData != null)
            {
                ReleaseEntity(data.invData.holdingEntity as EntityPlayerLocal, data, false);
            }
        }

        public override void OnHoldingUpdate(ItemActionData _actionData)
        {
            base.OnHoldingUpdate(_actionData);

            ItemActionPhysgunData data = (ItemActionPhysgunData)_actionData;
            EntityPlayerLocal player = data.invData.holdingEntity as EntityPlayerLocal;

            if (player == null || player.AttachedToEntity != null || data.GrabbedEntity == null)
            {
                ReleaseEntity(player, data, false);
                return;
            }

            ItemValue itemValue = data.invData.itemValue;
            if (itemValue != null && itemValue.UseTimes >= itemValue.MaxUseTimes)
            {
                GameManager.ShowTooltip(player, "Physgun out of charge!");
                player.PlayOneShot("weapon_jam");
                ReleaseEntity(player, data, false);
                return;
            }

            // freeze
            if (Input.GetMouseButtonDown(1))
            {

                Rigidbody[] rbsToFreeze = data.GrabbedEntity.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in rbsToFreeze)
                {
                    if (rb != null) rb.isKinematic = true;
                }

                data.GrabbedEntity.motion = Vector3.zero;
                ReleaseEntity(player, data, true);
                return;
            }

            // when reload is pressed, unfreeze all rigit bodies
            if (Input.GetKeyDown(KeyCode.R))
            {
         
                if (data.GrabbedEntity is EntityAlive aliveTarget)
                {
                    aliveTarget.Buffs.RemoveBuff("buffRagdoll");
                }

                Rigidbody[] rbsToUnfreeze = data.GrabbedEntity.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in rbsToUnfreeze)
                {
                    if (rb != null)
                    {
                        rb.isKinematic = false;
                        rb.WakeUp();
                    }
                }
                player.PlayOneShot("weapon_pump_shotgun_fire");
                ReleaseEntity(player, data, false);
                return;
            }

            // middle click (make alive target a ragdoll) must be last
            if (Input.GetMouseButtonDown(2))
            {

                if (data.GrabbedEntity is EntityAlive aliveTarget)
                {
                    aliveTarget.Buffs.AddBuff("buffRagdoll");

                }
            }

            // scroll wheel
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                data.GrabDistance += scroll * 15f;
                data.GrabDistance = Mathf.Clamp(data.GrabDistance, 2f, 100f);
            }

            Transform camTransform = player.cameraTransform;
            Vector3 targetPosition = camTransform.position + (camTransform.forward * data.GrabDistance);

            bool isAI = data.GrabbedEntity is EntityAlive aliveEntity &&
                                    aliveEntity.IsAlive() &&
                                    !aliveEntity.Buffs.HasBuff("buffRagdoll") &&
                                    (aliveEntity.emodel == null || !aliveEntity.emodel.IsRagdollActive);

            if (isAI)
            {
                Vector3 currentPos = data.GrabbedEntity.position;
                Vector3 newPos = Vector3.Lerp(currentPos, targetPosition, Time.deltaTime * 30f);

                data.GrabbedEntity.SetPosition(newPos, true);
                data.GrabbedEntity.motion = Vector3.zero;

                Rigidbody[] rbs = data.GrabbedEntity.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in rbs)
                {
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }
            else
            {

                if (data.GrabbedRigidbody == null || data.GrabbedRigidbody.isKinematic)
                {
                    Rigidbody[] rbs = data.GrabbedEntity.GetComponentsInChildren<Rigidbody>();
                    foreach (Rigidbody rb in rbs)
                    {
                        if (rb != null && !rb.isKinematic)
                        {
                            data.GrabbedRigidbody = rb;
                            break;
                        }
                    }
                }

                if (data.GrabbedRigidbody != null && !data.GrabbedRigidbody.isKinematic)
                {
                    data.GrabbedRigidbody.WakeUp();
                    Vector3 direction = targetPosition - data.GrabbedRigidbody.position;
                    data.GrabbedRigidbody.velocity = direction * 15f;
                    data.GrabbedRigidbody.angularVelocity = Vector3.Lerp(data.GrabbedRigidbody.angularVelocity, Vector3.zero, Time.deltaTime * 5f);
                }
                else
                {
                    Vector3 newPos = Vector3.Lerp(data.GrabbedEntity.position, targetPosition, Time.deltaTime * 30f);
                    data.GrabbedEntity.SetPosition(newPos, true);
                }
            }

            data.GrabbedEntity.fallDistance = 0f;
            UpdateLaser(player, data);
        }

        private void GrabEntity(EntityPlayerLocal player, ItemActionPhysgunData data)
        {
            if (player.AttachedToEntity != null) return;

            ItemValue itemValue = data.invData.itemValue;
            if (itemValue != null && itemValue.UseTimes >= itemValue.MaxUseTimes)
            {
                GameManager.ShowTooltip(player, "Physgun out of charge! Reload with Flux Cells.");
                player.PlayOneShot("ui_denied");
                return;
            }

            Ray ray = new Ray(player.cameraTransform.position, player.cameraTransform.forward);
            int allLayersMask = ~0;
            float grabRadius = 0.5f;

            RaycastHit[] hits = Physics.SphereCastAll(ray, grabRadius, 50f, allLayersMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // get the closest oone

            foreach (RaycastHit hit in hits)
            {
                Entity hitEntity = hit.transform.GetComponentInParent<Entity>();

                if (hitEntity == null)
                {
                    var rootRef = hit.transform.GetComponentInParent<RootTransformRefEntity>();
                    if (rootRef != null && rootRef.RootTransform != null)
                    {
                        hitEntity = rootRef.RootTransform.GetComponent<Entity>();
                    }
                }

                if (hitEntity != null && hitEntity.entityId != player.entityId)
                {
                    data.GrabbedEntity = hitEntity;
                    data.GrabDistance = hit.distance;

                    data.GrabbedRigidbody = hit.collider.GetComponent<Rigidbody>();
                    if (data.GrabbedRigidbody == null)
                    {
                        data.GrabbedRigidbody = hit.collider.GetComponentInParent<Rigidbody>();
                    }

                    // wake up our physics if we are asleep
                    if (data.GrabbedRigidbody != null)
                    {
                        data.GrabbedRigidbody.isKinematic = false;
                        data.GrabbedRigidbody.WakeUp();
                    }

                    // add a nice shock effect
                    if (hitEntity is EntityAlive aliveTarget)
                    {
                         aliveTarget.Buffs.AddBuff("buffShocked");
                    }

                    // prepare the laser
                    data.LaserObj = new GameObject("PhysgunLaser");
                    data.LaserRenderer = data.LaserObj.AddComponent<LineRenderer>();
                    data.LaserRenderer.startWidth = 0.05f;
                    data.LaserRenderer.endWidth = 0.05f;
                    data.LaserRenderer.startColor = Color.cyan;
                    data.LaserRenderer.endColor = Color.cyan;

                    Shader laserShader = Shader.Find("UI/Default") ?? Shader.Find("Hidden/Internal-Colored");
                    if (laserShader != null)
                    {
                        data.LaserRenderer.material = new Material(laserShader);
                        data.LaserRenderer.material.color = Color.cyan;
                    }

                    data.isHolding = true;
                    SetWeaponScrollEnabled(player, false);

                    // drain charge a bit
                    if (itemValue != null)
                    {
                        itemValue.UseTimes += 5f;
                        player.inventory.onInventoryChanged();
                    }

                    // play a sound
                    player.PlayOneShot("weapon_electric_baton_start");
                    break;
                }
            }
        }

        // disables the weapon from being able to scroll
        private void SetWeaponScrollEnabled(EntityPlayerLocal player, bool enabled)
        {
            if (player != null && player.playerInput != null)
            {

                if (player.playerInput.InventorySlotLeft != null)
                    player.playerInput.InventorySlotLeft.Enabled = enabled;

                if (player.playerInput.InventorySlotRight != null)
                    player.playerInput.InventorySlotRight.Enabled = enabled;

                player.playerInput.Scroll.Enabled = enabled;
            }
        }

        private void ReleaseEntity(EntityPlayerLocal player, ItemActionPhysgunData data, bool isFrozen, bool removeBuff = true)
        {

            // allow scrolling
            SetWeaponScrollEnabled(player, true);

            if (data.GrabbedEntity != null)
            {


                if (data.invData != null && data.invData.itemValue != null)
                {
                    data.invData.itemValue.UseTimes += 5f;
                    if (player != null && player.inventory != null)
                    {
                        player.inventory.onInventoryChanged();
                    }
                }

                if (data.GrabbedEntity is EntityAlive aliveTarget)
                {
                    aliveTarget.Buffs.RemoveBuff("buffShocked");
                    if (removeBuff)
                    {
                        aliveTarget.Buffs.RemoveBuff("buffRagdoll");
                    }
                }

                if (!isFrozen)
                {
                    bool shouldDropAll = data.GrabbedEntity is EntityAlive aTarget && aTarget.IsDead();
                    if (!(data.GrabbedEntity is EntityAlive)) shouldDropAll = true;

                    if (shouldDropAll)
                    {
                        Rigidbody[] rbs = data.GrabbedEntity.GetComponentsInChildren<Rigidbody>();
                        foreach (Rigidbody rb in rbs)
                        {
                            if (rb != null) rb.isKinematic = false;
                        }
                    }
                    else if (data.GrabbedRigidbody != null)
                    {
                        data.GrabbedRigidbody.isKinematic = false;
                    }
                }
                data.GrabbedEntity = null;
                data.GrabbedRigidbody = null;
            }

            if (data.LaserObj != null)
            {
                UnityEngine.Object.Destroy(data.LaserObj);
                data.LaserObj = null;
                data.LaserRenderer = null;
            }

            data.isHolding = false;
        }

        private void UpdateLaser(EntityPlayerLocal player, ItemActionPhysgunData data)
        {
            if (data.LaserRenderer != null && data.GrabbedEntity != null)
            {
                Transform rightHand = player.emodel.GetRightHandTransform();
                Vector3 startPos = rightHand != null ? rightHand.position : player.cameraTransform.position + (Vector3.down * 0.2f);

                float forwardOffset = 0.3f;
                float upOffset = 0.12f;
                float rightOffset = 0.08f;

                startPos += (player.cameraTransform.forward * forwardOffset) +
                            (player.cameraTransform.up * upOffset) +
                            (player.cameraTransform.right * rightOffset);

                Vector3 endPos;

                if (data.GrabbedRigidbody != null)
                {
                    endPos = data.GrabbedRigidbody.position;
                }
                else if (data.GrabbedEntity is EntityAlive aliveTarget)
                {
                    endPos = aliveTarget.getBellyPosition();
                }
                else
                {
                    endPos = data.GrabbedEntity.GetPosition() + new Vector3(0, 0.3f, 0);
                }

                data.LaserRenderer.SetPosition(0, startPos);
                data.LaserRenderer.SetPosition(1, endPos);
            }
        }
    }

    // disable scrolling for this weapon

    [HarmonyPatch(typeof(XUiC_Toolbelt), "Update")]
    public class XUiC_Toolbelt_Update_Patch
    {
        public static bool Prefix(XUiC_Toolbelt __instance)
        {
            var player = __instance.xui?.playerUI?.entityPlayer;
            if (player != null && player.inventory != null)
            {
                var holdingData = player.inventory.holdingItemData;
                if (holdingData != null && holdingData.actionData != null && holdingData.actionData.Count > 0)
                {
                    var physgunData = holdingData.actionData[0] as ItemActionPhysgunData;
                    if (physgunData != null && physgunData.GrabbedEntity != null && physgunData.isHolding)
                    {
                        
                        return false;
                    }
                }
            }
            return true;
        }
    }
    
    // further disabling of scrolling

    [HarmonyPatch(typeof(Inventory), "SetHoldingItemIdx")]
    public class Inventory_SetHoldingItemIdx_Patch
    {
        public static bool Prefix(Inventory __instance)
        {
            if (__instance.entity is EntityPlayerLocal)
            {
                if (__instance.holdingItemData != null &&
                    __instance.holdingItemData.actionData != null &&
                    __instance.holdingItemData.actionData.Count > 0)
                {
                    var physgunData = __instance.holdingItemData.actionData[0] as ItemActionPhysgunData;
                    if (physgunData != null && physgunData.GrabbedEntity != null && physgunData.isHolding)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}