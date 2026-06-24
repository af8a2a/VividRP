using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class UnlitShaderTests
    {
        [Test]
        public void UnlitShader_DeclaresRequiredPasses_ForVividRenderLists()
        {
            Material material = CreateMaterial();

            try
            {
                AssertPassTag(material, RenderGraphRenderListDesc.PreDepthShaderTagName, RenderGraphRenderListDesc.PreDepthShaderTagName);
                AssertPassTag(material, "VividForward", RenderGraphRenderListDesc.ForwardShaderTagName);
                AssertPassTag(material, RenderGraphRenderListDesc.DefaultUnlitShaderTagName, RenderGraphRenderListDesc.DefaultUnlitShaderTagName);
                AssertPassTag(material, "ShadowCaster", "ShadowCaster");
                AssertPassTag(material, "MotionVectors", "MotionVectors");
                AssertPassTag(material, "Meta", "Meta");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SyncsKeywordsAndLegacyAliases_WhenInputsAssigned()
        {
            Material material = CreateMaterial();
            Texture2D colorMap = CreateTexture();
            Texture2D emissiveMap = CreateTexture();

            try
            {
                material.SetTexture("_UnlitColorMap", colorMap);
                material.SetTextureScale("_UnlitColorMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_UnlitColorMap", new Vector2(0.25f, 0.5f));
                material.SetTexture("_EmissiveColorMap", emissiveMap);
                material.SetColor("_UnlitColor", new Color(0.1f, 0.2f, 0.3f, 0.4f));
                material.SetColor("_EmissiveColor", new Color(1.0f, 0.5f, 0.25f, 1.0f));
                material.SetFloat("_AlphaCutoffEnable", 1.0f);
                material.SetFloat("_AlphaCutoff", 0.35f);

                UnlitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.True);
                Assert.That(material.GetTexture("_MainTex"), Is.SameAs(colorMap));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(colorMap));
                Assert.That(material.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(material.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(material.GetColor("_Color"), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_EmissionColor"), Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetFloat("_Cutoff"), Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            }
            finally
            {
                Object.DestroyImmediate(colorMap);
                Object.DestroyImmediate(emissiveMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_ConfiguresTransparentBlendState_WhenSurfaceIsTransparent()
        {
            Material material = CreateMaterial();

            try
            {
                material.SetFloat("_SurfaceType", UnlitMaterialUtility.TransparentSurface);
                material.SetFloat("_BlendMode", 0.0f);
                material.SetFloat("_QueueOffset", 7.0f);

                UnlitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent + 7));
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_AlphaSrcBlend"), Is.EqualTo((float)BlendMode.One));
                Assert.That(material.GetFloat("_AlphaDstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_PreservesInstancingChoice_WhenInstancingIsDisabled()
        {
            Material material = CreateMaterial();

            try
            {
                material.enableInstancing = false;

                UnlitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.enableInstancing, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static Material CreateMaterial()
        {
            Shader shader = UnlitMaterialUtility.GetUnlitShader();
            Assert.That(shader, Is.Not.Null, $"Expected shader '{UnlitMaterialUtility.UnlitShaderName}' at '{UnlitMaterialUtility.UnlitShaderAssetPath}'.");
            return new Material(shader)
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

        private static void AssertPassTag(Material material, string passName, string expectedLightMode)
        {
            int passIndex = material.FindPass(passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), $"Expected pass '{passName}'.");
            Assert.That(
                material.shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")),
                Is.EqualTo(new ShaderTagId(expectedLightMode)));
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
