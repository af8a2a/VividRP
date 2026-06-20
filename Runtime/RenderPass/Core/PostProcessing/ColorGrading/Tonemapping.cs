using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// Available tonemapping modes.
    /// </summary>
    /// <seealso cref="Tonemapping.mode"/>
    [GenerateHLSL]
    public enum TonemappingMode
    {
        /// <summary>
        /// No tonemapping.
        /// </summary>
        None = 0,

        /// <summary>
        /// Tonemapping mode with minimal impact on color hue and saturation.
        /// </summary>
        Neutral = 1,

        /// <summary>
        /// ACES tonemapper for a more filmic look.
        /// </summary>
        ACES = 2,

        /// <summary>
        /// Gran Turismo inspired tonemapping curve.
        /// </summary>
        GranTurismo = 5,

        /// <summary>
        /// AgX tonemapper.
        /// </summary>
        [InspectorName("AgX")]
        AgX = 6,

        /// <summary>
        /// Khronos PBR neutral tonemapper.
        /// </summary>
        [InspectorName("Khronos PBR Neutral")]
        KhronosPBR = 7,

        /// <summary>
        /// AMD FidelityFX Luma Preserving Mapper tonemapper.
        /// </summary>
        [InspectorName("LPM")]
        LPM = 8,

        /// <summary>
        /// A tweakable, artist-friendly tonemapping curve.
        /// </summary>
        Custom = 3,

        /// <summary>
        /// Specifies a custom lookup table.
        /// </summary>
        /// <seealso cref="Tonemapping.lutTexture"/>
        /// <seealso cref="Tonemapping.lutContribution"/>
        External = 4
    }


    /// <summary>
    /// Available options for when HDR Output is enabled and Tonemap is set to Neutral.
    /// </summary>
    public enum NeutralRangeReductionMode
    {
        /// <summary>
        /// Simple Reinhard tonemapping curve.
        /// </summary>
        Reinhard = 1,

        /// <summary>
        /// Range reduction curve as specified in the BT.2390 standard.
        /// </summary>
        BT2390 = 2
    }

    /// <summary>
    /// Preset used when selecting ACES tonemapping for HDR displays.
    /// </summary>
    public enum HDRACESPreset
    {
        /// <summary>
        /// Preset for display with maximum 1000 nits display.
        /// </summary>
        ACES1000Nits = HDRRangeReduction.ACES1000Nits,

        /// <summary>
        /// Preset for display with maximum 2000 nits display.
        /// </summary>
        ACES2000Nits = HDRRangeReduction.ACES2000Nits,

        /// <summary>
        /// Preset for display with maximum 4000 nits display.
        /// </summary>
        ACES4000Nits = HDRRangeReduction.ACES4000Nits,
    }

    /// <summary>
    /// Output color gamut used by FidelityFX LPM when gamut mapping is enabled.
    /// </summary>
    public enum LpmColorGamut
    {
        /// <summary>
        /// Rec.709 / sRGB display primaries.
        /// </summary>
        [InspectorName("Rec.709")]
        Rec709 = 0,

        /// <summary>
        /// Display P3 primaries with D65 white point.
        /// </summary>
        [InspectorName("Display P3")]
        DisplayP3 = 1,

        /// <summary>
        /// Rec.2020 display primaries.
        /// </summary>
        [InspectorName("Rec.2020")]
        Rec2020 = 2
    }

    /// <summary>
    /// Tonemap mode to be used when outputting to HDR device and when the main mode is not supported on HDR.
    /// </summary>
    public enum FallbackHDRTonemap
    {
        /// <summary>
        /// No tonemapping.
        /// </summary>
        None = 0,

        /// <summary>
        /// Tonemapping mode with minimal impact on color hue and saturation.
        /// </summary>
        Neutral,

        /// <summary>
        /// ACES tonemapper for a more filmic look.
        /// </summary>
        ACES
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="NeutralRangeReductionMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class NeutralRangeReductionModeParameter : VolumeParameter<NeutralRangeReductionMode>
    {
        /// <summary>
        /// Creates a new <see cref="NeutralRangeReductionModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public NeutralRangeReductionModeParameter(NeutralRangeReductionMode value, bool overrideState = false) : base(
            value, overrideState)
        {
        }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="HDRACESPreset"/> value.
    /// </summary>
    [Serializable]
    public sealed class HDRACESPresetParameter : VolumeParameter<HDRACESPreset>
    {
        /// <summary>
        /// Creates a new <see cref="HDRACESPresetParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public HDRACESPresetParameter(HDRACESPreset value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="LpmColorGamut"/> value.
    /// </summary>
    [Serializable]
    public sealed class LpmColorGamutParameter : VolumeParameter<LpmColorGamut>
    {
        /// <summary>
        /// Creates a new <see cref="LpmColorGamutParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public LpmColorGamutParameter(LpmColorGamut value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="FallbackHDRTonemap"/> value.
    /// </summary>
    [Serializable]
    public sealed class FallbackHDRTonemapParameter : VolumeParameter<FallbackHDRTonemap>
    {
        /// <summary>
        /// Creates a new <see cref="FallbackHDRTonemapParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public FallbackHDRTonemapParameter(FallbackHDRTonemap value, bool overrideState = false) : base(value,
            overrideState)
        {
        }
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="TonemappingMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode>
    {
        /// <summary>
        /// Creates a new <see cref="TonemappingModeParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }

    /// <summary>
    /// A volume component that holds settings for the Tonemapping effect.
    /// </summary>
    [Serializable, VolumeComponentMenu("Post-processing/Tonemapping")]
    public class Tonemapping : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Specifies the tonemapping algorithm to use for the color grading process.
        /// </summary>
        /// <seealso cref="TonemappingMode"/>
        [Tooltip("Specifies the tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        /// <summary>
        /// Whether to use full ACES tonemap instead of an approximation.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Whether to use full ACES tonemap instead of an approximation. When outputting to an HDR display, full ACES is always used regardless of this checkbox.")]
        public BoolParameter useFullACES = new BoolParameter(false);

        /// <summary>
        /// The maximum brightness the Gran Turismo tonemapper maps towards.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("The maximum brightness of the Gran Turismo tonemapper.")]
        public ClampedFloatParameter maxBrightness = new ClampedFloatParameter(1.0f, 1.0f, 20.0f);

        /// <summary>
        /// Controls the overall contrast of the Gran Turismo tonemapper.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("Controls the overall contrast of the Gran Turismo tonemapper.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.11f, 0.0f, 5.0f);

        /// <summary>
        /// Controls where the linear section begins.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("Controls where the linear section begins.")]
        public ClampedFloatParameter linearSectionStart = new ClampedFloatParameter(0.2f, 0.01f, 1.0f);

        /// <summary>
        /// Controls the length of the linear section.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("Controls the length of the linear section.")]
        public ClampedFloatParameter linearSectionLength = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        /// <summary>
        /// Controls the dark-region curve exponent.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("Controls the dark-region curve exponent.")]
        public ClampedFloatParameter blackPow = new ClampedFloatParameter(1.29f, 1.0f, 3.0f);

        /// <summary>
        /// Controls the dark-region lift.
        /// This parameter is only used when <see cref="TonemappingMode.GranTurismo"/> is set.
        /// </summary>
        [Tooltip("Controls the dark-region lift.")]
        public ClampedFloatParameter blackMin = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Enables the FidelityFX LPM shoulder curve.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Enables the FidelityFX LPM shoulder curve.")]
        public BoolParameter lpmShoulder = new BoolParameter(false);

        /// <summary>
        /// Maximum input HDR value used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Maximum input HDR value used by FidelityFX LPM.")]
        public MinFloatParameter lpmHdrMax = new MinFloatParameter(16.0f, 0.001f);

        /// <summary>
        /// Output color gamut used by FidelityFX LPM. HDR output uses linear Rec.2020 before the final HDR mapping step.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Output color gamut used by FidelityFX LPM. HDR output uses linear Rec.2020 before the final HDR mapping step.")]
        public LpmColorGamutParameter lpmColorGamut = new LpmColorGamutParameter(LpmColorGamut.Rec709);

        /// <summary>
        /// Soft gamut mapping feather amount used when FidelityFX LPM converts between gamuts.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Soft gamut mapping feather amount used when FidelityFX LPM converts between gamuts.")]
        public ClampedFloatParameter lpmSoftGap = new ClampedFloatParameter(1.0f / 1024.0f, 1.0f / 1024.0f, 1.0f);

        /// <summary>
        /// Exposure offset in stops used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Exposure offset in stops used by FidelityFX LPM.")]
        public ClampedFloatParameter lpmExposure = new ClampedFloatParameter(0.0f, -16.0f, 16.0f);

        /// <summary>
        /// Contrast control used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Contrast control used by FidelityFX LPM.")]
        public ClampedFloatParameter lpmContrast = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Shoulder contrast control used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Shoulder contrast control used by FidelityFX LPM.")]
        public ClampedFloatParameter lpmShoulderContrast = new ClampedFloatParameter(1.0f, 0.1f, 4.0f);

        /// <summary>
        /// Per-channel saturation controls used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Per-channel saturation controls used by FidelityFX LPM.")]
        public Vector3Parameter lpmSaturation = new Vector3Parameter(Vector3.zero);

        /// <summary>
        /// Per-channel crosstalk controls used by FidelityFX LPM.
        /// This parameter is only used when <see cref="TonemappingMode.LPM"/> is set.
        /// </summary>
        [Tooltip("Per-channel crosstalk controls used by FidelityFX LPM.")]
        public Vector3Parameter lpmCrosstalk = new Vector3Parameter(Vector3.one);

        /// <summary>
        /// Controls the transition between the toe and the mid section of the curve. A value of 0
        /// results in no transition and a value of 1 results in a very hard transition.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Controls the transition between the toe and the mid section of the curve. A value of 0 results in no transition and a value of 1 results in a very hard transition.")]
        public ClampedFloatParameter toeStrength = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>
        /// Controls how much of the dynamic range is in the toe. Higher values result in longer
        /// toes and therefore contain more of the dynamic range.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Controls how much of the dynamic range is in the toe. Higher values result in longer toes and therefore contain more of the dynamic range.")]
        public ClampedFloatParameter toeLength = new ClampedFloatParameter(0.5f, 0f, 1f);

        /// <summary>
        /// Controls the transition between the midsection and the shoulder of the curve. A value of 0
        /// results in no transition and a value of 1 results in a very hard transition.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Controls the transition between the midsection and the shoulder of the curve. A value of 0 results in no transition and a value of 1 results in a very hard transition.")]
        public ClampedFloatParameter shoulderStrength = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>
        /// Sets how many F-stops (EV) to add to the dynamic range of the curve.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Sets how many F-stops (EV) to add to the dynamic range of the curve.")]
        public MinFloatParameter shoulderLength = new MinFloatParameter(0.5f, 0f);

        /// <summary>
        /// Controls how much overshoot to add to the shoulder.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Controls how much overshoot to add to the shoulder.")]
        public ClampedFloatParameter shoulderAngle = new ClampedFloatParameter(0f, 0f, 1f);

        /// <summary>
        /// Sets a gamma correction value that HDRP applies to the whole curve.
        /// This parameter is only used when <see cref="TonemappingMode.Custom"/> is set.
        /// </summary>
        [Tooltip("Sets a gamma correction value that HDRP applies to the whole curve.")]
        public MinFloatParameter gamma = new MinFloatParameter(1f, 0.001f);

        /// <summary>
        /// A custom 3D texture lookup table to apply.
        /// This parameter is only used when <see cref="TonemappingMode.External"/> is set.
        /// </summary>
        [Tooltip("A custom 3D texture lookup table to apply.")]
        public Texture3DParameter lutTexture = new Texture3DParameter(null);

        /// <summary>
        /// How much of the lookup texture will contribute to the color grading effect.
        /// This parameter is only used when <see cref="TonemappingMode.External"/> is set.
        /// </summary>
        [Tooltip("How much of the lookup texture will contribute to the color grading effect.")]
        public ClampedFloatParameter lutContribution = new ClampedFloatParameter(1f, 0f, 1f);

        // -- HDR Output options --

        /// <summary>
        /// Specifies the range reduction mode used when HDR output is enabled and Neutral tonemapping is enabled.
        /// </summary>
        [AdditionalProperty]
        [Tooltip("Specifies the range reduction mode used when HDR output is enabled and Neutral tonemapping is enabled.")]
        public NeutralRangeReductionModeParameter neutralHDRRangeReductionMode = new NeutralRangeReductionModeParameter(NeutralRangeReductionMode.BT2390);

        /// <summary>
        /// Specifies the preset to be used for HDR displays.
        /// </summary>
        [Tooltip("Specifies the ACES preset to be used for HDR displays.")]
        public HDRACESPresetParameter acesPreset = new HDRACESPresetParameter(HDRACESPreset.ACES1000Nits);

        /// <summary>
        /// Specifies the fallback tonemapping algorithm to use when outputting to an HDR device, when the main mode is not supported.
        /// </summary>
        /// <seealso cref="TonemappingMode"/>
        [Tooltip("Specifies the fallback tonemapping algorithm to use when outputting to an HDR device, when the main mode is not supported.")]
        public FallbackHDRTonemapParameter fallbackMode = new FallbackHDRTonemapParameter(FallbackHDRTonemap.Neutral);

        /// <summary>
        /// How much hue we want to preserve. Values closer to 0 try to preserve hue, while as values get closer to 1 hue shifts are reintroduced.
        /// </summary>
        [Tooltip("How much hue we want to preserve. Values closer to 0 try to preserve hue, while as values get closer to 1 hue shifts are reintroduced.")]
        public ClampedFloatParameter hueShiftAmount = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// <summary>
        /// Whether to use values detected from the output device as paperwhite. This value will often not lead to equivalent images between SDR and HDR. It is suggested to manually set this value.
        /// </summary>
        [Tooltip("Whether to use values detected from the output device as paperwhite. This value will often not lead to equivalent images between SDR and HDR. It is suggested to manually set this value.")]
        public BoolParameter detectPaperWhite = new BoolParameter(false);
        /// <summary>
        /// The paper white value. It controls how bright a paper white surface should be, it also determines the maximum brightness of UI. The scene is also scaled relative to this value. Value in nits.
        /// </summary>
        [Tooltip("It controls how bright a paper white surface should be, it also determines the maximum brightness of UI. The scene is also scaled relative to this value. Value in nits.")]
        public ClampedFloatParameter paperWhite = new ClampedFloatParameter(300.0f, 0.0f, 400.0f);
        /// <summary>
        /// Whether to use the minimum and maximum brightness values detected from the output device. It might be worth considering calibrating this values manually if the results are not the desired ones.
        /// </summary>
        [Tooltip("Whether to use the minimum and maximum brightness values detected from the output device. It might be worth considering calibrating this values manually if the results are not the desired ones.")]
        public BoolParameter detectBrightnessLimits = new BoolParameter(true);
        /// <summary>
        /// The minimum brightness (in nits) of the screen. Note that this is assumed to be 0.005f with ACES Tonemap.
        /// </summary>
        [Tooltip("The minimum brightness (in nits) of the screen. Note that this is assumed to be 0.005 with ACES Tonemap.")]
        public ClampedFloatParameter minNits = new ClampedFloatParameter(0.005f, 0.0f, 50.0f);
        /// <summary>
        /// The maximum brightness (in nits) of the screen. Note that this is assumed to be defined by the preset when ACES Tonemap is used.
        /// </summary>
        [Tooltip("The maximum brightness (in nits) of the screen. Note that this is assumed to be defined by the preset when ACES Tonemap is used.")]
        public ClampedFloatParameter maxNits = new ClampedFloatParameter(1000.0f, 0.0f, 5000.0f);

        /// <summary>
        /// Tells if the effect needs to be rendered or not.
        /// </summary>
        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive()
        {
            if (mode.value == TonemappingMode.External)
                return ValidateLUT() && lutContribution.value > 0f;

            return mode.value != TonemappingMode.None;
        }

        internal TonemappingMode GetHDRTonemappingMode()
        {
            if (mode.value == TonemappingMode.GranTurismo ||
                mode.value == TonemappingMode.KhronosPBR ||
                mode.value == TonemappingMode.Custom ||
                mode.value == TonemappingMode.External)
            {
                if (fallbackMode.value == FallbackHDRTonemap.None) return TonemappingMode.None;
                if (fallbackMode.value == FallbackHDRTonemap.Neutral) return TonemappingMode.Neutral;
                if (fallbackMode.value == FallbackHDRTonemap.ACES) return TonemappingMode.ACES;
            }

            return mode.value;
        }

        /// <summary>
        /// Validates the format and size of the LUT texture set in <see cref="lutTexture"/>.
        /// </summary>
        /// <returns><c>true</c> if the LUT is valid, <c>false</c> otherwise.</returns>
        public bool ValidateLUT()
        {

            bool valid = false;

            switch (lutTexture.value)
            {
                case Texture3D t:
                    valid |= t.width == t.height
                        && t.height == t.depth;
                    break;
                case RenderTexture rt:
                    valid |= rt.dimension == TextureDimension.Tex3D
                        && rt.width == rt.height
                        && rt.height == rt.volumeDepth;
                    break;
            }

            return valid;
        }


    }
}
