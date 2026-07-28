using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

namespace LizziesMod
{
    public class CustomTexture
    {
        public int PaintID = -1;
        public ushort TextureID;
        public string Name;
        public string DisplayName;
        public string BundlePath;
        public string DiffuseName;
        public string NormalName;
        public string SpecularName;
        public string Group = "Custom";
        public ushort PaintCost = 1;
        public byte SortIndex = byte.MaxValue;
        public bool Hidden;
        public int NewSliceIndex = -1;
    }

    public static class CustomTextureManager
    {
        public const int FirstCustomTextureId = 256;

        public static readonly List<CustomTexture> CustomTextures = new List<CustomTexture>();

        private static readonly Dictionary<string, int> textureIdsByName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<CustomTexture> activeTextures = new List<CustomTexture>();
        private static bool definitionsLoaded;
        private static bool registrationsFinalized;

        public static void LoadAllTextures()
        {
            if (definitionsLoaded) return;

            definitionsLoaded = true;
            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                if (mod == null || string.IsNullOrEmpty(mod.Path)) continue;

                string configPath = Path.Combine(mod.Path, "Config", "CustomTextures.xml");
                if (File.Exists(configPath)) LoadTextureConfig(mod, configPath);
            }

            Logger.Info($"[CustomTextures] Loaded {CustomTextures.Count} texture definition(s).");
        }

        public static IEnumerator RegisterAfterVanillaTextureLoad(IEnumerator vanillaLoad)
        {
            while (vanillaLoad != null && vanillaLoad.MoveNext())
            {
                yield return vanillaLoad.Current;
            }

            FinalizeRegistrations();
        }

        public static void ResolveBlockTextureReferences(XmlFile xmlFile)
        {
            if (xmlFile == null || textureIdsByName.Count == 0) return;

            XElement root = xmlFile.XmlDoc?.Root;
            if (root == null) return;

            foreach (XElement block in root.Elements("block"))
            {
                foreach (XElement property in block.Descendants("property"))
                {
                    XAttribute propertyName = property.Attribute("name");
                    XAttribute propertyValue = property.Attribute("value");
                    if (propertyName == null || propertyValue == null) continue;
                    if (propertyName.Value != "Texture" && propertyName.Value != "UiBackgroundTexture") continue;

                    string resolvedValue = ResolveTextureValue(propertyValue.Value);
                    if (!string.Equals(resolvedValue, propertyValue.Value, StringComparison.Ordinal))
                    {
                        propertyValue.Value = resolvedValue;
                    }
                }
            }
        }

        public static IEnumerator WaitAndExpandTextures(MeshDescription meshDescription)
        {
            while (meshDescription.TexDiffuse == null || meshDescription.TexNormal == null || meshDescription.TexSpecular == null)
            {
                yield return new WaitForSeconds(0.1f);
            }

            Texture2DArray oldDiffuse = meshDescription.TexDiffuse as Texture2DArray;
            Texture2DArray oldNormal = meshDescription.TexNormal as Texture2DArray;
            Texture2DArray oldSpecular = meshDescription.TexSpecular as Texture2DArray;
            if (oldDiffuse == null || oldNormal == null || oldSpecular == null || IsExtendedArray(oldDiffuse)) yield break;

            Texture2DArray newDiffuse = ExpandTextureArray(oldDiffuse, "Diffuse");
            Texture2DArray newNormal = ExpandTextureArray(oldNormal, "Normal");
            Texture2DArray newSpecular = ExpandTextureArray(oldSpecular, "Specular");

            meshDescription.TexDiffuse = newDiffuse;
            meshDescription.TexNormal = newNormal;
            meshDescription.TexSpecular = newSpecular;

            TextureAtlasBlocks atlas = meshDescription.textureAtlas as TextureAtlasBlocks;
            if (atlas != null)
            {
                atlas.diffuseTexture = newDiffuse;
                atlas.normalTexture = newNormal;
                atlas.specularTexture = newSpecular;
            }

            if (meshDescription.material != null)
            {
                UpdateMaterialProperty(meshDescription.material, "_MainTex", oldDiffuse, newDiffuse);
                UpdateMaterialProperty(meshDescription.material, "_TextureArray", oldDiffuse, newDiffuse);
                UpdateMaterialProperty(meshDescription.material, "_BumpMap", oldNormal, newNormal);
                UpdateMaterialProperty(meshDescription.material, "_Normal", oldNormal, newNormal);
                UpdateMaterialProperty(meshDescription.material, "_SpecularMap", oldSpecular, newSpecular);
                UpdateMaterialProperty(meshDescription.material, "_GlossMap", oldSpecular, newSpecular);
                UpdateMaterialProperty(meshDescription.material, "_MetallicGlossMap", oldSpecular, newSpecular);
            }
        }

        private static void LoadTextureConfig(Mod mod, string configPath)
        {
            try
            {
                XDocument document = XDocument.Load(configPath);
                if (document.Root == null) return;

                foreach (XElement element in document.Root.Elements("opaque"))
                {
                    string name = GetAttribute(element, "id");
                    string bundlePath = GetAttribute(element, "bundle");
                    string diffuse = GetAttribute(element, "diffuse");
                    string normal = GetAttribute(element, "normal");
                    string specular = GetAttribute(element, "specular");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(bundlePath) ||
                        string.IsNullOrEmpty(diffuse) || string.IsNullOrEmpty(normal) || string.IsNullOrEmpty(specular))
                    {
                        Logger.Warning($"[CustomTextures] Ignoring malformed texture definition in '{configPath}'.");
                        continue;
                    }

                    if (!Path.IsPathRooted(bundlePath)) bundlePath = Path.Combine(mod.Path, bundlePath);

                    RegisterDefinition(new CustomTexture
                    {
                        Name = name,
                        DisplayName = GetAttribute(element, "name") ?? name,
                        BundlePath = bundlePath,
                        DiffuseName = diffuse,
                        NormalName = normal,
                        SpecularName = specular,
                        Group = GetAttribute(element, "group") ?? "Custom",
                        PaintCost = ParseUShort(GetAttribute(element, "paintCost"), 1),
                        SortIndex = ParseByte(GetAttribute(element, "sortIndex"), byte.MaxValue),
                        Hidden = ParseBool(GetAttribute(element, "hidden"))
                    }, mod.Name);
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[CustomTextures] Failed to load '{configPath}': {exception.Message}");
            }
        }

        private static void RegisterDefinition(CustomTexture definition, string modName)
        {
            if (string.IsNullOrEmpty(definition.Name)) return;

            foreach (CustomTexture existing in CustomTextures)
            {
                if (string.Equals(existing.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning($"[CustomTextures] Ignoring duplicate texture id '{definition.Name}' from '{modName}'.");
                    return;
                }
            }

            CustomTextures.Add(definition);
            Logger.Info($"[CustomTextures] Registered '{definition.Name}' from '{modName}'.");
        }

        private static void FinalizeRegistrations()
        {
            if (registrationsFinalized) return;

            registrationsFinalized = true;
            if (CustomTextures.Count == 0) return;

            MeshDescription opaqueMesh = MeshDescription.meshes[MeshDescription.MESH_OPAQUE];
            TextureAtlasBlocks atlas = opaqueMesh?.textureAtlas as TextureAtlasBlocks;
            Texture2DArray diffuse = atlas?.diffuseTexture as Texture2DArray;
            Texture2DArray normal = atlas?.normalTexture as Texture2DArray;
            Texture2DArray specular = atlas?.specularTexture as Texture2DArray;
            if (atlas == null || diffuse == null || normal == null || specular == null || BlockTextureData.list == null)
            {
                Logger.Error("[CustomTextures] The opaque texture atlas was unavailable; custom paints were not registered.");
                return;
            }

            List<CustomTexture> compatibleTextures = new List<CustomTexture>();
            foreach (CustomTexture texture in CustomTextures)
            {
                if (ValidateTextureAssets(texture, diffuse, normal, specular)) compatibleTextures.Add(texture);
            }

            HashSet<int> reservedPaintIds = new HashSet<int>();
            foreach (CustomTexture texture in compatibleTextures)
            {
                int paintId = FindFreePaintId(reservedPaintIds);
                if (paintId < 0)
                {
                    Logger.Error($"[CustomTextures] No free paint-menu slot remains for '{texture.Name}'.");
                    continue;
                }

                texture.PaintID = paintId;
                reservedPaintIds.Add(paintId);
                activeTextures.Add(texture);
            }

            if (activeTextures.Count == 0) return;

            int firstTextureId = Math.Max(FirstCustomTextureId, atlas.uvMapping?.Length ?? 0);
            if (firstTextureId + activeTextures.Count > ushort.MaxValue)
            {
                Logger.Error("[CustomTextures] Texture atlas ID limit reached; custom paints were not registered.");
                activeTextures.Clear();
                return;
            }

            if (atlas.uvMapping == null) atlas.uvMapping = new UVRectTiling[firstTextureId + activeTextures.Count];
            else if (atlas.uvMapping.Length < firstTextureId + activeTextures.Count)
                Array.Resize(ref atlas.uvMapping, firstTextureId + activeTextures.Count);

            for (int index = 0; index < activeTextures.Count; index++)
            {
                CustomTexture texture = activeTextures[index];
                int textureId = firstTextureId + index;
                texture.TextureID = (ushort)textureId;
                texture.NewSliceIndex = diffuse.depth + index;

                atlas.uvMapping[textureId] = new UVRectTiling
                {
                    index = texture.NewSliceIndex,
                    textureName = texture.Name,
                    uv = new Rect(0f, 0f, 1f, 1f),
                    bGlobalUV = false,
                    bSwitchUV = false
                };

                BlockTextureData data = new BlockTextureData
                {
                    ID = texture.PaintID,
                    TextureID = texture.TextureID,
                    Name = texture.DisplayName,
                    LocalizedName = texture.DisplayName,
                    Group = texture.Group,
                    PaintCost = texture.PaintCost,
                    SortIndex = texture.SortIndex,
                    Hidden = texture.Hidden
                };

                BlockTextureData.list[texture.PaintID] = data;
                data.Init();
                textureIdsByName.Add(texture.Name, textureId);
                Logger.Info($"[CustomTextures] '{texture.Name}' uses paint slot {texture.PaintID}, texture ID {textureId}, and atlas slice {texture.NewSliceIndex}.");
            }
        }

        private static int FindFreePaintId(HashSet<int> reservedPaintIds)
        {
            int lastPaintId = Math.Min(255, BlockTextureData.list.Length - 1);
            for (int paintId = 1; paintId <= lastPaintId; paintId++)
            {
                if (BlockTextureData.list[paintId] == null && !reservedPaintIds.Contains(paintId)) return paintId;
            }

            return -1;
        }

        private static bool ValidateTextureAssets(CustomTexture definition, Texture2DArray diffuse, Texture2DArray normal, Texture2DArray specular)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(definition.BundlePath);
            if (bundle == null)
            {
                Logger.Error($"[CustomTextures] '{definition.Name}' could not load bundle '{definition.BundlePath}'.");
                return false;
            }

            try
            {
                return ValidateTextureAsset(bundle, definition, "Diffuse", definition.DiffuseName, diffuse) &&
                    ValidateTextureAsset(bundle, definition, "Normal", definition.NormalName, normal) &&
                    ValidateTextureAsset(bundle, definition, "Specular", definition.SpecularName, specular);
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        private static bool ValidateTextureAsset(AssetBundle bundle, CustomTexture definition, string channel, string assetName, Texture2DArray target)
        {
            Texture2D source = bundle.LoadAsset<Texture2D>(assetName);
            if (source == null)
            {
                Logger.Error($"[CustomTextures] '{definition.Name}' is missing its {channel} asset '{assetName}'.");
                return false;
            }

            if (TryGetCompatibleBaseMip(source, target, out int baseMip)) return true;

            Logger.Error(
                $"[CustomTextures] '{definition.Name}' {channel} asset '{assetName}' is incompatible. " +
                $"Expected {target.width}x{target.height}, {target.format}, and {target.mipmapCount} usable mip level(s); " +
                $"received {source.width}x{source.height}, {source.format}, and {source.mipmapCount} mip level(s).");
            return false;
        }

        private static Texture2DArray ExpandTextureArray(Texture2DArray original, string textureType)
        {
            Texture2DArray expanded = new Texture2DArray(
                original.width,
                original.height,
                original.depth + activeTextures.Count,
                original.format,
                original.mipmapCount > 1,
                textureType != "Diffuse");

            expanded.name = "lizzies_extended_" + textureType.ToLowerInvariant();
            expanded.filterMode = original.filterMode;
            expanded.wrapMode = original.wrapMode;
            expanded.anisoLevel = original.anisoLevel;

            for (int slice = 0; slice < original.depth; slice++)
            {
                for (int mip = 0; mip < original.mipmapCount; mip++)
                {
                    Graphics.CopyTexture(original, slice, mip, expanded, slice, mip);
                }
            }

            foreach (CustomTexture customTexture in activeTextures)
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(customTexture.BundlePath);
                if (bundle == null) continue;

                try
                {
                    string assetName = GetAssetName(customTexture, textureType);
                    Texture2D source = bundle.LoadAsset<Texture2D>(assetName);
                    if (source == null || !TryGetCompatibleBaseMip(source, original, out int baseMip)) continue;

                    for (int mip = 0; mip < original.mipmapCount; mip++)
                    {
                        Graphics.CopyTexture(source, 0, baseMip + mip, expanded, customTexture.NewSliceIndex, mip);
                    }
                }
                finally
                {
                    bundle.Unload(false);
                }
            }

            return expanded;
        }

        private static bool TryGetCompatibleBaseMip(Texture2D source, Texture2DArray target, out int baseMip)
        {
            baseMip = 0;
            if (source.format != target.format) return false;

            int sourceWidth = source.width;
            int sourceHeight = source.height;
            while (sourceWidth > target.width || sourceHeight > target.height)
            {
                sourceWidth = Math.Max(1, sourceWidth / 2);
                sourceHeight = Math.Max(1, sourceHeight / 2);
                baseMip++;
            }

            return sourceWidth == target.width &&
                sourceHeight == target.height &&
                source.mipmapCount >= baseMip + target.mipmapCount;
        }

        private static string ResolveTextureValue(string value)
        {
            string[] textureNames = value.Split(',');
            bool resolvedAny = false;
            for (int index = 0; index < textureNames.Length; index++)
            {
                string textureName = textureNames[index].Trim();
                if (textureIdsByName.TryGetValue(textureName, out int textureId))
                {
                    textureNames[index] = textureId.ToString(CultureInfo.InvariantCulture);
                    resolvedAny = true;
                }
            }

            return resolvedAny ? string.Join(",", textureNames) : value;
        }

        private static string GetAssetName(CustomTexture texture, string textureType)
        {
            if (textureType == "Diffuse") return texture.DiffuseName;
            if (textureType == "Normal") return texture.NormalName;
            return texture.SpecularName;
        }

        private static string GetAttribute(XElement element, string name)
        {
            return element.Attribute(name)?.Value;
        }

        private static ushort ParseUShort(string value, ushort defaultValue)
        {
            return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsed) ? parsed : defaultValue;
        }

        private static byte ParseByte(string value, byte defaultValue)
        {
            return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte parsed) ? parsed : defaultValue;
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool parsed) && parsed;
        }

        private static bool IsExtendedArray(Texture2DArray texture)
        {
            return texture != null && texture.name.StartsWith("lizzies_extended_", StringComparison.Ordinal);
        }

        private static void UpdateMaterialProperty(Material material, string propertyName, Texture oldTexture, Texture newTexture)
        {
            if (material.HasProperty(propertyName) && material.GetTexture(propertyName) == oldTexture)
            {
                material.SetTexture(propertyName, newTexture);
            }
        }
    }

    [HarmonyPatch(typeof(BlockTexturesFromXML), "CreateBlockTextures")]
    public class BlockTexturesFromXML_CreateBlockTextures_Patch
    {
        public static void Postfix(ref IEnumerator __result)
        {
            __result = CustomTextureManager.RegisterAfterVanillaTextureLoad(__result);
        }
    }

    [HarmonyPatch(typeof(BlocksFromXml), "CreateBlocks")]
    public class BlocksFromXml_CreateBlocks_Patch
    {
        public static void Prefix(XmlFile _xmlFile)
        {
            CustomTextureManager.ResolveBlockTextureReferences(_xmlFile);
        }
    }

    [HarmonyPatch(typeof(MeshDescription), "LoadTextureArraysForQuality")]
    public class MeshDescription_LoadTextureArrays_Patch
    {
        public static void Postfix(MeshDescription __instance)
        {
            if (!string.Equals(__instance.Name, "opaque", StringComparison.OrdinalIgnoreCase) || CustomTextureManager.CustomTextures.Count == 0) return;

            ThreadManager.StartCoroutine(CustomTextureManager.WaitAndExpandTextures(__instance));
        }
    }
}
