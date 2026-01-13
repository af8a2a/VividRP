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
        /// <summary>No denoising - Raw path tracing output (noisy but fast)</summary>
        None = 0,
        /// <summary>Simple temporal accumulation with optional reprojection rejection</summary>
        TemporalAccumulation = 1,
        /// <summary>NRD REBLUR - High quality spatio-temporal denoising with separate diffuse/specular</summary>
        NRDReblur = 2,
        /// <summary>DLSS Ray Reconstruction - NVIDIA's AI-based ray tracing denoiser with upscaling</summary>
        RayReconstruction = 3
    }

    [Serializable]
    public sealed class PathTracingDenoiseModeParameter : VolumeParameter<PathTracingDenoiseMode>
    {
        public PathTracingDenoiseModeParameter(PathTracingDenoiseMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>
    /// Debug visualization mode for path tracing
    /// </summary>
    [Serializable]
    public enum PathTracingDebugMode
    {
        /// <summary>Normal rendering - Show path tracing result</summary>
        None = 0,
        /// <summary>Show accumulated frame count as heatmap</summary>
        FrameCount = 1,
        /// <summary>Show primary hit normals</summary>
        Normals = 2,
        /// <summary>Show primary hit albedo</summary>
        Albedo = 3,
        /// <summary>Show primary hit metallic</summary>
        Metallic = 4,
        /// <summary>Show primary hit roughness</summary>
        Roughness = 5,
        /// <summary>Show primary hit occlusion</summary>
        Occlusion = 6
    }

    [Serializable]
    public sealed class PathTracingDebugModeParameter : VolumeParameter<PathTracingDebugMode>
    {
        public PathTracingDebugModeParameter(PathTracingDebugMode value, bool overrideState = false)
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

        #region Denoising

        /// <summary>
        /// Denoising mode for path tracing output
        /// </summary>
        [Tooltip("Denoising mode. None = raw output (noisy), TemporalAccumulation = simple accumulation with reprojection, NRDReblur = NVIDIA REBLUR denoiser (best quality).")]
        public PathTracingDenoiseModeParameter denoiseMode =
            new PathTracingDenoiseModeParameter(PathTracingDenoiseMode.TemporalAccumulation);

        #endregion

        #region Temporal Accumulation Settings

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

        // --- History Reprojection Rejection ---

        /// <summary>
        /// Enable history reprojection with rejection
        /// </summary>
        [Tooltip("Enable motion-vector based history reprojection with disocclusion rejection. Improves temporal stability while reducing ghosting.")]
        public BoolParameter enableReprojectionRejection = new BoolParameter(true);

        /// <summary>
        /// Depth rejection threshold
        /// </summary>
        [Tooltip("Relative depth difference threshold for rejecting history. Higher values are more permissive. (0.01-0.5)")]
        [AdditionalProperty]
        public ClampedFloatParameter reprojectionDepthThreshold = new ClampedFloatParameter(0.05f, 0.01f, 0.5f);

        /// <summary>
        /// Normal rejection threshold
        /// </summary>
        [Tooltip("Normal difference threshold (1 - dot product) for rejecting history. Higher values are more permissive. (0.01-0.5)")]
        [AdditionalProperty]
        public ClampedFloatParameter reprojectionNormalThreshold = new ClampedFloatParameter(0.1f, 0.01f, 0.5f);

        /// <summary>
        /// Roughness rejection threshold
        /// </summary>
        [Tooltip("Roughness difference threshold for rejecting history. Higher values are more permissive. (0.05-0.5)")]
        [AdditionalProperty]
        public ClampedFloatParameter reprojectionRoughnessThreshold = new ClampedFloatParameter(0.2f, 0.05f, 0.5f);

        /// <summary>
        /// Enable variance clamping
        /// </summary>
        [Tooltip("Enable variance-based history clamping to reduce ghosting artifacts.")]
        [AdditionalProperty]
        public BoolParameter enableVarianceClamping = new BoolParameter(true);

        /// <summary>
        /// Variance clamping gamma
        /// </summary>
        [Tooltip("Gamma value for variance clamping. Higher values allow more variance. (0.5-3.0)")]
        [AdditionalProperty]
        public ClampedFloatParameter varianceClampingGamma = new ClampedFloatParameter(1.5f, 0.5f, 3.0f);

        /// <summary>
        /// Minimum blend weight
        /// </summary>
        [Tooltip("Minimum weight for current frame contribution. Prevents over-reliance on history. (0.01-0.5)")]
        [AdditionalProperty]
        public ClampedFloatParameter minTemporalBlendWeight = new ClampedFloatParameter(0.05f, 0.01f, 0.5f);

        #endregion

        #region NRD REBLUR Settings

        // --- Blur Radius Settings ---

        /// <summary>
        /// NRD minimum blur radius
        /// </summary>
        [Tooltip("Minimum blur radius for NRD REBLUR (in pixels). Controls the minimum denoising strength when converged.")]
        public ClampedFloatParameter nrdMinBlurRadius = new ClampedFloatParameter(1.0f, 0.0f, 10.0f);

        /// <summary>
        /// NRD maximum blur radius
        /// </summary>
        [Tooltip("Maximum blur radius for NRD REBLUR (in pixels). Controls the initial denoising strength before temporal convergence.")]
        public ClampedFloatParameter nrdMaxBlurRadius = new ClampedFloatParameter(30.0f, 1.0f, 60.0f);

        /// <summary>
        /// Diffuse prepass blur radius
        /// </summary>
        [Tooltip("Pre-accumulation spatial blur radius for diffuse signal (in pixels). Helps reduce noise before temporal accumulation.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdDiffusePrepassBlurRadius = new ClampedFloatParameter(30.0f, 0.0f, 75.0f);

        /// <summary>
        /// Specular prepass blur radius
        /// </summary>
        [Tooltip("Pre-accumulation spatial blur radius for specular signal (in pixels). Helps reduce noise before temporal accumulation.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdSpecularPrepassBlurRadius = new ClampedFloatParameter(50.0f, 0.0f, 75.0f);

        // --- Temporal Accumulation Settings ---

        /// <summary>
        /// NRD maximum accumulated frame count
        /// </summary>
        [Tooltip("Maximum number of frames for NRD temporal accumulation. Higher values produce smoother results but may ghost more. [1-63]")]
        public ClampedIntParameter nrdMaxAccumulatedFrameNum = new ClampedIntParameter(30, 1, 63);

        /// <summary>
        /// NRD maximum fast accumulated frame count
        /// </summary>
        [Tooltip("Maximum frames for fast history (responsive accumulation). Lower values respond faster to changes. [1-63]")]
        [AdditionalProperty]
        public ClampedIntParameter nrdMaxFastAccumulatedFrameNum = new ClampedIntParameter(6, 1, 63);

        /// <summary>
        /// NRD maximum stabilized frame count
        /// </summary>
        [Tooltip("Maximum frames for stabilized radiance. Controls temporal stability vs responsiveness. [1-63]")]
        [AdditionalProperty]
        public ClampedIntParameter nrdMaxStabilizedFrameNum = new ClampedIntParameter(0, 0, 63);

        /// <summary>
        /// History fix frame count
        /// </summary>
        [Tooltip("Number of frames to reconstruct history after disocclusion or reset. Higher values fill holes better but may blur.")]
        [AdditionalProperty]
        public ClampedIntParameter nrdHistoryFixFrameNum = new ClampedIntParameter(3, 0, 6);

        // --- Quality Settings ---

        /// <summary>
        /// NRD anti-firefly filter
        /// </summary>
        [Tooltip("Enable anti-firefly filter in NRD REBLUR to suppress bright noise pixels (fireflies).")]
        public BoolParameter nrdAntiFirefly = new BoolParameter(true);

        /// <summary>
        /// Firefly suppressor minimum relative scale
        /// </summary>
        [Tooltip("Minimum relative scale for firefly suppression. Higher values suppress more aggressively. [1.0-3.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdFireflySuppressorMinRelativeScale = new ClampedFloatParameter(1.0f, 1.0f, 3.0f);

        /// <summary>
        /// Fast history clamping sigma scale
        /// </summary>
        [Tooltip("Sigma scale for fast history clamping. Higher values allow more variance. [1.0-3.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdFastHistoryClampingSigmaScale = new ClampedFloatParameter(2.0f, 1.0f, 3.0f);

        /// <summary>
        /// Minimum hit distance weight
        /// </summary>
        [Tooltip("Minimum weight for hit distance in filtering. Lower values increase sensitivity to shadows. (0.0-0.2]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdMinHitDistanceWeight = new ClampedFloatParameter(0.1f, 0.01f, 0.2f);

        // --- Rejection Settings ---

        /// <summary>
        /// Lobe angle fraction for normal rejection
        /// </summary>
        [Tooltip("Fraction of lobe angle used for normal-based rejection. Higher values are more permissive. [0.0-1.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdLobeAngleFraction = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);

        /// <summary>
        /// Roughness fraction for rejection
        /// </summary>
        [Tooltip("Fraction of roughness used for roughness-based rejection. Higher values are more permissive. [0.0-1.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdRoughnessFraction = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);

        /// <summary>
        /// Plane distance sensitivity
        /// </summary>
        [Tooltip("Sensitivity to tangent plane deviation. Higher values reject more samples based on depth discontinuities. [0.0-1.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdPlaneDistanceSensitivity = new ClampedFloatParameter(0.005f, 0.0f, 0.1f);

        // --- Antilag Settings ---

        /// <summary>
        /// Antilag luminance sigma scale
        /// </summary>
        [Tooltip("Variance multiplier for antilag detection. Higher values are less sensitive to luminance changes. [1.0-5.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdAntilagLuminanceSigmaScale = new ClampedFloatParameter(4.0f, 1.0f, 5.0f);

        /// <summary>
        /// Antilag luminance sensitivity
        /// </summary>
        [Tooltip("Sensitivity of antilag to luminance differences. Higher values trigger antilag more easily. [1.0-5.0]")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdAntilagLuminanceSensitivity = new ClampedFloatParameter(3.0f, 1.0f, 5.0f);

        // --- Hit Distance Parameters ---

        /// <summary>
        /// Hit distance parameter A (constant)
        /// </summary>
        [Tooltip("Constant value for hit distance normalization (in world units). Affects near-field shadow sensitivity.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdHitDistanceA = new ClampedFloatParameter(3.0f, 0.0f, 50.0f);

        /// <summary>
        /// Hit distance parameter B (viewZ scale)
        /// </summary>
        [Tooltip("ViewZ-based linear scale for hit distance normalization. Affects distance-dependent shadow softness.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdHitDistanceB = new ClampedFloatParameter(0.1f, 0.0f, 1.0f);

        /// <summary>
        /// Hit distance parameter C (roughness scale)
        /// </summary>
        [Tooltip("Roughness-based scale for hit distance. Higher values make rough surfaces use longer hit distances.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdHitDistanceC = new ClampedFloatParameter(20.0f, 1.0f, 100.0f);

        /// <summary>
        /// Hit distance parameter D (roughness collapse)
        /// </summary>
        [Tooltip("Roughness collapse factor for hit distance. Negative values, affects very rough surface behavior.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdHitDistanceD = new ClampedFloatParameter(-25.0f, -100.0f, 0.0f);

        // --- Debug Settings ---

        /// <summary>
        /// NRD split screen visualization
        /// </summary>
        [Tooltip("Split screen visualization for NRD debugging. 0 = off, 0.5 = half screen shows unfiltered input.")]
        [AdditionalProperty]
        public ClampedFloatParameter nrdSplitScreen = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        #endregion

#if DLSS_PLUGIN_INTEGRATE
        #region DLSS-RR Settings

        /// <summary>
        /// DLSS-RR quality preset
        /// </summary>
        [Tooltip("DLSS Ray Reconstruction quality preset. Higher quality modes use lower internal resolution for better performance.")]
        public DLSSQualityParameter dlssRRQuality = new DLSSQualityParameter(DLSSQuality.Balanced);

        /// <summary>
        /// Hit distance scale for DLSS-RR
        /// </summary>
        [Tooltip("World-space scale for hit distances. Adjust based on scene scale (larger scenes need larger values).")]
        public ClampedFloatParameter dlssRRHitDistanceScale = new ClampedFloatParameter(1000.0f, 1.0f, 100000.0f);

        /// <summary>
        /// Pre-exposure value for DLSS-RR
        /// </summary>
        [Tooltip("Pre-exposure value for DLSS-RR input. Should match your rendering's pre-exposure if used.")]
        [AdditionalProperty]
        public ClampedFloatParameter dlssRRPreExposure = new ClampedFloatParameter(1.0f, 0.001f, 100.0f);

        /// <summary>
        /// Exposure scale for DLSS-RR auto-exposure
        /// </summary>
        [Tooltip("Exposure scale multiplier for DLSS-RR. Used when auto-exposure is enabled.")]
        [AdditionalProperty]
        public ClampedFloatParameter dlssRRExposureScale = new ClampedFloatParameter(1.0f, 0.001f, 100.0f);

        /// <summary>
        /// Force reset DLSS-RR history
        /// </summary>
        [Tooltip("Force reset DLSS-RR temporal history. Enable temporarily when scene changes significantly (teleportation, scene load, etc.).")]
        [AdditionalProperty]
        public BoolParameter dlssRRResetHistory = new BoolParameter(false);

        /// <summary>
        /// DLSS-RR sharpness
        /// </summary>
        [Tooltip("Sharpness applied to DLSS-RR output. 0 = no sharpening, 1 = maximum sharpening.")]
        [AdditionalProperty]
        public ClampedFloatParameter dlssRRSharpness = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Use auto-exposure for DLSS-RR
        /// </summary>
        [Tooltip("Enable auto-exposure handling in DLSS-RR. Should be enabled if your rendering uses auto-exposure.")]
        [AdditionalProperty]
        public BoolParameter dlssRRAutoExposure = new BoolParameter(false);

        /// <summary>
        /// Enable HDR input for DLSS-RR
        /// </summary>
        [Tooltip("Indicate that input is HDR (pre-tonemapped). Should be enabled for path tracing which outputs linear HDR radiance.")]
        [AdditionalProperty]
        public BoolParameter dlssRRIsHDR = new BoolParameter(true);

        /// <summary>
        /// DLSS-RR debug split screen
        /// </summary>
        [Tooltip("Split screen visualization for DLSS-RR debugging. 0 = off, 0.5 = half screen shows raw input.")]
        [AdditionalProperty]
        public ClampedFloatParameter dlssRRSplitScreen = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        #endregion
#endif

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
        /// Debug visualization mode
        /// </summary>
        [Tooltip("Debug visualization mode. None = normal path tracing, other modes show GBuffer data or accumulation info.")]
        [AdditionalProperty]
        public PathTracingDebugModeParameter debugMode =
            new PathTracingDebugModeParameter(PathTracingDebugMode.None);

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

        #region SHARC Settings (Spatially Hashed Radiance Cache)

        /// <summary>
        /// Enable SHARC for radiance caching
        /// </summary>
        [Tooltip("Enable SHARC (Spatially Hashed Radiance Cache) for accelerated path tracing convergence. SHARC caches radiance at hit points in a spatial hash grid.")]
        public BoolParameter enableSharc = new BoolParameter(false);

        /// <summary>
        /// SHARC update mode - write radiance to cache
        /// </summary>
        [Tooltip("Enable SHARC update mode. When enabled, path tracing will write radiance values to the cache.")]
        public BoolParameter sharcUpdate = new BoolParameter(true);

        /// <summary>
        /// SHARC query mode - read cached radiance for early termination
        /// </summary>
        [Tooltip("Enable SHARC query mode. When enabled, path tracing will query the cache for early path termination.")]
        public BoolParameter sharcQuery = new BoolParameter(true);

        /// <summary>
        /// SHARC scene scale
        /// </summary>
        [Tooltip("Scene scale for SHARC grid. Adjust based on your scene size. Larger values create finer grids.")]
        [AdditionalProperty]
        public ClampedFloatParameter sharcSceneScale = new ClampedFloatParameter(1.0f, 0.01f, 100.0f);

        /// <summary>
        /// SHARC hash entries count (in thousands)
        /// </summary>
        [Tooltip("Number of hash entries in SHARC cache (in thousands). Higher values allow more cached data but use more memory.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcEntriesK = new ClampedIntParameter(1024, 256, 8192);

        /// <summary>
        /// SHARC roughness threshold
        /// </summary>
        [Tooltip("Minimum roughness for SHARC update. Surfaces with lower roughness won't be cached during update pass.")]
        [AdditionalProperty]
        public ClampedFloatParameter sharcRoughnessThreshold = new ClampedFloatParameter(0.3f, 0.0f, 1.0f);

        /// <summary>
        /// SHARC radiance scale for quantization
        /// </summary>
        [Tooltip("Quantization factor for atomic radiance accumulation. Reduce for large radiance values to prevent overflow.")]
        [AdditionalProperty]
        public ClampedFloatParameter sharcRadianceScale = new ClampedFloatParameter(1000.0f, 100.0f, 10000.0f);

        /// <summary>
        /// SHARC propagation depth
        /// </summary>
        [Tooltip("Number of path vertices stored for backpropagation. Higher values improve cache quality but use more memory.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcPropagationDepth = new ClampedIntParameter(4, 1, 8);

        /// <summary>
        /// SHARC sample threshold for resampling
        /// </summary>
        [Tooltip("Minimum sample count for valid cache entry. Entries with fewer samples won't be used for early termination.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcSampleThreshold = new ClampedIntParameter(2, 0, 16);

        /// <summary>
        /// SHARC grid level bias
        /// </summary>
        [Tooltip("LOD bias for grid levels. Positive values add magnified levels, negative reduces levels.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcGridLevelBias = new ClampedIntParameter(0, -4, 4);

        /// <summary>
        /// SHARC anti-firefly filter
        /// </summary>
        [Tooltip("Enable anti-firefly filter for SHARC to reduce bright noise pixels in the cache.")]
        [AdditionalProperty]
        public BoolParameter sharcAntiFirefly = new BoolParameter(true);

        /// <summary>
        /// SHARC debug visualization
        /// </summary>
        [Tooltip("Enable SHARC debug visualization to show cached data.")]
        [AdditionalProperty]
        public BoolParameter sharcDebug = new BoolParameter(false);

        /// <summary>
        /// SHARC accumulation frame count
        /// </summary>
        [Tooltip("Maximum number of frames for SHARC temporal accumulation. Higher values produce smoother results but take longer to update.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcAccumulationFrames = new ClampedIntParameter(64, 1, 1024);

        /// <summary>
        /// SHARC stale frame count
        /// </summary>
        [Tooltip("Maximum number of frames without new samples before a cache entry is evicted.")]
        [AdditionalProperty]
        public ClampedIntParameter sharcStaleFrames = new ClampedIntParameter(256, 8, 1024);

        #endregion

        /// <summary>
        /// Check if path tracing is active
        /// </summary>
        public bool IsPathTracingActive()
        {
            return technique.value == GlobalIlluminationTechnique.ReferencedPathTracing;
        }

        /// <summary>
        /// Check if temporal accumulation is enabled (either mode uses accumulation)
        /// </summary>
        public bool UseTemporalAccumulation()
        {
            return denoiseMode.value != PathTracingDenoiseMode.None;
        }

        /// <summary>
        /// Check if NRD REBLUR denoising is enabled
        /// </summary>
        public bool UseNRDDenoising()
        {
            return denoiseMode.value == PathTracingDenoiseMode.NRDReblur;
        }

        /// <summary>
        /// Check if simple temporal accumulation with reprojection is enabled
        /// </summary>
        public bool UseSimpleTemporalAccumulation()
        {
            return denoiseMode.value == PathTracingDenoiseMode.TemporalAccumulation;
        }

        /// <summary>
        /// Check if DLSS Ray Reconstruction denoising is enabled
        /// </summary>
        public bool UseDLSSRRDenoising()
        {
            return denoiseMode.value == PathTracingDenoiseMode.RayReconstruction;
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