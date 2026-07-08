using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class ModManualUIController : XUiController
    {
  
        public static string CurrentBookID = "";

        private ModBook currentBook;
        private int currentPageIndex = 0;

        private XUiController pageListGrid;
        private XUiV_Label lblBookTitle;
        private XUiV_Label lblPageTitle;
        private XUiV_Label lblPageText;
        private XUiV_Texture imgPageDiagram;

        public override void Init()
        {
            base.Init();
            pageListGrid = GetChildById("pageListGrid");

            lblBookTitle = GetChildById("lblBookTitle")?.viewComponent as XUiV_Label;
            lblPageTitle = GetChildById("lblPageTitle")?.viewComponent as XUiV_Label;
            lblPageText = GetChildById("lblPageText")?.viewComponent as XUiV_Label;
            imgPageDiagram = GetChildById("imgPageDiagram")?.viewComponent as XUiV_Texture;

            XUiController btnClose = GetChildById("btnClose")?.GetChildById("clickable") ?? GetChildById("btnClose");
            if (btnClose != null) btnClose.OnPress += (s, e) => xui.playerUI.windowManager.Close("windowModManual");
        }

        public override void OnOpen()
        {
            base.OnOpen();

            if (!ModManualManager.AllBooks.TryGetValue(CurrentBookID, out currentBook))
            {
                Logger.Error($"[ModManual] Could not find book with ID: {CurrentBookID}");
                xui.playerUI.windowManager.Close("windowModManual");
                return;
            }

            if (lblBookTitle != null) lblBookTitle.Text = currentBook.Title;

            currentPageIndex = currentBook.StartPage;
            PopulatePageList();
            SelectPage(currentPageIndex);
        }

        public void PopulatePageList()
        {
            if (pageListGrid == null || currentBook == null) return;

            int i = 0;
            foreach (var child in pageListGrid.Children)
            {
                if (child is ModManualPageEntryController entry)
                {
                    if (i < currentBook.Pages.Count)
                    {
                        entry.SetPage(i, currentBook.Pages[i].Title, this);
                    }
                    else
                    {
                        entry.Clear();
                    }
                    i++;
                }
            }
        }

        public void SelectPage(int index)
        {
            if (currentBook == null) return;
            ModPage page = currentBook.GetPage(index);
            if (page == null) return;

            currentPageIndex = index;

            if (lblPageTitle != null) lblPageTitle.Text = page.Title;
            if (lblPageText != null) lblPageText.Text = page.Text;

     
            if (imgPageDiagram != null)
            {
                if (!string.IsNullOrEmpty(page.ImageName))
                {

                    imgPageDiagram.IsVisible = true;
                }
                else
                {
                    imgPageDiagram.IsVisible = false;
                }
            }
        }
    }

    public class ModManualPageEntryController : XUiController
    {
        private int pageIndex;
        private ModManualUIController mainController;
        private XUiV_Label lblTitle;

        public override void Init()
        {
            base.Init();
            lblTitle = GetChildById("lblTitle")?.viewComponent as XUiV_Label;
            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += (s, e) => mainController?.SelectPage(pageIndex);
        }

        public void SetPage(int index, string title, ModManualUIController main)
        {
            pageIndex = index;
            mainController = main;
            if (lblTitle != null) lblTitle.Text = $"Page {index + 1}: {title}";
            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            viewComponent.IsVisible = false;
        }
    }
}