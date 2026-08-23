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
        public Vector2i? CanvasSize;
        public List<ReadmeTextArea> TextAreas = new List<ReadmeTextArea>();
        public List<ReadmeImage> Images = new List<ReadmeImage>();

        public bool UsesReadmeLayout => TextAreas.Count > 0 || Images.Count > 0;
    }

    public class ReadmeTextArea
    {
        public string Id;
        public string Text;
        public string Font;
        public string TextColor;
        public Vector2i Position;
        public Vector2i Size;
    }

    public class ReadmeImage
    {
        public string Id;
        public string Source;
        public Vector2i Position;
        public Vector2i Size;
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

            ModPatcher.ShowDisabledMods = true;
            List<Mod> allMods = global::ModManager.GetLoadedMods();
            ModPatcher.ShowDisabledMods = false;

            if (allMods == null) return;

            foreach (Mod mod in allMods)
            {
                 string manualPath = Path.Combine(mod.Path, "Config", "ModManual.xml");
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

                                Background = pageNode.Attributes["background"]?.Value ?? book.DefaultBackground,
                                Font = pageNode.Attributes["font"]?.Value ?? book.DefaultFont,
                                TextColor = pageNode.Attributes["text_color"]?.Value ?? book.DefaultTextColor,

                                ImagePos = ParseVector2i(pageNode.Attributes["image_pos"]?.Value),
                                ImageSize = ParseVector2i(pageNode.Attributes["image_size"]?.Value),
                                TextPos = ParseVector2i(pageNode.Attributes["text_pos"]?.Value),
                                TextSize = ParseVector2i(pageNode.Attributes["text_size"]?.Value)
                            };

                            if (book.IsReadme)
                            {
                                page.CanvasSize = ParseVector2i(pageNode.Attributes["canvas_size"]?.Value);
                                ParseReadmeLayout(pageNode, page, manualPath, book);
                            }

                            page.Text = page.UsesReadmeLayout ? "" : pageNode.InnerText?.Trim() ?? "";

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
                    Logger.Error($"[ModManualManager] Failed to parse Config/ModManual.xml for {mod.Name}: {e.Message}");
                }
            }
        }

        private static void ParseReadmeLayout(XmlNode pageNode, ModPage page, string manualPath, ModBook book)
        {
            HashSet<string> elementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int textAreaIndex = 0;
            int imageIndex = 0;

            foreach (XmlNode elementNode in pageNode.ChildNodes)
            {
                if (elementNode.Name == "TextArea")
                {
                    string id = elementNode.Attributes["id"]?.Value ?? "text-" + (++textAreaIndex);
                    if (!elementIds.Add(id))
                    {
                        Logger.Warning($"[ModLibrary] Ignoring duplicate README element id '{id}' in '{manualPath}'.");
                        continue;
                    }

                    if (!TryParseReadmeElementBounds(elementNode, manualPath, id, out Vector2i position, out Vector2i size)) continue;

                    page.TextAreas.Add(new ReadmeTextArea
                    {
                        Id = id,
                        Text = elementNode.InnerText?.Trim() ?? "",
                        Font = elementNode.Attributes["font"]?.Value ?? page.Font ?? book.DefaultFont,
                        TextColor = elementNode.Attributes["text_color"]?.Value ?? page.TextColor ?? book.DefaultTextColor,
                        Position = position,
                        Size = size
                    });
                }
                else if (elementNode.Name == "Image")
                {
                    string id = elementNode.Attributes["id"]?.Value ?? "image-" + (++imageIndex);
                    if (!elementIds.Add(id))
                    {
                        Logger.Warning($"[ModLibrary] Ignoring duplicate README element id '{id}' in '{manualPath}'.");
                        continue;
                    }

                    string source = elementNode.Attributes["source"]?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        Logger.Warning($"[ModLibrary] Ignoring README image '{id}' in '{manualPath}' because source is missing.");
                        continue;
                    }

                    if (!TryParseReadmeElementBounds(elementNode, manualPath, id, out Vector2i position, out Vector2i size)) continue;

                    page.Images.Add(new ReadmeImage
                    {
                        Id = id,
                        Source = source,
                        Position = position,
                        Size = size
                    });
                }
            }
        }

        private static bool TryParseReadmeElementBounds(XmlNode elementNode, string manualPath, string elementId, out Vector2i position, out Vector2i size)
        {
            position = default(Vector2i);
            size = default(Vector2i);

            Vector2i? parsedPosition = ParseVector2i(elementNode.Attributes["pos"]?.Value);
            Vector2i? parsedSize = ParseVector2i(elementNode.Attributes["size"]?.Value);
            if (!parsedPosition.HasValue || !parsedSize.HasValue || parsedSize.Value.x <= 0 || parsedSize.Value.y <= 0)
            {
                Logger.Warning($"[ModLibrary] Ignoring README element '{elementId}' in '{manualPath}' because it needs valid pos and positive size attributes.");
                return false;
            }

            position = parsedPosition.Value;
            size = parsedSize.Value;
            return true;
        }
    }
}