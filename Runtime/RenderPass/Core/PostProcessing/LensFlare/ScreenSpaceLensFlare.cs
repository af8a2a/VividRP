using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum ScreenSpaceLensFlareResolution
    {
        Half = 2,
        Quarter = 4,
        Eighth = 8
    }

    [Serializable]
    public sealed class ScreenSpaceLensFlareResolutionParameter : VolumeParameter<ScreenSpaceLensFlareResolution>
    {
        public ScreenSpaceLensFlareResolutionParameter(
            ScreenSpaceLensFlareResolution value,
            bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Screen Space Lens Flare")]
    public sealed class ScreenSpaceLensFlare : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Sets the global intensity of the Screen Space Lens Flare effect. When set to 0, the pass is skipped.")]
        public MinFloatParameter intensity = new(0f, 0f);

        [Tooltip("Sets the color used to tint all flares.")]
        public ColorParameter tintColor = new(Color.white);

        [Tooltip("Controls the Bloom mip used as a source for the Lens Flare effect.")]
        public ClampedIntParameter bloomMip = new(1, 0, 5);

        [Header("Flares")]
        [Tooltip("Controls the intensity of the regular flare sample.")]
        public MinFloatParameter firstFlareIntensity = new(1f, 0f);

        [Tooltip("Controls the intensity of the reversed flare sample.")]
        public MinFloatParameter secondaryFlareIntensity = new(1f, 0f);

        [Tooltip("Controls the intensity of the warped flare sample.")]
        public MinFloatParameter warpedFlareIntensity = new(1f, 0f);

        [Tooltip("Sets the scale of the warped flare sample.")]
        public Vector2Parameter warpedFlareScale = new(new Vector2(1f, 1f));

        [Tooltip("Controls how many times the flare effect is repeated for each flare type.")]
        public ClampedIntParameter samples = new(1, 1, 3);

        [Tooltip("Controls the multiplier applied to each additional sample.")]
        public ClampedFloatParameter sampleDimmer = new(0.5f, 0.1f, 1f);

        [Tooltip("Controls the vignette used to occlude flares near the center of the screen.")]
        public ClampedFloatParameter vignetteEffect = new(1f, 0f, 1f);

        [Tooltip("Controls the starting position of the flares in screen space relative to their source.")]
        public ClampedFloatParameter startingPosition = new(1.25f, 1f, 3f);

        [Tooltip("Controls the scale at which the flares are sampled.")]
        public ClampedFloatParameter scale = new(1.5f, 1f, 4f);

        [Header("Streaks")]
        [Tooltip("Controls the intensity of the streaks effect.")]
        public MinFloatParameter streaksIntensity = new(1f, 0f);

        [Tooltip("Controls the length of the streaks effect.")]
        public ClampedFloatParameter streaksLength = new(0.5f, 0f, 1f);

        [Tooltip("Controls the orientation of the streaks effect in degrees.")]
        public FloatParameter streaksOrientation = new(0f);

        [Tooltip("Controls the threshold of the streaks effect.")]
        public ClampedFloatParameter streaksThreshold = new(0.25f, 0f, 1f);

        [Tooltip("Specifies the resolution at which the streak effect is evaluated.")]
        public ScreenSpaceLensFlareResolutionParameter resolution = new(ScreenSpaceLensFlareResolution.Quarter);

        [Header("Chromatic Abberation")]
        [Tooltip("Specifies a texture used to shift the hue of chromatic aberrations. If null, VividRP creates a default texture.")]
        public Texture2DParameter spectralLut = new(null);

        [Tooltip("Controls the strength of the chromatic aberration effect.")]
        public ClampedFloatParameter chromaticAbberationIntensity = new(0.5f, 0f, 1f);

        [Tooltip("Controls the number of samples used to render the chromatic aberration effect.")]
        public ClampedIntParameter chromaticAbberationSampleCount = new(3, 3, 8);

        public bool IsActive()
        {
            return intensity.value > 0f;
        }

        public bool IsStreaksActive()
        {
            return streaksIntensity.value > 0f;
        }
    }

    internal struct ScreenSpaceLensFlareSettingsData
    {
        public bool enabled;
        public float intensity;
        public Color tintColor;
        public int bloomMip;
        public float firstFlareIntensity;
        public float secondaryFlareIntensity;
        public float warpedFlareIntensity;
        public Vector2 warpedFlareScale;
        public int samples;
        public float sampleDimmer;
        public float vignetteEffect;
        public float startingPosition;
        public float scale;
        public float streaksIntensity;
        public float streaksLength;
        public float streaksOrientation;
        public float streaksThreshold;
        public ScreenSpaceLensFlareResolution resolution;
        public Texture spectralLut;
        public float chromaticAbberationIntensity;
        public int chromaticAbberationSampleCount;

        public bool streaksEnabled => enabled && streaksIntensity > 0f;

        public static ScreenSpaceLensFlareSettingsData CreateDefault()
        {
            return new ScreenSpaceLensFlareSettingsData
            {
                enabled = false,
                intensity = 0f,
                tintColor = Color.white,
                bloomMip = 1,
                firstFlareIntensity = 1f,
                secondaryFlareIntensity = 1f,
                warpedFlareIntensity = 1f,
                warpedFlareScale = Vector2.one,
                samples = 1,
                sampleDimmer = 0.5f,
                vignetteEffect = 1f,
                startingPosition = 1.25f,
                scale = 1.5f,
                streaksIntensity = 1f,
                streaksLength = 0.5f,
                streaksOrientation = 0f,
                streaksThreshold = 0.25f,
                resolution = ScreenSpaceLensFlareResolution.Quarter,
                spectralLut = null,
                chromaticAbberationIntensity = 0.5f,
                chromaticAbberationSampleCount = 3,
            };
        }
    }

    internal static class ScreenSpaceLensFlareSettingsResolver
    {
        internal static ScreenSpaceLensFlareSettingsData Resolve()
        {
            var settings = ScreenSpaceLensFlareSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var component = stack.GetComponent<ScreenSpaceLensFlare>();
            if (component == null || !component.IsActive())
                return settings;

            settings.enabled = true;
            settings.intensity = component.intensity.value;
            settings.tintColor = component.tintColor.value;
            settings.bloomMip = component.bloomMip.value;
            settings.firstFlareIntensity = component.firstFlareIntensity.value;
            settings.secondaryFlareIntensity = component.secondaryFlareIntensity.value;
            settings.warpedFlareIntensity = component.warpedFlareIntensity.value;
            settings.warpedFlareScale = component.warpedFlareScale.value;
            settings.samples = component.samples.value;
            settings.sampleDimmer = component.sampleDimmer.value;
            settings.vignetteEffect = component.vignetteEffect.value;
            settings.startingPosition = component.startingPosition.value;
            settings.scale = component.scale.value;
            settings.streaksIntensity = component.streaksIntensity.value;
            settings.streaksLength = component.streaksLength.value;
            settings.streaksOrientation = component.streaksOrientation.value;
            settings.streaksThreshold = component.streaksThreshold.value;
            settings.resolution = component.resolution.value;
            settings.spectralLut = component.spectralLut.value;
            settings.chromaticAbberationIntensity = component.chromaticAbberationIntensity.value;
            settings.chromaticAbberationSampleCount = component.chromaticAbberationSampleCount.value;
            return settings;
        }
    }
}
