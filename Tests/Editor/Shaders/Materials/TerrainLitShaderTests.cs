using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Editor.Tests
{
    public sealed class TerrainLitShaderTests
    {
        private const string TerrainLitShaderRelativePath = "Shaders/Material/TerrainLit/TerrainLit.shader";
        private const string TerrainLitBasemapShaderRelativePath = "Shaders/Material/TerrainLit/TerrainLit_Basemap.shader";
        private const string TerrainLitBasemapGenShaderRelativePath = "Shaders/Material/TerrainLit/TerrainLit_BasemapGen.shader";

        [Test]
        public void TerrainLitShader_LoadsAndDeclaresRequiredPasses()
        {
            Shader shader = LoadShader(TerrainLitShaderRelativePath);

            Assert.That(shader, Is.Not.Null);
            Material material = new(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            try
            {
                AssertPassTag(material, "VividPreDepth", "VividPreDepth");
                AssertPassTag(material, "ShadowCaster", "ShadowCaster");
                AssertPassTag(material, "VividGBuffer", "VividGBuffer");
                AssertPassTag(material, "VividGBufferGPUDrivenDecal", "VividGBufferGPUDrivenDecal");
                AssertPassTag(material, "Meta", "Meta");
                AssertPassTag(material, "MotionVectors", "MotionVectors");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_SyncsTerrainKeywordsAndOpaqueState()
        {
            Material material = CreateTerrainLitMaterial();

            try
            {
                material.SetFloat("_EnableHeightBlend", 1.0f);
                material.SetFloat("_EnableInstancedPerPixelNormal", 1.0f);
                material.enableInstancing = false;

                TerrainLitMaterialUtility.SetupMaterial(material);

                Assert.That(material.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT"), Is.True);
                Assert.That(material.IsKeywordEnabled("_TERRAIN_INSTANCED_PERPIXEL_NORMAL"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.False);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Opaque"));
                Assert.That(material.enableInstancing, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_ConfiguresAlphaTestQueue_WhenAlphaClipIsEnabled()
        {
            Material material = CreateTerrainLitMaterial();

            try
            {
                material.SetFloat("_AlphaClip", 1.0f);

                TerrainLitMaterialUtility.SetupMaterial(material);

                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static Material CreateTerrainLitMaterial()
        {
            Shader shader = LoadShader(TerrainLitShaderRelativePath);
            Assert.That(shader, Is.Not.Null, "Expected TerrainLit shader asset to load.");
            return new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Shader LoadShader(string relativePath)
        {
            foreach (string packagePath in GetCandidateAssetPaths(relativePath))
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(packagePath);
                if (shader != null)
                    return shader;
            }

            return null;
        }

        private static void AssertPassTag(Material material, string passName, string expectedLightMode)
        {
            int passIndex = material.FindPass(passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), $"Expected pass '{passName}'.");
            Assert.That(
                material.shader.FindPassTagValue(passIndex, new ShaderTagId("LightMode")),
                Is.EqualTo(new ShaderTagId(expectedLightMode)));
        }

        private static string[] GetCandidateAssetPaths(string relativePath)
        {
            return new[]
            {
                $"Packages/com.af8a2a.vividrp/{relativePath}",
                $"Packages/VividRP/{relativePath}",
                $"Packages/Custom_URP/{relativePath}",
            };
        }
    }
}
