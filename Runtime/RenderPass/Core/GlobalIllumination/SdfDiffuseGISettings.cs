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

        [Tooltip("Screen tracing requires final-depth HZB and motion vector inputs on the gather pass.")]
        public SdfDiffuseTraceModeParameter traceMode = new(SdfDiffuseTraceMode.SdfOnly);

        [Tooltip("Use the realtime result even when no surface lighting was resolved. Useful for GI comparisons.")]
        public BoolParameter realtimeOnly = new(false);

        [Tooltip("Diagnostic view that retains only bounce from hits outside the camera frustum.")]
        public BoolParameter offscreenBounceOnly = new(false);
    }
}
