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

            Assert.That(source, Does.Contain("VividDisneyDiffuse"));
            Assert.That(source, Does.Contain("VividDV_SmithJointGGX"));
            Assert.That(source, Does.Contain("VividD_Charlie"));
            Assert.That(source, Does.Contain("BuildVividHdrpLitBSDFData"));
            Assert.That(source, Does.Contain("EvaluateVividHdrpLitDirectLight"));
            Assert.That(source, Does.Contain("EvaluateDirectionalLight"));
            Assert.That(source, Does.Contain("EvaluatePunctualLight"));
        }

        [Test]
        public void HdrpLitLightingInclude_ContainsHdrpInspiredImageBasedLightingBuildingBlocks()
        {
            var source = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "HdrpLitLighting.hlsl"));

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/PreIntegratedFGD.hlsl\""));
            Assert.That(source, Does.Contain("_VividSkyIBLCubemap"));
            Assert.That(source, Does.Contain("EvaluateVividHdrpLitIndirectLight"));
            Assert.That(source, Does.Contain("EvaluateVividFabricIndirectLight"));
            Assert.That(source, Does.Contain("EvaluateIndirectLighting"));
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
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));

            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl\""));
        }

        [Test]
        public void DeferredLightingShaders_EvaluateSharedIndirectLighting()
        {
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "SimpleDeferredLitPass.hlsl")),
                Does.Contain("EvaluateIndirectLighting"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredDirectionalLightingIndirectPass.hlsl")),
                Does.Contain("EvaluateIndirectLighting"));
            Assert.That(
                File.ReadAllText(GetPackageFilePath("Shaders", "Material", "DeferredLit.compute")),
                Does.Contain("EvaluateIndirectLighting"));
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
