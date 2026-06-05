using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
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
        public void ResolveColorGradingSpace_DefaultsToSrgb_WhenPipelineAssetIsNull()
        {
            var result = ColorGradingSpaceUtility.ResolveColorGradingSpace(null);

            Assert.That(result, Is.EqualTo(ColorGradingSpace.sRGB));
        }

        [Test]
        public void GetColorGradingSpaceKeyword_ReturnsAcesCgKeyword_WhenAcesCgIsSelected()
        {
            var result = ColorGradingSpaceUtility.GetColorGradingSpaceKeyword(ColorGradingSpace.AcesCg);

            Assert.That(result, Is.EqualTo("GRADE_IN_ACESCG"));
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
        public void GetHDRTonemappingMode_ReturnsAgX_WhenAgXIsSelected()
        {
            var tonemapping = new Tonemapping();
            tonemapping.mode.value = TonemappingMode.AgX;
            tonemapping.fallbackMode.value = FallbackHDRTonemap.ACES;

            Assert.That(tonemapping.GetHDRTonemappingMode(), Is.EqualTo(TonemappingMode.AgX));
        }

        [Test]
        public void GetHDRTonemappingMode_UsesFallback_WhenKhronosPbrIsSelected()
        {
            var tonemapping = new Tonemapping();
            tonemapping.mode.value = TonemappingMode.KhronosPBR;
            tonemapping.fallbackMode.value = FallbackHDRTonemap.Neutral;

            Assert.That(tonemapping.GetHDRTonemappingMode(), Is.EqualTo(TonemappingMode.Neutral));
        }

        [Test]
        public void ColorCurves_IsInactive_WithDefaultCurves()
        {
            var colorCurves = new ColorCurves();

            Assert.That(colorCurves.IsActive(), Is.False);
        }

        [Test]
        public void ShadowsMidtonesHighlights_IsInactive_WithDefaultValues()
        {
            var settings = new ShadowsMidtonesHighlights();

            Assert.That(settings.IsActive(), Is.False);
        }

        [Test]
        public void ShadowsMidtonesHighlights_IsActive_WhenColorWheelChanges()
        {
            var settings = new ShadowsMidtonesHighlights();

            settings.shadows.value = new Vector4(1.1f, 1f, 1f, 0f);

            Assert.That(settings.IsActive(), Is.True);
        }

        [Test]
        public void ShadowsMidtonesHighlights_IsActive_DoesNotAllocate_WithDefaultValues()
        {
            var settings = new ShadowsMidtonesHighlights();
            settings.IsActive();

            var isActive = false;
            var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 128; i++)
                isActive |= settings.IsActive();
            var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(isActive, Is.False);
            Assert.That(allocatedBytes, Is.Zero);
        }

        [Test]
        public void Resolve_WithFrameData_CachesSettingsForLaterPasses()
        {
            using var frameData = new ContextContainer();

            var settings = ColorGradingSettingsResolver.Resolve(frameData, out var curves);

            Assert.That(
                ColorGradingSettingsResolver.TryGetResolved(frameData, out var cachedSettings, out var cachedCurves),
                Is.True);
            Assert.That(cachedSettings.postExposureLinear, Is.EqualTo(settings.postExposureLinear));
            Assert.That(cachedCurves, Is.SameAs(curves));
        }

        [Test]
        public void ClearFrameCache_InvalidatesCachedSettings()
        {
            using var frameData = new ContextContainer();

            ColorGradingSettingsResolver.Resolve(frameData, out _);
            Assert.That(ColorGradingSettingsResolver.TryGetResolved(frameData, out _, out _), Is.True);

            ColorGradingSettingsResolver.ClearFrameCache(frameData);

            Assert.That(ColorGradingSettingsResolver.TryGetResolved(frameData, out _, out _), Is.False);
        }
    }
}
