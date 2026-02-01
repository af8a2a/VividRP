using System;

namespace UnityEngine.Rendering.Universal
{
    #region Surface Cache Enums and Parameters

    [Serializable]
    public enum SurfaceCacheEstimationMethod
    {
        Uniform = 0,
        Restir = 1,
        Ris = 2
    }

    [Serializable]
    public sealed class SurfaceCacheEstimationMethodParameter : VolumeParameter<SurfaceCacheEstimationMethod>
    {
        public SurfaceCacheEstimationMethodParameter(SurfaceCacheEstimationMethod value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public enum SurfaceCacheDebugViewMode
    {
        CellIndex = 0,
        StableIrradiance = 1,
        FastIrradiance = 2,
        CoefficientOfVariation = 3,
        Drift = 4,
        StdDev = 5,
        UpdateCount = 6,
        FlatNormal = 7
    }

    [Serializable]
    public sealed class SurfaceCacheDebugViewModeParameter : VolumeParameter<SurfaceCacheDebugViewMode>
    {
        public SurfaceCacheDebugViewModeParameter(SurfaceCacheDebugViewMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    #endregion

    partial class GlobalIllumination
    {
        #region General Settings

        [Header("Surface Cache - General")]
        [Tooltip("Estimation method for computing patch irradiance.")]
        public SurfaceCacheEstimationMethodParameter surfaceCacheEstimationMethod =
            new SurfaceCacheEstimationMethodParameter(SurfaceCacheEstimationMethod.Uniform);

        [Tooltip("Enable multi-bounce indirect lighting.")]
        public BoolParameter surfaceCacheMultiBounce = new BoolParameter(true);

        #endregion

        #region Grid/Volume Parameters

        [Header("Surface Cache - Grid")]
        [Tooltip("Spatial resolution of the grid (16-64).")]
        public ClampedIntParameter surfaceCacheGridResolution = new ClampedIntParameter(32, 16, 64);

        [Tooltip("Size of the volume in world units (1.0-1000.0).")]
        public ClampedFloatParameter surfaceCacheVolumeSize = new ClampedFloatParameter(128.0f, 1.0f, 1000.0f);

        [Tooltip("Number of cascades for multi-resolution caching (1-8).")]
        public ClampedIntParameter surfaceCacheCascadeCount = new ClampedIntParameter(4, 1, 8);

        [Tooltip("Enable cascade movement to follow camera.")]
        public BoolParameter surfaceCacheCascadeMovement = new BoolParameter(true);

        #endregion

        #region Uniform Estimation Parameters

        [Header("Surface Cache - Uniform Estimation")]
        [Tooltip("Number of samples per patch (1-8).")]
        public ClampedIntParameter surfaceCacheUniformSampleCount = new ClampedIntParameter(2, 1, 8);

        #endregion

        #region Restir Estimation Parameters

        [Header("Surface Cache - Restir Estimation")]
        [Tooltip("Maximum confidence value for Restir reservoir (10-100).")]
        public ClampedIntParameter surfaceCacheRestirConfidenceCap = new ClampedIntParameter(30, 10, 100);

        [Tooltip("Number of spatial samples for Restir resampling (1-8).")]
        public ClampedIntParameter surfaceCacheRestirSpatialSampleCount = new ClampedIntParameter(4, 1, 8);

        [Tooltip("Spatial filter size for Restir in voxels (0.5-5.0).")]
        public ClampedFloatParameter surfaceCacheRestirSpatialFilterSize = new ClampedFloatParameter(2.0f, 0.5f, 5.0f);

        [Tooltip("Frame interval for reservoir validation (1-16).")]
        public ClampedIntParameter surfaceCacheRestirValidationFrameInterval = new ClampedIntParameter(4, 1, 16);

        #endregion

        #region Ris Estimation Parameters

        [Header("Surface Cache - RIS Estimation")]
        [Tooltip("Number of candidate samples for RIS (4-16).")]
        public ClampedIntParameter surfaceCacheRisCandidateCount = new ClampedIntParameter(8, 4, 16);

        [Tooltip("Update weight for RIS target function (0.0-1.0).")]
        public ClampedFloatParameter surfaceCacheRisTargetFunctionUpdateWeight = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);

        #endregion

        #region Patch Filtering Parameters

        [Header("Surface Cache - Patch Filtering")]
        [Tooltip("Temporal smoothing factor (0.0-1.0).")]
        public ClampedFloatParameter surfaceCacheTemporalSmoothing = new ClampedFloatParameter(0.8f, 0.0f, 1.0f);

        [Tooltip("Enable spatial filtering of patches.")]
        public BoolParameter surfaceCacheSpatialFilterEnabled = new BoolParameter(true);

        [Tooltip("Number of samples for spatial filtering (1-8).")]
        public ClampedIntParameter surfaceCacheSpatialFilterSampleCount = new ClampedIntParameter(4, 1, 8);

        [Tooltip("Radius for spatial filtering in voxels (0.5-5.0).")]
        public ClampedFloatParameter surfaceCacheSpatialFilterRadius = new ClampedFloatParameter(1.0f, 0.5f, 5.0f);

        [Tooltip("Enable temporal post-filtering.")]
        public BoolParameter surfaceCacheTemporalPostFilterEnabled = new BoolParameter(true);

        #endregion

        #region Screen Filtering Parameters

        [Header("Surface Cache - Screen Filtering")]
        [Tooltip("Number of samples for screen-space lookup (1-16).")]
        public ClampedIntParameter surfaceCacheLookupSampleCount = new ClampedIntParameter(8, 1, 16);

        [Tooltip("Kernel size for upsampling filter in pixels (1.0-10.0).")]
        public ClampedFloatParameter surfaceCacheUpsamplingKernelSize = new ClampedFloatParameter(5.0f, 1.0f, 10.0f);

        [Tooltip("Number of samples for upsampling (1-8).")]
        public ClampedIntParameter surfaceCacheUpsamplingSampleCount = new ClampedIntParameter(3, 1, 8);

        #endregion

        #region Advanced Parameters

        [Header("Surface Cache - Advanced")]
        [Tooltip("Number of defragmentation passes per frame (0-8).")]
        [AdditionalProperty]
        public ClampedIntParameter surfaceCacheDefragCount = new ClampedIntParameter(2, 0, 8);

        #endregion

        #region Debug Settings

        [Header("Surface Cache - Debug")]
        [Tooltip("Enable debug visualization mode.")]
        [AdditionalProperty]
        public BoolParameter surfaceCacheDebugEnabled = new BoolParameter(false);

        [Tooltip("Debug visualization mode to display.")]
        [AdditionalProperty]
        public SurfaceCacheDebugViewModeParameter surfaceCacheDebugViewMode =
            new SurfaceCacheDebugViewModeParameter(SurfaceCacheDebugViewMode.CellIndex);

        [Tooltip("Show sample positions in debug visualization.")]
        [AdditionalProperty]
        public BoolParameter surfaceCacheDebugShowSamplePosition = new BoolParameter(false);

        #endregion

        #region Helper Methods

        public bool IsSurfaceCacheActive()
        {
            return technique.value == GlobalIlluminationTechnique.SurfaceCache;
        }

        public SurfaceCacheEstimationMethod GetSurfaceCacheEstimationMethod()
        {
            return surfaceCacheEstimationMethod.value;
        }

        public bool UsesSurfaceCacheMultiBounce()
        {
            return surfaceCacheMultiBounce.value;
        }

        #endregion
    }
}
