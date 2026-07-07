using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class CustomTexture
    {
        public int PaintID;
        public string Name;
        public string BundlePath;
        public string DiffuseName;
        public string NormalName;
        public string SpecularName;
        public int NewSliceIndex;
    }

    public static class CustomTextureManager
    {
        public static List<CustomTexture> CustomTextures = new List<CustomTexture>();

        private const int BASE_TEXTURE_ID = 1000;
        private static Dictionary<string, int> modTextureCounts = new Dictionary<string, int>();


        public static int RegisterTexture(Mod modInstance, string bundlePath, string diffuse, string normal, string specular, string name = "CustomTexture")
        {
            if (modInstance == null) return -1;

            int modIndex = ModManager.GetLoadedMods().IndexOf(modInstance);
            if (modIndex == -1) modIndex = 99; 

            string modName = modInstance.Name;
            if (!modTextureCounts.ContainsKey(modName))
            {
                modTextureCounts[modName] = 0;
            }

            int currentCount = modTextureCounts[modName];
            if (currentCount >= 32)
            {
                Logger.Warning($"[CustomTextureManager] Mod '{modName}' exceeded the 32 custom texture limit!");
                return -1;
            }

            int assignedPaintId = BASE_TEXTURE_ID + (modIndex * 32) + currentCount;
            modTextureCounts[modName]++;

            CustomTextures.Add(new CustomTexture
            {
                PaintID = assignedPaintId,
                BundlePath = bundlePath,
                DiffuseName = diffuse,
                NormalName = normal,
                SpecularName = specular,
                Name = name
            });

            string settingKey = $"TextureID_{name.Replace(" ", "_")}";

            if (!ModSettingsManager.AllModSettings.ContainsKey(modName))
            {
                ModSettingsManager.AllModSettings[modName] = new List<ModSetting>();
            }

            ModSettingsManager.AllModSettings[modName].Add(new ModSetting
            {
                ModName = modName,
                Name = settingKey,
                Value = assignedPaintId.ToString(),
                Type = "int",
                Hidden = true
            });

            Logger.Info($"Registered Texture '{name}' for '{modName}' at Static PaintID: {assignedPaintId} (Slot: {currentCount + 1}/32)");
            return assignedPaintId;
        }

        public static void InjectBlockTextureData()
        {
            if (CustomTextures.Count == 0) return;

            Logger.Info("Injecting Custom BlockTextureData...");

            int maxId = 0;
            foreach (var tex in CustomTextures)
            {
                if (tex.PaintID > maxId) maxId = tex.PaintID;
            }

            int targetSize = Math.Max(BlockTextureData.list != null ? BlockTextureData.list.Length : 2048, maxId + 50);

            if (BlockTextureData.list == null)
            {
                BlockTextureData.list = new BlockTextureData[targetSize];
            }
            else if (BlockTextureData.list.Length < targetSize)
            {
                Array.Resize(ref BlockTextureData.list, targetSize);
            }

            foreach (var tex in CustomTextures)
            {
                BlockTextureData data = new BlockTextureData();
                data.ID = tex.PaintID; // how the game finds it
                data.Name = tex.Name;
                data.Group = "Custom";
                data.SortIndex = 1;
                data.PaintCost = 1;
                data.LocalizedName = tex.Name;
                data.TextureID = (ushort)tex.NewSliceIndex;  // where it is in the atlas

                BlockTextureData.list[tex.PaintID] = data;
                Logger.Info($"Registered BlockTextureData for '{tex.Name}' at Paint ID {tex.PaintID}/{tex.NewSliceIndex}");
            }
        }

        public static Texture2DArray ExpandTextureArray(Texture2DArray original, string textureType)
        { 

            if (original == null) return null;

            Logger.Info("expanding " + textureType);

            int oldDepth = original.depth;
            int newDepth = oldDepth + CustomTextures.Count;
            bool isLinear = (textureType != "Diffuse");
            Texture2DArray newArray = new Texture2DArray(
                original.width,
                original.height,
                newDepth,
                original.format,
                original.mipmapCount > 1,
                isLinear
            );

            newArray.filterMode = original.filterMode;
            newArray.wrapMode = original.wrapMode;
            newArray.anisoLevel = original.anisoLevel;

            for (int i = 0; i < oldDepth; i++)
            {
                for (int mip = 0; mip < original.mipmapCount; mip++)
                {
                    Graphics.CopyTexture(original, i, mip, newArray, i, mip);
                }
            }

            for (int i = 0; i < CustomTextures.Count; i++)
            {
                CustomTexture customTex = CustomTextures[i];
                int newSliceIndex = oldDepth + i;
                customTex.NewSliceIndex = newSliceIndex;

                AssetBundle bundle = AssetBundle.LoadFromFile(customTex.BundlePath);

                if (bundle != null)
                {
                    string targetTexName = textureType == "Diffuse" ? customTex.DiffuseName :
                                           textureType == "Normal" ? customTex.NormalName : customTex.SpecularName;

                    if (!string.IsNullOrEmpty(targetTexName))
                    {
                        Texture2D tex = bundle.LoadAsset<Texture2D>(targetTexName);

                        if (tex != null)
                        {
                            if (tex.mipmapCount != original.mipmapCount)
                            {
                                Logger.Error($"MIPMAP MISHAP! {targetTexName} needs to equal " + original.mipmapCount + " it is " + tex.mipmapCount);
                            } 
                           
                            int mipsToCopy = Math.Min(tex.mipmapCount, original.mipmapCount);
                            for (int mip = 0; mip < mipsToCopy; mip++)
                            {
                                Graphics.CopyTexture(tex, 0, mip, newArray, newSliceIndex, mip);
                            }
                        }
                        else
                            Logger.Warning("Tex invalid: " + customTex.BundlePath);
                    }
                    else
                        Logger.Warning("Tex Empty " + customTex.BundlePath);

                    bundle.Unload(false);
                }
                else
                    Logger.Warning("Bundle invalid: " + customTex.BundlePath);
            }

            return newArray;
        }

        public static IEnumerator WaitAndExpandTextures(MeshDescription meshDesc)
        {
            Logger.Info("Waiting for vanilla textures to load asynchronously...");

            while (meshDesc.TexDiffuse == null)
            {
                yield return new WaitForSeconds(0.1f);
            }

            Logger.Info("Vanilla textures loaded! Expanding arrays...");

            Texture oldDiffuse = meshDesc.TexDiffuse;
            Texture oldNormal = meshDesc.TexNormal;
            Texture oldSpecular = meshDesc.TexSpecular;
            Texture2DArray newDiffuse = ExpandTextureArray((Texture2DArray)oldDiffuse, "Diffuse");
            Texture2DArray newNormal = ExpandTextureArray((Texture2DArray)oldNormal, "Normal");
            Texture2DArray newSpecular = ExpandTextureArray((Texture2DArray)oldSpecular, "Specular");

            meshDesc.TexDiffuse = newDiffuse;
            meshDesc.TexNormal = newNormal;
            meshDesc.TexSpecular = newSpecular;

            if (meshDesc.material != null)
            {
                UpdateMaterialProperty(meshDesc.material, "_MainTex", oldDiffuse, newDiffuse);
                UpdateMaterialProperty(meshDesc.material, "_TextureArray", oldDiffuse, newDiffuse);
                UpdateMaterialProperty(meshDesc.material, "_BumpMap", oldNormal, newNormal);
                UpdateMaterialProperty(meshDesc.material, "_Normal", oldNormal, newNormal);
                UpdateMaterialProperty(meshDesc.material, "_SpecularMap", oldSpecular, newSpecular);
                UpdateMaterialProperty(meshDesc.material, "_GlossMap", oldSpecular, newSpecular);
                UpdateMaterialProperty(meshDesc.material, "_MetallicGlossMap", oldSpecular, newSpecular);

                foreach (var tex in CustomTextures)
                {
                    if (BlockTextureData.list != null && BlockTextureData.list[tex.PaintID] != null)
                    {
                        BlockTextureData.list[tex.PaintID].TextureID = (ushort)tex.NewSliceIndex;
                        Logger.Info($"Mapped UI PaintID {tex.PaintID} to GPU Slice {tex.NewSliceIndex}");
                    }
                }
            }
        }

        private static void UpdateMaterialProperty(Material mat, string propertyName, Texture oldTex, Texture newTex)
        {
            if (mat.HasProperty(propertyName))
            {
                if (mat.GetTexture(propertyName) == oldTex)
                {
                    mat.SetTexture(propertyName, newTex);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Block), "Init")]
    public class Block_Init_Patch
    {
        private static bool _initialized = false;

        public static void Postfix()
        {
            if (!_initialized)
            {
                CustomTextureManager.InjectBlockTextureData();
                _initialized = true;
            }
        }
    }

    [HarmonyPatch(typeof(MeshDescription), "LoadTextureArraysForQuality")]
    public class MeshDescription_LoadTextureArrays_Patch
    {
        public static void Postfix(MeshDescription __instance)
        {
            if (__instance.Name.ToLower() != "opaque" || CustomTextureManager.CustomTextures.Count == 0) return;

            ThreadManager.StartCoroutine(CustomTextureManager.WaitAndExpandTextures(__instance));
        }
    }
}
