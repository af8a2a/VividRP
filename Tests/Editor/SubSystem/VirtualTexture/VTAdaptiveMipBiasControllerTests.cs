using NUnit.Framework;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class VTAdaptiveMipBiasControllerTests
    {
        [Test]
        public void Update_AttacksQuicklyAndClamps_WhenGlobalPressurePersists()
        {
            var controller = new VTAdaptiveMipBiasController();
            var pressure = new VTAdaptiveMipBiasInputs(
                uploadBudget: 4,
                pendingUploadCount: 4,
                blockedUploadCount: 0,
                streamSaturatedRequestCount: 1,
                feedbackOverflowCount: 0,
                fallbackSampleCount: 0);

            Assert.That(controller.Update(1, pressure), Is.EqualTo(0.5f));

            for (int frameIndex = 2; frameIndex <= 20; frameIndex++)
                controller.Update(frameIndex, pressure);

            Assert.That(controller.CurrentMipBias, Is.EqualTo(VTAdaptiveMipBiasController.MaxMipBias));
            Assert.That(controller.LastPressure, Is.EqualTo(1f));
        }

        [Test]
        public void Update_HoldsInsideHysteresisBand_ThenRecoversAfterCalmDelay()
        {
            var controller = new VTAdaptiveMipBiasController();
            var highPressure = new VTAdaptiveMipBiasInputs(4, 4, 4, 0, 0, 0);
            var hysteresisPressure = new VTAdaptiveMipBiasInputs(4, 4, 1, 0, 0, 0);
            var calm = new VTAdaptiveMipBiasInputs(4, 4, 0, 0, 0, 0);

            Assert.That(controller.Update(1, highPressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(2, hysteresisPressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(3, calm), Is.EqualTo(0.5f));
            Assert.That(controller.Update(4, calm), Is.EqualTo(0.5f));
            Assert.That(controller.Update(5, calm), Is.EqualTo(0.5f));
            Assert.That(controller.Update(6, calm), Is.EqualTo(0.375f));
        }

        [Test]
        public void Update_IsIdempotentForMultipleCamerasInSameFrame()
        {
            var controller = new VTAdaptiveMipBiasController();
            var pressure = new VTAdaptiveMipBiasInputs(2, 2, 2, 0, 0, 0);

            Assert.That(controller.Update(17, pressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(17, pressure), Is.EqualTo(0.5f));
        }

        [Test]
        public void ComputePressure_UsesExcessBacklogAndExplicitFeedbackFailureSignals()
        {
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(4, 4, 0, 0, 0, 0)), Is.Zero);
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(4, 6, 0, 0, 0, 0)), Is.EqualTo(0.5f));
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(4, 0, 0, 0, 1, 0)), Is.EqualTo(1f));
        }
    }
}
