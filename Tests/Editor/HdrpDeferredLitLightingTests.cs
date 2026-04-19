using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using ResourcePathAttribute = VividRP.Runtime.ResourcePathAttribute;

namespace VividRP.Editor.Tests
{
    public sealed class HdrpDeferredLitLightingTests
    {
        [Test]
        public void HdrpLitLightingInclude_ContainsHdrpInspiredDirectLightingBuildingBlocks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));

            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl\""));
            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/LTCAreaLight.hlsl\""));
            Assert.That(source, Does.Contain("return DisneyDiffuse("));
            Assert.That(source, Does.Contain("return DV_SmithJointGGX("));
            Assert.That(source, Does.Contain("return D_Charlie("));
            Assert.That(source, Does.Contain("struct VividCBSDF"));
            Assert.That(source, Does.Contain("struct VividDirectLighting"));
            Assert.That(source, Does.Contain("struct VividIndirectLighting"));
            Assert.That(source, Does.Contain("struct VividAggregateLighting"));
            Assert.That(source, Does.Contain("struct VividLightLoopOutput"));
            Assert.That(source, Does.Contain("struct VividPreLightData"));
            Assert.That(source, Does.Contain("GetVividPreLightData("));
            Assert.That(source, Does.Contain("InitVividPreLightData("));
            Assert.That(source, Does.Contain("EvaluateBSDF("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Directional("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Punctual("));
            Assert.That(source, Does.Contain("EvaluateBSDF_Area("));
            Assert.That(source, Does.Contain("EvaluateDirectional("));
            Assert.That(source, Does.Contain("BuildVividHDRPLitBSDFData"));
            Assert.That(source, Does.Contain("VividCBSDF cbsdf = (VividCBSDF)0;"));
            Assert.That(source, Does.Contain("VividIndirectLighting lighting = (VividIndirectLighting)0;"));
            Assert.That(source, Does.Contain("if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)"));
            Assert.That(source, Does.Not.Contain("? EvaluateVividFabricBSDF("));
            Assert.That(source, Does.Not.Contain("? EvaluateVividFabricIndirectBSDF("));
            Assert.That(source, Does.Contain("EvaluateVividLitDirectLight"));
            Assert.That(source, Does.Contain("EvaluateDirectionalLight"));
            Assert.That(source, Does.Contain("EvaluatePunctualLight"));
            Assert.That(source, Does.Contain("EvaluateAreaLightIntensity("));
            Assert.That(source, Does.Contain("ApplyRectangularAreaLightBarnDoor("));
            Assert.That(source, Does.Contain("areaLight.cosBarnDoorAngle"));
            Assert.That(source, Does.Contain("areaLight.barnDoorLength"));
            Assert.That(source, Does.Contain("AccumulateDirectLighting("));
            Assert.That(source, Does.Contain("FinalizeVividSpecularLighting("));
            Assert.That(source, Does.Contain("surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC"));
            Assert.That(source, Does.Contain("float EvaluatePunctualLightDistanceAttenuation"));
            Assert.That(source, Does.Contain("float distanceAttenuation = rcp(max(distanceSquared, 1e-6));"));
            Assert.That(source, Does.Contain("float rangeAttenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);"));
            Assert.That(source, Does.Contain("PillowWindowing("));
            Assert.That(source, Does.Contain("SampleLtcMatrix("));
        }

        [Test]
        public void LightingInclude_PacksAreaBarnDoorParameters()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "Lighting.hlsl"));

            Assert.That(source, Does.Contain("float cosBarnDoorAngle;"));
            Assert.That(source, Does.Contain("float barnDoorLength;"));
            Assert.That(source, Does.Not.Contain("float2 padding;"));
        }

        [Test]
        public void HdrpLitLightingInclude_ContainsHdrpInspiredImageBasedLightingBuildingBlocks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/PreIntegratedFGD.hlsl\""));
            Assert.That(source, Does.Contain("_VividSkyIBLCubemap"));
            Assert.That(source, Does.Contain("bsdfData.roughness =  ClampRoughnessForAnalyticalLights(surfaceData.linearRoughness);"));
            Assert.That(source, Does.Contain("float sigma = RoughnessToVariance(bsdfData.roughness);"));
            Assert.That(source, Does.Contain("return Luminance(color);"));
            Assert.That(source, Does.Contain("EvaluateBSDF_Env("));
            Assert.That(source, Does.Contain("ApplyVividSpecularEnergyCompensation("));
            Assert.That(source, Does.Contain("EvaluateVividHdrpLitIndirectLight"));
            Assert.That(source, Does.Contain("EvaluateVividFabricIndirectLight"));
            Assert.That(source, Does.Contain("EvaluateIndirectLighting"));
            Assert.That(source, Does.Contain("AccumulateIndirectLighting("));
            Assert.That(source, Does.Contain("PostEvaluateBSDF("));
            Assert.That(source, Does.Contain("GetSpecularDominantDir"));
        }

        [Test]
        public void PreIntegratedFGDInclude_ContainsHdrpInspiredLutSamplingFunctions()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "PreIntegratedFGD.hlsl"));

            Assert.That(source, Does.Contain("GetPreIntegratedFGDGGXAndDisneyDiffuse"));
            Assert.That(source, Does.Contain("GetPreIntegratedFGDCharlieAndFabricLambert"));
            Assert.That(source, Does.Contain("_PreIntegratedFGD_GGXDisneyDiffuse"));
            Assert.That(source, Does.Contain("_PreIntegratedFGD_CharlieAndFabric"));
            Assert.That(source, Does.Contain("VIVID_FGD_TEXTURE_RESOLUTION 64"));
        }

        [Test]
        public void LTCAreaLightInclude_ContainsHdrpInspiredLtcSamplingFunctions()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "LTCAreaLight.hlsl"));

            Assert.That(source, Does.Contain("_LtcData"));
            Assert.That(source, Does.Contain("SampleLtcMatrix("));
            Assert.That(source, Does.Contain("EvaluateLTC_Area("));
            Assert.That(source, Does.Contain("PolygonIrradiance("));
            Assert.That(source, Does.Contain("ComputeLineWidthFactor("));
        }

        [Test]
        public void PreIntegratedFGDShaders_UseHdrpStyleLutIntegrators()
        {
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PreIntegratedFGD_GGXDisneyDiffuse.shader")),
                Does.Contain("IntegrateGGXAndDisneyDiffuseFGD"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PreIntegratedFGD_GGXDisneyDiffuse.shader")),
                Does.Contain("RemapHalfTexelCoordTo01"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PreIntegratedFGD_CharlieFabricLambert.shader")),
                Does.Contain("IntegrateCharlieAndFabricLambertFGD"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "PreIntegratedFGD_CharlieFabricLambert.shader")),
                Does.Contain("SampleConeStrata"));
        }

        [Test]
        public void PreIntegratedFGDRuntimeHelper_UsesRenderGraphTextureDescriptors_InsteadOfRenderTextureState()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "VividPreIntegratedFGD.cs"));

            Assert.That(source, Does.Contain("RenderGraphTexture CreateTexture"));
            Assert.That(source, Does.Contain("RenderGraphTextureDesc"));
            Assert.That(source, Does.Contain("CreatePersistentTexture"));
            Assert.That(source, Does.Contain("Graphics.ExecuteCommandBuffer"));
            Assert.That(source, Does.Not.Contain("RenderTexture m_"));
            Assert.That(source, Does.Not.Contain("SetGlobalTexture"));
        }

        [Test]
        public void PreIntegratedFGDPreparePass_ExposesReusablePreparedLutOutputs()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "PreIntegratedFGDPreparePass.cs"));

            Assert.That(source, Does.Contain("PreIntegratedFGD_GGXDisneyDiffuse"));
            Assert.That(source, Does.Contain("PreIntegratedFGD_CharlieAndFabric"));
            Assert.That(source, Does.Contain("PassOwnedOverrideable"));
            Assert.That(source, Does.Contain("PassRecorder.ImportTexture"));
        }

        [Test]
        public void DeferredLightingPasses_UseSharedHdrpLitLightingInclude()
        {
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));
        }

        [Test]
        public void DeferredLightingShaders_UseSharedHdrpStyleLightingEvaluation()
        {
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("EvaluateBSDF_Env("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("EvaluateBSDF_Env("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("EvaluateBSDF_Env("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("AccumulateDirectLighting("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("AccumulateDirectLighting("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("AccumulateDirectLighting("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "ShaderPass", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("PostEvaluateBSDF("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("PostEvaluateBSDF("));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("PostEvaluateBSDF("));
        }

        [Test]
        public void LightGridPass_SourceAppliesSkyAttenuationBeforeUploadingDirectionalLights()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "LightGridPass.cs"));

            Assert.That(source, Does.Contain("UpdateDirectionalLightUploadData(lightData, camera);"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyAtmosphericAttenuation.TryCreate(camera, out var attenuationContext)"));
            Assert.That(source, Does.Contain("PhysicallyBasedSkyAtmosphericAttenuation.Evaluate("));
            Assert.That(source, Does.Contain("ShouldDirectionalLightInteractWithSky(light)"));
        }

        [Test]
        public void LightGridPass_SourceUsesCombinedFiniteLightCullingForPunctualAndAreaLights()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "LightGridPass.cs"));

            Assert.That(source, Does.Contain("m_FiniteLightCount = m_PunctualLightCount + m_AreaLightCount;"));
            Assert.That(source, Does.Contain("lightData.UpdateFiniteLightClusteredCullData(worldToViewMatrix);"));
            Assert.That(source, Does.Contain("UpdateFiniteLightUploadData(lightData);"));
            Assert.That(source, Does.Not.Contain("UploadAreaLightGridData();"));
            Assert.That(source, Does.Not.Contain("UpdateAreaLightScreenSpaceBounds(lightData, camera);"));
        }

        [Test]
        public void VividRPCoreResources_DeclarePreIntegratedFGDShaders()
        {
            AssertResourcePath(
                nameof(VividRPCoreResources.PreIntegratedFGDGGXDisneyDiffuseShader),
                "Shaders/Core/Private/PreIntegratedFGD_GGXDisneyDiffuse");
            AssertResourcePath(
                nameof(VividRPCoreResources.PreIntegratedFGDCharlieFabricLambertShader),
                "Shaders/Core/Private/PreIntegratedFGD_CharlieFabricLambert");
        }

        private static void AssertResourcePath(string fieldName, string expectedPath)
        {
            var field = typeof(VividRPCoreResources).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo(expectedPath));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var vividPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "VividRP",
                Path.Combine(relativeParts)));

            if (File.Exists(vividPath))
                return vividPath;

            var legacyPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                Path.Combine(relativeParts)));

            Assert.That(File.Exists(legacyPath), Is.True, $"Expected source file at '{vividPath}' or '{legacyPath}'.");
            return legacyPath;
        }
    }
}
