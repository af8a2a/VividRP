using System;
using NUnit.Framework;
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
    }
}
