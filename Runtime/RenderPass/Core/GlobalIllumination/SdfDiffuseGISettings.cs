using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.GPUDriven.Meshlets;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum SdfDiffuseTraceMode
    {
        SdfOnly,
        ScreenOnly,
        Hybrid
    }

    [Serializable]
    public sealed class SdfDiffuseTraceModeParameter : VolumeParameter<SdfDiffuseTraceMode>
    {
        public SdfDiffuseTraceModeParameter(SdfDiffuseTraceMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    public enum SdfDiffuseGatherMethod
    {
        [InspectorName("Screen Probe Gather")]
        ScreenProbeGather,
        [InspectorName("ReSTIR Gather (2 × 2)")]
        ReSTIRGather
    }

    [Serializable]
    public sealed class SdfDiffuseGatherMethodParameter : VolumeParameter<SdfDiffuseGatherMethod>
    {
        public SdfDiffuseGatherMethodParameter(SdfDiffuseGatherMethod value) : base(value) { }
    }

    public enum SdfDiffuseSpatialDenoise
    {
        Legacy,
        [InspectorName("À-Trous (2 passes)")] Atrous2 = 2,
        [InspectorName("À-Trous (3 passes)")] Atrous3 = 3
    }

    [Serializable]
    public sealed class SdfDiffuseSpatialDenoiseParameter : VolumeParameter<SdfDiffuseSpatialDenoise>
    {
        public SdfDiffuseSpatialDenoiseParameter(SdfDiffuseSpatialDenoise value)
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

    [Serializable, VolumeComponentMenu("VividRP/Lighting/SDF Diffuse GI")]
    public sealed class SdfDiffuseGISettings : VolumeComponent
    {
        [Tooltip("Enable SDF diffuse GI in graphs that contain the gather pass.")]
        public BoolParameter enabled = new(true);

        [Tooltip("Choose the final gather independently of the trace mode. Screen Probe Gather filters directional probe radiance. ReSTIR Gather resamples 2×2 reservoirs and requires a usable Mesh SDF scene.")]
        public SdfDiffuseGatherMethodParameter gatherMethod = new(SdfDiffuseGatherMethod.ScreenProbeGather);

        [Tooltip("Optional baked orthographic surface cards. Uncached SDFs still occlude rays.")]
        public VividSdfSurfaceCacheParameter surfaceCache = new(null);

        [Tooltip("Update instance card visibility for the directional sun in bounded batches. Unresolved card texels trace shadows at the hit.")]
        public BoolParameter cacheSunVisibility = new(false);

        [Tooltip("Additional diffuse bounce orders propagated through the Surface Cache. Zero uses direct light and emission at SDF hits.")]
        public ClampedIntParameter surfaceCacheBounceOrders = new(0, 0, 8);

        [Tooltip("Cosine rays per radiosity texel and bounce sweep. Higher counts reduce fixed Surface Cache sampling error; use a smaller card update budget for expensive reference solves.")]
        public ClampedIntParameter surfaceCacheRadiositySamples = new(16, 16, 4096);

        [Tooltip("Maximum cards updated per frame in a large Surface Cache. Each complete sweep publishes one additional diffuse bounce order.")]
        public ClampedIntParameter surfaceCacheCardsPerFrame = new(16, 1, 4096);

        [Tooltip("Update a complete bounce sweep per frame when the scene has at most this many cards. Zero disables the small-scene fast fill. Large scenes retain the per-frame card budget.")]
        public ClampedIntParameter surfaceCacheFullUpdateCardLimit = new(128, 0, 4096);

        [Tooltip("Add published card indirect lighting to screen hits that match the same Mesh SDF surface.")]
        public BoolParameter screenHitCardLighting = new(true);

        [Tooltip("Screen tracing requires final-depth HZB and motion vector inputs on the gather pass.")]
        public SdfDiffuseTraceModeParameter traceMode = new(SdfDiffuseTraceMode.SdfOnly);

        [Tooltip("Split the SDF trace into detail, clipmap-assisted and far segments. Reconstruct far lighting at the confirmed first hit.")]
        public BoolParameter segmentedSdfTracing = new(false);

        [Tooltip("Length of the initial Mesh SDF detail segment in world units.")]
        public MinFloatParameter meshSdfDetailDistance = new(2f, 0.1f);

        [Tooltip("Sample the sky on confirmed Mesh SDF escapes. ReSTIR requires 2x2 integration and Far Radiance Cache. Physically Based Sky requires a cubemap baked without the sun disk.")]
        public BoolParameter occludedSky = new(false);

        [Tooltip("Maximum sky visibility distance. A ray that reaches the limit inside the SDF scene is unresolved.")]
        public MinFloatParameter skyTraceDistance = new(64f, 1f);

        [Tooltip("Use a budgeted world-space directional Surface Cache for rays that leave the near trace range.")]
        public BoolParameter farRadianceCache = new(false);

        [Tooltip("Maximum distance from the receiver for a far-cache hit.")]
        public MinFloatParameter farTraceDistance = new(64f, 1f);

        [Tooltip("Strength of current-frame contact occlusion on realtime diffuse GI.")]
        public ClampedFloatParameter nearOcclusionStrength = new(0f, 0f, 1f);

        [Tooltip("Apply directional near-field visibility during diffuse integration. Uses the receiver shading normal and keeps GI coverage separate.")]
        public BoolParameter directionalNearOcclusion = new(true);

        [Tooltip("World-space radius of realtime diffuse contact occlusion.")]
        public ClampedFloatParameter nearOcclusionRadius = new(0.35f, 0.05f, 1f);

        [HideInInspector]
        public BoolParameter directionalScreenProbes = new(false);

        [HideInInspector]
        public BoolParameter screenProbeDirectionalCache = new(false);

        [Tooltip("Use reprojected radiance to subdivide bright directions while retaining cosine coverage across the hemisphere.")]
        public BoolParameter screenProbeSamplingGuide = new(true);

        [Tooltip("Refine a hierarchy of octahedral directions. Use Far Radiance Cache, when enabled, if screen probe history is unavailable.")]
        public BoolParameter screenProbeHierarchicalGuide = new(false);

        [Tooltip("Trace one additional probe per tile with weak geometric support. Enabled by default with Screen Probe Gather.")]
        public BoolParameter adaptiveScreenProbes = new(false);

        [Tooltip("Filter neighboring screen probes before resolving diffuse GI. Enabled by default with Screen Probe Gather.")]
        public BoolParameter screenProbeSpatialFilter = new(false);

        [Tooltip("Probe-grid filter footprint. Adaptive 5x5 adds an outer ring for weak probes. The ray-pair hit-distance filter uses the cross footprint.")]
        public SdfScreenProbeSpatialKernelParameter screenProbeSpatialKernel = new(SdfScreenProbeSpatialKernel.Neighborhood3x3);

        [Tooltip("Experimental conservative reconstruction of the SH energy anchor on connected coplanar screen probes. Keeps coverage and unfiltered temporal history separate. Screen Probe Gather only.")]
        public BoolParameter screenProbeSpatialAnchor = new(false);

        [Tooltip("Downweight neighboring probes when rays in matching world directions disagree on hit distance. Requires Screen Probe Spatial Filter.")]
        public BoolParameter screenProbeHitDistanceFilter = new(false);

        [HideInInspector]
        public BoolParameter restirGather = new(false);

        [HideInInspector]
        public BoolParameter restirPixelIntegration = new(false);

        [Tooltip("Evaluate the native OpenPBR opaque diffuse BRDF per direction during 2x2 ReSTIR integration. Requires the three OpenPBR GBuffer inputs.")]
        public BoolParameter restirOpenPbrDiffuse = new(true);

        [Tooltip("Reproject the resolved diffuse lighting using motion vectors. Enabled by default for either Gather Method when motion is connected.")]
        public BoolParameter pixelTemporal = new(false);

        [Tooltip("Maximum age of stable full-resolution diffuse GI history. Motion, coverage loss and shading changes shorten the window.")]
        public ClampedIntParameter pixelHistoryMaxAge = new(16, 8, 32);

        [Tooltip("Filter resolved diffuse GI at full resolution. Legacy uses the existing ReSTIR bilateral filter and skips filtering for ordinary probes.")]
        public SdfDiffuseSpatialDenoiseParameter spatialDenoise = new(SdfDiffuseSpatialDenoise.Atrous2);

        [Tooltip("Resolve thin-shell self hits using the matching GPU-driven visibility and depth buffers.")]
        public BoolParameter correctReceiverSelfHits = new(false);

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
            && gatherMethod.value == SdfDiffuseGatherMethod.ScreenProbeGather
            && (restirGather.overrideState || restirGather.value
                || restirPixelIntegration.overrideState || restirPixelIntegration.value
                || directionalScreenProbes.overrideState || directionalScreenProbes.value
                || screenProbeDirectionalCache.overrideState);
    }
}
