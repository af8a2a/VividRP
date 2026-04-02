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

            Assert.That(shaderSource, Does.Contain("#pragma kernel ClearHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel BuildHistogram"));
            Assert.That(shaderSource, Does.Contain("#pragma kernel ResolveExposure"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<uint> _HistogramBuffer;"));
            Assert.That(shaderSource, Does.Contain("RWStructuredBuffer<float4> _CurrentExposureBuffer;"));
            Assert.That(shaderSource, Does.Contain("const float preExposure = max(_PreviousExposureBuffer[0].x, kEpsilon);"));
            Assert.That(shaderSource, Does.Contain("_InputColor.Load(int3(dispatchThreadId.xy, 0)).rgb / preExposure"));
            Assert.That(helperSource, Does.Contain("StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;"));
            Assert.That(helperSource, Does.Contain("float3 VividApplyPreExposure(float3 color)"));
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
