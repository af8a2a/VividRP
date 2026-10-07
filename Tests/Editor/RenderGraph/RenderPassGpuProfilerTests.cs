using System;
using NUnit.Framework;
using UnityEngine.Profiling;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderPassGpuProfilerTests
    {
        [TearDown]
        public void TearDown() => RenderPassProfilingUtility.Clear();

        [Test]
        public void ConsumingSamples_KeepsRecorderRunning()
        {
            var marker = new Unity.Profiling.ProfilerMarker("VividRP.Tests.ConsumeGpuSamples");
            var recorder = new Unity.Profiling.ProfilerRecorder(marker, 1,
                Unity.Profiling.ProfilerRecorderOptions.StartImmediately);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using (marker.Auto()) { }
                    RenderPassGpuProfiler.TryConsumeLatest(ref recorder, out _);
                    Assert.That(recorder.IsRunning, Is.True);
                    Assert.That(recorder.Count, Is.Zero);
                }
            }
            finally { recorder.Dispose(); }
        }

        [Test]
        public void MissingSamples_ReportsDisabledGpuCollection()
        {
            var message = RenderPassGpuProfilerModule.DescribeMissingSamples(
                UnityEditorInternal.GpuProfilingStatisticsAvailabilityStates.Supported);
            StringAssert.Contains("disabled", message);
        }

        [TestCase(typeof(BloomPass), 7)]
        [TestCase(typeof(FinalBlitPass), 8)]
        [TestCase(typeof(SkyInjectionPass), 4)]
        [TestCase(typeof(LightGridPass), 2)]
        [TestCase(typeof(GTAOPass), 3)]
        [TestCase(typeof(DirectionalRayTracedShadowPass), 1)]
        [TestCase(typeof(RTASBuildPass), 6)]
        [TestCase(typeof(VirtualTextureFeedbackPass), 5)]
        [TestCase(typeof(GBufferPass), 0)]
        [TestCase(typeof(VirtualTextureDemoPass), 9)]
        public void Classification_UsesRenderPurposeInsteadOfDirectory(Type type, int expected)
        {
            Assert.That(RenderPassGpuProfiler.Classify(type), Is.EqualTo(expected));
        }

        [Test]
        public void Register_DeduplicatesSharedMarkerNames_ButKeepsDistinctGraphNodes()
        {
            RenderPassProfilingUtility.Clear();
            RenderPassProfilingUtility.GetMarkers(new GBufferPass(), "Surface", 0);
            RenderPassProfilingUtility.GetMarkers(new GBufferPass(), "Surface", 0);
            Assert.That(RenderPassGpuProfiler.RegisteredCount, Is.EqualTo(1));
            RenderPassProfilingUtility.GetMarkers(new GBufferPass(), "Surface", 1);
            Assert.That(RenderPassGpuProfiler.RegisteredCount, Is.EqualTo(2));
            RenderPassProfilingUtility.Clear();
            Assert.That(RenderPassGpuProfiler.RegisteredCount, Is.Zero);
            RenderPassProfilingUtility.GetMarkers(new GBufferPass(), "Surface", 0);
            Assert.That(RenderPassGpuProfiler.RegisteredCount, Is.EqualTo(1));
        }

        [Test]
        public void Aggregation_RejectsMissingSamples_ButAcceptsMeasuredZero()
        {
            long total = 123;
            Assert.That(RenderPassGpuProfiler.TryAccumulate(900, 0, ref total), Is.False);
            Assert.That(RenderPassGpuProfiler.TryAccumulate(-1, 1, ref total), Is.False);
            Assert.That(total, Is.EqualTo(123));
            Assert.That(RenderPassGpuProfiler.TryAccumulate(0, 1, ref total), Is.True);
            Assert.That(RenderPassGpuProfiler.TryAccumulate(500, 2, ref total), Is.True);
            Assert.That(total, Is.EqualTo(623), "Recorder values already sum calls; do not multiply by count.");
        }

        [Test]
        public void Collect_WhenDisabled_DoesNotAllocateAfterWarmup()
        {
            bool wasEnabled = Profiler.IsCategoryEnabled(RenderPassGpuProfiler.Category);
            try
            {
                Profiler.SetCategoryEnabled(RenderPassGpuProfiler.Category, false);
                RenderPassGpuProfiler.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++) RenderPassGpuProfiler.Collect();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero);
            }
            finally
            {
                Profiler.SetCategoryEnabled(RenderPassGpuProfiler.Category, wasEnabled);
            }
        }
    }
}
