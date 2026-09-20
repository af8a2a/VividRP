using VividRP.Runtime.VirtualShadowMap;
using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderPass.Core;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VSMBaselineRecorderTests
    {
        [Test]
        public void DetailedQualitySchedule_CapturesEveryRecoveryFrameWithoutDuplicateAnchors()
        {
            int count = 0;
            for (int step = 0; step < VSMQualityReproduction.FrameCount; step++)
            {
                bool capture = VSMQualityReproduction.ShouldCapture(step, true);
                if (step >= 440 && step <= 480) Assert.That(capture, Is.True);
                if (capture) count++;
            }
            Assert.That(count, Is.EqualTo(69));
            Assert.That(VSMQualityReproduction.ShouldCapture(-1, true), Is.False);
            Assert.That(VSMQualityReproduction.ShouldCapture(601, true), Is.False);
            Assert.That(VSMQualityReproduction.ShouldCapture(461, false), Is.False);
            for (int i = 0; i < 1000; i++) VSMQualityReproduction.ShouldCapture(i, true);
            int captured = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                if (VSMQualityReproduction.ShouldCapture(i, true)) captured++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(captured, Is.EqualTo(69));
        }

        [Test]
        public void RepaintComparison_KeepsMatched4KInputsAndManualBaselineDefaults()
        {
            var cases = VSMBaselineCase.CreateRepaintComparison();
            Assert.That(cases.Length, Is.EqualTo(2));
            Assert.That(cases[0].resolution, Is.EqualTo(4096));
            Assert.That(cases[0].pcf, Is.False);
            Assert.That(cases[0].recorderRepaint, Is.EqualTo(VSMBaselineRepaintMode.FourHz));
            Assert.That(cases[1].recorderRepaint, Is.EqualTo(VSMBaselineRepaintMode.Manual));
            Assert.That(cases[0], Is.Not.SameAs(cases[1]));
            var expected = cases[0].Copy();
            expected.name = cases[1].name; expected.recorderRepaint = cases[1].recorderRepaint;
            Assert.That(JsonUtility.ToJson(cases[1]), Is.EqualTo(JsonUtility.ToJson(expected)));
            foreach (var item in VSMBaselineCase.CreateDefaults())
                Assert.That(item.recorderRepaint, Is.EqualTo(VSMBaselineRepaintMode.Manual));
        }

        [Test]
        public void RepaintSchedule_ManualStaysQuietAndCanReturnToPeriodicModeWithoutAllocation()
        {
            double next = 0;
            Assert.That(VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.FourHz, 0, ref next), Is.True);
            Assert.That(next, Is.EqualTo(0.25));
            Assert.That(VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.FourHz, 0.249, ref next), Is.False);
            Assert.That(VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.FourHz, 0.25, ref next), Is.True);
            Assert.That(next, Is.EqualTo(0.5));
            for (int i = 0; i < 1000; i++)
                VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.Manual, i, ref next);
            int repaints = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                if (VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.Manual, i, ref next)) repaints++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(repaints, Is.Zero);
            Assert.That(next, Is.EqualTo(0.5), "Manual must not set an infinite deadline for the next case.");
            Assert.That(VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.OneHz, 1000, ref next), Is.True);
            Assert.That(next, Is.EqualTo(1001));
            Assert.That(VSMBaselineRecorderWindow.ShouldRepaint(VSMBaselineRepaintMode.OneHz, 1000.999, ref next), Is.False);
        }

        [Test]
        public void TimingExperiment_ChangesOnlyInstrumentationAndRotatesOrder()
        {
            var cases = VSMBaselineCase.CreateTimingExperiment(3);
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                cases[0].Apply(settings);
                for (int round = 0; round < 3; round++)
                    for (int step = 0; step < 3; step++)
                    {
                        var item = cases[round * 3 + step];
                        Assert.That(item.Matches(settings), Is.True, "Rendering inputs must stay fixed in a repaint experiment.");
                        Assert.That((int)item.recorderRepaint, Is.EqualTo((round + step) % 3));
                    }
                Assert.That(double.IsPositiveInfinity(VSMBaselineSession.RepaintInterval(VSMBaselineRepaintMode.Manual)), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void ObservationMetadata_RemainsDistinctFromDelayedGpuTimestamp()
        {
            var samples = new VSMBaselineSamples(1, 1);
            samples.Add(100, 10, 42, new[] { 0.035 }, 2, 99, 80, 4, 0.012);
            Assert.That(samples.CameraFrames[0], Is.EqualTo(99));
            Assert.That(samples.TimingTimestamps[0], Is.EqualTo(42));
            Assert.That(samples.WindowRepaints[0], Is.EqualTo(4));
            Assert.That(samples.SecondsSinceRepaint[0], Is.EqualTo(0.012));
            Assert.That(samples.Values[0], Is.EqualTo(0.035), "Low readings must remain in the raw data.");
        }

        [Test]
        public void QualityTrajectory_WarmupClampsAndStableEvaluationAllocatesZero()
        {
            var start = new Vector3(3, 7, -2); var offset = new Vector3(2, 0, 2);
            Assert.That(VSMQualityReproduction.PositionAt(start, offset, -60), Is.EqualTo(start));
            Assert.That(VSMQualityReproduction.PositionAt(start, offset, 300), Is.EqualTo(start + offset * 0.5f));
            Assert.That(VSMQualityReproduction.PositionAt(start, offset, 999), Is.EqualTo(start + offset));
            for (int i = 0; i < 1000; i++) VSMQualityReproduction.PositionAt(start, offset, i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) VSMQualityReproduction.PositionAt(start, offset, i);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Statistics_ExcludeUnavailableSamplesAndKeepMetricsIndependent()
        {
            var samples = new VSMBaselineSamples(4, 2);
            samples.Add(1, 1, 1, new[] { 4.0, double.NaN });
            samples.Add(2, 2, 2, new[] { 1.0, double.NaN });
            samples.Add(3, 3, 3, new[] { 3.0, double.NaN });
            samples.Add(4, 4, 4, new[] { 2.0, double.NaN });
            Assert.That(samples.Statistics(0, out double median, out double p95, out double min, out double max), Is.EqualTo(4));
            Assert.That(median, Is.EqualTo(2.5)); Assert.That(p95, Is.EqualTo(4));
            Assert.That(min, Is.EqualTo(1)); Assert.That(max, Is.EqualTo(4));
            Assert.That(samples.Statistics(1, out median, out p95, out min, out max), Is.Zero);
            Assert.That(double.IsNaN(median) && double.IsNaN(p95), Is.True);
            Assert.That(samples.Values[0], Is.EqualTo(4), "Summary sorting must preserve raw observation order.");
        }

        [Test]
        public void WarmSampleCollection_AllocatesZeroBytesAndDoesNotOverwriteCapacity()
        {
            var samples = new VSMBaselineSamples(256, 2);
            var input = new[] { 1.25, double.NaN };
            for (int i = 0; i < 32; i++) samples.Add(i, i, (ulong)i, input);
            samples.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 512; i++) samples.Add(i, i, (ulong)i, input);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(samples.Count, Is.EqualTo(256));
            Assert.That(samples.Frames[255], Is.EqualTo(255));
        }

        [Test]
        public void Export_UsesInvariantDecimalsEscapesLabelsAndLeavesMissingGpuBlank()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VSMBaselineExport_" + Guid.NewGuid().ToString("N"));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                Directory.CreateDirectory(directory);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var samples = new VSMBaselineSamples(1, 2);
                samples.Add(7, 0.5, 123, new[] { 1.25, double.NaN });
                samples.Write(directory, 0, new VSMBaselineCase { name = "PCF, \"fine\"" }, "completed",
                    new[] { "cpu_ms", "gpu_ms" });
                string raw = File.ReadAllText(Path.Combine(directory, "case_000_samples.csv"));
                StringAssert.Contains("7,0.5,123,0,1.25,", raw);
                string summary = File.ReadAllText(Path.Combine(directory, "summary.csv"));
                StringAssert.Contains("\"PCF, \"\"fine\"\"\"", summary);
                StringAssert.Contains("\"gpu_ms\",1,0,,,,", summary);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LowGpuReadings_RemainInRawExportAndSummaryInObservationOrder()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VSMBaselineLowGpu_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var samples = new VSMBaselineSamples(4, 1);
                samples.Add(10, 1, 100, new[] { 8.0 }, 1, 9, 7, 2, 0.1);
                samples.Add(11, 1.25, 101, new[] { 0.035 }, 1, 10, 8, 3, 0.01);
                samples.Add(12, 1.5, 102, new[] { 0.045 }, 1, 11, 9, 4, 0.01);
                samples.Add(13, 1.75, 0, new[] { double.NaN }, 1, 12, 10, 4, 0.26);
                samples.Write(directory, 0, new VSMBaselineCase { name = "manual" }, "completed",
                    new[] { "gpu_frame_ms" });
                string[] raw = File.ReadAllLines(Path.Combine(directory, "case_000_samples.csv"));
                Assert.That(raw.Length, Is.EqualTo(5));
                Assert.That(raw[1], Is.EqualTo("10,1,100,1,8,9,7,2,0.1"));
                Assert.That(raw[2], Is.EqualTo("11,1.25,101,1,0.035,10,8,3,0.01"));
                Assert.That(raw[3], Is.EqualTo("12,1.5,102,1,0.045,11,9,4,0.01"));
                Assert.That(raw[4], Is.EqualTo("13,1.75,0,1,,12,10,4,0.26"));
                StringAssert.Contains("\"gpu_frame_ms\",4,3,0.045,8,0.035,8",
                    File.ReadAllText(Path.Combine(directory, "summary.csv")));
                Assert.That(samples.Values[0], Is.EqualTo(8));
                Assert.That(samples.Values[1], Is.EqualTo(0.035));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void Resume_KeepsActiveTimeAndExcludesPauseAndWarmup()
        {
            var clock = new VSMBaselineSamplingClock();
            clock.BeginSegment();
            clock.Observe(10); clock.Observe(12.5);
            clock.BeginSegment(); // Resume after pause and a fresh warmup.
            clock.Observe(100); clock.Observe(103);
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(5.5));
            Assert.That(clock.Segment, Is.EqualTo(2));
            clock.Reset(); clock.BeginSegment(); clock.Observe(200);
            Assert.That(clock.ElapsedSeconds, Is.Zero);
            Assert.That(clock.Segment, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedCheckpoint_ReplacesSummaryRowsAndPreservesOtherCases()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VSMBaselineCheckpoint_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var samples = new VSMBaselineSamples(4, 2);
                var settings = new VSMBaselineCase { name = "checkpoint" };
                var metrics = new[] { "cpu_ms", "gpu_ms" };
                samples.Add(1, 1, 1, new[] { 2.0, double.NaN }, 1);
                samples.Write(directory, 1, settings, "completed", metrics);
                samples.Write(directory, 0, settings, "paused", metrics);
                samples.Add(2, 10, 2, new[] { 4.0, 8.0 }, 2);
                samples.Write(directory, 0, settings, "paused", metrics);
                samples.Write(directory, 0, settings, "completed", metrics);
                string[] summary = File.ReadAllLines(Path.Combine(directory, "summary.csv"));
                Assert.That(summary.Length, Is.EqualTo(5), "Two cases and two metrics, regardless of checkpoint count.");
                StringAssert.Contains("\"cpu_ms\",2,2,3,4,2,4", summary[1]);
                StringAssert.Contains("\"gpu_ms\",2,1,8,8,8,8", summary[2]);
                StringAssert.Contains("\"cpu_ms\",1,1,2,2,2,2", summary[3]);
                string raw = File.ReadAllText(Path.Combine(directory, "case_000_samples.csv"));
                StringAssert.Contains("1,1,1,1,2,", raw);
                StringAssert.Contains("2,10,2,2,4,8", raw);
                Assert.That(samples.ValidCounts[0], Is.EqualTo(2));
                Assert.That(samples.ValidCounts[1], Is.EqualTo(1));
                samples.Clear();
                Assert.That(samples.Count, Is.Zero);
                Assert.That(samples.ValidCounts[0], Is.Zero);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test]
        public void WarmedStageProfilingScopes_AllocateZeroManagedBytes()
        {
            using (var command = new CommandBuffer())
            {
                var samplers = new[] { VSMProfiling.Allocate, VSMProfiling.Clear, VSMProfiling.Finalize, VSMProfiling.DynamicRaster };
                for (int frame = 0; frame < 32; frame++)
                {
                    foreach (var sampler in samplers) { using (new ProfilingScope(command, sampler)) { } }
                    command.Clear();
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int frame = 0; frame < 256; frame++)
                {
                    foreach (var sampler in samplers) { using (new ProfilingScope(command, sampler)) { } }
                    command.Clear();
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
        }

        [Test]
        public void Coverage_RequiresActualSamplesAndNinetyFivePercent()
        {
            Assert.That(VSMBaselineSession.SufficientCoverage(0, 0), Is.False);
            Assert.That(VSMBaselineSession.SufficientCoverage(0, 300), Is.False);
            Assert.That(VSMBaselineSession.SufficientCoverage(284, 300), Is.False);
            Assert.That(VSMBaselineSession.SufficientCoverage(285, 300), Is.True);
        }

        [Test]
        public void Cases_ApplyAllRequestedOverridesAndDetectAnIneffectiveSetting()
        {
            var settings = ScriptableObject.CreateInstance<CascadedShadowSettingsVolume>();
            try
            {
                var parameter = new VSMBaselineCase { resolution = 4096, pcf = true, stochasticFiltering = true, screenDensity = true,
                    targetTexelPixels = 0.5f, lodBias = -1, firstLevel = 0, maxDistance = 80, transition = 0.3f };
                settings.cascadeCount.value = 2;
                parameter.Validate(); parameter.Apply(settings);
                Assert.That(parameter.Matches(settings), Is.True);
                Assert.That(settings.virtualShadowMapStochasticFiltering.overrideState, Is.True);
                settings.virtualShadowMapStochasticFiltering.value = false;
                Assert.That(parameter.Matches(settings), Is.False);
                settings.virtualShadowMapStochasticFiltering.value = true;
                Assert.That(settings.virtualShadowMapResolution.overrideState, Is.True);
                Assert.That(settings.cascadeCount.value, Is.EqualTo(2));
                settings.virtualShadowMapResolutionLodBias.value = 1;
                Assert.That(parameter.Matches(settings), Is.False);
                parameter.resolution = 1000;
                Assert.Throws<ArgumentException>(() => parameter.Validate());
                parameter.resolution = 2048; parameter.maxDistance = float.NaN;
                Assert.Throws<ArgumentException>(() => parameter.Validate());
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }
    }
}
