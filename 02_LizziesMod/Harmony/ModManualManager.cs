using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

namespace LizziesMod
{
    public class ModPage
    {
        public string Title;
        public string Text;
        public string ImageName;

        public string Background;
        public string Font;
        public string TextColor;

        public Vector2i? ImagePos;
        public Vector2i? ImageSize;
        public Vector2i? TextPos;
        public Vector2i? TextSize;
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

        public static Vector2i? ParseVector2i(string val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            string[] parts = val.Split(',');
            if (parts.Length == 2 &&
                int.TryParse(parts[0].Trim(), out int x) &&
                int.TryParse(parts[1].Trim(), out int y))
            {
                return new Vector2i(x, y);
            }
            return null;
        }

        public static void LoadAllManuals()
        {
            AllBooks.Clear();

            ModController.ShowDisabledMods = true;
            List<Mod> allMods = global::ModManager.GetLoadedMods();
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

                        ModBook book = new ModBook
                        {
                            ID = id,
                            ModSource = mod.Name,
                            Title = bookNode.Attributes["title"]?.Value ?? "Untitled Guide",
                            Author = bookNode.Attributes["author"]?.Value ?? "Unknown",
                            Icon = bookNode.Attributes["icon"]?.Value ?? "ui_game_symbol_book",
                            IsReadme = (bookNode.Attributes["is_readme"] != null) && bookNode.Attributes["is_readme"].Value.Equals("true", StringComparison.OrdinalIgnoreCase),

                            DefaultBackground = bookNode.Attributes["default_background"]?.Value ?? "menu_empty",
                            DefaultFont = bookNode.Attributes["default_font"]?.Value ?? "UIFontSmall",
                            DefaultTextColor = bookNode.Attributes["default_text_color"]?.Value ?? "255,255,255,255",
                        };

                        foreach (XmlNode pageNode in bookNode.ChildNodes)
                        {
                            if (pageNode.Name != "Page") continue;

                            ModPage page = new ModPage
                            {
                                Title = pageNode.Attributes["title"]?.Value ?? "",
                                ImageName = pageNode.Attributes["image"]?.Value ?? "",
                                Text = pageNode.InnerText?.Trim() ?? "",

                                Background = pageNode.Attributes["background"]?.Value ?? book.DefaultBackground,
                                Font = pageNode.Attributes["font"]?.Value ?? book.DefaultFont,
                                TextColor = pageNode.Attributes["text_color"]?.Value ?? book.DefaultTextColor,

                                ImagePos = ParseVector2i(pageNode.Attributes["image_pos"]?.Value),
                                ImageSize = ParseVector2i(pageNode.Attributes["image_size"]?.Value),
                                TextPos = ParseVector2i(pageNode.Attributes["text_pos"]?.Value),
                                TextSize = ParseVector2i(pageNode.Attributes["text_size"]?.Value)
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