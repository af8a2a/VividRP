using System.IO;
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
        public void TerrainLitShader_DeclaresTerrainCompatibilityTagsAndDependencies()
        {
            string source = File.ReadAllText(GetPackageFilePath(TerrainLitShaderRelativePath));

            Assert.That(source, Does.Contain("Shader \"VividRP/Terrain/TerrainLit\""));
            Assert.That(source, Does.Contain("\"RenderPipeline\" = \"VividRenderPipeline\""));
            Assert.That(source, Does.Contain("\"TerrainCompatible\" = \"True\""));
            Assert.That(source, Does.Contain("\"SplatCount\" = \"8\""));
            Assert.That(source, Does.Contain("\"MaskMapR\" = \"Metallic\""));
            Assert.That(source, Does.Contain("\"MaskMapG\" = \"AO\""));
            Assert.That(source, Does.Contain("\"MaskMapB\" = \"Height\""));
            Assert.That(source, Does.Contain("\"MaskMapA\" = \"Smoothness\""));
            Assert.That(source, Does.Contain("Dependency \"BaseMapShader\" = \"Hidden/VividRP/TerrainLit_Basemap\""));
            Assert.That(source, Does.Contain("Dependency \"BaseMapGenShader\" = \"Hidden/VividRP/TerrainLit_BasemapGen\""));
            Assert.That(source, Does.Contain("CustomEditor \"VividRP.Editor.TerrainLitShaderGUI\""));
        }

        [Test]
        public void TerrainLitShader_DeclaresRequiredTerrainKeywords()
        {
            string source = File.ReadAllText(GetPackageFilePath(TerrainLitShaderRelativePath));

            Assert.That(source, Does.Contain("_TERRAIN_8_LAYERS"));
            Assert.That(source, Does.Contain("_NORMALMAP"));
            Assert.That(source, Does.Contain("_MASKMAP"));
            Assert.That(source, Does.Contain("_TERRAIN_BLEND_HEIGHT"));
            Assert.That(source, Does.Contain("_TERRAIN_INSTANCED_PERPIXEL_NORMAL"));
            Assert.That(source, Does.Contain("_ALPHATEST_ON"));
        }

        [Test]
        public void TerrainLitBasemapShaders_LoadAndDeclareExpectedContracts()
        {
            Shader basemapShader = LoadShader(TerrainLitBasemapShaderRelativePath);
            Shader basemapGenShader = LoadShader(TerrainLitBasemapGenShaderRelativePath);
            string basemapSource = File.ReadAllText(GetPackageFilePath(TerrainLitBasemapShaderRelativePath));
            string basemapGenSource = File.ReadAllText(GetPackageFilePath(TerrainLitBasemapGenShaderRelativePath));

            Assert.That(basemapShader, Is.Not.Null);
            Assert.That(basemapGenShader, Is.Not.Null);
            Assert.That(basemapSource, Does.Contain("Shader \"Hidden/VividRP/TerrainLit_Basemap\""));
            Assert.That(basemapSource, Does.Contain("Name \"VividGBuffer\""));
            Assert.That(basemapSource, Does.Contain("#define VIVID_TERRAIN_BASEMAP 1"));
            Assert.That(basemapGenSource, Does.Contain("Shader \"Hidden/VividRP/TerrainLit_BasemapGen\""));
            Assert.That(basemapGenSource, Does.Contain("\"Name\" = \"_MainTex\""));
            Assert.That(basemapGenSource, Does.Contain("\"Format\" = \"ARGB32\""));
            Assert.That(basemapGenSource, Does.Contain("\"Size\" = \"1\""));
            Assert.That(basemapGenSource, Does.Contain("\"Name\" = \"_MetallicTex\""));
            Assert.That(basemapGenSource, Does.Contain("\"Format\" = \"RG16\""));
            Assert.That(basemapGenSource, Does.Contain("\"Size\" = \"1/4\""));
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
                Assert.That(material.enableInstancing, Is.True);
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

        [Test]
        public void TerrainLitGBufferPath_MapsSsrAndDecalPropertiesToMaterialFeatures()
        {
            Material material = CreateTerrainLitMaterial();
            string passSource = File.ReadAllText(GetPackageFilePath("Shaders/Material/TerrainLit/TerrainLitPass.hlsl"));

            try
            {
                Assert.That(material.HasProperty("_ReceivesSSR"), Is.True);
                Assert.That(material.HasProperty("_SupportDecals"), Is.True);
                Assert.That(passSource, Does.Contain("if (_ReceivesSSR > 0.5)"));
                Assert.That(passSource, Does.Contain("VIVID_MATERIALFEATURE_SSR_RECEIVE"));
                Assert.That(passSource, Does.Contain("if (_SupportDecals > 0.5)"));
                Assert.That(passSource, Does.Contain("VIVID_MATERIALFEATURE_DECAL_RECEIVE"));
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

        private static string GetPackageFilePath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (string packageRoot in GetCandidatePackageRoots(projectRoot))
            {
                string fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(projectRoot, "Packages", "Custom_URP", relativePath.Replace('/', Path.DirectorySeparatorChar));
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

        private static string[] GetCandidatePackageRoots(string projectRoot)
        {
            return new[]
            {
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp"),
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
            };
        }
    }
}
