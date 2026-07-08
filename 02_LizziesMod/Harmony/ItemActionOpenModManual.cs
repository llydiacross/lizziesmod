using UnityEngine;

namespace LizziesMod
{
    public class ItemActionOpenModManual : ItemAction
    {

        private string bookId = "";

        public override void ReadFrom(DynamicProperties _props)
        {
            base.ReadFrom(_props);

            if (_props.Values.ContainsKey("BookId"))
            {
                this.bookId = _props.Values["BookId"];
            }
            else
            {
                Logger.Warning($"[ItemActionOpenModManual] No 'BookId' property defined for action on item!");
            }
        }

        public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
        {
           
            if (_bReleased) return;

    
            if (_actionData.invData.holdingEntity is EntityPlayerLocal playerLocal)
            {
                if (string.IsNullOrEmpty(this.bookId))
                {
                    Logger.Error($"[ItemActionOpenModManual] Cannot open manual: BookId is empty for item '{_actionData.invData.itemValue.ItemClass.GetItemName()}'");
                    return;
                }

               
                if (!ModManualManager.AllBooks.ContainsKey(this.bookId))
                {
                    Logger.Error($"[ItemActionOpenModManual] Could not find a loaded book matching ID '{this.bookId}'. Check your ModManual.xml!");
                    playerLocal.PlayOneShot("ui_denied");
                    return;
                }

                ModManualUIController.CurrentBookID = this.bookId;

                LocalPlayerUI ui = LocalPlayerUI.GetUIForPlayer(playerLocal);
                if (ui != null)
                {
                    playerLocal.PlayOneShot("open_inventory");
                    ui.windowManager.Open("windowModManual", true);
                }
            }
        }

        public override ItemActionData CreateModifierData(ItemInventoryData _invData, int _indexInEntityOfAction)
        {
            return new ItemActionData(_invData, _indexInEntityOfAction);
        }
    }
}