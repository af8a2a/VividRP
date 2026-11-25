using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseTraceMode")]
    public enum LumenIndirectDiffuseTraceMode
    {
        SdfOnly,
        ScreenOnly,
        Hybrid
    }

    [Serializable]
    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseTraceModeParameter")]
    public sealed class LumenIndirectDiffuseTraceModeParameter : VolumeParameter<LumenIndirectDiffuseTraceMode>
    {
        public LumenIndirectDiffuseTraceModeParameter(LumenIndirectDiffuseTraceMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseGatherMethod")]
    public enum LumenIndirectDiffuseGatherMethod
    {
        [InspectorName("Screen Probe Gather")]
        ScreenProbeGather,
        [InspectorName("ReSTIR Gather (2 × 2)")]
        ReSTIRGather
    }

    [Serializable]
    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseGatherMethodParameter")]
    public sealed class LumenIndirectDiffuseGatherMethodParameter : VolumeParameter<LumenIndirectDiffuseGatherMethod>
    {
        public LumenIndirectDiffuseGatherMethodParameter(LumenIndirectDiffuseGatherMethod value) : base(value) { }
    }

    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseSpatialDenoise")]
    public enum LumenIndirectDiffuseSpatialDenoise
    {
        Legacy,
        [InspectorName("À-Trous (2 passes)")] Atrous2 = 2,
        [InspectorName("À-Trous (3 passes)")] Atrous3 = 3,
        [InspectorName("À-Trous (4 passes)")] Atrous4 = 4
    }

    [Serializable]
    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseSpatialDenoiseParameter")]
    public sealed class LumenIndirectDiffuseSpatialDenoiseParameter : VolumeParameter<LumenIndirectDiffuseSpatialDenoise>
    {
        public LumenIndirectDiffuseSpatialDenoiseParameter(LumenIndirectDiffuseSpatialDenoise value)
            : base(value) { }
    }

    public enum SdfScreenProbeSpatialKernel
    {
        Cross,
        [InspectorName("3 × 3")] Neighborhood3x3,
        [InspectorName("Adaptive 5 × 5")] Adaptive5x5
    }

    [Serializable]
    public sealed class SdfScreenProbeSpatialKernelParameter : VolumeParameter<SdfScreenProbeSpatialKernel>
    {
        public SdfScreenProbeSpatialKernelParameter(SdfScreenProbeSpatialKernel value) : base(value) { }
    }

    [Serializable, VolumeComponentMenu("VividRP/Lighting/Lumen Indirect Diffuse")]
    [MovedFrom(true, "VividRP.Runtime.RenderPass.Core", "VividRP.Runtime", "SdfDiffuseGISettings")]
    public sealed class LumenIndirectDiffuse : VolumeComponent
    {
        [Tooltip("Enable Lumen Indirect Diffuse GI and Reflections for cameras using the pipeline asset's Realtime GI mode.")]
        public BoolParameter enabled = new(true);

        [Tooltip("Choose the final gather independently of the trace mode. Screen Probe Gather filters directional probe radiance. ReSTIR Gather resamples 2×2 reservoirs and requires a usable Mesh SDF scene.")]
        public LumenIndirectDiffuseGatherMethodParameter gatherMethod = new(LumenIndirectDiffuseGatherMethod.ScreenProbeGather);

        // Existing profiles retain their reference until the Editor migrates it to the owning scene.
        [SerializeField, HideInInspector]
        internal VividSdfSurfaceCacheParameter surfaceCache = new(null);

        // Retain serialization for profiles created before direct lighting moved to FinalLighting.
        [HideInInspector]
        public BoolParameter cacheSunVisibility = new(false);

        [Tooltip("Additional diffuse bounce orders propagated through the Surface Cache. Zero uses direct light and emission at SDF hits.")]
        public ClampedIntParameter surfaceCacheBounceOrders = new(3, 0, 8);

        [Tooltip("Use Global SDF and continue indirect lighting in one feedback atlas when the graph provides Global SDF cards. Bounce orders above zero enable feedback.")]
        public BoolParameter surfaceCacheRadiosityFeedback = new(true);

        [Tooltip("Cosine rays per radiosity texel and bounce sweep. Higher counts reduce fixed Surface Cache sampling error; use a smaller card update budget for expensive reference solves.")]
        public ClampedIntParameter surfaceCacheRadiositySamples = new(32, 16, 4096);

        [Tooltip("Keep a separate estimate for each bounce order and refine it with new sample batches. Lighting changes restart sample accumulation; progressive publishing can retain compatible incident lighting during the rebuild.")]
        public BoolParameter surfaceCacheRadiosityAccumulate = new(true);

        [Tooltip("Retain compatible incident lighting while rebuilding. Cold starts and large changes publish low orders progressively; small changes retain higher bounces until fresh target-order cards arrive. Later sample batches publish at target order. Incompatible card identities discard history.")]
        public BoolParameter surfaceCacheProgressiveRadiosity = new(true);

        [Tooltip("Relative RGB energy deadband for cached lighting (0.005 = 0.5%). Compare radiosity to the last accepted lighting input so a gradual change eventually triggers an update. Sky content and card identities still invalidate explicitly.")]
        public ClampedFloatParameter surfaceCacheLightingTolerance = new(0.005f, 0f, 0.01f);

        [Tooltip("Temporary card-budget multiplier after lighting/geometry changes or fast camera motion. One disables the boost. Small-scene full updates and the remaining-card limit still apply.")]
        public ClampedIntParameter surfaceCacheUpdateBoost = new(2, 1, 4);

        [Tooltip("Maximum bounce sweeps in a relighting frame for a compatible small scene. One retains incremental rebuilding. Only the first sample batch is accelerated; the existing card limit and a bounded ray-work budget still apply.")]
        public ClampedIntParameter surfaceCacheRelightSweeps = new(8, 1, 8);

        [Tooltip("Independent sample batches per bounce order before the static radiosity cache stops updating. Rays per texel are samples per batch times this limit.")]
        public ClampedIntParameter surfaceCacheRadiosityBatchLimit = new(4, 1, 4096);

        [Tooltip("Base cards updated per frame in a large Surface Cache. Dynamic boost can temporarily raise this budget. One complete sweep updates a bounce order.")]
        public ClampedIntParameter surfaceCacheCardsPerFrame = new(16, 1, 4096);

        [Tooltip("Update a complete bounce sweep per frame when the scene has at most this many cards. Zero disables the small-scene fast fill. Large scenes retain the per-frame card budget.")]
        public ClampedIntParameter surfaceCacheFullUpdateCardLimit = new(128, 0, 4096);

        [Tooltip("Sample local lights (Point, Spot, Rectangle, Tube) into the Surface Cache via world-space Onion ReGIR and single-pass multi-iteration RIS.")]
        public BoolParameter surfaceCacheSampledLighting = new(true);

        [Tooltip("Use DXR 1.1 Inline RayQuery against the scene RTAS for Surface Cache direct-lighting visibility when available. Automatically falls back to segmented Software SDF tracing otherwise.")]
        public BoolParameter surfaceCacheSampledLightingHardwareRayTracing = new(true);

        [Tooltip("Number of independent RIS shadow-ray iterations per Surface Cache card texel when sampling more than 4 local lights (NvRTX default: 4).")]
        public ClampedIntParameter surfaceCacheSampledLightingIterations = new(4, 1, 8);

        [Tooltip("Number of local-light candidates evaluated per RIS iteration from the Onion ReGIR cell reservoirs (NvRTX default: 4).")]
        public ClampedIntParameter surfaceCacheSampledLightingCandidates = new(4, 1, 16);

        [Tooltip("Add published card indirect lighting to screen hits that match the same Mesh SDF surface.")]
        public BoolParameter screenHitCardLighting = new(true);

        [Tooltip("Trace the full-sphere screen probes through the closest HZB and sample the previous pre-postprocessing SceneColor.")]
        public BoolParameter lumenScreenTracing = new(true);

        [Tooltip("Selects the tracing backend. Hybrid combines screen-space HZB traces with Mesh SDF and Global SDF tracing (Lumen default).")]
        public LumenIndirectDiffuseTraceModeParameter traceMode = new(LumenIndirectDiffuseTraceMode.Hybrid);

        [Tooltip("Split the SDF trace into detail, clipmap-assisted and far segments. Reconstruct far lighting at the confirmed first hit.")]
        public BoolParameter segmentedSdfTracing = new(true);

        [Tooltip("Length of the initial Mesh SDF detail segment in world units.")]
        public MinFloatParameter meshSdfDetailDistance = new(2f, 0.1f);

        [Tooltip("Sample the sky on confirmed Mesh SDF escapes. ReSTIR requires 2x2 integration and Far Radiance Cache. Physically Based Sky requires a cubemap baked without the sun disk.")]
        public BoolParameter occludedSky = new(true);

        [Tooltip("Maximum sky visibility distance. A ray that reaches the limit inside the SDF scene is unresolved.")]
        public MinFloatParameter skyTraceDistance = new(64f, 1f);

        [Tooltip("Use a budgeted world-space directional Surface Cache for rays that leave the near trace range.")]
        public BoolParameter farRadianceCache = new(true);

        [Tooltip("Maximum distance from the receiver for far radiance cache and sky visibility traces.")]
        public MinFloatParameter farTraceDistance = new(64f, 1f);

        [Tooltip("Strength of short-range ambient occlusion on realtime indirect lighting.")]
        public ClampedFloatParameter nearOcclusionStrength = new(1f, 0f, 1f);

        [Tooltip("Apply directional near-field visibility during diffuse integration. Uses the receiver shading normal and keeps GI coverage separate.")]
        public BoolParameter directionalNearOcclusion = new(false);

        [Tooltip("Use half-resolution horizon search and a separate per-camera bent-normal history for Screen Probe Gather. Overrides directional near occlusion.")]
        public BoolParameter screenProbeBentNormalAO = new(true);

        [Tooltip("Use Lumen's full-resolution short-range AO during SH3 integration. With this disabled, Bent Normal AO uses its separate history.")]
        public BoolParameter screenProbeBentNormalApplyDuringIntegration = new(false);

        [Tooltip("World-space radius for scalar and directional contact occlusion. Bent Normal AO uses two screen probe spacings.")]
        public ClampedFloatParameter nearOcclusionRadius = new(0.35f, 0.05f, 1f);

        [HideInInspector]
        public BoolParameter directionalScreenProbes = new(false);

        [HideInInspector]
        public BoolParameter screenProbeDirectionalCache = new(false);

        [Tooltip("Trace a world-space 8×8 equal-area octahedron per Screen Probe. Disable for the eight-ray cosine hemisphere.")]
        public BoolParameter screenProbeFullSphere = new(true);

        [Tooltip("Stochastically scatter each traced direction to neighboring octahedral texels before filtering.")]
        public BoolParameter screenProbeDitherTraceDirections = new(true);

        [Tooltip("Use Lumen full-tile blue-noise jitter and pick one weighted probe per pixel for SH3 integration.")]
        public BoolParameter screenProbeStochasticInterpolation = new(true);

        [Tooltip("Blend the previous probe radiance before spatial filtering. The lighting sampling guide retains its own filtered history.")]
        public BoolParameter screenProbeTemporalFilter = new(false);

        [Tooltip("Maximum weighted ray channel in pre-exposed space before scattering. Zero disables the limit.")]
        public MinFloatParameter screenProbeMaxRayIntensity = new(10f, 0f);

        [Tooltip("Use reprojected radiance to subdivide bright directions while retaining cosine coverage across the hemisphere.")]
        public BoolParameter screenProbeSamplingGuide = new(true);

        [Tooltip("Refine a hierarchy of octahedral directions. Use Far Radiance Cache, when enabled, if screen probe history is unavailable.")]
        public BoolParameter screenProbeHierarchicalGuide = new(false);

        [Tooltip("Concentrate Screen Probe rays onto high-variance, high-contribution directions via multi-level structured importance sampling and cull unneeded back-facing rays on converged probes.")]
        public BoolParameter screenProbeAdaptiveRayBudget = new(true);

        [Tooltip("Maximum structured importance sampling subdivision depth for full-sphere Screen Probes (1 = up to 4×, 2 = up to 16× on high-variance peaks).")]
        public ClampedIntParameter screenProbeImportanceSubdivisionLevels = new(2, 1, 2);

        [Tooltip("Weight of directional radiance standard deviation (temporal + octahedral angular) added to mean radiance in the Screen Probe importance PDF.")]
        public ClampedFloatParameter screenProbeImportanceVarianceWeight = new(1.0f, 0.0f, 4.0f);

        [Tooltip("Minimum active ray budget fraction (relative to 64 rays/probe) for converged low-variance probes. Active front-facing cells are always preserved.")]
        public ClampedFloatParameter screenProbeMinAdaptiveRayFraction = new(0.5f, 0.25f, 1.0f);

        [Tooltip("Trace one additional probe per tile with weak geometric support. Enabled by default with Screen Probe Gather.")]
        public BoolParameter adaptiveScreenProbes = new(true);

        [Tooltip("Filter neighboring screen probes before resolving diffuse GI. Enabled by default with Screen Probe Gather.")]
        public BoolParameter screenProbeSpatialFilter = new(true);

        [Tooltip("Legacy probe-grid filter footprint. Screen Probe Gather uses three directional cross passes; the ray-pair hit-distance filter also uses the cross footprint.")]
        public SdfScreenProbeSpatialKernelParameter screenProbeSpatialKernel = new(SdfScreenProbeSpatialKernel.Neighborhood3x3);

        // Retained for serialized profile compatibility; directional radiance replaces scalar anchors.
        [HideInInspector]
        public BoolParameter screenProbeSpatialAnchor = new(false);

        [HideInInspector]
        public BoolParameter screenProbeAnchorPreFilter = new(false);

        // Retained for serialized profiles and legacy probe-anchor diagnostics.
        [HideInInspector]
        public ClampedIntParameter screenProbeAnchorMaxAge = new(32, 32, 512);

        [Tooltip("Downweight neighboring probes when rays in matching world directions disagree on hit distance. Requires Screen Probe Spatial Filter.")]
        public BoolParameter screenProbeHitDistanceFilter = new(false);

        [HideInInspector]
        public BoolParameter restirGather = new(false);

        [HideInInspector]
        public BoolParameter restirPixelIntegration = new(false);

        [Tooltip("Evaluate the native OpenPBR opaque diffuse BRDF per direction during 2x2 ReSTIR integration. Requires the three OpenPBR GBuffer inputs.")]
        public BoolParameter restirOpenPbrDiffuse = new(true);

        [Tooltip("Reproject the resolved diffuse lighting using motion vectors. Enabled by default for either Gather Method when motion is connected.")]
        public BoolParameter pixelTemporal = new(true);

        [Tooltip("Maximum age of stable full-resolution diffuse GI history. Motion, coverage loss and shading changes shorten the window.")]
        public ClampedIntParameter pixelHistoryMaxAge = new(10, 8, 32);

        [Tooltip("Clamp fast-updating Screen Probe pixel history to the 3x3 current resolved lighting neighborhood when traces hit moving geometry. Disabled by default to match UE Lumen.")]
        public BoolParameter screenProbeFastUpdateNeighborhoodClamp = new(false);

        [Tooltip("Reject Screen Probe pixel temporal history when the reprojected normal differs by more than 60 degrees. Enabled by default to prevent cross-surface bleed behind foreground edges while tolerating normal-map variation.")]
        public BoolParameter screenProbeTemporalRejectBasedOnNormal = new(true);

        [Tooltip("Use world-space plane distance instead of linear scene depth with grazing-angle expansion for Screen Probe pixel temporal disocclusion. Enabled by default to prevent grazing-wall false disocclusion and occluder bleed.")]
        public BoolParameter screenProbeTemporalPlaneDisocclusion = new(true);

        [Tooltip("Boost fast-update response and apply tonemapped YCoCg soft clamping during dynamic directional-light or sky transitions, while remaining inactive under steady lighting to avoid static probe-tile oscillation noise.")]
        public BoolParameter screenProbeCoherentRadianceFastUpdate = new(true);

        [Tooltip("Apply a lightweight geometry-aware 3x3 bilateral spatial filter on newly disoccluded Screen Probe pixels (history age < 2) to suppress single-frame stochastic probe noise during camera motion.")]
        public BoolParameter screenProbeDisocclusionSpatialFilter = new(true);

        [Tooltip("Integrate occluded directional Rough Specular and Sky Visibility from Screen Probe octahedral radiance (UE Lumen RoughSpecularIndirect) to replace unoccluded Sky IBL indoors.")]
        public BoolParameter screenProbeRoughSpecular = new(true);

        [Header("Lumen Specular Reflections")]
        [Tooltip("Enable UE5 Lumen dedicated specular reflections (Software SDF: Screen Trace + Mesh SDF + Global SDF + Native Temporal/Spatial Denoiser).")]
        public BoolParameter lumenReflections = new(true);

        [Tooltip("Downsample factor for reflection ray tracing (1 = full resolution, 2 = 2×2 half resolution with 4-rooks jitter and upsample).")]
        public ClampedIntParameter lumenReflectionDownsampleFactor = new(1, 1, 2);

        [Tooltip("Max perceptual roughness for which Lumen traces dedicated reflection rays (UE5 default 0.4). Rougher surfaces transition smoothly to Rough Specular.")]
        public ClampedFloatParameter lumenReflectionMaxRoughnessToTrace = new(0.4f, 0.0f, 1.0f);

        [Tooltip("Max perceptual roughness for foliage/subsurface pixels to trace dedicated reflection rays (UE5 default 0.2).")]
        public ClampedFloatParameter lumenReflectionMaxRoughnessToTraceForFoliage = new(0.2f, 0.0f, 1.0f);

        [Tooltip("Roughness transition width over which dedicated reflections fade into Rough Specular (UE5 default 0.1).")]
        public ClampedFloatParameter lumenReflectionRoughnessFadeLength = new(0.1f, 0.001f, 1.0f);

        [Tooltip("Global roughness smooth-bias applied to Lumen Reflections (0 = physically based, >0 biases smooth materials toward mirror).")]
        public ClampedFloatParameter lumenReflectionSmoothBias = new(0.0f, 0.0f, 1.0f);

        [Tooltip("Bias VNDF GGX sampling away from the tail to reduce fireflies (UE5 default 0.1).")]
        public ClampedFloatParameter lumenReflectionGGXSamplingBias = new(0.1f, 0.0f, 0.99f);

        [Tooltip("Maximum pre-exposed ray radiance intensity clamp for reflection rays (UE5 default 40, 0 disables).")]
        public MinFloatParameter lumenReflectionMaxRayIntensity = new(40.0f, 0.0f);

        [Tooltip("Enable HZB screen-space tracing against previous SceneColor before falling back to Mesh SDF / Global SDF.")]
        public BoolParameter lumenReflectionScreenTraces = new(true);

        [Tooltip("Enable near-field Mesh SDF + Surface Cache tracing for reflection rays that miss screen space.")]
        public BoolParameter lumenReflectionMeshSdfTraces = new(true);

        [Tooltip("Enable UE5 screen-space BRDF reweighting spatial reconstruction in the resolve pass.")]
        public BoolParameter lumenReflectionScreenSpaceReconstruction = new(true);

        [Tooltip("Number of neighbor rays sampled during BRDF reweighting spatial reconstruction (UE5 default 5).")]
        public ClampedIntParameter lumenReflectionReconstructionSamples = new(5, 1, 16);

        [Tooltip("Kernel radius in pixels for screen-space BRDF spatial reconstruction (UE5 default 8).")]
        public ClampedFloatParameter lumenReflectionReconstructionKernelRadius = new(8.0f, 1.0f, 32.0f);

        [Tooltip("Enable UE5 native Lumen Reflection temporal denoiser (virtual hit-point reprojection + YCoCg variance clamp).")]
        public BoolParameter lumenReflectionTemporalFilter = new(true);

        [Tooltip("Maximum accumulated frames in the reflection temporal denoiser (UE5 default 12; mirror surfaces automatically converge faster).")]
        public ClampedFloatParameter lumenReflectionTemporalMaxFramesAccumulated = new(12.0f, 1.0f, 32.0f);

        [Tooltip("YCoCg neighborhood variance clamp scale (UE5 default 1.0). Higher reduces noise, lower reduces ghosting.")]
        public ClampedFloatParameter lumenReflectionTemporalNeighborhoodClampScale = new(1.0f, 0.25f, 4.0f);

        [Tooltip("Relative depth disocclusion threshold for surface reprojection (UE5 default 0.03).")]
        public ClampedFloatParameter lumenReflectionTemporalDistanceThreshold = new(0.03f, 0.001f, 0.2f);

        [Tooltip("Tonemap range in pre-exposed space during reflection denoising (UE5 default 10, 0 disables).")]
        public MinFloatParameter lumenReflectionDenoiserTonemapRange = new(10.0f, 0.0f);

        [Tooltip("Enable UE5 variance-guided bilateral spatial filter after temporal accumulation.")]
        public BoolParameter lumenReflectionBilateralFilter = new(true);

        [Tooltip("Kernel radius in pixels for the bilateral spatial filter (UE5 default 8).")]
        public ClampedFloatParameter lumenReflectionBilateralFilterKernelRadius = new(8.0f, 1.0f, 32.0f);

        [Tooltip("Number of bilateral filter samples (UE5 default 4).")]
        public ClampedIntParameter lumenReflectionBilateralFilterNumSamples = new(4, 0, 16);

        [Tooltip("Depth plane weight scale for the bilateral spatial filter (UE5 default 10000).")]
        public MinFloatParameter lumenReflectionBilateralFilterDepthWeightScale = new(10000.0f, 1.0f);

        [Tooltip("Filter resolved diffuse GI at full resolution. Legacy uses the native Screen Probe filters and skips extra À-Trous passes.")]
        public LumenIndirectDiffuseSpatialDenoiseParameter spatialDenoise = new(LumenIndirectDiffuseSpatialDenoise.Legacy);

        [Tooltip("Resolve thin-shell self hits using the matching GPU-driven visibility and depth buffers.")]
        public BoolParameter correctReceiverSelfHits = new(true);

        [Tooltip("Store per-ray radiance, hit distance and trace state for GPU inspection.")]
        public BoolParameter recordRayDiagnostics = new(false);

        [Tooltip("Include first-hit instance, ray origin sign and card rejection data in ray diagnostics.")]
        public BoolParameter recordFirstHitDiagnostics = new(false);

        [Tooltip("Show the covered realtime diffuse contribution with baked and ambient diffuse fallback suppressed.")]
        public BoolParameter realtimeOnly = new(false);

        [Tooltip("Diagnostic view that retains only bounce from hits outside the camera frustum.")]
        public BoolParameter offscreenBounceOnly = new(false);

        // Serialized profiles can retain the former gather options until a method is selected.
        internal bool UsesLegacyGatherSettings => !gatherMethod.overrideState
            && gatherMethod.value == LumenIndirectDiffuseGatherMethod.ScreenProbeGather
            && (restirGather.overrideState || restirGather.value
                || restirPixelIntegration.overrideState || restirPixelIntegration.value
                || directionalScreenProbes.overrideState || directionalScreenProbes.value
                || screenProbeDirectionalCache.overrideState);
    }
}
