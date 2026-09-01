using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    internal readonly struct GPUDrivenMaterialProxySourceTextures
    {
        internal GPUDrivenMaterialProxySourceTextures(
            Texture2D baseMap,
            Texture2D bumpMap,
            Texture2D maskMap,
            GPUDrivenMaterialMaskMode maskMode)
        {
            BaseMap = baseMap;
            BumpMap = bumpMap;
            MaskMap = maskMap;
            MaskMode = maskMode;
        }

        internal Texture2D BaseMap { get; }

        internal Texture2D BumpMap { get; }

        internal Texture2D MaskMap { get; }

        internal GPUDrivenMaterialMaskMode MaskMode { get; }

        internal bool HasAnyTexture => BaseMap != null || BumpMap != null || MaskMap != null;
    }

    internal readonly struct GPUDrivenMaterialProxySyncResult
    {
        public GPUDrivenMaterialProxySyncResult(bool success, bool changed, string errorMessage, string[] warnings)
        {
            Success = success;
            Changed = changed;
            ErrorMessage = errorMessage;
            Warnings = warnings ?? System.Array.Empty<string>();
        }

        public bool Success { get; }

        public bool Changed { get; }

        public string ErrorMessage { get; }

        public string[] Warnings { get; }
    }

    internal static class GPUDrivenMaterialProxySyncUtility
    {
        internal const string StandardLitShaderName = "VividRP/Material/StandardLit";

        private const float EnabledThreshold = 0.001f;

        private static readonly int s_BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int s_BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int s_MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int s_SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int s_MetallicRemapMinId = Shader.PropertyToID("_MetallicRemapMin");
        private static readonly int s_MetallicRemapMaxId = Shader.PropertyToID("_MetallicRemapMax");
        private static readonly int s_SmoothnessRemapMinId = Shader.PropertyToID("_SmoothnessRemapMin");
        private static readonly int s_SmoothnessRemapMaxId = Shader.PropertyToID("_SmoothnessRemapMax");
        private static readonly int s_AORemapMinId = Shader.PropertyToID("_AORemapMin");
        private static readonly int s_AORemapMaxId = Shader.PropertyToID("_AORemapMax");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int s_CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_OpacityMapId = Shader.PropertyToID("_OpacityMap");
        private static readonly int s_RMOMapId = Shader.PropertyToID("_RMOMap");
        private static readonly int s_MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int s_RoughnessMapId = Shader.PropertyToID("_RoughnessMap");
        private static readonly int s_EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int s_OcclusionMapId = Shader.PropertyToID("_OcclusionMap");
        private static readonly int s_ClearCoatMaskId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int s_SmoothnessTextureChannelId = Shader.PropertyToID("_SmoothnessTextureChannel");

        public static GPUDrivenMaterialProxySyncResult SyncFromSourceMaterial(this GPUDrivenMaterialProxy materialProxy)
        {
            if (materialProxy == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "GPUDriven material proxy is null.", null);
            }

            return SyncFromSourceMaterial(
                materialProxy,
                materialProxy.SourceMaterial,
                materialProxy.TextureMode);
        }

        public static GPUDrivenMaterialProxySyncResult SyncFromSourceMaterial(
            this GPUDrivenMaterialProxy materialProxy,
            Material sourceMaterial
        )
        {
            return SyncFromSourceMaterial(
                materialProxy,
                sourceMaterial,
                materialProxy != null
                    ? materialProxy.TextureMode
                    : GPUDrivenMaterialProxyTextureMode.Bindless);
        }

        internal static GPUDrivenMaterialProxySyncResult SyncFromSourceMaterial(
            this GPUDrivenMaterialProxy materialProxy,
            Material sourceMaterial,
            GPUDrivenMaterialProxyTextureMode textureMode,
            bool recordUndo = true,
            bool saveAsset = true
        )
        {
            if (materialProxy == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "GPUDriven material proxy is null.", null);
            }

            if (sourceMaterial == null)
            {
                return new GPUDrivenMaterialProxySyncResult(false, false, "Source Material is not assigned.", null);
            }

            var warnings = new List<string>();
            CollectUnsupportedWarnings(sourceMaterial, warnings);

            int baseTexturePropertyId = sourceMaterial.HasProperty(s_BaseMapId) ? s_BaseMapId : s_MainTexId;
            GPUDrivenMaterialProxySourceTextures sourceTextures =
                ExtractSourceTextures(sourceMaterial, warnings);
            uint initialRevision = materialProxy.Revision;

            if (recordUndo)
            {
                Undo.RecordObject(materialProxy, "Sync GPUDriven Material Proxy");
            }

            materialProxy.SourceMaterial = sourceMaterial;
            // Synchronization updates the source-backed Slab payload. The material
            // model and its Closure topology remain author-owned asset state.
            materialProxy.TextureMode = textureMode;
            if (textureMode == GPUDrivenMaterialProxyTextureMode.Bindless)
            {
                materialProxy.BaseMap = sourceTextures.BaseMap;
                materialProxy.BumpMap = sourceTextures.BumpMap;
                materialProxy.MaskMap = sourceTextures.MaskMap;
            }
            materialProxy.BaseColor = GetColor(sourceMaterial, s_BaseColorId, sourceMaterial.HasProperty(s_ColorId) ? sourceMaterial.GetColor(s_ColorId) : Color.white);
            materialProxy.TextureTilingOffset = GetTilingOffset(sourceMaterial, baseTexturePropertyId);
            materialProxy.BumpScale = GetFloat(sourceMaterial, s_BumpScaleId, 1.0f);
            materialProxy.MaskMode = sourceTextures.MaskMode;
            materialProxy.Metallic = GetFloat(sourceMaterial, s_MetallicId, 0.0f);
            materialProxy.Roughness = 1.0f - Mathf.Clamp01(GetFloat(sourceMaterial, s_SmoothnessId, 0.5f));
            materialProxy.MetallicRemap = GetRemap(
                sourceMaterial,
                s_MetallicRemapMinId,
                s_MetallicRemapMaxId);
            materialProxy.SmoothnessRemap = GetRemap(
                sourceMaterial,
                s_SmoothnessRemapMinId,
                s_SmoothnessRemapMaxId);
            materialProxy.AmbientOcclusionRemap = GetRemap(
                sourceMaterial,
                s_AORemapMinId,
                s_AORemapMaxId);
            materialProxy.EmissionColor = GetColor(sourceMaterial, s_EmissionColorId, Color.black);
            materialProxy.AlphaClip = IsAlphaClipEnabled(sourceMaterial);
            materialProxy.Cutoff = GetFloat(sourceMaterial, s_CutoffId, 0.5f);
            materialProxy.CullMode = (CullMode) Mathf.RoundToInt(GetFloat(sourceMaterial, s_CullId, (float) CullMode.Back));
            materialProxy.DisableLighting = false;

            bool changed = materialProxy.Revision != initialRevision;
            if (changed)
            {
                EditorUtility.SetDirty(materialProxy);
                if (saveAsset)
                {
                    AssetDatabase.SaveAssetIfDirty(materialProxy);
                }
            }

            return new GPUDrivenMaterialProxySyncResult(
                true,
                changed,
                string.Empty,
                warnings.ToArray()
            );
        }

        internal static GPUDrivenMaterialProxySourceTextures ExtractSourceTextures(
            Material sourceMaterial,
            List<string> warnings = null)
        {
            if (sourceMaterial == null)
            {
                return default;
            }

            int baseTexturePropertyId = sourceMaterial.HasProperty(s_BaseMapId) ? s_BaseMapId : s_MainTexId;
            Texture2D baseMap = GetTexture2D(sourceMaterial, baseTexturePropertyId, "_BaseMap", warnings);
            Texture2D bumpMap = GetTexture2D(sourceMaterial, s_BumpMapId, "_BumpMap", warnings);
            Texture2D rmoMap = GetTexture2D(
                sourceMaterial,
                s_RMOMapId,
                "_RMOMap",
                warnings);
            Texture2D metallicMap = GetTexture2D(
                sourceMaterial,
                s_MetallicGlossMapId,
                "_MetallicGlossMap",
                warnings);
            Texture2D roughnessMap = GetTexture2D(
                sourceMaterial,
                s_RoughnessMapId,
                "_RoughnessMap",
                warnings);
            Texture2D maskMap = rmoMap != null
                ? rmoMap
                : metallicMap != null
                    ? metallicMap
                    : roughnessMap;
            GPUDrivenMaterialMaskMode maskMode = rmoMap != null
                ? GPUDrivenMaterialMaskMode.RoughnessMetallicOcclusion
                : metallicMap != null
                    ? GPUDrivenMaterialMaskMode.MetallicSmoothness
                    : roughnessMap != null
                        ? GPUDrivenMaterialMaskMode.Roughness
                        : GPUDrivenMaterialMaskMode.None;
            return new GPUDrivenMaterialProxySourceTextures(baseMap, bumpMap, maskMap, maskMode);
        }

        public static string[] CollectUnsupportedWarnings(this Material sourceMaterial)
        {
            var warnings = new List<string>();
            CollectUnsupportedWarnings(sourceMaterial, warnings);
            return warnings.ToArray();
        }

        private static void CollectUnsupportedWarnings(Material sourceMaterial, List<string> warnings)
        {
            if (sourceMaterial == null || warnings == null)
            {
                return;
            }

            if (sourceMaterial.shader == null || sourceMaterial.shader.name != StandardLitShaderName)
            {
                warnings.Add(
                    $"Source shader '{sourceMaterial.shader?.name ?? "<null>"}' is outside the V1 GPUDriven StandardLit sync target; values are synchronized best-effort."
                );
            }

            if (HasTexture(sourceMaterial, s_OpacityMapId))
            {
                warnings.Add("_OpacityMap is not consumed by the V1 GPUDriven StandardLit path.");
            }

            if (HasTexture(sourceMaterial, s_MetallicGlossMapId)
                && HasTexture(sourceMaterial, s_RoughnessMapId))
            {
                warnings.Add("_RoughnessMap is ignored because _MetallicGlossMap already occupies the GPUDriven mask layer.");
            }

            if (HasTexture(sourceMaterial, s_EmissionMapId))
            {
                warnings.Add("_EmissionMap is not consumed by the V1 GPUDriven StandardLit path.");
            }

            if (HasTexture(sourceMaterial, s_OcclusionMapId))
            {
                warnings.Add("_OcclusionMap is not consumed by the V1 GPUDriven StandardLit path.");
            }

            if (Mathf.Abs(GetFloat(sourceMaterial, s_ClearCoatMaskId, 0.0f)) > EnabledThreshold)
            {
                warnings.Add("_ClearCoatMask is not consumed by the V1 GPUDriven StandardLit path.");
            }

            if (GetFloat(sourceMaterial, s_SmoothnessTextureChannelId, 0.0f) > 0.5f)
            {
                warnings.Add("_SmoothnessTextureChannel = Albedo Alpha is not consumed by the V1 GPUDriven StandardLit path.");
            }
        }

        private static Texture2D GetTexture2D(
            Material sourceMaterial,
            int propertyId,
            string propertyName,
            List<string> warnings
        )
        {
            if (!HasTexture(sourceMaterial, propertyId))
            {
                return null;
            }

            Texture texture = sourceMaterial.GetTexture(propertyId);
            if (texture is Texture2D texture2D)
            {
                return texture2D;
            }

            warnings?.Add($"{propertyName} uses non-Texture2D asset '{texture.name}', which is skipped by the V1 GPUDriven StandardLit path.");
            return null;
        }

        private static bool HasTexture(Material sourceMaterial, int propertyId)
        {
            return sourceMaterial != null && sourceMaterial.HasProperty(propertyId) && sourceMaterial.GetTexture(propertyId) != null;
        }

        private static Color GetColor(Material sourceMaterial, int propertyId, Color fallback)
        {
            return sourceMaterial != null && sourceMaterial.HasProperty(propertyId)
                ? sourceMaterial.GetColor(propertyId)
                : fallback;
        }

        private static float GetFloat(Material sourceMaterial, int propertyId, float fallback)
        {
            return sourceMaterial != null && sourceMaterial.HasProperty(propertyId)
                ? sourceMaterial.GetFloat(propertyId)
                : fallback;
        }

        private static Vector2 GetRemap(Material sourceMaterial, int minPropertyId, int maxPropertyId)
        {
            return new Vector2(
                GetFloat(sourceMaterial, minPropertyId, 0.0f),
                GetFloat(sourceMaterial, maxPropertyId, 1.0f));
        }

        private static Vector4 GetTilingOffset(Material sourceMaterial, int baseTexturePropertyId)
        {
            if (sourceMaterial == null || !sourceMaterial.HasProperty(baseTexturePropertyId))
            {
                return new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
            }

            Vector2 scale = sourceMaterial.GetTextureScale(baseTexturePropertyId);
            Vector2 offset = sourceMaterial.GetTextureOffset(baseTexturePropertyId);
            return new Vector4(scale.x, scale.y, offset.x, offset.y);
        }

        private static bool IsAlphaClipEnabled(Material sourceMaterial)
        {
            return sourceMaterial != null
                   && ((sourceMaterial.HasProperty(s_AlphaClipId) && sourceMaterial.GetFloat(s_AlphaClipId) > 0.5f)
                       || sourceMaterial.IsKeywordEnabled("_ALPHATEST_ON"));
        }
    }
}
