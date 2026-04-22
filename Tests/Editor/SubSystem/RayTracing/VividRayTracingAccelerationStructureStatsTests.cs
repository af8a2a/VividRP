using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VividRayTracingAccelerationStructureStatsTests
    {
        [TearDown]
        public void TearDown()
        {
            VividRayTracingAccelerationStructureStatsRegistry.Clear();
        }

        [Test]
        public void CullRate_ReturnsExpectedRatio_WhenCandidateCountIsAvailable()
        {
            var stats = new VividRayTracingAccelerationStructureStats(
                true,
                null,
                "Main Camera",
                CameraType.Game,
                10,
                1.0,
                VividRTASBuildMode.Automatic,
                VividRTASCullingMode.ExtendedFrustum,
                20,
                5,
                4096,
                false);

            Assert.That(stats.HasCullRate, Is.True);
            Assert.That(stats.CullRate, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void Registry_Clear_ResetsLastStats()
        {
            VividRayTracingAccelerationStructureStatsRegistry.Report(
                new VividRayTracingAccelerationStructureStats(
                    true,
                    null,
                    "Scene View",
                    CameraType.SceneView,
                    3,
                    2.0,
                    VividRTASBuildMode.Automatic,
                    VividRTASCullingMode.Sphere,
                    8,
                    4,
                    2048,
                    true));

            VividRayTracingAccelerationStructureStatsRegistry.Clear();

            Assert.That(VividRayTracingAccelerationStructureStatsRegistry.LastStats.IsAvailable, Is.False);
            Assert.That(VividRayTracingAccelerationStructureStatsRegistry.LastStats.InstanceCount, Is.EqualTo(0u));
        }
    }
}
