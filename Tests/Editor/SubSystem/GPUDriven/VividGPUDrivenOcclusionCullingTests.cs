using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.Tests
{
    public sealed class VividGPUDrivenOcclusionCullingTests
    {
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(1080, 11)]
        [TestCase(1920, 11)]
        public void CalculateMipCount_MatchesHardwareMipChain(int dimension, int expectedMipCount)
        {
            Assert.That(
                VividGPUDrivenOcclusionHistorySystem.CalculateMipCount(dimension, dimension),
                Is.EqualTo(expectedMipCount));
        }

        [TestCase(1)]
        [TestCase(1080)]
        [TestCase(1920)]
        public void CalculateTextureDimension_DoesNotPowerOfTwoPadHistory(int dimension)
        {
            Assert.That(
                VividGPUDrivenOcclusionHistorySystem.CalculateTextureDimension(dimension),
                Is.EqualTo(dimension));
        }

        [Test]
        public void GPUDrivenFrameData_ResetClearsOcclusionObservationState()
        {
            var frameData = new VividGPUDrivenFrameData
            {
                occlusionCullingEnabled = true,
                occlusionHistoryValid = true,
                occlusionObservationMode = true,
            };

            frameData.Reset();

            Assert.That(frameData.occlusionCullingEnabled, Is.False);
            Assert.That(frameData.occlusionHistoryValid, Is.False);
            Assert.That(frameData.occlusionObservationMode, Is.False);
        }
    }
}
