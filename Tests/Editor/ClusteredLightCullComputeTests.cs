using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class ClusteredLightCullComputeTests
    {
        [Test]
        public void ClusteredLightCullCompute_DeclaresExpectedKernelsAndGpuCoarseCullingInputs()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel ClearClusterLightCounter"));
            Assert.That(source, Does.Contain("#pragma kernel CountClusterBigTileLights"));
            Assert.That(source, Does.Contain("#pragma kernel BuildClusterBigTileLightRanges"));
            Assert.That(source, Does.Contain("#pragma kernel BuildClusterBigTileLightList"));
            Assert.That(source, Does.Contain("#pragma kernel BuildClusteredLightList"));
            Assert.That(source, Does.Contain("StructuredBuffer<PunctualLightCullData> _PunctualLightCullData;"));
            Assert.That(source, Does.Contain("StructuredBuffer<PunctualLightScreenSpaceBounds> _PunctualLightScreenSpaceBounds;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ClusterBigTileLightCounts;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint2> _ClusterBigTileLightRanges;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ClusterBigTileLightIndices;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint2> _ClusterLightGrid;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ClusterLightIndices;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ClusterLightListStats;"));
            Assert.That(source, Does.Contain("int bigTileMinX;"));
            Assert.That(source, Does.Contain("int bigTileMaxX;"));
            Assert.That(source, Does.Contain("int bigTileMinY;"));
            Assert.That(source, Does.Contain("int bigTileMaxY;"));
            Assert.That(source, Does.Contain("bool SphereIntersectsCluster(float3 sphereCenter, float sphereRadius, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("bool SpotConeIntersectsCluster(PunctualLightCullData punctualLightCullData, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("bool PunctualLightIntersectsCluster(PunctualLightCullData punctualLightCullData, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("void CountClusterBigTileLights(uint3 dispatchThreadId : SV_DispatchThreadID)"));
            Assert.That(source, Does.Contain("void BuildClusterBigTileLightRanges(uint3 dispatchThreadId : SV_DispatchThreadID)"));
            Assert.That(source, Does.Contain("void BuildClusterBigTileLightList(uint3 dispatchThreadId : SV_DispatchThreadID)"));
            Assert.That(source, Does.Contain("_ClusterBigTileLightCounts[bigTileIndex] = bigTileLightCount;"));
            Assert.That(source, Does.Contain("_ClusterBigTileLightRanges[bigTileIndex] = uint2(runningOffset, bigTileLightCount);"));
            Assert.That(source, Does.Contain("(int)dispatchThreadId.x < screenSpaceBounds.bigTileMinX"));
            Assert.That(source, Does.Contain("(int)dispatchThreadId.y > screenSpaceBounds.bigTileMaxY"));
            Assert.That(source, Does.Not.Contain("InterlockedAdd(_ClusterAllocationCounter[0], bigTileLightCount, bigTileStart);"));
            Assert.That(source, Does.Contain("uint2 bigTileLightRange = _ClusterBigTileLightRanges[GetBigTileIndex(bigTileCoord)];"));
            Assert.That(source, Does.Contain("InterlockedMax(_ClusterLightListStats[VIVID_CLUSTER_LIGHT_LIST_STAT_MAX_CLUSTER_LIGHT_COUNT], localLightCount);"));
            Assert.That(source, Does.Contain("InterlockedAdd(_ClusterLightListStats[VIVID_CLUSTER_LIGHT_LIST_STAT_OVERFLOW_CLUSTER_COUNT], 1u);"));
            Assert.That(source, Does.Contain("InterlockedAdd(_ClusterLightListStats[VIVID_CLUSTER_LIGHT_LIST_STAT_DROPPED_LIGHT_COUNT], droppedLightCount);"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresClusteredLightCullCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.ClusteredLightCullCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Material/ClusteredLightCull"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "ClusteredLightCull.compute"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
