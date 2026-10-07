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
        internal static event Action<CommandBuffer, Camera, int, Vector4, Texture, Texture, Texture, Texture, Texture> EditorFlickeringCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture> EditorOcclusionCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture> EditorShadingGuideCapture;
        internal static event Action<CommandBuffer, Camera, int, Texture, Texture, Texture> EditorGuideConfidenceCapture;
        private static readonly int HistoryLossDiagnosticsId = Shader.PropertyToID("_HistoryLossDiagnostics");
        private readonly RenderGraphTextureDesc m_RejectDiagnosticsDescriptor = new();
        private readonly RenderGraphTextureDesc m_UpdateDiagnosticsDescriptor = new();
#endif
        private static readonly ProfilingSampler s_FlickerPrepareSampler = new("TSR.PrepareFlickering");
        private static readonly ProfilingSampler s_FlickerAnalyzeSampler = new("TSR.AnalyzeFlickering");
        private static readonly ProfilingSampler s_FlickerUpdateSampler = new("TSR.UpdateFlickering");
        private static readonly int PreviousFlickerHistoryId = Shader.PropertyToID("_PreviousFlickerHistory");
        private static readonly int FlickerPreviousDepthId = Shader.PropertyToID("_FlickerPreviousDepth");
        private static readonly int FlickerInputId = Shader.PropertyToID("_FlickerInput");
        private static readonly int ReprojectedFlickerHistoryId = Shader.PropertyToID("_ReprojectedFlickerHistory");
        private static readonly int FlickerGradientId = Shader.PropertyToID("_FlickerGradient");
        private static readonly int OutputFlickerInputId = Shader.PropertyToID("_OutputFlickerInput");
        private static readonly int OutputReprojectedFlickerHistoryId = Shader.PropertyToID("_OutputReprojectedFlickerHistory");
        private static readonly int OutputFlickerGradientId = Shader.PropertyToID("_OutputFlickerGradient");
        private static readonly int CurrentFlickerHistoryId = Shader.PropertyToID("_CurrentFlickerHistory");
        private static readonly int OutputFlickerErrorId = Shader.PropertyToID("_OutputFlickerError");
        private static readonly int FlickerParamsId = Shader.PropertyToID("_FlickerParams");
        private static readonly int FlickerClipToPrevClipId = Shader.PropertyToID("_FlickerClipToPrevClip");
        private static readonly int FlickerRotationalClipToPrevClipId = Shader.PropertyToID("_FlickerRotationalClipToPrevClip");
        private static readonly int FlickerInvViewProjectionId = Shader.PropertyToID("_FlickerInvViewProjection");
        private static readonly int FlickerPrevInvViewProjectionId = Shader.PropertyToID("_FlickerPrevInvViewProjection");
        private readonly RenderGraphTextureDesc m_FlickerInputDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectedFlickerHistoryDescriptor = new();
        private readonly RenderGraphTextureDesc m_FlickerGradientDescriptor = new();
        private const int CameraStateExpirationFrames = 400;
        private const int KernelThreadGroupSize = 8;
        private const string TsrWaveOpsKeyword = "VIVID_TSR_WAVE_OPS";
        private static readonly int FramePreExposureId = Shader.PropertyToID("_TSRFramePreExposure");
        private static readonly int PreviousPreExposureId = Shader.PropertyToID("_TSRPreviousPreExposure");
        private static readonly int OutputPreExposureId = Shader.PropertyToID("_TSROutputPreExposure");
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
        private static readonly ProfilingSampler s_OcclusionSampler = new("TSR.OcclusionReprojection");
        private static readonly int PreviousClosestOccluderId = Shader.PropertyToID("_PreviousClosestOccluder");
        private static readonly int ReprojectionValidityId = Shader.PropertyToID("_ReprojectionValidity");
        private static readonly int OutputReprojectionValidityId = Shader.PropertyToID("_OutputReprojectionValidity");
        private static readonly int OcclusionClipToPrevClipId = Shader.PropertyToID("_OcclusionClipToPrevClip");
        private static readonly int OcclusionDepthToViewId = Shader.PropertyToID("_OcclusionDepthToView");
        private static readonly int OcclusionPixelScaleId = Shader.PropertyToID("_OcclusionPixelScale");
        private readonly RenderGraphTextureDesc m_ClosestOccluderDescriptor = new();
        private readonly RenderGraphTextureDesc m_ReprojectionValidityDescriptor = new();
        private static readonly ProfilingSampler s_ResurrectionSampler = new("TSR.SelectResurrection");
        private static readonly ProfilingSampler s_PersistentStoreSampler = new("TSR.StorePersistentHistory");
        private static readonly int PersistentColorId = Shader.PropertyToID("_PersistentHistoryColor");
        private static readonly int PersistentMetaId = Shader.PropertyToID("_PersistentHistoryMeta");
        private static readonly int PersistentExposureId = Shader.PropertyToID("_PersistentPreExposure");
        private static readonly int ClipToPersistentId = Shader.PropertyToID("_ClipToPersistentClip");
        private static readonly int PersistentParamsId = Shader.PropertyToID("_PersistentParams");
        private static readonly int SnapshotHistoryColorId = Shader.PropertyToID("_SnapshotHistoryColor");
        private static readonly int SnapshotHistoryMetaId = Shader.PropertyToID("_SnapshotHistoryMeta");
        private static readonly int OutputPersistentColorId = Shader.PropertyToID("_OutputPersistentColor");
        private static readonly int OutputPersistentMetaId = Shader.PropertyToID("_OutputPersistentMeta");
        private static readonly int OutputPersistentExposureId = Shader.PropertyToID("_OutputPersistentExposure");
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
        private static readonly int HistoryWeightControlId = Shader.PropertyToID("_HistoryWeightControl");
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
        private readonly RenderGraphTextureDesc m_HistoryWeightControlDescriptor = new();
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
            bool forceResetHistory = false, GraphicsBuffer framePreExposure = null)
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
                    GraphicsFormat.R16_SFloat));
            var flickerInput = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_FlickerInputDescriptor, "TSR_FlickerInput", renderSize.x, renderSize.y, GraphicsFormat.R16G16B16A16_SFloat));
            var closestOccluder = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_ClosestOccluderDescriptor, "TSR_ClosestOccluder", renderSize.x, renderSize.y, GraphicsFormat.R32_UInt));
            var reprojectionValidity = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_ReprojectionValidityDescriptor, "TSR_ReprojectionValidity", renderSize.x, renderSize.y, GraphicsFormat.R16G16_SFloat));
            var reprojectedFlickerHistory = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_ReprojectedFlickerHistoryDescriptor, "TSR_ReprojectedFlickerHistory", renderSize.x, renderSize.y, GraphicsFormat.R16G16B16A16_SFloat));
            var flickerGradient = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_FlickerGradientDescriptor, "TSR_FlickerGradient", renderSize.x, renderSize.y, GraphicsFormat.R16G16B16A16_SFloat));
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
                    GraphicsFormat.R16G16B16A16_SFloat));
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
                m_GuideConfidenceDescriptor, "TSR_GuideConfidence", renderSize.x, renderSize.y, GraphicsFormat.R16G16B16A16_SFloat)) : default;
            var historyWeightControl = renderGraph.CreateTexture(ConfigureColorDescriptor(
                m_HistoryWeightControlDescriptor, "TSR_HistoryWeightControl", outputSize.x, outputSize.y,
                GraphicsFormat.R16G16B16A16_SFloat));
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
                passData.FramePreExposure = renderGraph.ImportBuffer(framePreExposure ?? VividAutoExposureSystem.GetOrCreateDefaultExposureBuffer());
                passData.PreviousPreExposure = handles.PreviousPreExposure;
                passData.CurrentPreExposure = handles.CurrentPreExposure;
                passData.Source = sourceTexture.innerHandle;
                passData.Depth = depthTexture.innerHandle;
                passData.MotionVectors = motionTexture.innerHandle;
                passData.Output = outputHandle;
                passData.DilatedMotion = dilatedMotion;
                passData.ClosestOccluder = closestOccluder;
                passData.ReprojectionValidity = reprojectionValidity;
                passData.DilatedDepth = dilatedDepth;
                passData.DepthError = depthError;
                passData.ReprojectionBoundary = reprojectionBoundary;
                passData.ThinGeometryCoverage = thinGeometryCoverage;
                passData.LumaInstability = lumaInstability;
                passData.FlickerInput = flickerInput;
                passData.ReprojectedFlickerHistory = reprojectedFlickerHistory;
                passData.FlickerGradient = flickerGradient;
                passData.PreviousFlickerHistory = handles.PreviousFlickerHistory;
                passData.CurrentFlickerHistory = handles.CurrentFlickerHistory;
                var currentVP = temporalData?.ViewProjection ?? cameraData.mainViewConstants.nonJitteredViewProjMatrix;
                var previousVP = temporalData?.PreviousViewProjection ?? cameraData.mainViewConstants.prevViewProjMatrix;
                var invCurrentVP = currentVP.inverse;
                passData.CurrentViewProjection = currentVP;
                int readSlot = cameraState.PersistentReadSlot;
                passData.PersistentStoreSlot = cameraState.PersistentStoreSlot;
                passData.CanResurrect = cameraState.CanResurrect;
                passData.ClipToPersistent = cameraState.PersistentViewProjection[readSlot] * invCurrentVP;
                var snapshotJitter = cameraState.PersistentJitter[readSlot] * 0.5f;
                if (SystemInfo.graphicsUVStartsAtTop) snapshotJitter.y = -snapshotJitter.y;
                passData.PersistentParams = new Vector4(passData.CanResurrect ? 1 : 0, snapshotJitter.x, snapshotJitter.y, 0);
                // Reset may clear both slots, even if only one is sampled/stored.
                for (int slot = 0; slot < 2; slot++)
                {
                    if (!resetHistory && slot != readSlot && slot != passData.PersistentStoreSlot) continue;
                    var c = renderGraph.ImportTexture(cameraState.PersistentColor[slot].GetCurrent());
                    var m = renderGraph.ImportTexture(cameraState.PersistentMeta[slot].GetCurrent());
                    var e = renderGraph.ImportTexture(cameraState.PersistentExposure[slot].GetCurrent());
                    var access = resetHistory || slot == passData.PersistentStoreSlot ? AccessFlags.ReadWrite : AccessFlags.Read;
                    builder.UseTexture(c, access); builder.UseTexture(m, access); builder.UseTexture(e, access);
                    if (slot == readSlot)
                    { passData.PersistentColor = c; passData.PersistentMeta = m; passData.PersistentExposure = e; }
                    if (slot == passData.PersistentStoreSlot)
                    { passData.StorePersistentColor = c; passData.StorePersistentMeta = m; passData.StorePersistentExposure = e; }
                }
                passData.FlickerInvViewProjection = invCurrentVP;
                passData.FlickerPrevInvViewProjection = previousVP.inverse;
                passData.FlickerClipToPrevClip = previousVP * invCurrentVP;
                var currentView = temporalData?.ViewMatrix ?? cameraData.mainViewConstants.viewMatrix;
                var previousView = temporalData?.PreviousViewMatrix ?? cameraData.mainViewConstants.prevViewMatrix;
                var currentProjection = currentVP * currentView.inverse;
                var previousProjection = previousVP * previousView.inverse;
                var rotation = previousView * currentView.inverse;
                rotation.SetColumn(3, new Vector4(0, 0, 0, 1));
                var previousInverseProjection = previousProjection.inverse;
                passData.OcclusionDepthToView = new Vector4(previousInverseProjection.m22, previousInverseProjection.m23,
                    previousInverseProjection.m32, previousInverseProjection.m33);
                passData.OcclusionPixelScale = new Vector4(Mathf.Abs(previousInverseProjection.m00) / renderSize.x,
                    Mathf.Abs(previousInverseProjection.m11) / renderSize.y, cameraData.camera.orthographic ? 1f : 0f, 0f);
                passData.FlickerRotationalClipToPrevClip = previousProjection * rotation * currentProjection.inverse;
                var rateRatio = cameraState.FlickerDeltaTime > 0 ? cameraState.FlickerDeltaTime * 60f : 1f;
                passData.FlickerParams = new Vector4(2f / Mathf.Max(rateRatio, 1f),
                    1f / Mathf.Max(rateRatio * 10f * renderSize.x / 1920f, 0.001f), cameraData.frameIndex & 7, 0);

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
                passData.HistoryWeightControl = historyWeightControl;
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

                builder.UseBuffer(passData.FramePreExposure, AccessFlags.Read);
                builder.UseTexture(passData.PreviousPreExposure, resetHistory ? AccessFlags.ReadWrite : AccessFlags.Read);
                builder.UseTexture(passData.CurrentPreExposure, AccessFlags.Write);
                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.UseTexture(passData.Output, AccessFlags.WriteAll);
                builder.UseTexture(passData.DilatedMotion, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ClosestOccluder, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectionValidity, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DilatedDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DepthError, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectionBoundary, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ThinGeometryCoverage, AccessFlags.ReadWrite);
                builder.UseTexture(passData.LumaInstability, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FlickerInput, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedFlickerHistory, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FlickerGradient, AccessFlags.ReadWrite);
                builder.UseTexture(passData.CurrentFlickerHistory, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousFlickerHistory, resetHistory ? AccessFlags.ReadWrite : AccessFlags.Read);
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
                builder.UseTexture(passData.HistoryWeightControl, AccessFlags.ReadWrite);
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
            DispatchOcclusionReprojection(cmd, data);
            DispatchReprojectHistory(cmd, data);
            DispatchFlickering(cmd, data);
            if (data.EnablePairedGuides)
            {
                DispatchBuildShadingGuides(cmd, data);
                DispatchPropagateShadingConfidence(cmd, data);
            }
            DispatchRejectShading(cmd, data);
            if (data.CanResurrect) DispatchSelectResurrection(cmd, data);
            DispatchSpatialAntiAliasing(cmd, data);
            DispatchUpdateHistory(cmd, data);
            if (data.PersistentStoreSlot >= 0) DispatchStorePersistentHistory(cmd, data);
            DispatchResolveHistory(cmd, data);

            if (data.EnableSharpening)
                DispatchSharpen(cmd, data);

#if UNITY_EDITOR
            EditorOcclusionCapture?.Invoke(cmd, data.Camera, data.FrameIndex, data.ReprojectionValidity.ResolveTexture(), data.ClosestOccluder.ResolveTexture());
            EditorFlickeringCapture?.Invoke(cmd, data.Camera, data.FrameIndex, data.FlickerParams,
                data.FlickerInput.ResolveTexture(), data.FlickerGradient.ResolveTexture(),
                data.CurrentFlickerHistory.ResolveTexture(), data.LumaInstability.ResolveTexture(), data.ReprojectedFlickerHistory.ResolveTexture());
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

        private static void BindPreExposure(CommandBuffer cmd, PassData data, ComputeShader shader, int kernel)
        {
            cmd.SetComputeBufferParam(shader, kernel, FramePreExposureId, data.FramePreExposure);
            cmd.SetComputeTextureParam(shader, kernel, PreviousPreExposureId, data.PreviousPreExposure);
        }

        private static void DispatchFlickering(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.RejectShading;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeVectorParam(shader, FlickerParamsId, data.FlickerParams);
            cmd.SetComputeMatrixParam(shader, FlickerClipToPrevClipId, data.FlickerClipToPrevClip);
            cmd.SetComputeMatrixParam(shader, FlickerRotationalClipToPrevClipId, data.FlickerRotationalClipToPrevClip);
            cmd.SetComputeMatrixParam(shader, FlickerInvViewProjectionId, data.FlickerInvViewProjection);
            cmd.SetComputeMatrixParam(shader, FlickerPrevInvViewProjectionId, data.FlickerPrevInvViewProjection);
            int kernel = data.Shaders.PrepareFlickerKernel;
            using (new ProfilingScope(cmd, s_FlickerPrepareSampler))
            {
                BindPreExposure(cmd, data, shader, kernel);
                cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
                cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
                cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
                cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
                cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
                cmd.SetComputeTextureParam(shader, kernel, FlickerPreviousDepthId, data.PreviousHistoryMeta);
                cmd.SetComputeTextureParam(shader, kernel, PreviousFlickerHistoryId, data.PreviousFlickerHistory);
                cmd.SetComputeTextureParam(shader, kernel, OutputFlickerInputId, data.FlickerInput);
                cmd.SetComputeTextureParam(shader, kernel, OutputReprojectedFlickerHistoryId, data.ReprojectedFlickerHistory);
                cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, 8), DivRoundUp(data.RenderSize.y, 8), 1);
            }
            kernel = data.Shaders.AnalyzeFlickerKernel;
            using (new ProfilingScope(cmd, s_FlickerAnalyzeSampler))
            {
                cmd.SetComputeTextureParam(shader, kernel, FlickerInputId, data.FlickerInput);
                cmd.SetComputeTextureParam(shader, kernel, ReprojectedFlickerHistoryId, data.ReprojectedFlickerHistory);
                cmd.SetComputeTextureParam(shader, kernel, OutputFlickerGradientId, data.FlickerGradient);
                cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, 8), DivRoundUp(data.RenderSize.y, 8), 1);
            }
            kernel = data.Shaders.UpdateFlickerKernel;
            using (new ProfilingScope(cmd, s_FlickerUpdateSampler))
            {
                cmd.SetComputeTextureParam(shader, kernel, FlickerInputId, data.FlickerInput);
                cmd.SetComputeTextureParam(shader, kernel, ReprojectedFlickerHistoryId, data.ReprojectedFlickerHistory);
                cmd.SetComputeTextureParam(shader, kernel, FlickerGradientId, data.FlickerGradient);
                cmd.SetComputeTextureParam(shader, kernel, CurrentFlickerHistoryId, data.CurrentFlickerHistory);
                cmd.SetComputeTextureParam(shader, kernel, OutputFlickerErrorId, data.LumaInstability);
                cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, 8), DivRoundUp(data.RenderSize.y, 8), 1);
            }
        }

        private static void DispatchDilateVelocity(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.DilateVelocity;
            var kernel = data.Shaders.DilateVelocityKernel;
            SetCommonConstants(cmd, shader, data);
            BindPreExposure(cmd, data, shader, kernel);
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

        private static void DispatchOcclusionReprojection(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_OcclusionSampler);
            var shader = data.Shaders.ReprojectHistory;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeMatrixParam(shader, OcclusionClipToPrevClipId, data.FlickerClipToPrevClip);
            cmd.SetComputeVectorParam(shader, OcclusionDepthToViewId, data.OcclusionDepthToView);
            cmd.SetComputeVectorParam(shader, OcclusionPixelScaleId, data.OcclusionPixelScale);
            int groupsX = DivRoundUp(data.RenderSize.x, KernelThreadGroupSize);
            int groupsY = DivRoundUp(data.RenderSize.y, KernelThreadGroupSize);
            int kernel = data.Shaders.ClearOccludersKernel;
            cmd.SetComputeTextureParam(shader, kernel, PreviousClosestOccluderId, data.ClosestOccluder);
            cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
            kernel = data.Shaders.ScatterOccludersKernel;
            cmd.SetComputeTextureParam(shader, kernel, PreviousClosestOccluderId, data.ClosestOccluder);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
            kernel = data.Shaders.ResolveOcclusionKernel;
            cmd.SetComputeTextureParam(shader, kernel, PreviousClosestOccluderId, data.ClosestOccluder);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, InputMotionVectorsId, data.MotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, OutputReprojectionValidityId, data.ReprojectionValidity);
            cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
        }

        private static void DispatchSelectResurrection(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_ResurrectionSampler);
            var shader = data.Shaders.RejectShading;
            int kernel = data.Shaders.SelectResurrectionKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionColorId, data.ReprojectedResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionMetaId, data.ReprojectedResurrectionMeta);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, HistoryWeightControlId, data.HistoryWeightControl);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
            if (data.EnablePairedGuides)
            {
                kernel = data.Shaders.UpdateResurrectedGuideKernel;
                cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
                cmd.SetComputeTextureParam(shader, kernel, ReprojectedResurrectionColorId, data.ReprojectedResurrectionColor);
                cmd.SetComputeTextureParam(shader, kernel, HistoryWeightControlId, data.HistoryWeightControl);
                cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
                cmd.SetComputeTextureParam(shader, kernel, CurrentShadingGuideId, data.CurrentShadingGuide);
                cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize), DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
            }
        }

        private static void DispatchStorePersistentHistory(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_PersistentStoreSampler);
            var shader = data.Shaders.ReprojectHistory;
            int kernel = data.Shaders.StorePersistentKernel;
            SetCommonConstants(cmd, shader, data);
            BindPreExposure(cmd, data, shader, kernel);
            cmd.SetComputeTextureParam(shader, kernel, SnapshotHistoryColorId, data.CurrentHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, SnapshotHistoryMetaId, data.CurrentHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
            cmd.SetComputeTextureParam(shader, kernel, OutputPersistentColorId, data.StorePersistentColor);
            cmd.SetComputeTextureParam(shader, kernel, OutputPersistentMetaId, data.StorePersistentMeta);
            cmd.SetComputeTextureParam(shader, kernel, OutputPersistentExposureId, data.StorePersistentExposure);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
            data.State.StorePersistentTransform(data.PersistentStoreSlot, data.CurrentViewProjection, data.Jitter);
        }

        private static void DispatchReprojectHistory(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.ReprojectHistory;
            var kernel = data.Shaders.ReprojectHistoryKernel;
            SetCommonConstants(cmd, shader, data);
            BindPreExposure(cmd, data, shader, kernel);
            cmd.SetComputeMatrixParam(shader, ClipToPersistentId, data.ClipToPersistent);
            cmd.SetComputeVectorParam(shader, PersistentParamsId, data.PersistentParams);
            cmd.SetComputeTextureParam(shader, kernel, PersistentColorId, data.PersistentColor);
            cmd.SetComputeTextureParam(shader, kernel, PersistentMetaId, data.PersistentMeta);
            cmd.SetComputeTextureParam(shader, kernel, PersistentExposureId, data.PersistentExposure);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, HistoryColorId, data.PreviousHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, HistoryMetaId, data.PreviousHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ResurrectionColorId, data.PreviousResurrectionColor);
            cmd.SetComputeTextureParam(shader, kernel, ResurrectionMetaId, data.PreviousResurrectionMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
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
            BindPreExposure(cmd, data, shader, kernel);
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
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
        }

        private static void DispatchPropagateShadingConfidence(CommandBuffer cmd, PassData data)
        {
            using var scope = new ProfilingScope(cmd, s_GuideConfidenceSampler);
            var shader = data.Shaders.RejectShading;
            var kernel = data.Shaders.PropagateShadingConfidenceKernel;
            SetCommonConstants(cmd, shader, data);
            BindPreExposure(cmd, data, shader, kernel);
            BindGuideHistory(cmd, data, shader, kernel);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, InputShadingGuideId, data.InputShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, HistoryShadingGuideId, data.HistoryShadingGuide);
            cmd.SetComputeTextureParam(shader, kernel, ShadingGuideMetadataId, data.GuideMetadata);
            cmd.SetComputeTextureParam(shader, kernel, OutputShadingGuideConfidenceId, data.GuideConfidence);
            cmd.SetComputeTextureParam(shader, kernel, LumaInstabilityId, data.LumaInstability);
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
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionValidityId, data.ReprojectionValidity);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            if (data.EnablePairedGuides)
            {
                cmd.SetComputeTextureParam(shader, kernel, InputShadingGuideId, data.InputShadingGuide);
                cmd.SetComputeTextureParam(shader, kernel, HistoryShadingGuideId, data.HistoryShadingGuide);
                cmd.SetComputeTextureParam(shader, kernel, ShadingGuideConfidenceId, data.GuideConfidence);
            }
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, HistoryWeightControlId, data.HistoryWeightControl);
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
            cmd.SetComputeBufferParam(shader, kernel, FramePreExposureId, data.FramePreExposure);
            cmd.SetComputeTextureParam(shader, kernel, OutputPreExposureId, data.CurrentPreExposure);
            cmd.SetComputeMatrixParam(shader, ClipToPersistentId, data.ClipToPersistent);
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
            cmd.SetComputeTextureParam(shader, kernel, HistoryWeightControlId, data.HistoryWeightControl);
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

            var keyword = shader.keywordSpace.FindKeyword(keywordName);
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
                TextureHandle currentShadingGuide, TextureHandle previousPreExposure, TextureHandle currentPreExposure,
                TextureHandle previousFlickerHistory, TextureHandle currentFlickerHistory)
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
                PreviousPreExposure = previousPreExposure;
                CurrentPreExposure = currentPreExposure;
                PreviousFlickerHistory = previousFlickerHistory;
                CurrentFlickerHistory = currentFlickerHistory;
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
            public TextureHandle PreviousPreExposure { get; }
            public TextureHandle CurrentPreExposure { get; }
            public TextureHandle PreviousFlickerHistory { get; }
            public TextureHandle CurrentFlickerHistory { get; }
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
            public readonly int StorePersistentKernel, SelectResurrectionKernel, UpdateResurrectedGuideKernel;
            public readonly int ReprojectHistoryKernel;
            public readonly int ClearOccludersKernel, ScatterOccludersKernel, ResolveOcclusionKernel;
            public readonly int RejectShadingKernel;
            public readonly int PrepareFlickerKernel, AnalyzeFlickerKernel, UpdateFlickerKernel;
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
                UpdateResurrectedGuideKernel = RejectShading != null && RejectShading.HasKernel("CSUpdateResurrectedGuide") ? RejectShading.FindKernel("CSUpdateResurrectedGuide") : -1;
                StorePersistentKernel = ReprojectHistory != null && ReprojectHistory.HasKernel("CSStorePersistentHistory") ? ReprojectHistory.FindKernel("CSStorePersistentHistory") : -1;
                SelectResurrectionKernel = RejectShading != null && RejectShading.HasKernel("CSSelectResurrection") ? RejectShading.FindKernel("CSSelectResurrection") : -1;
                ClearOccludersKernel = ReprojectHistory != null && ReprojectHistory.HasKernel("CSClearOccluders") ? ReprojectHistory.FindKernel("CSClearOccluders") : -1;
                ScatterOccludersKernel = ReprojectHistory != null && ReprojectHistory.HasKernel("CSScatterOccluders") ? ReprojectHistory.FindKernel("CSScatterOccluders") : -1;
                ResolveOcclusionKernel = ReprojectHistory != null && ReprojectHistory.HasKernel("CSResolveOcclusion") ? ReprojectHistory.FindKernel("CSResolveOcclusion") : -1;
                RejectShadingKernel = FindKernel(RejectShading);
                PrepareFlickerKernel = RejectShading != null && RejectShading.HasKernel("CSPrepareFlicker") ? RejectShading.FindKernel("CSPrepareFlicker") : -1;
                AnalyzeFlickerKernel = RejectShading != null && RejectShading.HasKernel("CSAnalyzeFlicker") ? RejectShading.FindKernel("CSAnalyzeFlicker") : -1;
                UpdateFlickerKernel = RejectShading != null && RejectShading.HasKernel("CSUpdateFlicker") ? RejectShading.FindKernel("CSUpdateFlicker") : -1;
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
                && StorePersistentKernel >= 0 && SelectResurrectionKernel >= 0 && UpdateResurrectedGuideKernel >= 0
                && ClearOccludersKernel >= 0 && ScatterOccludersKernel >= 0 && ResolveOcclusionKernel >= 0
                && RejectShading != null && RejectShadingKernel >= 0
                && PrepareFlickerKernel >= 0 && AnalyzeFlickerKernel >= 0 && UpdateFlickerKernel >= 0
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
            public BufferHandle FramePreExposure;
            public TextureHandle PreviousPreExposure;
            public TextureHandle CurrentPreExposure;
            public TextureHandle Source;
            public TextureHandle Depth;
            public TextureHandle MotionVectors;
            public TextureHandle Output;
            public TextureHandle DilatedMotion;
            public TextureHandle PersistentColor, PersistentMeta, PersistentExposure;
            public TextureHandle StorePersistentColor, StorePersistentMeta, StorePersistentExposure;
            public Matrix4x4 ClipToPersistent, CurrentViewProjection;
            public Vector4 PersistentParams;
            public bool CanResurrect;
            public int PersistentStoreSlot;
            public TextureHandle ClosestOccluder, ReprojectionValidity;
            public Vector4 OcclusionDepthToView, OcclusionPixelScale;
            public TextureHandle DilatedDepth;
            public TextureHandle DepthError;
            public TextureHandle ReprojectionBoundary;
            public TextureHandle ThinGeometryCoverage;
            public TextureHandle LumaInstability;
            public TextureHandle FlickerInput, ReprojectedFlickerHistory, FlickerGradient;
            public TextureHandle PreviousFlickerHistory, CurrentFlickerHistory;
            public Vector4 FlickerParams;
            public Matrix4x4 FlickerClipToPrevClip, FlickerRotationalClipToPrevClip;
            public Matrix4x4 FlickerInvViewProjection, FlickerPrevInvViewProjection;
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
            public TextureHandle HistoryWeightControl;
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
            // UE defaults: two persistent frames, 31-frame odd storage period.
            internal const int PersistentPeriod = 31;
            private static readonly CameraHistoryId[] s_PersistentColorIds = { CameraHistoryId.Create("TSRPersistentColor0"), CameraHistoryId.Create("TSRPersistentColor1") };
            private static readonly CameraHistoryId[] s_PersistentMetaIds = { CameraHistoryId.Create("TSRPersistentMeta0"), CameraHistoryId.Create("TSRPersistentMeta1") };
            private static readonly CameraHistoryId[] s_PersistentExposureIds = { CameraHistoryId.Create("TSRPersistentExposure0"), CameraHistoryId.Create("TSRPersistentExposure1") };
            internal readonly CameraHistoryTexture[] PersistentColor = new CameraHistoryTexture[2];
            internal readonly CameraHistoryTexture[] PersistentMeta = new CameraHistoryTexture[2];
            internal readonly CameraHistoryTexture[] PersistentExposure = new CameraHistoryTexture[2];
            internal readonly Matrix4x4[] PersistentViewProjection = new Matrix4x4[2];
            internal readonly Vector2[] PersistentJitter = new Vector2[2];
            private readonly bool[] m_PersistentValid = new bool[2];
            private int m_PersistentFrameCount;
            internal static int GetPersistentReadSlot(int completedFrames)
            {
                // FTSRHistorySliceSequence::GetResurrectionFrameRollingIndex,
                // specialized to two persistent slots plus two transient frames.
                if (completedFrames < 2 * PersistentPeriod) return 0;
                int last = (completedFrames - 1) % (2 * PersistentPeriod);
                return ((last + 2 * PersistentPeriod - 1) / PersistentPeriod) % 2;
            }
            internal int PersistentReadSlot => GetPersistentReadSlot(m_PersistentFrameCount);
            internal int PersistentStoreSlot => m_PersistentFrameCount % PersistentPeriod == 0
                ? (m_PersistentFrameCount / PersistentPeriod) % 2 : -1;
            internal bool CanResurrect => m_PersistentFrameCount > 1 && m_PersistentValid[PersistentReadSlot];
            internal void PreparePersistent(Camera camera, Vector2Int size, bool reset)
            {
                var history = camera.GetVividCameraHistory();
                for (int i = 0; i < 2; i++)
                {
                    var color = history.GetOrCreateTexture(s_PersistentColorIds[i], 1, CreateHistoryDescriptor(size, GraphicsFormat.R16G16B16A16_SFloat));
                    var meta = history.GetOrCreateTexture(s_PersistentMetaIds[i], 1, CreateHistoryDescriptor(size, GraphicsFormat.R16G16_SFloat));
                    var exposure = history.GetOrCreateTexture(s_PersistentExposureIds[i], 1, CreateHistoryDescriptor(Vector2Int.one, GraphicsFormat.R32_SFloat));
                    reset |= !ReferenceEquals(color, PersistentColor[i]) || !ReferenceEquals(meta, PersistentMeta[i]) || !ReferenceEquals(exposure, PersistentExposure[i]);
                    reset |= m_PersistentValid[i] && (!color.IsValid(0) || !meta.IsValid(0) || !exposure.IsValid(0));
                    PersistentColor[i] = color; PersistentMeta[i] = meta; PersistentExposure[i] = exposure;
                }
                if (reset) { m_PersistentFrameCount = 0; m_PersistentValid[0] = m_PersistentValid[1] = false; }
            }
            internal void StorePersistentTransform(int slot, Matrix4x4 viewProjection, Vector2 jitter)
            {
                PersistentViewProjection[slot] = viewProjection; PersistentJitter[slot] = jitter;
                m_PersistentValid[slot] = true;
            }
            private CameraHistoryTexture m_HistoryColor;
            private CameraHistoryTexture m_HistoryMeta;
            private CameraHistoryTexture m_ResurrectionColor;
            private CameraHistoryTexture m_ResurrectionMeta;
            private CameraHistoryTexture m_ShadingGuide;
            private CameraHistoryTexture m_PreExposure;
            private CameraHistoryTexture m_Flickering;
            private double m_LastFlickerTime;
            private int m_LastFlickerFrame = -1;
            public float FlickerDeltaTime { get; private set; }
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
                // UE uses real frame time. Unity's Time.unscaledDeltaTime can be
                // stale in paused/Edit Mode, so measure this camera's render cadence.
                int flickerFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
                if (m_LastFlickerFrame != flickerFrame)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    FlickerDeltaTime = m_LastFlickerFrame >= 0 ? (float)Math.Max(now - m_LastFlickerTime, 0.0) : 0f;
                    m_LastFlickerTime = now;
                    m_LastFlickerFrame = flickerFrame;
                }
                historySampleCount = Mathf.Clamp(historySampleCount, 8, 32);
                EnsureTextures(camera, outputSize);
                m_PreExposure = camera.GetVividCameraHistory().GetOrCreateTexture(
                    CameraHistoryIds.TsrPreExposure, 2,
                    CreateHistoryDescriptor(Vector2Int.one, GraphicsFormat.R32_SFloat));
                m_Flickering = camera.GetVividCameraHistory().GetOrCreateTexture(
                    CameraHistoryIds.TsrFlickering, 2,
                    CreateHistoryDescriptor(renderSize, GraphicsFormat.R8G8B8A8_UNorm));
                if (pairedGuides)
                    m_ShadingGuide = camera.GetVividCameraHistory().GetOrCreateTexture(
                        CameraHistoryIds.TsrShadingGuide, 2,
                        CreateHistoryDescriptor(renderSize, GraphicsFormat.R16G16B16A16_SFloat));
                var historyResourcesValid = m_HistoryColor.IsValid()
                    && m_HistoryMeta.IsValid()
                    && m_ResurrectionColor.IsValid()
                    && m_ResurrectionMeta.IsValid()
                    && m_PreExposure.IsValid()
                    && m_Flickering.IsValid()
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
                PreparePersistent(camera, outputSize, resetHistory);
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
                    m_PairedGuides ? renderGraph.ImportTexture(m_ShadingGuide.GetCurrent()) : default,
                    renderGraph.ImportTexture(m_PreExposure.GetPrevious()),
                    renderGraph.ImportTexture(m_PreExposure.GetCurrent()),
                    renderGraph.ImportTexture(m_Flickering.GetPrevious()),
                    renderGraph.ImportTexture(m_Flickering.GetCurrent()));
            }

            public void CommitFrame(Vector2Int renderSize, Vector2Int outputSize, Vector2 jitter)
            {
                PreviousRenderSize = renderSize;
                PreviousOutputSize = outputSize;
                PreviousJitter = jitter;
            }

            public void MarkHistoryWritten()
            {
                for (int i = 0; i < 2; i++)
                { PersistentColor[i].MarkWritten(); PersistentMeta[i].MarkWritten(); PersistentExposure[i].MarkWritten(); }
                // Retained single-frame surfaces remain valid while untouched.
                m_PersistentFrameCount++;
                if (m_PersistentFrameCount >= 4 * PersistentPeriod) m_PersistentFrameCount -= 2 * PersistentPeriod;
                m_PreExposure?.MarkWritten();
                m_Flickering?.MarkWritten();
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
                    ClearRTHandle(cmd, PersistentColor[i].GetCurrent(), Color.clear);
                    ClearRTHandle(cmd, PersistentMeta[i].GetCurrent(), Color.clear);
                    ClearRTHandle(cmd, PersistentExposure[i].GetCurrent(), Color.white);
                    ClearRTHandle(cmd, m_PreExposure.GetFrame(i), Color.white);
                    ClearRTHandle(cmd, m_Flickering.GetFrame(i), new Color(0, 127f / 255f, 0, 0));
                    ClearRTHandle(cmd, m_HistoryColor.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_HistoryMeta.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_ResurrectionColor.GetFrame(i), Color.clear);
                    ClearRTHandle(cmd, m_ResurrectionMeta.GetFrame(i), Color.clear);
                    if (m_PairedGuides) ClearRTHandle(cmd, m_ShadingGuide.GetFrame(i), Color.clear);
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < 2; i++)
                { PersistentColor[i] = null; PersistentMeta[i] = null; PersistentExposure[i] = null; m_PersistentValid[i] = false; }
                m_PersistentFrameCount = 0;
                m_HistoryColor = null;
                m_HistoryMeta = null;
                m_ResurrectionColor = null;
                m_ResurrectionMeta = null;
                m_ShadingGuide = null;
                m_PreExposure = null;
                m_Flickering = null;
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
