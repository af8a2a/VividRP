using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public enum DisplayMode
    {
        FfxLpmDisplaymodeLDR = 0,
        FfxLpmDisplaymodeHDR102084 = 1,
        FfxLpmDisplaymodeHDR10Scrgb = 2,
        // FfxLpmDisplaymodeFshdr2084  = 3,
        // FfxLpmDisplaymodeFshdrScrgb = 4
    }

    public enum FfxLpmColorSpace
    {
        FfxLpmColorSpaceRec709 = 0,
        FfxLpmColorSpaceP3 = 1,
        FfxLpmColorSpaceRec2020 = 2,
        FfxLpmColorSapceDisplay = 3,
    };


    public class LumaPreservingMapper : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter shoulder = new BoolParameter(false);

        // public ClampedFloatParameter Intensity = new ClampedFloatParameter(0f, 0f, 1f);
        public FloatParameter HdrMax = new MinFloatParameter(256.0f, 0f);

        public ClampedFloatParameter SoftGap = new ClampedFloatParameter(0f, 0f, 1f);

        // public ClampedFloatParameter Exposure = new ClampedFloatParameter(0.0f, -4.0f, 1.0f);
        public ClampedFloatParameter LPMExposure = new ClampedFloatParameter(8.0f, 3.0f, 11.0f);
        public ClampedFloatParameter Contrast = new ClampedFloatParameter(0.3f, 0.0f, 1.0f);
        public ClampedFloatParameter ShoulderContrast = new ClampedFloatParameter(1.0f, 1.0f, 1.2f);
        public NoInterpVector3Parameter Saturation = new NoInterpVector3Parameter(Vector3.zero);
        public NoInterpVector3Parameter Crosstalk = new NoInterpVector3Parameter(new Vector3(1.0f, 0.5f, 1.0f / 32.0f));
        public VolumeParameter<DisplayMode> displayMode = new VolumeParameter<DisplayMode>();
        public VolumeParameter<FfxLpmColorSpace> colorSpace = new VolumeParameter<FfxLpmColorSpace>();
        public BoolParameter computeShaderMode = new BoolParameter(true);
        public BoolParameter enabled = new BoolParameter(false);
        public bool IsActive() => enabled.value;

        public bool IsTileCompatible() => false;
    }
}