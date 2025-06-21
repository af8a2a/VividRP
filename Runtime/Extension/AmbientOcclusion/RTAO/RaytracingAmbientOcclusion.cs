namespace UnityEngine.Rendering.Universal
{
    [VolumeRequiresRendererFeatures(typeof(RaytracingCoreFeature), typeof(RaytracingAmbientOcclusionFeature))]
    public class RaytracingAmbientOcclusion : VolumeComponent
    {
        public FloatParameter radius = new ClampedFloatParameter(0f, 0, 1f);
        public IntParameter samplesPerPixel = new ClampedIntParameter(1, 1, 16);

        public FloatParameter intensity = new ClampedFloatParameter(0f, 0, 1);

        public FloatParameter directLightingStrength = new ClampedFloatParameter(0f, 0, 1);

        public BoolParameter enabled = new BoolParameter(false);
        public bool IsActive() => enabled.value;
    }
}