using System;
using System.Collections.Generic;
using System.IO;

namespace LizziesMod
{
 
    public class MainMenuModIconsController : XUiController
    {
        private XUiController grid;

        public override void Init()
        {
            base.Init();
            grid = GetChildById("modIconsGrid");
        }

        public override void OnOpen()
        {
            base.OnOpen();

            if (grid == null) return;

            List<Mod> modsWithIcons = new List<Mod>();
            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                if (File.Exists(Path.Combine(mod.Path, "atlas.png")) ||
                    File.Exists(Path.Combine(mod.Path, "icon.png")))
                {
                    modsWithIcons.Add(mod);
                }
            }


            int index = 0;
            foreach (var child in grid.Children)
            {
                if (child is ModIconEntryController entry)
                {
                    if (index < modsWithIcons.Count)
                    {
                        entry.SetMod(modsWithIcons[index]);
                        index++;
                    }
                    else
                    {
                        entry.Clear();
                    }
                }
            }
        }
    }

    public class ModIconEntryController : XUiController
    {
        private XUiV_Texture imgMenuIcon;

        public override void Init()
        {
            base.Init();
            XUiController iconControl = GetChildById("imgMenuIcon");
            if (iconControl != null)
            {
                imgMenuIcon = iconControl.viewComponent as XUiV_Texture;
            }
        }

        public void SetMod(Mod mod)
        {
            if (imgMenuIcon == null) return;
            string iconPath = Path.Combine(mod.Path, "atlas.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(mod.Path, "icon.png");
            }

            try
            {

                byte[] fileData = File.ReadAllBytes(iconPath);
                UnityEngine.Texture2D tex = new UnityEngine.Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
                UnityEngine.ImageConversion.LoadImage(tex, fileData);

                imgMenuIcon.Texture = tex;
                viewComponent.IsVisible = true;
                imgMenuIcon.IsVisible = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load main menu icon for '{mod.Name}': {ex.Message}");
                Clear();
            }
        }

        public void Clear()
        {
            viewComponent.IsVisible = false;
            if (imgMenuIcon != null) imgMenuIcon.IsVisible = false;
        }
    }
}