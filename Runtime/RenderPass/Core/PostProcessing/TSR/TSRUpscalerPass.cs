using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class TSRUpscalerPass : IDisposable
    {
#if UNITY_EDITOR
        // Opt-in diagnostics: issue readbacks on the same command buffer after TSR.
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture, Texture, Texture, Texture, Texture> EditorTemporalCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture, Texture, Texture, Texture, Texture> EditorHistoryCapture;
        // The selected camera uses diagnostic kernels only while subscribed.
        // Outputs: rejection reason bits/metrics, blend weights, updated history.
        internal static Camera EditorHistoryLossCaptureCamera;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture, Texture> EditorHistoryLossCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture> EditorShadingGuideCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture, Texture> EditorGuideConfidenceCapture;
        private static readonly int HistoryLossDiagnosticsId = Shader.PropertyToID("_HistoryLossDiagnostics");
        private readonly RenderGraphTextureDesc m_RejectDiagnosticsDescriptor = new();
        private readonly RenderGraphTextureDesc m_UpdateDiagnosticsDescriptor = new();
#endif
        private const int CameraStateExpirationFrames = 400;
        private const int KernelThreadGroupSize = 8;
        private const string TsrWaveOpsKeyword = "VIVID_TSR_WAVE_OPS";
        private const string TsrPairedGuidesKeyword = "VIVID_TSR_PAIRED_GUIDES";

        private static readonly ProfilerMarker s_RecordGraphMarker =
            new("VividRP.RenderPass.RecordGraph/Temporal Super Resolution (Injected)");
        private static readonly ProfilerMarker s_RecordMarker =
            new("VividRP.RenderPass.Record/Temporal Super Resolution (Injected)");
        private static readonly ProfilerMarker s_DisposeMarker =
            new("VividRP.RenderPass.Dispose/Temporal Super Resolution (Injected)");
        private static readonly ProfilingSampler s_ProfilingSampler = new("Temporal Super Resolution");

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int InputShadingGuideId = Shader.PropertyToID("_InputShadingGuide");
        private static readonly int HistoryShadingGuideId = Shader.PropertyToID("_HistoryShadingGuide");
        private static readonly int OutputInputShadingGuideId = Shader.PropertyToID("_OutputInputShadingGuide");
        private static readonly int OutputHistoryShadingGuideId = Shader.PropertyToID("_OutputHistoryShadingGuide");
        private static readonly ProfilingSampler s_ShadingGuideSampler = new("TSR.BuildShadingGuides");
        private static readonly ProfilingSampler s_GuideConfidenceSampler = new("TSR.PropagateShadingConfidence");
        private static readonly int PreviousShadingGuideId = Shader.PropertyToID("_PreviousShadingGuide");
        private static readonly int CurrentShadingGuideId = Shader.PropertyToID("_CurrentShadingGuide");
        private static readonly int ShadingGuideMetadataId = Shader.PropertyToID("_ShadingGuideMetadata");
        private static readonly int OutputShadingGuideMetadataId = Shader.PropertyToID("_OutputShadingGuideMetadata");
        private static readonly int ShadingGuideConfidenceId = Shader.PropertyToID("_ShadingGuideConfidence");
        private static readonly int OutputShadingGuideConfidenceId = Shader.PropertyToID("_OutputShadingGuideConfidence");
        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int InputMotionVectorsId = Shader.PropertyToID("_InputMotionVectors");
        private static readonly int HistoryColorId = Shader.PropertyToID("_HistoryColor");
        private static readonly int HistoryMetaId = Shader.PropertyToID("_HistoryMeta");
        private static readonly int DilatedMotionId = Shader.PropertyToID("_DilatedMotion");
        private static readonly int DilatedDepthId = Shader.PropertyToID("_DilatedDepth");
        private static readonly int DepthErrorId = Shader.PropertyToID("_DepthError");
        private static readonly int ReprojectionBoundaryId = Shader.PropertyToID("_ReprojectionBoundary");
        private static readonly int ThinGeometryCoverageId = Shader.PropertyToID("_ThinGeometryCoverage");
        private static readonly int LumaInstabilityId = Shader.PropertyToID("_LumaInstability");
        private static readonly int ReprojectedHistoryColorId = Shader.PropertyToID("_ReprojectedHistoryColor");
        private static readonly int ReprojectedHistoryMetaId = Shader.PropertyToID("_ReprojectedHistoryMeta");
        private static readonly int ResurrectionColorId = Shader.PropertyToID("_ResurrectionColor");
        private static readonly int ResurrectionMetaId = Shader.PropertyToID("_ResurrectionMeta");
        private static readonly int ReprojectedResurrectionColorId = Shader.PropertyToID("_ReprojectedResurrectionColor");
        private static readonly int ReprojectedResurrectionMetaId = Shader.PropertyToID("_ReprojectedResurrectionMeta");
        private static readonly int AcceptedHistoryColorId = Shader.PropertyToID("_AcceptedHistoryColor");
        private static readonly int RejectionMaskId = Shader.PropertyToID("_RejectionMask");
        private static readonly int CurrentFrameColorId = Shader.PropertyToID("_CurrentFrameColor");
        private static readonly int SpatialAntiAliasedColorId = Shader.PropertyToID("_SpatialAntiAliasedColor");
        private static readonly int UpdatedHistoryColorId = Shader.PropertyToID("_UpdatedHistoryColor");
        private static readonly int UpdatedHistoryMetaId = Shader.PropertyToID("_UpdatedHistoryMeta");
        private static readonly int UpdatedResurrectionColorId = Shader.PropertyToID("_UpdatedResurrectionColor");
        private static readonly int UpdatedResurrectionMetaId = Shader.PropertyToID("_UpdatedResurrectionMeta");
        private static readonly int ResolvedOutputId = Shader.PropertyToID("_ResolvedOutput");
        private static readonly int SharpenInputId = Shader.PropertyToID("_SharpenInput");
        private static readonly int OutputColorId = Shader.PropertyToID("_OutputColor");
        private static readonly int RenderSizeId = Shader.PropertyToID("_RenderSize");
        private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");
        private static readonly int PreviousOutputSizeId = Shader.PropertyToID("_PreviousOutputSize");
        private static readonly int JitterId = Shader.PropertyToID("_Jitter");
        private static readonly int TSRParamsId = Shader.PropertyToID("_TSRParams");
        private static readonly int TSRRejectionParamsId = Shader.PropertyToID("_TSRRejectionParams");

        private readonly Dictionary<EntityId, CameraState> m_CameraStates = new();
        private readonly List<EntityId> m_ExpiredCameraIds = new();
        private readonly RenderGraphTextureDesc m_OutputDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTextureDesc m_DilatedMotionDescriptor = new();
        private readonly RenderGraphTextureDesc m_DilatedDepthDescriptor = new();
        private readonly RenderGraphTextureDesc m_DepthErrorDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectionBoundaryDescriptor = new();
        private readonly RenderGraphTextureDesc m_ThinGeometryCoverageDescriptor = new();
        private readonly RenderGraphTextureDesc m_LumaInstabilityDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectedHistoryColorDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectedHistoryMetaDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectedResurrectionColorDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectedResurrectionMetaDescriptor = new();
        private readonly RenderGraphTextureDesc m_AcceptedHistoryColorDescriptor = new();
        private readonly RenderGraphTextureDesc m_InputShadingGuideDescriptor = new();
        private readonly RenderGraphTextureDesc m_HistoryShadingGuideDescriptor = new();
        private readonly RenderGraphTextureDesc m_GuideMetadataDescriptor = new();
        private readonly RenderGraphTextureDesc m_GuideConfidenceDescriptor = new();
        private readonly RenderGraphTextureDesc m_RejectionMaskDescriptor = new();
        private readonly RenderGraphTextureDesc m_SpatialAntiAliasedColorDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        private readonly RenderGraphTextureDesc m_PreSharpenOutputDescriptor =
            RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R16G16B16A16_SFloat);

        public static bool IsSupported
        {
            get
            {
                if (!SystemInfo.supportsComputeShaders)
                    return false;

                var resources = PipelineResourceManager.Get<VividRPCoreResources>();
                return TryResolveShaderSet(resources, out _);
            }
        }

        public bool Record(
            RenderGraph renderGraph,
            VividCameraData cameraData,
            CameraTemporalData temporalData,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture depthTexture,
            RenderGraphTexture motionTexture,
            RenderGraphTexture outputTexture,
            Vector2Int requestedRenderSize,
            Vector2Int requestedOutputSize,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            bool forceResetHistory = false)
        {
            using var recordGraphScope = s_RecordGraphMarker.Auto();
            if (renderGraph == null
                || cameraData?.camera == null
                || sourceTexture?.innerHandle.IsValid() != true
                || depthTexture?.innerHandle.IsValid() != true
                || motionTexture?.innerHandle.IsValid() != true
                || outputTexture == null)
            {
                return false;
            }

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (!TryResolveShaderSet(resources, out var shaders))
                return false;

            var renderSize = ResolveRenderSize(requestedRenderSize, sourceTexture, cameraData);
            var outputSize = ResolveOutputSize(requestedOutputSize, outputTexture, cameraData, renderSize);
            if (renderSize.x <= 0 || renderSize.y <= 0 || outputSize.x <= 0 || outputSize.y <= 0)
                return false;

            var additionalData = cameraData.additionalData;
            bool enablePairedGuides = VividRenderingDebugDisplaySettings.Data.tsrPairedShadingGuides;
            var quality = additionalData != null
                ? additionalData.tsrQuality
                : VividTsrQualityMode.Balanced;
            var historySampleCount = additionalData != null
                ? additionalData.tsrHistorySampleCount
                : 16;

            var cameraState = GetOrCreateCameraState(cameraData.camera, cameraData.frameIndex);
            CleanupExpiredCameraStates(cameraData.frameIndex);

            var resetHistory = cameraState.Prepare(
                cameraData.camera,
                renderSize,
                outputSize,
                quality,
                historySampleCount,
                cameraData.frameIndex,
                forceResetHistory || (temporalData != null && temporalData.IsFirstFrame), enablePairedGuides);

            var outputDescriptor = ConfigureOutputDescriptor(m_OutputDescriptor, sourceTexture.desc, outputSize);
            var outputHandle = renderGraph.CreateTexture(outputDescriptor);
            var handles = cameraState.Import(renderGraph);
            var currentJitter = ResolveCurrentJitter(cameraData, temporalData);
            var previousJitter = ResolvePreviousJitter(cameraState, temporalData);

            var dilatedMotion = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_DilatedMotionDescriptor,
                    "TSR_DilatedMotion",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R16G16_SFloat));
            var dilatedDepth = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_DilatedDepthDescriptor,
                    "TSR_DilatedDepth",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R32_SFloat));
            var depthError = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_DepthErrorDescriptor,
                    "TSR_DepthError",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R16_SFloat));
            var reprojectionBoundary = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ReprojectionBoundaryDescriptor,
                    "TSR_ReprojectionBoundary",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R8_UNorm));
            var thinGeometryCoverage = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ThinGeometryCoverageDescriptor,
                    "TSR_ThinGeometryCoverage",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R8_UNorm));
            var lumaInstability = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_LumaInstabilityDescriptor,
                    "TSR_LumaInstability",
                    renderSize.x,
                    renderSize.y,
                    GraphicsFormat.R8_UNorm));
            var reprojectedHistoryColor = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ReprojectedHistoryColorDescriptor,
                    "TSR_ReprojectedHistoryColor",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R16G16B16A16_SFloat));
            var reprojectedHistoryMeta = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ReprojectedHistoryMetaDescriptor,
                    "TSR_ReprojectedHistoryMeta",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R16G16_SFloat));
            var reprojectedResurrectionColor = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ReprojectedResurrectionColorDescriptor,
                    "TSR_ReprojectedResurrectionColor",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R16G16B16A16_SFloat));
            var reprojectedResurrectionMeta = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_ReprojectedResurrectionMetaDescriptor,
                    "TSR_ReprojectedResurrectionMeta",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R16G16_SFloat));
            var inputShadingGuide = enablePairedGuides ? renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_InputShadingGuideDescriptor, "TSR_InputShadingGuide", renderSize.x, renderSize.y,
                GraphicsFormat.R16G16B16A16_SFloat)) : default;
            var historyShadingGuide = enablePairedGuides ? renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_HistoryShadingGuideDescriptor, "TSR_HistoryShadingGuide", renderSize.x, renderSize.y,
                GraphicsFormat.R16G16B16A16_SFloat)) : default;
            var acceptedHistoryColor = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_AcceptedHistoryColorDescriptor,
                    "TSR_AcceptedHistoryColor",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R16G16B16A16_SFloat));
            var guideMetadata = enablePairedGuides ? renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_GuideMetadataDescriptor, "TSR_GuideMetadata", renderSize.x, renderSize.y, GraphicsFormat.R16G16_SFloat)) : default;
            var guideConfidence = enablePairedGuides ? renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_GuideConfidenceDescriptor, "TSR_GuideConfidence", renderSize.x, renderSize.y, GraphicsFormat.R16G16_SFloat)) : default;
            var rejectionMask = renderGraph.CreateTexture(
                ConfigureColorDescriptor(
                    m_RejectionMaskDescriptor,
                    "TSR_RejectionMask",
                    outputSize.x,
                    outputSize.y,
                    GraphicsFormat.R8_UNorm));
            var spatialAntiAliasedColor = renderGraph.CreateTexture(
                ConfigureOutputDescriptor(
                    m_SpatialAntiAliasedColorDescriptor,
                    sourceTexture.desc,
                    outputSize,
                    "TSR_SpatialAntiAliasedColor"));
            var enableSharpening = additionalData == null || additionalData.tsrEnableSharpening;
            var resolveOutput = enableSharpening
                ? renderGraph.CreateTexture(
                    ConfigureOutputDescriptor(
                        m_PreSharpenOutputDescriptor,
                        sourceTexture.desc,
                        outputSize,
                        "TSR_PreSharpenOutput"))
                : outputHandle;

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "Temporal Super Resolution",
                       out var passData,
                       s_ProfilingSampler))
            {
                passData.State = cameraState;
#if UNITY_EDITOR
                passData.Camera = cameraData.camera;
                passData.FrameIndex = cameraData.frameIndex;
                passData.CaptureHistoryLoss = EditorHistoryLossCapture != null
                    && EditorHistoryLossCaptureCamera == cameraData.camera
                    && shaders.RejectDiagnosticsKernel >= 0 && shaders.UpdateDiagnosticsKernel >= 0;
                passData.RejectDiagnostics = default;
                passData.UpdateDiagnostics = default;
                if (passData.CaptureHistoryLoss)
                {
                    passData.RejectDiagnostics = renderGraph.CreateTexture(ConfigureColorDescriptor(
                        m_RejectDiagnosticsDescriptor, "TSR_RejectDiagnostics", outputSize.x, outputSize.y,
                        GraphicsFormat.R32G32B32A32_SFloat));
                    passData.UpdateDiagnostics = renderGraph.CreateTexture(ConfigureColorDescriptor(
                        m_UpdateDiagnosticsDescriptor, "TSR_UpdateDiagnostics", outputSize.x, outputSize.y,
                        GraphicsFormat.R32G32B32A32_SFloat));
                    builder.UseTexture(passData.RejectDiagnostics, AccessFlags.WriteAll);
                    builder.UseTexture(passData.UpdateDiagnostics, AccessFlags.WriteAll);
                }
#endif
                passData.Shaders = shaders;
                passData.Source = sourceTexture.innerHandle;
                passData.Depth = depthTexture.innerHandle;
                passData.MotionVectors = motionTexture.innerHandle;
                passData.Output = outputHandle;
                passData.DilatedMotion = dilatedMotion;
                passData.DilatedDepth = dilatedDepth;
                passData.DepthError = depthError;
                passData.ReprojectionBoundary = reprojectionBoundary;
                passData.ThinGeometryCoverage = thinGeometryCoverage;
                passData.LumaInstability = lumaInstability;
                passData.ReprojectedHistoryColor = reprojectedHistoryColor;
                passData.ReprojectedHistoryMeta = reprojectedHistoryMeta;
                passData.ReprojectedResurrectionColor = reprojectedResurrectionColor;
                passData.ReprojectedResurrectionMeta = reprojectedResurrectionMeta;
                passData.AcceptedHistoryColor = acceptedHistoryColor;
                passData.InputShadingGuide = inputShadingGuide;
                passData.HistoryShadingGuide = historyShadingGuide;
                passData.EnablePairedGuides = enablePairedGuides;
                passData.GuideMetadata = guideMetadata;
                passData.GuideConfidence = guideConfidence;
                passData.PreviousShadingGuide = handles.PreviousShadingGuide;
                passData.CurrentShadingGuide = handles.CurrentShadingGuide;
                passData.RejectionMask = rejectionMask;
                passData.SpatialAntiAliasedColor = spatialAntiAliasedColor;
                passData.PreviousHistoryColor = handles.PreviousHistoryColor;
                passData.CurrentHistoryColor = handles.CurrentHistoryColor;
                passData.PreviousHistoryMeta = handles.PreviousHistoryMeta;
                passData.CurrentHistoryMeta = handles.CurrentHistoryMeta;
                passData.PreviousResurrectionColor = handles.PreviousResurrectionColor;
                passData.CurrentResurrectionColor = handles.CurrentResurrectionColor;
                passData.PreviousResurrectionMeta = handles.PreviousResurrectionMeta;
                passData.CurrentResurrectionMeta = handles.CurrentResurrectionMeta;
                passData.ResolveOutput = resolveOutput;
                passData.RenderSize = renderSize;
                passData.OutputSize = outputSize;
                passData.PreviousOutputSize = cameraState.PreviousOutputSize;
                passData.Jitter = currentJitter;
                passData.PreviousJitter = previousJitter;
                passData.HasHistory = !resetHistory;
                passData.ResetHistory = resetHistory;
                passData.HistorySampleCount = Mathf.Clamp(historySampleCount, 8, 32);
                passData.EnableSharpening = enableSharpening;
                passData.Sharpness = additionalData != null ? additionalData.tsrSharpness : 0.2f;
                passData.EnableWaveOps = SupportsWaveOps();

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.UseTexture(passData.Output, AccessFlags.WriteAll);
                builder.UseTexture(passData.DilatedMotion, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DilatedDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DepthError, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectionBoundary, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ThinGeometryCoverage, AccessFlags.ReadWrite);
                builder.UseTexture(passData.LumaInstability, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedHistoryColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedHistoryMeta, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedResurrectionColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedResurrectionMeta, AccessFlags.ReadWrite);
                builder.UseTexture(passData.AcceptedHistoryColor, AccessFlags.ReadWrite);
                if (enablePairedGuides)
                {
                    builder.UseTexture(passData.InputShadingGuide, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.HistoryShadingGuide, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.GuideMetadata, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.GuideConfidence, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.PreviousShadingGuide, resetHistory ? AccessFlags.ReadWrite : AccessFlags.Read);
                    builder.UseTexture(passData.CurrentShadingGuide, AccessFlags.ReadWrite);
                }
                builder.UseTexture(passData.RejectionMask, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SpatialAntiAliasedColor, AccessFlags.ReadWrite);
                var previousAccess = passData.ResetHistory
                    ? AccessFlags.ReadWrite
                    : AccessFlags.Read;
                builder.UseTexture(passData.PreviousHistoryColor, previousAccess);
                builder.UseTexture(passData.CurrentHistoryColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousHistoryMeta, previousAccess);
                builder.UseTexture(passData.CurrentHistoryMeta, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousResurrectionColor, previousAccess);
                builder.UseTexture(passData.CurrentResurrectionColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousResurrectionMeta, previousAccess);
                builder.UseTexture(passData.CurrentResurrectionMeta, AccessFlags.ReadWrite);
                if (passData.EnableSharpening)
                    builder.UseTexture(passData.ResolveOutput, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    using var recordScope = s_RecordMarker.Auto();
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Execute(cmd, data);
                });
            }

            cameraState.CommitFrame(
                renderSize,
                outputSize,
                currentJitter);

            outputTexture.desc = outputDescriptor;
            outputTexture.innerHandle = outputHandle;
            if (textureCache != null)
                textureCache[outputTexture] = outputHandle;
            return true;
        }

        public void Dispose()
        {
            using (s_DisposeMarker.Auto())
            {
                foreach (var state in m_CameraStates.Values)
                    state.Dispose();

                m_CameraStates.Clear();
                m_ExpiredCameraIds.Clear();
            }
        }

        private CameraState GetOrCreateCameraState(Camera camera, int frameIndex)
        {
            var cameraId = camera.GetEntityId();
            if (!m_CameraStates.TryGetValue(cameraId, out var state))
            {
                state = new CameraState();
                m_CameraStates.Add(cameraId, state);
            }

            state.LastUsedFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
            return state;
        }

        private void CleanupExpiredCameraStates(int frameIndex)
        {
            var currentFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
            m_ExpiredCameraIds.Clear();
            foreach (var pair in m_CameraStates)
            {
                if (currentFrame - pair.Value.LastUsedFrame > CameraStateExpirationFrames)
                    m_ExpiredCameraIds.Add(pair.Key);
            }

            foreach (var cameraId in m_ExpiredCameraIds)
            {
                if (m_CameraStates.TryGetValue(cameraId, out var state))
                    state.Dispose();

                m_CameraStates.Remove(cameraId);
            }
        }

        private static bool TryResolveShaderSet(VividRPCoreResources resources, out ShaderSet shaders)
        {
            if (resources == null)
            {
                shaders = default;
                return false;
            }

            shaders = new ShaderSet(resources);
            return shaders.IsValid;
        }

        private static void Execute(CommandBuffer cmd, PassData data)
        {
            if (cmd == null || !data.Shaders.IsValid)
                return;

            if (data.ResetHistory)
                data.State.ClearHistory(cmd);

            DispatchDilateVelocity(cmd, data);
            DispatchReprojectHistory(cmd, data);
            if (data.EnablePairedGuides)
            {
                DispatchBuildShadingGuides(cmd, data);
                DispatchPropagateShadingConfidence(cmd, data);
            }
            DispatchRejectShading(cmd, data);
            DispatchSpatialAntiAliasing(cmd, data);
            DispatchUpdateHistory(cmd, data);
            DispatchResolveHistory(cmd, data);

            if (data.EnableSharpening)
                DispatchSharpen(cmd, data);

#if UNITY_EDITOR
            EditorTemporalCapture?.Invoke(cmd, data.Camera, data.FrameIndex,
                data.Source.ResolveTexture(), data.Output.ResolveTexture(), data.ResolveOutput.ResolveTexture(),
                data.RejectionMask.ResolveTexture(), data.CurrentHistoryMeta.ResolveTexture(), data.DilatedMotion.ResolveTexture());
            EditorHistoryCapture?.Invoke(cmd, data.Camera, data.FrameIndex,
                data.ReprojectedHistoryColor.ResolveTexture(), data.AcceptedHistoryColor.ResolveTexture(),
                data.SpatialAntiAliasedColor.ResolveTexture(), data.ReprojectedResurrectionColor.ResolveTexture(),
                data.ReprojectedHistoryMeta.ResolveTexture(), data.DepthError.ResolveTexture());
            if (data.EnablePairedGuides)
            {
                EditorShadingGuideCapture?.Invoke(cmd, data.Camera, data.FrameIndex,
                    data.InputShadingGuide.ResolveTexture(), data.HistoryShadingGuide.ResolveTexture());
                EditorGuideConfidenceCapture?.Invoke(cmd, data.Camera, data.FrameIndex,
                    data.GuideMetadata.ResolveTexture(), data.GuideConfidence.ResolveTexture(), data.CurrentShadingGuide.ResolveTexture());
            }
            if (data.CaptureHistoryLoss)
                EditorHistoryLossCapture?.Invoke(cmd, data.Camera, data.FrameIndex,
                    data.RejectDiagnostics.ResolveTexture(), data.UpdateDiagnostics.ResolveTexture(),
                    data.CurrentHistoryColor.ResolveTexture());
#endif
            data.State.MarkHistoryWritten();
        }

        private static void DispatchDilateVelocity(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.DilateVelocity;
            var kernel = data.Shaders.DilateVelocityKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, InputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, InputMotionVectorsId, data.MotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, HistoryColorId, data.PreviousHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize), DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchReprojectHistory(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.ReprojectHistory;
            var kernel = data.Shaders.ReprojectHistoryKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, HistoryColorId, data.PreviousHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, HistoryMetaId, data.PreviousHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ResurrectionColorId, data.PreviousResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, ResurrectionMetaId, data.PreviousResurrectionMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionColorId, data.ReprojectedResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionMetaId, data.ReprojectedResurrectionMeta);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchBuildShadingGuides(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_ShadingGuideSampler);
            var shader = data.Shaders.RejectShading;
            var kernel = data.Shaders.BuildShadingGuidesKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            BindGuideHistory(cmd, data, shader, kernel);
            cmd.SetComputeTextureParam(shader, kernel, OutputShadingGuideMetadataId, data.GuideMetadata);
            cmd.SetComputeTextureParam(shader, kernel, OutputInputShadingGuideId, data.InputShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, OutputHistoryShadingGuideId, data.HistoryShadingGuide);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize),
                DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void BindGuideHistory(CommandBuffer cmd, PassData data, ComputeShader shader, int kernel)
        {
            cmd.SetComputeTextureParam(shader, kernel, PreviousShadingGuideId, data.PreviousShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, InputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
        }

        private static void DispatchPropagateShadingConfidence(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_GuideConfidenceSampler);
            var shader = data.Shaders.RejectShading;
            var kernel = data.Shaders.PropagateShadingConfidenceKernel;
            SetCommonConstants(cmd, shader, data);
            BindGuideHistory(cmd, data, shader, kernel);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, InputShadingGuideId, data.InputShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, HistoryShadingGuideId, data.HistoryShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, ShadingGuideMetadataId, data.GuideMetadata);
            cmd.SetComputeTextureParam(shader, kernel, OutputShadingGuideConfidenceId, data.GuideConfidence);
            cmd.SetComputeTextureParam(shader, kernel, CurrentShadingGuideId, data.CurrentShadingGuide);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize),
                DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchRejectShading(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.RejectShading;
            var kernel = data.Shaders.RejectShadingKernel;
#if UNITY_EDITOR
            if (data.CaptureHistoryLoss)
            {
                kernel = data.Shaders.RejectDiagnosticsKernel;
                cmd.SetComputeTextureParam(shader, kernel, HistoryLossDiagnosticsId, data.RejectDiagnostics);
            }
#endif
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, InputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            if (data.EnablePairedGuides)
            {
                cmd.SetComputeTextureParam(shader, kernel, InputShadingGuideId, data.InputShadingGuide);
                cmd.SetComputeTextureParam(shader, kernel, HistoryShadingGuideId, data.HistoryShadingGuide);
                cmd.SetComputeTextureParam(shader, kernel, ShadingGuideConfidenceId, data.GuideConfidence);
            }
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionColorId, data.ReprojectedResurrectionColor);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchUpdateHistory(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.UpdateHistory;
            var kernel = data.Shaders.UpdateHistoryKernel;
#if UNITY_EDITOR
            if (data.CaptureHistoryLoss)
            {
                kernel = data.Shaders.UpdateDiagnosticsKernel;
                cmd.SetComputeTextureParam(shader, kernel, HistoryLossDiagnosticsId, data.UpdateDiagnostics);
            }
#endif
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, CurrentFrameColorId, data.SpatialAntiAliasedColor);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionColorId, data.ReprojectedResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionMetaId, data.ReprojectedResurrectionMeta);
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedHistoryColorId, data.CurrentHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedHistoryMetaId, data.CurrentHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedResurrectionColorId, data.CurrentResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedResurrectionMetaId, data.CurrentResurrectionMeta);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchSpatialAntiAliasing(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.SpatialAntiAliasing;
            var kernel = data.Shaders.SpatialAntiAliasingKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, SpatialAntiAliasedColorId, data.SpatialAntiAliasedColor);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchResolveHistory(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.ResolveHistory;
            var kernel = data.Shaders.ResolveHistoryKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, HistoryColorId, data.CurrentHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ResolvedOutputId, data.ResolveOutput);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchSharpen(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.Sharpen;
            var kernel = data.Shaders.SharpenKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, SharpenInputId, data.ResolveOutput);
            cmd.SetComputeTextureParam(shader, kernel, OutputColorId, data.Output);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void SetCommonConstants(CommandBuffer cmd, ComputeShader shader, PassData data)
        {
            SetKeyword(cmd, shader, TsrWaveOpsKeyword, data.EnableWaveOps);
            SetKeyword(cmd, shader, TsrPairedGuidesKeyword, data.EnablePairedGuides);
            cmd.SetComputeVectorParam(
                shader,
                RenderSizeId,
                new Vector4(
                    data.RenderSize.x,
                    data.RenderSize.y,
                    1.0f / Mathf.Max(1, data.RenderSize.x),
                    1.0f / Mathf.Max(1, data.RenderSize.y)));
            cmd.SetComputeVectorParam(
                shader,
                OutputSizeId,
                new Vector4(
                    data.OutputSize.x,
                    data.OutputSize.y,
                    1.0f / Mathf.Max(1, data.OutputSize.x),
                    1.0f / Mathf.Max(1, data.OutputSize.y)));
            cmd.SetComputeVectorParam(
                shader,
                PreviousOutputSizeId,
                new Vector4(
                    data.PreviousOutputSize.x,
                    data.PreviousOutputSize.y,
                    1.0f / Mathf.Max(1, data.PreviousOutputSize.x),
                    1.0f / Mathf.Max(1, data.PreviousOutputSize.y)));
            cmd.SetComputeVectorParam(
                shader,
                JitterId,
                new Vector4(data.Jitter.x, data.Jitter.y, data.PreviousJitter.x, data.PreviousJitter.y));
            cmd.SetComputeVectorParam(
                shader,
                TSRParamsId,
                new Vector4(
                    data.HasHistory ? 1.0f : 0.0f,
                    data.HistorySampleCount,
                    Mathf.Clamp01(data.Sharpness),
                    data.EnableSharpening ? 1.0f : 0.0f));
            cmd.SetComputeVectorParam(shader, TSRRejectionParamsId, new Vector4(0.003f, 16.0f, 0.28f, 0.35f));
        }

        private static bool SupportsWaveOps()
        {
            if (!SystemInfo.supportsComputeShaders)
                return false;

            var deviceType = SystemInfo.graphicsDeviceType;
            return deviceType == GraphicsDeviceType.Direct3D11
                || deviceType == GraphicsDeviceType.Direct3D12
                || deviceType == GraphicsDeviceType.Vulkan
                || deviceType == GraphicsDeviceType.Metal;
        }

        private static void SetKeyword(CommandBuffer cmd, ComputeShader shader, string keywordName, bool enabled)
        {
            if (cmd == null || shader == null)
                return;

            var keyword = new LocalKeyword(shader, keywordName);
            if (!keyword.isValid)
                return;

            cmd.SetKeyword(shader, keyword, enabled);
        }

        private static Vector2Int ResolveRenderSize(
            Vector2Int requestedRenderSize,
            RenderGraphTexture sourceTexture,
            VividCameraData cameraData)
        {
            if (requestedRenderSize.x > 0 && requestedRenderSize.y > 0)
                return requestedRenderSize;

            var descriptor = sourceTexture?.desc;
            if (descriptor != null && descriptor.Width > 0 && descriptor.Height > 0)
                return new Vector2Int(descriptor.Width, descriptor.Height);

            return new Vector2Int(
                CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width),
                CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height));
        }

        private static Vector2Int ResolveOutputSize(
            Vector2Int requestedOutputSize,
            RenderGraphTexture outputTexture,
            VividCameraData cameraData,
            Vector2Int renderSize)
        {
            if (requestedOutputSize.x > 0 && requestedOutputSize.y > 0)
                return requestedOutputSize;

            var descriptor = outputTexture?.desc;
            if (descriptor != null && descriptor.Width > 0 && descriptor.Height > 0)
                return new Vector2Int(descriptor.Width, descriptor.Height);

            var outputWidth = cameraData.pixelWidth > 0 ? cameraData.pixelWidth : renderSize.x;
            var outputHeight = cameraData.pixelHeight > 0 ? cameraData.pixelHeight : renderSize.y;
            return new Vector2Int(Mathf.Max(1, outputWidth), Mathf.Max(1, outputHeight));
        }

        internal static RenderGraphTextureDesc ConfigureOutputDescriptor(
            RenderGraphTextureDesc descriptor,
            RenderGraphTextureDesc sourceDescriptor,
            Vector2Int outputSize,
            string name = "TSROutput")
        {
            if (descriptor == null)
                return null;

            if (sourceDescriptor != null)
            {
                sourceDescriptor.Copy(descriptor);
            }
            else
            {
                ConfigureColorDescriptor(
                    descriptor,
                    name,
                    Mathf.Max(1, outputSize.x),
                    Mathf.Max(1, outputSize.y),
                    GraphicsFormat.R16G16B16A16_SFloat);
            }

            var colorFormat = descriptor.ColorFormat != GraphicsFormat.None
                ? descriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            if (!SystemInfo.IsFormatSupported(colorFormat, GraphicsFormatUsage.LoadStore))
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            descriptor.Name = name;
            descriptor.Width = Mathf.Max(1, outputSize.x);
            descriptor.Height = Mathf.Max(1, outputSize.y);
            descriptor.ColorFormat = colorFormat;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Bilinear;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.ClearBuffer = false;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            descriptor.EnableRandomWrite = true;
            descriptor.BindTextureMS = false;
            descriptor.Dimension = descriptor.Dimension == TextureDimension.None
                ? TextureDimension.Tex2D
                : descriptor.Dimension;
            descriptor.Slices = Mathf.Max(1, descriptor.Slices);
            return descriptor;
        }

        internal static RenderGraphTextureDesc ConfigureColorDescriptor(
            RenderGraphTextureDesc descriptor,
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            if (descriptor == null)
                return null;

            descriptor.Name = name;
            descriptor.Width = Mathf.Max(1, width);
            descriptor.Height = Mathf.Max(1, height);
            descriptor.Slices = 1;
            descriptor.Dimension = TextureDimension.Tex2D;
            descriptor.ColorFormat = format;
            descriptor.DepthBufferBits = DepthBits.None;
            descriptor.MsaaSamples = MSAASamples.None;
            descriptor.FilterMode = FilterMode.Bilinear;
            descriptor.WrapMode = TextureWrapMode.Clamp;
            descriptor.AnisoLevel = 1;
            descriptor.MipMapBias = 0f;
            descriptor.ClearBuffer = false;
            descriptor.ClearColor = Color.clear;
            descriptor.IsShadowMap = false;
            descriptor.EnableRandomWrite = true;
            descriptor.BindTextureMS = false;
            descriptor.UseDynamicScale = false;
            descriptor.UseDynamicScaleExplicit = false;
            descriptor.ScaleFactor = Vector2.one;
            descriptor.UseMipMap = false;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = 1;
            return descriptor;
        }

        private static int DivRoundUp(int value, int divisor)
        {
            return (Mathf.Max(1, value) + divisor - 1) / divisor;
        }

        private static Vector2 ResolveCurrentJitter(VividCameraData cameraData, CameraTemporalData temporalData)
        {
            if (temporalData != null)
                return temporalData.Jitter;

            return cameraData != null
                ? cameraData.GetJitter()
                : Vector2.zero;
        }

        private static Vector2 ResolvePreviousJitter(CameraState cameraState, CameraTemporalData temporalData)
        {
            if (temporalData != null)
                return temporalData.PreviousJitter;

            return cameraState != null
                ? cameraState.PreviousJitter
                : Vector2.zero;
        }

        internal readonly struct ImportedHandles
        {
            public ImportedHandles(
                TextureHandle previousHistoryColor,
                TextureHandle currentHistoryColor,
                TextureHandle previousHistoryMeta,
                TextureHandle currentHistoryMeta,
                TextureHandle previousResurrectionColor,
                TextureHandle currentResurrectionColor,
                TextureHandle previousResurrectionMeta,
                TextureHandle currentResurrectionMeta,
                TextureHandle previousShadingGuide,
                TextureHandle currentShadingGuide)
            {
                PreviousHistoryColor = previousHistoryColor;
                CurrentHistoryColor = currentHistoryColor;
                PreviousHistoryMeta = previousHistoryMeta;
                CurrentHistoryMeta = currentHistoryMeta;
                PreviousResurrectionColor = previousResurrectionColor;
                CurrentResurrectionColor = currentResurrectionColor;
                PreviousResurrectionMeta = previousResurrectionMeta;
                CurrentResurrectionMeta = currentResurrectionMeta;
                PreviousShadingGuide = previousShadingGuide;
                CurrentShadingGuide = currentShadingGuide;
            }

            public TextureHandle PreviousHistoryColor { get; }
            public TextureHandle CurrentHistoryColor { get; }
            public TextureHandle PreviousHistoryMeta { get; }
            public TextureHandle CurrentHistoryMeta { get; }
            public TextureHandle PreviousResurrectionColor { get; }
            public TextureHandle CurrentResurrectionColor { get; }
            public TextureHandle PreviousResurrectionMeta { get; }
            public TextureHandle CurrentResurrectionMeta { get; }
            public TextureHandle PreviousShadingGuide { get; }
            public TextureHandle CurrentShadingGuide { get; }
        }

        private readonly struct ShaderSet
        {
            public readonly ComputeShader DilateVelocity;
            public readonly ComputeShader ReprojectHistory;
            public readonly ComputeShader RejectShading;
            public readonly ComputeShader SpatialAntiAliasing;
            public readonly ComputeShader UpdateHistory;
            public readonly ComputeShader ResolveHistory;
            public readonly ComputeShader Sharpen;
            public readonly int DilateVelocityKernel;
            public readonly int ReprojectHistoryKernel;
            public readonly int RejectShadingKernel;
            public readonly int BuildShadingGuidesKernel;
            public readonly int PropagateShadingConfidenceKernel;
            public readonly int SpatialAntiAliasingKernel;
            public readonly int UpdateHistoryKernel;
            public readonly int ResolveHistoryKernel;
            public readonly int SharpenKernel;
#if UNITY_EDITOR
            public readonly int RejectDiagnosticsKernel;
            public readonly int UpdateDiagnosticsKernel;
#endif

            public ShaderSet(VividRPCoreResources resources)
            {
                DilateVelocity = resources.TSRDilateVelocityCompute;
                ReprojectHistory = resources.TSRReprojectHistoryCompute;
                RejectShading = resources.TSRRejectShadingCompute;
                SpatialAntiAliasing = resources.TSRSpatialAntiAliasingCompute;
                UpdateHistory = resources.TSRUpdateHistoryCompute;
                ResolveHistory = resources.TSRResolveHistoryCompute;
                Sharpen = resources.TSRSharpenCompute;
                DilateVelocityKernel = FindKernel(DilateVelocity);
                ReprojectHistoryKernel = FindKernel(ReprojectHistory);
                RejectShadingKernel = FindKernel(RejectShading);
                BuildShadingGuidesKernel = RejectShading != null && RejectShading.HasKernel("CSBuildShadingGuides")
                    ? RejectShading.FindKernel("CSBuildShadingGuides") : -1;
                PropagateShadingConfidenceKernel = RejectShading != null && RejectShading.HasKernel("CSPropagateShadingConfidence")
                    ? RejectShading.FindKernel("CSPropagateShadingConfidence") : -1;
                SpatialAntiAliasingKernel = FindKernel(SpatialAntiAliasing);
                UpdateHistoryKernel = FindKernel(UpdateHistory);
                ResolveHistoryKernel = FindKernel(ResolveHistory);
                SharpenKernel = FindKernel(Sharpen);
#if UNITY_EDITOR
                RejectDiagnosticsKernel = RejectShading != null && RejectShading.HasKernel("CSHistoryDiagnostics")
                    ? RejectShading.FindKernel("CSHistoryDiagnostics") : -1;
                UpdateDiagnosticsKernel = UpdateHistory != null && UpdateHistory.HasKernel("CSHistoryDiagnostics")
                    ? UpdateHistory.FindKernel("CSHistoryDiagnostics") : -1;
#endif
            }

            public bool IsValid =>
                DilateVelocity != null && DilateVelocityKernel >= 0
                && ReprojectHistory != null && ReprojectHistoryKernel >= 0
                && RejectShading != null && RejectShadingKernel >= 0
                && BuildShadingGuidesKernel >= 0
                && PropagateShadingConfidenceKernel >= 0
                && SpatialAntiAliasing != null && SpatialAntiAliasingKernel >= 0
                && UpdateHistory != null && UpdateHistoryKernel >= 0
                && ResolveHistory != null && ResolveHistoryKernel >= 0
                && Sharpen != null && SharpenKernel >= 0;

            private static int FindKernel(ComputeShader shader)
            {
                if (shader == null)
                    return -1;

                try
                {
                    return shader.FindKernel("CS");
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }

        private sealed class PassData
        {
#if UNITY_EDITOR
            public Camera Camera;
            public int FrameIndex;
            public bool CaptureHistoryLoss;
            public TextureHandle RejectDiagnostics;
            public TextureHandle UpdateDiagnostics;
#endif
            public CameraState State;
            public ShaderSet Shaders;
            public TextureHandle Source;
            public TextureHandle Depth;
            public TextureHandle MotionVectors;
            public TextureHandle Output;
            public TextureHandle DilatedMotion;
            public TextureHandle DilatedDepth;
            public TextureHandle DepthError;
            public TextureHandle ReprojectionBoundary;
            public TextureHandle ThinGeometryCoverage;
            public TextureHandle LumaInstability;
            public TextureHandle ReprojectedHistoryColor;
            public TextureHandle ReprojectedHistoryMeta;
            public TextureHandle ReprojectedResurrectionColor;
            public TextureHandle ReprojectedResurrectionMeta;
            public TextureHandle AcceptedHistoryColor;
            public TextureHandle InputShadingGuide;
            public TextureHandle HistoryShadingGuide;
            public bool EnablePairedGuides;
            public TextureHandle GuideMetadata;
            public TextureHandle GuideConfidence;
            public TextureHandle PreviousShadingGuide;
            public TextureHandle CurrentShadingGuide;
            public TextureHandle RejectionMask;
            public TextureHandle SpatialAntiAliasedColor;
            public TextureHandle PreviousHistoryColor;
            public TextureHandle CurrentHistoryColor;
            public TextureHandle PreviousHistoryMeta;
            public TextureHandle CurrentHistoryMeta;
            public TextureHandle PreviousResurrectionColor;
            public TextureHandle CurrentResurrectionColor;
            public TextureHandle PreviousResurrectionMeta;
            public TextureHandle CurrentResurrectionMeta;
            public TextureHandle ResolveOutput;
            public Vector2Int RenderSize;
            public Vector2Int OutputSize;
            public Vector2Int PreviousOutputSize;
            public Vector2 Jitter;
            public Vector2 PreviousJitter;
            public bool HasHistory;
            public bool ResetHistory;
            public int HistorySampleCount;
            public bool EnableSharpening;
            public bool EnableWaveOps;
            public float Sharpness;
        }

        internal sealed class CameraState : IDisposable
        {
            private CameraHistoryTexture m_HistoryColor;
            private CameraHistoryTexture m_HistoryMeta;
            private CameraHistoryTexture m_ResurrectionColor;
            private CameraHistoryTexture m_ResurrectionMeta;
            private CameraHistoryTexture m_ShadingGuide;
            private Vector2Int m_RenderSize;
            private Vector2Int m_OutputSize;
            private VividTsrQualityMode m_Quality;
            private int m_HistorySampleCount;
            private bool m_HasValidHistory;
            private bool m_PairedGuides;

            public int LastUsedFrame { get; set; }
            public Vector2Int PreviousRenderSize { get; private set; } = Vector2Int.one;
            public Vector2Int PreviousOutputSize { get; private set; } = Vector2Int.one;
            public Vector2 PreviousJitter { get; private set; }

            public bool Prepare(
                Camera camera,
                Vector2Int renderSize,
                Vector2Int outputSize,
                VividTsrQualityMode quality,
                int historySampleCount,
                int frameIndex,
                bool forceResetHistory,
                bool pairedGuides = false)
            {
                historySampleCount = Mathf.Clamp(historySampleCount, 8, 32);
                EnsureTextures(camera, outputSize);
                if (pairedGuides)
                    m_ShadingGuide = camera.GetVividCameraHistory().GetOrCreateTexture(
                        CameraHistoryIds.TsrShadingGuide, 2,
                        CreateHistoryDescriptor(renderSize, GraphicsFormat.R16G16B16A16_SFloat));
                var historyResourcesValid = m_HistoryColor.IsValid()
                    && m_HistoryMeta.IsValid()
                    && m_ResurrectionColor.IsValid()
                    && m_ResurrectionMeta.IsValid()
                    && (!pairedGuides || m_ShadingGuide.IsValid());
                var resetHistory = forceResetHistory
                    || !m_HasValidHistory
                    || !historyResourcesValid
                    || m_RenderSize != renderSize
                    || m_OutputSize != outputSize
                    || m_Quality != quality
                    || m_PairedGuides != pairedGuides
                    || m_HistorySampleCount != historySampleCount;

                if (resetHistory)
                {
                    PreviousRenderSize = renderSize;
                    PreviousOutputSize = outputSize;
                    PreviousJitter = Vector2.zero;
                }

                m_RenderSize = renderSize;
                m_OutputSize = outputSize;
                m_Quality = quality;
                m_PairedGuides = pairedGuides;
                m_HistorySampleCount = historySampleCount;
                m_HasValidHistory = true;
                LastUsedFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
                return resetHistory;
            }

            internal ImportedHandles Import(RenderGraph renderGraph)
            {
                return new ImportedHandles(
                    renderGraph.ImportTexture(m_HistoryColor.GetPrevious()),
                    renderGraph.ImportTexture(m_HistoryColor.GetCurrent()),
                    renderGraph.ImportTexture(m_HistoryMeta.GetPrevious()),
                    renderGraph.ImportTexture(m_HistoryMeta.GetCurrent()),
                    renderGraph.ImportTexture(m_ResurrectionColor.GetPrevious()),
                    renderGraph.ImportTexture(m_ResurrectionColor.GetCurrent()),
                    renderGraph.ImportTexture(m_ResurrectionMeta.GetPrevious()),
                    renderGraph.ImportTexture(m_ResurrectionMeta.GetCurrent()),
                    m_PairedGuides ? renderGraph.ImportTexture(m_ShadingGuide.GetPrevious()) : default,
                    m_PairedGuides ? renderGraph.ImportTexture(m_ShadingGuide.GetCurrent()) : default);
            }

            public void CommitFrame(Vector2Int renderSize, Vector2Int outputSize, Vector2 jitter)
            {
                PreviousRenderSize = renderSize;
                PreviousOutputSize = outputSize;
                PreviousJitter = jitter;
            }

            public void MarkHistoryWritten()
            {
                m_HistoryColor?.MarkWritten();
                m_HistoryMeta?.MarkWritten();
                m_ResurrectionColor?.MarkWritten();
                m_ResurrectionMeta?.MarkWritten();
                if (m_PairedGuides) m_ShadingGuide?.MarkWritten();
            }

            public void ClearHistory(CommandBuffer cmd)
            {
                if (cmd == null)
                    return;

                for (var i = 0; i < 2; i++)
                {
                    ClearRTHandle(cmd, m_HistoryColor.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_HistoryMeta.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_ResurrectionColor.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_ResurrectionMeta.GetFrame(i), Color.clear);
                    if (m_PairedGuides) ClearRTHandle(cmd, m_ShadingGuide.GetFrame(i), Color.clear);
                }
            }

            public void Dispose()
            {
                m_HistoryColor = null;
                m_HistoryMeta = null;
                m_ResurrectionColor = null;
                m_ResurrectionMeta = null;
                m_ShadingGuide = null;
                m_HasValidHistory = false;
            }

            private void EnsureTextures(Camera camera, Vector2Int outputSize)
            {
                var history = camera.GetVividCameraHistory();
                m_HistoryColor = history.GetOrCreateTexture(
                    CameraHistoryIds.TsrHistoryColor,
                    2,
                    CreateHistoryDescriptor(outputSize, GraphicsFormat.R16G16B16A16_SFloat));
                m_HistoryMeta = history.GetOrCreateTexture(
                    CameraHistoryIds.TsrHistoryMeta,
                    2,
                    CreateHistoryDescriptor(outputSize, GraphicsFormat.R16G16_SFloat));
                m_ResurrectionColor = history.GetOrCreateTexture(
                    CameraHistoryIds.TsrResurrectionColor,
                    2,
                    CreateHistoryDescriptor(outputSize, GraphicsFormat.R16G16B16A16_SFloat));
                m_ResurrectionMeta = history.GetOrCreateTexture(
                    CameraHistoryIds.TsrResurrectionMeta,
                    2,
                    CreateHistoryDescriptor(outputSize, GraphicsFormat.R16G16_SFloat));
            }

            private static CameraHistoryTextureDescriptor CreateHistoryDescriptor(
                Vector2Int size,
                GraphicsFormat format)
            {
                return new CameraHistoryTextureDescriptor(
                    size.x,
                    size.y,
                    format,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Clamp,
                    enableRandomWrite: true);
            }

            private static void ClearRTHandle(CommandBuffer cmd, RTHandle handle, Color clearColor)
            {
                if (handle == null)
                    return;

                CoreUtils.SetRenderTarget(cmd, handle);
                cmd.ClearRenderTarget(false, true, clearColor);
            }

        }
    }
}
