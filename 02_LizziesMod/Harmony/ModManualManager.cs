using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;

namespace LizziesMod
{
    // Represents a single page inside a book
    public class ModPage
    {
        public string Title;
        public string Text;
        public string ImageName;

        // Optional styling overrides
        public string Background;
        public string Font;
        public string TextColor;
    }

    public class ModBook
    {
        public string ID;
        public string ModSource; 
        public string Title;
        public string Author;
        public string Icon;
        public int StartPage;
        public bool IsReadme;
        public string DefaultBackground;
        public string DefaultFont;
        public string DefaultTextColor;

        public List<ModPage> Pages = new List<ModPage>();


        public ModPage GetPage(int pageIndex)
        {
            if (pageIndex >= 0 && pageIndex < Pages.Count)
                return Pages[pageIndex];

            return null;
        }

        public int TotalPages => Pages.Count;
    }

    public static class ModManualManager
    {
        public static Dictionary<string, ModBook> AllBooks = new Dictionary<string, ModBook>(StringComparer.OrdinalIgnoreCase);

        public static void LoadAllManuals()
        {
            AllBooks.Clear();

            ModController.ShowDisabledMods = true;
            List<Mod> allMods = ModManager.GetLoadedMods();
            ModController.ShowDisabledMods = false;

            if (allMods == null) return;

            foreach (Mod mod in allMods)
            {
                string manualPath = Path.Combine(mod.Path, "ModManual.xml");
                if (!File.Exists(manualPath)) continue;

                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(manualPath);

                    foreach (XmlNode bookNode in xmlDoc.DocumentElement.ChildNodes)
                    {
                        if (bookNode.Name != "Book") continue;

                        string id = bookNode.Attributes["id"]?.Value;
                        if (string.IsNullOrEmpty(id)) continue;

                        // Parse Book-level defaults
                        ModBook book = new ModBook
                        {
                            ID = id,
                            ModSource = mod.Name,
                            Title = bookNode.Attributes["title"]?.Value ?? "Untitled Guide",
                            Author = bookNode.Attributes["author"]?.Value ?? "Unknown",
                            Icon = bookNode.Attributes["icon"]?.Value ?? "ui_game_symbol_book",

                            IsReadme = (bookNode.Attributes["is_readme"] != null) && bookNode.Attributes["is_readme"].Value.Equals("true", StringComparison.OrdinalIgnoreCase),

                            // Grab defaults, or fall back to standard 7D2D styles
                            DefaultBackground = bookNode.Attributes["default_background"]?.Value ?? "menu_empty",
                            DefaultFont = bookNode.Attributes["default_font"]?.Value ?? "UIFontSmall",
                            DefaultTextColor = bookNode.Attributes["default_text_color"]?.Value ?? "255,255,255,255"
                        };

                        // Parse individual pages
                        foreach (XmlNode pageNode in bookNode.ChildNodes)
                        {
                            if (pageNode.Name != "Page") continue;

                            ModPage page = new ModPage
                            {
                                Title = pageNode.Attributes["title"]?.Value ?? "",
                                ImageName = pageNode.Attributes["image"]?.Value ?? "",
                                Text = pageNode.InnerText?.Trim() ?? "",

                                // If the page doesn't specify a style, inherit the Book's default!
                                Background = pageNode.Attributes["background"]?.Value ?? book.DefaultBackground,
                                Font = pageNode.Attributes["font"]?.Value ?? book.DefaultFont,
                                TextColor = pageNode.Attributes["text_color"]?.Value ?? book.DefaultTextColor
                            };

                            book.Pages.Add(page);
                        }

                        if (book.Pages.Count > 0)
                        {
                            AllBooks[id] = book;
                            Logger.Info($"[ModManualManager] Loaded {(book.IsReadme ? "Readme" : "Manual")} '{book.Title}' from '{mod.Name}'");
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[ModManualManager] Failed to parse ModManual.xml for {mod.Name}: {e.Message}");
                }
            }
        }
    }
}