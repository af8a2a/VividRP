using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum BloomMode
    {
        Scattering,
        ConvolutionFFT
    }

    [Serializable]
    public sealed class BloomModeParameter : VolumeParameter<BloomMode>
    {
        public BloomModeParameter(BloomMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    public enum BloomResolution
    {
        Quarter = 4,
        Half = 2
    }

    [Serializable]
    public sealed class BloomResolutionParameter : VolumeParameter<BloomResolution>
    {
        public BloomResolutionParameter(BloomResolution value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    [VolumeComponentMenu("Post-processing/Bloom")]
    public sealed class Bloom : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Scattering uses the fast mip pyramid. Convolution FFT uses an image-space kernel for physically shaped bloom.")]
        public BloomModeParameter mode = new(BloomMode.Scattering);

        [Tooltip("Brightness cutoff applied before the blur (gamma-space).")]
        public MinFloatParameter threshold = new(0f, 0f);

        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new(0f, 0f);

        [Tooltip("Extent of the veiling effect.")]
        public ClampedFloatParameter scatter = new(0.7f, 0f, 1f);

        [Tooltip("Tint of the bloom filter.")]
        public ColorParameter tint = new(Color.white, hdr: false, showAlpha: false, showEyeDropper: true);

        [Tooltip("Dirt texture applied on top of the bloom filter to simulate dirt on the lens.")]
        public Texture2DParameter dirtTexture = new(null);

        [Tooltip("Strength of the lens dirt.")]
        public MinFloatParameter dirtIntensity = new(0f, 0f);

        [Tooltip("Stretches the bloom horizontally (negative) or vertically (positive).")]
        public ClampedFloatParameter anamorphic = new(0f, -1f, 1f);

        [Tooltip("Resolution at which bloom is processed. Quarter is faster, Half looks better.")]
        public BloomResolutionParameter resolution = new(BloomResolution.Half);

        [Tooltip("Use a higher quality 13-tap prefilter (slower, fewer artifacts).")]
        public BoolParameter highQualityPrefiltering = new(false);

        [Tooltip("Use bicubic sampling during upsample (slower, smoother).")]
        public BoolParameter highQualityFiltering = new(true);

        [Tooltip("Experimental: use FidelityFX SPD to build the downsample chain in one compute dispatch.")]
        public BoolParameter experimentalSpdDownsample = new(false);

        [Tooltip("Kernel texture used by Convolution FFT. The brightest point should be near Convolution Center.")]
        public Texture2DParameter convolutionKernel = new(null);

        [Tooltip("Kernel diameter relative to the bloom image's major axis.")]
        public ClampedFloatParameter convolutionSize = new(0.15f, 0.01f, 1f);

        [Tooltip("Limits the zero-padding reserved for the kernel. Zero uses the full convolution size.")]
        public ClampedFloatParameter convolutionBufferScale = new(0.25f, 0f, 1f);

        [Tooltip("Normalized center of the convolution kernel texture.")]
        public Vector2Parameter convolutionCenter = new(new Vector2(0.5f, 0.5f));

        [Tooltip("Clamps the bright kernel center before convolution, preserving the scattering lobe without duplicating the source highlight.")]
        public ClampedFloatParameter convolutionKernelClamp = new(0.1f, 0.001f, 1f);

        [Tooltip("Axis resolution used by FFT convolution. Lower values reduce the power-of-two FFT domain and memory cost.")]
        public ClampedFloatParameter convolutionResolutionScale = new(0.25f, 0.1f, 0.5f);

        public bool IsActive()
        {
            return intensity.value > 0f;
        }
    }
}
