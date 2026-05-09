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
        public void ClusteredLightListGen_UsesParallelCompactionInSphericalIntersectionTests()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            // DXC is required for wave intrinsics.
            Assert.That(source, Does.Contain("#pragma use_dxc"));

            // Groupshared running count for cross-pass accumulation.
            Assert.That(source, Does.Contain("groupshared uint convergeLightCount;"));

            // Wave intrinsic compaction primitives.
            Assert.That(source, Does.Contain("WavePrefixCountBits(isValid)"));
            Assert.That(source, Does.Contain("WaveActiveCountBits(isValid)"));

            // Cross-wave prefix sum via ldsTilePassList scratch.
            Assert.That(source, Does.Contain("ldsTilePassList[waveIndex] = waveValidCount;"));
            Assert.That(source, Does.Contain("ldsTilePassList[wavesPerGroup] = convergeLightCount;"));

            // In-place compacted write back to coarseList.
            Assert.That(source, Does.Contain("coarseList[passStart + waveOffset + wavePrefixCount] = srcVal;"));

            // Must NOT contain the old serial thread-0 compaction.
            Assert.That(source, Does.Not.Contain("// to greedy to double buffer coarseList lds"));
            Assert.That(source, Does.Not.Contain("coarseList[offs++] = coarseList[l];"));
        }

        [Test]
        public void ClusteredLightListGen_UsesGroupLevelPrefixSumForGlobalAllocation()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            // Groupshared variable for the single group-level allocation base.
            Assert.That(source, Does.Contain("groupshared uint groupAllocationBase;"));

            // Wave prefix sum for per-cluster allocation sizes.
            Assert.That(source, Does.Contain("WavePrefixSum(myAlloc)"));
            Assert.That(source, Does.Contain("WaveActiveSum(myAlloc)"));

            // Cross-wave scan publishes wave totals to ldsTilePassList scratch.
            Assert.That(source, Does.Contain("ldsTilePassList[waveIdx] = waveTotalAlloc;"));

            // Single global atomic per group instead of per cluster.
            Assert.That(source, Does.Contain("InterlockedAdd(g_LayeredSingleIdxBuffer[0], acc, groupAllocationBase);"));

            // Each thread derives start from group base + wave offset + wave prefix.
            Assert.That(source, Does.Contain("start = groupAllocationBase + waveOffset + wavePrefixAlloc;"));

            // Must NOT contain the old per-cluster InterlockedAdd pattern.
            Assert.That(source, Does.Not.Contain("InterlockedAdd(g_LayeredSingleIdxBuffer[0], (uint) iSpaceAvail, start);"));
        }

        [Test]
        public void ClusteredLightListGen_PreComputesClusterCornerVertices()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            // Cluster corners pre-computed once before the light loop.
            Assert.That(source, Does.Contain("float4 clusterVerts[8];"));
            Assert.That(source, Does.Contain("ClusterIdxToZ(i, suggestedBase)"));
            Assert.That(source, Does.Contain("ClusterIdxToZ(i+1, suggestedBase)"));
            Assert.That(source, Does.Contain("float4(GetViewPosFromLinDepth( float2(x, y), z, eyeIndex), 1.0)"));

            // CheckIntersection takes pre-computed verts instead of tile coords + depth params.
            Assert.That(source, Does.Contain("bool CheckIntersection(int l, int k, float4 clusterVerts[8])"));
            Assert.That(source, Does.Contain("dot(plane, clusterVerts[vi])"));

            // Must NOT contain the old per-light vertex recomputation inside CheckIntersection.
            Assert.That(source, Does.Not.Contain("float depthAtNearZ = ClusterIdxToZ(k, suggestedBase)"));
            Assert.That(source, Does.Not.Contain("float3 vP = GetViewPosFromLinDepth"));
            Assert.That(source, Does.Not.Contain("dot(plane, float4(vP,1.0))"));
        }

        [Test]
        public void ClusteredLightListGen_ExactEdgeTests_UsesWaveIntrinsicsInsteadOfPerLightBarriers()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            // Per-thread accumulation into local bool instead of per-edge-pair atomic.
            Assert.That(source, Does.Contain("bool threadFoundSep = false;"));
            Assert.That(source, Does.Contain("if((resh*resf)<0) threadFoundSep = true;"));

            // Wave-level reduce replaces InterlockedOr.
            Assert.That(source, Does.Contain("WaveActiveAnyTrue(threadFoundSep)"));

            // Cross-wave reduce via ldsTilePassList scratch for multi-wave groups.
            Assert.That(source, Does.Contain("ldsTilePassList[waveIdx] = waveFoundSep ? 1u : 0u;"));

            // Must NOT contain the old per-light InterlockedOr + ldsIsLightInvisible pattern.
            Assert.That(source, Does.Not.Contain("InterlockedOr(ldsIsLightInvisible, 1)"));
            Assert.That(source, Does.Not.Contain("ldsIsLightInvisible=0"));
        }

        [Test]
        public void ClusteredLightListGen_DepthRT_UsesHiZMipReadInsteadOfPerPixelLoop()        {
            var clustered = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));
            var shaderBase = File.ReadAllText(GetLightingPath("ShaderBase.hlsl"));

            // ShaderBase.hlsl must declare the HiZ texture at register t5.
            Assert.That(shaderBase, Does.Contain("Texture2D g_depth_tex_hiz : register( t5 )"));

            // Non-MSAA path: thread 0 reads one HiZ mip texel instead of looping over pixels.
            Assert.That(clustered, Does.Contain("const uint hizMip = log2TileSize;"));
            Assert.That(clustered, Does.Contain("g_depth_tex_hiz.mips[hizMip][tileIDX].x"));

            // The ldsZMax broadcast from thread 0 must still be used.
            Assert.That(clustered, Does.Contain("ldsZMax = asuint(max(linDistZ, 0.0));"));
            Assert.That(clustered, Does.Contain("linMaDist = asfloat(ldsZMax);"));

            // MSAA guard: the per-pixel loop must still be present for MSAA kernels.
            Assert.That(clustered, Does.Contain("#ifdef MSAA_ENABLED"));
            Assert.That(clustered, Does.Contain("for(int i=0; i<g_iNumSamplesMSAA; i++)"));

            // Must NOT contain the old non-MSAA per-pixel half-texel-centre constant
            // (fracSampleCoord = float2(0.5,0.5)) — that was only in the removed non-MSAA loop.
            Assert.That(clustered, Does.Not.Contain("fracSampleCoord = float2(0.5,0.5)"));
        }

        [Test]
        public void ClusteredLightListGen_ZBinning_DepthSortAndPerClusterRangePruning()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            // groupshared Z-bin table and group-wide ceiling declared.
            Assert.That(source, Does.Contain("groupshared uint zBinEnd[MAX_NR_CLUSTER_SLICES];"));
            Assert.That(source, Does.Contain("groupshared uint groupMaxBinEnd;"));

            // Depth-sort encoding: minCluster packed into coarseList sort key.
            Assert.That(source, Does.Contain("coarseList[l] = (minC << 12) | (uint)lightIdx;"));

            // Sort-key bits stripped after SORTLIST to restore plain light indices.
            Assert.That(source, Does.Contain("coarseList[l] &= 0xFFFu;"));

            // Thread 0 serial scan builds zBinEnd and groupMaxBinEnd.
            Assert.That(source, Does.Contain("zBinEnd[c] = (uint)sl;"));
            Assert.That(source, Does.Contain("groupMaxBinEnd = (nrClusters > 0) ? zBinEnd[nrClusters - 1] : 0u;"));

            // Counting loop uses per-cluster Z-bin end.
            Assert.That(source, Does.Contain("const int binEnd = (int)zBinEnd[i];"));
            Assert.That(source, Does.Contain("for(int l=0; l<binEnd; l++)"));

            // Fine-cull outer loop bounded by groupMaxBinEnd; inner body gated by fineCullEnd.
            Assert.That(source, Does.Contain("const int cullRange   = (int)groupMaxBinEnd;"));
            Assert.That(source, Does.Contain("for(int ll=0; ll<cullRange; ll+=4)"));
            Assert.That(source, Does.Contain("if(l < fineCullEnd && offs<(start+iSpaceAvail)"));

            // Must NOT contain the old unbounded counting loop over all coarse lights.
            Assert.That(source, Does.Not.Contain("for(int l=0; l<iNrCoarseLights; l++)"));
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
        public void SFiniteLightBound_PackedLayout_StrideIs56AndScaleRadiusInWComponents()
        {
            // Stride must be unchanged at 56 bytes after packing scaleXY/radius into w.
            Assert.That(VividLightData.SFiniteLightBound.Stride, Is.EqualTo(56));

            // HLSL struct uses float4 for boxAxisX (w=scaleXY) and boxAxisY (w=radius).
            var source = File.ReadAllText(GetLightingPath("LightLoop.cs.hlsl"));
            Assert.That(source, Does.Contain("float4 boxAxisX;   // xyz = axis, w = scaleXY"));
            Assert.That(source, Does.Contain("float4 boxAxisY;   // xyz = axis, w = radius"));
            Assert.That(source, Does.Not.Contain("float scaleXY;"));
            Assert.That(source, Does.Not.Contain("float radius;"));

            // Accessors read from packed w components.
            Assert.That(source, Does.Contain("return value.boxAxisX.w;"));
            Assert.That(source, Does.Contain("return value.boxAxisY.w;"));

            // Shader call sites must use packed fields, not loose fields.
            var bigtile = File.ReadAllText(GetLightingPath("lightlistbuild-bigtile.compute"));
            Assert.That(bigtile, Does.Contain("lgtDat.boxAxisX.w"));
            Assert.That(bigtile, Does.Contain("lgtDat.boxAxisY.w"));
            Assert.That(bigtile, Does.Not.Contain("lgtDat.scaleXY"));
            Assert.That(bigtile, Does.Not.Contain("lgtDat.radius"));

            var clustered = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));
            Assert.That(clustered, Does.Contain("lgtDat.boxAxisX.w"));
            Assert.That(clustered, Does.Contain("lgtDat.boxAxisY.w"));
            Assert.That(clustered, Does.Not.Contain("lgtDat.scaleXY"));
            Assert.That(clustered, Does.Not.Contain("lgtDat.radius"));

            var scrbound = File.ReadAllText(GetLightingPath("scrbound.compute"));
            Assert.That(scrbound, Does.Contain("cullData.boxAxisX.w"));
            Assert.That(scrbound, Does.Contain("cullData.boxAxisY.w"));
            Assert.That(scrbound, Does.Not.Contain("cullData.scaleXY"));
            Assert.That(scrbound, Does.Not.Contain("cullData.radius"));
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
            Assert.That(Marshal.SizeOf(lightListType), Is.EqualTo(564));
        }

        [Test]
        public void ClusteredLightListGen_AppliesAreaLightIndexShiftForSeparateAreaBuffer()
        {
            var lightLoopSource = File.ReadAllText(GetLightingPath("LightLoop.cs.hlsl"));
            var clusteredSource = File.ReadAllText(GetLightingPath("lightlistbuild-clustered.compute"));

            Assert.That(lightLoopSource, Does.Contain("uint _AreaLightIndexShift;"));
            Assert.That(clusteredSource, Does.Contain("WriteShiftIndex(t, LIGHTCATEGORY_AREA, _AreaLightIndexShift);"));
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

        [Test]
        public void BigTileLightListGen_UsesWaveCompactionInsteadOfBitonicSort()
        {
            var source = File.ReadAllText(GetLightingPath("lightlistbuild-bigtile.compute"));

            // Groupshared compaction state declared.
            Assert.That(source, Does.Contain("groupshared uint bigtileConvergeCount;"));
            Assert.That(source, Does.Contain("groupshared uint bigtilePassScratch[8];"));

            // Wave intrinsics used for per-pass valid-count accumulation.
            Assert.That(source, Does.Contain("WavePrefixCountBits(isValid)"));
            Assert.That(source, Does.Contain("WaveActiveCountBits(isValid)"));
            Assert.That(source, Does.Contain("WaveIsFirstLane()"));

            // Final compacted count written back to iNrCoarseLights.
            Assert.That(source, Does.Contain("iNrCoarseLights = (int)bigtileConvergeCount;"));

            // Must NOT contain the old bitonic sort call.
            Assert.That(source, Does.Not.Contain("SORTLIST(lightsListLDS"));
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

            var resourcePath = field.GetCustomAttribute<VividResourcePathAttribute>();

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
