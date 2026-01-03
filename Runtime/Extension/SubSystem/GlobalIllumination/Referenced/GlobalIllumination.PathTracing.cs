using System;

namespace UnityEngine.Rendering.Universal
{
    #region Path Tracing Enums and Parameters

    /// <summary>
    /// Quality preset for path tracing
    /// </summary>
    [Serializable]
    public enum PathTracingQuality
    {
        /// <summary>Low quality - Fast, 1-2 bounces, suitable for real-time</summary>
        Low = 0,
        /// <summary>Medium quality - Balanced, 2-3 bounces</summary>
        Medium = 1,
        /// <summary>High quality - High fidelity, 4-6 bounces</summary>
        High = 2,
        /// <summary>Ultra quality - Maximum fidelity, 8+ bounces</summary>
        Ultra = 3,
        /// <summary>Custom - User defined settings</summary>
        Custom = 4
    }

    [Serializable]
    public sealed class PathTracingQualityParameter : VolumeParameter<PathTracingQuality>
    {
        public PathTracingQualityParameter(PathTracingQuality value, bool overrideState = false) 
            : base(value, overrideState) { }
    }

    /// <summary>
    /// Denoising mode for path tracing output
    /// </summary>
    [Serializable]
    public enum PathTracingDenoiseMode
    {
        /// <summary>No denoising - Raw path tracing output</summary>
        None = 0,
        /// <summary>Temporal accumulation only</summary>
        Temporal = 1,
        /// <summary>Spatial + Temporal (simple bilateral filter)</summary>
        SpatialTemporal = 2,
        /// <summary>NRD (NVIDIA Real-time Denoisers) - Best quality</summary>
        NRD = 3
    }

    [Serializable]
    public sealed class PathTracingDenoiseModeParameter : VolumeParameter<PathTracingDenoiseMode>
    {
        public PathTracingDenoiseModeParameter(PathTracingDenoiseMode value, bool overrideState = false) 
            : base(value, overrideState) { }
    }

    #endregion

    /// <summary>
    /// Path tracing specific settings for Referenced Path Tracing
    /// </summary>
    [VolumeRequiresRendererFeatures(typeof(RaytracingCoreFeature))]
    partial class GlobalIllumination
    {
        #region General Settings


        /// <summary>
        /// Quality preset for path tracing
        /// </summary>
        [Tooltip("Quality preset. Custom allows manual control of all parameters.")]
        public PathTracingQualityParameter pathTracingQuality = 
            new PathTracingQualityParameter(PathTracingQuality.Medium);

        /// <summary>
        /// Intensity/brightness multiplier for path traced GI
        /// </summary>
        [Tooltip("Global intensity multiplier for path traced indirect lighting.")]
        public ClampedFloatParameter pathTracingIntensity = new ClampedFloatParameter(1.0f, 0.0f, 10.0f);

        #endregion

        #region Ray Tracing Settings

        /// <summary>
        /// Maximum number of ray bounces
        /// </summary>
        [Tooltip("Maximum number of ray bounces. Higher values produce more accurate indirect lighting but are slower.")]
        public ClampedIntParameter maxBounces = new ClampedIntParameter(4, 1, 8);

        /// <summary>
        /// Number of samples per pixel per frame
        /// </summary>
        [Tooltip("Number of samples per pixel per frame. Higher values reduce noise but are slower. Use 1 with accumulation for progressive refinement.")]
        public ClampedIntParameter samplesPerPixel = new ClampedIntParameter(1, 1, 16);

        /// <summary>
        /// Maximum ray length in world units
        /// </summary>
        [Tooltip("Maximum ray length in world units. Longer rays can capture distant lighting but may be slower.")]
        public MinFloatParameter rayLength = new MinFloatParameter(100.0f, 0.1f);

        /// <summary>
        /// Layer mask for ray tracing
        /// </summary>
        [Tooltip("Defines which layers should be considered for path tracing.")]
        [AdditionalProperty]
        public LayerMaskParameter layerMask = new LayerMaskParameter(-1);

        #endregion

        #region Quality Settings

        /// <summary>
        /// Russian Roulette path termination
        /// </summary>
        [Tooltip("Enable Russian Roulette for early path termination. Improves performance with minimal quality loss.")]
        [AdditionalProperty]
        public BoolParameter useRussianRoulette = new BoolParameter(true);

        /// <summary>
        /// Russian Roulette minimum bounce before termination
        /// </summary>
        [Tooltip("Minimum number of bounces before Russian Roulette can terminate paths.")]
        [AdditionalProperty]
        public ClampedIntParameter russianRouletteStartBounce = new ClampedIntParameter(3, 1, 8);

        /// <summary>
        /// Firefly (high intensity pixel) clamping threshold
        /// </summary>
        [Tooltip("Maximum radiance value to prevent fireflies (bright noise pixels). Lower values reduce fireflies but may darken very bright surfaces.")]
        public ClampedFloatParameter fireflyClamp = new ClampedFloatParameter(10.0f, 0.0f, 100.0f);

        /// <summary>
        /// Use NVIDIA Shader Execution Reordering (SER) if available
        /// </summary>
        [Tooltip("Enable NVIDIA Shader Execution Reordering for improved ray coherence and performance on RTX GPUs.")]
        [AdditionalProperty]
        public BoolParameter useNVSER = new BoolParameter(true);

        /// <summary>
        /// LOD bias for texture sampling in path tracing
        /// </summary>
        [Tooltip("LOD bias for texture sampling. Higher values use lower resolution mipmaps, improving performance and reducing aliasing.")]
        [AdditionalProperty]
        public ClampedFloatParameter textureLODBias = new ClampedFloatParameter(0.5f, 0.0f, 4.0f);

        #endregion

        #region Temporal Accumulation

        /// <summary>
        /// Enable temporal accumulation across frames
        /// </summary>
        [Tooltip("Enable temporal accumulation for progressive refinement. Produces cleaner results over time but can cause ghosting with camera or object movement.")]
        public BoolParameter temporalAccumulation = new BoolParameter(true);

        /// <summary>
        /// Maximum number of accumulated frames
        /// </summary>
        [Tooltip("Maximum number of frames to accumulate. Higher values produce cleaner results but take longer to converge and may ghost more.")]
        public ClampedIntParameter maxAccumulatedFrames = new ClampedIntParameter(64, 1, 1024);

        /// <summary>
        /// Reset accumulation on camera movement
        /// </summary>
        [Tooltip("Reset temporal accumulation when camera moves. Prevents ghosting but may cause flickering during camera movement.")]
        [AdditionalProperty]
        public BoolParameter resetOnCameraMove = new BoolParameter(true);

        /// <summary>
        /// Camera movement threshold for accumulation reset
        /// </summary>
        [Tooltip("Minimum camera movement distance (in world units) to trigger accumulation reset.")]
        [AdditionalProperty]
        public ClampedFloatParameter cameraMovementThreshold = new ClampedFloatParameter(0.01f, 0.0f, 1.0f);

        #endregion

        #region Denoising

        /// <summary>
        /// Denoising mode
        /// </summary>
        [Tooltip("Denoising mode. None = raw output, Temporal = accumulation only, SpatialTemporal = bilateral filter, NRD = NVIDIA denoisers (best quality).")]
        public PathTracingDenoiseModeParameter denoiseMode = 
            new PathTracingDenoiseModeParameter(PathTracingDenoiseMode.Temporal);

        /// <summary>
        /// Spatial denoising radius
        /// </summary>
        [Tooltip("Radius for spatial denoising filter. Larger values produce smoother results but may blur details.")]
        public ClampedFloatParameter denoiseRadius = new ClampedFloatParameter(8.0f, 1.0f, 32.0f);

        /// <summary>
        /// Use NRD (NVIDIA Real-time Denoisers)
        /// </summary>
        [Tooltip("Use NVIDIA Real-time Denoisers for high-quality denoising. Requires NRD integration.")]
        [AdditionalProperty]
        public BoolParameter useNRD = new BoolParameter(false);

        #endregion

        #region Advanced Settings

        /// <summary>
        /// Environment lighting contribution
        /// </summary>
        [Tooltip("Intensity multiplier for environment/sky lighting in path tracing.")]
        [AdditionalProperty]
        public ClampedFloatParameter environmentIntensity = new ClampedFloatParameter(1.0f, 0.0f, 10.0f);

        /// <summary>
        /// Enable emissive materials in path tracing
        /// </summary>
        [Tooltip("Include emissive materials as light sources in path tracing. Enabling this allows emissive materials to contribute to global illumination.")]
        [AdditionalProperty]
        public BoolParameter includeEmissive = new BoolParameter(true);

        /// <summary>
        /// Enable direct lighting in path tracing
        /// </summary>
        [Tooltip("Evaluate direct lighting from WorldLightCluster at each bounce. Disabling this produces only indirect lighting (bounced light).")]
        [AdditionalProperty]
        public BoolParameter includeDirectLighting = new BoolParameter(true);

        /// <summary>
        /// Motion vector rejection for temporal accumulation
        /// </summary>
        [Tooltip("Reject accumulated samples for moving objects using motion vectors. Reduces ghosting but may introduce noise.")]
        [AdditionalProperty]
        public BoolParameter receiverMotionRejection = new BoolParameter(true);

        #endregion

        #region Debug Settings

        /// <summary>
        /// Visualize bounces only
        /// </summary>
        [Tooltip("Show only a specific bounce number for debugging. 0 = all bounces, 1 = first bounce only, etc.")]
        [AdditionalProperty]
        public ClampedIntParameter debugVisualizeBounce = new ClampedIntParameter(0, 0, 8);

        /// <summary>
        /// Show path tracing output only
        /// </summary>
        [Tooltip("Show only path tracing output without compositing with main rendering.")]
        [AdditionalProperty]
        public BoolParameter debugShowPathTracingOnly = new BoolParameter(false);

        #endregion

        /// <summary>
        /// Check if path tracing is active
        /// </summary>
        public bool IsPathTracingActive()
        {
            return technique.value == GlobalIlluminationTechnique.ReferencedPathTracing;
        }

        /// <summary>
        /// Get actual max bounces based on quality preset
        /// </summary>
        public int GetMaxBounces()
        {
            if (pathTracingQuality.value == PathTracingQuality.Custom)
                return maxBounces.value;

            return pathTracingQuality.value switch
            {
                PathTracingQuality.Low => 1,
                PathTracingQuality.Medium => 2,
                PathTracingQuality.High => 4,
                PathTracingQuality.Ultra => 8,
                _ => maxBounces.value
            };
        }

        /// <summary>
        /// Get actual samples per pixel based on quality preset
        /// </summary>
        public int GetSamplesPerPixel()
        {
            if (pathTracingQuality.value == PathTracingQuality.Custom)
                return samplesPerPixel.value;

            return pathTracingQuality.value switch
            {
                PathTracingQuality.Low => 1,
                PathTracingQuality.Medium => 1,
                PathTracingQuality.High => 2,
                PathTracingQuality.Ultra => 4,
                _ => samplesPerPixel.value
            };
        }

    }
}