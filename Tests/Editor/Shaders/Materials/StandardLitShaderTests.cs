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
        private const string StandardLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit/StandardLit.shader";
        private const string StandardLayeredLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLayeredLit/StandardLayeredLit.shader";

        [Test]
        public void StandardLitShader_DeclaresRequiredPasses_ForDrawObjectPass()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain($"Name \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain($"\"LightMode\" = \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain("Name \"VividGBuffer\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividGBuffer\""));
            Assert.That(shaderSource, Does.Contain("Name \"VividGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("Name \"SRPDefaultUnlit\""));
        }

        [Test]
        public void StandardLitShader_DoesNotDeclareVirtualTextureGBufferPasses_ForNonSvtMaterials()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Not.Contain("Name \"VividVTGBuffer\""));
            Assert.That(shaderSource, Does.Not.Contain("\"LightMode\" = \"VividVTGBuffer\""));
            Assert.That(shaderSource, Does.Not.Contain("#define VIVID_VT_ENABLE_FEEDBACK_RW 1"));
        }

        [Test]
        public void StandardLitShader_PreDepthPassUsesDepthOnlyPass_WithoutGBufferVariants()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            string preDepthPass = ExtractPassBlock(shaderSource, RenderGraphRenderListDesc.PreDepthShaderTagName);

            Assert.That(preDepthPass, Does.Contain("StandardLitDepthOnlyPass.hlsl"));
            Assert.That(preDepthPass, Does.Not.Contain("StandardLitGBufferPass.hlsl"));
            Assert.That(preDepthPass, Does.Contain("#pragma shader_feature_local_fragment _ALPHATEST_ON"));
            Assert.That(preDepthPass, Does.Contain("#pragma shader_feature_local_fragment _OPACITYMAP"));
            AssertNoGBufferOnlyPreDepthVariants(preDepthPass);
        }

        [Test]
        public void StandardLitShader_DeclaresVividShaderPassContractMacros_ForReusablePasses()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            AssertPassContainsContract(
                shaderSource,
                RenderGraphRenderListDesc.PreDepthShaderTagName,
                "VIVIDRP_SHADERPASS_DEPTH_ONLY",
                "VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0",
                "VIVIDRP_VARYINGS_NEED_TEXCOORD0");
            AssertPassContainsContract(
                shaderSource,
                "ShadowCaster",
                "VIVIDRP_SHADERPASS_SHADOW_CASTER",
                "VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0",
                "VIVIDRP_VARYINGS_NEED_TEXCOORD0");
            AssertPassContainsContract(
                shaderSource,
                "VividGBuffer",
                "VIVIDRP_SHADERPASS_GBUFFER",
                "VIVIDRP_ATTRIBUTES_NEED_NORMAL",
                "VIVIDRP_ATTRIBUTES_NEED_TANGENT",
                "VIVIDRP_VARYINGS_NEED_POSITION_WS",
                "VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD");
            AssertPassContainsContract(
                shaderSource,
                "VividGBufferGPUDrivenDecal",
                "VIVIDRP_SHADERPASS_GBUFFER",
                "VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER",
                "VIVIDRP_VARYINGS_NEED_POSITION_WS");
            AssertPassContainsContract(
                shaderSource,
                "Meta",
                "VIVIDRP_SHADERPASS_META",
                "VIVIDRP_ATTRIBUTES_NEED_TEXCOORD2",
                "VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS");
            AssertPassContainsContract(
                shaderSource,
                "SRPDefaultUnlit",
                "VIVIDRP_SHADERPASS_DEBUG",
                "VIVIDRP_ATTRIBUTES_NEED_TANGENT",
                "VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD");
            AssertPassContainsContract(
                shaderSource,
                "MotionVectors",
                "VIVIDRP_SHADERPASS_MOTION_VECTORS",
                "VIVIDRP_ATTRIBUTES_NEED_PREVIOUS_POSITION",
                "VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS");
        }

        [Test]
        public void StandardLayeredLitShader_DeclaresVirtualTextureGBufferPasses_ForSvtBaseColor()
        {
            string shaderSource = File.ReadAllText(GetStandardLayeredLitShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("Shader \"VividRP/Material/StandardLayeredLit\""));
            Assert.That(shaderSource, Does.Contain("_UseVirtualTextureBaseColor"));
            Assert.That(shaderSource, Does.Contain("Name \"VividVTGBuffer\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividVTGBuffer\""));
            Assert.That(shaderSource, Does.Contain("Name \"VividVTGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"VividVTGBufferGPUDrivenDecal\""));
            Assert.That(shaderSource, Does.Contain("#pragma target 5.0"));
            Assert.That(shaderSource, Does.Contain("#pragma require randomwrite"));
            Assert.That(shaderSource, Does.Contain("#pragma shader_feature_local_fragment _VIRTUAL_TEXTURE_BASE_COLOR"));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_STANDARD_LIT_VIRTUAL_TEXTURE 1"));
            Assert.That(shaderSource, Does.Contain("#define VIVID_VT_ENABLE_FEEDBACK_RW 1"));
            Assert.That(shaderSource, Does.Contain("CustomEditor \"VividRP.Editor.StandardLayeredLitShaderGUI\""));
        }

        [Test]
        public void StandardLayeredLitShader_PreDepthPassUsesDepthOnlyPass_WithoutGBufferVariants()
        {
            string shaderSource = File.ReadAllText(GetStandardLayeredLitShaderSourcePath());

            string preDepthPass = ExtractPassBlock(shaderSource, RenderGraphRenderListDesc.PreDepthShaderTagName);

            Assert.That(preDepthPass, Does.Contain("StandardLitDepthOnlyPass.hlsl"));
            Assert.That(preDepthPass, Does.Not.Contain("StandardLitGBufferPass.hlsl"));
            Assert.That(preDepthPass, Does.Contain("#pragma shader_feature_local_fragment _ALPHATEST_ON"));
            Assert.That(preDepthPass, Does.Contain("#pragma shader_feature_local_fragment _OPACITYMAP"));
            AssertNoGBufferOnlyPreDepthVariants(preDepthPass);
        }

        [Test]
        public void StandardLayeredLitShader_DeclaresVividShaderPassContractMacros_ForSharedStandardLitPasses()
        {
            string shaderSource = File.ReadAllText(GetStandardLayeredLitShaderSourcePath());

            AssertPassContainsContract(
                shaderSource,
                "VividVTGBuffer",
                "VIVIDRP_SHADERPASS_GBUFFER",
                "VIVIDRP_STANDARD_LIT_VIRTUAL_TEXTURE",
                "VIVID_VT_ENABLE_FEEDBACK_RW",
                "VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD");
            AssertPassContainsContract(
                shaderSource,
                "VividVTGBufferGPUDrivenDecal",
                "VIVIDRP_SHADERPASS_GBUFFER",
                "VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER",
                "VIVIDRP_VARYINGS_NEED_POSITION_WS");
            AssertPassContainsContract(
                shaderSource,
                "MotionVectors",
                "VIVIDRP_SHADERPASS_MOTION_VECTORS",
                "VIVIDRP_ATTRIBUTES_NEED_PREVIOUS_POSITION",
                "VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS");
        }

        [Test]
        public void StandardLitShader_DeclaresIndirectDxrPass_ForIndirectDiffuseHitShaders()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("Name \"IndirectDXR\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"IndirectDXR\""));
            Assert.That(shaderSource, Does.Contain("#pragma raytracing surface_shader"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile _ INSTANCING_ON"));
            Assert.That(shaderSource, Does.Contain("#pragma shader_feature_local_raytracing _ALPHATEST_ON"));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME StandardLitIndirectDiffuseClosestHit"));
            Assert.That(shaderSource, Does.Contain("#define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME StandardLitIndirectDiffuseAnyHit"));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/IndirectDiffuse.hlsl\""));
        }

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
        public void StandardLitShader_DeclaresAdaptiveProbeVolumeVariants_ForDeferredAndIndirectDxrPasses()
        {
            string shaderSource = File.ReadAllText(GetShaderSourcePath());

            Assert.That(shaderSource, Does.Contain("#pragma instancing_options renderinglayer"));
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
            Assert.That(source, Does.Contain("surfaceData.builtinData = BuildVividBuiltinData("));
            Assert.That(source, Does.Contain("lightingRadiance += surfaceData.builtinData.bakeDiffuseLighting * diffuseColor * INV_PI;"));
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
        public void StandardLitShader_ExposesOptInThinWalledTransmissionProperties()
        {
            UnityEngine.Material material = CreateMaterial();
            try
            {
                Assert.That(
                    material.HasProperty("_ThinWalledTransmission"),
                    Is.True);
                Assert.That(material.HasProperty("_TransmissionWeight"), Is.True);
                Assert.That(material.HasProperty("_TransmissionColor"), Is.True);
                Assert.That(material.HasProperty("_SpecularIOR"), Is.True);
                Assert.That(
                    material.GetFloat("_ThinWalledTransmission"),
                    Is.Zero);
                Assert.That(material.GetFloat("_TransmissionWeight"), Is.Zero);
                Assert.That(
                    material.GetColor("_TransmissionColor"),
                    Is.EqualTo(Color.white));
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

        private static string GetShaderSourcePath()
        {
            return GetPackageFilePath("Shaders", "Material", "StandardLit", "StandardLit.shader");
        }

        private static string GetIndirectDiffuseSourcePath()
        {
            return GetPackageFilePath("Shaders", "Material", "ShaderPass", "IndirectDiffuse.hlsl");
        }

        private static string GetStandardLayeredLitShaderSourcePath()
        {
            return GetPackageFilePath("Shaders", "Material", "StandardLayeredLit", "StandardLayeredLit.shader");
        }

        private static string ExtractPassBlock(string shaderSource, string passName)
        {
            var match = Regex.Match(
                shaderSource,
                $@"Pass\s*\{{(?:(?!Pass\s*\{{).)*Name\s+""{Regex.Escape(passName)}""(?:(?!Pass\s*\{{).)*\}}",
                RegexOptions.Singleline);

            Assert.That(match.Success, Is.True, $"Expected pass '{passName}' in shader source.");
            return match.Value;
        }

        private static void AssertPassContainsContract(string shaderSource, string passName, params string[] expectedTokens)
        {
            string passBlock = ExtractPassBlock(shaderSource, passName);

            foreach (var expectedToken in expectedTokens)
                Assert.That(passBlock, Does.Contain(expectedToken), $"Expected pass '{passName}' to declare '{expectedToken}'.");
        }

        private static void AssertNoGBufferOnlyPreDepthVariants(string preDepthPass)
        {
            Assert.That(preDepthPass, Does.Not.Contain("LIGHTMAP_ON"));
            Assert.That(preDepthPass, Does.Not.Contain("DIRLIGHTMAP_COMBINED"));
            Assert.That(preDepthPass, Does.Not.Contain("_NORMALMAP"));
            Assert.That(preDepthPass, Does.Not.Contain("_METALLICSPECGLOSSMAP"));
            Assert.That(preDepthPass, Does.Not.Contain("_ROUGHNESSMAP"));
            Assert.That(preDepthPass, Does.Not.Contain("_OCCLUSIONMAP"));
            Assert.That(preDepthPass, Does.Not.Contain("_EMISSION"));
            Assert.That(preDepthPass, Does.Not.Contain("_CLEARCOAT"));
            Assert.That(preDepthPass, Does.Not.Contain("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "Custom_URP"),
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
