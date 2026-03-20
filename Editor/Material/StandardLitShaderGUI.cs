using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor
{
    public sealed class StandardLitShaderGUI : LWGUI.LWGUI
    {
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
    }

    internal static class StandardLitMaterialUtility
    {
        internal const float MetallicWorkflow = 1.0f;
        internal const float OpaqueSurface = 0.0f;
        private const float AlphaClipThreshold = 0.5f;
        private const float EnabledThreshold = 0.001f;

        private const string AlphaTestKeyword = "_ALPHATEST_ON";
        private const string OpacityMapKeyword = "_OPACITYMAP";
        private const string NormalMapKeyword = "_NORMALMAP";
        private const string MetallicGlossMapKeyword = "_METALLICSPECGLOSSMAP";
        private const string RoughnessMapKeyword = "_ROUGHNESSMAP";
        private const string OcclusionMapKeyword = "_OCCLUSIONMAP";
        private const string EmissionKeyword = "_EMISSION";
        private const string ClearCoatKeyword = "_CLEARCOAT";
        private const string SmoothnessFromAlbedoKeyword = "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A";
        private const string ReceiveShadowsOffKeyword = "_RECEIVE_SHADOWS_OFF";
        private const string SurfaceTypeTransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";
        private const string SpecularSetupKeyword = "_SPECULAR_SETUP";

        internal static void SetupMaterial(Material material, Shader oldShader, bool logWarnings)
        {
            if (material == null)
            {
                return;
            }

            MigrateLegacyValues(material, oldShader);
            ApplyUnsupportedFallbacks(material, logWarnings);
            SyncLegacyAliases(material);
            SyncOpaqueState(material);
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

            if (material.HasProperty("_Surface") && Mathf.Abs(material.GetFloat("_Surface") - OpaqueSurface) > EnabledThreshold)
            {
                Warn(material, logWarnings, "Transparent surface type is not supported yet. Falling back to Opaque.");
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

        private static void SyncOpaqueState(Material material)
        {
            SetFloat(material, "_Surface", OpaqueSurface);
            SetFloat(material, "_Blend", 0.0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(material, "_DstBlendAlpha", (float)BlendMode.Zero);
            SetFloat(material, "_ZWrite", 1.0f);
        }

        private static void SyncKeywords(Material material)
        {
            CoreUtils.SetKeyword(material, AlphaTestKeyword, GetFloat(material, "_AlphaClip") > AlphaClipThreshold);
            CoreUtils.SetKeyword(material, OpacityMapKeyword, material.GetTexture("_OpacityMap") != null);
            CoreUtils.SetKeyword(material, NormalMapKeyword, material.GetTexture("_BumpMap") != null);
            CoreUtils.SetKeyword(material, MetallicGlossMapKeyword, material.GetTexture("_MetallicGlossMap") != null);
            CoreUtils.SetKeyword(material, RoughnessMapKeyword, material.GetTexture("_RoughnessMap") != null);
            CoreUtils.SetKeyword(material, OcclusionMapKeyword, material.GetTexture("_OcclusionMap") != null);
            CoreUtils.SetKeyword(material, EmissionKeyword, HasEmission(material));
            CoreUtils.SetKeyword(material, ClearCoatKeyword, GetFloat(material, "_ClearCoatMask") > EnabledThreshold);
            CoreUtils.SetKeyword(material, SmoothnessFromAlbedoKeyword, GetFloat(material, "_SmoothnessTextureChannel") > AlphaClipThreshold);
            CoreUtils.SetKeyword(material, ReceiveShadowsOffKeyword, GetFloat(material, "_ReceiveShadows") <= AlphaClipThreshold);
            CoreUtils.SetKeyword(material, SurfaceTypeTransparentKeyword, false);
            CoreUtils.SetKeyword(material, SpecularSetupKeyword, false);
        }

        private static void SyncRenderQueue(Material material)
        {
            bool alphaClip = GetFloat(material, "_AlphaClip") > AlphaClipThreshold;
            int queueOffset = Mathf.RoundToInt(GetFloat(material, "_QueueOffset"));
            int baseQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            material.renderQueue = baseQueue + queueOffset;
            material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
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
