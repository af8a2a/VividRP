using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ReferencedPathTracingReblurHitDistanceReconstructionMode
    {
        Off,
        Area3x3,
        Area5x5
    }

    [Serializable]
    public sealed class ReferencedPathTracingReblurHitDistanceReconstructionModeParameter
        : VolumeParameter<ReferencedPathTracingReblurHitDistanceReconstructionMode>
    {
        public ReferencedPathTracingReblurHitDistanceReconstructionModeParameter(
            ReferencedPathTracingReblurHitDistanceReconstructionMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Path Tracing/REBLUR Denoiser")]
    public sealed class ReferencedPathTracingReblurVolume : VolumeComponent
    {
        public const int MaxHistoryFrameNum = 63;

        [Tooltip("Enables REBLUR. When disabled, the pass resolves the unfiltered path-tracing signals.")]
        public BoolParameter enabled = new(true);

        [Tooltip("Maximum number of frames accumulated by the main history.")]
        public ClampedIntParameter maxAccumulatedFrameNum = new(30, 0, MaxHistoryFrameNum);

        [Tooltip("Maximum number of frames accumulated by responsive fast history.")]
        public ClampedIntParameter maxFastAccumulatedFrameNum = new(6, 0, MaxHistoryFrameNum);

        [Tooltip(
            "Maximum number of frames accumulated by temporal stabilization. " +
            "Zero disables the stabilization dispatch.")]
        public ClampedIntParameter maxStabilizedFrameNum =
            new(MaxHistoryFrameNum, 0, MaxHistoryFrameNum);

        [Tooltip("Number of frames reconstructed after a history reset.")]
        public ClampedIntParameter historyFixFrameNum = new(3, 0, 3);

        [Tooltip("Base pixel stride used by the 5x5 history reconstruction kernel.")]
        public ClampedIntParameter historyFixBasePixelStride = new(14, 1, 20);

        [Tooltip("Color-box sigma used to clamp main history to fast history.")]
        public ClampedFloatParameter fastHistoryClampingSigmaScale = new(2.0f, 1.0f, 3.0f);

        [Tooltip("Diffuse pre-accumulation spatial reuse radius in pixels. Zero disables it.")]
        public ClampedFloatParameter diffusePrepassBlurRadius = new(30.0f, 0.0f, 75.0f);

        [Tooltip("Specular pre-accumulation spatial reuse radius in pixels. Zero disables it.")]
        public ClampedFloatParameter specularPrepassBlurRadius = new(50.0f, 0.0f, 75.0f);

        [Tooltip(
            "Reconstructs missing normalized hit distance before the pre-pass. " +
            "3x3 is the recommended starting point for probabilistically sampled path-tracing signals; " +
            "5x5 fills larger gaps but can spread hit distance farther.")]
        public ReferencedPathTracingReblurHitDistanceReconstructionModeParameter
            hitDistanceReconstructionMode =
                new(ReferencedPathTracingReblurHitDistanceReconstructionMode.Off);

        [Tooltip("Minimum spatial denoising radius in pixels.")]
        public ClampedFloatParameter minBlurRadius = new(1.0f, 0.0f, 10.0f);

        [Tooltip("Base maximum spatial denoising radius in pixels.")]
        public ClampedFloatParameter maxBlurRadius = new(30.0f, 0.0f, 60.0f);

        [Tooltip("Fraction of the diffuse or specular lobe angle used for normal rejection.")]
        public ClampedFloatParameter lobeAngleFraction = new(0.15f, 0.0f, 1.0f);

        [Tooltip("Fraction of the center roughness used for roughness rejection.")]
        public ClampedFloatParameter roughnessFraction = new(0.15f, 0.0f, 1.0f);

        [Tooltip("Maximum allowed deviation from the local tangent plane.")]
        public ClampedFloatParameter planeDistanceSensitivity = new(0.02f, 0.0f, 1.0f);

        [Tooltip("Minimum hit-distance weight used by spatial passes.")]
        public ClampedFloatParameter minHitDistanceWeight = new(0.1f, 0.01f, 0.2f);

        [Tooltip("Relative intensity threshold used to suppress sporadic fireflies.")]
        public ClampedFloatParameter fireflySuppressorMinRelativeScale = new(2.0f, 1.0f, 3.0f);

        [Tooltip("Enables biased anti-firefly history filtering.")]
        public BoolParameter enableAntiFirefly = new(false);

        [Tooltip("Use specular pre-pass only for motion estimation instead of filtering radiance.")]
        public BoolParameter usePrepassOnlyForSpecularMotionEstimation = new(false);

        [Tooltip("Local variance multiplier used by REBLUR anti-lag.")]
        public ClampedFloatParameter antilagLuminanceSigmaScale = new(4.0f, 1.0f, 5.0f);

        [Tooltip("REBLUR anti-lag sensitivity. Smaller values increase responsiveness.")]
        public ClampedFloatParameter antilagLuminanceSensitivity = new(3.0f, 1.0f, 5.0f);

        [Tooltip("Roughness below which temporal accumulation becomes more responsive.")]
        public ClampedFloatParameter responsiveAccumulationRoughnessThreshold =
            new(0.0f, 0.0f, 1.0f);

        [Tooltip("Minimum history retained by responsive accumulation.")]
        public ClampedIntParameter responsiveAccumulationMinFrameNum = new(3, 0, 3);

        [Tooltip("Constant term A used to normalize diffuse/specular hit distance.")]
        public MinFloatParameter hitDistanceA = new(3.0f, 0.0001f);

        [Tooltip("View-depth scale B used to normalize diffuse/specular hit distance.")]
        public MinFloatParameter hitDistanceB = new(0.1f, 0.0001f);

        [Tooltip("Low-roughness scale C used to normalize diffuse/specular hit distance.")]
        public MinFloatParameter hitDistanceC = new(20.0f, 1.0f);

        [Tooltip("Roughness exponent D used to normalize diffuse/specular hit distance.")]
        public ClampedFloatParameter hitDistanceD = new(-25.0f, -100.0f, 0.0f);

        [Tooltip("Writes history length to signal alpha instead of normalized hit distance.")]
        public BoolParameter returnHistoryLengthInsteadOfOcclusion = new(false);

        public bool IsActive()
        {
            return active && enabled.value;
        }
    }
}
