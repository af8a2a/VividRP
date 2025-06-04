using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class BackgroundLightScatter : VolumeComponent, IPostProcessComponent
    {
        public MinFloatParameter threshold = new MinFloatParameter(0.7f, 0f);
        public ClampedFloatParameter lumRangeScale = new ClampedFloatParameter(0.2f, 0f, 1f);
        public ClampedFloatParameter preFilterScale = new ClampedFloatParameter(2.5f, 0f, 5.0f);
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter blurScale = new ClampedFloatParameter(1f, 0f, 2.0f);
        public ColorParameter tint = new ColorParameter(new Color(1f, 1f, 1f, 0f), false, true, true);
        public Vector4Parameter blurCompositeWeight = new Vector4Parameter(new Vector4(0.3f, 0.3f, 0.26f, 0.15f));
        public BoolParameter enabled = new BoolParameter(false);

        public bool IsActive() => enabled.value;
        public bool IsTileCompatible() => false;
    }
}