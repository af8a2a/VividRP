using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class HdrpLightingShaderPortTests
    {
        [Test]
        public void LightingFolder_ContainsHdrpClusteredLightBuildShaders()
        {
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "LightLoop.cs.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderBase.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderConfig.cs.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ShaderVariablesGlobalLightLoop.hlsl")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "scrbound.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-bigtile.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-clustered.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "ClearLightLists.compute")), Is.True);
            Assert.That(File.Exists(GetPackagePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-clearatomic.compute")), Is.True);
        }

        [Test]
        public void HdrpClusteredLightingShaders_UseLocalLightingIncludes()
        {
            AssertLocalIncludes(
                "lightlistbuild-bigtile.compute",
                "ShaderConfig.cs.hlsl",
                "ShaderVariablesGlobalLightLoop.hlsl",
                "LightLoop.cs.hlsl",
                "LightingConvexHullUtils.hlsl",
                "SortingComputeUtils.hlsl",
                "LightCullUtils.hlsl");

            AssertLocalIncludes(
                "lightlistbuild-clustered.compute",
                "ShaderConfig.cs.hlsl",
                "ShaderVariablesGlobalLightLoop.hlsl",
                "ShaderBase.hlsl",
                "LightLoop.cs.hlsl",
                "LightingConvexHullUtils.hlsl",
                "SortingComputeUtils.hlsl",
                "LightCullUtils.hlsl",
                "ClusteredUtils.hlsl");

            AssertLocalIncludes(
                "scrbound.compute",
                "ShaderConfig.cs.hlsl",
                "LightLoop.cs.hlsl",
                "LightCullUtils.hlsl");
        }

        [Test]
        public void LightCullUtils_DeclaresStereoAwareIndexHelpers()
        {
            var source = File.ReadAllText(GetLightingPath("LightCullUtils.hlsl"));

            Assert.That(source, Does.Contain("uint GenerateLightCullDataIndex(uint lightIndex, uint numVisibleLights, uint eyeIndex)"));
            Assert.That(source, Does.Contain("const uint perEyeBaseIndex = eyeIndex * numVisibleLights;"));
            Assert.That(source, Does.Contain("ScreenSpaceBoundsIndices GenerateScreenSpaceBoundsIndices(uint lightIndex, uint numVisibleLights, uint eyeIndex)"));
            Assert.That(source, Does.Contain("const uint eyeRelativeBase = eyeIndex * 2 * numVisibleLights;"));
        }

        [Test]
        public void ShaderVariablesGlobalLightLoop_DeclaresClusteredLightGlobals()
        {
            var source = File.ReadAllText(GetLightingPath("ShaderVariablesGlobalLightLoop.hlsl"));

            Assert.That(source, Does.Contain("float g_fClustScale;"));
            Assert.That(source, Does.Contain("float g_fClustBase;"));
            Assert.That(source, Does.Contain("float g_fNearPlane;"));
            Assert.That(source, Does.Contain("float g_fFarPlane;"));
            Assert.That(source, Does.Contain("int g_iLog2NumClusters;"));
            Assert.That(source, Does.Contain("uint g_isLogBaseBufferEnabled;"));
            Assert.That(source, Does.Contain("uint _NumTileClusteredX;"));
            Assert.That(source, Does.Contain("uint _NumTileClusteredY;"));
        }

        [Test]
        public void ShaderVariablesLightList_CSharpLayout_MatchesHdrpCBufferPacking()
        {
            var assembly = typeof(LightGridPass).Assembly;
            var dimensionsType = assembly.GetType("VividRP.Runtime.ShaderVariablesLightListInt2");
            var lightListType = assembly.GetType("VividRP.Runtime.ShaderVariablesLightList");

            Assert.That(dimensionsType, Is.Not.Null);
            Assert.That(lightListType, Is.Not.Null);
            Assert.That(Marshal.SizeOf(dimensionsType), Is.EqualTo(8));
            Assert.That(Marshal.SizeOf(lightListType), Is.EqualTo(560));
        }

        [Test]
        public void VividRPCoreResources_DeclaresHdrpClusteredLightBuildShaders()
        {
            AssertResourcePath(nameof(VividRPCoreResources.BuildScreenAABBCompute), "Shaders/Core/Private/Lighting/scrbound");
            AssertResourcePath(nameof(VividRPCoreResources.BuildPerBigTileLightListCompute), "Shaders/Core/Private/Lighting/lightlistbuild-bigtile");
            AssertResourcePath(nameof(VividRPCoreResources.BuildPerVoxelLightListCompute), "Shaders/Core/Private/Lighting/lightlistbuild-clustered");
            AssertResourcePath(nameof(VividRPCoreResources.ClearLightListsCompute), "Shaders/Core/Private/Lighting/ClearLightLists");
            AssertResourcePath(nameof(VividRPCoreResources.ClearClusterAtomicIndexCompute), "Shaders/Core/Private/Lighting/lightlistbuild-clearatomic");
        }

        private static void AssertLocalIncludes(string fileName, params string[] localIncludes)
        {
            var source = File.ReadAllText(GetLightingPath(fileName));

            foreach (var localInclude in localIncludes)
                Assert.That(source, Does.Contain($"#include \"{localInclude}\""));

            Assert.That(source, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/"));
            Assert.That(source, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"));
            Assert.That(source, Does.Not.Contain("Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.hlsl"));
        }

        private static void AssertResourcePath(string fieldName, string expectedPath)
        {
            var field = typeof(VividRPCoreResources).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo(expectedPath));
        }

        private static string GetLightingPath(string fileName)
        {
            return GetPackagePath("Shaders", "Core", "Private", "Lighting", fileName);
        }

        private static string GetPackagePath(params string[] parts)
        {
            var vividPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "VividRP"));
            if (Directory.Exists(vividPath))
                return Path.Combine(vividPath, Path.Combine(parts));

            var legacyPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.af8a2a.vividrp"));
            return Path.Combine(legacyPath, Path.Combine(parts));
        }
    }
}
