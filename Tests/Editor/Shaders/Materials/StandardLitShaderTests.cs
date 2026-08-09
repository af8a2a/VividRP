using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StandardLitShaderTests
    {
        private const string StandardLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit/StandardLit.shader";
        private const string StandardLayeredLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLayeredLit/StandardLayeredLit.shader";

        [Test]
        public void StandardLitShader_DeclaresMetaPass_ForGlobalIlluminationBaking()
        {
            UnityEngine.Material material = CreateMaterial();

            try
            {
                int metaPassIndex = material.FindPass("Meta");

                Assert.That(metaPassIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    material.shader.FindPassTagValue(metaPassIndex, new ShaderTagId("LightMode")),
                    Is.EqualTo(new ShaderTagId("Meta")));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void StandardLitShader_ExposesOptInOpenPbrTransmissionProperties()
        {
            UnityEngine.Material material = CreateMaterial();
            try
            {
                Assert.That(
                    material.HasProperty("_ThinWalledTransmission"),
                    Is.True);
                Assert.That(material.HasProperty("_TransmissionWeight"), Is.True);
                Assert.That(material.HasProperty("_TransmissionMap"), Is.True);
                Assert.That(material.HasProperty("_TransmissionColor"), Is.True);
                Assert.That(material.HasProperty("_TransmissionDepth"), Is.True);
                Assert.That(material.HasProperty("_TransmissionScatter"), Is.True);
                Assert.That(
                    material.HasProperty(
                        "_TransmissionScatterAnisotropy"),
                    Is.True);
                Assert.That(material.HasProperty("_SpecularIOR"), Is.True);
                Assert.That(
                    material.GetFloat("_ThinWalledTransmission"),
                    Is.Zero);
                Assert.That(material.GetFloat("_TransmissionWeight"), Is.Zero);
                Assert.That(
                    material.GetColor("_TransmissionColor"),
                    Is.EqualTo(Color.white));
                Assert.That(material.GetFloat("_TransmissionDepth"), Is.Zero);
                Assert.That(
                    material.GetColor("_TransmissionScatter"),
                    Is.EqualTo(Color.clear));
                Assert.That(
                    material.GetFloat(
                        "_TransmissionScatterAnisotropy"),
                    Is.Zero);
                Assert.That(
                    material.GetFloat("_SpecularIOR"),
                    Is.EqualTo(1.5f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void StandardLitShader_ExposesOptInFaceSubsurfaceProperties()
        {
            UnityEngine.Material material = CreateMaterial();
            try
            {
                Assert.That(material.HasProperty("_SubsurfaceWeight"), Is.True);
                Assert.That(material.HasProperty("_SubsurfaceColor"), Is.True);
                Assert.That(material.HasProperty("_SubsurfaceRadius"), Is.True);
                Assert.That(
                    material.HasProperty("_SubsurfaceRadiusScale"),
                    Is.True);
                Assert.That(
                    material.HasProperty(
                        "_SubsurfaceScatterAnisotropy"),
                    Is.True);
                Assert.That(
                    material.HasProperty(
                        "_SubsurfaceTransmissionWeight"),
                    Is.True);
                Assert.That(material.GetFloat("_SubsurfaceWeight"), Is.Zero);
                Assert.That(
                    material.GetColor("_SubsurfaceColor"),
                    Is.EqualTo(Color.white));
                Assert.That(
                    material.GetFloat("_SubsurfaceRadius"),
                    Is.EqualTo(0.01f).Within(1e-6f));
                Assert.That(
                    material.GetColor("_SubsurfaceRadiusScale"),
                    Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)));
                Assert.That(
                    material.GetFloat(
                        "_SubsurfaceScatterAnisotropy"),
                    Is.Zero);
                Assert.That(
                    material.GetFloat(
                        "_SubsurfaceTransmissionWeight"),
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void StandardLitShader_DoesNotExposeDeprecatedOpacityColor()
        {
            UnityEngine.Material material = CreateMaterial();
            try
            {
                Assert.That(material.HasProperty("_OpacityColor"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_UpdatesAlphaClipKeywordAndQueue_WhenAlphaClipEnabled()
        {
            UnityEngine.Material material = CreateMaterial();
            try
            {
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetFloat("_QueueOffset", 7.0f);

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest + 7));
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_UpdatesFeatureKeywords_WhenFeatureInputsAssigned()
        {
            Material material = CreateMaterial();
            Texture2D opacityMap = CreateTexture();
            Texture2D transmissionMap = CreateTexture();
            Texture2D normalMap = CreateTexture();
            Texture2D metallicMap = CreateTexture();
            Texture2D roughnessMap = CreateTexture();
            Texture2D occlusionMap = CreateTexture();
            try
            {
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetTexture("_OpacityMap", opacityMap);
                material.SetTexture("_TransmissionMap", transmissionMap);
                material.SetTexture("_BumpMap", normalMap);
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetTexture("_RoughnessMap", roughnessMap);
                material.SetTexture("_OcclusionMap", occlusionMap);
                material.SetColor("_EmissionColor", new Color(0.25f, 0.5f, 0.75f, 1.0f));
                material.SetFloat("_ClearCoatMask", 0.8f);
                material.SetFloat("_SmoothnessTextureChannel", 1.0f);

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.IsKeywordEnabled("_OPACITYMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_TRANSMISSIONMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ROUGHNESSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_OCCLUSIONMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(material.IsKeywordEnabled("_CLEARCOAT"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(opacityMap);
                Object.DestroyImmediate(transmissionMap);
                Object.DestroyImmediate(normalMap);
                Object.DestroyImmediate(metallicMap);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(occlusionMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_PreservesPathTracingTransparency_AndDowngradesUnsupportedSpecularWorkflow()
        {
            Material material = CreateMaterial();
            try
            {
                material.SetFloat("_WorkflowMode", 0.0f);
                material.SetFloat("_Surface", 1.0f);
                material.SetFloat("_QueueOffset", 5.0f);

                LogAssert.Expect(LogType.Warning, new Regex("Specular workflow is not supported yet"));

                StandardLitMaterialUtility.SetupMaterial(material, null, true);

                Assert.That(material.GetFloat("_WorkflowMode"), Is.EqualTo(StandardLitMaterialUtility.MetallicWorkflow));
                Assert.That(
                    material.GetFloat("_Surface"),
                    Is.EqualTo(StandardLitMaterialUtility.TransparentSurface));
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SPECULAR_SETUP"), Is.False);
                Assert.That(
                    material.GetFloat("_SrcBlend"),
                    Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(
                    material.GetFloat("_DstBlend"),
                    Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
                Assert.That(
                    material.renderQueue,
                    Is.EqualTo((int)RenderQueue.Transparent + 5));
                Assert.That(
                    material.GetTag("RenderType", false),
                    Is.EqualTo("Transparent"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SyncsLegacyAliases_WhenBaseInputsChange()
        {
            UnityEngine.Material material = CreateMaterial();
            Texture2D baseMap = CreateTexture();
            try
            {
                material.SetTexture("_BaseMap", baseMap);
                material.SetTextureScale("_BaseMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_BaseMap", new Vector2(0.25f, 0.5f));
                material.SetColor("_BaseColor", new Color(0.1f, 0.2f, 0.3f, 0.4f));

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.GetTexture("_MainTex"), Is.SameAs(baseMap));
                Assert.That(material.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(material.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(material.GetColor("_Color"), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)).Using(ColorEqualityComparer.Instance));
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_PreservesInstancingChoice_WhenInstancingIsDisabled()
        {
            UnityEngine.Material material = CreateMaterial();

            try
            {
                material.enableInstancing = false;

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.enableInstancing, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SyncsVirtualTextureBaseColorKeyword_WhenLayeredLitToggleChanges()
        {
            UnityEngine.Material material = CreateStandardLayeredLitMaterial();

            try
            {
                material.SetFloat("_UseVirtualTextureBaseColor", 1.0f);

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_VIRTUAL_TEXTURE_BASE_COLOR"), Is.True);

                material.SetFloat("_UseVirtualTextureBaseColor", 0.0f);

                StandardLitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_VIRTUAL_TEXTURE_BASE_COLOR"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_KeepsLayeredLitTransparencyUnsupported()
        {
            UnityEngine.Material material = CreateStandardLayeredLitMaterial();
            try
            {
                material.SetFloat("_Surface", 1.0f);

                StandardLitMaterialUtility.SetupMaterial(
                    material,
                    null,
                    false);

                Assert.That(
                    material.GetFloat("_Surface"),
                    Is.EqualTo(StandardLitMaterialUtility.OpaqueSurface));
                Assert.That(
                    material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static UnityEngine.Material CreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StandardLitShaderAssetPath);
            Assert.That(shader, Is.Not.Null, $"Expected shader asset at '{StandardLitShaderAssetPath}'.");
            return new UnityEngine.Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static UnityEngine.Material CreateStandardLayeredLitMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StandardLayeredLitShaderAssetPath);
            Assert.That(shader, Is.Not.Null, $"Expected shader asset at '{StandardLayeredLitShaderAssetPath}'.");
            return new UnityEngine.Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Texture2D CreateTexture()
        {
            return new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private sealed class ColorEqualityComparer : IEqualityComparer<Color>
        {
            internal static readonly ColorEqualityComparer Instance = new ColorEqualityComparer();

            private const float Tolerance = 0.0001f;

            public bool Equals(Color x, Color y)
            {
                return Mathf.Abs(x.r - y.r) <= Tolerance
                    && Mathf.Abs(x.g - y.g) <= Tolerance
                    && Mathf.Abs(x.b - y.b) <= Tolerance
                    && Mathf.Abs(x.a - y.a) <= Tolerance;
            }

            public int GetHashCode(Color obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
