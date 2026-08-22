using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    public sealed class StandardLitShaderGUI : LWGUI.LWGUI
    {
        protected override bool ShowLogo => false;

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            StandardLitMaterialUtility.SetupMaterial(material, oldShader, true);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            StandardLitMaterialUtility.SetupMaterial(material, null, true);
            GPUDriven.GPUDrivenMaterialProxyAutoSyncService.QueueMaterial(material);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);
            DrawRMOConversion(materialEditor);
        }

        internal static void DrawRMOConversion(MaterialEditor materialEditor)
        {
            if (materialEditor == null || materialEditor.targets == null)
            {
                return;
            }

            Material material = materialEditor.targets.Length == 1
                ? materialEditor.target as Material
                : null;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("RMO Conversion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Packs the material's legacy Roughness, Metallic, and AO inputs into an RMO texture.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(
                material == null || !StandardLitRMOTexturePacker.CanPackMaterial(material)))
            {
                if (GUILayout.Button("Convert Legacy Maps to RMO"))
                {
                    ConvertLegacyMapsToRMO(materialEditor, material);
                }
            }
        }

        private static void ConvertLegacyMapsToRMO(MaterialEditor materialEditor, Material material)
        {
            string materialPath = AssetDatabase.GetAssetPath(material);
            string directory = materialPath.StartsWith("Assets/")
                ? Path.GetDirectoryName(materialPath)?.Replace('\\', '/')
                : "Assets";
            string fileName = SanitizeFileName(material.name) + "_RMO";
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Save Material RMO Texture",
                fileName,
                "png",
                "Choose where to save the packed RMO texture.",
                directory);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (!StandardLitRMOTexturePacker.TryPackMaterialToAsset(
                assetPath,
                material,
                out Texture2D packedTexture,
                out string errorMessage))
            {
                EditorUtility.DisplayDialog("RMO Conversion", errorMessage, "OK");
                return;
            }

            Undo.RecordObject(material, "Convert Material Maps to RMO");
            material.SetTexture("_RMOMap", packedTexture);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetTexture("_RoughnessMap", null);
            material.SetTexture("_OcclusionMap", null);
            material.SetFloat("_SmoothnessTextureChannel", 0.0f);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
            EditorUtility.SetDirty(material);
            if (EditorUtility.IsPersistent(material))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            materialEditor.PropertiesChanged();
            EditorGUIUtility.PingObject(packedTexture);
        }

        private static string SanitizeFileName(string value)
        {
            string sanitized = string.IsNullOrWhiteSpace(value) ? "Material" : value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized;
        }
    }

    public sealed class StandardLayeredLitShaderGUI : LWGUI.LWGUI
    {
        internal const string VirtualTextureAssetGuidTag = "VividVirtualTextureAssetGuid";

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            StandardLitMaterialUtility.SetupMaterial(material, oldShader, true);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            StandardLitMaterialUtility.SetupMaterial(material, null, true);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);
            DrawVirtualTextureBinding(materialEditor);
        }

        private static void DrawVirtualTextureBinding(MaterialEditor materialEditor)
        {
            if (materialEditor == null || materialEditor.targets == null || materialEditor.targets.Length == 0)
                return;

            Material primaryMaterial = materialEditor.target as Material;
            if (primaryMaterial == null)
                return;

            bool mixedValue = HasMixedVirtualTextureAsset(materialEditor.targets, primaryMaterial);
            VividVirtualTextureAsset currentAsset = LoadVirtualTextureAsset(primaryMaterial);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Virtual Texture", EditorStyles.boldLabel);

            EditorGUI.showMixedValue = mixedValue;
            EditorGUI.BeginChangeCheck();
            var nextAsset = (VividVirtualTextureAsset)EditorGUILayout.ObjectField(
                "SVT Asset",
                currentAsset,
                typeof(VividVirtualTextureAsset),
                false);
            EditorGUI.showMixedValue = false;

            if (!EditorGUI.EndChangeCheck())
                return;

            string nextGuid = GetAssetGuid(nextAsset);
            foreach (Object target in materialEditor.targets)
            {
                if (target is not Material material)
                    continue;

                Undo.RecordObject(material, "Set Virtual Texture Asset");
                material.SetOverrideTag(VirtualTextureAssetGuidTag, nextGuid);
                EditorUtility.SetDirty(material);
            }
        }

        private static bool HasMixedVirtualTextureAsset(Object[] targets, Material primaryMaterial)
        {
            string primaryGuid = GetVirtualTextureAssetGuid(primaryMaterial);
            foreach (Object target in targets)
            {
                if (target is Material material
                    && !string.Equals(GetVirtualTextureAssetGuid(material), primaryGuid, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static VividVirtualTextureAsset LoadVirtualTextureAsset(Material material)
        {
            string guid = GetVirtualTextureAssetGuid(material);
            if (string.IsNullOrEmpty(guid))
                return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<VividVirtualTextureAsset>(path);
        }

        private static string GetAssetGuid(VividVirtualTextureAsset asset)
        {
            if (asset == null)
                return string.Empty;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static string GetVirtualTextureAssetGuid(Material material)
        {
            return material != null
                ? material.GetTag(VirtualTextureAssetGuidTag, false, string.Empty)
                : string.Empty;
        }
    }

    internal static class StandardLitMaterialUtility
    {
        internal const float MetallicWorkflow = 1.0f;
        internal const float OpaqueSurface = 0.0f;
        internal const float TransparentSurface = 1.0f;

        /// <summary>
        /// MaterialFinalizer delegate for use with MaterialUpgrader.
        /// Calls SetupMaterial to sync keywords, render queue, and other state after conversion.
        /// </summary>
        internal static void SetupMaterialFinalizer(Material material)
        {
            SetupMaterial(material, null, false);
        }
        private const float AlphaClipThreshold = 0.5f;
        private const float EnabledThreshold = 0.001f;

        private const string AlphaTestKeyword = "_ALPHATEST_ON";
        private const string OpacityMapKeyword = "_OPACITYMAP";
        private const string TransmissionMapKeyword = "_TRANSMISSIONMAP";
        private const string NormalMapKeyword = "_NORMALMAP";
        private const string RMOMapKeyword = "_RMOMAP";
        private const string MetallicGlossMapKeyword = "_METALLICSPECGLOSSMAP";
        private const string RoughnessMapKeyword = "_ROUGHNESSMAP";
        private const string OcclusionMapKeyword = "_OCCLUSIONMAP";
        private const string EmissionKeyword = "_EMISSION";
        private const string ClearCoatKeyword = "_CLEARCOAT";
        private const string SmoothnessFromAlbedoKeyword = "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A";
        private const string ReceiveShadowsOffKeyword = "_RECEIVE_SHADOWS_OFF";
        private const string SurfaceTypeTransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";
        private const string SpecularSetupKeyword = "_SPECULAR_SETUP";
        private const string VirtualTextureBaseColorKeyword = "_VIRTUAL_TEXTURE_BASE_COLOR";
        private const string StandardLitShaderName =
            "VividRP/Material/StandardLit";

        internal static void SetupMaterial(Material material, Shader oldShader, bool logWarnings)
        {
            if (material == null)
            {
                return;
            }

            MigrateLegacyValues(material, oldShader);
            ApplyUnsupportedFallbacks(material, logWarnings);
            SyncLegacyAliases(material);
            SyncSurfaceState(material);
            SyncKeywords(material);
            SyncRenderQueue(material);
            SyncGlobalIlluminationFlags(material);
        }

        private static void MigrateLegacyValues(Material material, Shader oldShader)
        {
            if (oldShader == null)
            {
                return;
            }

            if (material.HasProperty("_MainTex") && material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == null)
            {
                Texture mainTexture = material.GetTexture("_MainTex");
                if (mainTexture != null)
                {
                    material.SetTexture("_BaseMap", mainTexture);
                    material.SetTextureScale("_BaseMap", material.GetTextureScale("_MainTex"));
                    material.SetTextureOffset("_BaseMap", material.GetTextureOffset("_MainTex"));
                }
            }

            if (material.HasProperty("_Color") && material.HasProperty("_BaseColor"))
            {
                Color baseColor = material.GetColor("_BaseColor");
                if (baseColor == Color.white)
                {
                    material.SetColor("_BaseColor", material.GetColor("_Color"));
                }
            }
        }

        private static void ApplyUnsupportedFallbacks(Material material, bool logWarnings)
        {
            if (material.HasProperty("_WorkflowMode") && Mathf.Abs(material.GetFloat("_WorkflowMode") - MetallicWorkflow) > EnabledThreshold)
            {
                Warn(material, logWarnings, "Specular workflow is not supported yet. Falling back to Metallic workflow.");
                material.SetFloat("_WorkflowMode", MetallicWorkflow);
            }

            if (IsTransparent(material)
                && !SupportsPathTracingTransparency(material))
            {
                Warn(
                    material,
                    logWarnings,
                    "Transparent surface type is currently supported only by StandardLit reference path tracing. Falling back to Opaque.");
                material.SetFloat("_Surface", OpaqueSurface);
            }
        }

        private static void SyncLegacyAliases(Material material)
        {
            if (material.HasProperty("_BaseMap") && material.HasProperty("_MainTex"))
            {
                Texture baseMap = material.GetTexture("_BaseMap");
                material.SetTexture("_MainTex", baseMap);
                material.SetTextureScale("_MainTex", material.GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_MainTex", material.GetTextureOffset("_BaseMap"));
            }

            if (material.HasProperty("_BaseColor") && material.HasProperty("_Color"))
            {
                material.SetColor("_Color", material.GetColor("_BaseColor"));
            }
        }

        private static void SyncSurfaceState(Material material)
        {
            bool transparent = IsTransparent(material);
            SetFloat(
                material,
                "_Surface",
                transparent ? TransparentSurface : OpaqueSurface);
            SetFloat(material, "_Blend", 0.0f);
            SetFloat(
                material,
                "_SrcBlend",
                (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            SetFloat(
                material,
                "_DstBlend",
                (float)(transparent
                    ? BlendMode.OneMinusSrcAlpha
                    : BlendMode.Zero));
            SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(
                material,
                "_DstBlendAlpha",
                (float)(transparent
                    ? BlendMode.OneMinusSrcAlpha
                    : BlendMode.Zero));
            SetFloat(material, "_ZWrite", transparent ? 0.0f : 1.0f);
        }

        private static void SyncKeywords(Material material)
        {
            bool hasRMOMap = material.HasProperty("_RMOMap") && material.GetTexture("_RMOMap") != null;
            CoreUtils.SetKeyword(material, AlphaTestKeyword, GetFloat(material, "_AlphaClip") > AlphaClipThreshold);
            CoreUtils.SetKeyword(material, OpacityMapKeyword, material.GetTexture("_OpacityMap") != null);
            CoreUtils.SetKeyword(material, TransmissionMapKeyword, material.GetTexture("_TransmissionMap") != null);
            CoreUtils.SetKeyword(material, NormalMapKeyword, material.GetTexture("_BumpMap") != null);
            CoreUtils.SetKeyword(material, RMOMapKeyword, hasRMOMap);
            CoreUtils.SetKeyword(material, MetallicGlossMapKeyword, !hasRMOMap && material.GetTexture("_MetallicGlossMap") != null);
            CoreUtils.SetKeyword(material, RoughnessMapKeyword, !hasRMOMap && material.GetTexture("_RoughnessMap") != null);
            CoreUtils.SetKeyword(material, OcclusionMapKeyword, !hasRMOMap && material.GetTexture("_OcclusionMap") != null);
            CoreUtils.SetKeyword(material, EmissionKeyword, HasEmission(material));
            CoreUtils.SetKeyword(material, ClearCoatKeyword, GetFloat(material, "_ClearCoatMask") > EnabledThreshold);
            CoreUtils.SetKeyword(material, SmoothnessFromAlbedoKeyword, GetFloat(material, "_SmoothnessTextureChannel") > AlphaClipThreshold);
            CoreUtils.SetKeyword(material, ReceiveShadowsOffKeyword, GetFloat(material, "_ReceiveShadows") <= AlphaClipThreshold);
            CoreUtils.SetKeyword(
                material,
                SurfaceTypeTransparentKeyword,
                IsTransparent(material));
            CoreUtils.SetKeyword(material, SpecularSetupKeyword, false);
            CoreUtils.SetKeyword(material, VirtualTextureBaseColorKeyword, GetFloat(material, "_UseVirtualTextureBaseColor") > EnabledThreshold);
        }

        private static void SyncRenderQueue(Material material)
        {
            bool transparent = IsTransparent(material);
            bool alphaClip = GetFloat(material, "_AlphaClip") > AlphaClipThreshold;
            int queueOffset = Mathf.RoundToInt(GetFloat(material, "_QueueOffset"));
            int baseQueue = transparent
                ? (int)RenderQueue.Transparent
                : alphaClip
                    ? (int)RenderQueue.AlphaTest
                    : (int)RenderQueue.Geometry;

            material.renderQueue = baseQueue + queueOffset;
            material.SetOverrideTag(
                "RenderType",
                transparent
                    ? "Transparent"
                    : alphaClip
                        ? "TransparentCutout"
                        : "Opaque");
        }

        private static bool IsTransparent(Material material)
        {
            return GetFloat(material, "_Surface")
                > (OpaqueSurface + TransparentSurface) * 0.5f;
        }

        private static bool SupportsPathTracingTransparency(Material material)
        {
            return material != null
                && material.shader != null
                && material.shader.name == StandardLitShaderName;
        }

        private static void SyncGlobalIlluminationFlags(Material material)
        {
            bool hasEmission = HasEmission(material);
            if (hasEmission)
            {
                material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            else
            {
                material.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        private static bool HasEmission(Material material)
        {
            if (!material.HasProperty("_EmissionColor"))
            {
                return false;
            }

            Color emissionColor = material.GetColor("_EmissionColor");
            return emissionColor.maxColorComponent > EnabledThreshold;
        }

        private static float GetFloat(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : 0.0f;
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void Warn(Material material, bool enabled, string message)
        {
            if (!enabled)
            {
                return;
            }

            Debug.LogWarning($"VividRP StandardLit: {message} Material: '{material.name}'.", material);
        }
    }
}
