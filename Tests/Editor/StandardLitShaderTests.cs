using System.Collections.Generic;
using System.IO;
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
        private const string StandardLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit.shader";

        [Test]
        public void StandardLitShader_DeclaresRequiredPasses_ForDrawObjectPass()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain($"Name \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain($"\"LightMode\" = \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain("Name \"VividGBuffer\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividGBuffer\""));
            Assert.That(shaderSource, Does.Contain("Name \"SRPDefaultUnlit\""));
        }

        [Test]
        public void StandardLitShader_DeclaresIndirectDxrPass_ForIndirectDiffuseHitShaders()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("Name \"IndirectDXR\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"IndirectDXR\""));
            Assert.That(shaderSource, Does.Contain("#pragma raytracing surface_shader"));
            Assert.That(shaderSource, Does.Contain("#pragma shader_feature_local_raytracing _ALPHATEST_ON"));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME StandardLitIndirectDiffuseClosestHit"));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME StandardLitIndirectDiffuseAnyHit"));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl\""));
        }

        [Test]
        public void StandardLitShader_DeclaresAdaptiveProbeVolumeVariants_ForDeferredAndIndirectDxrPasses()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile _ LIGHTMAP_ON"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile _ DIRLIGHTMAP_COMBINED"));
        }

        [Test]
        public void StandardLitIndirectDiffusePass_SamplesBakedGiOrProbeVolume_ForRayTracingHits()
        {
            string source = File.ReadAllText(GetIndirectDiffuseSourcePath());

            Assert.That(source, Does.Contain("#define ATTRIBUTES_NEED_TEXCOORD0"));
            Assert.That(source, Does.Contain("#define ATTRIBUTES_NEED_TEXCOORD1"));
            Assert.That(source, Does.Contain("SampleStandardLitIndirectDiffuseBakedGI("));
            Assert.That(source, Does.Contain("SampleVividBakedGI(geometry.lightmapUV, normalWS)"));
            Assert.That(source, Does.Contain("SampleVividProbeVolume("));
            Assert.That(source, Does.Contain("surfaceData.bakedGI = SampleStandardLitIndirectDiffuseBakedGI(geometry, surfaceData.normalWS);"));
            Assert.That(source, Does.Contain("lightingRadiance += surfaceData.bakedGI * diffuseColor * INV_PI;"));
        }

        [Test]
        public void StandardLitShader_DeclaresUrpCompatibleCoreProperties()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("_WorkflowMode"));
            Assert.That(shaderSource, Does.Contain("_BaseMap"));
            Assert.That(shaderSource, Does.Contain("_BaseColor"));
            Assert.That(shaderSource, Does.Contain("_OpacityMap"));
            Assert.That(shaderSource, Does.Contain("_Cutoff"));
            Assert.That(shaderSource, Does.Contain("_Smoothness"));
            Assert.That(shaderSource, Does.Contain("_SmoothnessTextureChannel"));
            Assert.That(shaderSource, Does.Contain("_Metallic"));
            Assert.That(shaderSource, Does.Contain("_MetallicGlossMap"));
            Assert.That(shaderSource, Does.Contain("_RoughnessMap"));
            Assert.That(shaderSource, Does.Contain("_BumpScale"));
            Assert.That(shaderSource, Does.Contain("_BumpMap"));
            Assert.That(shaderSource, Does.Contain("_OcclusionStrength"));
            Assert.That(shaderSource, Does.Contain("_OcclusionMap"));
            Assert.That(shaderSource, Does.Contain("_EmissionColor"));
            Assert.That(shaderSource, Does.Contain("_EmissionMap"));
            Assert.That(shaderSource, Does.Contain("_ClearCoatMask"));
            Assert.That(shaderSource, Does.Contain("_ClearCoatSmoothness"));
            Assert.That(shaderSource, Does.Contain("_MainTex"));
            Assert.That(shaderSource, Does.Contain("_Color"));
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
            Texture2D normalMap = CreateTexture();
            Texture2D metallicMap = CreateTexture();
            Texture2D roughnessMap = CreateTexture();
            Texture2D occlusionMap = CreateTexture();
            try
            {
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetTexture("_OpacityMap", opacityMap);
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
                Object.DestroyImmediate(normalMap);
                Object.DestroyImmediate(metallicMap);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(occlusionMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SetupMaterial_DowngradesUnsupportedModes_WhenSpecularWorkflowAndTransparentSurfaceSelected()
        {
            Material material = CreateMaterial();
            try
            {
                material.SetFloat("_WorkflowMode", 0.0f);
                material.SetFloat("_Surface", 1.0f);

                LogAssert.Expect(LogType.Warning, new Regex("Specular workflow is not supported yet"));
                LogAssert.Expect(LogType.Warning, new Regex("Transparent surface type is not supported yet"));

                StandardLitMaterialUtility.SetupMaterial(material, null, true);

                Assert.That(material.GetFloat("_WorkflowMode"), Is.EqualTo(StandardLitMaterialUtility.MetallicWorkflow));
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(0.0f));
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.False);
                Assert.That(material.IsKeywordEnabled("_SPECULAR_SETUP"), Is.False);
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

        private static UnityEngine.Material CreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StandardLitShaderAssetPath);
            Assert.That(shader, Is.Not.Null, $"Expected shader asset at '{StandardLitShaderAssetPath}'.");
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

        private static string GetShaderSourcePath()
        {
            return GetPackageFilePath("Shaders", "Material", "StandardLit.shader");
        }

        private static string GetIndirectDiffuseSourcePath()
        {
            return GetPackageFilePath("Shaders", "Material", "ShaderPass", "IndirectDiffuse.hlsl");
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string packageRoot in packageRoots)
            {
                string fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
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
