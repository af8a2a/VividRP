using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class TSRUpscalerPassTests
    {
        [Test]
        public void QualityMode_MapsToExpectedRenderSize()
        {
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.NativeAA), Is.EqualTo(1.0f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Quality), Is.EqualTo(1.5f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Balanced), Is.EqualTo(1.7f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.Performance), Is.EqualTo(2.0f));
            Assert.That(TSRUpscalerUtility.GetUpscaleRatio(VividTsrQualityMode.UltraPerformance), Is.EqualTo(3.0f));

            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.NativeAA),
                Is.EqualTo(new Vector2Int(3840, 2160)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Quality),
                Is.EqualTo(new Vector2Int(2560, 1440)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Balanced),
                Is.EqualTo(new Vector2Int(2259, 1271)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.Performance),
                Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(
                TSRUpscalerUtility.ResolveRenderSize(3840, 2160, VividTsrQualityMode.UltraPerformance),
                Is.EqualTo(new Vector2Int(1280, 720)));
        }

        [Test]
        public void Jitter_UsesOutputScalePhaseCountAndHaltonOffset()
        {
            Assert.That(TSRUpscalerUtility.GetJitterPhaseCount(1920, 3840), Is.EqualTo(32));

            var offset = TSRUpscalerUtility.GetJitterOffset(0, 32);
            Assert.That(offset.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(offset.y, Is.EqualTo(-1.0f / 6.0f).Within(0.0001f));
        }

        [Test]
        public void SourceFiles_RecordTsrThroughExplicitAntialiasingPass()
        {
            var antialiasingPassSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "AntialiasingPass.cs"));
            var resolverSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "FrameContext",
                "VividAntialiasingData.cs"));
            var serializedCameraSource = File.ReadAllText(GetPackageFilePath(
                "Editor",
                "ComponentEditor",
                "VividSerializedCamera.cs"));
            var cameraEditorSource = File.ReadAllText(GetPackageFilePath(
                "Editor",
                "ComponentEditor",
                "VividCameraEditor.cs"));

            Assert.That(antialiasingPassSource, Does.Contain("TryRecordTsrPass"));
            Assert.That(antialiasingPassSource, Does.Contain("m_TsrPass.Record"));
            Assert.That(antialiasingPassSource, Does.Contain("HasTemporalInputs()"));
            Assert.That(resolverSource, Does.Contain("TSRUpscalerPass.IsSupported"));
            Assert.That(resolverSource, Does.Contain("TSRUpscalerUtility.ResolveRenderSize"));
            Assert.That(resolverSource, Does.Contain("ApplyTsrJitter"));
            Assert.That(serializedCameraSource, Does.Contain("m_TSRQuality"));
            Assert.That(serializedCameraSource, Does.Contain("m_TSRHistorySampleCount"));
            Assert.That(cameraEditorSource, Does.Contain("ShouldShowTSRSettings()"));
            Assert.That(cameraEditorSource, Does.Contain("m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.TemporalSuperResolution"));
        }

        [Test]
        public void SourceFiles_CreateExpectedHistoryAndTransientResources()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "TSR",
                "TSRUpscalerPass.cs"));

            Assert.That(passSource, Does.Contain("CameraStateExpirationFrames = 400"));
            Assert.That(passSource, Does.Contain("descriptor.Name = name"));
            Assert.That(passSource, Does.Contain("descriptor.EnableRandomWrite = true"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16G16B16A16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16G16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R32_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R8_UNorm"));
            Assert.That(passSource, Does.Contain("m_HistoryColor"));
            Assert.That(passSource, Does.Contain("m_HistoryMeta"));
            Assert.That(passSource, Does.Contain("m_RenderSize != renderSize"));
            Assert.That(passSource, Does.Contain("m_OutputSize != outputSize"));
            Assert.That(passSource, Does.Contain("m_Quality != quality"));
            Assert.That(passSource, Does.Contain("m_HistorySampleCount != historySampleCount"));
            Assert.That(passSource, Does.Contain("forceResetHistory || (temporalData != null && temporalData.IsFirstFrame)"));
        }

        [Test]
        public void SourceFiles_DispatchTsrPassPipelineInOrder()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "TSR",
                "TSRUpscalerPass.cs"));

            Assert.That(passSource.IndexOf("DispatchDilateVelocity", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchReprojectHistory", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchReprojectHistory", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchRejectShading", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchRejectShading", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchUpdateHistory", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchUpdateHistory", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchResolveHistory", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchResolveHistory", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchSharpen", System.StringComparison.Ordinal)));
        }

        [Test]
        public void ShaderFiles_ExistWithUnityComputeKernels()
        {
            string[] computeFiles =
            {
                "TSRDilateVelocity.compute",
                "TSRReprojectHistory.compute",
                "TSRRejectShading.compute",
                "TSRUpdateHistory.compute",
                "TSRResolveHistory.compute",
                "TSRSharpen.compute",
            };

            foreach (var computeFile in computeFiles)
            {
                var path = GetPackageFilePath("Shaders", "Core", "Private", "TSR", computeFile);
                Assert.That(File.Exists(path), Is.True, $"Expected TSR compute source at '{path}'.");
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Contain("#pragma kernel CS"));
                Assert.That(source, Does.Contain("TSRCommon.hlsl"));
            }

            Assert.That(File.Exists(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRCommon.hlsl")), Is.True);
        }

        [Test]
        public void PipelineResources_RegisterAllTsrComputeShaders()
        {
            var resourcesSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "Utility",
                "PipelineResource",
                "VividResources.cs"));

            Assert.That(resourcesSource, Does.Contain("TSRDilateVelocityCompute"));
            Assert.That(resourcesSource, Does.Contain("TSRReprojectHistoryCompute"));
            Assert.That(resourcesSource, Does.Contain("TSRRejectShadingCompute"));
            Assert.That(resourcesSource, Does.Contain("TSRUpdateHistoryCompute"));
            Assert.That(resourcesSource, Does.Contain("TSRResolveHistoryCompute"));
            Assert.That(resourcesSource, Does.Contain("TSRSharpenCompute"));
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(parts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(parts));
        }
    }
}
