using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
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

#pragma warning disable 414
        [SerializeField]
        private int m_SelectedCurve;
#pragma warning restore 414

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
}
