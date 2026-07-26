using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class AutoExposureBenchmarkPresetTests
    {
        [TearDown]
        public void TearDown()
        {
            AutoExposureCompensationCurveUtility.Dispose();
        }

        [Test]
        public void AutoExposureBenchmarkPresets_ExposeUniqueNamedPresetCoverage()
        {
            var names = new HashSet<string>();
            var presetCount = 0;

            foreach (var preset in AutoExposureBenchmarkPresets.All)
            {
                presetCount++;
                Assert.That(names.Add(preset.Name), Is.True, $"Duplicate preset name '{preset.Name}'.");
                Assert.That(preset.Description, Is.Not.Empty);
            }

            Assert.That(presetCount, Is.EqualTo(6));
        }

        [TestCaseSource(typeof(AutoExposureBenchmarkPresets), nameof(AutoExposureBenchmarkPresets.All))]
        public void AutoExposureBenchmarkPresets_StayWithinVolumeSafeEv100Range(AutoExposurePresetDefinition preset)
        {
            Assert.That(
                preset.MinEV100,
                Is.InRange(AutoExposureCommonPresets.VolumeSafeMinEV100, AutoExposureCommonPresets.VolumeSafeMaxEV100));
            Assert.That(
                preset.MaxEV100,
                Is.InRange(AutoExposureCommonPresets.VolumeSafeMinEV100, AutoExposureCommonPresets.VolumeSafeMaxEV100));
            Assert.That(preset.MaxEV100, Is.GreaterThan(preset.MinEV100));
        }

        [Test]
        public void HistogramBalanced_UsesUeDefaultPercentWindowAndHdrFriendlyBias()
        {
            var preset = AutoExposureCommonPresets.Get(AutoExposureCommonPreset.HistogramBalanced);

            Assert.That(preset.Percent.x, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(preset.Percent.y, Is.EqualTo(90f).Within(1e-5f));
            Assert.That(preset.ExposureCompensation, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void HistogramBalanced_PrefersMidSkyOverBrightOutliers_WhenEvaluatingHdrSkyHistogram()
        {
            var balancedSettings = AutoExposureCommonPresets
                .Get(AutoExposureCommonPreset.HistogramBalanced)
                .CreateSettingsData(isFirstFrame: true);
            var aggressiveSettings = balancedSettings;
            aggressiveSettings.exposureLowPercent = 0.8f;
            aggressiveSettings.exposureHighPercent = 0.95f;

            var hdrSkyHistogram = new uint[AutoExposureReferenceSolver.HistogramBinCount];
            hdrSkyHistogram[18] = 720;
            hdrSkyHistogram[36] = 240;
            hdrSkyHistogram[63] = 40;

            var previousExposureState = new Vector4(
                1f,
                1f,
                AutoExposureSettingsResolver.MiddleGrey,
                balancedSettings.exposureCompensationAll);

            var balancedResult = AutoExposureReferenceSolver.ResolveExposureState(
                hdrSkyHistogram,
                balancedSettings,
                previousExposureState);
            var aggressiveResult = AutoExposureReferenceSolver.ResolveExposureState(
                hdrSkyHistogram,
                aggressiveSettings,
                previousExposureState);

            Assert.That(balancedResult.currentExposureScale, Is.GreaterThan(aggressiveResult.currentExposureScale));
            Assert.That(balancedResult.averageSceneLuminance, Is.LessThan(aggressiveResult.averageSceneLuminance));
        }

        [TestCaseSource(typeof(AutoExposureBenchmarkPresets), nameof(AutoExposureBenchmarkPresets.All))]
        public void AutoExposureBenchmarkPresets_CreateVolumeComponent_ProducesActivePreset(AutoExposurePresetDefinition preset)
        {
            var component = preset.CreateVolumeComponent();

            try
            {
                Assert.That(component.enabled.value, Is.True);
                Assert.That(component.mode.value, Is.EqualTo(preset.Mode));
                Assert.That(
                    component.exposureMode.value,
                    Is.EqualTo(AutoExposureExposureMode.AutomaticHistogram),
                    "Unreal presets must not mutate HDRP settings.");
                Assert.That(component.percent.value.x, Is.EqualTo(preset.Percent.x).Within(1e-5f));
                Assert.That(component.percent.value.y, Is.EqualTo(preset.Percent.y).Within(1e-5f));
                Assert.That(component.minEV100.value, Is.EqualTo(preset.MinEV100).Within(1e-5f));
                Assert.That(component.maxEV100.value, Is.EqualTo(preset.MaxEV100).Within(1e-5f));
                Assert.That(component.manualEV100.value, Is.EqualTo(preset.ManualEV100).Within(1e-5f));
                Assert.That(component.applyPhysicalCameraExposure.value, Is.EqualTo(preset.ApplyPhysicalCameraExposure));
                Assert.That(component.exposureCompensation.value, Is.EqualTo(preset.ExposureCompensation).Within(1e-5f));
                Assert.That(component.histogramLogRange.value.x, Is.EqualTo(preset.HistogramLogRangeEV100.x).Within(1e-5f));
                Assert.That(component.histogramLogRange.value.y, Is.EqualTo(preset.HistogramLogRangeEV100.y).Within(1e-5f));
                Assert.That(component.IsUnrealActive(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(component);
            }
        }

        [TestCaseSource(typeof(AutoExposureBenchmarkPresets), nameof(AutoExposureBenchmarkPresets.All))]
        public void AutoExposureBenchmarkPresets_CreateSettingsData_ProducesResolvableBenchmarkInputs(AutoExposurePresetDefinition preset)
        {
            var cameraObject = preset.ApplyPhysicalCameraExposure
                ? new GameObject($"{preset.Name} Camera")
                : null;
            var camera = cameraObject != null ? cameraObject.AddComponent<Camera>() : null;

            try
            {
                if (camera != null)
                {
                    camera.usePhysicalProperties = true;
                    camera.aperture = 2.8f;
                    camera.shutterSpeed = 1f / 48f;
                    camera.iso = 200;
                }

                var settings = preset.CreateSettingsData(camera);

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.mode, Is.EqualTo(preset.Mode));
                Assert.That(settings.exposureCompensationCurveTexture, Is.Not.Null);
                Assert.That(settings.exposureCompensationCurveInvRange, Is.GreaterThan(0f));

                if (preset.Mode == AutoExposureMode.Histogram)
                {
                    Assert.That(settings.exposureLowPercent, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(settings.exposureLowPercent, Is.LessThanOrEqualTo(settings.exposureHighPercent));
                    Assert.That(settings.minAverageLuminance, Is.GreaterThan(0f));
                    Assert.That(settings.maxAverageLuminance, Is.GreaterThanOrEqualTo(settings.minAverageLuminance));
                    Assert.That(settings.exposureSpeedUp, Is.GreaterThan(0f));
                    Assert.That(settings.exposureSpeedDown, Is.GreaterThan(0f));
                    Assert.That(settings.histogramScale, Is.GreaterThan(0f));
                    Assert.That(settings.luminanceMin, Is.GreaterThan(0f));
                    Assert.That(settings.forceTarget, Is.EqualTo(0f).Within(1e-5f));
                }
                else
                {
                    Assert.That(settings.forceTarget, Is.EqualTo(1f).Within(1e-5f));
                    Assert.That(settings.fixedExposureScale, Is.GreaterThan(0f));
                    Assert.That(settings.manualAverageSceneLuminance, Is.GreaterThan(0f));
                }

                if (camera != null)
                {
                    var expectedEV100 = AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(camera);
                    Assert.That(settings.manualEV100, Is.EqualTo(expectedEV100).Within(1e-5f));
                }
            }
            finally
            {
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
