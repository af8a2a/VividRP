using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderPass.Core
{
    internal struct ReferencedPathTracingReblurSettings : IEquatable<ReferencedPathTracingReblurSettings>
    {
        public bool enabled;
        public Vector4 hitDistanceParameters;
        public Vector2 antilagParameters;
        public int maxAccumulatedFrameNum;
        public int maxFastAccumulatedFrameNum;
        public int historyFixFrameNum;
        public int historyFixBasePixelStride;
        public float fastHistoryClampingSigmaScale;
        public float diffusePrepassBlurRadius;
        public float specularPrepassBlurRadius;
        public ReferencedPathTracingReblurHitDistanceReconstructionMode
            hitDistanceReconstructionMode;
        public float minBlurRadius;
        public float maxBlurRadius;
        public float lobeAngleFraction;
        public float roughnessFraction;
        public float planeDistanceSensitivity;
        public float minHitDistanceWeight;
        public float fireflySuppressorMinRelativeScale;
        public bool enableAntiFirefly;
        public bool usePrepassOnlyForSpecularMotionEstimation;
        public float responsiveAccumulationRoughnessThreshold;
        public int responsiveAccumulationMinFrameNum;
        public bool returnHistoryLengthInsteadOfOcclusion;

        public static ReferencedPathTracingReblurSettings CreateDefault()
        {
            return new ReferencedPathTracingReblurSettings
            {
                enabled = true,
                hitDistanceParameters = new Vector4(3.0f, 0.1f, 20.0f, -25.0f),
                antilagParameters = new Vector2(4.0f, 3.0f),
                maxAccumulatedFrameNum = 30,
                maxFastAccumulatedFrameNum = 6,
                historyFixFrameNum = 3,
                historyFixBasePixelStride = 14,
                fastHistoryClampingSigmaScale = 2.0f,
                diffusePrepassBlurRadius = 30.0f,
                specularPrepassBlurRadius = 50.0f,
                hitDistanceReconstructionMode =
                    ReferencedPathTracingReblurHitDistanceReconstructionMode.Off,
                minBlurRadius = 1.0f,
                maxBlurRadius = 30.0f,
                lobeAngleFraction = 0.15f,
                roughnessFraction = 0.15f,
                planeDistanceSensitivity = 0.02f,
                minHitDistanceWeight = 0.1f,
                fireflySuppressorMinRelativeScale = 2.0f,
                enableAntiFirefly = false,
                usePrepassOnlyForSpecularMotionEstimation = false,
                responsiveAccumulationRoughnessThreshold = 0.0f,
                responsiveAccumulationMinFrameNum = 3,
                returnHistoryLengthInsteadOfOcclusion = false
            };
        }

        public bool Equals(ReferencedPathTracingReblurSettings other)
        {
            return enabled == other.enabled
                && hitDistanceParameters.Equals(other.hitDistanceParameters)
                && antilagParameters.Equals(other.antilagParameters)
                && maxAccumulatedFrameNum == other.maxAccumulatedFrameNum
                && maxFastAccumulatedFrameNum == other.maxFastAccumulatedFrameNum
                && historyFixFrameNum == other.historyFixFrameNum
                && historyFixBasePixelStride == other.historyFixBasePixelStride
                && fastHistoryClampingSigmaScale.Equals(other.fastHistoryClampingSigmaScale)
                && diffusePrepassBlurRadius.Equals(other.diffusePrepassBlurRadius)
                && specularPrepassBlurRadius.Equals(other.specularPrepassBlurRadius)
                && hitDistanceReconstructionMode == other.hitDistanceReconstructionMode
                && minBlurRadius.Equals(other.minBlurRadius)
                && maxBlurRadius.Equals(other.maxBlurRadius)
                && lobeAngleFraction.Equals(other.lobeAngleFraction)
                && roughnessFraction.Equals(other.roughnessFraction)
                && planeDistanceSensitivity.Equals(other.planeDistanceSensitivity)
                && minHitDistanceWeight.Equals(other.minHitDistanceWeight)
                && fireflySuppressorMinRelativeScale.Equals(other.fireflySuppressorMinRelativeScale)
                && enableAntiFirefly == other.enableAntiFirefly
                && usePrepassOnlyForSpecularMotionEstimation
                    == other.usePrepassOnlyForSpecularMotionEstimation
                && responsiveAccumulationRoughnessThreshold.Equals(
                    other.responsiveAccumulationRoughnessThreshold)
                && responsiveAccumulationMinFrameNum == other.responsiveAccumulationMinFrameNum
                && returnHistoryLengthInsteadOfOcclusion
                    == other.returnHistoryLengthInsteadOfOcclusion;
        }

        public override bool Equals(object obj)
        {
            return obj is ReferencedPathTracingReblurSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = enabled ? 1 : 0;
                hash = (hash * 397) ^ hitDistanceParameters.GetHashCode();
                hash = (hash * 397) ^ antilagParameters.GetHashCode();
                hash = (hash * 397) ^ maxAccumulatedFrameNum;
                hash = (hash * 397) ^ maxFastAccumulatedFrameNum;
                hash = (hash * 397) ^ historyFixFrameNum;
                hash = (hash * 397) ^ historyFixBasePixelStride;
                hash = (hash * 397) ^ fastHistoryClampingSigmaScale.GetHashCode();
                hash = (hash * 397) ^ diffusePrepassBlurRadius.GetHashCode();
                hash = (hash * 397) ^ specularPrepassBlurRadius.GetHashCode();
                hash = (hash * 397) ^ (int)hitDistanceReconstructionMode;
                hash = (hash * 397) ^ minBlurRadius.GetHashCode();
                hash = (hash * 397) ^ maxBlurRadius.GetHashCode();
                hash = (hash * 397) ^ lobeAngleFraction.GetHashCode();
                hash = (hash * 397) ^ roughnessFraction.GetHashCode();
                hash = (hash * 397) ^ planeDistanceSensitivity.GetHashCode();
                hash = (hash * 397) ^ minHitDistanceWeight.GetHashCode();
                hash = (hash * 397) ^ fireflySuppressorMinRelativeScale.GetHashCode();
                hash = (hash * 397) ^ (enableAntiFirefly ? 1 : 0);
                hash = (hash * 397) ^ (usePrepassOnlyForSpecularMotionEstimation ? 1 : 0);
                hash = (hash * 397) ^ responsiveAccumulationRoughnessThreshold.GetHashCode();
                hash = (hash * 397) ^ responsiveAccumulationMinFrameNum;
                hash = (hash * 397) ^ (returnHistoryLengthInsteadOfOcclusion ? 1 : 0);
                return hash;
            }
        }
    }

    internal static class ReferencedPathTracingReblurSettingsResolver
    {
        internal static ReferencedPathTracingReblurSettings Resolve()
        {
            var settings = ReferencedPathTracingReblurSettings.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var volume = stack.GetComponent<ReferencedPathTracingReblurVolume>();
            if (volume == null || !volume.active)
                return settings;

            settings.enabled = volume.enabled.value;
            settings.hitDistanceParameters = new Vector4(
                Mathf.Max(volume.hitDistanceA.value, 0.0001f),
                Mathf.Max(volume.hitDistanceB.value, 0.0001f),
                Mathf.Max(volume.hitDistanceC.value, 1.0f),
                Mathf.Min(volume.hitDistanceD.value, 0.0f));
            settings.antilagParameters = new Vector2(
                Mathf.Clamp(volume.antilagLuminanceSigmaScale.value, 1.0f, 5.0f),
                Mathf.Clamp(volume.antilagLuminanceSensitivity.value, 1.0f, 5.0f));
            settings.maxAccumulatedFrameNum = Mathf.Clamp(
                volume.maxAccumulatedFrameNum.value,
                0,
                ReferencedPathTracingReblurVolume.MaxHistoryFrameNum);
            settings.maxFastAccumulatedFrameNum = Mathf.Clamp(
                volume.maxFastAccumulatedFrameNum.value,
                0,
                ReferencedPathTracingReblurVolume.MaxHistoryFrameNum);
            int maximumHistoryFixFrameNum = Mathf.Max(
                0,
                Mathf.Min(3, settings.maxFastAccumulatedFrameNum - 1));
            settings.historyFixFrameNum = Mathf.Clamp(
                volume.historyFixFrameNum.value,
                0,
                maximumHistoryFixFrameNum);
            settings.historyFixBasePixelStride = Mathf.Clamp(
                volume.historyFixBasePixelStride.value,
                1,
                20);
            settings.fastHistoryClampingSigmaScale = Mathf.Clamp(
                volume.fastHistoryClampingSigmaScale.value,
                1.0f,
                3.0f);
            settings.diffusePrepassBlurRadius = Mathf.Clamp(
                volume.diffusePrepassBlurRadius.value,
                0.0f,
                75.0f);
            settings.specularPrepassBlurRadius = Mathf.Clamp(
                volume.specularPrepassBlurRadius.value,
                0.0f,
                75.0f);
            settings.hitDistanceReconstructionMode =
                SanitizeHitDistanceReconstructionMode(
                    volume.hitDistanceReconstructionMode.value);
            settings.maxBlurRadius = Mathf.Clamp(volume.maxBlurRadius.value, 0.0f, 60.0f);
            settings.minBlurRadius = Mathf.Min(
                Mathf.Clamp(volume.minBlurRadius.value, 0.0f, 10.0f),
                settings.maxBlurRadius);
            settings.lobeAngleFraction = Mathf.Clamp01(volume.lobeAngleFraction.value);
            settings.roughnessFraction = Mathf.Clamp01(volume.roughnessFraction.value);
            settings.planeDistanceSensitivity = Mathf.Clamp01(
                volume.planeDistanceSensitivity.value);
            settings.minHitDistanceWeight = Mathf.Clamp(
                volume.minHitDistanceWeight.value,
                0.01f,
                0.2f);
            settings.fireflySuppressorMinRelativeScale = Mathf.Clamp(
                volume.fireflySuppressorMinRelativeScale.value,
                1.0f,
                3.0f);
            settings.enableAntiFirefly = volume.enableAntiFirefly.value;
            settings.usePrepassOnlyForSpecularMotionEstimation =
                volume.usePrepassOnlyForSpecularMotionEstimation.value;
            settings.responsiveAccumulationRoughnessThreshold = Mathf.Clamp01(
                volume.responsiveAccumulationRoughnessThreshold.value);
            settings.responsiveAccumulationMinFrameNum = Mathf.Clamp(
                volume.responsiveAccumulationMinFrameNum.value,
                0,
                settings.historyFixFrameNum);
            settings.returnHistoryLengthInsteadOfOcclusion =
                volume.returnHistoryLengthInsteadOfOcclusion.value;
            return settings;
        }

        private static ReferencedPathTracingReblurHitDistanceReconstructionMode
            SanitizeHitDistanceReconstructionMode(
                ReferencedPathTracingReblurHitDistanceReconstructionMode mode)
        {
            switch (mode)
            {
                case ReferencedPathTracingReblurHitDistanceReconstructionMode.Area3x3:
                case ReferencedPathTracingReblurHitDistanceReconstructionMode.Area5x5:
                    return mode;
                default:
                    return ReferencedPathTracingReblurHitDistanceReconstructionMode.Off;
            }
        }
    }
}
