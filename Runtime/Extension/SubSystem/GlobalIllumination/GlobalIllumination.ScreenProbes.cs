using System;

namespace UnityEngine.Rendering.Universal
{
    #region Screen Probe Enums and Parameters

    [Serializable]
    public enum ScreenProbeQuality
    {
        /// <summary>Low quality - 4x4 downsampling, 4x4 octahedral</summary>
        Low = 0,
        /// <summary>Medium quality - 4x4 downsampling, 6x6 octahedral</summary>
        Medium = 1,
        /// <summary>High quality - 4x4 downsampling, 8x8 octahedral</summary>
        High = 2,
        /// <summary>Ultra quality - 2x2 downsampling, 8x8 octahedral</summary>
        Ultra = 3
    }

    [Serializable]
    public sealed class ScreenProbeQualityParameter : VolumeParameter<ScreenProbeQuality>
    {
        public ScreenProbeQualityParameter(ScreenProbeQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    #endregion

    partial class GlobalIllumination
    {
        #region Screen Probes - General

        [Header("Screen Probes - General")]
        [Tooltip("Enable screen probes for near-field GI detail.")]
        public BoolParameter screenProbesEnabled = new BoolParameter(false);

        [Tooltip("Quality preset for screen probes.")]
        public ScreenProbeQualityParameter screenProbesQuality =
            new ScreenProbeQualityParameter(ScreenProbeQuality.Medium);

        [Tooltip("Intensity multiplier for screen probe indirect lighting.")]
        public ClampedFloatParameter screenProbesIntensity = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        #endregion

        #region Screen Probes - Tracing

        [Header("Screen Probes - Tracing")]
        [Tooltip("Maximum ray trace distance in world units (1-100).")]
        public ClampedFloatParameter screenProbesMaxRayDistance = new ClampedFloatParameter(50.0f, 1.0f, 100.0f);

        [Tooltip("Distance to switch from screen probes to surface cache (1-50).")]
        public ClampedFloatParameter screenProbesNearFieldDistance = new ClampedFloatParameter(10.0f, 1.0f, 50.0f);

        [Tooltip("Use BRDF-based importance sampling for better quality.")]
        [AdditionalProperty]
        public BoolParameter screenProbesUseImportanceSampling = new BoolParameter(true);

        [Tooltip("Use surface cache for far-field lighting fallback.")]
        public BoolParameter screenProbesUseSurfaceCacheFallback = new BoolParameter(true);

        #endregion

        #region Screen Probes - Temporal Filtering

        [Header("Screen Probes - Temporal Filtering")]
        [Tooltip("Temporal filter strength (0.0-1.0). Higher values = more stable but may ghost.")]
        public ClampedFloatParameter screenProbesTemporalFilterStrength = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);

        [Tooltip("Depth difference threshold for history rejection (0.01-0.5).")]
        [AdditionalProperty]
        public ClampedFloatParameter screenProbesDepthRejectionThreshold = new ClampedFloatParameter(0.05f, 0.01f, 0.5f);

        [Tooltip("Normal difference threshold for history rejection (0.01-0.5).")]
        [AdditionalProperty]
        public ClampedFloatParameter screenProbesNormalRejectionThreshold = new ClampedFloatParameter(0.1f, 0.01f, 0.5f);

        [Tooltip("Enable variance-based history clamping to reduce ghosting.")]
        [AdditionalProperty]
        public BoolParameter screenProbesEnableVarianceClamping = new BoolParameter(true);

        #endregion

        #region Screen Probes - Spatial Filtering

        [Header("Screen Probes - Spatial Filtering")]
        [Tooltip("Spatial filter radius in pixels for upsampling (1.0-10.0).")]
        public ClampedFloatParameter screenProbesSpatialFilterRadius = new ClampedFloatParameter(3.0f, 1.0f, 10.0f);

        [Tooltip("Number of spatial filter samples (4-16).")]
        [AdditionalProperty]
        public ClampedIntParameter screenProbesSpatialFilterSamples = new ClampedIntParameter(8, 4, 16);

        #endregion

        #region Screen Probes - Debug

        [Header("Screen Probes - Debug")]
        [Tooltip("Show screen probe visualization.")]
        [AdditionalProperty]
        public BoolParameter screenProbesDebugVisualization = new BoolParameter(false);

        [Tooltip("Show only screen probe contribution (no surface cache).")]
        [AdditionalProperty]
        public BoolParameter screenProbesDebugShowOnlyProbes = new BoolParameter(false);

        #endregion

        #region Helper Methods

        public bool IsScreenProbesEnabled()
        {
            return screenProbesEnabled.value;
        }

        public ScreenProbeQuality GetScreenProbesQuality()
        {
            return screenProbesQuality.value;
        }

        public void GetScreenProbeSettings(
            out uint downsampleFactor,
            out uint tracingResolution,
            out uint gatherResolution)
        {
            switch (screenProbesQuality.value)
            {
                case ScreenProbeQuality.Low:
                    downsampleFactor = 8;
                    tracingResolution = 4;
                    gatherResolution = 4;
                    break;
                case ScreenProbeQuality.Medium:
                    downsampleFactor = 4;
                    tracingResolution = 6;
                    gatherResolution = 6;
                    break;
                case ScreenProbeQuality.High:
                    downsampleFactor = 4;
                    tracingResolution = 8;
                    gatherResolution = 8;
                    break;
                case ScreenProbeQuality.Ultra:
                    downsampleFactor = 2;
                    tracingResolution = 8;
                    gatherResolution = 8;
                    break;
                default:
                    downsampleFactor = 4;
                    tracingResolution = 6;
                    gatherResolution = 6;
                    break;
            }
        }

        #endregion
    }
}
