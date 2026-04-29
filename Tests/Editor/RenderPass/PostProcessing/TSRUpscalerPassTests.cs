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
            Assert.That(antialiasingPassSource, Does.Contain("antialiasingData?.renderSize"));
            Assert.That(antialiasingPassSource, Does.Contain("antialiasingData?.outputSize"));
            Assert.That(resolverSource, Does.Contain("TSRUpscalerPass.IsSupported"));
            Assert.That(resolverSource, Does.Contain("TSRUpscalerUtility.ResolveRenderSize"));
            Assert.That(resolverSource, Does.Contain("ApplyTsrJitter"));
            Assert.That(serializedCameraSource, Does.Contain("m_TSRQuality"));
            Assert.That(serializedCameraSource, Does.Contain("m_TSRHistorySampleCount"));
            Assert.That(cameraEditorSource, Does.Contain("ShouldShowTSRSettings()"));
            Assert.That(cameraEditorSource, Does.Contain("m_SerializedCamera.antialiasing.intValue == (int)VividAntialiasingMode.TemporalSuperResolution"));
        }

        [Test]
        public void SourceFiles_AdvanceTemporalFramesOutsidePlayMode()
        {
            var passRecorderSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "PassRecorder.Execution.cs"));
            var resolverSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "FrameContext",
                "VividAntialiasingData.cs"));
            var pipelineSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPipeline",
                "VividRenderPipeline.cs"));

            Assert.That(passRecorderSource, Does.Contain("s_EditModeFrameIndex"));
            Assert.That(passRecorderSource, Does.Contain("if (Application.isPlaying)"));
            Assert.That(passRecorderSource, Does.Contain("s_EditModeFrameIndex++"));
            Assert.That(passRecorderSource, Does.Contain("VividAntialiasingRuntimeUtility.ApplyJitter(camera, additionalCameraData, antialiasingData, frameIndex)"));
            Assert.That(resolverSource, Does.Contain("ResolveTemporalFrameIndex(frameIndex)"));
            Assert.That(pipelineSource, Does.Contain("currentFrameIndex = PassRecorder.GetFrameData().Get<VividCameraData>()?.frameIndex"));
            Assert.That(pipelineSource, Does.Contain("UnityEditor.EditorApplication.QueuePlayerLoopUpdate()"));
            Assert.That(pipelineSource, Does.Contain("UnityEditorInternal.InternalEditorUtility.RepaintAllViews()"));
        }

        [Test]
        public void ShaderFiles_KeepStaticNativeAaEdgesAccumulating()
        {
            var rejectSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRRejectShading.compute"));

            Assert.That(rejectSource, Does.Contain("hasSignificantMotion"));
            Assert.That(rejectSource, Does.Contain("depthRejected = hasSignificantMotion"));
            Assert.That(rejectSource, Does.Contain("colorRejected = hasSignificantMotion"));
            Assert.That(rejectSource, Does.Contain("&& !depthRejected"));
            Assert.That(rejectSource, Does.Contain("&& !colorRejected"));
        }

        [Test]
        public void ShaderFiles_DilateVelocityUsesDepthAndReprojectionBoundarySignals()
        {
            var dilateSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRDilateVelocity.compute"));
            var reprojectSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRReprojectHistory.compute"));
            var rejectSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRRejectShading.compute"));
            var updateSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRUpdateHistory.compute"));

            Assert.That(dilateSource, Does.Contain("RWTexture2D<float> _DepthError"));
            Assert.That(dilateSource, Does.Contain("RWTexture2D<float> _ReprojectionBoundary"));
            Assert.That(dilateSource, Does.Contain("RWTexture2D<float> _ThinGeometryCoverage"));
            Assert.That(dilateSource, Does.Contain("RWTexture2D<float> _LumaInstability"));
            Assert.That(dilateSource, Does.Contain("TSR_IsDepthCloser(sampleDepth, selectedDepth)"));
            Assert.That(dilateSource, Does.Contain("maxDepthError"));
            Assert.That(dilateSource, Does.Contain("motionBoundary"));
            Assert.That(dilateSource, Does.Contain("closestDepthCoverage"));
            Assert.That(dilateSource, Does.Contain("thinGeometryCoverage"));
            Assert.That(dilateSource, Does.Contain("spatialMoire"));
            Assert.That(dilateSource, Does.Contain("temporalFlicker"));
            Assert.That(dilateSource, Does.Contain("_LumaInstability[pixelCoord]"));
            Assert.That(reprojectSource, Does.Contain("Texture2D<float> _ReprojectionBoundary"));
            Assert.That(reprojectSource, Does.Contain("Texture2D<float> _ThinGeometryCoverage"));
            Assert.That(reprojectSource, Does.Contain("Texture2D<float> _LumaInstability"));
            Assert.That(reprojectSource, Does.Contain("historyConfidence"));
            Assert.That(reprojectSource, Does.Contain("max(localBoundary, uvBoundary)"));
            Assert.That(reprojectSource, Does.Contain("historyConfidence *= lerp(1.0, 0.55, thinGeometryCoverage)"));
            Assert.That(reprojectSource, Does.Contain("historyConfidence *= lerp(1.0, 0.75, lumaInstability"));
            Assert.That(rejectSource, Does.Contain("Texture2D<float> _DepthError"));
            Assert.That(rejectSource, Does.Contain("Texture2D<float> _LumaInstability"));
            Assert.That(rejectSource, Does.Contain("depthTolerance = _TSRRejectionParams.x + depthError * 2.0"));
            Assert.That(rejectSource, Does.Contain("lumaThreshold = lerp(lumaThreshold, lumaThreshold * 1.45, lumaInstability)"));
            Assert.That(rejectSource, Does.Contain("motionLimit = lerp"));
            Assert.That(updateSource, Does.Contain("Texture2D<float2> _DilatedMotion"));
            Assert.That(updateSource, Does.Contain("Texture2D<float> _ThinGeometryCoverage"));
            Assert.That(updateSource, Does.Contain("Texture2D<float> _LumaInstability"));
            Assert.That(updateSource, Does.Contain("velocityWeightClamp"));
            Assert.That(updateSource, Does.Contain("boundaryWeightClamp"));
            Assert.That(updateSource, Does.Contain("thinGeometryWeightClamp"));
            Assert.That(updateSource, Does.Contain("thinGeometrySampleCount"));
            Assert.That(updateSource, Does.Contain("antiFlickerSampleCount"));
            Assert.That(updateSource, Does.Contain("staticMoire"));
            Assert.That(updateSource, Does.Contain("movingMoire"));
            Assert.That(updateSource, Does.Contain("moireWeightFloor"));
            Assert.That(updateSource, Does.Contain("weightRelaxation"));
            Assert.That(updateSource, Does.Contain("recoveryRate"));
            Assert.That(updateSource, Does.Contain("rejectionRetention"));
            Assert.That(updateSource, Does.Contain("boundarySampleCount"));
            Assert.That(updateSource, Does.Contain("historyWeight *= saturate(1.0 - weightRelaxation)"));
        }

        [Test]
        public void ShaderFiles_SpatialAntiAliasingFiltersRejectedPixelsOnly()
        {
            var spatialSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRSpatialAntiAliasing.compute"));
            var updateSource = File.ReadAllText(GetPackageFilePath(
                "Shaders",
                "Core",
                "Private",
                "TSR",
                "TSRUpdateHistory.compute"));

            Assert.That(spatialSource, Does.Contain("Texture2D<float> _RejectionMask"));
            Assert.That(spatialSource, Does.Contain("bool rejected = _RejectionMask[pixelCoord] <= 0.5"));
            Assert.That(spatialSource, Does.Contain("if (!rejected)"));
            Assert.That(spatialSource, Does.Contain("_SpatialAntiAliasedColor[pixelCoord] = current"));
            Assert.That(spatialSource, Does.Contain("edgeStrength"));
            Assert.That(updateSource, Does.Contain("Texture2D<float4> _CurrentFrameColor"));
            Assert.That(updateSource, Does.Contain("float3 currentColor = _CurrentFrameColor[pixelCoord].rgb"));
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
            Assert.That(passSource, Does.Contain("GraphicsFormat.R16_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R32_SFloat"));
            Assert.That(passSource, Does.Contain("GraphicsFormat.R8_UNorm"));
            Assert.That(passSource, Does.Contain("m_HistoryColor"));
            Assert.That(passSource, Does.Contain("m_HistoryMeta"));
            Assert.That(passSource, Does.Contain("TSR_DepthError"));
            Assert.That(passSource, Does.Contain("TSR_ReprojectionBoundary"));
            Assert.That(passSource, Does.Contain("TSR_ThinGeometryCoverage"));
            Assert.That(passSource, Does.Contain("TSR_LumaInstability"));
            Assert.That(passSource, Does.Contain("TSR_SpatialAntiAliasedColor"));
            Assert.That(passSource, Does.Contain("ThinGeometryCoverage"));
            Assert.That(passSource, Does.Contain("LumaInstability"));
            Assert.That(passSource, Does.Contain("SpatialAntiAliasedColor"));
            Assert.That(passSource, Does.Contain("requestedRenderSize"));
            Assert.That(passSource, Does.Contain("requestedOutputSize"));
            Assert.That(passSource, Does.Contain("ResolveCurrentJitter(cameraData, temporalData)"));
            Assert.That(passSource, Does.Contain("ResolvePreviousJitter(cameraState, temporalData)"));
            Assert.That(passSource, Does.Not.Contain("passData.Jitter = additionalData != null ? additionalData.tsrJitterOffset"));
            Assert.That(passSource, Does.Contain("new Vector4("));
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
                Is.LessThan(passSource.IndexOf("DispatchSpatialAntiAliasing", System.StringComparison.Ordinal)));
            Assert.That(passSource.IndexOf("DispatchSpatialAntiAliasing", System.StringComparison.Ordinal),
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
                "TSRSpatialAntiAliasing.compute",
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
            Assert.That(resourcesSource, Does.Contain("TSRSpatialAntiAliasingCompute"));
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
