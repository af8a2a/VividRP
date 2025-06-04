using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    [VolumeComponentMenu("HBAO Setting")]
    public sealed class HBAOSetting : VolumeComponent, IPostProcessComponent
    {
        public FloatParameter radius = new ClampedFloatParameter(0f, 0, 4);
        public FloatParameter maxRadiusPixels = new ClampedFloatParameter(128f, 16f, 256f);

        public FloatParameter intensity = new ClampedFloatParameter(0f, 0, 4);
        public FloatParameter bias = new ClampedFloatParameter(0f, 0, 1);
        public FloatParameter sharpness = new ClampedFloatParameter(0f, 0, 1);

        public FloatParameter maxDistance = new FloatParameter(150f);
        public FloatParameter distanceFalloff = new FloatParameter(50f);

        public FloatParameter directLightingStrength = new ClampedFloatParameter(0f, 0, 1);

        public BoolParameter enabled = new BoolParameter(false);
        public bool IsActive() => enabled.value;
    }
}