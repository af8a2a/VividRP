using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    public sealed class OverlayDebugVisualizationModeParameter : VolumeParameter<RenderPass.Core.OverlayDebugVisualizationMode>
    {
        public OverlayDebugVisualizationModeParameter(
            RenderPass.Core.OverlayDebugVisualizationMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class OverlayDebugDepthModeParameter : VolumeParameter<RenderPass.Core.OverlayDebugDepthMode>
    {
        public OverlayDebugDepthModeParameter(
            RenderPass.Core.OverlayDebugDepthMode value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Debug/Overlay Debug")]
    public sealed class OverlayDebugVolume : VolumeComponent
    {
        public ClampedFloatParameter overlayAmount = new(0f, 0f, 1f);
        public MinIntParameter arraySlice = new(0, 0);
        public ClampedFloatParameter exposure = new(0f, -16f, 16f);
        public ClampedFloatParameter opacity = new(1f, 0f, 1f);
        public OverlayDebugVisualizationModeParameter visualizationMode =
            new(RenderPass.Core.OverlayDebugVisualizationMode.Auto);
        public OverlayDebugDepthModeParameter depthMode =
            new(RenderPass.Core.OverlayDebugDepthMode.Raw);

        public bool IsActive()
        {
            return active
                && ((overlayAmount != null && overlayAmount.overrideState)
                    || (arraySlice != null && arraySlice.overrideState)
                    || (exposure != null && exposure.overrideState)
                    || (opacity != null && opacity.overrideState)
                    || (visualizationMode != null && visualizationMode.overrideState)
                    || (depthMode != null && depthMode.overrideState));
        }
    }
}
