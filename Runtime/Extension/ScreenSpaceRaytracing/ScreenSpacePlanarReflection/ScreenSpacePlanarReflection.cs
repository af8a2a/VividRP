using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public sealed class ScreenSpacePlanarReflection : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter shouldRenderSSPR = new BoolParameter(false);
        public BoolParameter fillHole = new BoolParameter(true);
        public ColorParameter tintColor = new ColorParameter(Color.white, true, true, true);
        public ClampedFloatParameter screenLRStretchIntensity = new ClampedFloatParameter(0.0f, 0.0f, 8.0f);
        public ClampedFloatParameter screenLRStretchThreshold = new ClampedFloatParameter(0.8f, -1.0f, 1.0f);

        public bool IsActive() => shouldRenderSSPR.value;
        
    }
}