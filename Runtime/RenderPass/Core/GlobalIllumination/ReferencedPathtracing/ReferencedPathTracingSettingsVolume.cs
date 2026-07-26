using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ReferencedPathTracingSamplingMode
    {
        [InspectorName("Indexed BND (Owen-Sobol)")]
        IndexedBnd = 0,
        [InspectorName("Indexed Hash")]
        IndexedHash = 1
    }

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

    public enum ReferencedPathTracingTransportDebugMode
    {
        Combined = 0,
        [InspectorName("NEE PDFs")]
        NeePdfs = 1,
        [InspectorName("NEE MIS Weight")]
        NeeMisWeight = 2,
        [InspectorName("BSDF Segment PDFs")]
        BsdfSegmentPdfs = 3,
        [InspectorName("BSDF Segment MIS Weight")]
        BsdfSegmentMisWeight = 4,
        [InspectorName("NEE Light Identity")]
        NeeLightIdentity = 5,
        [InspectorName("Invalid Sample Mask")]
        InvalidSampleMask = 6,
        [InspectorName("Light Spatial Index")]
        LightSpatialIndex = 7,
        [InspectorName("Path Samples")]
        PathSamples = 8,
        [InspectorName("Shading Normal")]
        ShadingNormal = 9,
        [InspectorName("Physical Camera")]
        PhysicalCamera = 10
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
    public sealed class ReferencedPathTracingSamplingModeParameter
        : VolumeParameter<ReferencedPathTracingSamplingMode>
    {
        public ReferencedPathTracingSamplingModeParameter(
            ReferencedPathTracingSamplingMode value,
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
            "Uses the fixed seed below and marks the accumulation-relative sample " +
            "sequence as reproducible. Enable this for canonical reference captures.")]
        public BoolParameter deterministicSampling = new(false);

        [Tooltip("Fixed random seed used by deterministic reference captures.")]
        public ClampedIntParameter fixedSeed =
            new(0x13579B, 0, int.MaxValue);

        [Tooltip(
            "Selects the random-access path sampler. Indexed BND uses the Owen-Sobol " +
            "blue-noise resources; Indexed Hash is a deterministic validation fallback. " +
            "Both modes use the same fixed sample-dimension layout.")]
        public ReferencedPathTracingSamplingModeParameter pathSamplingMode =
            new(ReferencedPathTracingSamplingMode.IndexedBnd);

        [Tooltip("Maximum number of path segments evaluated by the reference integrator.")]
        public ClampedIntParameter maxBounceCount =
            new(4, 1, MaximumSupportedBounceCount);

        [Tooltip(
            "Path segment after which Russian roulette starts. Values above Max Bounce Count " +
            "effectively disable roulette for the current path depth.")]
        public ClampedIntParameter russianRouletteStartBounce =
            new(3, 1, MaximumSupportedBounceCount);

        [Tooltip(
            "Allows punctual and area lights from the stable Reference Light List to participate " +
            "in canonical next-event estimation. The serialized field name is retained for " +
            "existing Volume assets.")]
        public BoolParameter enableReGIR = new(true);

        [Tooltip(
            "Mixes the stable global light distribution with a position- and normal-aware " +
            "proposal at every shading vertex. The global proposal remains active as a support " +
            "floor, so enabling this changes variance but not the converged result.")]
        public BoolParameter shadingPointLightSelection = new(true);

        [Tooltip(
            "Probability of sampling the stable global light proposal when shading-point-aware " +
            "selection is enabled. The remaining probability samples the local proposal.")]
        public ClampedFloatParameter globalLightProposalProbability =
            new(0.25f, 0.05f, 1.0f);

        [Tooltip(
            "Uses the deterministic Reference Light Spatial Index to bound shading-point " +
            "light selection and analytic emitter traversal. Overflowed cells fall back to " +
            "the complete Reference Light List.")]
        public BoolParameter lightSpatialIndex = new(true);

        [Tooltip(
            "Uses NVIDIA Shader Execution Reordering for surface rays when running Direct3D 12 " +
            "on supported NVIDIA hardware. Unsupported systems use the standard path automatically.")]
        public BoolParameter enableShaderExecutionReordering = new(false);

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

        [InspectorName("Transport Estimator Mode")]
        [Tooltip(
            "Selects how analytic-light and environment NEE are combined with BSDF-sampled " +
            "segments. MIS is the production estimator. Light Only and BSDF Only are validation " +
            "modes; singular lights and delta BSDF events retain their only reachable strategy. " +
            "The serialized field name is retained for existing Volume assets.")]
        public ReferencedPathTracingEnvironmentEstimatorModeParameter environmentEstimatorMode =
            new(ReferencedPathTracingEnvironmentEstimatorMode.Mis);

        protected override void OnEnable()
        {
            deterministicSampling ??= new BoolParameter(false);
            fixedSeed ??= new ClampedIntParameter(0x13579B, 0, int.MaxValue);
            pathSamplingMode ??=
                new ReferencedPathTracingSamplingModeParameter(
                    ReferencedPathTracingSamplingMode.IndexedBnd);
            maxBounceCount ??=
                new ClampedIntParameter(4, 1, MaximumSupportedBounceCount);
            russianRouletteStartBounce ??=
                new ClampedIntParameter(3, 1, MaximumSupportedBounceCount);
            enableReGIR ??= new BoolParameter(true);
            shadingPointLightSelection ??= new BoolParameter(true);
            globalLightProposalProbability ??=
                new ClampedFloatParameter(0.25f, 0.05f, 1.0f);
            lightSpatialIndex ??= new BoolParameter(true);
            enableShaderExecutionReordering ??= new BoolParameter(false);
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
            base.OnEnable();
        }
    }
}
