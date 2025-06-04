using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class MobileGroundTruthAmbientOcclusion : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter Downsample = new BoolParameter(false);
        public BoolParameter AfterOpaque = new BoolParameter(false);
        public VolumeParameter<DepthSource> Source = new VolumeParameter<DepthSource>();
        public VolumeParameter<NormalQuality> NormalSamples = new VolumeParameter<NormalQuality>();
        public FloatParameter Intensity = new ClampedFloatParameter(1, 0f,4f);
        public FloatParameter DirectLightingStrength = new ClampedFloatParameter(0.25f, 0, 1);
        public FloatParameter Radius = new ClampedFloatParameter(1, 0f,4f);
        public IntParameter SampleCount = new ClampedIntParameter(4, 4, 20);
        public BoolParameter enabled = new BoolParameter(false);

        public bool IsActive()
        {
            return enabled.value;
        }
    }

    // Enums
    public enum DepthSource
    {
        Depth = 0,
        DepthNormals = 1
    }

    public enum NormalQuality
    {
        Low,
        Medium,
        High
    }
}