using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [GenerateHLSL]
    public enum FilmGrainLookup
    {
        Thin1 = 0,
        Thin2 = 1,
        Medium1 = 2,
        Medium2 = 3,
        Medium3 = 4,
        Medium4 = 5,
        Medium5 = 6,
        Medium6 = 7,
        Large01 = 8,
        Large02 = 9,
        Custom = 10
    }

    [Serializable]
    public sealed class FilmGrainLookupParameter : VolumeParameter<FilmGrainLookup>
    {
        public FilmGrainLookupParameter(FilmGrainLookup value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Film Grain")]
    public sealed class FilmGrain : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Specifies the preset texture used for film grain.")]
        public FilmGrainLookupParameter type = new(FilmGrainLookup.Thin1);

        [Tooltip("Controls the strength of the film grain effect.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Tooltip("Controls how strongly the grain responds to luminance.")]
        public ClampedFloatParameter response = new(0.8f, 0f, 1f);

        [Tooltip("A custom texture to use when Type is set to Custom.")]
        public Texture2DParameter texture = new(null);

        protected override void OnEnable()
        {
            if (texture == null)
                texture = new Texture2DParameter(null);

            base.OnEnable();
        }

        public bool IsActive()
        {
            return intensity.value > 0f && HasValidTexture();
        }

        public bool IsCustomTextureMode()
        {
            return type.value == FilmGrainLookup.Custom;
        }

        public bool HasValidTexture()
        {
            return !IsCustomTextureMode() || texture.value != null;
        }
    }
}
