using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    public sealed class ExposureDebugModeParameter : VolumeParameter<RenderPass.Core.ExposureDebugMode>
    {
        public ExposureDebugModeParameter(
            RenderPass.Core.ExposureDebugMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Debug/Exposure Debug")]
    public sealed class ExposureDebugVolume : VolumeComponent
    {
        public ExposureDebugModeParameter mode = new(RenderPass.Core.ExposureDebugMode.None);
        public ClampedFloatParameter debugExposure = new(0f, -16f, 16f);
        public BoolParameter centerHistogramAroundMiddleGrey = new(false);
        public BoolParameter showTonemapCurveAlongHistogramView = new(true);
        public BoolParameter displayMaskOnly = new(false);
        public BoolParameter displayOnSceneOverlay = new(true);

        public bool IsActive()
        {
            return active
                && ((mode != null && mode.overrideState && mode.value != RenderPass.Core.ExposureDebugMode.None)
                    || (debugExposure != null && debugExposure.overrideState)
                    || (centerHistogramAroundMiddleGrey != null && centerHistogramAroundMiddleGrey.overrideState)
                    || (showTonemapCurveAlongHistogramView != null && showTonemapCurveAlongHistogramView.overrideState)
                    || (displayMaskOnly != null && displayMaskOnly.overrideState)
                    || (displayOnSceneOverlay != null && displayOnSceneOverlay.overrideState));
        }
    }
}
