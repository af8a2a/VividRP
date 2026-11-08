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

    [Serializable, VolumeComponentMenu("VividRP/Lighting/SDF Diffuse GI")]
    public sealed class SdfDiffuseGISettings : VolumeComponent
    {
        [Tooltip("Enable SDF diffuse GI in graphs that contain the gather pass.")]
        public BoolParameter enabled = new(true);

        [Tooltip("Optional baked orthographic surface cards. Uncached SDFs still occlude rays.")]
        public VividSdfSurfaceCacheParameter surfaceCache = new(null);

        [Tooltip("Update instance card visibility for the directional sun in bounded batches. Unresolved card texels trace shadows at the hit.")]
        public BoolParameter cacheSunVisibility = new(false);

        [Tooltip("Screen tracing requires final-depth HZB and motion vector inputs on the gather pass.")]
        public SdfDiffuseTraceModeParameter traceMode = new(SdfDiffuseTraceMode.SdfOnly);

        [Tooltip("Gather first-order directional lighting in the screen probes and evaluate it at the receiver normal.")]
        public BoolParameter directionalScreenProbes = new(false);

        [Tooltip("Trace one additional probe per tile where the uniform probes do not represent nearby geometry.")]
        public BoolParameter adaptiveScreenProbes = new(false);

        [Tooltip("Filter neighboring screen probes using depth and normal agreement before resolving diffuse GI.")]
        public BoolParameter screenProbeSpatialFilter = new(false);

        [Tooltip("Reproject the resolved indirect diffuse lighting with motion vectors instead of accumulating the probe grid.")]
        public BoolParameter pixelTemporal = new(false);

        [Tooltip("Resolve thin-shell self hits using the matching GPU-driven visibility and depth buffers.")]
        public BoolParameter correctReceiverSelfHits = new(false);

        [Tooltip("Store per-ray radiance, hit distance and trace state for GPU inspection.")]
        public BoolParameter recordRayDiagnostics = new(false);

        [Tooltip("Include first-hit instance, ray origin sign and card rejection data in ray diagnostics.")]
        public BoolParameter recordFirstHitDiagnostics = new(false);

        [Tooltip("Use the realtime result even when no surface lighting was resolved. Useful for GI comparisons.")]
        public BoolParameter realtimeOnly = new(false);

        [Tooltip("Diagnostic view that retains only bounce from hits outside the camera frustum.")]
        public BoolParameter offscreenBounceOnly = new(false);
    }
}
