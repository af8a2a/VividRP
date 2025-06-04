using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    [VolumeComponentMenu("Diffusion")]
    [VolumeRequiresRendererFeatures(typeof(DiffusionFeature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class Diffusion : VolumeComponent, IPostProcessComponent
    {
        public Diffusion()
        {
            displayName = "Diffusion";
        }

        public DiffusionModeParameter mode = new DiffusionModeParameter(DiffusionMode.Filter);
        public ClampedFloatParameter multiply = new ClampedFloatParameter(0.5f, 0, 1);
        public ClampedFloatParameter blurScale = new ClampedFloatParameter(0.5f, 0, 2);
        public ClampedFloatParameter filter = new ClampedFloatParameter(0.5f, 0, 1);
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 0, 1);
        public ClampedFloatParameter blurIntensity = new ClampedFloatParameter(1f, 0f, 1f);

        public BoolParameter enabled = new BoolParameter(false);

        public bool IsActive()
        {
            return enabled.value;
        }
    }

    public enum DiffusionMode
    {
        Max,
        Filter,
    }

    [Serializable]
    public sealed class DiffusionModeParameter : VolumeParameter<DiffusionMode>
    {
        public DiffusionModeParameter(DiffusionMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}