using HarmonyLib;

namespace LizziesMod
{
    public interface IScrollWheelActionData
    {
        bool IsScrollWheelCaptureActive { get; }
    }

    public abstract class ScrollWheelActionData : ItemActionData, IScrollWheelActionData
    {
        protected bool UsesScrollWheel { get; private set; }

        protected ScrollWheelActionData(ItemInventoryData _invData, int _indexInEntityOfAction, bool usesScrollWheel)
            : base(_invData, _indexInEntityOfAction)
        {
            UsesScrollWheel = usesScrollWheel;
        }

        public abstract bool IsScrollWheelCaptureActive { get; }
    }

    public abstract class ItemActionWithScrollWheel : ItemAction
    {
        protected bool UsesScrollWheel { get; private set; }

        public override void ReadFrom(DynamicProperties _props)
        {
            base.ReadFrom(_props);
            UsesScrollWheel = false;

            if (_props == null || _props.Values == null || !_props.Values.ContainsKey("UsesScrollWheel"))
            {
                return;
            }

            bool usesScrollWheel;
            if (!bool.TryParse(_props.Values["UsesScrollWheel"], out usesScrollWheel))
            {
                Logger.Warning("[ItemActionWithScrollWheel] UsesScrollWheel must be true or false. Scroll-wheel capture is disabled for this action.");
                return;
            }

            UsesScrollWheel = usesScrollWheel;
        }
    }

    internal static class ScrollWheelActionController
    {
        internal static bool UpdateInputLock(EntityPlayerLocal player)
        {
            bool isCaptureActive = IsScrollWheelCaptureActive(player);

            if (player == null || player.playerInput == null)
            {
                return isCaptureActive;
            }

            if (player.playerInput.InventorySlotLeft != null)
            {
                player.playerInput.InventorySlotLeft.Enabled = !isCaptureActive;
            }

            if (player.playerInput.InventorySlotRight != null)
            {
                player.playerInput.InventorySlotRight.Enabled = !isCaptureActive;
            }

            if (player.playerInput.Scroll != null)
            {
                player.playerInput.Scroll.Enabled = !isCaptureActive;
            }

            return isCaptureActive;
        }

        private static bool IsScrollWheelCaptureActive(EntityPlayerLocal player)
        {
            if (player == null || player.inventory == null || player.inventory.holdingItemData == null || player.inventory.holdingItemData.actionData == null)
            {
                return false;
            }

            foreach (ItemActionData actionData in player.inventory.holdingItemData.actionData)
            {
                IScrollWheelActionData scrollWheelActionData = actionData as IScrollWheelActionData;
                if (scrollWheelActionData != null && scrollWheelActionData.IsScrollWheelCaptureActive)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(XUiC_Toolbelt), "Update")]
    public class XUiC_Toolbelt_ScrollWheelAction_Patch
    {
        public static bool Prefix(XUiC_Toolbelt __instance)
        {
            EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
            return !ScrollWheelActionController.UpdateInputLock(player);
        }
    }

    [HarmonyPatch(typeof(Inventory), "SetHoldingItemIdx")]
    public class Inventory_SetHoldingItemIdx_ScrollWheelAction_Patch
    {
        public static bool Prefix(Inventory __instance)
        {
            EntityPlayerLocal player = __instance.entity as EntityPlayerLocal;
            return !ScrollWheelActionController.UpdateInputLock(player);
        }
    }
}