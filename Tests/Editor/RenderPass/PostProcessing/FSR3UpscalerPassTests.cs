using System.IO;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class FSR3UpscalerPassTests
    {
        [Test]
        public void QualityMode_MapsToExpectedRenderSize()
        {
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.NativeAA), Is.EqualTo(1.0f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Quality), Is.EqualTo(1.5f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Balanced), Is.EqualTo(1.7f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.Performance), Is.EqualTo(2.0f));
            Assert.That(FSR3UpscalerUtility.GetUpscaleRatio(VividFsr3QualityMode.UltraPerformance), Is.EqualTo(3.0f));

            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.NativeAA),
                Is.EqualTo(new Vector2Int(3840, 2160)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.Quality),
                Is.EqualTo(new Vector2Int(2560, 1440)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.Performance),
                Is.EqualTo(new Vector2Int(1920, 1080)));
            Assert.That(
                FSR3UpscalerUtility.ResolveRenderSize(3840, 2160, VividFsr3QualityMode.UltraPerformance),
                Is.EqualTo(new Vector2Int(1280, 720)));
        }

        [Test]
        public void Jitter_UsesSdkPhaseCountAndHaltonOffset()
        {
            Assert.That(FSR3UpscalerUtility.GetJitterPhaseCount(1920, 3840), Is.EqualTo(32));

            var offset = FSR3UpscalerUtility.GetJitterOffset(0, 32);
            Assert.That(offset.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(offset.y, Is.EqualTo(-1.0f / 6.0f).Within(0.0001f));
        }

        [Test]
        public void MotionVectorScale_IsNegativeRenderSize()
        {
            Assert.That(
                FSR3UpscalerUtility.GetMotionVectorScale(1920, 1080),
                Is.EqualTo(new Vector2(-1920.0f, -1080.0f)));

            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "FSR3",
                "FSR3UpscalerPass.cs"));

            Assert.That(passSource, Does.Contain("return FSR3UpscalerUtility.GetMotionVectorScale(renderSize.x, renderSize.y);"));
        }

        [Test]
        public void SourceFiles_RecordFsr3ThroughExplicitAntialiasingPass()
        {
            var passRecorderSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "PassRecorder.Execution.cs"));
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
            var cameraEditorSource = File.ReadAllText(GetPackageFilePath(
                "Editor",
                "ComponentEditor",
                "VividCameraEditor.cs"));

            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectFsr3Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("GetOrCreateInjectedFsr3Pass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedFsr3Pass"));
            Assert.That(passRecorderSource, Does.Contain("pass is IRenderGraphRecordingPass graphRecordingPass"));
            Assert.That(antialiasingPassSource, Does.Contain("TryRecordFsr3Pass"));
            Assert.That(antialiasingPassSource, Does.Contain("m_Fsr3Pass.Record"));
            Assert.That(antialiasingPassSource, Does.Contain("AntialiasingOutput"));
            Assert.That(antialiasingPassSource, Does.Contain("m_ResetHistory"));
            Assert.That(resolverSource, Does.Contain("FSR3UpscalerUtility.ResolveRenderSize"));
            Assert.That(resolverSource, Does.Contain("hasAntialiasingPass"));
            Assert.That(cameraEditorSource, Does.Contain("ShouldShowFSR3Settings()"));
            Assert.That(cameraEditorSource, Does.Contain("m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.FidelityFXSuperResolution3"));
            Assert.That(cameraEditorSource, Does.Contain("AntialiasingPass node"));
        }

        [Test]
        public void SourceFiles_CreatePresentationSizedRandomWriteOutputAndSdkResources()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "FSR3",
                "FSR3UpscalerPass.cs"));

            Assert.That(passSource, Does.Contain("descriptor.Name = \"FSR3Output\""));
            Assert.That(passSource, Does.Contain("descriptor.EnableRandomWrite = true"));
            Assert.That(passSource, Does.Contain("descriptor.Width = Mathf.Max(1, outputSize.x)"));
            Assert.That(passSource, Does.Contain("descriptor.Height = Mathf.Max(1, outputSize.y)"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R8_UNorm"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16G16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16G16B16A16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R32G32B32A32_SFloat"));
            Assert.That(passSource, Does.Contain("m_Accumulation"));
            Assert.That(passSource, Does.Contain("m_InternalUpscaled"));
            Assert.That(passSource, Does.Contain("m_LumaHistory"));
            Assert.That(passSource, Does.Contain("m_Luma"));
            Assert.That(passSource, Does.Contain("renderGraph.ImportTexture(m_Luma[readIndex])"));
            Assert.That(passSource, Does.Contain("renderGraph.ImportTexture(m_Luma[writeIndex])"));
            Assert.That(passSource, Does.Contain("temporalData != null && temporalData.IsFirstFrame"));
            Assert.That(passSource, Does.Contain("forceResetHistory"));
        }

        [Test]
        public void SourceFiles_DispatchSdkPassPipelineInOrder()
        {
            var passSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "FSR3",
                "FSR3UpscalerPass.cs"));

            Assert.That(passSource.IndexOf("DispatchPrepareInputs", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchLumaPyramid", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchLumaPyramid", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchShadingChangePyramid", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchShadingChangePyramid", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchShadingChange", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchShadingChange", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchPrepareReactivity", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchPrepareReactivity", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchLumaInstability", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchLumaInstability", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchAccumulate", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchAccumulate", System.StringComparison.Ordinal),
                Is.LessThan(passSource.IndexOf("DispatchRcas", System.StringComparison.Ordinal)));
        }

        [Test]
        public void ShaderWrappers_ExistAndKeepAmdLicenseWithoutNativeFfxApi()
        {
            string[] computeFiles =
            {
                "FSR3PrepareInputs.compute",
                "FSR3LumaPyramid.compute",
                "FSR3ShadingChangePyramid.compute",
                "FSR3ShadingChange.compute",
                "FSR3PrepareReactivity.compute",
                "FSR3LumaInstability.compute",
                "FSR3Accumulate.compute",
                "FSR3AccumulateSharpen.compute",
                "FSR3RCAS.compute",
            };

            foreach (var computeFile in computeFiles)
            {
                var path = GetPackageFilePath("Shaders", "Core", "Private", "FSR3", computeFile);
                Assert.That(File.Exists(path), Is.True, $"Expected FSR3 compute source at '{path}'.");
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Contain("Copyright (C) 2024 Advanced Micro Devices, Inc."));
                Assert.That(source, Does.Contain("#pragma kernel CS"));
                Assert.That(source, Does.Not.Contain("ffx_api"));
            }

            Assert.That(
                File.Exists(GetPackageFilePath(
                    "Shaders",
                    "Core",
                    "Private",
                    "FSR3",
                    "fsr3upscaler",
                    "ffx_fsr3upscaler_debug_view.h")),
                Is.False);
        }

        [Test]
        public void PipelineResources_RegisterAllFsr3ComputeShaders()
        {
            var resourcesSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "Utility",
                "PipelineResource",
                "VividResources.cs"));

            Assert.That(resourcesSource, Does.Contain("FSR3PrepareInputsCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3LumaPyramidCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3ShadingChangePyramidCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3ShadingChangeCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3PrepareReactivityCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3LumaInstabilityCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3AccumulateCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3AccumulateSharpenCompute"));
            Assert.That(resourcesSource, Does.Contain("FSR3RCASCompute"));
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
