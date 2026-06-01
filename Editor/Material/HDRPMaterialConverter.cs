using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Converter;
using UnityEngine;

namespace VividRP.Editor
{
    [Serializable]
    [PipelineConverter("High Definition Render Pipeline", "VividRP")]
    internal sealed class HDRPMaterialConverter : RenderPipelineConverterMaterialUpgrader
    {
        private const string HDRPLitShaderName = "HDRP/Lit";
        private const string LegacyHDRPLitShaderName = "HDRenderPipeline/Lit";
        private const string HDRPLitTessellationShaderName = "HDRP/LitTessellation";
        private const string LegacyHDRPLitTessellationShaderName = "HDRenderPipeline/LitTessellation";
        private const string HDRPUnlitShaderName = "HDRP/Unlit";
        private const string LegacyHDRPUnlitShaderName = "HDRenderPipeline/Unlit";

        protected override List<MaterialUpgrader> upgraders
        {
            get
            {
                var list = new List<MaterialUpgrader>();
                list.Add(CreateHDRPLitUpgrader(HDRPLitShaderName));
                list.Add(CreateHDRPLitUpgrader(LegacyHDRPLitShaderName));
                list.Add(CreateHDRPLitUpgrader(HDRPLitTessellationShaderName));
                list.Add(CreateHDRPLitUpgrader(LegacyHDRPLitTessellationShaderName));
                list.Add(CreateHDRPUnlitUpgrader(HDRPUnlitShaderName));
                list.Add(CreateHDRPUnlitUpgrader(LegacyHDRPUnlitShaderName));
                return list;
            }
        }

        internal static MaterialUpgrader CreateHDRPLitUpgrader(string sourceShaderName)
        {
            var upgrader = new HDRPLitToStandardLitUpgrader();
            upgrader.RenameShader(
                sourceShaderName,
                StandardLitMaterialImportUtility.StandardLitShaderName,
                StandardLitMaterialUtility.SetupMaterialFinalizer);

            // Textures
            upgrader.RenameTexture("_BaseColorMap", "_BaseMap");
            upgrader.RenameTexture("_NormalMap", "_BumpMap");
            upgrader.RenameTexture("_MaskMap", "_MetallicGlossMap");
            upgrader.RenameTexture("_EmissiveColorMap", "_EmissionMap");

            // Colors
            upgrader.RenameColor("_BaseColor", "_BaseColor");
            upgrader.RenameColor("_EmissiveColor", "_EmissionColor");

            // Floats
            upgrader.RenameFloat("_Metallic", "_Metallic");
            upgrader.RenameFloat("_Smoothness", "_Smoothness");
            upgrader.RenameFloat("_NormalScale", "_BumpScale");
            upgrader.RenameFloat("_AlphaCutoff", "_Cutoff");
            upgrader.RenameFloat("_AlphaCutoffEnable", "_AlphaClip");
            upgrader.RenameFloat("_CoatMask", "_ClearCoatMask");
            upgrader.RenameFloat("_CullMode", "_Cull");

            // HDRP Lit is metallic/roughness based; VividRP StandardLit stores smoothness.
            upgrader.SetFloat("_WorkflowMode", StandardLitMaterialUtility.MetallicWorkflow);
            upgrader.SetFloat("_SmoothnessTextureChannel", 0.0f);
            upgrader.SetFloat("_OcclusionStrength", 1.0f);
            upgrader.SetFloat("_ReceiveShadows", 1.0f);

            return upgrader;
        }

        internal static MaterialUpgrader CreateHDRPUnlitUpgrader(string sourceShaderName)
        {
            var upgrader = new HDRPUnlitToUnlitUpgrader();
            upgrader.RenameShader(
                sourceShaderName,
                UnlitMaterialUtility.UnlitShaderName,
                UnlitMaterialUtility.SetupMaterialFinalizer);

            upgrader.RenameTexture("_UnlitColorMap", "_UnlitColorMap");
            upgrader.RenameTexture("_EmissiveColorMap", "_EmissiveColorMap");
            upgrader.RenameTexture("_MainTex", "_MainTex");

            upgrader.RenameColor("_UnlitColor", "_UnlitColor");
            upgrader.RenameColor("_EmissiveColor", "_EmissiveColor");
            upgrader.RenameColor("_EmissiveColorLDR", "_EmissiveColorLDR");
            upgrader.RenameColor("_Color", "_Color");

            upgrader.RenameFloat("_AlphaCutoff", "_AlphaCutoff");
            upgrader.RenameFloat("_AlphaCutoffEnable", "_AlphaCutoffEnable");
            upgrader.RenameFloat("_AlphaRemapMin", "_AlphaRemapMin");
            upgrader.RenameFloat("_AlphaRemapMax", "_AlphaRemapMax");
            upgrader.RenameFloat("_SurfaceType", "_SurfaceType");
            upgrader.RenameFloat("_BlendMode", "_BlendMode");
            upgrader.RenameFloat("_CullMode", "_CullMode");
            upgrader.RenameFloat("_DoubleSidedEnable", "_DoubleSidedEnable");
            upgrader.RenameFloat("_TransparentZWrite", "_TransparentZWrite");
            upgrader.RenameFloat("_TransparentSortPriority", "_TransparentSortPriority");
            upgrader.RenameFloat("_AlbedoAffectEmissive", "_AlbedoAffectEmissive");
            upgrader.RenameFloat("_EmissiveExposureWeight", "_EmissiveExposureWeight");
            upgrader.RenameFloat("_UseEmissiveIntensity", "_UseEmissiveIntensity");
            upgrader.RenameFloat("_EmissiveIntensity", "_EmissiveIntensity");

            return upgrader;
        }
    }

    internal sealed class HDRPLitToStandardLitUpgrader : MaterialUpgrader
    {
        public override void Convert(Material srcMaterial, Material dstMaterial)
        {
            HDRPMaterialPropertySnapshot snapshot = HDRPMaterialPropertySnapshot.Capture(srcMaterial);

            base.Convert(srcMaterial, dstMaterial);

            HDRPMaterialConversionUtility.ApplySnapshotToStandardLit(snapshot, dstMaterial);
        }
    }

    internal sealed class HDRPUnlitToUnlitUpgrader : MaterialUpgrader
    {
        public override void Convert(Material srcMaterial, Material dstMaterial)
        {
            HDRPMaterialPropertySnapshot snapshot = HDRPMaterialPropertySnapshot.Capture(srcMaterial);

            base.Convert(srcMaterial, dstMaterial);

            HDRPMaterialConversionUtility.ApplySnapshotToUnlit(snapshot, dstMaterial);
        }
    }

    internal static class HDRPMaterialConversionUtility
    {
        private const string AssetsMenuPath = "Assets/VividRP/Convert Selected HDRP Materials to VividRP";
        private const string ToolsMenuPath = "Tools/VividRP/Material/Convert Selected HDRP Materials to VividRP";
        private const float EnabledThreshold = 0.001f;

        [MenuItem(AssetsMenuPath, false, 2200)]
        [MenuItem(ToolsMenuPath, false, 2200)]
        private static void ConvertSelectedMaterials()
        {
            List<Material> selectedMaterials = GetSelectedMaterials();
            int convertedCount = 0;
            int skippedCount = 0;

            foreach (Material material in selectedMaterials)
            {
                if (TryConvertMaterial(material, true, true))
                {
                    convertedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(
                "VividRP HDRP Material Converter",
                $"Converted {convertedCount} material(s).\nSkipped {skippedCount} material(s).",
                "OK");
        }

        [MenuItem(AssetsMenuPath, true)]
        [MenuItem(ToolsMenuPath, true)]
        private static bool ValidateConvertSelectedMaterials()
        {
            foreach (Material material in GetSelectedMaterials())
            {
                if (CanConvertMaterial(material))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool CanConvertMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            HDRPMaterialPropertySnapshot snapshot = HDRPMaterialPropertySnapshot.Capture(material);
            return (snapshot.LooksLikeHDRPLit && StandardLitMaterialImportUtility.GetStandardLitShader() != null)
                || (snapshot.LooksLikeHDRPUnlit && UnlitMaterialUtility.GetUnlitShader() != null);
        }

        internal static bool TryConvertMaterial(Material material, bool recordUndo, bool saveAsset)
        {
            if (material == null)
            {
                return false;
            }

            HDRPMaterialPropertySnapshot snapshot = HDRPMaterialPropertySnapshot.Capture(material);
            if (!snapshot.LooksLikeHDRPLit && !snapshot.LooksLikeHDRPUnlit)
            {
                return false;
            }

            if (snapshot.LooksLikeHDRPUnlit)
            {
                Shader unlitShader = UnlitMaterialUtility.GetUnlitShader();
                if (unlitShader == null)
                {
                    return false;
                }

                if (recordUndo)
                {
                    Undo.RecordObject(material, "Convert HDRP Unlit Material to VividRP Unlit");
                }

                material.shader = unlitShader;
                ApplySnapshotToUnlit(snapshot, material);
                EditorUtility.SetDirty(material);

                if (saveAsset)
                {
                    AssetDatabase.SaveAssetIfDirty(material);
                }

                return true;
            }

            Shader standardLitShader = StandardLitMaterialImportUtility.GetStandardLitShader();
            if (standardLitShader == null)
            {
                return false;
            }

            if (recordUndo)
            {
                Undo.RecordObject(material, "Convert HDRP Lit Material to VividRP StandardLit");
            }

            material.shader = standardLitShader;
            ApplySnapshotToStandardLit(snapshot, material);
            EditorUtility.SetDirty(material);

            if (saveAsset)
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            return true;
        }

        internal static void ApplySnapshotToStandardLit(HDRPMaterialPropertySnapshot snapshot, Material material)
        {
            if (material == null)
            {
                return;
            }

            CopyTexture(snapshot, material, "_BaseColorMap", "_BaseMap", false);
            CopyFallbackTexture(snapshot, material, "_MainTex", "_BaseMap");
            CopyTexture(snapshot, material, "_NormalMap", "_BumpMap", false);
            CopyTexture(snapshot, material, "_MaskMap", "_MetallicGlossMap", false);
            CopyTexture(snapshot, material, "_MaskMap", "_OcclusionMap", false);
            CopyTexture(snapshot, material, "_EmissiveColorMap", "_EmissionMap", false);

            CopyColor(snapshot, material, "_BaseColor", "_BaseColor");
            CopyFallbackColor(snapshot, material, "_Color", "_BaseColor");

            bool hasMaskMap = snapshot.TryGetTexture("_MaskMap", out HDRPMaterialTextureProperty maskMap) && maskMap.Texture != null;
            SetFloatIfPresent(material, "_Metallic", hasMaskMap
                ? snapshot.GetFloat("_MetallicRemapMax", snapshot.GetFloat("_Metallic", 0.0f))
                : snapshot.GetFloat("_Metallic", 0.0f));
            SetFloatIfPresent(material, "_Smoothness", hasMaskMap
                ? snapshot.GetFloat("_SmoothnessRemapMax", snapshot.GetFloat("_Smoothness", 0.5f))
                : snapshot.GetFloat("_Smoothness", 0.5f));
            SetFloatIfPresent(material, "_BumpScale", snapshot.GetFloat("_NormalScale", 1.0f));
            SetFloatIfPresent(material, "_Cutoff", snapshot.GetFloat("_AlphaCutoff", snapshot.GetFloat("_Cutoff", 0.5f)));
            SetFloatIfPresent(material, "_AlphaClip", snapshot.GetFloat("_AlphaCutoffEnable", 0.0f) > EnabledThreshold ? 1.0f : 0.0f);
            SetFloatIfPresent(material, "_ClearCoatMask", snapshot.GetFloat("_CoatMask", 0.0f));
            SetFloatIfPresent(material, "_ClearCoatSmoothness", snapshot.GetFloat("_CoatSmoothness", 1.0f));
            SetFloatIfPresent(material, "_Cull", ResolveCullMode(snapshot));
            SetFloatIfPresent(material, "_WorkflowMode", StandardLitMaterialUtility.MetallicWorkflow);
            SetFloatIfPresent(material, "_SmoothnessTextureChannel", 0.0f);
            SetFloatIfPresent(material, "_OcclusionStrength", 1.0f);
            SetFloatIfPresent(material, "_ReceiveShadows", 1.0f);

            Color emissionColor = snapshot.TryGetColor("_EmissiveColor", out Color emissiveColor)
                ? emissiveColor
                : Color.black;
            SetColorIfPresent(material, "_EmissionColor", emissionColor);

            StandardLitMaterialUtility.SetupMaterial(material, null, false);
        }

        internal static void ApplySnapshotToUnlit(HDRPMaterialPropertySnapshot snapshot, Material material)
        {
            if (material == null)
            {
                return;
            }

            CopyTexture(snapshot, material, "_UnlitColorMap", "_UnlitColorMap", false);
            CopyFallbackTexture(snapshot, material, "_MainTex", "_UnlitColorMap");
            CopyTexture(snapshot, material, "_EmissiveColorMap", "_EmissiveColorMap", false);

            CopyColor(snapshot, material, "_UnlitColor", "_UnlitColor");
            CopyFallbackColor(snapshot, material, "_Color", "_UnlitColor");
            CopyColor(snapshot, material, "_EmissiveColor", "_EmissiveColor");
            CopyColor(snapshot, material, "_EmissiveColorLDR", "_EmissiveColorLDR");

            SetFloatIfPresent(material, "_AlphaCutoff", snapshot.GetFloat("_AlphaCutoff", snapshot.GetFloat("_Cutoff", 0.5f)));
            SetFloatIfPresent(material, "_AlphaCutoffEnable", snapshot.GetFloat("_AlphaCutoffEnable", 0.0f) > EnabledThreshold ? 1.0f : 0.0f);
            SetFloatIfPresent(material, "_AlphaRemapMin", snapshot.GetFloat("_AlphaRemapMin", 0.0f));
            SetFloatIfPresent(material, "_AlphaRemapMax", snapshot.GetFloat("_AlphaRemapMax", 1.0f));
            SetFloatIfPresent(material, "_SurfaceType", snapshot.GetFloat("_SurfaceType", UnlitMaterialUtility.OpaqueSurface));
            SetFloatIfPresent(material, "_BlendMode", snapshot.GetFloat("_BlendMode", 0.0f));
            SetFloatIfPresent(material, "_CullMode", ResolveUnlitCullMode(snapshot));
            SetFloatIfPresent(material, "_DoubleSidedEnable", snapshot.GetFloat("_DoubleSidedEnable", 0.0f));
            SetFloatIfPresent(material, "_TransparentZWrite", snapshot.GetFloat("_TransparentZWrite", 0.0f));
            SetFloatIfPresent(material, "_TransparentSortPriority", snapshot.GetFloat("_TransparentSortPriority", 0.0f));
            SetFloatIfPresent(material, "_AlbedoAffectEmissive", snapshot.GetFloat("_AlbedoAffectEmissive", 0.0f));
            SetFloatIfPresent(material, "_EmissiveExposureWeight", snapshot.GetFloat("_EmissiveExposureWeight", 1.0f));
            SetFloatIfPresent(material, "_UseEmissiveIntensity", snapshot.GetFloat("_UseEmissiveIntensity", 0.0f));
            SetFloatIfPresent(material, "_EmissiveIntensity", snapshot.GetFloat("_EmissiveIntensity", 1.0f));

            UnlitMaterialUtility.SetupMaterial(material, null, false);
        }

        private static List<Material> GetSelectedMaterials()
        {
            var materials = new List<Material>();
            var seenMaterials = new HashSet<Material>();

            foreach (Material material in Selection.GetFiltered<Material>(SelectionMode.Assets | SelectionMode.DeepAssets))
            {
                if (material != null && seenMaterials.Add(material))
                {
                    materials.Add(material);
                }
            }

            return materials;
        }

        private static void CopyFallbackTexture(
            HDRPMaterialPropertySnapshot snapshot,
            Material material,
            string sourcePropertyName,
            string destinationPropertyName)
        {
            if (!material.HasProperty(destinationPropertyName) || material.GetTexture(destinationPropertyName) != null)
            {
                return;
            }

            CopyTexture(snapshot, material, sourcePropertyName, destinationPropertyName, true);
        }

        private static void CopyTexture(
            HDRPMaterialPropertySnapshot snapshot,
            Material material,
            string sourcePropertyName,
            string destinationPropertyName,
            bool requireAssignedTexture)
        {
            if (!material.HasProperty(destinationPropertyName)
                || !snapshot.TryGetTexture(sourcePropertyName, out HDRPMaterialTextureProperty textureProperty))
            {
                return;
            }

            if (requireAssignedTexture && textureProperty.Texture == null)
            {
                return;
            }

            material.SetTexture(destinationPropertyName, textureProperty.Texture);
            material.SetTextureScale(destinationPropertyName, textureProperty.Scale);
            material.SetTextureOffset(destinationPropertyName, textureProperty.Offset);
        }

        private static void CopyFallbackColor(
            HDRPMaterialPropertySnapshot snapshot,
            Material material,
            string sourcePropertyName,
            string destinationPropertyName)
        {
            if (!material.HasProperty(destinationPropertyName) || material.GetColor(destinationPropertyName) != Color.white)
            {
                return;
            }

            CopyColor(snapshot, material, sourcePropertyName, destinationPropertyName);
        }

        private static void CopyColor(
            HDRPMaterialPropertySnapshot snapshot,
            Material material,
            string sourcePropertyName,
            string destinationPropertyName)
        {
            if (material.HasProperty(destinationPropertyName) && snapshot.TryGetColor(sourcePropertyName, out Color color))
            {
                material.SetColor(destinationPropertyName, color);
            }
        }

        private static float ResolveCullMode(HDRPMaterialPropertySnapshot snapshot)
        {
            return snapshot.GetFloat("_DoubleSidedEnable", 0.0f) > EnabledThreshold
                ? (float)UnityEngine.Rendering.CullMode.Off
                : snapshot.GetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Back);
        }

        private static float ResolveUnlitCullMode(HDRPMaterialPropertySnapshot snapshot)
        {
            return snapshot.GetFloat("_DoubleSidedEnable", 0.0f) > EnabledThreshold
                ? (float)UnityEngine.Rendering.CullMode.Off
                : snapshot.GetFloat("_CullMode", snapshot.GetFloat("_OpaqueCullMode", (float)UnityEngine.Rendering.CullMode.Back));
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }
    }

    internal sealed class HDRPMaterialPropertySnapshot
    {
        private readonly Dictionary<string, HDRPMaterialTextureProperty> m_Textures =
            new Dictionary<string, HDRPMaterialTextureProperty>(StringComparer.Ordinal);

        private readonly Dictionary<string, float> m_Floats =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private readonly Dictionary<string, Color> m_Colors =
            new Dictionary<string, Color>(StringComparer.Ordinal);

        internal bool LooksLikeHDRPUnlit =>
            m_Textures.ContainsKey("_UnlitColorMap")
            || m_Colors.ContainsKey("_UnlitColor");

        internal bool LooksLikeHDRPLit =>
            !LooksLikeHDRPUnlit &&
            (m_Textures.ContainsKey("_BaseColorMap")
                || m_Textures.ContainsKey("_MaskMap")
                || m_Textures.ContainsKey("_NormalMap")
                || m_Textures.ContainsKey("_EmissiveColorMap")
                || m_Floats.ContainsKey("_MaterialID")
                || m_Floats.ContainsKey("_SurfaceType")
                || m_Colors.ContainsKey("_EmissiveColor"));

        internal static HDRPMaterialPropertySnapshot Capture(Material material)
        {
            var snapshot = new HDRPMaterialPropertySnapshot();
            if (material == null)
            {
                return snapshot;
            }

            var serializedObject = new SerializedObject(material);
            snapshot.CaptureTextures(serializedObject);
            snapshot.CaptureFloats(serializedObject);
            snapshot.CaptureColors(serializedObject);
            return snapshot;
        }

        internal bool TryGetTexture(string propertyName, out HDRPMaterialTextureProperty value)
        {
            return m_Textures.TryGetValue(propertyName, out value);
        }

        internal float GetFloat(string propertyName, float fallback)
        {
            return m_Floats.TryGetValue(propertyName, out float value) ? value : fallback;
        }

        internal bool TryGetColor(string propertyName, out Color value)
        {
            return m_Colors.TryGetValue(propertyName, out value);
        }

        private void CaptureTextures(SerializedObject serializedObject)
        {
            SerializedProperty textureProperties = serializedObject.FindProperty("m_SavedProperties.m_TexEnvs");
            if (textureProperties == null || !textureProperties.isArray)
            {
                return;
            }

            for (int i = 0; i < textureProperties.arraySize; i++)
            {
                SerializedProperty entry = textureProperties.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = entry.FindPropertyRelative("first");
                SerializedProperty textureProperty = entry.FindPropertyRelative("second.m_Texture");
                SerializedProperty scaleProperty = entry.FindPropertyRelative("second.m_Scale");
                SerializedProperty offsetProperty = entry.FindPropertyRelative("second.m_Offset");

                if (nameProperty == null || string.IsNullOrEmpty(nameProperty.stringValue))
                {
                    continue;
                }

                m_Textures[nameProperty.stringValue] = new HDRPMaterialTextureProperty(
                    textureProperty != null ? textureProperty.objectReferenceValue as Texture : null,
                    scaleProperty != null ? scaleProperty.vector2Value : Vector2.one,
                    offsetProperty != null ? offsetProperty.vector2Value : Vector2.zero);
            }
        }

        private void CaptureFloats(SerializedObject serializedObject)
        {
            SerializedProperty floatProperties = serializedObject.FindProperty("m_SavedProperties.m_Floats");
            if (floatProperties == null || !floatProperties.isArray)
            {
                return;
            }

            for (int i = 0; i < floatProperties.arraySize; i++)
            {
                SerializedProperty entry = floatProperties.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = entry.FindPropertyRelative("first");
                SerializedProperty valueProperty = entry.FindPropertyRelative("second");

                if (nameProperty != null && valueProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue))
                {
                    m_Floats[nameProperty.stringValue] = valueProperty.floatValue;
                }
            }
        }

        private void CaptureColors(SerializedObject serializedObject)
        {
            SerializedProperty colorProperties = serializedObject.FindProperty("m_SavedProperties.m_Colors");
            if (colorProperties == null || !colorProperties.isArray)
            {
                return;
            }

            for (int i = 0; i < colorProperties.arraySize; i++)
            {
                SerializedProperty entry = colorProperties.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = entry.FindPropertyRelative("first");
                SerializedProperty valueProperty = entry.FindPropertyRelative("second");

                if (nameProperty != null && valueProperty != null && !string.IsNullOrEmpty(nameProperty.stringValue))
                {
                    m_Colors[nameProperty.stringValue] = valueProperty.colorValue;
                }
            }
        }
    }

    internal readonly struct HDRPMaterialTextureProperty
    {
        internal HDRPMaterialTextureProperty(Texture texture, Vector2 scale, Vector2 offset)
        {
            Texture = texture;
            Scale = scale;
            Offset = offset;
        }

        internal Texture Texture { get; }
        internal Vector2 Scale { get; }
        internal Vector2 Offset { get; }
    }
}
