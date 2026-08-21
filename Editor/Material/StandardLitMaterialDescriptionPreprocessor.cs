using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor
{
    internal interface IImportedMaterialDescription
    {
        bool TryGetFloat(string propertyName, out float value);
        bool TryGetVector(string propertyName, out Vector4 value);
        bool TryGetTexture(string propertyName, out ImportedTextureProperty value);
        bool TryGetString(string propertyName, out string value);
    }

    internal readonly struct ImportedTextureProperty
    {
        internal ImportedTextureProperty(Texture texture, Vector2 offset, Vector2 scale)
        {
            Texture = texture;
            Offset = offset;
            Scale = scale;
        }

        internal Texture Texture { get; }
        internal Vector2 Offset { get; }
        internal Vector2 Scale { get; }
        internal bool IsAssigned => Texture != null;
    }

    internal static class StandardLitMaterialImportUtility
    {
        internal const string StandardLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit/StandardLit.shader";
        internal const string StandardLitShaderName = "VividRP/Material/StandardLit";

        private const string PhysicalMaterialTypeName = "PHYSICAL_MTL";
        private const float MaxPhysicalClassIdA = 4.1222316e+08f;
        private const float MaxPhysicalClassIdB = -5.5903846e+08f;
        private const float MaxSimplifiedPhysicalClassIdA = -8.0431565e+08f;
        private const float MaxSimplifiedPhysicalClassIdB = -1.0994388e+09f;
        private const float MaxSimplifiedGlossinessMode = 2.0f;
        private const float DefaultAlphaCutoff = 0.5f;
        private const string NormalTextureNameKeyword = "normal";

        internal static Shader GetStandardLitShader()
        {
            Shader shader = Shader.Find(StandardLitShaderName);
            if (shader != null)
            {
                return shader;
            }

            return AssetDatabase.LoadAssetAtPath<Shader>(StandardLitShaderAssetPath);
        }

        internal static bool TryImport(IImportedMaterialDescription description, Material material, Shader shader)
        {
            if (description == null || material == null || shader == null)
            {
                return false;
            }

            if (Is3DsMaxPhysicalMaterial(description))
            {
                Apply3DsMaxPhysicalMaterial(description, material, shader);
                return true;
            }

            if (Is3DsMaxSimplifiedPhysicalMaterial(description))
            {
                Apply3DsMaxSimplifiedPhysicalMaterial(description, material, shader);
                return true;
            }

            if (LooksLikeLegacyStandardMaterial(description))
            {
                ApplyLegacyStandardMaterial(description, material, shader);
                return true;
            }

            return false;
        }

        internal static bool Is3DsMaxPhysicalMaterial(IImportedMaterialDescription description)
        {
            description.TryGetFloat("ClassIDa", out float classIdA);
            description.TryGetFloat("ClassIDb", out float classIdB);
            description.TryGetString("ORIGINAL_MTL", out string originalMaterialType);

            if (Mathf.Approximately(classIdA, MaxPhysicalClassIdA) && Mathf.Approximately(classIdB, MaxPhysicalClassIdB))
            {
                return true;
            }

            return string.Equals(originalMaterialType, PhysicalMaterialTypeName, StringComparison.Ordinal);
        }

        internal static bool Is3DsMaxSimplifiedPhysicalMaterial(IImportedMaterialDescription description)
        {
            description.TryGetFloat("ClassIDa", out float classIdA);
            description.TryGetFloat("ClassIDb", out float classIdB);
            description.TryGetFloat("useGlossiness", out float useGlossiness);

            return Mathf.Approximately(classIdA, MaxSimplifiedPhysicalClassIdA)
                && Mathf.Approximately(classIdB, MaxSimplifiedPhysicalClassIdB)
                && Mathf.Approximately(useGlossiness, MaxSimplifiedGlossinessMode);
        }

        private static void Apply3DsMaxPhysicalMaterial(IImportedMaterialDescription description, Material material, Shader shader)
        {
            material.shader = shader;

            Color baseColor = Color.white;
            if (TryGetVectorColor(description, "base_color", out Color importedBaseColor))
            {
                baseColor = importedBaseColor;
            }

            if (TryGetFloat(description, "base_weight", out float baseWeight))
            {
                baseColor = MultiplyColorRgb(baseColor, baseWeight);
            }

            float alpha = 1.0f;
            if (TryGetFloat(description, "transparency", out float transparency))
            {
                alpha = Mathf.Clamp01(1.0f - transparency);
            }

            baseColor.a *= alpha;

            if (TryGetTexture(description, "base_color_map", out ImportedTextureProperty baseColorMap))
            {
                SetMaterialTextureProperty("_BaseMap", material, baseColorMap);
            }

            material.SetColor("_BaseColor", baseColor);

            if (TryGetFloat(description, "metalness", out float metalness))
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(metalness));
            }

            bool hasRoughnessTexture = false;
            if (TryGetTexture(description, "roughness_map", out ImportedTextureProperty roughnessMap))
            {
                SetMaterialTextureProperty("_RoughnessMap", material, roughnessMap);
                hasRoughnessTexture = roughnessMap.IsAssigned;
            }

            if (TryGetFloat(description, "roughness", out float roughness))
            {
                material.SetFloat("_Smoothness", Mathf.Clamp01(1.0f - roughness));
            }
            else if (hasRoughnessTexture)
            {
                material.SetFloat("_Smoothness", 1.0f);
            }

            if (TryGetTexture(description, "metalness_map", out ImportedTextureProperty metalnessMap))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, metalnessMap);
            }

            if (TryGetFloat(description, "bump_map_amt", out float bumpScale))
            {
                material.SetFloat("_BumpScale", bumpScale);
            }

            if (TryGetTexture(description, "bump_map", out ImportedTextureProperty bumpMap))
            {
                SetMaterialTextureProperty("_BumpMap", material, bumpMap);
            }

            float emissionWeight = 0.0f;
            if (TryGetFloat(description, "emission", out float importedEmissionWeight))
            {
                emissionWeight = Mathf.Max(importedEmissionWeight, 0.0f);
            }

            ApplyEmission(description, material, "emit_color_map", "emit_color", emissionWeight);

            bool hasOpacityMap = TryGetTexture(description, "transparency_map", out ImportedTextureProperty transparencyMap);
            if (hasOpacityMap)
            {
                SetMaterialTextureProperty("_OpacityMap", material, transparencyMap);
            }

            ApplyOpacityClipFallback(material, hasOpacityMap, DefaultAlphaCutoff);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
        }

        private static void Apply3DsMaxSimplifiedPhysicalMaterial(IImportedMaterialDescription description, Material material, Shader shader)
        {
            material.shader = shader;

            Color baseColor = Color.white;
            if (TryGetVectorColor(description, "basecolor", out Color importedBaseColor))
            {
                baseColor = importedBaseColor;
            }

            material.SetColor("_BaseColor", baseColor);

            if (TryGetTexture(description, "base_color_map", out ImportedTextureProperty baseColorMap))
            {
                SetMaterialTextureProperty("_BaseMap", material, baseColorMap);
            }

            bool hasOpacityMap = TryGetTexture(description, "opacity_map", out ImportedTextureProperty opacityMap);
            if (hasOpacityMap)
            {
                SetMaterialTextureProperty("_OpacityMap", material, opacityMap);
            }

            if (TryGetFloat(description, "metalness", out float metalness))
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(metalness));
            }

            if (TryGetTexture(description, "metalness_map", out ImportedTextureProperty metalnessMap))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, metalnessMap);
            }

            bool hasRoughnessTexture = false;
            if (TryGetTexture(description, "roughness_map", out ImportedTextureProperty roughnessMap))
            {
                SetMaterialTextureProperty("_RoughnessMap", material, roughnessMap);
                hasRoughnessTexture = roughnessMap.IsAssigned;
            }

            if (TryGetFloat(description, "roughness", out float roughness))
            {
                material.SetFloat("_Smoothness", Mathf.Clamp01(1.0f - roughness));
            }
            else if (hasRoughnessTexture)
            {
                material.SetFloat("_Smoothness", 1.0f);
            }

            if (TryGetFloat(description, "bump_map_amt", out float bumpScale))
            {
                material.SetFloat("_BumpScale", bumpScale);
            }

            if (TryGetTexture(description, "norm_map", out ImportedTextureProperty normalMap))
            {
                SetMaterialTextureProperty("_BumpMap", material, normalMap);
            }

            if (TryGetTexture(description, "ao_map", out ImportedTextureProperty occlusionMap))
            {
                SetMaterialTextureProperty("_OcclusionMap", material, occlusionMap);
                material.SetFloat("_OcclusionStrength", 1.0f);
            }

            ApplyEmission(description, material, "emit_color_map", "emit_color", 1.0f);
            ApplyOpacityClipFallback(material, hasOpacityMap, DefaultAlphaCutoff);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
        }

        private static void ApplyLegacyStandardMaterial(IImportedMaterialDescription description, Material material, Shader shader)
        {
            material.shader = shader;

            float alpha = ResolveLegacyOpacity(description);

            Color baseColor = Color.white;
            if (TryGetTexture(description, "DiffuseColor", out ImportedTextureProperty diffuseMap))
            {
                SetMaterialTextureProperty("_BaseMap", material, diffuseMap);
                if (TryGetFloat(description, "DiffuseFactor", out float diffuseFactor))
                {
                    baseColor = MultiplyColorRgb(baseColor, diffuseFactor);
                }
            }
            else if (TryGetVectorColor(description, "DiffuseColor", out Color importedDiffuseColor))
            {
                baseColor = importedDiffuseColor;
                if (TryGetFloat(description, "DiffuseFactor", out float diffuseFactor))
                {
                    baseColor = MultiplyColorRgb(baseColor, diffuseFactor);
                }
            }

            baseColor.a *= alpha;
            material.SetColor("_BaseColor", baseColor);

            bool hasOpacityMap = false;
            if (TryGetTexture(description, "Opacity", out ImportedTextureProperty opacityMap))
            {
                SetMaterialTextureProperty("_OpacityMap", material, opacityMap);
                hasOpacityMap = opacityMap.IsAssigned;
            }
            else if (TryGetTexture(description, "TransparentColor", out ImportedTextureProperty transparencyMap))
            {
                SetMaterialTextureProperty("_OpacityMap", material, transparencyMap);
                hasOpacityMap = transparencyMap.IsAssigned;
            }

            if (TryGetTexture(description, "Bump", out ImportedTextureProperty bumpMap))
            {
                SetMaterialTextureProperty("_BumpMap", material, bumpMap);
            }
            else if (TryGetTexture(description, "NormalMap", out ImportedTextureProperty normalMap))
            {
                SetMaterialTextureProperty("_BumpMap", material, normalMap);
            }

            if (TryGetFloat(description, "BumpFactor", out float bumpScale))
            {
                material.SetFloat("_BumpScale", bumpScale);
            }

            float smoothness = 0.0f;
            if (TryGetFloat(description, "Shininess", out float shininess))
            {
                smoothness = Mathf.Sqrt(Mathf.Max(0.0f, shininess * 0.01f));
            }

            material.SetFloat("_Smoothness", smoothness);

            float emissiveFactor = 1.0f;
            if (TryGetFloat(description, "EmissiveFactor", out float importedEmissiveFactor))
            {
                emissiveFactor = Mathf.Max(importedEmissiveFactor, 0.0f);
            }

            ApplyEmission(description, material, "EmissiveColor", "EmissiveColor", emissiveFactor);
            ApplyOpacityClipFallback(material, hasOpacityMap, DefaultAlphaCutoff);
            StandardLitMaterialUtility.SetupMaterial(material, null, false);
        }

        private static bool LooksLikeLegacyStandardMaterial(IImportedMaterialDescription description)
        {
            return HasAnyProperty(
                description,
                "Opacity",
                "TransparencyFactor",
                "TransparentColor",
                "DiffuseColor",
                "Bump",
                "NormalMap",
                "EmissiveColor",
                "Shininess");
        }

        private static float ResolveLegacyOpacity(IImportedMaterialDescription description)
        {
            if (TryGetFloat(description, "Opacity", out float opacity))
            {
                return Mathf.Clamp01(opacity);
            }

            if (TryGetFloat(description, "TransparencyFactor", out float transparencyFactor))
            {
                if (!Mathf.Approximately(transparencyFactor, 1.0f))
                {
                    return Mathf.Clamp01(1.0f - transparencyFactor);
                }
            }

            if (TryGetVectorColor(description, "TransparentColor", out Color transparentColor))
            {
                if (!Mathf.Approximately(transparentColor.r, 1.0f))
                {
                    return Mathf.Clamp01(1.0f - transparentColor.r);
                }
            }

            return 1.0f;
        }

        private static void ApplyEmission(
            IImportedMaterialDescription description,
            Material material,
            string emissionTexturePropertyName,
            string emissionColorPropertyName,
            float emissionMultiplier)
        {
            emissionMultiplier = Mathf.Max(emissionMultiplier, 0.0f);

            if (TryGetTexture(description, emissionTexturePropertyName, out ImportedTextureProperty emissionMap))
            {
                SetMaterialTextureProperty("_EmissionMap", material, emissionMap);
                Color emissionColor = Color.white * (Mathf.Approximately(emissionMultiplier, 0.0f) ? 1.0f : emissionMultiplier);
                material.SetColor("_EmissionColor", emissionColor);
                return;
            }

            if (TryGetVectorColor(description, emissionColorPropertyName, out Color emissionColorVector))
            {
                if (!Mathf.Approximately(emissionMultiplier, 0.0f))
                {
                    emissionColorVector = MultiplyColorRgb(emissionColorVector, emissionMultiplier);
                }

                material.SetColor("_EmissionColor", emissionColorVector);
                return;
            }

            if (emissionMultiplier > 0.0f)
            {
                material.SetColor("_EmissionColor", Color.white * emissionMultiplier);
            }
        }

        private static void ApplyOpacityClipFallback(Material material, bool hasOpacityMap, float cutoff)
        {
            if (!hasOpacityMap)
            {
                return;
            }

            material.SetFloat("_AlphaClip", 1.0f);
            material.SetFloat("_Cutoff", cutoff);
        }

        private static void SetMaterialTextureProperty(string propertyName, Material material, ImportedTextureProperty textureProperty)
        {
            Texture texture = PrepareTextureForMaterial(textureProperty);
            material.SetTexture(propertyName, texture);
            material.SetTextureOffset(propertyName, textureProperty.Offset);
            material.SetTextureScale(propertyName, textureProperty.Scale);
        }

        private static Texture PrepareTextureForMaterial(ImportedTextureProperty textureProperty)
        {
            if (!textureProperty.IsAssigned)
            {
                return null;
            }

            Texture texture = textureProperty.Texture;
            if (!ShouldImportTextureAsNormalMap(texture))
            {
                return texture;
            }

            string textureAssetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(textureAssetPath))
            {
                return texture;
            }

            if (AssetImporter.GetAtPath(textureAssetPath) is not TextureImporter textureImporter)
            {
                return texture;
            }

            if (!ApplyNormalMapImportSettings(textureImporter))
            {
                return texture;
            }

            textureImporter.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture>(textureAssetPath) ?? texture;
        }

        internal static bool ShouldImportTextureAsNormalMap(Texture texture)
        {
            return texture != null
                && texture.name.IndexOf(NormalTextureNameKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool ApplyNormalMapImportSettings(TextureImporter textureImporter)
        {
            if (textureImporter == null)
            {
                return false;
            }

            bool hasChanges = false;

            if (textureImporter.textureType != TextureImporterType.NormalMap)
            {
                textureImporter.textureType = TextureImporterType.NormalMap;
                hasChanges = true;
            }

            if (textureImporter.wrapMode != TextureWrapMode.Repeat)
            {
                textureImporter.wrapMode = TextureWrapMode.Repeat;
                hasChanges = true;
            }

            if (textureImporter.filterMode != FilterMode.Trilinear)
            {
                textureImporter.filterMode = FilterMode.Trilinear;
                hasChanges = true;
            }

            return hasChanges;
        }

        private static bool TryGetFloat(IImportedMaterialDescription description, string propertyName, out float value)
        {
            return description.TryGetFloat(propertyName, out value);
        }

        private static bool TryGetTexture(IImportedMaterialDescription description, string propertyName, out ImportedTextureProperty textureProperty)
        {
            return description.TryGetTexture(propertyName, out textureProperty);
        }

        private static bool TryGetVectorColor(IImportedMaterialDescription description, string propertyName, out Color color)
        {
            if (description.TryGetVector(propertyName, out Vector4 value))
            {
                color = value;
                return true;
            }

            color = default;
            return false;
        }

        private static bool HasAnyProperty(IImportedMaterialDescription description, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (description.TryGetFloat(propertyName, out _)
                    || description.TryGetVector(propertyName, out _)
                    || description.TryGetTexture(propertyName, out _)
                    || description.TryGetString(propertyName, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static Color MultiplyColorRgb(Color color, float multiplier)
        {
            color.r *= multiplier;
            color.g *= multiplier;
            color.b *= multiplier;
            return color;
        }
    }

    internal sealed class StandardLitMaterialDescriptionPreprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx",
            ".obj",
            ".blend",
            ".mb",
            ".ma",
            ".max",
        };

        public override uint GetVersion()
        {
            return 2;
        }

        public override int GetPostprocessOrder()
        {
            return 100;
        }

        public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (description == null || material == null)
            {
                return;
            }

            if (GraphicsSettings.currentRenderPipeline is not VividRenderPipelineAsset)
            {
                return;
            }

            string extension = Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(extension) || !SupportedExtensions.Contains(extension))
            {
                return;
            }

            Shader shader = StandardLitMaterialImportUtility.GetStandardLitShader();
            if (shader == null)
            {
                return;
            }

            _ = clips;
            var adapter = new MaterialDescriptionAdapter(description);
            if (StandardLitMaterialImportUtility.TryImport(adapter, material, shader))
            {
                StandardLitRMOAutoPacker.BindOrSchedule(assetPath, adapter, material);
            }
        }

        private sealed class MaterialDescriptionAdapter : IImportedMaterialDescription
        {
            private readonly MaterialDescription m_Description;

            internal MaterialDescriptionAdapter(MaterialDescription description)
            {
                m_Description = description;
            }

            public bool TryGetFloat(string propertyName, out float value)
            {
                return m_Description.TryGetProperty(propertyName, out value);
            }

            public bool TryGetVector(string propertyName, out Vector4 value)
            {
                return m_Description.TryGetProperty(propertyName, out value);
            }

            public bool TryGetTexture(string propertyName, out ImportedTextureProperty value)
            {
                if (m_Description.TryGetProperty(propertyName, out TexturePropertyDescription textureProperty))
                {
                    value = new ImportedTextureProperty(textureProperty.texture, textureProperty.offset, textureProperty.scale);
                    return true;
                }

                value = default;
                return false;
            }

            public bool TryGetString(string propertyName, out string value)
            {
                return m_Description.TryGetProperty(propertyName, out value);
            }
        }
    }
}
