using System;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    internal static class DefaultSkyCubemapImportUtility
    {
        internal const string DefaultSkyRelativeAssetPath = "Texture/Default/DefaultHDRISky.exr";

        [InitializeOnLoadMethod]
        private static void RegisterDefaultSkyImportFixup()
        {
            EditorApplication.delayCall += EnsureDefaultSkyImportSettings;
        }

        internal static bool IsDefaultSkyAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            var normalizedPath = assetPath.Replace('\\', '/');
            var candidatePaths = VividPackagePathUtility.GetCandidateAssetPaths(DefaultSkyRelativeAssetPath);
            for (var i = 0; i < candidatePaths.Length; i++)
            {
                if (string.Equals(normalizedPath, candidatePaths[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static void EnsureDefaultSkyImportSettings()
        {
            var assetPath = VividPackagePathUtility.GetPreferredAssetPath(DefaultSkyRelativeAssetPath);
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
                return;

            if (!ApplyImportSettings(textureImporter))
                return;

            textureImporter.SaveAndReimport();
        }

        internal static bool ApplyImportSettings(TextureImporter textureImporter)
        {
            if (textureImporter == null)
                return false;

            var hasChanges = false;

            if (textureImporter.textureShape != TextureImporterShape.TextureCube)
            {
                textureImporter.textureShape = TextureImporterShape.TextureCube;
                hasChanges = true;
            }

            // Match HDRP's checked-in import settings for DefaultHDRISky.exr so the same source texture
            // follows the same cubemap detection, filtering and platform format path.
            if (textureImporter.generateCubemap != TextureImporterGenerateCubemap.AutoCubemap)
            {
                textureImporter.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                hasChanges = true;
            }

            if (!textureImporter.mipmapEnabled)
            {
                textureImporter.mipmapEnabled = true;
                hasChanges = true;
            }

            if (textureImporter.npotScale != TextureImporterNPOTScale.ToNearest)
            {
                textureImporter.npotScale = TextureImporterNPOTScale.ToNearest;
                hasChanges = true;
            }

            if (textureImporter.filterMode != FilterMode.Trilinear)
            {
                textureImporter.filterMode = FilterMode.Trilinear;
                hasChanges = true;
            }

            if (textureImporter.anisoLevel != -1)
            {
                textureImporter.anisoLevel = -1;
                hasChanges = true;
            }

            if (!Mathf.Approximately(textureImporter.mipMapBias, -100.0f))
            {
                textureImporter.mipMapBias = -100.0f;
                hasChanges = true;
            }

            if (textureImporter.wrapModeU != TextureWrapMode.Clamp)
            {
                textureImporter.wrapModeU = TextureWrapMode.Clamp;
                hasChanges = true;
            }

            if (textureImporter.wrapModeV != TextureWrapMode.Clamp)
            {
                textureImporter.wrapModeV = TextureWrapMode.Clamp;
                hasChanges = true;
            }

            if (textureImporter.wrapModeW != TextureWrapMode.Clamp)
            {
                textureImporter.wrapModeW = TextureWrapMode.Clamp;
                hasChanges = true;
            }

            var standaloneSettings = textureImporter.GetPlatformTextureSettings("Standalone");
            if (standaloneSettings.overridden)
            {
                textureImporter.ClearPlatformTextureSettings("Standalone");
                hasChanges = true;
            }

            return hasChanges;
        }
    }

    internal sealed class DefaultSkyTextureImportPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion()
        {
            return 2;
        }

        public void OnPreprocessTexture()
        {
            if (!DefaultSkyCubemapImportUtility.IsDefaultSkyAsset(assetPath))
                return;

            if (assetImporter is TextureImporter textureImporter)
                DefaultSkyCubemapImportUtility.ApplyImportSettings(textureImporter);
        }
    }
}
