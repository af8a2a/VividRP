using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividGPUDrivenStatsTests
    {
        [TearDown]
        public void TearDown()
        {
            VividGPUDrivenStatsRegistry.Clear();
        }

        [Test]
        public void Registry_Report_StoresLatestStats()
        {
            VividGPUDrivenStatsRegistry.Report(
                new VividGPUDrivenStats(
                    true,
                    "ok",
                    true,
                    "Main Camera",
                    CameraType.Game,
                    17,
                    1.25,
                    true,
                    8,
                    12,
                    6,
                    21,
                    55,
                    144,
                    288,
                    13,
                    89,
                    2,
                    1024,
                    128,
                    7,
                    4,
                    12.5f));

            VividGPUDrivenStats stats = VividGPUDrivenStatsRegistry.LastStats;

            Assert.That(stats.IsAvailable, Is.True);
            Assert.That(stats.HasCamera, Is.True);
            Assert.That(stats.CameraName, Is.EqualTo("Main Camera"));
            Assert.That(stats.MeshletCount, Is.EqualTo(55));
            Assert.That(stats.AllocatedDescriptorCount, Is.EqualTo(128u));
            Assert.That(stats.MeshLODErrorThreshold, Is.EqualTo(12.5f).Within(0.0001f));
        }

        [Test]
        public void Registry_Clear_ResetsLastStats()
        {
            VividGPUDrivenStatsRegistry.Report(
                new VividGPUDrivenStats(
                    true,
                    string.Empty,
                    false,
                    null,
                    default,
                    3,
                    0.5,
                    false,
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    7,
                    8,
                    9,
                    1,
                    64,
                    4,
                    2,
                    -1,
                    50f));

            VividGPUDrivenStatsRegistry.Clear();

            Assert.That(VividGPUDrivenStatsRegistry.LastStats.IsAvailable, Is.False);
            Assert.That(VividGPUDrivenStatsRegistry.LastStats.MeshletCount, Is.EqualTo(0));
            Assert.That(VividGPUDrivenStatsRegistry.LastStats.HasCamera, Is.False);
        }
    }
}
