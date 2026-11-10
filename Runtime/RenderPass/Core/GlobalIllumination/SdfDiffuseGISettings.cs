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

    [Serializable, VolumeComponentMenu("VividRP/Lighting/SDF Diffuse GI")]
    public sealed class SdfDiffuseGISettings : VolumeComponent
    {
        [Tooltip("Enable SDF diffuse GI in graphs that contain the gather pass.")]
        public BoolParameter enabled = new(true);

        [Tooltip("Optional baked orthographic surface cards. Uncached SDFs still occlude rays.")]
        public VividSdfSurfaceCacheParameter surfaceCache = new(null);

        [Tooltip("Update instance card visibility for the directional sun in bounded batches. Unresolved card texels trace shadows at the hit.")]
        public BoolParameter cacheSunVisibility = new(false);

        [Tooltip("Additional diffuse bounce orders propagated through the Surface Cache. Zero uses direct light and emission at SDF hits.")]
        public ClampedIntParameter surfaceCacheBounceOrders = new(0, 0, 3);

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

        [Tooltip("Gather first-order directional lighting in the screen probes and evaluate it at the receiver normal.")]
        public BoolParameter directionalScreenProbes = new(false);

        [Tooltip("Cache incoming radiance and hit distance per direction before filtering 16 px directional screen probes.")]
        public BoolParameter screenProbeDirectionalCache = new(false);

        [Tooltip("Use reprojected radiance to subdivide bright directions while retaining cosine coverage across the hemisphere.")]
        public BoolParameter screenProbeSamplingGuide = new(true);

        [Tooltip("Refine a hierarchy of octahedral directions. Use Far Radiance Cache, when enabled, if screen probe history is unavailable.")]
        public BoolParameter screenProbeHierarchicalGuide = new(false);

        [Tooltip("Trace one additional probe per tile where the uniform probes do not represent nearby geometry.")]
        public BoolParameter adaptiveScreenProbes = new(false);

        [Tooltip("Filter neighboring screen probes using depth and normal agreement before resolving diffuse GI.")]
        public BoolParameter screenProbeSpatialFilter = new(false);

        [Tooltip("Downweight neighboring probes when rays in matching world directions disagree on hit distance. Requires Screen Probe Spatial Filter.")]
        public BoolParameter screenProbeHitDistanceFilter = new(false);

        [Tooltip("Use temporal and spatial reservoir resampling. Requires a Mesh SDF scene and replaces the screen probe spatial filters. Hybrid also requires ReSTIR Pixel Integration.")]
        public BoolParameter restirGather = new(false);

        [Tooltip("Generate one ReSTIR candidate per 2x2 pixels. Hybrid uses valid screen hits first and falls back to Mesh SDF. Integrate per pixel with variance-guided filtering.")]
        public BoolParameter restirPixelIntegration = new(false);

        [Tooltip("Evaluate the native OpenPBR opaque diffuse BRDF per direction during 2x2 ReSTIR integration. Requires the three OpenPBR GBuffer inputs.")]
        public BoolParameter restirOpenPbrDiffuse = new(true);

        [Tooltip("Reproject the resolved indirect diffuse lighting with motion vectors instead of accumulating the probe grid.")]
        public BoolParameter pixelTemporal = new(false);

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
    }
}
