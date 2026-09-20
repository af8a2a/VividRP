using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class VividDiagnosticsTests
    {
        [TestCase("{\"action\":\"unknown\"}")]
        [TestCase("{\"action\":\"cancel\",\"jobId\":\"wrong-job\"}")]
        [TestCase("{\"action\":\"profile\",\"frames\":0}")]
        [TestCase("{\"action\":\"capture\",\"timeoutSeconds\":0}")]
        public void InvalidRequestsFailWithoutStartingAJob(string json)
        {
            var result = JsonUtility.FromJson<VividDiagnostics.Result>(VividDiagnostics.Execute(json));
            Assert.That(result.success, Is.False);
            Assert.That(VividDiagnostics.IsRunning, Is.False);
            Assert.That(result.error, Is.Not.Empty);
        }

        [Test]
        public void RepeatedUnityFrameIdsRetainDistinctCameraObservations()
        {
            using var capture = new RenderStageCapture(new[] { "VividRP.Diagnostics.AbsentMarker" }, 2);
            capture.Observe(1, 1); capture.Observe(1, 2);
            Assert.That(capture.Count, Is.EqualTo(2));
        }

        [Test]
        public void StageObservationIsAllocationFreeAfterWarmup()
        {
            // Native recorder for an absent marker exercises unavailable handling;
            // active GPU timing validity requires a rendered profile capture.
            using var capture = new RenderStageCapture(new[] { "VividRP.Diagnostics.AbsentMarker" }, 4224);
            for (int i = 0; i < 128; i++) capture.Observe(i, i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 128; i < 4224; i++) capture.Observe(i, i);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(capture.Count, Is.EqualTo(4224));
            capture.Observe(4224, 4224);
            Assert.That(capture.Count, Is.EqualTo(4224));
        }

        [Test]
        public void SMRTCostSummaryExcludesSkyKeepsZeroWorkAndUsesWideTotals()
        {
            using var storage = new NativeArray<uint>(21 * SMRTCostCapture.CounterCount, Allocator.Temp);
            var data = storage;
            for (int pixel = 0; pixel < 20; pixel++)
            {
                int offset = pixel * SMRTCostCapture.CounterCount;
                data[offset] = 1;
                data[offset + 10] = (uint)pixel;
                data[offset + 18] = uint.MaxValue;
            }
            data[20 * SMRTCostCapture.CounterCount + 10] = 999;
            data[20 * SMRTCostCapture.CounterCount + 31] = 1;
            var report = SMRTCostCapture.Summarize(data, 21, 1);
            Assert.That(report.receivers, Is.EqualTo(20));
            Assert.That(report.metrics[10].total, Is.EqualTo(190));
            Assert.That(report.metrics[10].meanPerReceiver, Is.EqualTo(9.5));
            Assert.That(report.metrics[10].p50, Is.EqualTo(9));
            Assert.That(report.metrics[10].p95, Is.EqualTo(18));
            Assert.That(report.metrics[10].p99, Is.EqualTo(19));
            Assert.That(report.metrics[10].activePixels, Is.EqualTo(19));
            Assert.That(report.metrics[10].activeP95, Is.EqualTo(19));
            Assert.That(report.metrics[18].total, Is.EqualTo(20L * uint.MaxValue));
            Assert.That(report.shadowDifferencesOverTolerance, Is.EqualTo(1));
            Assert.That(report.metrics[31].total, Is.Zero);
        }

        [Test]
        public void SMRTCostSummaryHandlesSkyOnlyAndRejectsWrongBufferLength()
        {
            using var data = new NativeArray<uint>(SMRTCostCapture.CounterCount, Allocator.Temp);
            var report = SMRTCostCapture.Summarize(data, 1, 1);
            Assert.That(report.receivers, Is.Zero);
            Assert.That(report.metrics[10].meanPerReceiver, Is.Zero);
            Assert.That(report.metrics[10].p95, Is.Zero);
            Assert.That(report.metrics[10].activeP95, Is.Zero);
            Assert.Throws<ArgumentException>(() => SMRTCostCapture.Summarize(data, 2, 1));
        }
    }
}
