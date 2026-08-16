using UnityEngine;

namespace VividRP.Runtime
{
    internal readonly struct VTAdaptiveMipBiasInputs
    {
        internal VTAdaptiveMipBiasInputs(
            int uploadBudget,
            int pendingUploadCount,
            int blockedUploadCount,
            int streamSaturatedRequestCount,
            int feedbackOverflowCount,
            int fallbackSampleCount,
            int physicalPoolFreePageCount = int.MaxValue,
            int evictionCount = 0,
            bool hasFreshFeedbackMeasurement = true,
            int measuredFeedbackOverflowCount = -1,
            int measuredFallbackSampleCount = -1,
            int measuredFaultOverflowCount = 0,
            int measuredResidentOverflowCount = 0,
            int measuredNonResidentFallbackSampleCount = -1,
            int measuredResidentFallbackSampleCount = 0,
            int feedbackMeasurementFrameIndex = -1,
            int weightedAccessSampleCount = 0,
            int measuredWeightedAccessSampleCount = -1,
            int acceptedFaultRequestCount = 0,
            int acceptedResidentRequestCount = 0,
            bool feedbackOverflowOverrideActive = false,
            bool fallbackSampleOverrideActive = false)
        {
            UploadBudget = uploadBudget;
            PendingUploadCount = Mathf.Max(0, pendingUploadCount);
            BlockedUploadCount = Mathf.Max(0, blockedUploadCount);
            StreamSaturatedRequestCount = Mathf.Max(0, streamSaturatedRequestCount);
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
            PhysicalPoolFreePageCount = Mathf.Max(0, physicalPoolFreePageCount);
            EvictionCount = Mathf.Max(0, evictionCount);
            HasFreshFeedbackMeasurement = hasFreshFeedbackMeasurement;
            MeasuredFeedbackOverflowCount = measuredFeedbackOverflowCount >= 0
                ? measuredFeedbackOverflowCount
                : FeedbackOverflowCount;
            MeasuredFallbackSampleCount = measuredFallbackSampleCount >= 0
                ? measuredFallbackSampleCount
                : FallbackSampleCount;
            MeasuredFaultOverflowCount = Mathf.Max(0, measuredFaultOverflowCount);
            MeasuredResidentOverflowCount = Mathf.Max(0, measuredResidentOverflowCount);
            MeasuredNonResidentFallbackSampleCount = measuredNonResidentFallbackSampleCount >= 0
                ? measuredNonResidentFallbackSampleCount
                : MeasuredFallbackSampleCount;
            MeasuredResidentFallbackSampleCount = Mathf.Max(
                0,
                measuredResidentFallbackSampleCount);
            FeedbackMeasurementFrameIndex = feedbackMeasurementFrameIndex;
            WeightedAccessSampleCount = Mathf.Max(0, weightedAccessSampleCount);
            MeasuredWeightedAccessSampleCount = measuredWeightedAccessSampleCount >= 0
                ? measuredWeightedAccessSampleCount
                : WeightedAccessSampleCount;
            AcceptedFaultRequestCount = Mathf.Max(0, acceptedFaultRequestCount);
            AcceptedResidentRequestCount = Mathf.Max(0, acceptedResidentRequestCount);
            FeedbackOverflowOverrideActive = feedbackOverflowOverrideActive;
            FallbackSampleOverrideActive = fallbackSampleOverrideActive;
        }

        internal int UploadBudget { get; }

        internal int PendingUploadCount { get; }

        internal int BlockedUploadCount { get; }

        internal int StreamSaturatedRequestCount { get; }

        internal int FeedbackOverflowCount { get; }

        internal int FallbackSampleCount { get; }

        internal int PhysicalPoolFreePageCount { get; }

        internal int EvictionCount { get; }

        internal bool HasFreshFeedbackMeasurement { get; }

        internal int MeasuredFeedbackOverflowCount { get; }

        internal int MeasuredFallbackSampleCount { get; }

        internal int MeasuredFaultOverflowCount { get; }

        internal int MeasuredResidentOverflowCount { get; }

        internal int MeasuredNonResidentFallbackSampleCount { get; }

        internal int MeasuredResidentFallbackSampleCount { get; }

        internal int FeedbackMeasurementFrameIndex { get; }

        internal int WeightedAccessSampleCount { get; }

        internal int MeasuredWeightedAccessSampleCount { get; }

        internal int AcceptedFaultRequestCount { get; }

        internal int AcceptedResidentRequestCount { get; }

        internal bool FeedbackOverflowOverrideActive { get; }

        internal bool FallbackSampleOverrideActive { get; }

    }

    internal sealed class VTAdaptiveMipBiasController
    {
        internal const float MaxMipBias = 4f;
        internal const float AttackStep = 0.5f;
        internal const float RecoveryStep = 0.125f;
        internal const float HighPressureThreshold = 0.5f;
        internal const float LowPressureThreshold = 0.125f;
        internal const int RecoveryDelayFrames = 4;

        private const int k_UnlimitedBudgetPressureScale = 64;

        private int m_LastFrameIndex;
        private int m_CalmFrameCount;
        private bool m_HasUpdated;
        private float m_LastMeasuredFeedbackOverflowPressure;
        private float m_LastMeasuredFallbackPressure;
        private int m_LastMeasuredAcceptedFaultRequestCount;
        private bool m_HasMeasuredFeedbackPressure;

        internal float CurrentMipBias { get; private set; }

        internal float LastPressure { get; private set; }

        internal int LastFeedbackOverflowCount { get; private set; }

        internal int LastFallbackSampleCount { get; private set; }

        internal int LastMeasuredFeedbackOverflowCount { get; private set; }

        internal int LastMeasuredFallbackSampleCount { get; private set; }

        internal int LastMeasuredFaultOverflowCount { get; private set; }

        internal int LastMeasuredResidentOverflowCount { get; private set; }

        internal int LastMeasuredNonResidentFallbackSampleCount { get; private set; }

        internal int LastMeasuredResidentFallbackSampleCount { get; private set; }

        internal int LastWeightedAccessSampleCount { get; private set; }

        internal int LastMeasuredWeightedAccessSampleCount { get; private set; }

        internal int LastMeasuredAcceptedFaultRequestCount { get; private set; }

        internal int LastMeasuredAcceptedResidentRequestCount { get; private set; }

        internal float LastFeedbackOverflowPressure { get; private set; }

        internal float LastFallbackPressure { get; private set; }

        internal float LastFallbackCoverage => LastFallbackPressure;

        internal bool LastUpdateHadFreshFeedbackMeasurement { get; private set; }

        internal float LastTargetMipBias { get; private set; }

        internal int LastFreshFeedbackFrameIndex { get; private set; } = -1;

        internal int LastFreshMeasuredFeedbackOverflowCount { get; private set; }

        internal int LastFreshMeasuredFallbackSampleCount { get; private set; }

        internal int LastFreshMeasuredFaultOverflowCount { get; private set; }

        internal int LastFreshMeasuredResidentOverflowCount { get; private set; }

        internal int LastFreshMeasuredNonResidentFallbackSampleCount { get; private set; }

        internal int LastFreshMeasuredResidentFallbackSampleCount { get; private set; }

        internal int LastFreshMeasuredWeightedAccessSampleCount { get; private set; }

        internal float LastFreshFeedbackOverflowPressure { get; private set; }

        internal float LastFreshFallbackPressure { get; private set; }

        internal bool HasUpdatedFrame(int frameIndex)
        {
            return m_HasUpdated && frameIndex == m_LastFrameIndex;
        }

        internal float Update(int frameIndex, in VTAdaptiveMipBiasInputs inputs)
        {
            if (m_HasUpdated && frameIndex == m_LastFrameIndex)
                return CurrentMipBias;

            m_HasUpdated = true;
            m_LastFrameIndex = frameIndex;
            LastFeedbackOverflowCount = inputs.FeedbackOverflowCount;
            LastFallbackSampleCount = inputs.FallbackSampleCount;
            LastMeasuredFeedbackOverflowCount = inputs.MeasuredFeedbackOverflowCount;
            LastMeasuredFallbackSampleCount = inputs.MeasuredFallbackSampleCount;
            LastMeasuredFaultOverflowCount = inputs.MeasuredFaultOverflowCount;
            LastMeasuredResidentOverflowCount = inputs.MeasuredResidentOverflowCount;
            LastMeasuredNonResidentFallbackSampleCount =
                inputs.MeasuredNonResidentFallbackSampleCount;
            LastMeasuredResidentFallbackSampleCount =
                inputs.MeasuredResidentFallbackSampleCount;
            LastWeightedAccessSampleCount = inputs.WeightedAccessSampleCount;
            LastMeasuredWeightedAccessSampleCount = inputs.MeasuredWeightedAccessSampleCount;
            LastMeasuredAcceptedFaultRequestCount = inputs.AcceptedFaultRequestCount;
            LastMeasuredAcceptedResidentRequestCount = inputs.AcceptedResidentRequestCount;
            LastUpdateHadFreshFeedbackMeasurement = inputs.HasFreshFeedbackMeasurement;
            if (inputs.HasFreshFeedbackMeasurement)
            {
                LastFreshFeedbackFrameIndex = inputs.FeedbackMeasurementFrameIndex >= 0
                    ? inputs.FeedbackMeasurementFrameIndex
                    : frameIndex;
                LastFreshMeasuredFeedbackOverflowCount = inputs.MeasuredFeedbackOverflowCount;
                LastFreshMeasuredFallbackSampleCount = inputs.MeasuredFallbackSampleCount;
                LastFreshMeasuredFaultOverflowCount = inputs.MeasuredFaultOverflowCount;
                LastFreshMeasuredResidentOverflowCount = inputs.MeasuredResidentOverflowCount;
                LastFreshMeasuredNonResidentFallbackSampleCount =
                    inputs.MeasuredNonResidentFallbackSampleCount;
                LastFreshMeasuredResidentFallbackSampleCount =
                    inputs.MeasuredResidentFallbackSampleCount;
                LastFreshMeasuredWeightedAccessSampleCount =
                    inputs.MeasuredWeightedAccessSampleCount;
                m_LastMeasuredFeedbackOverflowPressure = ComputeFeedbackOverflowPressure(
                    inputs.MeasuredFaultOverflowCount,
                    inputs.AcceptedFaultRequestCount);
                m_LastMeasuredAcceptedFaultRequestCount = inputs.AcceptedFaultRequestCount;
                m_LastMeasuredFallbackPressure = ComputeFallbackPressure(
                    inputs.MeasuredNonResidentFallbackSampleCount,
                    inputs.MeasuredWeightedAccessSampleCount);
                m_HasMeasuredFeedbackPressure = true;
            }

            LastFeedbackOverflowPressure = inputs.FeedbackOverflowOverrideActive
                ? ComputeFeedbackOverflowPressure(
                    inputs.FeedbackOverflowCount,
                    ResolveOverrideAcceptedFaultRequestCount(inputs))
                : ResolveMeasuredFeedbackPressure(m_LastMeasuredFeedbackOverflowPressure);
            LastFallbackPressure = inputs.FallbackSampleOverrideActive
                ? ComputeFallbackPressure(
                    inputs.FallbackSampleCount,
                    ResolveOverrideWeightedAccessSampleCount(inputs))
                : ResolveMeasuredFeedbackPressure(m_LastMeasuredFallbackPressure);
            if (inputs.HasFreshFeedbackMeasurement)
            {
                LastFreshFeedbackOverflowPressure = LastFeedbackOverflowPressure;
                LastFreshFallbackPressure = LastFallbackPressure;
            }
            float livePressure = ComputeLivePressure(inputs);
            LastPressure = Mathf.Max(
                livePressure,
                Mathf.Max(LastFeedbackOverflowPressure, LastFallbackPressure));
            LastTargetMipBias = LastPressure >= HighPressureThreshold
                ? Mathf.Lerp(1f, MaxMipBias, LastPressure)
                : LastPressure <= LowPressureThreshold
                    ? 0f
                    : CurrentMipBias;

            float actionableFeedbackPressure = 0f;
            if (inputs.HasFreshFeedbackMeasurement || inputs.FeedbackOverflowOverrideActive)
                actionableFeedbackPressure = LastFeedbackOverflowPressure;
            if (inputs.HasFreshFeedbackMeasurement || inputs.FallbackSampleOverrideActive)
            {
                actionableFeedbackPressure = Mathf.Max(
                    actionableFeedbackPressure,
                    LastFallbackPressure);
            }

            float actionablePressure = Mathf.Max(livePressure, actionableFeedbackPressure);
            bool hasFeedbackEvidence = inputs.HasFreshFeedbackMeasurement
                || inputs.FeedbackOverflowOverrideActive
                || inputs.FallbackSampleOverrideActive;
            if (actionablePressure >= HighPressureThreshold)
            {
                m_CalmFrameCount = 0;
                float actionableTargetMipBias = Mathf.Lerp(
                    1f,
                    MaxMipBias,
                    actionablePressure);
                CurrentMipBias = Mathf.MoveTowards(
                    CurrentMipBias,
                    Mathf.Max(CurrentMipBias, actionableTargetMipBias),
                    AttackStep);
            }
            else if (LastPressure <= LowPressureThreshold && hasFeedbackEvidence)
            {
                m_CalmFrameCount += 1;
                if (m_CalmFrameCount >= RecoveryDelayFrames)
                {
                    CurrentMipBias = Mathf.MoveTowards(CurrentMipBias, 0f, RecoveryStep);
                    m_CalmFrameCount = 0;
                }
            }
            else if (livePressure > LowPressureThreshold || hasFeedbackEvidence)
            {
                m_CalmFrameCount = 0;
            }

            CurrentMipBias = Mathf.Clamp(CurrentMipBias, 0f, MaxMipBias);
            return CurrentMipBias;
        }

        internal void Reset()
        {
            m_LastFrameIndex = 0;
            m_CalmFrameCount = 0;
            m_HasUpdated = false;
            m_LastMeasuredFeedbackOverflowPressure = 0f;
            m_LastMeasuredFallbackPressure = 0f;
            m_LastMeasuredAcceptedFaultRequestCount = 0;
            m_HasMeasuredFeedbackPressure = false;
            CurrentMipBias = 0f;
            LastPressure = 0f;
            LastFeedbackOverflowCount = 0;
            LastFallbackSampleCount = 0;
            LastMeasuredFeedbackOverflowCount = 0;
            LastMeasuredFallbackSampleCount = 0;
            LastMeasuredFaultOverflowCount = 0;
            LastMeasuredResidentOverflowCount = 0;
            LastMeasuredNonResidentFallbackSampleCount = 0;
            LastMeasuredResidentFallbackSampleCount = 0;
            LastWeightedAccessSampleCount = 0;
            LastMeasuredWeightedAccessSampleCount = 0;
            LastMeasuredAcceptedFaultRequestCount = 0;
            LastMeasuredAcceptedResidentRequestCount = 0;
            LastFeedbackOverflowPressure = 0f;
            LastFallbackPressure = 0f;
            LastUpdateHadFreshFeedbackMeasurement = false;
            LastTargetMipBias = 0f;
            LastFreshFeedbackFrameIndex = -1;
            LastFreshMeasuredFeedbackOverflowCount = 0;
            LastFreshMeasuredFallbackSampleCount = 0;
            LastFreshMeasuredFaultOverflowCount = 0;
            LastFreshMeasuredResidentOverflowCount = 0;
            LastFreshMeasuredNonResidentFallbackSampleCount = 0;
            LastFreshMeasuredResidentFallbackSampleCount = 0;
            LastFreshMeasuredWeightedAccessSampleCount = 0;
            LastFreshFeedbackOverflowPressure = 0f;
            LastFreshFallbackPressure = 0f;
        }

        internal static float ComputePressure(in VTAdaptiveMipBiasInputs inputs)
        {
            return Mathf.Max(
                ComputeLivePressure(inputs),
                Mathf.Max(
                    ComputeFeedbackOverflowPressure(inputs),
                    ComputeFallbackPressure(inputs)));
        }

        internal static float ComputeLivePressure(in VTAdaptiveMipBiasInputs inputs)
        {
            int pressureScale = ResolvePressureScale(inputs.UploadBudget);
            float blockedUploadPressure = NormalizeCount(inputs.BlockedUploadCount, pressureScale);
            float streamPressure = inputs.StreamSaturatedRequestCount > 0 ? 1f : 0f;
            float evictionPressure = NormalizeCount(
                inputs.EvictionCount,
                Mathf.Max(1f, pressureScale * 0.5f));
            if (inputs.PhysicalPoolFreePageCount == 0 && inputs.EvictionCount > 0)
            {
                evictionPressure = Mathf.Max(
                    evictionPressure,
                    HighPressureThreshold);
            }

            float backlogPressure = 0f;
            if (inputs.UploadBudget > 0 && inputs.UploadBudget < int.MaxValue)
            {
                int excessPendingCount = Mathf.Max(0, inputs.PendingUploadCount - inputs.UploadBudget);
                backlogPressure = NormalizeCount(excessPendingCount, pressureScale);
            }

            return Mathf.Max(
                Mathf.Max(Mathf.Max(blockedUploadPressure, streamPressure), evictionPressure),
                backlogPressure);
        }

        internal static float ComputeFeedbackOverflowPressure(in VTAdaptiveMipBiasInputs inputs)
        {
            return ComputeFeedbackOverflowPressure(
                inputs.FeedbackOverflowCount,
                inputs.AcceptedFaultRequestCount);
        }

        internal static float ComputeFallbackPressure(in VTAdaptiveMipBiasInputs inputs)
        {
            return ComputeFallbackPressure(
                inputs.FallbackSampleCount,
                inputs.WeightedAccessSampleCount);
        }

        private static float ComputeFeedbackOverflowPressure(
            int overflowCount,
            int acceptedFaultRequestCount)
        {
            int count = Mathf.Max(0, overflowCount);
            if (count == 0)
                return 0f;

            if (acceptedFaultRequestCount <= 0)
                return 1f;

            long attemptedCount = (long)acceptedFaultRequestCount + count;
            return Mathf.Clamp01(count / (float)attemptedCount);
        }

        private static float ComputeFallbackPressure(
            int nonResidentFallbackSampleCount,
            int weightedAccessSampleCount)
        {
            int count = Mathf.Max(0, nonResidentFallbackSampleCount);
            if (count == 0)
                return 0f;

            if (weightedAccessSampleCount <= 0)
                return 1f;

            return NormalizeCount(count, weightedAccessSampleCount);
        }

        private int ResolveOverrideWeightedAccessSampleCount(
            in VTAdaptiveMipBiasInputs inputs)
        {
            if (inputs.WeightedAccessSampleCount > 0)
                return inputs.WeightedAccessSampleCount;

            return inputs.MeasuredWeightedAccessSampleCount > 0
                ? inputs.MeasuredWeightedAccessSampleCount
                : LastFreshMeasuredWeightedAccessSampleCount;
        }

        private int ResolveOverrideAcceptedFaultRequestCount(in VTAdaptiveMipBiasInputs inputs)
        {
            return inputs.HasFreshFeedbackMeasurement || inputs.AcceptedFaultRequestCount > 0
                ? inputs.AcceptedFaultRequestCount
                : m_LastMeasuredAcceptedFaultRequestCount;
        }

        private float ResolveMeasuredFeedbackPressure(float pressure)
        {
            return m_HasMeasuredFeedbackPressure ? pressure : 0f;
        }

        private static int ResolvePressureScale(int uploadBudget)
        {
            if (uploadBudget <= 0 || uploadBudget == int.MaxValue)
                return k_UnlimitedBudgetPressureScale;

            return Mathf.Max(1, uploadBudget);
        }

        private static float NormalizeCount(int count, float scale)
        {
            return Mathf.Clamp01(Mathf.Max(0, count) / Mathf.Max(1f, scale));
        }

    }
}
