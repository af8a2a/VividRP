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
        public void ClusteredLightCullCompute_DeclaresExpectedKernelsAndCoarseCullingInputs()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#pragma kernel ClearClusterLightCounter"));
            Assert.That(source, Does.Contain("#pragma kernel BuildClusteredLightList"));
            Assert.That(source, Does.Contain("StructuredBuffer<PunctualLightCullData> _PunctualLightCullData;"));
            Assert.That(source, Does.Contain("StructuredBuffer<uint2> _ClusterCoarseLightRanges;"));
            Assert.That(source, Does.Contain("StructuredBuffer<PunctualLightCoarseRecord> _ClusterCoarseLightRecords;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint2> _ClusterLightGrid;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _ClusterLightIndices;"));
            Assert.That(source, Does.Contain("bool SphereIntersectsCluster(float3 sphereCenter, float sphereRadius, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("bool SpotConeIntersectsCluster(PunctualLightCullData punctualLightCullData, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("bool PunctualLightIntersectsCluster(PunctualLightCullData punctualLightCullData, float3 boundsMin, float3 boundsMax)"));
            Assert.That(source, Does.Contain("PunctualLightCoarseRecord coarseLightRecord = _ClusterCoarseLightRecords[coarseLightRange.x + coarseLightOffset];"));
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
