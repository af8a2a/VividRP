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

    public enum ReferencedPathTracingRTXTFMode
    {
        [InspectorName("Linear")]
        Linear = 1,
        [InspectorName("Cubic")]
        Cubic = 2,
        [InspectorName("Gaussian")]
        Gaussian = 3
    }

    public enum ReferencedPathTracingEnvironmentSamplingMode
    {
        BsdfOnly = 0,
        ImportanceSampling = 1,
        UniformSphere = 2
    }

    public enum ReferencedPathTracingEnvironmentMode
    {
        [InspectorName("HDRI")]
        Hdri = 0,
        [InspectorName("Reference Atmosphere")]
        ReferenceAtmosphere = 1
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

    public enum ReferencedPathTracingCloudMultipleScatteringMode
    {
        [InspectorName("Off (Single Scattering)")]
        Off = 0,
        [InspectorName("Energy Compensation")]
        EnergyCompensation = 1
    }

    public enum ReferencedPathTracingAtmosphereTransportMode
    {
        [InspectorName("Numerical Reference")]
        NumericalReference = 0,
        [InspectorName("Optimized Preview")]
        OptimizedPreview = 1
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
        PhysicalCamera = 10,
        [InspectorName("Atmosphere Transport")]
        AtmosphereTransport = 11,
        [InspectorName("Thin-Walled Transmission")]
        ThinWalledTransmission = 12,
        [InspectorName("Stochastic Transparency")]
        StochasticTransparency = 13
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
    public sealed class ReferencedPathTracingEnvironmentModeParameter
        : VolumeParameter<ReferencedPathTracingEnvironmentMode>
    {
        public ReferencedPathTracingEnvironmentModeParameter(
            ReferencedPathTracingEnvironmentMode value,
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
    public sealed class ReferencedPathTracingRTXTFModeParameter
        : VolumeParameter<ReferencedPathTracingRTXTFMode>
    {
        public ReferencedPathTracingRTXTFModeParameter(
            ReferencedPathTracingRTXTFMode value,
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
    public sealed class ReferencedPathTracingCloudMultipleScatteringModeParameter
        : VolumeParameter<ReferencedPathTracingCloudMultipleScatteringMode>
    {
        public ReferencedPathTracingCloudMultipleScatteringModeParameter(
            ReferencedPathTracingCloudMultipleScatteringMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class ReferencedPathTracingAtmosphereTransportModeParameter
        : VolumeParameter<ReferencedPathTracingAtmosphereTransportMode>
    {
        public ReferencedPathTracingAtmosphereTransportModeParameter(
            ReferencedPathTracingAtmosphereTransportMode value,
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

        [Header("RTX Texture Filtering")]
        [Tooltip(
            "Uses NVIDIA RTXTF stochastic texture filtering for opaque StandardLit " +
            "materials in the reference path tracer. Alpha-tested, transparent, and " +
            "virtual-textured materials retain their visibility-safe sampling path.")]
        public BoolParameter enableRTXTF = new(true);

        [Tooltip(
            "Selects the RTXTF reconstruction kernel. Linear converges to the normal " +
            "bilinear result; Cubic and Gaussian trade more temporal noise for a " +
            "higher-order filter.")]
        public ReferencedPathTracingRTXTFModeParameter rtxtfFilter =
            new(ReferencedPathTracingRTXTFMode.Linear);

        [Tooltip(
            "Gaussian standard deviation in texels. This is used only by the Gaussian " +
            "RTXTF filter.")]
        public ClampedFloatParameter rtxtfGaussianSigma =
            new(0.7f, 0.05f, 4.0f);

        [Tooltip(
            "Maximum accumulated samples for reference path tracing and canonical capture. " +
            "Rendering stops at this count and convergence-gated Open Image Denoise runs once.")]
        public ClampedIntParameter targetSampleCount =
            new(2048, 1, MaximumTargetSampleCount);

        [Header("Environment")]
        [Tooltip(
            "Selects the mutually exclusive reference environment. HDRI preserves the V1 " +
            "infinite-light path. Reference Atmosphere evaluates spherical participating-medium " +
            "transport directly without consuming raster sky cubemaps or atmosphere LUTs.")]
        public ReferencedPathTracingEnvironmentModeParameter environmentMode =
            new(ReferencedPathTracingEnvironmentMode.Hdri);

        [Tooltip("Allows the selected environment to contribute scene-linear lighting.")]
        public BoolParameter environmentLighting = new(true);

        [Tooltip(
            "Allows primary camera rays to see the selected environment. " +
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

        [Header("Reference Atmosphere Contract")]
        [Tooltip(
            "Numerical Reference bypasses the atmosphere optical-depth LUT, uses the " +
            "high-accuracy transmittance and cloud-shadow budgets, and disables empirical " +
            "cloud energy compensation. Optimized Preview enables cached LUT transport and " +
            "the lower cloud-shadow budget; approximation state is recorded in capture metadata.")]
        public ReferencedPathTracingAtmosphereTransportModeParameter
            referenceAtmosphereTransportMode =
                new(
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference);

        [Tooltip(
            "Allows camera rays to accumulate physical atmosphere scattering. Disabling it " +
            "retains atmosphere attenuation for surface transport.")]
        public BoolParameter referenceAtmosphereCameraVisible = new(true);

        [Tooltip(
            "Treats the atmosphere as a camera holdout while retaining its transport contribution. " +
            "The physical radiance is preserved while camera alpha is cleared.")]
        public BoolParameter referenceAtmosphereHoldout = new(false);

        [Tooltip(
            "Enables the PT-only spherical reference cloud layer. It does not consume raster " +
            "cloud color, depth, shadow, or history resources.")]
        public BoolParameter referenceClouds = new(false);

        [Tooltip(
            "Allows reference clouds to be visible to camera rays. This is independent from " +
            "whether clouds contribute to reference transport.")]
        public BoolParameter referenceCloudsCameraVisible = new(true);

        [Tooltip(
            "Treats reference clouds as a camera holdout while retaining their transport " +
            "contribution.")]
        public BoolParameter referenceCloudsHoldout = new(false);

        [Tooltip("Altitude in meters above the virtual planet ground where the cloud shell begins.")]
        public ClampedFloatParameter referenceCloudBottomAltitude =
            new(1500.0f, 0.0f, 30000.0f);

        [Tooltip("Thickness in meters of the spherical reference cloud shell.")]
        public ClampedFloatParameter referenceCloudThickness =
            new(4000.0f, 100.0f, 30000.0f);

        [Tooltip("Procedural cloud coverage. Zero is empty and one retains the full density field.")]
        public ClampedFloatParameter referenceCloudCoverage =
            new(0.55f, 0.0f, 1.0f);

        [Tooltip("Full-density cloud extinction coefficient in inverse meters.")]
        public ClampedFloatParameter referenceCloudExtinction =
            new(0.001f, 0.000001f, 0.01f);

        [Tooltip("Spectral single-scattering albedo of the cloud medium.")]
        public ColorParameter referenceCloudScatteringAlbedo =
            new(new Color(0.999f, 0.999f, 0.999f), false, false, true);

        [Tooltip("Henyey-Greenstein anisotropy used by the reference cloud phase function.")]
        public ClampedFloatParameter referenceCloudAnisotropy =
            new(0.7f, -0.95f, 0.95f);

        [Tooltip("World-space scale in meters of the deterministic procedural density field.")]
        public ClampedFloatParameter referenceCloudNoiseScale =
            new(8000.0f, 100.0f, 100000.0f);

        [Tooltip("Stable seed for the procedural reference cloud density field.")]
        public ClampedIntParameter referenceCloudNoiseSeed =
            new(1337, 0, int.MaxValue);

        [Tooltip(
            "Optional biased multiple-scattering approximation. Off leaves the explicit multi-bounce " +
            "cloud path unchanged; Energy Compensation is recorded in capture metadata.")]
        public ReferencedPathTracingCloudMultipleScatteringModeParameter
            referenceCloudMultipleScatteringMode =
                new(ReferencedPathTracingCloudMultipleScatteringMode.Off);

        [Tooltip(
            "Strength of the local cloud multiple-scattering energy compensation approximation.")]
        public ClampedFloatParameter referenceCloudMultipleScatteringStrength =
            new(0.5f, 0.0f, 2.0f);

        [Tooltip(
            "Allows the virtual planet ground to be visible to camera rays. Ground remains part " +
            "of atmosphere transport independently of this camera-visibility flag.")]
        public BoolParameter referenceGroundCameraVisible = new(true);

        [Tooltip(
            "Treats the virtual planet ground as a camera holdout while retaining its atmosphere " +
            "transport contribution. This flag is reserved by the Phase 2 contract.")]
        public BoolParameter referenceGroundHoldout = new(false);

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
            environmentMode ??=
                new ReferencedPathTracingEnvironmentModeParameter(
                    ReferencedPathTracingEnvironmentMode.Hdri);
            environmentLighting ??= new BoolParameter(true);
            environmentCameraVisible ??= new BoolParameter(true);
            environmentSamplingMode ??=
                new ReferencedPathTracingEnvironmentSamplingModeParameter(
                    ReferencedPathTracingEnvironmentSamplingMode.ImportanceSampling);
            environmentEstimatorMode ??=
                new ReferencedPathTracingEnvironmentEstimatorModeParameter(
                    ReferencedPathTracingEnvironmentEstimatorMode.Mis);
            referenceAtmosphereTransportMode ??=
                new ReferencedPathTracingAtmosphereTransportModeParameter(
                    ReferencedPathTracingAtmosphereTransportMode
                        .NumericalReference);
            referenceAtmosphereCameraVisible ??= new BoolParameter(true);
            referenceAtmosphereHoldout ??= new BoolParameter(false);
            referenceClouds ??= new BoolParameter(false);
            referenceCloudsCameraVisible ??= new BoolParameter(true);
            referenceCloudsHoldout ??= new BoolParameter(false);
            referenceCloudBottomAltitude ??=
                new ClampedFloatParameter(1500.0f, 0.0f, 30000.0f);
            referenceCloudThickness ??=
                new ClampedFloatParameter(4000.0f, 100.0f, 30000.0f);
            referenceCloudCoverage ??=
                new ClampedFloatParameter(0.55f, 0.0f, 1.0f);
            referenceCloudExtinction ??=
                new ClampedFloatParameter(0.001f, 0.000001f, 0.01f);
            referenceCloudScatteringAlbedo ??=
                new ColorParameter(
                    new Color(0.999f, 0.999f, 0.999f),
                    false,
                    false,
                    true);
            referenceCloudAnisotropy ??=
                new ClampedFloatParameter(0.7f, -0.95f, 0.95f);
            referenceCloudNoiseScale ??=
                new ClampedFloatParameter(8000.0f, 100.0f, 100000.0f);
            referenceCloudNoiseSeed ??=
                new ClampedIntParameter(1337, 0, int.MaxValue);
            referenceCloudMultipleScatteringMode ??=
                new ReferencedPathTracingCloudMultipleScatteringModeParameter(
                    ReferencedPathTracingCloudMultipleScatteringMode.Off);
            referenceCloudMultipleScatteringStrength ??=
                new ClampedFloatParameter(0.5f, 0.0f, 2.0f);
            referenceGroundCameraVisible ??= new BoolParameter(true);
            referenceGroundHoldout ??= new BoolParameter(false);
            base.OnEnable();
        }
    }
}
