using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    [Serializable, VolumeComponentMenu("Post-processing/White Balance")]
    public sealed class WhiteBalance : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter temperature = new(0f, -100f, 100f);
        public ClampedFloatParameter tint = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !Mathf.Approximately(temperature.value, 0f)
                || !Mathf.Approximately(tint.value, 0f);
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Color Adjustments")]
    public sealed class ColorAdjustments : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter postExposure = new(0f, -20f, 20f);
        public ClampedFloatParameter contrast = new(0f, -100f, 100f);
        public ColorParameter colorFilter = new(Color.white, false, false, true);
        public ClampedFloatParameter hueShift = new(0f, -180f, 180f);
        public ClampedFloatParameter saturation = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !Mathf.Approximately(postExposure.value, 0f)
                || !Mathf.Approximately(contrast.value, 0f)
                || !ColorGradingCurvePresets.IsApproximately(colorFilter.value, Color.white)
                || !Mathf.Approximately(hueShift.value, 0f)
                || !Mathf.Approximately(saturation.value, 0f);
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Channel Mixer")]
    public sealed class ChannelMixer : VolumeComponent, IPostProcessComponent
    {
        [Header("Red Output")]
        public ClampedFloatParameter redOutRedIn = new(100f, -200f, 200f);
        public ClampedFloatParameter redOutGreenIn = new(0f, -200f, 200f);
        public ClampedFloatParameter redOutBlueIn = new(0f, -200f, 200f);

        [Header("Green Output")]
        public ClampedFloatParameter greenOutRedIn = new(0f, -200f, 200f);
        public ClampedFloatParameter greenOutGreenIn = new(100f, -200f, 200f);
        public ClampedFloatParameter greenOutBlueIn = new(0f, -200f, 200f);

        [Header("Blue Output")]
        public ClampedFloatParameter blueOutRedIn = new(0f, -200f, 200f);
        public ClampedFloatParameter blueOutGreenIn = new(0f, -200f, 200f);
        public ClampedFloatParameter blueOutBlueIn = new(100f, -200f, 200f);

        public bool IsActive()
        {
            return !Mathf.Approximately(redOutRedIn.value, 100f)
                || !Mathf.Approximately(redOutGreenIn.value, 0f)
                || !Mathf.Approximately(redOutBlueIn.value, 0f)
                || !Mathf.Approximately(greenOutRedIn.value, 0f)
                || !Mathf.Approximately(greenOutGreenIn.value, 100f)
                || !Mathf.Approximately(greenOutBlueIn.value, 0f)
                || !Mathf.Approximately(blueOutRedIn.value, 0f)
                || !Mathf.Approximately(blueOutGreenIn.value, 0f)
                || !Mathf.Approximately(blueOutBlueIn.value, 100f);
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Split Toning")]
    public sealed class SplitToning : VolumeComponent, IPostProcessComponent
    {
        public NoInterpColorParameter shadows = new(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);
        public NoInterpColorParameter highlights = new(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);
        public ClampedFloatParameter balance = new(0f, -100f, 100f);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(shadows.value, new Color(0.5f, 0.5f, 0.5f, 1f))
                || !ColorGradingCurvePresets.IsApproximately(highlights.value, new Color(0.5f, 0.5f, 0.5f, 1f))
                || !Mathf.Approximately(balance.value, 0f);
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Lift Gamma Gain")]
    public sealed class LiftGammaGain : VolumeComponent, IPostProcessComponent
    {
        public ColorParameter lift = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter gamma = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter gain = new(new Color(1f, 1f, 1f, 0f), false, true, true);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(lift.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gamma.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(gain.value, new Color(1f, 1f, 1f, 0f));
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Shadows Midtones Highlights")]
    public sealed class ShadowsMidtonesHighlights : VolumeComponent, IPostProcessComponent
    {
        public ColorParameter shadows = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter midtones = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ColorParameter highlights = new(new Color(1f, 1f, 1f, 0f), false, true, true);
        public ClampedFloatParameter shadowsStart = new(0f, 0f, 1f);
        public ClampedFloatParameter shadowsEnd = new(0.3f, 0f, 1f);
        public ClampedFloatParameter highlightsStart = new(0.55f, 0f, 1f);
        public ClampedFloatParameter highlightsEnd = new(1f, 0f, 1f);

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsApproximately(shadows.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(midtones.value, new Color(1f, 1f, 1f, 0f))
                || !ColorGradingCurvePresets.IsApproximately(highlights.value, new Color(1f, 1f, 1f, 0f))
                || !Mathf.Approximately(shadowsStart.value, 0f)
                || !Mathf.Approximately(shadowsEnd.value, 0.3f)
                || !Mathf.Approximately(highlightsStart.value, 0.55f)
                || !Mathf.Approximately(highlightsEnd.value, 1f);
        }
    }

    [Serializable, VolumeComponentMenu("Post-processing/Color Curves")]
    public sealed class ColorCurves : VolumeComponent, IPostProcessComponent
    {
        [Header("YRGB")]
        public TextureCurveParameter master = new(ColorGradingCurvePresets.CreateLinearCurve());
        public TextureCurveParameter red = new(ColorGradingCurvePresets.CreateLinearCurve());
        public TextureCurveParameter green = new(ColorGradingCurvePresets.CreateLinearCurve());
        public TextureCurveParameter blue = new(ColorGradingCurvePresets.CreateLinearCurve());

        [Header("HSV")]
        public TextureCurveParameter hueVsHue = new(ColorGradingCurvePresets.CreateFlatCurve(0.5f, true));
        public TextureCurveParameter hueVsSat = new(ColorGradingCurvePresets.CreateFlatCurve(0.5f, true));
        public TextureCurveParameter satVsSat = new(ColorGradingCurvePresets.CreateFlatCurve(0.5f, false));
        public TextureCurveParameter lumVsSat = new(ColorGradingCurvePresets.CreateFlatCurve(0.5f, false));

        public bool IsActive()
        {
            return !ColorGradingCurvePresets.IsLinearCurve(master.value)
                || !ColorGradingCurvePresets.IsLinearCurve(red.value)
                || !ColorGradingCurvePresets.IsLinearCurve(green.value)
                || !ColorGradingCurvePresets.IsLinearCurve(blue.value)
                || !ColorGradingCurvePresets.IsFlatCurve(hueVsHue.value, 0.5f)
                || !ColorGradingCurvePresets.IsFlatCurve(hueVsSat.value, 0.5f)
                || !ColorGradingCurvePresets.IsFlatCurve(satVsSat.value, 0.5f)
                || !ColorGradingCurvePresets.IsFlatCurve(lumVsSat.value, 0.5f);
        }
    }

    internal static class ColorGradingCurvePresets
    {
        private const float CurveTolerance = 1e-3f;
        private static readonly float[] s_CurveSamples = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        internal static TextureCurve CreateLinearCurve()
        {
            return new TextureCurve(
                new[]
                {
                    new Keyframe(0f, 0f),
                    new Keyframe(1f, 1f),
                },
                0f,
                false,
                new Vector2(0f, 1f));
        }

        internal static TextureCurve CreateFlatCurve(float value, bool loop)
        {
            return new TextureCurve(
                new[]
                {
                    new Keyframe(0f, value),
                    new Keyframe(1f, value),
                },
                value,
                loop,
                new Vector2(0f, 1f));
        }

        internal static bool IsLinearCurve(TextureCurve curve)
        {
            if (curve == null)
                return true;

            foreach (var sample in s_CurveSamples)
            {
                if (Mathf.Abs(curve.Evaluate(sample) - sample) > CurveTolerance)
                    return false;
            }

            return true;
        }

        internal static bool IsFlatCurve(TextureCurve curve, float value)
        {
            if (curve == null)
                return true;

            foreach (var sample in s_CurveSamples)
            {
                if (Mathf.Abs(curve.Evaluate(sample) - value) > CurveTolerance)
                    return false;
            }

            return true;
        }

        internal static bool IsApproximately(Color left, Color right, float epsilon = CurveTolerance)
        {
            return Mathf.Abs(left.r - right.r) <= epsilon
                && Mathf.Abs(left.g - right.g) <= epsilon
                && Mathf.Abs(left.b - right.b) <= epsilon
                && Mathf.Abs(left.a - right.a) <= epsilon;
        }
    }
}
