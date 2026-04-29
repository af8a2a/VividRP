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
        private const int CameraStateExpirationFrames = 400;
        private const int KernelThreadGroupSize = 8;

        private static readonly ProfilerMarker s_RecordGraphMarker =
            new("VividRP.RenderPass.RecordGraph/Temporal Super Resolution (Injected)");
        private static readonly ProfilerMarker s_RecordMarker =
            new("VividRP.RenderPass.Record/Temporal Super Resolution (Injected)");
        private static readonly ProfilerMarker s_DisposeMarker =
            new("VividRP.RenderPass.Dispose/Temporal Super Resolution (Injected)");
        private static readonly ProfilingSampler s_ProfilingSampler = new("Temporal Super Resolution");

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int InputDepthId = Shader.PropertyToID("_InputDepth");
        private static readonly int InputMotionVectorsId = Shader.PropertyToID("_InputMotionVectors");
        private static readonly int HistoryColorId = Shader.PropertyToID("_HistoryColor");
        private static readonly int HistoryMetaId = Shader.PropertyToID("_HistoryMeta");
        private static readonly int DilatedMotionId = Shader.PropertyToID("_DilatedMotion");
        private static readonly int DilatedDepthId = Shader.PropertyToID("_DilatedDepth");
        private static readonly int DepthErrorId = Shader.PropertyToID("_DepthError");
        private static readonly int ReprojectionBoundaryId = Shader.PropertyToID("_ReprojectionBoundary");
        private static readonly int ThinGeometryCoverageId = Shader.PropertyToID("_ThinGeometryCoverage");
        private static readonly int ReprojectedHistoryColorId = Shader.PropertyToID("_ReprojectedHistoryColor");
        private static readonly int ReprojectedHistoryMetaId = Shader.PropertyToID("_ReprojectedHistoryMeta");
        private static readonly int AcceptedHistoryColorId = Shader.PropertyToID("_AcceptedHistoryColor");
        private static readonly int RejectionMaskId = Shader.PropertyToID("_RejectionMask");
        private static readonly int CurrentFrameColorId = Shader.PropertyToID("_CurrentFrameColor");
        private static readonly int SpatialAntiAliasedColorId = Shader.PropertyToID("_SpatialAntiAliasedColor");
        private static readonly int UpdatedHistoryColorId = Shader.PropertyToID("_UpdatedHistoryColor");
        private static readonly int UpdatedHistoryMetaId = Shader.PropertyToID("_UpdatedHistoryMeta");
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
            var quality = additionalData != null
                ? additionalData.tsrQuality
                : VividTsrQualityMode.Balanced;
            var historySampleCount = additionalData != null
                ? additionalData.tsrHistorySampleCount
                : 16;

            var cameraState = GetOrCreateCameraState(cameraData.camera, cameraData.frameIndex);
            CleanupExpiredCameraStates(cameraData.frameIndex);

            var resetHistory = cameraState.Prepare(
                renderSize,
                outputSize,
                quality,
                historySampleCount,
                cameraData.frameIndex,
                forceResetHistory || (temporalData != null && temporalData.IsFirstFrame));

            var outputDescriptor = CreateOutputDescriptor(sourceTexture.desc, outputSize);
            var outputHandle = renderGraph.CreateTexture(outputDescriptor);
            var handles = cameraState.Import(renderGraph);
            var currentJitter = ResolveCurrentJitter(cameraData, temporalData);
            var previousJitter = ResolvePreviousJitter(cameraState, temporalData);

            var dilatedMotion = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_DilatedMotion", renderSize.x, renderSize.y, GraphicsFormat.R16G16_SFloat));
            var dilatedDepth = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_DilatedDepth", renderSize.x, renderSize.y, GraphicsFormat.R32_SFloat));
            var depthError = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_DepthError", renderSize.x, renderSize.y, GraphicsFormat.R16_SFloat));
            var reprojectionBoundary = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_ReprojectionBoundary", renderSize.x, renderSize.y, GraphicsFormat.R8_UNorm));
            var thinGeometryCoverage = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_ThinGeometryCoverage", renderSize.x, renderSize.y, GraphicsFormat.R8_UNorm));
            var reprojectedHistoryColor = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_ReprojectedHistoryColor", outputSize.x, outputSize.y, GraphicsFormat.R16G16B16A16_SFloat));
            var reprojectedHistoryMeta = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_ReprojectedHistoryMeta", outputSize.x, outputSize.y, GraphicsFormat.R16G16_SFloat));
            var acceptedHistoryColor = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_AcceptedHistoryColor", outputSize.x, outputSize.y, GraphicsFormat.R16G16B16A16_SFloat));
            var rejectionMask = renderGraph.CreateTexture(
                CreateColorDescriptor("TSR_RejectionMask", outputSize.x, outputSize.y, GraphicsFormat.R8_UNorm));
            var spatialAntiAliasedColor = renderGraph.CreateTexture(
                CreateOutputDescriptor(sourceTexture.desc, outputSize, "TSR_SpatialAntiAliasedColor"));
            var enableSharpening = additionalData == null || additionalData.tsrEnableSharpening;
            var resolveOutput = enableSharpening
                ? renderGraph.CreateTexture(CreateOutputDescriptor(sourceTexture.desc, outputSize, "TSR_PreSharpenOutput"))
                : outputHandle;

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "Temporal Super Resolution",
                       out var passData,
                       s_ProfilingSampler))
            {
                passData.State = cameraState;
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
                passData.ReprojectedHistoryColor = reprojectedHistoryColor;
                passData.ReprojectedHistoryMeta = reprojectedHistoryMeta;
                passData.AcceptedHistoryColor = acceptedHistoryColor;
                passData.RejectionMask = rejectionMask;
                passData.SpatialAntiAliasedColor = spatialAntiAliasedColor;
                passData.PreviousHistoryColor = handles.PreviousHistoryColor;
                passData.CurrentHistoryColor = handles.CurrentHistoryColor;
                passData.PreviousHistoryMeta = handles.PreviousHistoryMeta;
                passData.CurrentHistoryMeta = handles.CurrentHistoryMeta;
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

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.UseTexture(passData.Output, AccessFlags.WriteAll);
                builder.UseTexture(passData.DilatedMotion, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DilatedDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DepthError, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectionBoundary, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ThinGeometryCoverage, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedHistoryColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReprojectedHistoryMeta, AccessFlags.ReadWrite);
                builder.UseTexture(passData.AcceptedHistoryColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.RejectionMask, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SpatialAntiAliasedColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousHistoryColor, AccessFlags.Read);
                builder.UseTexture(passData.CurrentHistoryColor, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousHistoryMeta, AccessFlags.Read);
                builder.UseTexture(passData.CurrentHistoryMeta, AccessFlags.ReadWrite);
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
            DispatchRejectShading(cmd, data);
            DispatchSpatialAntiAliasing(cmd, data);
            DispatchUpdateHistory(cmd, data);
            DispatchResolveHistory(cmd, data);

            if (data.EnableSharpening)
                DispatchSharpen(cmd, data);
        }

        private static void DispatchDilateVelocity(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.DilateVelocity;
            var kernel = data.Shaders.DilateVelocityKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, InputMotionVectorsId, data.MotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
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
            cmd.SetComputeTextureParam(shader, kernel, HistoryColorId, data.PreviousHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, HistoryMetaId, data.PreviousHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchRejectShading(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.RejectShading;
            var kernel = data.Shaders.RejectShadingKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, InputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, InputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DepthErrorId, data.DepthError);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryColorId, data.ReprojectedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchUpdateHistory(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.UpdateHistory;
            var kernel = data.Shaders.UpdateHistoryKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, CurrentFrameColorId, data.SpatialAntiAliasedColor);
            cmd.SetComputeTextureParam(shader, kernel, DilatedMotionId, data.DilatedMotion);
            cmd.SetComputeTextureParam(shader, kernel, DilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectionBoundaryId, data.ReprojectionBoundary);
            cmd.SetComputeTextureParam(shader, kernel, ThinGeometryCoverageId, data.ThinGeometryCoverage);
            cmd.SetComputeTextureParam(shader, kernel, AcceptedHistoryColorId, data.AcceptedHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, ReprojectedHistoryMetaId, data.ReprojectedHistoryMeta);
            cmd.SetComputeTextureParam(shader, kernel, RejectionMaskId, data.RejectionMask);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedHistoryColorId, data.CurrentHistoryColor);
            cmd.SetComputeTextureParam(shader, kernel, UpdatedHistoryMetaId, data.CurrentHistoryMeta);
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

        private static RenderGraphTextureDesc CreateOutputDescriptor(
            RenderGraphTextureDesc sourceDescriptor,
            Vector2Int outputSize,
            string name = "TSROutput")
        {
            var descriptor = sourceDescriptor?.Clone()
                ?? RenderGraphTextureDesc.CreateColorTarget(
                    Mathf.Max(1, outputSize.x),
                    Mathf.Max(1, outputSize.y),
                    GraphicsFormat.R16G16B16A16_SFloat);

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

        private static RenderGraphTextureDesc CreateColorDescriptor(string name, int width, int height, GraphicsFormat format)
        {
            return new RenderGraphTextureDesc
            {
                Name = name,
                Width = Mathf.Max(1, width),
                Height = Mathf.Max(1, height),
                ColorFormat = format,
                DepthBufferBits = DepthBits.None,
                MsaaSamples = MSAASamples.None,
                FilterMode = FilterMode.Bilinear,
                WrapMode = TextureWrapMode.Clamp,
                ClearBuffer = false,
                EnableRandomWrite = true,
                UseMipMap = false,
                AutoGenerateMips = false,
                MipCount = 1,
            };
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
                TextureHandle currentHistoryMeta)
            {
                PreviousHistoryColor = previousHistoryColor;
                CurrentHistoryColor = currentHistoryColor;
                PreviousHistoryMeta = previousHistoryMeta;
                CurrentHistoryMeta = currentHistoryMeta;
            }

            public TextureHandle PreviousHistoryColor { get; }
            public TextureHandle CurrentHistoryColor { get; }
            public TextureHandle PreviousHistoryMeta { get; }
            public TextureHandle CurrentHistoryMeta { get; }
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
            public readonly int SpatialAntiAliasingKernel;
            public readonly int UpdateHistoryKernel;
            public readonly int ResolveHistoryKernel;
            public readonly int SharpenKernel;

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
                SpatialAntiAliasingKernel = FindKernel(SpatialAntiAliasing);
                UpdateHistoryKernel = FindKernel(UpdateHistory);
                ResolveHistoryKernel = FindKernel(ResolveHistory);
                SharpenKernel = FindKernel(Sharpen);
            }

            public bool IsValid =>
                DilateVelocity != null && DilateVelocityKernel >= 0
                && ReprojectHistory != null && ReprojectHistoryKernel >= 0
                && RejectShading != null && RejectShadingKernel >= 0
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
            public TextureHandle ReprojectedHistoryColor;
            public TextureHandle ReprojectedHistoryMeta;
            public TextureHandle AcceptedHistoryColor;
            public TextureHandle RejectionMask;
            public TextureHandle SpatialAntiAliasedColor;
            public TextureHandle PreviousHistoryColor;
            public TextureHandle CurrentHistoryColor;
            public TextureHandle PreviousHistoryMeta;
            public TextureHandle CurrentHistoryMeta;
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
            public float Sharpness;
        }

        internal sealed class CameraState : IDisposable
        {
            private readonly RTHandle[] m_HistoryColor = new RTHandle[2];
            private readonly RTHandle[] m_HistoryMeta = new RTHandle[2];
            private int m_ResourceIndex;
            private Vector2Int m_RenderSize;
            private Vector2Int m_OutputSize;
            private VividTsrQualityMode m_Quality;
            private int m_HistorySampleCount;
            private bool m_HasValidHistory;

            public int LastUsedFrame { get; set; }
            public Vector2Int PreviousRenderSize { get; private set; } = Vector2Int.one;
            public Vector2Int PreviousOutputSize { get; private set; } = Vector2Int.one;
            public Vector2 PreviousJitter { get; private set; }

            public bool Prepare(
                Vector2Int renderSize,
                Vector2Int outputSize,
                VividTsrQualityMode quality,
                int historySampleCount,
                int frameIndex,
                bool forceResetHistory)
            {
                historySampleCount = Mathf.Clamp(historySampleCount, 8, 32);
                var resetHistory = forceResetHistory
                    || !m_HasValidHistory
                    || m_RenderSize != renderSize
                    || m_OutputSize != outputSize
                    || m_Quality != quality
                    || m_HistorySampleCount != historySampleCount;

                EnsureTextures(outputSize);

                if (resetHistory)
                {
                    m_ResourceIndex = 0;
                    PreviousRenderSize = renderSize;
                    PreviousOutputSize = outputSize;
                    PreviousJitter = Vector2.zero;
                }

                m_RenderSize = renderSize;
                m_OutputSize = outputSize;
                m_Quality = quality;
                m_HistorySampleCount = historySampleCount;
                m_HasValidHistory = true;
                LastUsedFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
                return resetHistory;
            }

            internal ImportedHandles Import(RenderGraph renderGraph)
            {
                var readIndex = m_ResourceIndex;
                var writeIndex = 1 - m_ResourceIndex;
                return new ImportedHandles(
                    renderGraph.ImportTexture(m_HistoryColor[readIndex]),
                    renderGraph.ImportTexture(m_HistoryColor[writeIndex]),
                    renderGraph.ImportTexture(m_HistoryMeta[readIndex]),
                    renderGraph.ImportTexture(m_HistoryMeta[writeIndex]));
            }

            public void CommitFrame(Vector2Int renderSize, Vector2Int outputSize, Vector2 jitter)
            {
                PreviousRenderSize = renderSize;
                PreviousOutputSize = outputSize;
                PreviousJitter = jitter;
                m_ResourceIndex = 1 - m_ResourceIndex;
            }

            public void ClearHistory(CommandBuffer cmd)
            {
                if (cmd == null)
                    return;

                for (var i = 0; i < 2; i++)
                {
                    ClearRTHandle(cmd, m_HistoryColor[i], Color.clear);
                    ClearRTHandle(cmd, m_HistoryMeta[i], Color.clear);
                }
            }

            public void Dispose()
            {
                ReleaseArray(m_HistoryColor);
                ReleaseArray(m_HistoryMeta);
                m_HasValidHistory = false;
            }

            private void EnsureTextures(Vector2Int outputSize)
            {
                for (var i = 0; i < 2; i++)
                {
                    EnsureHandle(
                        ref m_HistoryColor[i],
                        outputSize.x,
                        outputSize.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        $"TSR_HistoryColor{i + 1}");
                    EnsureHandle(
                        ref m_HistoryMeta[i],
                        outputSize.x,
                        outputSize.y,
                        GraphicsFormat.R16G16_SFloat,
                        $"TSR_HistoryMeta{i + 1}");
                }
            }

            private static void EnsureHandle(
                ref RTHandle handle,
                int width,
                int height,
                GraphicsFormat format,
                string name)
            {
                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);
                if (handle != null
                    && handle.rt != null
                    && handle.rt.width == width
                    && handle.rt.height == height
                    && handle.rt.graphicsFormat == format)
                {
                    return;
                }

                handle?.Release();
                handle = RTHandles.Alloc(
                    width,
                    height,
                    colorFormat: format,
                    enableRandomWrite: true,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Clamp,
                    name: name);
            }

            private static void ClearRTHandle(CommandBuffer cmd, RTHandle handle, Color clearColor)
            {
                if (handle == null)
                    return;

                CoreUtils.SetRenderTarget(cmd, handle);
                cmd.ClearRenderTarget(false, true, clearColor);
            }

            private static void ReleaseArray(RTHandle[] handles)
            {
                for (var i = 0; i < handles.Length; i++)
                {
                    handles[i]?.Release();
                    handles[i] = null;
                }
            }
        }
    }
}
