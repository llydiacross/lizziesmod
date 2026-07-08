using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LizziesMod
{
    public class ModLibraryUIController : XUiController
    {
        public static string PreviousMenu = "";

        private XUiController bookListGrid;
        private string selectedBookId = "";
        private int currentPageIndex = 0;

        private XUiV_Label lblBookTitle;
        private XUiV_Label lblPageTitle;
        private XUiV_Label lblPageText;
        private XUiV_Texture imgPageDiagram;
        private XUiV_Sprite sprBackground;

        private XUiController btnPrevPage;
        private XUiController btnNextPage;
        private XUiV_Label lblPageNumber;

        public override void Init()
        {
            base.Init();
            bookListGrid = GetChildById("bookListGrid");

            lblBookTitle = GetChildById("lblBookTitle")?.viewComponent as XUiV_Label;
            lblPageTitle = GetChildById("lblPageTitle")?.viewComponent as XUiV_Label;
            lblPageText = GetChildById("lblPageText")?.viewComponent as XUiV_Label;
            imgPageDiagram = GetChildById("imgPageDiagram")?.viewComponent as XUiV_Texture;

            sprBackground = GetChildById("readingBackground")?.viewComponent as XUiV_Sprite;

            btnPrevPage = GetChildById("btnPrevPage");
            if (btnPrevPage != null) btnPrevPage.OnPress += (s, e) => TurnPage(-1);

            btnNextPage = GetChildById("btnNextPage");
            if (btnNextPage != null) btnNextPage.OnPress += (s, e) => TurnPage(1);

            lblPageNumber = GetChildById("lblPageNumber")?.viewComponent as XUiV_Label;

            XUiController btnClose = GetChildById("btnClose")?.GetChildById("clickable") ?? GetChildById("btnClose");
            if (btnClose != null) btnClose.OnPress += HandleClose;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            PopulateBookList();


            var readmes = ModManualManager.AllBooks.Values.Where(b => b.IsReadme).ToList();
            if (readmes.Count > 0)
            {
                SelectBook(readmes[0].ID);
            }
            else
            {
                ClearReadingPane();
            }
        }

        private void PopulateBookList()
        {
            if (bookListGrid == null) return;

            var readmes = ModManualManager.AllBooks.Values.Where(b => b.IsReadme).ToList();
            int i = 0;

            foreach (var child in bookListGrid.Children)
            {
                if (child is ModLibraryBookEntryController entry)
                {
                    if (i < readmes.Count)
                        entry.SetBook(readmes[i], this);
                    else
                        entry.Clear();
                    i++;
                }
            }
        }

        public void SelectBook(string bookId)
        {
            selectedBookId = bookId;
            if (ModManualManager.AllBooks.TryGetValue(bookId, out ModBook book))
            {
                if (lblBookTitle != null) lblBookTitle.Text = book.Title;
                currentPageIndex = book.StartPage;
                UpdateReadingPane();
            }
        }

        private void TurnPage(int direction)
        {
            if (string.IsNullOrEmpty(selectedBookId) || !ModManualManager.AllBooks.TryGetValue(selectedBookId, out ModBook book)) return;

            int newPage = currentPageIndex + direction;
            if (newPage >= 0 && newPage < book.TotalPages)
            {
                currentPageIndex = newPage;
                UpdateReadingPane();
            }
        }

        private void UpdateReadingPane()
        {
            if (string.IsNullOrEmpty(selectedBookId) || !ModManualManager.AllBooks.TryGetValue(selectedBookId, out ModBook book)) return;

            ModPage page = book.GetPage(currentPageIndex);
            if (page == null) return;

            if (lblPageTitle != null) lblPageTitle.Text = page.Title;

            if (lblPageText != null)
            {
                lblPageText.Text = page.Text;


                string[] rgba = page.TextColor.Split(',');
                if (rgba.Length == 4 &&
                    float.TryParse(rgba[0], out float r) &&
                    float.TryParse(rgba[1], out float g) &&
                    float.TryParse(rgba[2], out float b) &&
                    float.TryParse(rgba[3], out float a))
                {
                    lblPageText.Color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                }
            }


            if (sprBackground != null)
            {
                sprBackground.SpriteName = !string.IsNullOrEmpty(page.Background) ? page.Background : book.DefaultBackground;
            }

            if (btnPrevPage != null) btnPrevPage.viewComponent.IsVisible = (currentPageIndex > 0);
            if (btnNextPage != null) btnNextPage.viewComponent.IsVisible = (currentPageIndex < book.TotalPages - 1);
            if (lblPageNumber != null) lblPageNumber.Text = $"Page {currentPageIndex + 1} of {book.TotalPages}";
        }

        private void ClearReadingPane()
        {
            selectedBookId = "";
            if (lblBookTitle != null) lblBookTitle.Text = "NO READMES FOUND";
            if (lblPageTitle != null) lblPageTitle.Text = "";
            if (lblPageText != null) lblPageText.Text = "Install mods containing a ModManual.xml to view their documentation here.";
            if (btnPrevPage != null) btnPrevPage.viewComponent.IsVisible = false;
            if (btnNextPage != null) btnNextPage.viewComponent.IsVisible = false;
            if (lblPageNumber != null) lblPageNumber.Text = "";
        }

        private void HandleClose(XUiController _sender, int _mouseButton)
        {
            xui.playerUI.windowManager.Close("windowModLibrary");
            if (!string.IsNullOrEmpty(PreviousMenu))
                xui.playerUI.windowManager.Open(PreviousMenu, true);
        }
    }


    public class ModLibraryBookEntryController : XUiController
    {
        private ModBook book;
        private ModLibraryUIController mainController;
        private XUiV_Label lblBookName;
        private XUiV_Label lblAuthor;

        public override void Init()
        {
            base.Init();
            lblBookName = GetChildById("lblBookName")?.viewComponent as XUiV_Label;
            lblAuthor = GetChildById("lblAuthor")?.viewComponent as XUiV_Label;

            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += HandlePress;
        }

        public void SetBook(ModBook b, ModLibraryUIController main)
        {
            book = b;
            mainController = main;

            if (lblBookName != null) lblBookName.Text = b.Title;
            if (lblAuthor != null) lblAuthor.Text = $"By: {b.Author}";

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            book = null;
            viewComponent.IsVisible = false;
        }

        private void HandlePress(XUiController _sender, int _mouseButton)
        {
            if (book != null && mainController != null)
            {
                mainController.SelectBook(book.ID);
            }
        }
    }
}