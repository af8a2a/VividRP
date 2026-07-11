using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor;
using VividRP.Runtime.Particle;

namespace VividRP.Editor.Tests
{
    public sealed class ParticleUnlitShaderTests
    {
        [Test]
        public void ParticleUnlitShader_CanBeResolvedByName()
        {
            Assert.That(Shader.Find(ParticleUnlitMaterialUtility.ParticleUnlitShaderName), Is.Not.Null);
            Assert.That(VividParticleSystemManager.DefaultShaderName, Is.EqualTo(ParticleUnlitMaterialUtility.ParticleUnlitShaderName));
            Assert.That(VividParticleSystemManager.PickingShaderName, Is.EqualTo(ParticleUnlitMaterialUtility.ParticleUnlitShaderName));
        }

        [Test]
        public void ParticleUnlitShader_DeclaresRequiredPasses_ForVividRenderLists()
        {
            Material material = CreateMaterial();

            try
            {
                AssertPassTag(material, "VividForward", "VividForward");
                AssertPassTag(material, "SRPDefaultUnlit", "SRPDefaultUnlit");
                AssertPassTag(material, "ScenePickingPass", "Picking");
                AssertPassTag(material, "SceneSelectionPass", "SceneSelectionPass");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ParticleUnlitShader_IsSRPBatcherCompatible_ForBRGPasses()
        {
            Material material = CreateMaterial();

            try
            {
                AssertNoSRPBatcherIssue(material, "VividForward");
                AssertNoSRPBatcherIssue(material, "SRPDefaultUnlit");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ParticleUnlitShader_DeclaresCompactParticleGpuProperties()
        {
            Material material = CreateMaterial();

            try
            {
                Assert.That(material.HasProperty("_VividParticlePositionSize"), Is.True);
                Assert.That(material.HasProperty("_BaseColor"), Is.True);
                Assert.That(material.HasProperty("_VividParticleRotation"), Is.True);
                Assert.That(material.HasProperty("_VividParticleVelocityStretch"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SyncsTransparentStateAndLegacyAliases_WhenInputsAssigned()
        {
            Material material = CreateMaterial();
            Texture2D colorMap = CreateTexture();

            try
            {
                material.SetTexture("_UnlitColorMap", colorMap);
                material.SetTextureScale("_UnlitColorMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_UnlitColorMap", new Vector2(0.25f, 0.5f));
                material.SetColor("_UnlitColor", new Color(0.1f, 0.2f, 0.3f, 0.4f));
                material.SetFloat("_AlphaCutoffEnable", 1.0f);
                material.SetFloat("_AlphaCutoff", 0.35f);
                material.SetFloat("_SurfaceType", UnlitMaterialUtility.TransparentSurface);
                material.SetFloat("_QueueOffset", 9.0f);

                ParticleUnlitMaterialUtility.SetupMaterial(material, null, false);

                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.GetTexture("_MainTex"), Is.SameAs(colorMap));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(colorMap));
                Assert.That(material.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(material.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(material.GetColor("_Color"), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 0.4f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetFloat("_Cutoff"), Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent + 9));
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_AlphaSrcBlend"), Is.EqualTo((float)BlendMode.One));
                Assert.That(material.GetFloat("_AlphaDstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
                Assert.That(material.GetFloat("_CullMode"), Is.EqualTo((float)CullMode.Off));
            }
            finally
            {
                Object.DestroyImmediate(colorMap);
                Object.DestroyImmediate(material);
            }
        }

        private static Material CreateMaterial()
        {
            Shader shader = ParticleUnlitMaterialUtility.GetParticleUnlitShader();
            Assert.That(shader, Is.Not.Null, $"Expected shader '{ParticleUnlitMaterialUtility.ParticleUnlitShaderName}'.");
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

        private static void AssertNoSRPBatcherIssue(Material material, string passName)
        {
            int passIndex = material.FindPass(passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), $"Expected pass '{passName}'.");

            string issueReason = GetSRPBatcherCompatibilityIssueReason(material.shader, passIndex);
            bool isCompatible = string.IsNullOrEmpty(issueReason)
                || issueReason.StartsWith("OK", System.StringComparison.OrdinalIgnoreCase)
                || issueReason.StartsWith("Not initialized", System.StringComparison.OrdinalIgnoreCase);
            Assert.That(isCompatible, Is.True, $"Pass '{passName}' is not SRP Batcher compatible: {issueReason}");
        }

        private static string GetSRPBatcherCompatibilityIssueReason(Shader shader, int passIndex)
        {
            MethodInfo method = typeof(ShaderUtil).GetMethod(
                "GetSRPBatcherCompatibilityIssueReason",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Shader), typeof(int), typeof(int) },
                null);

            if (method == null)
                Assert.Ignore("This Unity version does not expose ShaderUtil.GetSRPBatcherCompatibilityIssueReason.");

            return (string)method.Invoke(null, new object[] { shader, 0, passIndex }) ?? string.Empty;
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
