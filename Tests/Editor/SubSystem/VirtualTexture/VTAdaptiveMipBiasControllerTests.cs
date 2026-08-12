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
            var pressure = new VTAdaptiveMipBiasInputs(2, 2, 2, 0, 1, 0);
            var calm = new VTAdaptiveMipBiasInputs(
                2,
                0,
                0,
                0,
                0,
                0,
                hasFreshFeedbackMeasurement: false);

            Assert.That(controller.Update(17, pressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(17, calm), Is.EqualTo(0.5f));
            Assert.That(controller.LastFeedbackOverflowCount, Is.EqualTo(1));
            Assert.That(controller.LastUpdateHadFreshFeedbackMeasurement, Is.True);
        }

        [Test]
        public void ComputePressure_UsesExcessBacklogAndExplicitFeedbackFailureSignals()
        {
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(4, 4, 0, 0, 0, 0)), Is.Zero);
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(4, 6, 0, 0, 0, 0)), Is.EqualTo(0.5f));
            Assert.That(VTAdaptiveMipBiasController.ComputePressure(
                new VTAdaptiveMipBiasInputs(
                    4,
                    0,
                    0,
                    0,
                    100,
                    0,
                    acceptedFaultRequestCount: 100)), Is.EqualTo(0.5f));
        }

        [Test]
        public void ComputePressure_UsesEvictionsOnlyWhenThePhysicalPoolIsThrashing()
        {
            var fullButStable = new VTAdaptiveMipBiasInputs(
                16,
                0,
                0,
                0,
                0,
                0,
                physicalPoolFreePageCount: 0,
                evictionCount: 0);
            var fullAndEvicting = new VTAdaptiveMipBiasInputs(
                16,
                0,
                0,
                0,
                0,
                0,
                physicalPoolFreePageCount: 0,
                evictionCount: 1);

            Assert.That(VTAdaptiveMipBiasController.ComputePressure(fullButStable), Is.Zero);
            Assert.That(
                VTAdaptiveMipBiasController.ComputePressure(fullAndEvicting),
                Is.GreaterThanOrEqualTo(VTAdaptiveMipBiasController.HighPressureThreshold));
        }

        [Test]
        public void Update_ReportsFeedbackPressureBreakdownAndMeasurementFreshness()
        {
            var controller = new VTAdaptiveMipBiasController();
            var inputs = new VTAdaptiveMipBiasInputs(
                uploadBudget: 4,
                pendingUploadCount: 0,
                blockedUploadCount: 0,
                streamSaturatedRequestCount: 0,
                feedbackOverflowCount: 1,
                fallbackSampleCount: 8,
                hasFreshFeedbackMeasurement: false,
                measuredFeedbackOverflowCount: 3,
                measuredFallbackSampleCount: 11,
                measuredFaultOverflowCount: 2,
                measuredResidentOverflowCount: 1,
                measuredNonResidentFallbackSampleCount: 5,
                measuredResidentFallbackSampleCount: 6,
                weightedAccessSampleCount: 16,
                measuredWeightedAccessSampleCount: 10,
                acceptedFaultRequestCount: 4,
                acceptedResidentRequestCount: 7,
                feedbackOverflowOverrideActive: true,
                fallbackSampleOverrideActive: true);

            controller.Update(1, inputs);

            Assert.That(controller.LastFeedbackOverflowCount, Is.EqualTo(1));
            Assert.That(controller.LastFallbackSampleCount, Is.EqualTo(8));
            Assert.That(controller.LastMeasuredFeedbackOverflowCount, Is.EqualTo(3));
            Assert.That(controller.LastMeasuredFallbackSampleCount, Is.EqualTo(11));
            Assert.That(controller.LastMeasuredFaultOverflowCount, Is.EqualTo(2));
            Assert.That(controller.LastMeasuredResidentOverflowCount, Is.EqualTo(1));
            Assert.That(controller.LastMeasuredNonResidentFallbackSampleCount, Is.EqualTo(5));
            Assert.That(controller.LastMeasuredResidentFallbackSampleCount, Is.EqualTo(6));
            Assert.That(controller.LastWeightedAccessSampleCount, Is.EqualTo(16));
            Assert.That(controller.LastMeasuredWeightedAccessSampleCount, Is.EqualTo(10));
            Assert.That(controller.LastMeasuredAcceptedFaultRequestCount, Is.EqualTo(4));
            Assert.That(controller.LastMeasuredAcceptedResidentRequestCount, Is.EqualTo(7));
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(0.2f));
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.5f));
            Assert.That(controller.LastPressure, Is.EqualTo(0.5f));
            Assert.That(controller.LastTargetMipBias, Is.EqualTo(2.5f));
            Assert.That(controller.LastUpdateHadFreshFeedbackMeasurement, Is.False);
            Assert.That(controller.LastFreshFeedbackFrameIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Update_LatchesLastFreshFeedbackAcrossReadbackGaps()
        {
            var controller = new VTAdaptiveMipBiasController();
            var fresh = new VTAdaptiveMipBiasInputs(
                4,
                0,
                0,
                0,
                3,
                8,
                hasFreshFeedbackMeasurement: true,
                measuredFeedbackOverflowCount: 3,
                measuredFallbackSampleCount: 8,
                measuredFaultOverflowCount: 3,
                measuredNonResidentFallbackSampleCount: 8,
                weightedAccessSampleCount: 16,
                measuredWeightedAccessSampleCount: 16,
                acceptedFaultRequestCount: 12);
            var noReadback = new VTAdaptiveMipBiasInputs(
                4,
                0,
                0,
                0,
                0,
                0,
                hasFreshFeedbackMeasurement: false);

            controller.Update(1, fresh);
            controller.Update(2, noReadback);

            Assert.That(controller.LastUpdateHadFreshFeedbackMeasurement, Is.False);
            Assert.That(controller.LastFeedbackOverflowCount, Is.Zero);
            Assert.That(controller.LastFreshFeedbackFrameIndex, Is.EqualTo(1));
            Assert.That(controller.LastFreshMeasuredFeedbackOverflowCount, Is.EqualTo(3));
            Assert.That(controller.LastFreshMeasuredFallbackSampleCount, Is.EqualTo(8));
            Assert.That(controller.LastFreshMeasuredFaultOverflowCount, Is.EqualTo(3));
            Assert.That(controller.LastFreshMeasuredResidentOverflowCount, Is.Zero);
            Assert.That(controller.LastFreshMeasuredNonResidentFallbackSampleCount, Is.EqualTo(8));
            Assert.That(controller.LastFreshMeasuredResidentFallbackSampleCount, Is.Zero);
            Assert.That(controller.LastFreshMeasuredWeightedAccessSampleCount, Is.EqualTo(16));
            Assert.That(controller.LastFreshFeedbackOverflowPressure, Is.EqualTo(0.2f));
            Assert.That(controller.LastFreshFallbackPressure, Is.EqualTo(0.5f));
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(0.2f));
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.5f));
            Assert.That(controller.LastPressure, Is.EqualTo(0.5f));
            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.5f));
        }

        [Test]
        public void ComputeFeedbackPressure_NormalizesByMatchingFeedbackUnits()
        {
            var smallBudget = new VTAdaptiveMipBiasInputs(
                uploadBudget: 1,
                pendingUploadCount: 0,
                blockedUploadCount: 0,
                streamSaturatedRequestCount: 0,
                feedbackOverflowCount: 25,
                fallbackSampleCount: 50,
                weightedAccessSampleCount: 100,
                acceptedFaultRequestCount: 100);
            var largeBudget = new VTAdaptiveMipBiasInputs(
                uploadBudget: 64,
                pendingUploadCount: 0,
                blockedUploadCount: 0,
                streamSaturatedRequestCount: 0,
                feedbackOverflowCount: 250,
                fallbackSampleCount: 500,
                weightedAccessSampleCount: 1000,
                acceptedFaultRequestCount: 1000);

            Assert.That(
                VTAdaptiveMipBiasController.ComputeFallbackPressure(smallBudget),
                Is.EqualTo(0.5f));
            Assert.That(
                VTAdaptiveMipBiasController.ComputeFallbackPressure(largeBudget),
                Is.EqualTo(0.5f));
            Assert.That(
                VTAdaptiveMipBiasController.ComputeFeedbackOverflowPressure(smallBudget),
                Is.EqualTo(0.2f));
            Assert.That(
                VTAdaptiveMipBiasController.ComputeFeedbackOverflowPressure(largeBudget),
                Is.EqualTo(0.2f));
        }

        [Test]
        public void Update_ReadbackGapsDoNotRepeatFeedbackAttackOrRecovery()
        {
            var controller = new VTAdaptiveMipBiasController();
            var freshPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var noReadback = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false);
            var freshCalm = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);

            Assert.That(controller.Update(1, freshPressure), Is.EqualTo(0.5f));
            for (int frameIndex = 2; frameIndex <= 10; frameIndex++)
                controller.Update(frameIndex, noReadback);

            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.5f));
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(1f));

            controller.Update(11, freshCalm);
            for (int frameIndex = 12; frameIndex <= 20; frameIndex++)
                controller.Update(frameIndex, noReadback);

            Assert.That(controller.LastFallbackPressure, Is.Zero);
            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.5f));
        }

        [Test]
        public void Update_RecoveryRequiresEachGroupOfFreshCalmMeasurements()
        {
            var controller = new VTAdaptiveMipBiasController();
            var freshPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var freshCalm = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);

            controller.Update(1, freshPressure);
            for (int frameIndex = 2; frameIndex <= 5; frameIndex++)
                controller.Update(frameIndex, freshCalm);

            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.375f));

            for (int frameIndex = 6; frameIndex <= 8; frameIndex++)
                controller.Update(frameIndex, freshCalm);

            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.375f));
            Assert.That(controller.Update(9, freshCalm), Is.EqualTo(0.25f));
        }

        [Test]
        public void Update_LivePressureStillAttacksWithoutFreshFeedback()
        {
            var controller = new VTAdaptiveMipBiasController();
            var livePressure = new VTAdaptiveMipBiasInputs(
                uploadBudget: 4,
                pendingUploadCount: 4,
                blockedUploadCount: 4,
                streamSaturatedRequestCount: 0,
                feedbackOverflowCount: 0,
                fallbackSampleCount: 0,
                hasFreshFeedbackMeasurement: false);

            Assert.That(controller.Update(1, livePressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(2, livePressure), Is.EqualTo(1f));
            Assert.That(controller.Update(3, livePressure), Is.EqualTo(1.5f));
        }

        [Test]
        public void Update_PositiveOverrideStillAttacksWithoutFreshFeedback()
        {
            var controller = new VTAdaptiveMipBiasController();
            var overriddenPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: false,
                fallbackOverrideActive: true);

            Assert.That(controller.Update(1, overriddenPressure), Is.EqualTo(0.5f));
            Assert.That(controller.Update(2, overriddenPressure), Is.EqualTo(1f));
            Assert.That(controller.Update(3, overriddenPressure), Is.EqualTo(1.5f));
        }

        [Test]
        public void Update_ZeroOverridesProvideRecoveryEvidence()
        {
            var controller = new VTAdaptiveMipBiasController();
            var freshPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var clearedOverrides = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                overflowOverrideActive: true,
                fallbackOverrideActive: true);

            controller.Update(1, freshPressure);
            Assert.That(controller.Update(2, clearedOverrides), Is.EqualTo(0.5f));
            Assert.That(controller.Update(3, clearedOverrides), Is.EqualTo(0.5f));
            Assert.That(controller.Update(4, clearedOverrides), Is.EqualTo(0.5f));
            Assert.That(controller.Update(5, clearedOverrides), Is.EqualTo(0.375f));
        }

        [Test]
        public void Update_OneClearedOverrideDoesNotRecoverAgainstOtherHeldPressure()
        {
            var controller = new VTAdaptiveMipBiasController();
            var measuredFallbackPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var clearOverflowOnly = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                overflowOverrideActive: true);

            Assert.That(controller.Update(1, measuredFallbackPressure), Is.EqualTo(0.5f));
            for (int frameIndex = 2; frameIndex <= 10; frameIndex++)
                controller.Update(frameIndex, clearOverflowOnly);

            Assert.That(controller.LastFeedbackOverflowPressure, Is.Zero);
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(1f));
            Assert.That(controller.CurrentMipBias, Is.EqualTo(0.5f));
        }

        [Test]
        public void Update_DoesNotReduceBiasWhilePressureRemainsHigh()
        {
            var controller = new VTAdaptiveMipBiasController();
            var fullPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 100,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var thresholdPressure = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 50,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);

            for (int frameIndex = 1; frameIndex <= 8; frameIndex++)
                controller.Update(frameIndex, fullPressure);

            Assert.That(controller.CurrentMipBias, Is.EqualTo(4f));

            controller.Update(9, thresholdPressure);

            Assert.That(controller.LastPressure, Is.EqualTo(0.5f));
            Assert.That(controller.LastTargetMipBias, Is.EqualTo(2.5f));
            Assert.That(controller.CurrentMipBias, Is.EqualTo(4f));
        }

        [Test]
        public void Update_FeedbackOverridesActIndependentlyAndRestoreMeasuredPressure()
        {
            var controller = new VTAdaptiveMipBiasController();
            var measuredPressure = CreateFeedbackInputs(
                overflowCount: 50,
                fallbackCount: 50,
                weightedAccessCount: 100,
                hasFreshMeasurement: true);
            var clearOverflowOnly = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                overflowOverrideActive: true);
            var clearFallbackOnly = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                fallbackOverrideActive: true);
            var injectFallbackWithoutReadback = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 25,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                fallbackOverrideActive: true);
            var injectOverflowWithoutReadback = CreateFeedbackInputs(
                overflowCount: 25,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false,
                overflowOverrideActive: true);
            var noOverrides = CreateFeedbackInputs(
                overflowCount: 0,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: false);

            controller.Update(1, measuredPressure);
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(1f / 3f));
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.5f));

            controller.Update(2, clearOverflowOnly);
            Assert.That(controller.LastFeedbackOverflowPressure, Is.Zero);
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.5f));

            controller.Update(3, clearFallbackOnly);
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(1f / 3f));
            Assert.That(controller.LastFallbackPressure, Is.Zero);

            controller.Update(4, injectFallbackWithoutReadback);
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.25f));

            controller.Update(5, injectOverflowWithoutReadback);
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(0.2f));

            controller.Update(6, noOverrides);
            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(1f / 3f));
            Assert.That(controller.LastFallbackPressure, Is.EqualTo(0.5f));
        }

        [Test]
        public void Update_OverflowOverrideUsesFreshZeroAcceptedFaultDenominator()
        {
            var controller = new VTAdaptiveMipBiasController();
            var previousFresh = CreateFeedbackInputs(
                overflowCount: 25,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: true,
                acceptedFaultRequestCount: 100);
            var fresh = CreateFeedbackInputs(
                overflowCount: 25,
                fallbackCount: 0,
                weightedAccessCount: 0,
                hasFreshMeasurement: true,
                overflowOverrideActive: true,
                acceptedFaultRequestCount: 0);

            controller.Update(1, previousFresh);
            controller.Update(2, fresh);

            Assert.That(controller.LastFeedbackOverflowPressure, Is.EqualTo(1f));
        }

        private static VTAdaptiveMipBiasInputs CreateFeedbackInputs(
            int overflowCount,
            int fallbackCount,
            int weightedAccessCount,
            bool hasFreshMeasurement,
            bool overflowOverrideActive = false,
            bool fallbackOverrideActive = false,
            int acceptedFaultRequestCount = 100)
        {
            return new VTAdaptiveMipBiasInputs(
                uploadBudget: 4,
                pendingUploadCount: 0,
                blockedUploadCount: 0,
                streamSaturatedRequestCount: 0,
                feedbackOverflowCount: overflowCount,
                fallbackSampleCount: fallbackCount,
                hasFreshFeedbackMeasurement: hasFreshMeasurement,
                measuredFeedbackOverflowCount: overflowCount,
                measuredFallbackSampleCount: fallbackCount,
                measuredFaultOverflowCount: overflowCount,
                measuredNonResidentFallbackSampleCount: fallbackCount,
                weightedAccessSampleCount: weightedAccessCount,
                measuredWeightedAccessSampleCount: weightedAccessCount,
                acceptedFaultRequestCount: hasFreshMeasurement ? acceptedFaultRequestCount : 0,
                feedbackOverflowOverrideActive: overflowOverrideActive,
                fallbackSampleOverrideActive: fallbackOverrideActive);
        }
    }
}
