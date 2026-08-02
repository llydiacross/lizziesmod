using UnityEngine;
using System.Collections.Generic;

namespace LizziesMod
{
    public class JukeboxUIController : XUiController
    {
        public static Vector3 CurrentJukeboxPosition;
        private static JukeboxUIController activeController;

        private XUiController trackListGrid;
        private XUiC_TextInput priceInput;
        private XUiController setPriceButton;
        private XUiV_Label libraryStatusLabel;
        private XUiV_Label priceInfoLabel;
        private List<string> trackKeys = new List<string>();
        private int playbackPrice;
        private bool isOwner;

        // Pagination logic
        private int currentPage = 0;
        private int itemsPerPage = 5; // Must match the number of rows in our XML grid

        public override void Init()
        {
            base.Init();
            trackListGrid = GetChildById("trackListGrid");
            priceInput = GetChildById("txtPlaybackPrice") as XUiC_TextInput;
            setPriceButton = GetChildById("btnSetPrice");
            libraryStatusLabel = GetChildById("lblLibraryStatus")?.viewComponent as XUiV_Label;
            priceInfoLabel = GetChildById("lblPriceInfo")?.viewComponent as XUiV_Label;

            XUiController closeBtn = GetChildById("btnClose")?.GetChildById("clickable");
            if (closeBtn != null) closeBtn.OnPress += HandleClose;

            XUiController btnPageUp = GetChildById("btnPageUp")?.GetChildById("clickable");
            if (btnPageUp != null) btnPageUp.OnPress += (s, e) => ChangePage(-1);

            XUiController btnPageDown = GetChildById("btnPageDown")?.GetChildById("clickable");
            if (btnPageDown != null) btnPageDown.OnPress += (s, e) => ChangePage(1);

            XUiController setPriceClickable = setPriceButton?.GetChildById("clickable") ?? setPriceButton;
            if (setPriceClickable != null) setPriceClickable.OnPress += HandleSetPrice;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            activeController = this;
            currentPage = 0;
            trackKeys.Clear();
            playbackPrice = 0;
            isOwner = false;
            RefreshTrackList();
            JukeboxManager.RequestLibrary(xui.mPlayerUI.localPlayer.entityPlayerLocal, CurrentJukeboxPosition);
        }

        public override void OnClose()
        {
            base.OnClose();
            if (activeController == this) activeController = null;
        }

        public static void ReceiveLibrarySnapshot(Vector3 position, List<string> unlockedTrackIds, int price, bool playerIsOwner)
        {
            if (activeController == null || activeController.CurrentPositionDoesNotMatch(position)) return;

            activeController.ApplyLibrarySnapshot(unlockedTrackIds, price, playerIsOwner);
        }

        public static void ShowFeedback(string message, bool denied)
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || string.IsNullOrEmpty(message)) return;

            GameManager.ShowTooltip(player, message);
            player.PlayOneShot(denied ? "ui_denied" : "vending_machine_place_item");
        }

        private void ChangePage(int direction)
        {
            if (trackKeys.Count == 0) return;

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

            for (int i = 0; i < trackListGrid.Children.Count; i++)
            {
                if (trackListGrid.Children[i] is JukeboxUITrackEntryController entry)
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

            UpdateLibraryLabels();
        }

        private int CompareTracks(string leftKey, string rightKey)
        {
            AudioTrack left = CustomAudioManager.Instance.GetAvailableAudio()[leftKey];
            AudioTrack right = CustomAudioManager.Instance.GetAvailableAudio()[rightKey];

            int comparison = string.Compare(left.Artist, right.Artist, System.StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Album, right.Album, System.StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
            comparison = left.TrackNumber.CompareTo(right.TrackNumber);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Title, right.Title, System.StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
            return string.Compare(leftKey, rightKey, System.StringComparison.OrdinalIgnoreCase);
        }

        public void BuyAndPlayTrack(string targetTrack)
        {
            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;
            if (player == null) return;

            JukeboxManager.RequestPlayTrack(player, CurrentJukeboxPosition, targetTrack);
        }

        private bool CurrentPositionDoesNotMatch(Vector3 position)
        {
            return (CurrentJukeboxPosition - position).sqrMagnitude > 0.01f;
        }

        private void ApplyLibrarySnapshot(List<string> unlockedTrackIds, int price, bool playerIsOwner)
        {
            playbackPrice = price;
            isOwner = playerIsOwner;
            trackKeys = unlockedTrackIds == null ? new List<string>() : new List<string>(unlockedTrackIds);
            if (CustomAudioManager.Instance != null)
            {
                trackKeys.RemoveAll(trackId => !CustomAudioManager.Instance.HasTrack(trackId));
            }
            trackKeys.Sort(CompareTracks);
            currentPage = 0;
            RefreshTrackList();
        }

        private void UpdateLibraryLabels()
        {
            if (libraryStatusLabel != null)
            {
                libraryStatusLabel.Text = trackKeys.Count == 0
                    ? "INSERT A MUSIC DISC TO UNLOCK TRACKS"
                    : trackKeys.Count + (trackKeys.Count == 1 ? " TRACK UNLOCKED" : " TRACKS UNLOCKED");
            }

            if (priceInfoLabel != null)
            {
                priceInfoLabel.Text = isOwner
                    ? "OWNER PLAYBACK: FREE | VISITORS: " + playbackPrice + " DUKE" + (playbackPrice == 1 ? "" : "S")
                    : playbackPrice == 0
                        ? "PLAYBACK: FREE"
                        : "PLAYBACK: " + playbackPrice + " DUKE" + (playbackPrice == 1 ? "" : "S");
            }

            if (priceInput != null)
            {
                priceInput.Text = playbackPrice.ToString();
                priceInput.viewComponent.IsVisible = isOwner;
            }

            if (setPriceButton != null) setPriceButton.viewComponent.IsVisible = isOwner;
        }

        private void HandleSetPrice(XUiController sender, int mouseButton)
        {
            if (!isOwner || priceInput == null) return;

            int price;
            if (!int.TryParse(priceInput.Text, out price) || price < 0 || price > JukeboxManager.MaxPrice)
            {
                ShowFeedback("[FF0000]Enter a price between 0 and " + JukeboxManager.MaxPrice + ".[-]", true);
                return;
            }

            EntityPlayerLocal player = xui.mPlayerUI.localPlayer.entityPlayerLocal;
            if (player != null) JukeboxManager.RequestSetPrice(player, CurrentJukeboxPosition, price);
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