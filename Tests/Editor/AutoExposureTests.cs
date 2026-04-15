using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class AutoExposureTests
    {
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

            Assert.That(autoExposure.IsActive(), Is.True);
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

            Assert.That(autoExposure.IsActive(), Is.True);
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

            Assert.That(autoExposure.IsActive(), Is.True);
        }

        [Test]
        public void ResolveExposureMode_UsesLegacyManualFields_WhenHdrpModeIsStillDefault()
        {
            var autoExposure = new AutoExposure();
            autoExposure.mode.value = AutoExposureMode.Manual;
            autoExposure.applyPhysicalCameraExposure.value = true;
            autoExposure.exposureMode.overrideState = false;
            autoExposure.exposureMode.value = AutoExposureExposureMode.Automatic;

            Assert.That(autoExposure.ResolveExposureMode(), Is.EqualTo(AutoExposureExposureMode.UsePhysicalCamera));
        }

        [Test]
        public void AutoExposure_IsActive_WhenFixedAdaptationIgnoresSpeedParameters()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.adaptationMode.value = AutoExposureAdaptationMode.Fixed;
            autoExposure.speedUp.value = 0f;
            autoExposure.speedDown.value = 0f;

            Assert.That(autoExposure.IsActive(), Is.True);
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
        [TestCase(nameof(VividRPCoreResources.AutoExposureCompute), "Shaders/Core/Private/AutoExposure/Unreal/AutoExposure.compute")]
        [TestCase(nameof(VividRPCoreResources.AutoExposureHDRPCompute), "Shaders/Core/Private/AutoExposure/HDRP/Exposure.compute")]
        public void VividRPCoreResources_DeclaresAutoExposureComputePaths(string fieldName, string expectedPath)
        {
            var field = typeof(VividRPCoreResources).GetField(fieldName);

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo(expectedPath));
        }

        [Test]
        public void AutoExposureShader_DeclaresHistogramAndExposureKernels()
        {
            var autoExposureSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposure.cs"));
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "Unreal", "AutoExposure.compute"));
            var hdrpShaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "HDRP", "Exposure.compute"));
            var hdrpCommonSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure", "HDRP", "ExposureCommon.hlsl"));
            var hdrpPassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposurePass.HDRP.cs"));
            var helperSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "AutoExposure.hlsl"));
            var runtimeSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.cs"));
            var implementationSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureImplementationUtility.cs"));
            var assetSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipelineAsset.cs"));
            var resourcesSource = File.ReadAllText(GetPackageFilePath("Runtime", "Utility", "PipelineResource", "VividResources.cs"));

            Assert.That(autoExposureSource, Does.Contain("public AutoExposureExposureModeParameter exposureMode"));
            Assert.That(autoExposureSource, Does.Contain("public AutoExposureMeteringModeParameter meteringMode"));
            Assert.That(autoExposureSource, Does.Contain("public AutoExposureAdaptationModeParameter adaptationMode"));
            Assert.That(autoExposureSource, Does.Contain("public EnumParameter<TargetMidGray> targetMidGray"));
            Assert.That(autoExposureSource, Does.Contain("public NoInterpAnimationCurveParameter curveMap"));
            Assert.That(autoExposureSource, Does.Contain("public NoInterpAnimationCurveParameter exposureCompensationCurve"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ClearHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel BuildHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ResolveExposure"));
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
            Assert.That(hdrpShaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(hdrpShaderSource, Does.Contain("WriteExposureBuffer("));
            Assert.That(hdrpCommonSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(hdrpCommonSource, Does.Contain("float4 _HistogramRangeParams;"));
            Assert.That(hdrpCommonSource, Does.Contain("float CurveRemap(float inEV, out float limitMin, out float limitMax)"));
            Assert.That(hdrpCommonSource, Does.Contain("float3 curveSample = SAMPLE_TEXTURE2D_LOD(_ExposureCurveTexture"));
            Assert.That(hdrpPassSource, Does.Contain("ColorUtils.lensImperfectionExposureScale"));
            Assert.That(hdrpPassSource, Does.Contain("cmd.SetComputeIntParams("));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("Texture2D<float4> _AutoExposureCompensationCurve;"));
            Assert.That(shaderSource, Does.Contain("static const float3 kLuminanceWeights = float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0);"));
            Assert.That(shaderSource, Does.Contain("const float preExposure = max(_PreviousExposureBuffer[0].x, kEpsilon);"));
            Assert.That(shaderSource, Does.Contain("_InputColor.Load(int3(dispatchThreadId.xy, 0)).rgb / preExposure"));
            Assert.That(shaderSource, Does.Contain("const float oldMiddleGreyExposureCompensation = max(_PreviousExposureBuffer[0].w, kEpsilon);"));
            Assert.That(helperSource, Does.Contain("StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;"));
            Assert.That(helperSource, Does.Contain("float3 VividApplyPreExposure(float3 color)"));
            Assert.That(runtimeSource, Does.Contain("settings.mode == AutoExposureMode.Manual"));
            Assert.That(runtimeSource, Does.Contain("settings.exposureMode = autoExposure.ResolveExposureMode();"));
            Assert.That(runtimeSource, Does.Contain("settings.mode = AutoExposureExposureModeUtility.ResolveRuntimeMode(settings.exposureMode);"));
            Assert.That(runtimeSource, Does.Contain("settings.meteringMode = autoExposure.meteringMode.value;"));
            Assert.That(runtimeSource, Does.Contain("settings.adaptationMode = autoExposure.adaptationMode.value;"));
            Assert.That(runtimeSource, Does.Contain("settings.applyPhysicalCameraExposure = AutoExposureExposureModeUtility.UsesPhysicalCamera(settings.exposureMode);"));
            Assert.That(runtimeSource, Does.Contain("settings.targetMidGray = ColorUtils.s_LightMeterCalibrationConstant;"));
            Assert.That(runtimeSource, Does.Contain("settings.manualEV100 = ResolveManualEV100("));
            Assert.That(runtimeSource, Does.Contain("settings = AutoExposureSettingsResolver.ResolvePhysicalCameraFallback(settings, camera);"));
            Assert.That(runtimeSource, Does.Contain("var histogramLogRangeValue = autoExposure.histogramLogRange.value;"));
            Assert.That(runtimeSource, Does.Contain("ResolveWhitePointLuminanceFromEV100(autoExposure.minEV100.value)"));
            Assert.That(runtimeSource, Does.Contain("ResolveWhitePointLuminanceFromEV100(autoExposure.maxEV100.value)"));
            Assert.That(runtimeSource, Does.Contain("ResolveHistogramLogRangeFromEV100("));
            Assert.That(runtimeSource, Does.Contain("BuildHistogramScaleBiasFromEV100("));
            Assert.That(runtimeSource, Does.Contain("ColorUtils.ComputeEV100(aperture, shutterSpeed, iso)"));
            Assert.That(runtimeSource, Does.Contain("settings.exposureCompensationSettings = ResolveExposureCompensation(autoExposure.exposureCompensation.value);"));
            Assert.That(runtimeSource, Does.Contain("ResolveExposureCompensationCurveStops("));
            Assert.That(runtimeSource, Does.Contain("settings.exposureCompensationAll = ResolveExposureCompensationAll("));
            Assert.That(runtimeSource, Does.Contain("ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensationAll)"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCompensationCurveUtility.Resolve(autoExposure.exposureCompensationCurve.value)"));
            Assert.That(runtimeSource, Does.Contain("public Texture curveMapTexture;"));
            Assert.That(runtimeSource, Does.Contain("public float curveMapMinEV100;"));
            Assert.That(runtimeSource, Does.Contain("public float curveMapMaxEV100;"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCurveMapUtility.Resolve("));
            Assert.That(runtimeSource, Does.Contain("settings.curveMapTexture = curveMapTextureData.texture;"));
            Assert.That(runtimeSource, Does.Contain("settings.curveMapMinEV100 = curveMapTextureData.minEV100;"));
            Assert.That(runtimeSource, Does.Contain("settings.curveMapMaxEV100 = curveMapTextureData.maxEV100;"));
            Assert.That(runtimeSource, Does.Contain("internal readonly struct AutoExposureCurveMapTextureData"));
            Assert.That(runtimeSource, Does.Contain("internal static class AutoExposureCurveMapUtility"));
            Assert.That(runtimeSource, Does.Contain("new Color(remappedEV100, resolvedClampMinEV100, resolvedClampMaxEV100, 0f)"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureCurveMapUtility.Dispose();"));
            Assert.That(runtimeSource, Does.Contain("ResolveAverageSceneLuminanceFromEV100(settings.manualEV100)"));
            Assert.That(runtimeSource, Does.Contain("internal static AutoExposureSettingsData ResolvePhysicalCameraFallback"));
            Assert.That(runtimeSource, Does.Contain("AutoExposureImplementationUtility.ResolveComputeShader("));
            Assert.That(runtimeSource, Does.Contain("VividRenderPipelineAsset.GetActiveAsset()"));
            Assert.That(runtimeSource, Does.Contain("var preExposureBuffer = exposureEnabled"));
            Assert.That(runtimeSource, Does.Contain("? state.currentExposureBuffer"));
            Assert.That(runtimeSource, Does.Contain("public RenderTexture previousExposureTexture;"));
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
        public void AutoExposureEditorOnlyGpuReadbackPath_WiresRuntimeAndEditorStatsMonitor()
        {
            var autoExposurePassSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "AutoExposurePass.cs"));
            var readbackBridgeSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureStatsReadbackBridge.cs"));
            var editorSource = File.ReadAllText(GetPackageFilePath("Editor", "VolumeEditor", "AutoExposureEditor.cs"));
            var statsShaderSource = File.ReadAllText(GetPackageFilePath("Editor", "Shader", "AutoExposureStats.shader"));

            Assert.That(autoExposurePassSource, Does.Contain("AutoExposureStatsReadbackBridge.Request("));
            Assert.That(autoExposurePassSource, Does.Contain("UsesHistogramBufferAutoExposureExecution()"));
            Assert.That(autoExposurePassSource, Does.Contain("ExecuteHdrpHistogramAutoExposure("));
            Assert.That(autoExposurePassSource, Does.Contain("m_HistogramAutoExposureCompute"));
            Assert.That(autoExposurePassSource, Does.Contain("BindAutoExposureParameters(cmd, histogramCompute, m_ClearHistogramKernel);"));
            Assert.That(readbackBridgeSource, Does.Contain("Dictionary<Camera, SnapshotState>"));
            Assert.That(readbackBridgeSource, Does.Contain("internal static void TouchInspectorRequest()"));
            Assert.That(readbackBridgeSource, Does.Contain("internal static bool TryGetLatestSnapshot(out AutoExposureStatsReadbackSnapshot snapshot)"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(exposureBuffer"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(preExposureBuffer"));
            Assert.That(readbackBridgeSource, Does.Contain("HandlePreExposureReadback"));
            Assert.That(readbackBridgeSource, Does.Contain("public readonly Vector4 preExposureState;"));
            Assert.That(readbackBridgeSource, Does.Contain("public readonly bool hasPreExposureState;"));
            Assert.That(readbackBridgeSource, Does.Contain("commandBuffer.RequestAsyncReadback(histogramBuffer"));
            Assert.That(autoExposurePassSource, Does.Contain("AutoExposureShaderBindings.ResolvePreExposureBuffer(m_ExposureData)"));
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

        private static Vector4 CreateExposureState(float exposureScale, float middleGreyCompensation = 1f)
        {
            return new Vector4(
                exposureScale,
                exposureScale,
                AutoExposureSettingsResolver.MiddleGrey,
                middleGreyCompensation);
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
