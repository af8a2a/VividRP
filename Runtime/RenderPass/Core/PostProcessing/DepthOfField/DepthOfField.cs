using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace VividRP.Runtime
{
    public enum DepthOfFieldMode
    {
        Off,

        [InspectorName("Physical Camera")]
        UsePhysicalCamera,

        [InspectorName("Manual Ranges")]
        Manual
    }

    public enum DepthOfFieldResolution
    {
        Quarter = 4,
        Half = 2,
        Full = 1
    }

    public enum FocusDistanceMode
    {
        Volume,
        Camera
    }

    [Serializable]
    public sealed class DepthOfFieldModeParameter : VolumeParameter<DepthOfFieldMode>
    {
        public DepthOfFieldModeParameter(DepthOfFieldMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class DepthOfFieldResolutionParameter : VolumeParameter<DepthOfFieldResolution>
    {
        public DepthOfFieldResolutionParameter(DepthOfFieldResolution value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class FocusDistanceModeParameter : VolumeParameter<FocusDistanceMode>
    {
        public FocusDistanceModeParameter(FocusDistanceMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Depth Of Field")]
    public sealed class DepthOfField : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Specifies the mode that VividRP uses to set the focus for the depth of field effect.")]
        public DepthOfFieldModeParameter focusMode = new(DepthOfFieldMode.Off);

        [Tooltip("The distance to the focus plane from the camera.")]
        public MinFloatParameter focusDistance = new(10f, 0.1f);

        [Tooltip("Specifies where to read the focus distance from.")]
        public FocusDistanceModeParameter focusDistanceMode = new(FocusDistanceMode.Volume);

        [Header("Near Range")]
        [Tooltip("Sets the distance from the camera at which the near field blur begins to decrease in intensity.")]
        public MinFloatParameter nearFocusStart = new(0f, 0f);

        [Tooltip("Sets the distance from the camera at which the near field does not blur anymore.")]
        public MinFloatParameter nearFocusEnd = new(4f, 0f);

        [Header("Far Range")]
        [Tooltip("Sets the distance from the camera at which the far field starts blurring.")]
        public MinFloatParameter farFocusStart = new(10f, 0f);

        [Tooltip("Sets the distance from the camera at which the far field blur reaches its maximum blur radius.")]
        public MinFloatParameter farFocusEnd = new(20f, 0f);

        public int nearSampleCount
        {
            get => m_NearSampleCount.value;
            set => m_NearSampleCount.value = value;
        }

        public float nearMaxBlur
        {
            get => m_NearMaxBlur.value;
            set => m_NearMaxBlur.value = value;
        }

        public int farSampleCount
        {
            get => m_FarSampleCount.value;
            set => m_FarSampleCount.value = value;
        }

        public float farMaxBlur
        {
            get => m_FarMaxBlur.value;
            set => m_FarMaxBlur.value = value;
        }

        public bool highQualityFiltering
        {
            get => m_HighQualityFiltering.value;
            set => m_HighQualityFiltering.value = value;
        }

        public bool physicallyBased
        {
            get => m_PhysicallyBased.value;
            set => m_PhysicallyBased.value = value;
        }

        public float adaptiveSamplingWeight
        {
            get => m_AdaptiveSamplingWeight.value;
            set => m_AdaptiveSamplingWeight.value = value;
        }

        public bool limitManualRangeNearBlur
        {
            get => m_LimitManualRangeNearBlur.value;
            set => m_LimitManualRangeNearBlur.value = value;
        }

        public DepthOfFieldResolution resolution
        {
            get => m_Resolution.value;
            set => m_Resolution.value = value;
        }

        [Header("Near Blur")]
        [Tooltip("Sets the number of samples to use for the near field.")]
        [SerializeField, FormerlySerializedAs("nearSampleCount")]
        private ClampedIntParameter m_NearSampleCount = new(5, 3, 8);

        [Tooltip("Sets the maximum radius the near blur can reach.")]
        [SerializeField, FormerlySerializedAs("nearMaxBlur")]
        private ClampedFloatParameter m_NearMaxBlur = new(4f, 0f, 8f);

        [Header("Far Blur")]
        [Tooltip("Sets the number of samples to use for the far field.")]
        [SerializeField, FormerlySerializedAs("farSampleCount")]
        private ClampedIntParameter m_FarSampleCount = new(7, 3, 16);

        [Tooltip("Sets the maximum radius the far blur can reach.")]
        [SerializeField, FormerlySerializedAs("farMaxBlur")]
        private ClampedFloatParameter m_FarMaxBlur = new(8f, 0f, 16f);

        [Header("Advanced Tweaks")]
        [Tooltip("Specifies the resolution at which VividRP processes the depth of field effect.")]
        [SerializeField, FormerlySerializedAs("resolution")]
        private DepthOfFieldResolutionParameter m_Resolution = new(DepthOfFieldResolution.Half);

        [Tooltip("When enabled, VividRP uses bicubic instead of bilinear filtering for the physically based depth of field gather.")]
        [SerializeField, FormerlySerializedAs("highQualityFiltering")]
        private BoolParameter m_HighQualityFiltering = new(true);

        [Tooltip("When enabled, VividRP uses the HDRP-style physically based algorithm to compute depth of field.")]
        [SerializeField]
        private BoolParameter m_PhysicallyBased = new(false);

        [Tooltip("Adjusts the number of samples in the physically based depth of field depending on blur radius.")]
        [SerializeField]
        private ClampedFloatParameter m_AdaptiveSamplingWeight = new(0.75f, 0.5f, 4f);

        [Tooltip("Adjust near blur CoC based on depth distance when manual, non-physical mode is used.")]
        [SerializeField]
        private BoolParameter m_LimitManualRangeNearBlur = new(false);

        [Tooltip("Enables CoC stabilization when temporal antialiasing is active. The physical blur path is migrated first; stabilization wiring follows separately.")]
        [InspectorName("CoC Stabilization")]
        public BoolParameter coCStabilization = new(true);

        public bool IsActive()
        {
            return focusMode.value != DepthOfFieldMode.Off && (IsNearLayerActive() || IsFarLayerActive());
        }

        public bool IsNearLayerActive()
        {
            return nearMaxBlur > 0f && nearFocusEnd.value > 0f;
        }

        public bool IsFarLayerActive()
        {
            return farMaxBlur > 0f;
        }
    }
}
