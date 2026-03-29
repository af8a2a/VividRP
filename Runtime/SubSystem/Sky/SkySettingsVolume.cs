using System;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable]
    [VolumeComponentMenu("VividRP/Sky Settings")]
    public sealed class SkySettingsVolume : VolumeComponent
    {
        public EnumParameter<SkyType> skyType = new(SkyType.HDRI);
        public EnumParameter<SkyUpdateMode> updateMode = new(SkyUpdateMode.OnChanged);
        public MinFloatParameter updatePeriod = new(0.0f, 0.0f);
    }
}
