using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;

namespace LizziesMod
{
   
    public class ModPage
    {
        public string Title;
        public string Text;
        public string ImageName; 
    }

    public class ModBook
    {
        public string ID;
        public string ModSource;
        public string Title;
        public string Author;
        public string Icon;
        public int StartPage;

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
    
        public static Dictionary<string, ModBook> AllBooks = new Dictionary<string, ModBook>(System.StringComparer.OrdinalIgnoreCase);

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
                            StartPage = int.TryParse(bookNode.Attributes["start_page"]?.Value, out int sp) ? sp : 0
                        };

        
                        foreach (XmlNode pageNode in bookNode.ChildNodes)
                        {
                            if (pageNode.Name != "Page") continue;

                            ModPage page = new ModPage
                            {
                                Title = pageNode.Attributes["title"]?.Value ?? "",
                                ImageName = pageNode.Attributes["image"]?.Value ?? "",
                                Text = pageNode.InnerText?.Trim() ?? ""
                            };

                            book.Pages.Add(page);
                        }

                        if (book.Pages.Count > 0)
                        {
                            AllBooks[id] = book;
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
