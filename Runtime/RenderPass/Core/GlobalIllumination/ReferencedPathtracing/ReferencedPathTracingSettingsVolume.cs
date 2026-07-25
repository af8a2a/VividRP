using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ReferencedPathTracingEnvironmentSamplingMode
    {
        BsdfOnly = 0,
        ImportanceSampling = 1,
        UniformSphere = 2
    }

    public enum ReferencedPathTracingEnvironmentDebugMode
    {
        Combined = 0,
        EnvironmentOnly = 1,
        PrimaryBackgroundOnly = 2,
        IndirectMissOnly = 3
    }

    public enum ReferencedPathTracingEnvironmentEstimatorMode
    {
        [InspectorName("MIS")]
        Mis = 0,
        [InspectorName("Light Only")]
        LightOnly = 1,
        [InspectorName("BSDF Only")]
        BsdfOnly = 2
    }

    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentSamplingModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentSamplingMode>
    {
        public ReferencedPathTracingEnvironmentSamplingModeParameter(
            ReferencedPathTracingEnvironmentSamplingMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentDebugModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentDebugMode>
    {
        public ReferencedPathTracingEnvironmentDebugModeParameter(
            ReferencedPathTracingEnvironmentDebugMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingEnvironmentEstimatorModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentEstimatorMode>
    {
        public ReferencedPathTracingEnvironmentEstimatorModeParameter(
            ReferencedPathTracingEnvironmentEstimatorMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Path Tracing/Reference Path Tracing")]
    public sealed class ReferencedPathTracingSettingsVolume : VolumeComponent
    {
        internal const int MaximumSupportedBounceCount = 8;
        internal const int MaximumTargetSampleCount = 1048576;

        [Tooltip(
            "Uses an accumulation-relative sample index and the fixed seed below. " +
            "Enable this for canonical reference captures.")]
        public BoolParameter deterministicSampling = new(false);

        [Tooltip("Fixed random seed used by deterministic reference captures.")]
        public ClampedIntParameter fixedSeed =
            new(0x13579B, 0, int.MaxValue);

        [Tooltip("Maximum number of path segments evaluated by the reference integrator.")]
        public ClampedIntParameter maxBounceCount =
            new(4, 1, MaximumSupportedBounceCount);

        [Tooltip(
            "Path segment after which Russian roulette starts. Values above Max Bounce Count " +
            "effectively disable roulette for the current path depth.")]
        public ClampedIntParameter russianRouletteStartBounce =
            new(3, 1, MaximumSupportedBounceCount);

        [Tooltip(
            "Allows punctual and area lights to be sampled through ReGIR. Canonical HDRI V1 " +
            "validation disables this so the reference is independent of reservoir state.")]
        public BoolParameter enableReGIR = new(true);

        [Tooltip(
            "Target accumulated samples used by canonical capture tooling. Interactive " +
            "accumulation remains unbounded.")]
        public ClampedIntParameter targetSampleCount =
            new(2048, 1, MaximumTargetSampleCount);

        [Tooltip("Allows the active HDRI Sky to contribute scene-linear environment lighting.")]
        public BoolParameter environmentLighting = new(true);

        [Tooltip(
            "Allows primary camera rays to see the active HDRI Sky. " +
            "This does not disable environment lighting.")]
        public BoolParameter environmentCameraVisible = new(true);

        [Tooltip(
            "Selects the environment-light proposal. BSDF Only disables environment NEE, " +
            "Importance Sampling uses the HDRI distribution, and Uniform Sphere is a validation mode.")]
        public ReferencedPathTracingEnvironmentSamplingModeParameter environmentSamplingMode =
            new(ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling);

        [Tooltip(
            "Selects how environment NEE and BSDF misses are combined. MIS is the production " +
            "estimator; Light Only and BSDF Only are unbiased validation modes. Delta BSDF " +
            "events retain their only reachable BSDF-miss path in every mode.")]
        public ReferencedPathTracingEnvironmentEstimatorModeParameter environmentEstimatorMode =
            new(ReferencedPathTracingEnvironmentEstimatorMode.Mis);

        [Tooltip(
            "Selects the reference environment contribution shown in the resolved path-tracing output. " +
            "The physical AOVs remain unchanged.")]
        public ReferencedPathTracingEnvironmentDebugModeParameter environmentDebugMode =
            new(ReferencedPathTracingEnvironmentDebugMode.Combined);

        protected override void OnEnable()
        {
            deterministicSampling ??= new BoolParameter(false);
            fixedSeed ??= new ClampedIntParameter(0x13579B, 0, int.MaxValue);
            maxBounceCount ??=
                new ClampedIntParameter(4, 1, MaximumSupportedBounceCount);
            russianRouletteStartBounce ??=
                new ClampedIntParameter(3, 1, MaximumSupportedBounceCount);
            enableReGIR ??= new BoolParameter(true);
            targetSampleCount ??=
                new ClampedIntParameter(
                    2048,
                    1,
                    MaximumTargetSampleCount);
            environmentLighting ??= new BoolParameter(true);
            environmentCameraVisible ??= new BoolParameter(true);
            environmentSamplingMode ??=
                new ReferencedPathTracingEnvironmentSamplingModeParameter(
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling);
            environmentEstimatorMode ??=
                new ReferencedPathTracingEnvironmentEstimatorModeParameter(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis);
            environmentDebugMode ??=
                new ReferencedPathTracingEnvironmentDebugModeParameter(
                    ReferencedPathTracingEnvironmentDebugMode.Combined);
            base.OnEnable();
        }
    }
}
