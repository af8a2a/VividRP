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
        public void Tonemapping_IsInactive_WhenExternalModeHasNoLut()
        {
            var tonemapping = new Tonemapping();
            tonemapping.mode.value = TonemappingMode.External;
            tonemapping.lutTexture.value = null;

            Assert.That(tonemapping.IsActive(), Is.False);
        }

        [Test]
        public void ColorCurves_IsInactive_WithDefaultCurves()
        {
            var colorCurves = new ColorCurves();

            Assert.That(colorCurves.IsActive(), Is.False);
        }
    }
}
