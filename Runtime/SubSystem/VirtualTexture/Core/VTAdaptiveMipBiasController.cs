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
            int fallbackSampleCount)
        {
            UploadBudget = uploadBudget;
            PendingUploadCount = Mathf.Max(0, pendingUploadCount);
            BlockedUploadCount = Mathf.Max(0, blockedUploadCount);
            StreamSaturatedRequestCount = Mathf.Max(0, streamSaturatedRequestCount);
            FeedbackOverflowCount = Mathf.Max(0, feedbackOverflowCount);
            FallbackSampleCount = Mathf.Max(0, fallbackSampleCount);
        }

        internal int UploadBudget { get; }

        internal int PendingUploadCount { get; }

        internal int BlockedUploadCount { get; }

        internal int StreamSaturatedRequestCount { get; }

        internal int FeedbackOverflowCount { get; }

        internal int FallbackSampleCount { get; }
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

        internal float CurrentMipBias { get; private set; }

        internal float LastPressure { get; private set; }

        internal float Update(int frameIndex, in VTAdaptiveMipBiasInputs inputs)
        {
            if (m_HasUpdated && frameIndex == m_LastFrameIndex)
                return CurrentMipBias;

            m_HasUpdated = true;
            m_LastFrameIndex = frameIndex;
            LastPressure = ComputePressure(inputs);

            if (LastPressure >= HighPressureThreshold)
            {
                m_CalmFrameCount = 0;
                float targetMipBias = Mathf.Lerp(1f, MaxMipBias, LastPressure);
                CurrentMipBias = Mathf.MoveTowards(CurrentMipBias, targetMipBias, AttackStep);
            }
            else if (LastPressure <= LowPressureThreshold)
            {
                m_CalmFrameCount += 1;
                if (m_CalmFrameCount >= RecoveryDelayFrames)
                {
                    CurrentMipBias = Mathf.MoveTowards(CurrentMipBias, 0f, RecoveryStep);
                }
            }
            else
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
            CurrentMipBias = 0f;
            LastPressure = 0f;
        }

        internal static float ComputePressure(in VTAdaptiveMipBiasInputs inputs)
        {
            int pressureScale = ResolvePressureScale(inputs.UploadBudget);
            float blockedUploadPressure = NormalizeCount(inputs.BlockedUploadCount, pressureScale);
            float streamPressure = inputs.StreamSaturatedRequestCount > 0 ? 1f : 0f;
            float fallbackPressure = NormalizeCount(inputs.FallbackSampleCount, pressureScale * 4f);
            float overflowPressure = inputs.FeedbackOverflowCount > 0 ? 1f : 0f;

            float backlogPressure = 0f;
            if (inputs.UploadBudget > 0 && inputs.UploadBudget < int.MaxValue)
            {
                int excessPendingCount = Mathf.Max(0, inputs.PendingUploadCount - inputs.UploadBudget);
                backlogPressure = NormalizeCount(excessPendingCount, pressureScale);
            }

            return Mathf.Max(
                Mathf.Max(blockedUploadPressure, streamPressure),
                Mathf.Max(backlogPressure, Mathf.Max(overflowPressure, fallbackPressure)));
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
