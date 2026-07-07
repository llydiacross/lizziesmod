using UnityEngine;
using System.Collections.Generic;

namespace LizziesMod
{
    public class JukeboxUIController : XUiController
    {
        public static Vector3 CurrentJukeboxPosition;

        private XUiController trackListGrid;
        private List<string> trackKeys = new List<string>();

        // Pagination logic
        private int currentPage = 0;
        private int itemsPerPage = 5; // Must match the number of rows in our XML grid

        public override void Init()
        {
            base.Init();
            trackListGrid = GetChildById("trackListGrid");

            XUiController closeBtn = GetChildById("btnClose")?.GetChildById("clickable");
            if (closeBtn != null) closeBtn.OnPress += HandleClose;

            XUiController btnPageUp = GetChildById("btnPageUp")?.GetChildById("clickable");
            if (btnPageUp != null) btnPageUp.OnPress += (s, e) => ChangePage(-1);

            XUiController btnPageDown = GetChildById("btnPageDown")?.GetChildById("clickable");
            if (btnPageDown != null) btnPageDown.OnPress += (s, e) => ChangePage(1);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            currentPage = 0;
            RefreshTrackList();
        }

        private void ChangePage(int direction)
        {
            int maxPages = Mathf.CeilToInt((float)trackKeys.Count / itemsPerPage);
            currentPage += direction;

            if (currentPage < 0) currentPage = maxPages - 1;
            if (currentPage >= maxPages) currentPage = 0;

            RefreshTrackList();

            xui.mPlayerUI.localPlayer.entityPlayerLocal.PlayOneShot("weapon_click");
        }

        private void RefreshTrackList()
        {
            if (CustomAudioManager.Instance == null || trackListGrid == null) return;

            trackKeys = new List<string>(CustomAudioManager.Instance.GetAvailableAudio().Keys);

            for (int i = 0; i < trackListGrid.Children.Count; i++)
            {
                if (trackListGrid.Children[i] is JukeboxTrackUIEntryController entry)
                {
                    int trackIndex = (currentPage * itemsPerPage) + i;
                    if (trackIndex < trackKeys.Count)
                    {
                        string key = trackKeys[trackIndex];
                        AudioTrack track = CustomAudioManager.Instance.GetAvailableAudio()[key];
                        entry.SetTrack(key, track, this);
                    }
                    else
                    {
                        entry.Clear();
                    }
                }
            }
        }

        public void BuyAndPlayTrack(string targetTrack)
        {
            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;
            if (player == null) return;

            ItemClass tokenClass = ItemClass.GetItemClass("casinoCoin", false);
            if (tokenClass == null) return;

            ItemValue tokenValue = new ItemValue(tokenClass.Id);
            int coinCount = player.bag.GetItemCount(tokenValue, -1, -1, true);
            if (coinCount < 1)
            {
                GameManager.ShowTooltip(player, "[FF0000]Deposit required: 1 Casino Token (Duke)[-]");
                player.PlayOneShot("ui_denied");
                return;
            }


            player.bag.DecItem(tokenValue, 1, false, null);
            player.inventory.onInventoryChanged();
            player.PlayOneShot("vending_machine_place_item");


            if (SingletonMonoBehaviour<ConnectionManager>.Instance != null)
            {
                SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageJukeboxPlay>().Setup(CurrentJukeboxPosition, targetTrack)
                );
            }


            xui.playerUI.windowManager.Close("windowJukebox");
        }

        private void HandleClose(XUiController _sender, int _mouseButton)
        {
            xui.playerUI.windowManager.Close("windowJukebox");
        }
    }

    public class JukeboxUITrackEntryController : XUiController
    {
        private string trackKey;
        private JukeboxUIController mainController;
        private XUiV_Label lblTrackInfo;

        public override void Init()
        {
            base.Init();
            lblTrackInfo = GetChildById("lblTrackInfo")?.viewComponent as XUiV_Label;

            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null)
            {
                clickable.OnPress += HandlePress;
            }
        }

        public void SetTrack(string key, AudioTrack track, JukeboxUIController main)
        {
            trackKey = key;
            mainController = main;

            if (lblTrackInfo != null)
            {

                lblTrackInfo.Text = $"{track.Artist} - {track.Title}";
            }


            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            trackKey = "";
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController _sender, int _mouseButton)
        {
            if (!string.IsNullOrEmpty(trackKey) && mainController != null)
            {
 
                mainController.BuyAndPlayTrack(trackKey);
            }
        }
    }
}