using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class ColorGradingSettingsResolverTests
    {
        [Test]
        public void BuildHueSatCon_ConvertsHdrpStyleRangesToShaderParameters()
        {
            var result = ColorGradingSettingsResolver.BuildHueSatCon(90f, 25f, -40f);

            Assert.That(result.x, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(result.y, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(result.z, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(result.w, Is.EqualTo(0f));
        }

        [Test]
        public void BuildChannelMixerVector_NormalizesPercentageInputs()
        {
            var result = ColorGradingSettingsResolver.BuildChannelMixerVector(50f, -25f, 125f);

            Assert.That(result, Is.EqualTo(new Vector4(0.5f, -0.25f, 1.25f, 0f)));
        }

        [Test]
        public void ResolvePostExposure_ConvertsStopsToLinearMultiplier()
        {
            var adjustments = new ColorAdjustments();
            adjustments.postExposure.value = 2f;

            var result = ColorGradingSettingsResolver.ResolvePostExposure(adjustments);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void BuildGranTurismoParams_PacksShaderInputs()
        {
            var params0 = ColorGradingSettingsResolver.BuildGranTurismoParams0(4f, 1.25f, 0.3f, 0.45f);
            var params1 = ColorGradingSettingsResolver.BuildGranTurismoParams1(1.5f, 0.1f);

            Assert.That(params0, Is.EqualTo(new Vector4(4f, 1.25f, 0.3f, 0.45f)));
            Assert.That(params1, Is.EqualTo(new Vector4(1.5f, 0.1f, 0f, 0f)));
        }

        [Test]
        public void Tonemapping_IsInactive_WhenExternalModeHasNoLut()
        {
            var tonemapping = new Tonemapping();
            tonemapping.mode.value = TonemappingMode.External;
            tonemapping.lutTexture.value = null;

            Assert.That(tonemapping.IsActive(), Is.False);
        }

        [Test]
        public void GetHDRTonemappingMode_UsesFallback_WhenGranTurismoIsSelected()
        {
            var tonemapping = new Tonemapping();
            tonemapping.mode.value = TonemappingMode.GranTurismo;
            tonemapping.fallbackMode.value = FallbackHDRTonemap.ACES;

            Assert.That(tonemapping.GetHDRTonemappingMode(), Is.EqualTo(TonemappingMode.ACES));
        }

        [Test]
        public void ColorCurves_IsInactive_WithDefaultCurves()
        {
            var colorCurves = new ColorCurves();

            Assert.That(colorCurves.IsActive(), Is.False);
        }
    }
}
