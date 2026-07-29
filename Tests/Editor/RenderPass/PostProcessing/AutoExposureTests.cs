using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass;

namespace VividRP.Editor.Tests
{
    public class AutoExposureTests
    {
        [Test]
        public void AutoExposureHistory_UsesCameraHistoryBufferAndTexturePairs()
        {
            var cameraObject = new GameObject("AutoExposureCameraHistoryTests.Camera");
            var camera = cameraObject.AddComponent<Camera>();
            var history = camera.GetVividCameraHistory();
            var state = new AutoExposureHistoryState();

            try
            {
                history.BeginFrame(1, 1);
                var prepareHistory = typeof(VividAutoExposureSystem).GetMethod(
                    "PrepareAutoExposureHistory",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.That(prepareHistory, Is.Not.Null);
                prepareHistory.Invoke(null, new object[] { state, camera });

                Assert.That(state.exposureBufferHistory, Is.Not.Null);
                Assert.That(state.exposureTextureHistory, Is.Not.Null);
                Assert.That(state.exposureBufferHistory.FrameCount, Is.EqualTo(2));
                Assert.That(state.exposureTextureHistory.FrameCount, Is.EqualTo(2));
                Assert.That(state.exposureBufferHistory.GetCurrent(), Is.Not.Null);
                Assert.That(state.exposureBufferHistory.Descriptor.Count, Is.EqualTo(1));
                Assert.That(
                    state.exposureTextureHistory.GetCurrent().name,
                    Does.EndWith("_AutoExposureCameraHistoryTests.Camera_0"));
            }
            finally
            {
                history.AbortFrame();
                state.Dispose();
                CameraHistorySystem.Dispose();
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AutoExposure_IsInactive_WhenDisabled()
        {
            var autoExposure = new AutoExposure();

            Assert.That(autoExposure.IsActive(), Is.False);
        }

        [Test]
        public void AutoExposure_IsActive_WhenEnabledAndParametersAreValid()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.minEV100.value = -5.058894f;
            autoExposure.maxEV100.value = 1f;
            autoExposure.speedUp.value = 3f;
            autoExposure.speedDown.value = 1f;

            Assert.That(autoExposure.IsUnrealActive(), Is.True);
        }

        [Test]
        public void AutoExposure_IsActive_WhenHistogramEV100RangeIsValid()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.minEV100.value = -5.058894f;
            autoExposure.maxEV100.value = 1f;
            autoExposure.speedUp.value = 3f;
            autoExposure.speedDown.value = 1f;

            Assert.That(autoExposure.IsUnrealActive(), Is.True);
        }

        [Test]
        public void AutoExposure_IsActiveInManualMode_WhenEnabledWithoutHistogramConstraints()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.mode.value = AutoExposureMode.Manual;
            autoExposure.minBrightness.value = 2f;
            autoExposure.maxBrightness.value = 1f;
            autoExposure.speedUp.value = 0f;
            autoExposure.speedDown.value = 0f;

            Assert.That(autoExposure.IsUnrealActive(), Is.True);
        }

        [Test]
        public void AutoExposure_ImplementationPaths_DoNotSynchronizeModes()
        {
            var autoExposure = new AutoExposure();
            autoExposure.mode.value = AutoExposureMode.Manual;
            autoExposure.applyPhysicalCameraExposure.value = true;
            autoExposure.exposureMode.overrideState = false;
            autoExposure.exposureMode.value = AutoExposureExposureMode.AutomaticHistogram;

            Assert.That(
                autoExposure.ResolveExposureMode(),
                Is.EqualTo(AutoExposureExposureMode.AutomaticHistogram));
            Assert.That(autoExposure.mode.value, Is.EqualTo(AutoExposureMode.Manual));
        }

        [Test]
        public void AutoExposure_HDRPMigration_CopiesOverriddenSharedSettingsOnlyOnce()
        {
            var autoExposure = new AutoExposure();
            autoExposure.percent.overrideState = true;
            autoExposure.percent.value = new Vector2(25f, 75f);
            autoExposure.minEV100.overrideState = true;
            autoExposure.minEV100.value = -2f;

            var migrate = typeof(AutoExposure).GetMethod(
                "MigrateSharedHDRPSettingsIfNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(migrate, Is.Not.Null);
            migrate.Invoke(autoExposure, null);

            Assert.That(autoExposure.histogramPercentages.overrideState, Is.True);
            Assert.That(
                autoExposure.histogramPercentages.value,
                Is.EqualTo(new Vector2(25f, 75f)));
            Assert.That(autoExposure.limitMin.overrideState, Is.True);
            Assert.That(autoExposure.limitMin.value, Is.EqualTo(-2f));
            Assert.That(autoExposure.limitMax.overrideState, Is.False);
            Assert.That(autoExposure.limitMax.value, Is.EqualTo(13f));

            autoExposure.percent.value = new Vector2(40f, 60f);
            migrate.Invoke(autoExposure, null);

            Assert.That(
                autoExposure.histogramPercentages.value,
                Is.EqualTo(new Vector2(25f, 75f)));
        }

        [Test]
        public void AutoExposure_UnrealMaskMigration_CopiesLegacySharedMaskOnlyOnce()
        {
            var autoExposure = new AutoExposure();
            var legacyMask = new Texture2D(2, 2);
            var replacementMask = new Texture2D(2, 2);

            try
            {
                autoExposure.weightTextureMask.value = legacyMask;
                autoExposure.weightTextureMask.overrideState = true;

                var migrate = typeof(AutoExposure).GetMethod(
                    "MigrateLegacyUnrealMeteringMaskIfNeeded",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(migrate, Is.Not.Null);
                migrate.Invoke(autoExposure, null);

                Assert.That(autoExposure.exposureMeteringMask.value, Is.SameAs(legacyMask));
                Assert.That(autoExposure.exposureMeteringMask.overrideState, Is.True);

                autoExposure.weightTextureMask.value = replacementMask;
                migrate.Invoke(autoExposure, null);

                Assert.That(autoExposure.exposureMeteringMask.value, Is.SameAs(legacyMask));
            }
            finally
            {
                Object.DestroyImmediate(legacyMask);
                Object.DestroyImmediate(replacementMask);
            }
        }

        [Test]
        public void AutoExposure_IsActive_WhenFixedAdaptationIgnoresSpeedParameters()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.adaptationMode.value = AutoExposureAdaptationMode.Fixed;
            autoExposure.adaptationSpeedDarkToLight.value = 0f;
            autoExposure.adaptationSpeedLightToDark.value = 0f;

            Assert.That(autoExposure.IsHDRPActive(), Is.True);
        }

        [Test]
        public void AutoExposure_HDRPDefaults_MatchReferenceProfile()
        {
            var autoExposure = new AutoExposure();

            Assert.That(
                autoExposure.exposureMode.value,
                Is.EqualTo(AutoExposureExposureMode.AutomaticHistogram));
            Assert.That(
                autoExposure.meteringMode.value,
                Is.EqualTo(AutoExposureMeteringMode.ProceduralMask));
            Assert.That(autoExposure.centerAroundExposureTarget.value, Is.False);
            Assert.That(autoExposure.proceduralCenter.value, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(autoExposure.proceduralRadii.value, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(autoExposure.proceduralSoftness.value, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(autoExposure.maskMinIntensity.value, Is.EqualTo(-30f).Within(1e-5f));
            Assert.That(autoExposure.maskMaxIntensity.value, Is.EqualTo(30f).Within(1e-5f));
            Assert.That(autoExposure.limitMin.value, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(autoExposure.limitMax.value, Is.EqualTo(13f).Within(1e-5f));
            Assert.That(autoExposure.compensation.value, Is.Zero);
            Assert.That(
                autoExposure.histogramPercentages.value,
                Is.EqualTo(new Vector2(10f, 90f)));
            Assert.That(autoExposure.histogramUseCurveRemapping.value, Is.False);
            Assert.That(
                autoExposure.adaptationMode.value,
                Is.EqualTo(AutoExposureAdaptationMode.Progressive));
            Assert.That(
                autoExposure.adaptationSpeedDarkToLight.value,
                Is.EqualTo(4f).Within(1e-5f));
            Assert.That(
                autoExposure.adaptationSpeedLightToDark.value,
                Is.EqualTo(4f).Within(1e-5f));
            Assert.That(autoExposure.targetMidGray.value, Is.EqualTo(TargetMidGray.Grey125));
        }

        [Test]
        public void AutoExposure_UnrealDefaults_MatchUE56ReferenceProfile()
        {
            var autoExposure = new AutoExposure();

            Assert.That(autoExposure.mode.value, Is.EqualTo(AutoExposureMode.Histogram));
            Assert.That(autoExposure.percent.value, Is.EqualTo(new Vector2(10f, 90f)));
            Assert.That(autoExposure.minBrightness.value, Is.EqualTo(0.03f).Within(1e-5f));
            Assert.That(autoExposure.maxBrightness.value, Is.EqualTo(8f).Within(1e-5f));
            Assert.That(autoExposure.minEV100.value, Is.EqualTo(-10f).Within(1e-5f));
            Assert.That(autoExposure.maxEV100.value, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(autoExposure.minEV100.min, Is.EqualTo(-10f).Within(1e-5f));
            Assert.That(autoExposure.minEV100.max, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(autoExposure.maxEV100.min, Is.EqualTo(-10f).Within(1e-5f));
            Assert.That(autoExposure.maxEV100.max, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(
                autoExposure.histogramLogRange.value,
                Is.EqualTo(new Vector2(-10f, 20f)));
            Assert.That(autoExposure.histogramLogRange.min, Is.EqualTo(-10f).Within(1e-5f));
            Assert.That(autoExposure.histogramLogRange.max, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(autoExposure.speedUp.value, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(autoExposure.speedDown.value, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(autoExposure.applyPhysicalCameraExposure.value, Is.True);
            Assert.That(autoExposure.exposureCompensation.value, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(autoExposure.exposureMeteringMask.value, Is.Null);
        }

        [Test]
        public void ResolveUnreal_BasicUsesIndependentMaskAndFullHistogramRange()
        {
            var autoExposure = ScriptableObject.CreateInstance<AutoExposure>();
            var meterMask = new Texture2D(2, 2);

            try
            {
                autoExposure.enabled.value = true;
                autoExposure.mode.value = AutoExposureMode.Basic;
                autoExposure.exposureMeteringMask.value = meterMask;

                var settings = AutoExposureSettingsResolver.ResolveUnreal(
                    autoExposure,
                    null,
                    true);

                Assert.That(settings.implementation, Is.EqualTo(AutoExposureImplementationPath.Unreal));
                Assert.That(settings.mode, Is.EqualTo(AutoExposureMode.Basic));
                Assert.That(settings.exposureLowPercent, Is.Zero);
                Assert.That(settings.exposureHighPercent, Is.EqualTo(1f));
                Assert.That(settings.luminanceMin, Is.EqualTo(1e-4f).Within(1e-7f));
                Assert.That(settings.unrealExposureMeteringMask, Is.SameAs(meterMask));
                Assert.That(settings.unrealBlackHistogramBucketInfluence, Is.Zero);
                Assert.That(settings.unrealCompensationCurveHasHistory, Is.False);
                Assert.That(settings.hdrpWeightTextureMask, Is.Null);
                Assert.That(settings.forceTarget, Is.EqualTo(1f));

                settings = AutoExposureSettingsResolver.ResolveUnreal(
                    autoExposure,
                    null,
                    false);

                Assert.That(settings.unrealCompensationCurveHasHistory, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(meterMask);
                Object.DestroyImmediate(autoExposure);
            }
        }

        [Test]
        public void ResolveUnreal_HistogramUsesSelectedPercentWindow()
        {
            var autoExposure = ScriptableObject.CreateInstance<AutoExposure>();

            try
            {
                autoExposure.enabled.value = true;
                autoExposure.mode.value = AutoExposureMode.Histogram;
                autoExposure.percent.value = new Vector2(25f, 75f);

                var settings = AutoExposureSettingsResolver.ResolveUnreal(
                    autoExposure,
                    null,
                    false);

                Assert.That(settings.exposureLowPercent, Is.EqualTo(0.25f).Within(1e-5f));
                Assert.That(settings.exposureHighPercent, Is.EqualTo(0.75f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(autoExposure);
            }
        }

        [Test]
        public void ResolveUnreal_InvalidRangeLocksToMaximumEV100LikeUnreal()
        {
            var autoExposure = ScriptableObject.CreateInstance<AutoExposure>();

            try
            {
                autoExposure.enabled.value = true;
                autoExposure.minEV100.value = 8f;
                autoExposure.maxEV100.value = 2f;

                var settings = AutoExposureSettingsResolver.ResolveUnreal(
                    autoExposure,
                    null,
                    false);
                var expectedAverageLuminance =
                    AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(2f);

                Assert.That(autoExposure.IsUnrealActive(), Is.True);
                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.forceTarget, Is.EqualTo(1f));
                Assert.That(
                    settings.minAverageLuminance,
                    Is.EqualTo(expectedAverageLuminance).Within(1e-6f));
                Assert.That(
                    settings.maxAverageLuminance,
                    Is.EqualTo(expectedAverageLuminance).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(autoExposure);
            }
        }

        [Test]
        public void ResolveHDRPProceduralMeteringParameters_PacksReferenceDefaults()
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            settings.hdrpProceduralCenter = new Vector2(0.5f, 0.5f);
            settings.hdrpProceduralRadii = new Vector2(0.5f, 0.5f);
            settings.hdrpProceduralSoftness = 1f;
            settings.hdrpMaskMinIntensity = -30f;
            settings.hdrpMaskMaxIntensity = 30f;

            AutoExposureSettingsResolver.ResolveHDRPProceduralMeteringParameters(
                settings,
                null,
                1920,
                1080,
                out var maskParams,
                out var maskParams2);

            Assert.That(maskParams, Is.EqualTo(new Vector4(960f, 540f, 960f, 540f)));
            Assert.That(maskParams2.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(
                maskParams2.y,
                Is.EqualTo(LightUnitUtils.Ev100ToNits(-30f)).Within(1e-8f));
            Assert.That(
                maskParams2.z,
                Is.EqualTo(LightUnitUtils.Ev100ToNits(30f)).Within(1e-3f));
        }

        [Test]
        public void ResolveLuminanceMaxFromLensAttenuation_ReturnsUnrealUnitlessDefault()
        {
            var result = AutoExposureSettingsResolver.ResolveLuminanceMaxFromLensAttenuation();

            Assert.That(result, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ResolveHistogramLogRangeFromEV100_ConvertsIntoLog2LuminanceRange()
        {
            var result = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(-10f, 6f);

            Assert.That(result.x, Is.EqualTo(-10f).Within(1e-5f));
            Assert.That(result.y, Is.EqualTo(6f).Within(1e-5f));
        }

        [Test]
        public void BuildHistogramScaleBiasFromEV100_PacksUeRangeIntoShaderSpace()
        {
            var result = AutoExposureSettingsResolver.BuildHistogramScaleBiasFromEV100(-10f, 6f);

            Assert.That(result.x, Is.EqualTo(1f / 16f).Within(1e-5f));
            Assert.That(result.y, Is.EqualTo(10f / 16f).Within(1e-5f));
        }

        [Test]
        public void ResolveExposureCompensation_ConvertsStopsToLinearMultiplier()
        {
            var result = AutoExposureSettingsResolver.ResolveExposureCompensation(2f);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void ResolveExposureCompensationCurveStops_SamplesCurveAtAverageSceneEV100()
        {
            var curve = AnimationCurve.Linear(-2f, -1f, 2f, 1f);

            var result = AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(curve, 1f);

            Assert.That(result, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void AutoExposureCurveQueries_DoNotAllocate_ForRepeatedCurves()
        {
            var compensationCurve = AnimationCurve.Linear(-2f, -1f, 2f, 1f);
            var curveMap = AnimationCurve.Linear(-4f, -1f, 4f, 2f);

            try
            {
                AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(compensationCurve, 1f);
                AutoExposureCompensationCurveUtility.Resolve(compensationCurve);
                AutoExposureCurveMapUtility.Resolve(curveMap, -2f, 3f);

                var allocatedBefore = global::System.GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 32; index++)
                {
                    AutoExposureSettingsResolver.ResolveExposureCompensationCurveStops(compensationCurve, 1f);
                    AutoExposureCompensationCurveUtility.Resolve(compensationCurve);
                    AutoExposureCurveMapUtility.Resolve(curveMap, -2f, 3f);
                }

                var allocatedBytes = global::System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(allocatedBytes, Is.Zero);
            }
            finally
            {
                AutoExposureCompensationCurveUtility.Dispose();
                AutoExposureCurveMapUtility.Dispose();
            }
        }

        [Test]
        public void ResolveExposureCompensationAll_MultipliesSettingsAndCurveStops()
        {
            var result = AutoExposureSettingsResolver.ResolveExposureCompensationAll(2f, 1f);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void ResolveWhitePointLuminanceFromEV100_ConvertsStopsToWhitePoint()
        {
            var result = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(2f);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void ResolveManualExposureScale_CombinesManualEVAndExposureCompensation()
        {
            var result = AutoExposureSettingsResolver.ResolveManualExposureScale(2f, 4f);

            Assert.That(result, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void ResolveAverageSceneEV100FromLuminance_InvertsAverageSceneLuminanceConversion()
        {
            var expectedEV100 = 3f;
            var averageSceneLuminance = AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(expectedEV100);

            var result = AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(averageSceneLuminance);

            Assert.That(result, Is.EqualTo(expectedEV100).Within(1e-5f));
        }

        [Test]
        public void ResolvePhysicalCameraEV100_ComputesExpectedExposureValue()
        {
            var cameraObject = new GameObject("Physical Camera EV100 Test");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aperture = 4f;
                camera.shutterSpeed = 1f / 125f;
                camera.iso = 200;

                var result = AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(camera);
                var expected = Mathf.Log((4f * 4f) / (1f / 125f) * 100f / 200f, 2f);

                Assert.That(result, Is.EqualTo(expected).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ResolveManualEV100_UsesPhysicalCameraExposure_WhenEnabled()
        {
            var cameraObject = new GameObject("Manual EV100 Physical Camera Test");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aperture = 2.8f;
                camera.shutterSpeed = 1f / 60f;
                camera.iso = 100;

                var result = AutoExposureSettingsResolver.ResolveManualEV100(camera, -3f, true);
                var expected = AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(camera);

                Assert.That(result, Is.EqualTo(expected).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ResolveManualEV100_ReturnsManualValue_WhenPhysicalCameraExposureIsDisabled()
        {
            var cameraObject = new GameObject("Manual EV100 Fallback Test");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aperture = 2.8f;
                camera.shutterSpeed = 1f / 60f;
                camera.iso = 100;

                var result = AutoExposureSettingsResolver.ResolveManualEV100(camera, -3f, false);

                Assert.That(result, Is.EqualTo(-3f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void AutoExposure_MigratesLegacyHistogramLogRangeValuesIntoFloatRangeParameter()
        {
            var autoExposure = ScriptableObject.CreateInstance<AutoExposure>();

            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var legacyMinField = typeof(AutoExposure).GetField("histogramLogMin", flags);
                var legacyMaxField = typeof(AutoExposure).GetField("histogramLogMax", flags);
                var migrateMethod = typeof(AutoExposure).GetMethod("MigrateLegacyHistogramLogRangeIfNeeded", flags);

                Assert.That(legacyMinField, Is.Not.Null);
                Assert.That(legacyMaxField, Is.Not.Null);
                Assert.That(migrateMethod, Is.Not.Null);

                var legacyMin = legacyMinField.GetValue(autoExposure);
                var legacyMax = legacyMaxField.GetValue(autoExposure);

                Assert.That(legacyMin, Is.Not.Null);
                Assert.That(legacyMax, Is.Not.Null);

                autoExposure.histogramLogRange.overrideState = false;
                autoExposure.histogramLogRange.value = new Vector2(-10f, 6f);
                SetMemberValue(legacyMin, "overrideState", true);
                SetMemberValue(legacyMin, "value", -8f);
                SetMemberValue(legacyMax, "value", 12f);

                migrateMethod.Invoke(autoExposure, null);

                Assert.That(autoExposure.histogramLogRange.overrideState, Is.True);
                Assert.That(autoExposure.histogramLogRange.value.x, Is.EqualTo(-8f).Within(1e-5f));
                Assert.That(autoExposure.histogramLogRange.value.y, Is.EqualTo(12f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(autoExposure);
            }
        }

        [Test]
        public void ResolvePhysicalCameraFallback_UsesFixedManualExposure_WhenPhysicalCameraIsEnabled()
        {
            var cameraObject = new GameObject("Physical Camera Exposure Fallback Test");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.usePhysicalProperties = true;
                camera.aperture = 4f;
                camera.shutterSpeed = 1f / 125f;
                camera.iso = 200;

                var settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(
                    AutoExposureSettingsData.CreateDefault(),
                    camera);
                var expectedEV100 = AutoExposureSettingsResolver.ResolvePhysicalCameraEV100(camera);

                Assert.That(settings.enabled, Is.True);
                Assert.That(settings.mode, Is.EqualTo(AutoExposureMode.Manual));
                Assert.That(settings.applyPhysicalCameraExposure, Is.True);
                Assert.That(settings.manualEV100, Is.EqualTo(expectedEV100).Within(1e-5f));
                Assert.That(
                    settings.manualAverageSceneLuminance,
                    Is.EqualTo(AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(expectedEV100)).Within(1e-5f));
                Assert.That(
                    settings.fixedExposureScale,
                    Is.EqualTo(AutoExposureSettingsResolver.ResolveManualExposureScale(expectedEV100, 1f)).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ResolvePhysicalCameraFallback_KeepsExposureDisabled_WhenPhysicalCameraIsDisabled()
        {
            var cameraObject = new GameObject("Physical Camera Exposure Disabled Test");

            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.usePhysicalProperties = false;

                var settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(
                    AutoExposureSettingsData.CreateDefault(),
                    camera);

                Assert.That(settings.enabled, Is.False);
                Assert.That(settings.mode, Is.EqualTo(AutoExposureMode.Histogram));
                Assert.That(settings.applyPhysicalCameraExposure, Is.False);
                Assert.That(settings.fixedExposureScale, Is.EqualTo(1f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ComputeExponentialTransitionMultiplier_ReturnsPositiveBlendFactor()
        {
            var result = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(3f, AutoExposureSettingsResolver.DefaultStartDistance);

            Assert.That(result, Is.GreaterThan(0f));
            Assert.That(result, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesGoldenAverageSceneLuminanceFromHistogram()
        {
            var settings = CreateGoldenHistogramSettings();
            var histogram = CreateGoldenHistogram();

            var resolved = AutoExposureReferenceSolver.TryResolveAverageSceneLuminance(
                histogram,
                settings.exposureLowPercent,
                settings.exposureHighPercent,
                settings.histogramScale,
                settings.histogramBias,
                out var averageSceneLuminance);

            Assert.That(resolved, Is.True);
            Assert.That(averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_UsesUnrealEmptyHistogramFallback()
        {
            var settings = CreateGoldenHistogramSettings();
            var histogram = new uint[AutoExposureReferenceSolver.HistogramBinCount];

            var resolved = AutoExposureReferenceSolver.TryResolveAverageSceneLuminance(
                histogram,
                settings.exposureLowPercent,
                settings.exposureHighPercent,
                settings.histogramScale,
                settings.histogramBias,
                out var averageSceneLuminance);

            Assert.That(resolved, Is.True);
            Assert.That(averageSceneLuminance, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesBasicWeightedLogLuminanceWithoutHistogramQuantization()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.mode = AutoExposureMode.Basic;
            var firstLogLuminance = -4f;
            var secondLogLuminance = 2f;
            var firstWeight = 3f;
            var secondWeight = 1f;
            var weightedScaledLogLuminance =
                (firstLogLuminance * settings.histogramScale + settings.histogramBias) * firstWeight
                + (secondLogLuminance * settings.histogramScale + settings.histogramBias) * secondWeight;
            var accumulator = new uint[AutoExposureReferenceSolver.HistogramBinCount];
            accumulator[0] = FloatToUIntBits(weightedScaledLogLuminance);
            accumulator[1] = FloatToUIntBits(firstWeight + secondWeight);

            var resolved = AutoExposureReferenceSolver.TryResolveBasicAverageSceneLuminance(
                accumulator,
                settings.histogramScale,
                settings.histogramBias,
                out var averageSceneLuminance);

            Assert.That(resolved, Is.True);
            Assert.That(
                averageSceneLuminance,
                Is.EqualTo(Mathf.Pow(2f, -2.5f)).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_UsesBasicZeroWeightFallbackAtHistogramMinimum()
        {
            var settings = CreateGoldenHistogramSettings();
            var accumulator = new uint[AutoExposureReferenceSolver.HistogramBinCount];

            var resolved = AutoExposureReferenceSolver.TryResolveBasicAverageSceneLuminance(
                accumulator,
                settings.histogramScale,
                settings.histogramBias,
                out var averageSceneLuminance);

            Assert.That(resolved, Is.True);
            Assert.That(
                averageSceneLuminance,
                Is.EqualTo(Mathf.Pow(2f, -10f)).Within(1e-6f));
        }

        [TestCase(0, 64)]
        [TestCase(1, 64)]
        [TestCase(1080, 69120)]
        [TestCase(2160, 138240)]
        public void ResolveUnrealPartialHistogramBufferCount_AllocatesOneSlicePerRow(
            int height,
            int expectedCount)
        {
            Assert.That(
                AutoExposurePass.ResolveUnrealPartialHistogramBufferCount(height),
                Is.EqualTo(expectedCount));
        }

        [Test]
        public void AutoExposurePass_DeclaresPersistentHistorySideEffect()
        {
            Assert.That(
                typeof(IRenderGraphSideEffectPass)
                    .IsAssignableFrom(typeof(AutoExposurePass)),
                Is.True);
        }

        [TestCase(true, 0.016f, 0.016f)]
        [TestCase(true, 0f, 0.000001f)]
        [TestCase(false, 0f, 0.033f)]
        [TestCase(false, 0.5f, 0.033f)]
        public void ResolveUnrealExposureDeltaTime_UsesStableEditorStep(
            bool isPlaying,
            float deltaTime,
            float expected)
        {
            Assert.That(
                AutoExposureSettingsResolver.ResolveUnrealExposureDeltaTime(
                    isPlaying,
                    deltaTime),
                Is.EqualTo(expected).Within(1e-7f));
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void UnrealHistory_ReuseRequiresCommittedStateAndAllocatedBuffer(
            bool stateHasValidHistory,
            bool hasAllocatedPreviousBuffer,
            bool expected)
        {
            Assert.That(
                UnrealAutoExposureHistoryUtility.HasUsableExposureState(
                    stateHasValidHistory,
                    hasAllocatedPreviousBuffer),
                Is.EqualTo(expected));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesGoldenFirstFrameExposureState()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 1f;

            var result = AutoExposureReferenceSolver.ResolveExposureState(
                CreateGoldenHistogram(),
                settings,
                CreateExposureState(1f));

            Assert.That(result.currentExposureScale, Is.EqualTo(0.6593355f).Within(1e-6f));
            Assert.That(result.targetExposureScale, Is.EqualTo(0.6593355f).Within(1e-6f));
            Assert.That(result.averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
            Assert.That(result.middleGreyCompensation, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ClampsTargetExposureToGoldenMaxAverageLuminance()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 1f;
            settings.maxAverageLuminance = 0.2f;

            var result = AutoExposureReferenceSolver.ResolveExposureState(
                CreateGoldenHistogram(),
                settings,
                CreateExposureState(1f));

            Assert.That(result.currentExposureScale, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(result.targetExposureScale, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(result.averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
            Assert.That(result.middleGreyCompensation, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesGoldenBrighteningLinearStep()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 0f;

            var result = AutoExposureReferenceSolver.ResolveExposureState(
                CreateGoldenHistogram(),
                settings,
                CreateExposureState(8f));

            Assert.That(result.currentExposureScale, Is.EqualTo(7.727491f).Within(1e-5f));
            Assert.That(result.targetExposureScale, Is.EqualTo(0.6593355f).Within(1e-6f));
            Assert.That(result.averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
            Assert.That(result.middleGreyCompensation, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesGoldenDarkeningExponentialStep()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 0f;

            var result = AutoExposureReferenceSolver.ResolveExposureState(
                CreateGoldenHistogram(),
                settings,
                CreateExposureState(0.25f));

            Assert.That(result.currentExposureScale, Is.EqualTo(0.25270838f).Within(1e-6f));
            Assert.That(result.targetExposureScale, Is.EqualTo(0.6593355f).Within(1e-6f));
            Assert.That(result.averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
            Assert.That(result.middleGreyCompensation, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureReferenceSolver_ResolvesGoldenHistogramCurveCompensation()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 1f;

            var curve = new AnimationCurve(
                new Keyframe(-16f, 1f),
                new Keyframe(16f, 1f));

            try
            {
                var curveTextureData = AutoExposureCompensationCurveUtility.Resolve(curve);
                settings.exposureCompensationCurveTexture = curveTextureData.texture;
                settings.exposureCompensationCurveMinEV100 = curveTextureData.minEV100;
                settings.exposureCompensationCurveInvRange = curveTextureData.invRange;
                settings.exposureCompensationCurveEnabled = curveTextureData.enabled;
                settings.unrealCompensationCurveHasHistory = true;

                var result = AutoExposureReferenceSolver.ResolveExposureState(
                    CreateGoldenHistogram(),
                    settings,
                    CreateExposureState(1f));

                Assert.That(result.currentExposureScale, Is.EqualTo(1.318671f).Within(1e-5f));
                Assert.That(result.targetExposureScale, Is.EqualTo(1.318671f).Within(1e-5f));
                Assert.That(result.averageSceneLuminance, Is.EqualTo(0.27300215f).Within(1e-6f));
                Assert.That(result.middleGreyCompensation, Is.EqualTo(2f).Within(1e-6f));
            }
            finally
            {
                AutoExposureCompensationCurveUtility.Dispose();
            }
        }

        [Test]
        public void AutoExposureReferenceSolver_SamplesCurveFromPreviousAverageSceneLuminance()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.forceTarget = 1f;
            var curve = AnimationCurve.Linear(-16f, 0f, 16f, 4f);

            try
            {
                var curveTextureData = AutoExposureCompensationCurveUtility.Resolve(curve);
                settings.exposureCompensationCurveTexture = curveTextureData.texture;
                settings.exposureCompensationCurveMinEV100 = curveTextureData.minEV100;
                settings.exposureCompensationCurveInvRange = curveTextureData.invRange;
                settings.exposureCompensationCurveEnabled = curveTextureData.enabled;
                settings.unrealCompensationCurveHasHistory = true;

                var previousAverageSceneLuminance =
                    AutoExposureSettingsResolver.ResolveAverageSceneLuminanceFromEV100(8f);
                var result = AutoExposureReferenceSolver.ResolveExposureState(
                    CreateGoldenHistogram(),
                    settings,
                    CreateExposureState(
                        1f,
                        0.25f,
                        previousAverageSceneLuminance));

                Assert.That(
                    result.middleGreyCompensation,
                    Is.EqualTo(8f).Within(0.05f));
            }
            finally
            {
                AutoExposureCompensationCurveUtility.Dispose();
            }
        }

        [Test]
        public void AutoExposureReferenceSolver_UsesCurrentCompensationToRecoverOldExposure()
        {
            var settings = CreateGoldenHistogramSettings();
            settings.exposureCompensationSettings = 2f;
            settings.forceTarget = 0f;

            var result = AutoExposureReferenceSolver.ResolveExposureState(
                CreateGoldenHistogram(),
                settings,
                CreateExposureState(1f, 0.25f));

            Assert.That(result.currentExposureScale, Is.GreaterThan(1f));
            Assert.That(result.currentExposureScale, Is.LessThan(1.1f));
            Assert.That(result.middleGreyCompensation, Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void AutoExposureCurveMapUtility_BakesCurveRemapTextureAndClampRange()
        {
            var curve = AnimationCurve.Linear(-4f, -1f, 4f, 2f);

            try
            {
                var textureData = AutoExposureCurveMapUtility.Resolve(curve, -2f, 3f);
                var texture = textureData.texture as Texture2D;

                Assert.That(texture, Is.Not.Null);
                Assert.That(textureData.minEV100, Is.EqualTo(-4f).Within(1e-5f));
                Assert.That(textureData.maxEV100, Is.EqualTo(4f).Within(1e-5f));

                var sample = texture.GetPixelBilinear(0.5f, 0.5f);
                Assert.That(sample.r, Is.EqualTo(0.5f).Within(0.05f));
                Assert.That(sample.g, Is.EqualTo(-2f).Within(1e-5f));
                Assert.That(sample.b, Is.EqualTo(3f).Within(1e-5f));
            }
            finally
            {
                AutoExposureCurveMapUtility.Dispose();
            }
        }

        [Test]
        public void AutoExposureCurveMapUtility_UsesIdentityFallback_WhenCurveIsMissing()
        {
            try
            {
                var textureData = AutoExposureCurveMapUtility.Resolve(null, -5f, 1f);
                var texture = textureData.texture as Texture2D;

                Assert.That(texture, Is.Not.Null);
                Assert.That(textureData.minEV100, Is.EqualTo(AutoExposureCurveMapUtility.DefaultCurveMinEV100).Within(1e-5f));
                Assert.That(textureData.maxEV100, Is.EqualTo(AutoExposureCurveMapUtility.DefaultCurveMaxEV100).Within(1e-5f));

                var sample = texture.GetPixelBilinear(0.5f, 0.5f);
                Assert.That(sample.r, Is.EqualTo(0f).Within(0.05f));
                Assert.That(sample.g, Is.EqualTo(-5f).Within(1e-5f));
                Assert.That(sample.b, Is.EqualTo(1f).Within(1e-5f));
            }
            finally
            {
                AutoExposureCurveMapUtility.Dispose();
            }
        }

        [Test]
        public void AutoExposureShader_DeclaresHistogramAndExposureKernels()
        {
            var autoExposureCommonSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposure.cs"));
            var autoExposureHDRPSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposure.HDRP.cs"));
            var autoExposureUnrealSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposure.Unreal.cs"));
            var autoExposureSource =
                autoExposureCommonSource + autoExposureHDRPSource + autoExposureUnrealSource;
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "Unreal", "AutoExposure.compute"));
            var hdrpShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "HDRP", "Exposure.compute"));
            var hdrpCommonSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "HDRP", "ExposureCommon.hlsl"));
            var hdrpPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposurePass.HDRP.cs"));
            var unrealPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposurePass.Unreal.cs"));
            var normalizedHdrpPassSource = hdrpPassSource.Replace("\r\n", "\n");
            var autoExposurePassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposurePass.cs"));
            var normalizedAutoExposurePassSource = autoExposurePassSource.Replace("\r\n", "\n");
            var helperSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "AutoExposure.hlsl"));
            var runtimeSource =
                File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.cs"))
                + File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.HDRP.cs"))
                + File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.Unreal.cs"));
            var implementationSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureImplementationUtility.cs"));
            var assetSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipelineAsset.cs"));
            var resourcesSource = File.ReadAllText(GetPackageFilePath("Runtime", "Utility", "PipelineResource", "VividResources.cs"));

            Assert.That(autoExposureSource, Does.Contain("public AutoExposureExposureModeParameter exposureMode"));
            Assert.That(autoExposureSource, Does.Contain("public AutoExposureMeteringModeParameter meteringMode"));
            Assert.That(autoExposureSource, Does.Contain("AutoExposureMeteringMode.ProceduralMask"));
            Assert.That(autoExposureSource, Does.Contain("public AutoExposureAdaptationModeParameter adaptationMode"));
            Assert.That(autoExposureSource, Does.Contain("public EnumParameter<TargetMidGray> targetMidGray"));
            Assert.That(autoExposureSource, Does.Contain("public NoInterpAnimationCurveParameter curveMap"));
            Assert.That(autoExposureSource, Does.Contain("public NoInterpAnimationCurveParameter exposureCompensationCurve"));
            Assert.That(
                autoExposureCommonSource,
                Does.Not.Contain("public AutoExposureModeParameter mode"));
            Assert.That(
                autoExposureCommonSource,
                Does.Not.Contain("public AutoExposureExposureModeParameter exposureMode"));
            Assert.That(
                autoExposureHDRPSource,
                Does.Not.Contain("public AutoExposureModeParameter mode"));
            Assert.That(
                autoExposureUnrealSource,
                Does.Not.Contain("public AutoExposureExposureModeParameter exposureMode"));
            Assert.That(autoExposureUnrealSource, Does.Contain("Basic = 2"));
            Assert.That(
                autoExposureUnrealSource,
                Does.Contain("public Texture2DParameter exposureMeteringMask"));
            Assert.That(
                autoExposureHDRPSource,
                Does.Not.Contain("exposureMeteringMask"));
            Assert.That(shaderSource, Does.Not.Contain("#pragma kernel ClearHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel BuildHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ResolveExposure"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ResolveBasicExposure"));
            Assert.That(shaderSource, Does.Contain("#define HISTOGRAM_THREAD_GROUP_SIZE 64"));
            Assert.That(shaderSource, Does.Contain("groupshared uint s_GroupHistogram[HISTOGRAM_SIZE];"));
            Assert.That(shaderSource, Does.Contain("groupshared uint s_ResolvedHistogram[HISTOGRAM_SIZE];"));
            Assert.That(shaderSource, Does.Contain("groupshared float2 s_GroupBasicLuminance[HISTOGRAM_THREAD_GROUP_SIZE];"));
            Assert.That(shaderSource, Does.Contain("[numthreads(HISTOGRAM_THREAD_GROUP_SIZE, 1, 1)]"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KHistogramClear"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KHistogramGen"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KHistogramReduce"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KPrePass"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KReduction"));
            Assert.That(hdrpShaderSource, Does.Contain("#pragma kernel KReset"));
            Assert.That(hdrpShaderSource, Does.Contain("case 2u:"));
            Assert.That(hdrpShaderSource, Does.Contain("CurveRemap(avgLuminance, minExposure, maxExposure);"));
            Assert.That(hdrpShaderSource, Does.Contain("void KHistogramGen("));
            Assert.That(hdrpShaderSource, Does.Contain("void KHistogramReduce("));
            Assert.That(hdrpShaderSource, Does.Contain("#define HISTOGRAM_BINS 128"));
            Assert.That(hdrpShaderSource, Does.Contain("[numthreads(16,8,1)]"));
            Assert.That(hdrpShaderSource, Does.Contain("uint2 fullResCoords = dispatchThreadId << 1u;"));
            Assert.That(hdrpShaderSource, Does.Contain("[numthreads(HISTOGRAM_BINS,1,1)]"));
            Assert.That(hdrpShaderSource, Does.Contain("if (dispatchThreadId.x != 0u)"));
            Assert.That(hdrpShaderSource, Does.Contain("uint bucket = GetHistogramBinLocation(luma);"));
            Assert.That(hdrpShaderSource, Does.Contain("float binEV100 = BinLocationToEV(reduceBucketIndex);"));
            Assert.That(hdrpShaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(hdrpShaderSource, Does.Contain("WriteExposureBuffer("));
            Assert.That(hdrpCommonSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(hdrpCommonSource, Does.Contain("float4 _HistogramRangeParams;"));
            Assert.That(hdrpCommonSource, Does.Contain("float GetFractionWithinHistogram(float averageLuminance)"));
            Assert.That(hdrpCommonSource, Does.Contain("float BinLocationToEV(uint binIdx)"));
            Assert.That(hdrpCommonSource, Does.Contain("float CurveRemap(float inEV, out float limitMin, out float limitMax)"));
            Assert.That(hdrpCommonSource, Does.Contain("float3 curveSample = SAMPLE_TEXTURE2D_LOD(_ExposureCurveTexture"));
            Assert.That(hdrpCommonSource, Does.Contain("case 4u:"));
            Assert.That(hdrpCommonSource, Does.Contain("ProceduralCenter"));
            Assert.That(hdrpCommonSource, Does.Contain("ProceduralSoftness"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("ColorUtils.lensImperfectionExposureScale"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("ResolveHDRPHistogramScaleBias(minExposureEV100, maxExposureEV100);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("private static readonly uint[] s_EmptyHdrpHistogramData = new uint[HdrpAutoExposureHistogramBucketCount];"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("cmd.SetBufferData(m_AutoExposureHistogramBuffer, s_EmptyHdrpHistogramData);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("private readonly int[] m_HdrpVariants = new int[4];"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("CoreUtils.DivRoundUp(Mathf.Max(1, m_AutoExposureWidth / 2), HdrpHistogramThreadGroupSizeX)"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("CoreUtils.DivRoundUp(Mathf.Max(1, m_AutoExposureHeight / 2), HdrpHistogramThreadGroupSizeY)"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("var evaluateMode = UsesHDRPCurveRemapping()\n                ? 2u\n                : 0u;"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("BindHDRPHistogramGenerationParameters(cmd);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("BindHDRPHistogramReductionParameters(cmd, evaluateMode);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("case AutoExposureMeteringMode.ProceduralMask:"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("ResolveHDRPProceduralMeteringParameters("));
            Assert.That(normalizedHdrpPassSource, Does.Contain("private void SetHDRPVariants("));
            Assert.That(normalizedHdrpPassSource, Does.Contain("cmd.SetComputeIntParams(m_AutoExposureCompute, HdrpVariantsId, m_HdrpVariants);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("cmd.SetGlobalTexture(HdrpPreviousExposureTextureId, currentExposureTexture);"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("new Vector4(\n                    histogramScaleBias.x,\n                    histogramScaleBias.y,\n                    m_AutoExposureSettings.exposureLowPercent,\n                    m_AutoExposureSettings.exposureHighPercent));"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("new Vector4(\n                    m_AutoExposureSettings.exposureSpeedDown,\n                    m_AutoExposureSettings.exposureSpeedUp,\n                    0f,\n                    0f));"));
            Assert.That(normalizedHdrpPassSource, Does.Contain("cmd.SetComputeIntParams("));
            Assert.That(normalizedHdrpPassSource, Does.Not.Contain("HdrpVariantsId,\n                1,\n                meteringMode"));
            Assert.That(autoExposurePassSource, Does.Not.Contain("&& m_HdrpHistogramClearKernel >= 0"));
            Assert.That(autoExposurePassSource, Does.Contain("private const int UnrealAutoExposureHistogramBucketCount = 64;"));
            Assert.That(autoExposurePassSource, Does.Contain("private const int HdrpAutoExposureHistogramBucketCount = 128;"));
            Assert.That(autoExposurePassSource, Does.Contain("private const int HdrpHistogramThreadGroupSizeX = 16;"));
            Assert.That(autoExposurePassSource, Does.Contain("private const int HdrpHistogramThreadGroupSizeY = 8;"));
            Assert.That(autoExposurePassSource, Does.Contain("&& m_AutoExposureSettings.mode == AutoExposureMode.Histogram"));
            Assert.That(
                autoExposurePassSource,
                Does.Contain("IRenderGraphSideEffectPass"));
            Assert.That(
                autoExposurePassSource,
                Does.Contain("RegisterExposureBufferAccess();"));
            Assert.That(
                autoExposurePassSource,
                Does.Contain("m_ExposureData.frameExposureBuffer ="));
            Assert.That(
                autoExposurePassSource,
                Does.Contain("PassRecorder.ImportBufferForPass("));
            Assert.That(
                autoExposurePassSource,
                Does.Contain("AccessFlags.Write"));
            Assert.That(normalizedAutoExposurePassSource, Does.Contain("return m_AutoExposureImplementation == AutoExposureImplementationPath.HDRP\n                ? HdrpAutoExposureHistogramBucketCount\n                : UnrealAutoExposureHistogramBucketCount;"));
            Assert.That(unrealPassSource, Does.Contain("Mathf.Max(1, m_AutoExposureHeight)"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<uint> _PartialHistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("Texture2D<float4> _AutoExposureCompensationCurve;"));
            Assert.That(shaderSource, Does.Contain("static const float3 kLuminanceWeights = float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);"));
            Assert.That(shaderSource, Does.Contain("static const float kLuminanceMax = 1.0f;"));
            Assert.That(shaderSource, Does.Contain("averageSceneLuminance / (kMiddleGrey * kLuminanceMax)"));
            Assert.That(shaderSource, Does.Contain("const float preExposure = max(_PreviousExposureBuffer[0].x, kEpsilon);"));
            Assert.That(shaderSource, Does.Contain("_InputColor.Load(int3(pixelCoord, 0)).rgb / preExposure"));
            Assert.That(shaderSource, Does.Contain("_AutoExposureMeterMask.SampleLevel(sampler_LinearClamp"));
            Assert.That(shaderSource, Does.Not.Contain("sampler_AutoExposureMeterMask"));
            Assert.That(shaderSource, Does.Contain("const uint lowerBucket"));
            Assert.That(shaderSource, Does.Contain("const uint upperBucket"));
            Assert.That(shaderSource, Does.Contain("InterlockedAdd(s_GroupHistogram[lowerBucket], lowerWeight);"));
            Assert.That(shaderSource, Does.Contain("InterlockedAdd(s_GroupHistogram[upperBucket], upperWeight);"));
            Assert.That(shaderSource, Does.Not.Contain("InterlockedAdd(_HistogramBuffer"));
            Assert.That(shaderSource, Does.Not.Contain("InterlockedCompareExchange("));
            Assert.That(shaderSource, Does.Contain("_PartialHistogramBuffer[rowIndex * HISTOGRAM_SIZE + laneIndex]"));
            Assert.That(shaderSource, Does.Contain("_HistogramBuffer[laneIndex] = bucketWeight;"));
            Assert.That(shaderSource, Does.Contain("_UnrealAutoExposureMethodParams.y"));
            Assert.That(shaderSource, Does.Contain("void ResolveBasicExposure("));
            Assert.That(shaderSource, Does.Contain("const float previousAverageSceneLuminance ="));
            Assert.That(shaderSource, Does.Contain("middleGreyExposureCompensation"));
            Assert.That(shaderSource, Does.Contain("/ max(oldExposureScale, kEpsilon);"));
            Assert.That(shaderSource, Does.Not.Contain("_PreviousExposureBuffer[0].w"));
            Assert.That(helperSource, Does.Contain("StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;"));
            Assert.That(helperSource, Does.Contain("float3 VividApplyPreExposure(float3 color)"));
            Assert.That(runtimeSource, Does.Contain("settings.mode == AutoExposureMode.Manual"));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpExposureMode = autoExposure.ResolveExposureMode();"));
            Assert.That(runtimeSource, Does.Contain("settings.mode = AutoExposureExposureModeUtility.ResolveRuntimeMode("));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpMeteringMode = autoExposure.meteringMode.value;"));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpAdaptationMode = autoExposure.adaptationMode.value;"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureExposureModeUtility.UsesPhysicalCamera(settings.hdrpExposureMode)"));
            Assert.That(runtimeSource, Does.Contain("settings.unrealExposureMeteringMask = autoExposure.exposureMeteringMask.value;"));
            Assert.That(runtimeSource, Does.Contain("settings.unrealBlackHistogramBucketInfluence = 0f;"));
            Assert.That(runtimeSource, Does.Contain("settings.unrealCompensationCurveHasHistory = !isFirstFrame;"));
            Assert.That(runtimeSource, Does.Contain("settings.mode == AutoExposureMode.Basic"));
            Assert.That(runtimeSource, Does.Contain("settings.targetMidGray = ResolveHDRPTargetMidGray("));
            Assert.That(runtimeSource, Does.Contain("settings.manualEV100 = ResolveManualEV100("));
            Assert.That(runtimeSource, Does.Contain("settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(settings, camera);"));
            Assert.That(runtimeSource, Does.Contain("var histogramLogRangeValue = autoExposure.histogramLogRange.value;"));
            Assert.That(runtimeSource, Does.Contain("autoExposure.minEV100.value"));
            Assert.That(runtimeSource, Does.Contain("autoExposure.maxEV100.value"));
            Assert.That(runtimeSource, Does.Contain("ResolveHistogramLogRangeFromEV100("));
            Assert.That(runtimeSource, Does.Contain("BuildHistogramScaleBiasFromEV100("));
            Assert.That(runtimeSource, Does.Contain("ColorUtils.ComputeEV100(aperture, shutterSpeed, iso)"));
            Assert.That(runtimeSource, Does.Contain("autoExposure.exposureCompensation.value"));
            Assert.That(runtimeSource, Does.Contain("ResolveExposureCompensationCurveStops("));
            Assert.That(runtimeSource, Does.Contain("ResolveExposureCompensationAll("));
            Assert.That(runtimeSource, Does.Contain("ResolveManualExposureScale("));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCompensationCurveUtility.Resolve("));
            Assert.That(runtimeSource, Does.Contain("public Texture hdrpCurveMapTexture;"));
            Assert.That(runtimeSource, Does.Contain("public float hdrpCurveMapMinEV100;"));
            Assert.That(runtimeSource, Does.Contain("public float hdrpCurveMapMaxEV100;"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCurveMapUtility.Resolve("));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpCurveMapTexture = curveMapTextureData.texture;"));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpCurveMapMinEV100 = curveMapTextureData.minEV100;"));
            Assert.That(runtimeSource, Does.Contain("settings.hdrpCurveMapMaxEV100 = curveMapTextureData.maxEV100;"));
            Assert.That(runtimeSource, Does.Contain("internal readonly struct AutoExposureCurveMapTextureData"));
            Assert.That(runtimeSource, Does.Contain("internal static class AutoExposureCurveMapUtility"));
            Assert.That(runtimeSource, Does.Contain("new Color(remappedEV100, resolvedClampMinEV100, resolvedClampMaxEV100, 0f)"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCurveMapUtility.Dispose();"));
            Assert.That(runtimeSource, Does.Contain("ResolveAverageSceneLuminanceFromEV100("));
            Assert.That(runtimeSource, Does.Contain("internal static AutoExposureSettingsData ResolvePhysicalCameraFallback"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureImplementationUtility.ResolveComputeShader("));
            Assert.That(runtimeSource, Does.Contain("VividRenderPipelineAsset.GetActiveAsset()"));
            Assert.That(runtimeSource, Does.Contain("var preExposureBuffer = exposureEnabled"));
            Assert.That(runtimeSource, Does.Contain("public AutoExposureImplementationPath implementation;"));
            Assert.That(implementationSource, Does.Contain("AutoExposureImplementationPath.HDRP"));
            Assert.That(implementationSource, Does.Contain("SupportsDispatch("));
            Assert.That(implementationSource, Does.Contain("SupportsUnrealDispatch"));
            Assert.That(implementationSource, Does.Contain("SupportsHdrpDispatch"));
            Assert.That(implementationSource, Does.Contain("SupportsHdrpPrePassDispatch"));
            Assert.That(implementationSource, Does.Contain("SupportsHdrpHistogramDispatch"));
            Assert.That(implementationSource, Does.Contain("ResolveHistogramDebugCompute"));
            Assert.That(implementationSource, Does.Not.Contain("Falling back to the Unreal auto exposure compute."));
            Assert.That(assetSource, Does.Contain("public enum AutoExposureImplementationPath"));
            Assert.That(assetSource, Does.Contain("m_AutoExposureImplementation = AutoExposureImplementationPath.Unreal"));
            Assert.That(resourcesSource, Does.Contain("public ComputeShader AutoExposureHDRPCompute;"));
            Assert.That(resourcesSource, Does.Contain("ResolveAutoExposureCompute(VividRenderPipelineAsset pipelineAsset)"));
        }

        [Test]
        public void FrameContextClear_KeepsAutoExposureCallbacksRegistered_InEditor()
        {
            VividAutoExposureSystem.Initialize();

            Assert.That(
                HasFrameContextSubscriber(
                    "SubsystemDispose",
                    typeof(VividAutoExposureSystem),
                    "OnSubsystemDispose"),
                Is.True);

            FrameContextSystem.Clear();

            Assert.That(
                HasFrameContextSubscriber(
                    "SubsystemPreRender",
                    typeof(VividSubsystem<VividAutoExposureSystem>),
                    "DispatchUpdate"),
                Is.True);
            Assert.That(
                HasFrameContextSubscriber(
                    "SubsystemDispose",
                    typeof(VividAutoExposureSystem),
                    "OnSubsystemDispose"),
                Is.True);
        }

        [Test]
        public void AutoExposureEditorOnlyGpuReadbackPath_WiresRuntimeAndEditorStatsMonitor()
        {
            var autoExposurePassSource = File.ReadAllText(
                GetPackageFilePath(
                    "Runtime",
                    "RenderPass",
                    "Core",
                    "PostProcessing",
                    "AutoExposure",
                    "AutoExposurePass.cs"));
            var autoExposureUnrealPassSource = File.ReadAllText(
                GetPackageFilePath(
                    "Runtime",
                    "RenderPass",
                    "Core",
                    "PostProcessing",
                    "AutoExposure",
                    "AutoExposurePass.Unreal.cs"));
            var autoExposureHDRPPassSource = File.ReadAllText(
                GetPackageFilePath(
                    "Runtime",
                    "RenderPass",
                    "Core",
                    "PostProcessing",
                    "AutoExposure",
                    "AutoExposurePass.HDRP.cs"));
            var readbackBridgeSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureStatsReadbackBridge.cs"));
            var editorSource = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "AutoExposureEditor.cs"));
            var statsShaderSource = File.ReadAllText(GetPackageFilePath("Editor", "Shader", "AutoExposureStats.shader"));

            Assert.That(autoExposurePassSource, Does.Contain("AutoExposureStatsReadbackBridge.Request("));
            Assert.That(autoExposurePassSource, Does.Contain("UsesHistogramBufferAutoExposureExecution()"));
            Assert.That(autoExposureHDRPPassSource, Does.Contain("ExecuteHDRPHistogramAutoExposure("));
            Assert.That(autoExposurePassSource, Does.Contain("m_HistogramAutoExposureCompute"));
            Assert.That(autoExposureUnrealPassSource, Does.Not.Contain("m_ClearHistogramKernel"));
            Assert.That(autoExposureUnrealPassSource, Does.Contain("AutoExposurePartialHistogramBufferId"));
            Assert.That(autoExposureUnrealPassSource, Does.Contain("m_UnrealPartialHistogramBuffer"));
            Assert.That(autoExposureUnrealPassSource, Does.Contain("InsertUnrealHistogramBuildToResolveFence(cmd);"));
            Assert.That(readbackBridgeSource, Does.Contain("Dictionary<Camera, SnapshotState>"));
            Assert.That(readbackBridgeSource, Does.Contain("public readonly Action<AsyncGPUReadbackRequest> exposureReadback;"));
            Assert.That(readbackBridgeSource, Does.Contain("internal static void TouchInspectorRequest()"));
            Assert.That(readbackBridgeSource, Does.Contain("internal static bool TryGetLatestSnapshot(out AutoExposureStatsReadbackSnapshot snapshot)"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(exposureBuffer, state.exposureReadback);"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(preExposureBuffer, state.preExposureReadback);"));
            Assert.That(readbackBridgeSource, Does.Contain("HandlePreExposureReadback"));
            Assert.That(readbackBridgeSource, Does.Contain("public readonly Vector4 preExposureState;"));
            Assert.That(readbackBridgeSource, Does.Contain("public readonly bool hasPreExposureState;"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(histogramBuffer, state.histogramReadback);"));
            Assert.That(readbackBridgeSource, Does.Contain("cameraName = camera != null ? camera.name : string.Empty;"));
            Assert.That(readbackBridgeSource, Does.Not.Contain("request => HandleExposureReadback"));
            Assert.That(readbackBridgeSource, Does.Not.Contain("state.cameraName = camera.name;"));
            Assert.That(autoExposurePassSource, Does.Contain("VividAutoExposureSystem.ResolvePreExposureBuffer(m_ExposureData)"));
            Assert.That(editorSource, Does.Contain("AutoExposureStatsReadbackBridge.TouchInspectorRequest();"));
            Assert.That(editorSource, Does.Contain("return BuildLiveStatsPreviewData(snapshot);"));
            Assert.That(editorSource, Does.Contain("snapshot.hasPreExposureState"));
            Assert.That(editorSource, Does.Contain("\"Pre Buffer.x\""));
            Assert.That(editorSource, Does.Contain("resolvedPreExposure"));
            Assert.That(editorSource, Does.Contain("ResolveExposureEV100FromScale"));
            Assert.That(editorSource, Does.Contain("ResolveHistogramPercentileBins"));
            Assert.That(editorSource, Does.Contain("HistogramLabelRangeId"));
            Assert.That(editorSource, Does.Contain("HistogramExposureValuesId"));
            Assert.That(editorSource, Does.Contain("HistogramPercentileBinsId"));
            Assert.That(editorSource, Does.Contain("SetFloatArray(HistogramSamplesId, m_HistogramPreviewSamples);"));
            Assert.That(editorSource, Does.Contain("Live GPU ("));
            Assert.That(editorSource, Does.Contain("Waiting for editor-only GPU readback."));
            Assert.That(statsShaderSource, Does.Contain("float _HistogramSamples[64];"));
            Assert.That(statsShaderSource, Does.Contain("ExposureStatsSummary SummarizeExposureStats()"));
            Assert.That(statsShaderSource, Does.Contain("GetHistogramLabelRange("));
            Assert.That(statsShaderSource, Does.Contain("GetHistogramInfo("));
            Assert.That(statsShaderSource, Does.Contain("DrawHistogramFrame("));
            Assert.That(statsShaderSource, Does.Contain("DrawMiniCharacterFlippedY("));
            Assert.That(statsShaderSource, Does.Contain("DrawMiniFloatExplicitPrecisionFlippedY("));
            Assert.That(statsShaderSource, Does.Contain("DrawLiteralCurrentExposure("));
            Assert.That(statsShaderSource, Does.Contain("DrawLiteralTargetExposure("));
            Assert.That(statsShaderSource, Does.Contain("DrawLiteralExposureCompensation("));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }

        private static bool HasFrameContextSubscriber(string eventName, global::System.Type declaringType, string methodName)
        {
            FieldInfo eventField = typeof(FrameContextSystem).GetField(
                eventName,
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(eventField, Is.Not.Null);

            var multicastDelegate = eventField.GetValue(null) as global::System.MulticastDelegate;
            return multicastDelegate != null
                && multicastDelegate.GetInvocationList().Any(
                    callback => callback.Method.DeclaringType == declaringType
                        && callback.Method.Name == methodName);
        }

        private static AutoExposureSettingsData CreateGoldenHistogramSettings()
        {
            var settings = AutoExposureSettingsData.CreateDefault();
            var histogramScaleBias = AutoExposureSettingsResolver.BuildHistogramScaleBiasFromEV100(-10f, 6f);
            var histogramLogRange = AutoExposureSettingsResolver.ResolveHistogramLogRangeFromEV100(-10f, 6f);

            settings.enabled = true;
            settings.mode = AutoExposureMode.Histogram;
            settings.exposureLowPercent = 0.10f;
            settings.exposureHighPercent = 0.90f;
            settings.minAverageLuminance = 0.01f;
            settings.maxAverageLuminance = 10f;
            settings.exposureCompensationSettings = 1f;
            settings.exposureCompensationCurveStops = 0f;
            settings.exposureCompensationAll = 1f;
            settings.deltaTime = 1f / 60f;
            settings.exposureSpeedUp = 3f;
            settings.exposureSpeedDown = 1f;
            settings.histogramScale = histogramScaleBias.x;
            settings.histogramBias = histogramScaleBias.y;
            settings.luminanceMin = Mathf.Pow(2f, histogramLogRange.x);
            settings.exponentialUpM = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedUp,
                AutoExposureSettingsResolver.DefaultStartDistance);
            settings.exponentialDownM = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(
                settings.exposureSpeedDown,
                AutoExposureSettingsResolver.DefaultStartDistance);
            settings.startDistance = AutoExposureSettingsResolver.DefaultStartDistance;
            settings.forceTarget = 0f;
            settings.exposureCompensationCurveTexture = Texture2D.blackTexture;
            settings.exposureCompensationCurveMinEV100 = AutoExposureCompensationCurveUtility.DefaultCurveMinEV100;
            settings.exposureCompensationCurveInvRange = 1f / AutoExposureCompensationCurveUtility.DefaultCurveRange;
            settings.exposureCompensationCurveEnabled = false;
            return settings;
        }

        private static uint[] CreateGoldenHistogram()
        {
            var histogram = new uint[AutoExposureReferenceSolver.HistogramBinCount];
            histogram[8] = 10;
            histogram[24] = 40;
            histogram[40] = 40;
            histogram[56] = 10;
            return histogram;
        }

        private static Vector4 CreateExposureState(
            float exposureScale,
            float middleGreyCompensation = 1f,
            float averageSceneLuminance = AutoExposureSettingsResolver.MiddleGrey)
        {
            return new Vector4(
                exposureScale,
                exposureScale,
                averageSceneLuminance,
                middleGreyCompensation);
        }

        private static uint FloatToUIntBits(float value)
        {
            return unchecked((uint)global::System.BitConverter.SingleToInt32Bits(value));
        }

        private static void SetMemberValue(object instance, string memberName, object value)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();
            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            var property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                property.SetValue(instance, value);
                return;
            }

            Assert.Fail($"Member '{memberName}' was not found on '{type.FullName}'.");
        }
    }
}
