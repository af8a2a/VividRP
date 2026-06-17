using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor
{
    public sealed class UnlitShaderGUI : LWGUI.LWGUI
    {
        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            UnlitMaterialUtility.SetupMaterial(material, oldShader, true);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            UnlitMaterialUtility.SetupMaterial(material, null, true);
        }
    }

    internal static class UnlitMaterialUtility
    {
        internal const string UnlitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/Unlit/Unlit.shader";
        internal const string UnlitShaderName = "VividRP/Material/Unlit";
        internal const float OpaqueSurface = 0.0f;
        internal const float TransparentSurface = 1.0f;

        private const float EnabledThreshold = 0.001f;
        private const string AlphaTestKeyword = "_ALPHATEST_ON";
        private const string EmissiveColorMapKeyword = "_EMISSIVE_COLOR_MAP";
        private const string SurfaceTypeTransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";

        internal static Shader GetUnlitShader()
        {
            Shader shader = Shader.Find(UnlitShaderName);
            if (shader != null)
                return shader;

            return AssetDatabase.LoadAssetAtPath<Shader>(UnlitShaderAssetPath);
        }

        internal static void SetupMaterialFinalizer(Material material)
        {
            SetupMaterial(material, null, false);
        }

        internal static void SetupMaterial(Material material, Shader oldShader, bool logWarnings)
        {
            if (material == null)
                return;

            MigrateLegacyValues(material, oldShader);
            SyncEmissiveIntensity(material);
            SyncLegacyAliases(material);
            SyncKeywords(material);
            SyncRenderState(material);
            SyncGlobalIlluminationFlags(material);
            material.enableInstancing = true;
        }

        private static void MigrateLegacyValues(Material material, Shader oldShader)
        {
            if (oldShader == null)
                return;

            CopyTextureIfDestinationEmpty(material, "_MainTex", "_UnlitColorMap");
            CopyTextureIfDestinationEmpty(material, "_BaseMap", "_UnlitColorMap");
            CopyColorIfDestinationDefault(material, "_Color", "_UnlitColor", Color.white);
            CopyColorIfDestinationDefault(material, "_BaseColor", "_UnlitColor", Color.white);

            if (material.HasProperty("_Cutoff") && material.HasProperty("_AlphaCutoff"))
                material.SetFloat("_AlphaCutoff", material.GetFloat("_Cutoff"));
        }

        private static void SyncEmissiveIntensity(Material material)
        {
            if (GetFloat(material, "_UseEmissiveIntensity") <= EnabledThreshold
                || !material.HasProperty("_EmissiveColor")
                || !material.HasProperty("_EmissiveColorLDR"))
            {
                return;
            }

            float intensity = Mathf.Max(0.0f, GetFloat(material, "_EmissiveIntensity", 1.0f));
            material.SetColor("_EmissiveColor", material.GetColor("_EmissiveColorLDR") * intensity);
        }

        private static void SyncLegacyAliases(Material material)
        {
            CopyTexture(material, "_UnlitColorMap", "_MainTex");
            CopyTexture(material, "_UnlitColorMap", "_BaseMap");
            CopyColor(material, "_UnlitColor", "_Color");
            CopyColor(material, "_UnlitColor", "_BaseColor");
            CopyColor(material, "_EmissiveColor", "_EmissionColor");

            if (material.HasProperty("_AlphaCutoff") && material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", material.GetFloat("_AlphaCutoff"));
        }

        private static void SyncKeywords(Material material)
        {
            CoreUtils.SetKeyword(material, AlphaTestKeyword, GetFloat(material, "_AlphaCutoffEnable") > EnabledThreshold);
            CoreUtils.SetKeyword(material, EmissiveColorMapKeyword, material.HasProperty("_EmissiveColorMap") && material.GetTexture("_EmissiveColorMap") != null);
            CoreUtils.SetKeyword(material, SurfaceTypeTransparentKeyword, IsTransparent(material));
        }

        private static void SyncRenderState(Material material)
        {
            bool transparent = IsTransparent(material);
            bool alphaClip = GetFloat(material, "_AlphaCutoffEnable") > EnabledThreshold;
            int queueOffset = Mathf.RoundToInt(GetFloat(material, "_QueueOffset") + GetFloat(material, "_TransparentSortPriority"));

            SetFloat(material, "_CullMode", ResolveCullMode(material));

            if (transparent)
            {
                ApplyTransparentBlendState(material);
                SetFloat(material, "_ZWrite", GetFloat(material, "_TransparentZWrite") > EnabledThreshold ? 1.0f : 0.0f);
                material.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                material.SetOverrideTag("RenderType", "Transparent");
                return;
            }

            SetBlendState(material, BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero);
            SetFloat(material, "_ZWrite", 1.0f);
            material.renderQueue = (alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry) + queueOffset;
            material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
        }

        private static void ApplyTransparentBlendState(Material material)
        {
            int blendMode = Mathf.RoundToInt(GetFloat(material, "_BlendMode"));
            switch (blendMode)
            {
                case 1:
                    SetBlendState(material, BlendMode.One, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha);
                    break;
                case 2:
                    SetBlendState(material, BlendMode.SrcAlpha, BlendMode.One, BlendMode.One, BlendMode.One);
                    break;
                case 3:
                    SetBlendState(material, BlendMode.DstColor, BlendMode.Zero, BlendMode.One, BlendMode.Zero);
                    break;
                default:
                    SetBlendState(material, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, BlendMode.One, BlendMode.OneMinusSrcAlpha);
                    break;
            }
        }

        private static void SyncGlobalIlluminationFlags(Material material)
        {
            bool hasEmission = HasEmission(material);
            if (hasEmission)
                material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            else
                material.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static bool IsTransparent(Material material)
        {
            return GetFloat(material, "_SurfaceType") >= TransparentSurface - EnabledThreshold;
        }

        private static float ResolveCullMode(Material material)
        {
            if (GetFloat(material, "_DoubleSidedEnable") > EnabledThreshold)
                return (float)CullMode.Off;

            return GetFloat(material, "_CullMode", (float)CullMode.Back);
        }

        private static bool HasEmission(Material material)
        {
            bool hasEmissiveMap = material.HasProperty("_EmissiveColorMap") && material.GetTexture("_EmissiveColorMap") != null;
            if (hasEmissiveMap)
                return true;

            if (!material.HasProperty("_EmissiveColor"))
                return false;

            return material.GetColor("_EmissiveColor").maxColorComponent > EnabledThreshold;
        }

        private static void SetBlendState(
            Material material,
            BlendMode srcBlend,
            BlendMode dstBlend,
            BlendMode alphaSrcBlend,
            BlendMode alphaDstBlend)
        {
            SetFloat(material, "_SrcBlend", (float)srcBlend);
            SetFloat(material, "_DstBlend", (float)dstBlend);
            SetFloat(material, "_AlphaSrcBlend", (float)alphaSrcBlend);
            SetFloat(material, "_AlphaDstBlend", (float)alphaDstBlend);
        }

        private static void CopyTextureIfDestinationEmpty(Material material, string sourcePropertyName, string destinationPropertyName)
        {
            if (!material.HasProperty(sourcePropertyName)
                || !material.HasProperty(destinationPropertyName)
                || material.GetTexture(destinationPropertyName) != null)
            {
                return;
            }

            CopyTexture(material, sourcePropertyName, destinationPropertyName);
        }

        private static void CopyTexture(Material material, string sourcePropertyName, string destinationPropertyName)
        {
            if (!material.HasProperty(sourcePropertyName) || !material.HasProperty(destinationPropertyName))
                return;

            Texture texture = material.GetTexture(sourcePropertyName);
            material.SetTexture(destinationPropertyName, texture);
            material.SetTextureScale(destinationPropertyName, material.GetTextureScale(sourcePropertyName));
            material.SetTextureOffset(destinationPropertyName, material.GetTextureOffset(sourcePropertyName));
        }

        private static void CopyColorIfDestinationDefault(Material material, string sourcePropertyName, string destinationPropertyName, Color defaultValue)
        {
            if (!material.HasProperty(sourcePropertyName)
                || !material.HasProperty(destinationPropertyName)
                || material.GetColor(destinationPropertyName) != defaultValue)
            {
                return;
            }

            CopyColor(material, sourcePropertyName, destinationPropertyName);
        }

        private static void CopyColor(Material material, string sourcePropertyName, string destinationPropertyName)
        {
            if (material.HasProperty(sourcePropertyName) && material.HasProperty(destinationPropertyName))
                material.SetColor(destinationPropertyName, material.GetColor(sourcePropertyName));
        }

        private static float GetFloat(Material material, string propertyName, float fallback = 0.0f)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }
    }
}
