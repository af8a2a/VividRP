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
            autoExposure.minBrightness.value = 0.03f;
            autoExposure.maxBrightness.value = 2.0f;
            autoExposure.speedUp.value = 3f;
            autoExposure.speedDown.value = 1f;

            Assert.That(autoExposure.IsActive(), Is.True);
        }

        [Test]
        public void AutoExposure_IsActive_WhenHistogramEV100OverridesProduceValidRange()
        {
            var autoExposure = new AutoExposure();
            autoExposure.enabled.value = true;
            autoExposure.minBrightness.value = 2f;
            autoExposure.maxBrightness.value = 1f;
            autoExposure.minEV100.overrideState = true;
            autoExposure.maxEV100.overrideState = true;
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
        public void BuildHistogramScaleBias_PacksLogRangeIntoShaderSpace()
        {
            var result = AutoExposureSettingsResolver.BuildHistogramScaleBias(-10f, 6f);

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
        public void ResolveWhitePointLuminanceFromEV100_ConvertsStopsToWhitePoint()
        {
            var result = AutoExposureSettingsResolver.ResolveWhitePointLuminanceFromEV100(2f);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void ResolveHistogramWhitePointLuminance_UsesEV100Override_WhenEnabled()
        {
            var result = AutoExposureSettingsResolver.ResolveHistogramWhitePointLuminance(0.03f, 2f, true);

            Assert.That(result, Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void ResolveHistogramWhitePointLuminance_FallsBackToLegacyBrightness_WhenOverrideDisabled()
        {
            var result = AutoExposureSettingsResolver.ResolveHistogramWhitePointLuminance(0.03f, 2f, false);

            Assert.That(result, Is.EqualTo(0.03f).Within(1e-5f));
        }

        [Test]
        public void ResolveManualExposureScale_CombinesManualEVAndExposureCompensation()
        {
            var result = AutoExposureSettingsResolver.ResolveManualExposureScale(2f, 4f);

            Assert.That(result, Is.EqualTo(1f).Within(1e-5f));
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
        public void ComputeExponentialTransitionMultiplier_ReturnsPositiveBlendFactor()
        {
            var result = AutoExposureSettingsResolver.ComputeExponentialTransitionMultiplier(3f, AutoExposureSettingsResolver.DefaultStartDistance);

            Assert.That(result, Is.GreaterThan(0f));
            Assert.That(result, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void VividRPCoreResources_DeclaresAutoExposureCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.AutoExposureCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Core/Private/AutoExposure"));
        }

        [Test]
        public void AutoExposureShader_DeclaresHistogramAndExposureKernels()
        {
            var shaderSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "AutoExposure.compute"));
            var helperSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Public", "AutoExposure.hlsl"));
            var runtimeSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "PostProcessing", "AutoExposure", "AutoExposureRuntimeUtility.cs"));

            Assert.That(shaderSource, Does.Contain("#pragma kernel ClearHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel BuildHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ResolveExposure"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("const float preExposure = max(_PreviousExposureBuffer[0].x, kEpsilon);"));
            Assert.That(shaderSource, Does.Contain("_InputColor.Load(int3(dispatchThreadId.xy, 0)).rgb / preExposure"));
            Assert.That(helperSource, Does.Contain("StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;"));
            Assert.That(helperSource, Does.Contain("float3 VividApplyPreExposure(float3 color)"));
            Assert.That(runtimeSource, Does.Contain("settings.mode == AutoExposureMode.Manual"));
            Assert.That(runtimeSource, Does.Contain("settings.applyPhysicalCameraExposure = autoExposure.applyPhysicalCameraExposure.value;"));
            Assert.That(runtimeSource, Does.Contain("settings.manualEV100 = ResolveManualEV100("));
            Assert.That(runtimeSource, Does.Contain("ResolveHistogramWhitePointLuminance("));
            Assert.That(runtimeSource, Does.Contain("autoExposure.minEV100.overrideState"));
            Assert.That(runtimeSource, Does.Contain("autoExposure.maxEV100.overrideState"));
            Assert.That(runtimeSource, Does.Contain("ColorUtils.ComputeEV100(aperture, shutterSpeed, iso)"));
            Assert.That(runtimeSource, Does.Contain("ResolveManualExposureScale(settings.manualEV100, settings.exposureCompensation)"));
            Assert.That(runtimeSource, Does.Contain("ResolveAverageSceneLuminanceFromEV100(settings.manualEV100)"));
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
    }
}
