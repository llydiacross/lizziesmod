using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LizziesMod.Harmony
{
    public class ModTextures
    {

        public static string GetPath(Mod modInstance, string fileName= "Atlas.unity3d")
        {
            return modInstance.Path + "/Resources/" + fileName;
        }

        public static void Init(Mod modInstance, string fileName="Atlas.unity3d")
        {
            CustomTextureManager.RegisterTexture(modInstance, GetPath(modInstance, fileName), "Bark_Pine_001_Diffuse", "Bark_Pine_001_Normal", "Bark_Pine_001_Specular", "TestTexture");
        }
    }
}
