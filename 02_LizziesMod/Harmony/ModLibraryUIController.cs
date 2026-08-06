using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LizziesMod
{
    public class ModLibraryUIController : XUiController
    {
        private const int ReadmeViewportWidth = 1030;
        private const int ReadmeViewportHeight = 570;
        private const int ReadmeContentPadding = 20;
        // The XUi scroll view offsets dynamically positioned child views left of its visible origin.
        private const int ReadmeRenderOriginOffsetX = 135;
        private const int ReadmeContentWidth = ReadmeViewportWidth - (ReadmeContentPadding * 2) - ReadmeRenderOriginOffsetX;
        private const int ReadmeScrollEndPadding = 100;
        private const int ReadmeTextAreaPoolSize = 32;
        private const int ReadmeImagePoolSize = 32;
        private const string MainMenuWindow = "mainMenu";

        public static string PreviousMenu = "";
        public static string RequestedBookId = "";

        private XUiController bookListGrid;
        private string selectedBookId = "";
        private int currentPageIndex = 0;
        private XUiV_Label lblBookTitle;
        private XUiV_Label lblPageTitle;
        private XUiV_Sprite sprBackground;
        private XUiController btnPrevPage;
        private XUiController btnNextPage;
        private XUiV_Label lblPageNumber;
        private XUiController readmeCanvas;
        private XUiV_ScrollView readmeScrollView;
        private XUiController readmeScrollbar;
        private readonly List<XUiV_Label> readmeTextAreaViews = new List<XUiV_Label>();
        private readonly List<XUiV_Texture> readmeImageViews = new List<XUiV_Texture>();
        private readonly Dictionary<string, Texture2D> loadedImages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private bool ownsGamePause;

        public override void Init()
        {
            base.Init();

            bookListGrid = GetChildById("bookListGrid");

            lblBookTitle = GetChildById("lblBookTitle")?.viewComponent as XUiV_Label;
            lblPageTitle = GetChildById("lblPageTitle")?.viewComponent as XUiV_Label;
            sprBackground = GetChildById("readingBackground")?.viewComponent as XUiV_Sprite;
            readmeCanvas = FindDescendantById("readmeCanvas");
            readmeScrollView = FindDescendantById("readmeScrollView")?.viewComponent as XUiV_ScrollView;
            readmeScrollbar = FindDescendantById("readmeScrollbar");

            for (int index = 1; index <= ReadmeTextAreaPoolSize; index++)
            {
                XUiV_Label textArea = FindDescendantById("readmeText" + index.ToString("00"))?.viewComponent as XUiV_Label;
                if (textArea != null) readmeTextAreaViews.Add(textArea);
            }

            for (int index = 1; index <= ReadmeImagePoolSize; index++)
            {
                XUiV_Texture image = FindDescendantById("readmeImage" + index.ToString("00"))?.viewComponent as XUiV_Texture;
                if (image != null) readmeImageViews.Add(image);
            }

            Logger.Info($"[ModLibrary] Initialized README canvas with {readmeTextAreaViews.Count} text area(s) and {readmeImageViews.Count} image(s).");

            btnPrevPage = GetChildById("btnPrevPage");
            if (btnPrevPage != null)
            {
                XUiController clickablePrev = btnPrevPage.GetChildById("clickable") ?? btnPrevPage;
                clickablePrev.OnPress += (s, e) => TurnPage(-1);
            }

            btnNextPage = GetChildById("btnNextPage");
            if (btnNextPage != null)
            {
                XUiController clickableNext = btnNextPage.GetChildById("clickable") ?? btnNextPage;
                clickableNext.OnPress += (s, e) => TurnPage(1);
            }

            lblPageNumber = GetChildById("lblPageNumber")?.viewComponent as XUiV_Label;

            XUiController btnClose = GetChildById("btnClose")?.GetChildById("clickable") ?? GetChildById("btnClose");
            if (btnClose != null) btnClose.OnPress += HandleClose;
        }

        public void SelectBook(string bookId)
        {
            selectedBookId = bookId;
            PopulateBookList();
            if (ModManualManager.AllBooks.TryGetValue(bookId, out ModBook book))
            {
                if (lblBookTitle != null) lblBookTitle.Text = book.Title;

                currentPageIndex = book.StartPage;
                UpdateReadingPane();
            }
        }

        private void UpdateReadingPane()
        {
            if (string.IsNullOrEmpty(selectedBookId) || !ModManualManager.AllBooks.TryGetValue(selectedBookId, out ModBook book)) return;

            ModPage page = book.GetPage(currentPageIndex);
            if (page == null) return;

            if (lblPageTitle != null) lblPageTitle.Text = page.Title;

            if (sprBackground != null)
            {
                sprBackground.SpriteName = !string.IsNullOrEmpty(page.Background) ? page.Background : book.DefaultBackground;
            }

            RenderReadmePage(book, page);

            if (btnPrevPage != null) btnPrevPage.viewComponent.IsVisible = (currentPageIndex > 0);
            if (btnNextPage != null) btnNextPage.viewComponent.IsVisible = (currentPageIndex < book.TotalPages - 1);
            if (lblPageNumber != null) lblPageNumber.Text = $"Page {currentPageIndex + 1} of {book.TotalPages}";
        }

        public override void OnOpen()
        {
            base.OnOpen();
            ownsGamePause = InGameUiPause.Acquire();

            var readmes = ModManualManager.AllBooks.Values.Where(b => b.IsReadme).ToList();
            string requestedBookId = RequestedBookId;
            RequestedBookId = "";
            if (!string.IsNullOrEmpty(requestedBookId) && ModManualManager.AllBooks.TryGetValue(requestedBookId, out ModBook requestedBook) && requestedBook.IsReadme)
            {
                SelectBook(requestedBook.ID);
            }
            else if (readmes.Count > 0)
            {
                SelectBook(readmes[0].ID);
            }
            else
            {
                PopulateBookList();
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
                        entry.SetBook(readmes[i], this, readmes[i].ID.Equals(selectedBookId, StringComparison.OrdinalIgnoreCase));
                    else
                        entry.Clear();
                    i++;
                }
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

        private void ClearReadingPane()
        {
            selectedBookId = "";
            if (lblBookTitle != null) lblBookTitle.Text = "NO READMES FOUND";
            if (lblPageTitle != null) lblPageTitle.Text = "";
            ClearReadmeCanvas();
                RenderTextArea(0, "Install mods containing Config/ModManual.xml to view their documentation here.", new Vector2i(0, 0), new Vector2i(ReadmeViewportWidth, 100), Color.white);
            SetCanvasSize(ReadmeViewportWidth, ReadmeViewportHeight);
            if (btnPrevPage != null) btnPrevPage.viewComponent.IsVisible = false;
            if (btnNextPage != null) btnNextPage.viewComponent.IsVisible = false;
            if (lblPageNumber != null) lblPageNumber.Text = "";
        }
        public override void OnClose()
        {
            base.OnClose();
            InGameUiPause.Release(ownsGamePause);
            ownsGamePause = false;
            ReleaseLoadedImages();

            string previousMenu = PreviousMenu;
            PreviousMenu = "";
            if (string.IsNullOrEmpty(previousMenu) && !Main.IsPlayerInGame())
            {
                previousMenu = MainMenuWindow;
            }

            if (!string.IsNullOrEmpty(previousMenu))
                xui.playerUI.windowManager.Open(previousMenu, true);
        }

        private void HandleClose(XUiController _sender, int _mouseButton)
        {
            xui.playerUI.windowManager.Close("windowModLibrary");
        }

        private XUiController FindDescendantById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (XUiController child in Children)
            {
                if (string.Equals(child?.viewComponent?.ID, id, StringComparison.Ordinal)) return child;

                XUiController descendant = FindDescendantById(child, id);
                if (descendant != null) return descendant;
            }

            return null;
        }

        private static XUiController FindDescendantById(XUiController parent, string id)
        {
            if (parent?.Children == null) return null;

            foreach (XUiController child in parent.Children)
            {
                if (string.Equals(child?.viewComponent?.ID, id, StringComparison.Ordinal)) return child;

                XUiController descendant = FindDescendantById(child, id);
                if (descendant != null) return descendant;
            }

            return null;
        }

        private void RenderReadmePage(ModBook book, ModPage page)
        {
            ClearReadmeCanvas();

            int contentWidth = ReadmeViewportWidth;
            int contentHeight = page.CanvasSize.HasValue
                ? Math.Max(0, page.CanvasSize.Value.y)
                : 0;

            if (page.UsesReadmeLayout)
            {
                int textAreasToRender = Math.Min(page.TextAreas.Count, readmeTextAreaViews.Count);
                int imagesToRender = Math.Min(page.Images.Count, readmeImageViews.Count);
                if (page.TextAreas.Count > readmeTextAreaViews.Count || page.Images.Count > readmeImageViews.Count)
                {
                    Logger.Warning($"[ModLibrary] README page '{page.Title}' in '{book.Title}' exceeds the {readmeTextAreaViews.Count} text-area and {readmeImageViews.Count} image limit.");
                }

                for (int index = 0; index < textAreasToRender; index++)
                {
                    ReadmeTextArea textArea = page.TextAreas[index];
                    RenderTextArea(index, textArea.Text, textArea.Position, textArea.Size, ParseColor(textArea.TextColor, Color.white));
                    contentHeight = Math.Max(contentHeight, GetElementBottom(textArea.Position, textArea.Size));
                }

                for (int index = 0; index < imagesToRender; index++)
                {
                    ReadmeImage image = page.Images[index];
                    if (RenderImage(book, index, image.Source, image.Position, image.Size))
                    {
                        contentHeight = Math.Max(contentHeight, GetElementBottom(image.Position, image.Size));
                    }
                }
            }
            else
            {
                RenderLegacyPage(book, page, ref contentHeight);
            }

            int canvasHeight = Math.Max(ReadmeViewportHeight, contentHeight + ReadmeContentPadding);
            if (canvasHeight > ReadmeViewportHeight)
            {
                canvasHeight += ReadmeScrollEndPadding;
            }

            SetCanvasSize(contentWidth, canvasHeight);
        }

        private void RenderLegacyPage(ModBook book, ModPage page, ref int contentHeight)
        {
            bool hasImage = !string.IsNullOrEmpty(page.ImageName);
            Vector2i textPosition = page.TextPos ?? new Vector2i(ReadmeContentPadding, 0);
            Vector2i textSize = page.TextSize ?? new Vector2i(ReadmeContentWidth, hasImage ? 170 : 440);
            if (!string.IsNullOrEmpty(page.Text))
            {
                RenderTextArea(0, page.Text, textPosition, textSize, ParseColor(page.TextColor, Color.white));
                contentHeight = Math.Max(contentHeight, GetElementBottom(textPosition, textSize));
            }

            if (hasImage)
            {
                Vector2i imagePosition = page.ImagePos ?? new Vector2i(ReadmeContentPadding, -(textSize.y + ReadmeContentPadding));
                Vector2i imageSize = page.ImageSize ?? new Vector2i(ReadmeContentWidth, 280);
                if (RenderImage(book, 0, page.ImageName, imagePosition, imageSize))
                {
                    contentHeight = Math.Max(contentHeight, GetElementBottom(imagePosition, imageSize));
                }
            }
        }

        private void ClearReadmeCanvas()
        {
            foreach (XUiV_Label textArea in readmeTextAreaViews)
            {
                textArea.Text = "";
                textArea.IsVisible = false;
            }

            foreach (XUiV_Texture image in readmeImageViews)
            {
                image.Texture = null;
                image.IsVisible = false;
            }
        }

        private void RenderTextArea(int index, string text, Vector2i position, Vector2i size, Color color)
        {
            if (index < 0 || index >= readmeTextAreaViews.Count) return;

            GetContainedReadmeBounds(position, size, out Vector2i containedPosition, out Vector2i containedSize);
            XUiV_Label textArea = readmeTextAreaViews[index];
            textArea.Text = text ?? "";
            textArea.Position = GetRenderedReadmePosition(containedPosition);
            textArea.Size = containedSize;
            textArea.Color = color;
            textArea.Overflow = UILabel.Overflow.ClampContent;
            textArea.IsVisible = true;
        }

        private bool RenderImage(ModBook book, int index, string source, Vector2i position, Vector2i size)
        {
            if (index < 0 || index >= readmeImageViews.Count || !TryLoadImage(book, source, out Texture2D texture)) return false;

            GetContainedReadmeBounds(position, size, out Vector2i containedPosition, out Vector2i containedSize);
            XUiV_Texture image = readmeImageViews[index];
            image.Texture = texture;
            image.Position = GetRenderedReadmePosition(containedPosition);
            image.Size = containedSize;
            image.IsVisible = true;
            return true;
        }

        private static void GetContainedReadmeBounds(
            Vector2i requestedPosition,
            Vector2i requestedSize,
            out Vector2i containedPosition,
            out Vector2i containedSize)
        {
            int width = Math.Max(1, Math.Min(requestedSize.x, ReadmeContentWidth));
            int height = Math.Max(1, requestedSize.y);
            int maximumX = ReadmeViewportWidth - ReadmeContentPadding - width;
            int x = Math.Max(ReadmeContentPadding, Math.Min(requestedPosition.x, maximumX));

            containedPosition = new Vector2i(x, requestedPosition.y);
            containedSize = new Vector2i(width, height);
        }

        private static Vector2i GetRenderedReadmePosition(Vector2i containedPosition)
        {
            return new Vector2i(containedPosition.x + ReadmeRenderOriginOffsetX, containedPosition.y);
        }

        private bool TryLoadImage(ModBook book, string source, out Texture2D texture)
        {
            texture = null;
            Mod sourceMod = global::ModManager.GetLoadedMods().FirstOrDefault(mod => mod.Name == book.ModSource);
            if (sourceMod == null || string.IsNullOrWhiteSpace(source)) return false;

            string extension = Path.GetExtension(source);
            if (string.IsNullOrEmpty(extension)) source += ".png";
            else if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[ModLibrary] README image '{source}' uses an unsupported file type.");
                return false;
            }

            string resourcesPath = Path.GetFullPath(Path.Combine(sourceMod.Path, "ManualResources"));
            string imagePath = Path.GetFullPath(Path.Combine(resourcesPath, source));
            if (!imagePath.StartsWith(resourcesPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[ModLibrary] README image '{source}' is outside ManualResources.");
                return false;
            }

            if (loadedImages.TryGetValue(imagePath, out texture)) return true;
            if (!File.Exists(imagePath))
            {
                Logger.Warning($"[ModLibrary] README image '{source}' was not found for '{book.Title}'.");
                return false;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(imagePath);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, fileData))
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                    Logger.Warning($"[ModLibrary] README image '{source}' could not be decoded.");
                    return false;
                }

                texture.name = "Readme_" + Path.GetFileNameWithoutExtension(imagePath);
                loadedImages.Add(imagePath, texture);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Warning($"[ModLibrary] Failed to load README image '{source}': {exception.Message}");
                return false;
            }
        }

        private void SetCanvasSize(int width, int height)
        {
            if (readmeCanvas?.viewComponent != null)
            {
                readmeCanvas.viewComponent.Size = new Vector2i(width, Math.Max(ReadmeViewportHeight, height));
            }

            if (readmeScrollbar?.viewComponent != null)
            {
                readmeScrollbar.viewComponent.IsVisible = height > ReadmeViewportHeight;
            }

            readmeScrollView?.UpdatePosition();
            readmeScrollView?.ResetPosition();
        }

        private void ReleaseLoadedImages()
        {
            ClearReadmeCanvas();
            foreach (Texture2D texture in loadedImages.Values)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }

            loadedImages.Clear();
        }

        private static int GetElementBottom(Vector2i position, Vector2i size)
        {
            return Math.Max(0, -position.y) + size.y;
        }

        private static Color ParseColor(string colorText, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(colorText)) return fallback;

            string[] values = colorText.Split(',');
            if (values.Length != 4 ||
                !byte.TryParse(values[0].Trim(), out byte red) ||
                !byte.TryParse(values[1].Trim(), out byte green) ||
                !byte.TryParse(values[2].Trim(), out byte blue) ||
                !byte.TryParse(values[3].Trim(), out byte alpha))
            {
                return fallback;
            }

            return new Color32(red, green, blue, alpha);
        }
    }


    public class ModLibraryBookEntryController : XUiController
    {
        private ModBook book;
        private ModLibraryUIController mainController;
        private XUiV_Label lblBookName;
        private XUiV_Label lblAuthor;
        private XUiV_Sprite sprSelected;

        public override void Init()
        {
            base.Init();
            lblBookName = GetChildById("lblBookName")?.viewComponent as XUiV_Label;
            lblAuthor = GetChildById("lblAuthor")?.viewComponent as XUiV_Label;
            sprSelected = GetChildById("sprSelected")?.viewComponent as XUiV_Sprite;

            XUiController clickable = GetChildById("clickable") ?? this;
            if (clickable != null) clickable.OnPress += HandlePress;
        }

        public void SetBook(ModBook b, ModLibraryUIController main, bool isSelected)
        {
            book = b;
            mainController = main;

            if (lblBookName != null) lblBookName.Text = b.Title;
            if (lblAuthor != null) lblAuthor.Text = $"By: {b.Author}";
            if (sprSelected != null) sprSelected.IsVisible = isSelected;

            viewComponent.IsVisible = true;
        }

        public void Clear()
        {
            book = null;
            if (sprSelected != null) sprSelected.IsVisible = false;
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