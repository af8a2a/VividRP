using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    public enum AmbientOcclusionMode
    {
        GroundTruthAmbientOcclusion,
        HorizonBasedAmbientOcclusion,
        RaytracingAmbientOcclusion,
    }
    
    [Serializable]
    public sealed class AmbientOcclusionModeParameter : VolumeParameter<AmbientOcclusionMode>
    {
        public AmbientOcclusionModeParameter(AmbientOcclusionMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    public partial class AmbientOcclusion : VolumeComponent
    {
        public AmbientOcclusionModeParameter ambientOcclusionModeParameter =
            new AmbientOcclusionModeParameter(AmbientOcclusionMode.GroundTruthAmbientOcclusion);
    }
}