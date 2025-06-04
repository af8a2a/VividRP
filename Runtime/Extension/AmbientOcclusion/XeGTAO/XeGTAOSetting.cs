using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public enum XeGTAODenoisingLevel
    {
        Disabled = 0,
        Sharp = 1,
        Medium = 2,
        Soft = 3,
    }

    public enum XeGTAOQualityLevel
    {
        Low,
        Medium,
        High,
        Ultra,
    }

    public enum XeGTAOResolution
    {
        Full = 1,
        Half = 2,
        Quarter = 4,
    }


    [Serializable]
    public sealed class XeGTAOResolutionParameter : VolumeParameter<XeGTAOResolution>
    {
        public XeGTAOResolutionParameter(XeGTAOResolution value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class XeGTAOQualityLevelParameter : VolumeParameter<XeGTAOQualityLevel>
    {
        public XeGTAOQualityLevelParameter(XeGTAOQualityLevel value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class XeGTAODenoisingLevelParameter : VolumeParameter<XeGTAODenoisingLevel>
    {
        public XeGTAODenoisingLevelParameter(XeGTAODenoisingLevel value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    [VolumeComponentMenu("Ground Truth Ambient Occlusion")]
    public class XeGTAOSetting : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter Enabled = new(false);
        public ClampedFloatParameter FinalValuePower = new(1.0f, 0.0f, 5.0f);
        public ClampedFloatParameter FalloffRange = new(0.1f, 0.0f, 10.0f);

        public XeGTAOResolutionParameter Resolution = new XeGTAOResolutionParameter(XeGTAOResolution.Full);
        public XeGTAOQualityLevelParameter QualityLevel = new XeGTAOQualityLevelParameter(XeGTAOQualityLevel.High);
        public XeGTAODenoisingLevelParameter DenoisingLevel = new XeGTAODenoisingLevelParameter(XeGTAODenoisingLevel.Sharp);
        public BoolParameter BentNormals = new BoolParameter(false);
        public BoolParameter DirectLightingMicroshadows = new BoolParameter(false);
        public FloatParameter directLightingStrength = new ClampedFloatParameter(0f, 0, 1);


        
        public XeGTAOSetting() => displayName = "Ground Truth Ambient Occlusion";


        public bool IsActive()
        {
            return Enabled.value;
        }
    }
}