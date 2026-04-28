using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class FSR3UpscalerPass : IDisposable
    {
        private const int CameraStateExpirationFrames = 400;
        private const int KernelThreadGroupSize = 8;
        private const int SpdThreadGroupSize = 64;
        private const int RcasThreadGroupSize = 16;
        private const int MaxSpdMips = 12;

        private static readonly ProfilerMarker s_RecordGraphMarker =
            new("VividRP.RenderPass.RecordGraph/FSR3 Super Resolution (Injected)");
        private static readonly ProfilerMarker s_RecordMarker =
            new("VividRP.RenderPass.Record/FSR3 Super Resolution (Injected)");
        private static readonly ProfilerMarker s_DisposeMarker =
            new("VividRP.RenderPass.Dispose/FSR3 Super Resolution (Injected)");
        private static readonly ProfilingSampler s_ProfilingSampler = new("FSR3 Super Resolution");

        private static readonly int IRenderSizeId = Shader.PropertyToID("iRenderSize");
        private static readonly int IPreviousFrameRenderSizeId = Shader.PropertyToID("iPreviousFrameRenderSize");
        private static readonly int IUpscaleSizeId = Shader.PropertyToID("iUpscaleSize");
        private static readonly int IPreviousFrameUpscaleSizeId = Shader.PropertyToID("iPreviousFrameUpscaleSize");
        private static readonly int IMaxRenderSizeId = Shader.PropertyToID("iMaxRenderSize");
        private static readonly int IMaxUpscaleSizeId = Shader.PropertyToID("iMaxUpscaleSize");
        private static readonly int FDeviceToViewDepthId = Shader.PropertyToID("fDeviceToViewDepth");
        private static readonly int FJitterId = Shader.PropertyToID("fJitter");
        private static readonly int FPreviousFrameJitterId = Shader.PropertyToID("fPreviousFrameJitter");
        private static readonly int FMotionVectorScaleId = Shader.PropertyToID("fMotionVectorScale");
        private static readonly int FDownscaleFactorId = Shader.PropertyToID("fDownscaleFactor");
        private static readonly int FMotionVectorJitterCancellationId = Shader.PropertyToID("fMotionVectorJitterCancellation");
        private static readonly int FTanHalfFovId = Shader.PropertyToID("fTanHalfFOV");
        private static readonly int FJitterSequenceLengthId = Shader.PropertyToID("fJitterSequenceLength");
        private static readonly int FDeltaTimeId = Shader.PropertyToID("fDeltaTime");
        private static readonly int FDeltaPreExposureId = Shader.PropertyToID("fDeltaPreExposure");
        private static readonly int FViewSpaceToMetersFactorId = Shader.PropertyToID("fViewSpaceToMetersFactor");
        private static readonly int FFrameIndexId = Shader.PropertyToID("fFrameIndex");
        private static readonly int FVelocityFactorId = Shader.PropertyToID("fVelocityFactor");
        private static readonly int FReactivenessScaleId = Shader.PropertyToID("fReactivenessScale");
        private static readonly int FShadingChangeScaleId = Shader.PropertyToID("fShadingChangeScale");
        private static readonly int FAccumulationAddedPerFrameId = Shader.PropertyToID("fAccumulationAddedPerFrame");
        private static readonly int FMinDisocclusionAccumulationId = Shader.PropertyToID("fMinDisocclusionAccumulation");
        private static readonly int SpdMipsId = Shader.PropertyToID("mips");
        private static readonly int SpdNumWorkGroupsId = Shader.PropertyToID("numWorkGroups");
        private static readonly int SpdWorkGroupOffsetId = Shader.PropertyToID("workGroupOffset");
        private static readonly int SpdRenderSizeId = Shader.PropertyToID("renderSize");
        private static readonly int RcasConfigId = Shader.PropertyToID("rcasConfig");

        private static readonly int RInputMotionVectorsId = Shader.PropertyToID("r_input_motion_vectors");
        private static readonly int RInputDepthId = Shader.PropertyToID("r_input_depth");
        private static readonly int RInputColorId = Shader.PropertyToID("r_input_color_jittered");
        private static readonly int RInputExposureId = Shader.PropertyToID("r_input_exposure");
        private static readonly int RReactiveMaskId = Shader.PropertyToID("r_reactive_mask");
        private static readonly int RTransparencyAndCompositionMaskId = Shader.PropertyToID("r_transparency_and_composition_mask");
        private static readonly int RSpdMipsId = Shader.PropertyToID("r_spd_mips");
        private static readonly int RDilatedReactiveMasksId = Shader.PropertyToID("r_dilated_reactive_masks");
        private static readonly int RDilatedMotionVectorsId = Shader.PropertyToID("r_dilated_motion_vectors");
        private static readonly int RDilatedDepthId = Shader.PropertyToID("r_dilated_depth");
        private static readonly int RReconstructedPrevNearestDepthId = Shader.PropertyToID("r_reconstructed_previous_nearest_depth");
        private static readonly int RInternalUpscaledColorId = Shader.PropertyToID("r_internal_upscaled_color");
        private static readonly int RLanczosLutId = Shader.PropertyToID("r_lanczos_lut");
        private static readonly int RFarthestDepthId = Shader.PropertyToID("r_farthest_depth");
        private static readonly int RFarthestDepthMip1Id = Shader.PropertyToID("r_farthest_depth_mip1");
        private static readonly int RCurrentLumaId = Shader.PropertyToID("r_current_luma");
        private static readonly int RPreviousLumaId = Shader.PropertyToID("r_previous_luma");
        private static readonly int RFrameInfoId = Shader.PropertyToID("r_frame_info");
        private static readonly int RLumaHistoryId = Shader.PropertyToID("r_luma_history");
        private static readonly int RLumaInstabilityId = Shader.PropertyToID("r_luma_instability");
        private static readonly int RAccumulationId = Shader.PropertyToID("r_accumulation");
        private static readonly int RShadingChangeId = Shader.PropertyToID("r_shading_change");
        private static readonly int RRcasInputId = Shader.PropertyToID("r_rcas_input");

        private static readonly int RwDilatedMotionVectorsId = Shader.PropertyToID("rw_dilated_motion_vectors");
        private static readonly int RwDilatedDepthId = Shader.PropertyToID("rw_dilated_depth");
        private static readonly int RwReconstructedPrevNearestDepthId = Shader.PropertyToID("rw_reconstructed_previous_nearest_depth");
        private static readonly int RwFarthestDepthId = Shader.PropertyToID("rw_farthest_depth");
        private static readonly int RwCurrentLumaId = Shader.PropertyToID("rw_current_luma");
        private static readonly int RwSpdGlobalAtomicId = Shader.PropertyToID("rw_spd_global_atomic");
        private static readonly int RwFrameInfoId = Shader.PropertyToID("rw_frame_info");
        private static readonly int RwFarthestDepthMip1Id = Shader.PropertyToID("rw_farthest_depth_mip1");
        private static readonly int RwShadingChangeId = Shader.PropertyToID("rw_shading_change");
        private static readonly int RwDilatedReactiveMasksId = Shader.PropertyToID("rw_dilated_reactive_masks");
        private static readonly int RwNewLocksId = Shader.PropertyToID("rw_new_locks");
        private static readonly int RwAccumulationId = Shader.PropertyToID("rw_accumulation");
        private static readonly int RwLumaHistoryId = Shader.PropertyToID("rw_luma_history");
        private static readonly int RwLumaInstabilityId = Shader.PropertyToID("rw_luma_instability");
        private static readonly int RwInternalUpscaledColorId = Shader.PropertyToID("rw_internal_upscaled_color");
        private static readonly int RwUpscaledOutputId = Shader.PropertyToID("rw_upscaled_output");

        private static readonly int[] RwSpdMipIds =
        {
            Shader.PropertyToID("rw_spd_mip0"),
            Shader.PropertyToID("rw_spd_mip1"),
            Shader.PropertyToID("rw_spd_mip2"),
            Shader.PropertyToID("rw_spd_mip3"),
            Shader.PropertyToID("rw_spd_mip4"),
            Shader.PropertyToID("rw_spd_mip5"),
        };

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

        public RenderGraphTexture Record(
            RenderGraph renderGraph,
            VividCameraData cameraData,
            CameraTemporalData temporalData,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture depthTexture,
            RenderGraphTexture motionTexture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            bool forceResetHistory = false)
        {
            using var recordGraphScope = s_RecordGraphMarker.Auto();
            if (renderGraph == null
                || cameraData?.camera == null
                || sourceTexture?.innerHandle.IsValid() != true
                || depthTexture?.innerHandle.IsValid() != true
                || motionTexture?.innerHandle.IsValid() != true)
            {
                return null;
            }

            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            if (!TryResolveShaderSet(resources, out var shaders))
                return null;

            var renderSize = ResolveRenderSize(cameraData);
            var outputSize = ResolveOutputSize(cameraData, renderSize);
            if (renderSize.x <= 0 || renderSize.y <= 0 || outputSize.x <= 0 || outputSize.y <= 0)
                return null;

            var cameraState = GetOrCreateCameraState(cameraData.camera, cameraData.frameIndex);
            CleanupExpiredCameraStates(cameraData.frameIndex);

            var additionalData = cameraData.additionalData;
            var quality = additionalData != null
                ? additionalData.fsr3Quality
                : VividFsr3QualityMode.Balanced;
            var resetHistory = cameraState.Prepare(
                renderSize,
                outputSize,
                quality,
                cameraData.frameIndex,
                forceResetHistory || (temporalData != null && temporalData.IsFirstFrame));

            var outputDescriptor = CreateOutputDescriptor(sourceTexture.desc, outputSize);
            var outputHandle = renderGraph.CreateTexture(outputDescriptor);
            var handles = cameraState.Import(renderGraph);

            var dilatedMotionVectors = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_DilatedMotionVectors", renderSize.x, renderSize.y, GraphicsFormat.R16G16_SFloat));
            var dilatedDepth = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_DilatedDepth", renderSize.x, renderSize.y, GraphicsFormat.R32_SFloat));
            var reconstructedPrevNearestDepth = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_ReconstructedPrevNearestDepth", renderSize.x, renderSize.y, GraphicsFormat.R32_UInt));
            var farthestDepth = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_FarthestDepth", renderSize.x, renderSize.y, GraphicsFormat.R16_SFloat));
            var halfRenderSize = new Vector2Int(Mathf.Max(1, renderSize.x / 2), Mathf.Max(1, renderSize.y / 2));
            var spdMips = renderGraph.CreateTexture(
                CreateMipDescriptor("FSR3_SpdMips", halfRenderSize.x, halfRenderSize.y, GraphicsFormat.R16G16_SFloat));
            var farthestDepthMip1 = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_FarthestDepthMip1", halfRenderSize.x, halfRenderSize.y, GraphicsFormat.R16_SFloat));
            var shadingChange = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_ShadingChange", halfRenderSize.x, halfRenderSize.y, GraphicsFormat.R8_UNorm));
            var newLocks = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_NewLocks", outputSize.x, outputSize.y, GraphicsFormat.R8_UNorm));
            var dilatedReactiveMasks = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_DilatedReactiveMasks", renderSize.x, renderSize.y, GraphicsFormat.R8G8B8A8_UNorm));
            var lumaInstability = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_LumaInstability", renderSize.x, renderSize.y, GraphicsFormat.R8_UNorm));
            var spdAtomicCounter = renderGraph.CreateTexture(
                CreateColorDescriptor("FSR3_SpdAtomicCounter", 1, 1, GraphicsFormat.R32_UInt));

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "FSR3 Super Resolution",
                       out var passData,
                       s_ProfilingSampler))
            {
                passData.State = cameraState;
                passData.Shaders = shaders;
                passData.Source = sourceTexture.innerHandle;
                passData.Depth = depthTexture.innerHandle;
                passData.MotionVectors = motionTexture.innerHandle;
                passData.Output = outputHandle;
                passData.DilatedMotionVectors = dilatedMotionVectors;
                passData.DilatedDepth = dilatedDepth;
                passData.ReconstructedPrevNearestDepth = reconstructedPrevNearestDepth;
                passData.FarthestDepth = farthestDepth;
                passData.SpdMips = spdMips;
                passData.FarthestDepthMip1 = farthestDepthMip1;
                passData.ShadingChange = shadingChange;
                passData.NewLocks = newLocks;
                passData.DilatedReactiveMasks = dilatedReactiveMasks;
                passData.LumaInstability = lumaInstability;
                passData.SpdAtomicCounter = spdAtomicCounter;
                passData.CurrentLuma = handles.CurrentLuma;
                passData.PreviousLuma = handles.PreviousLuma;
                passData.PreviousAccumulation = handles.PreviousAccumulation;
                passData.CurrentAccumulation = handles.CurrentAccumulation;
                passData.PreviousInternalUpscaled = handles.PreviousInternalUpscaled;
                passData.CurrentInternalUpscaled = handles.CurrentInternalUpscaled;
                passData.PreviousLumaHistory = handles.PreviousLumaHistory;
                passData.CurrentLumaHistory = handles.CurrentLumaHistory;
                passData.FrameInfo = handles.FrameInfo;
                passData.ResetHistory = resetHistory;
                passData.RenderSize = renderSize;
                passData.PreviousRenderSize = cameraState.PreviousRenderSize;
                passData.OutputSize = outputSize;
                passData.PreviousOutputSize = cameraState.PreviousOutputSize;
                passData.Jitter = additionalData != null ? additionalData.fsr3JitterOffset : Vector2.zero;
                passData.PreviousJitter = cameraState.PreviousJitter;
                passData.JitterSequenceLength = additionalData != null && additionalData.fsr3JitterPhaseCount > 0
                    ? additionalData.fsr3JitterPhaseCount
                    : FSR3UpscalerUtility.GetJitterPhaseCount(renderSize.x, outputSize.x);
                passData.MotionVectorScale = ResolveMotionVectorScale(renderSize);
                passData.DeviceToViewDepth = ResolveDeviceToViewDepth(cameraData.camera, renderSize);
                passData.TanHalfFov = ResolveTanHalfHorizontalFov(cameraData.camera);
                passData.DeltaTime = temporalData != null && temporalData.DeltaTime > 0f
                    ? Mathf.Clamp01(temporalData.DeltaTime)
                    : Mathf.Clamp01(Time.deltaTime);
                passData.FrameIndex = resetHistory ? 0.0f : cameraState.AccumulatedFrameIndex + 1.0f;
                passData.EnableSharpening = additionalData == null || additionalData.fsr3EnableSharpening;
                passData.RcasConfig = CreateRcasConfig(additionalData != null ? additionalData.fsr3Sharpness : 0.2f);

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.UseTexture(passData.Output, AccessFlags.WriteAll);
                builder.UseTexture(passData.DilatedMotionVectors, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DilatedDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ReconstructedPrevNearestDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FarthestDepth, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SpdMips, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FarthestDepthMip1, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ShadingChange, AccessFlags.ReadWrite);
                builder.UseTexture(passData.NewLocks, AccessFlags.ReadWrite);
                builder.UseTexture(passData.DilatedReactiveMasks, AccessFlags.ReadWrite);
                builder.UseTexture(passData.LumaInstability, AccessFlags.ReadWrite);
                builder.UseTexture(passData.SpdAtomicCounter, AccessFlags.ReadWrite);
                builder.UseTexture(passData.CurrentLuma, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousLuma, AccessFlags.Read);
                builder.UseTexture(passData.PreviousAccumulation, AccessFlags.Read);
                builder.UseTexture(passData.CurrentAccumulation, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousInternalUpscaled, AccessFlags.Read);
                builder.UseTexture(passData.CurrentInternalUpscaled, AccessFlags.ReadWrite);
                builder.UseTexture(passData.PreviousLumaHistory, AccessFlags.Read);
                builder.UseTexture(passData.CurrentLumaHistory, AccessFlags.ReadWrite);
                builder.UseTexture(passData.FrameInfo, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    using var recordScope = s_RecordMarker.Auto();
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Execute(cmd, data);
                });
            }

            cameraState.CommitFrame(renderSize, outputSize, passDataJitter: additionalData != null ? additionalData.fsr3JitterOffset : Vector2.zero);

            var outputTexture = new RenderGraphTexture
            {
                desc = outputDescriptor,
                innerHandle = outputHandle,
            };
            textureCache[outputTexture] = outputHandle;
            return outputTexture;
        }

        public bool Record(
            RenderGraph renderGraph,
            VividCameraData cameraData,
            CameraTemporalData temporalData,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture depthTexture,
            RenderGraphTexture motionTexture,
            RenderGraphTexture outputTexture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            bool forceResetHistory = false)
        {
            if (outputTexture == null)
                return false;

            var recordedOutput = Record(
                renderGraph,
                cameraData,
                temporalData,
                sourceTexture,
                depthTexture,
                motionTexture,
                textureCache,
                forceResetHistory);
            if (recordedOutput == null)
                return false;

            outputTexture.desc = recordedOutput.desc;
            outputTexture.innerHandle = recordedOutput.innerHandle;
            textureCache[outputTexture] = recordedOutput.innerHandle;
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

            foreach (var kv in m_CameraStates)
            {
                if (currentFrame - kv.Value.LastUsedFrame > CameraStateExpirationFrames)
                    m_ExpiredCameraIds.Add(kv.Key);
            }

            for (var i = 0; i < m_ExpiredCameraIds.Count; i++)
            {
                var cameraId = m_ExpiredCameraIds[i];
                if (!m_CameraStates.TryGetValue(cameraId, out var state))
                    continue;

                state.Dispose();
                m_CameraStates.Remove(cameraId);
            }
        }

        private static void Execute(CommandBuffer cmd, PassData data)
        {
            if (cmd == null || data == null || !data.Shaders.IsValid)
                return;

            ClearFrameResources(cmd, data);

            DispatchPrepareInputs(cmd, data);
            DispatchLumaPyramid(cmd, data);
            DispatchShadingChangePyramid(cmd, data);
            DispatchShadingChange(cmd, data);
            DispatchPrepareReactivity(cmd, data);
            DispatchLumaInstability(cmd, data);
            DispatchAccumulate(cmd, data);

            if (data.EnableSharpening)
                DispatchRcas(cmd, data);
        }

        private static void DispatchPrepareInputs(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.PrepareInputs;
            var kernel = data.Shaders.PrepareInputsKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RInputMotionVectorsId, data.MotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RInputDepthId, data.Depth);
            cmd.SetComputeTextureParam(shader, kernel, RInputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, RwDilatedMotionVectorsId, data.DilatedMotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RwDilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, RwReconstructedPrevNearestDepthId, data.ReconstructedPrevNearestDepth);
            cmd.SetComputeTextureParam(shader, kernel, RwFarthestDepthId, data.FarthestDepth);
            cmd.SetComputeTextureParam(shader, kernel, RwCurrentLumaId, data.CurrentLuma);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize), DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchLumaPyramid(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.LumaPyramid;
            var kernel = data.Shaders.LumaPyramidKernel;
            SetCommonConstants(cmd, shader, data);
            SetSpdConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RCurrentLumaId, data.CurrentLuma);
            cmd.SetComputeTextureParam(shader, kernel, RFarthestDepthId, data.FarthestDepth);
            cmd.SetComputeTextureParam(shader, kernel, RwSpdGlobalAtomicId, data.SpdAtomicCounter);
            cmd.SetComputeTextureParam(shader, kernel, RwFrameInfoId, data.FrameInfo);
            BindSpdMips(cmd, shader, kernel, data.SpdMips);
            cmd.SetComputeTextureParam(shader, kernel, RwFarthestDepthMip1Id, data.FarthestDepthMip1);
            cmd.DispatchCompute(shader, kernel, data.SpdDispatchSize.x, data.SpdDispatchSize.y, 1);
        }

        private static void DispatchShadingChangePyramid(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.ShadingChangePyramid;
            var kernel = data.Shaders.ShadingChangePyramidKernel;
            SetCommonConstants(cmd, shader, data);
            SetSpdConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RCurrentLumaId, data.CurrentLuma);
            cmd.SetComputeTextureParam(shader, kernel, RPreviousLumaId, data.PreviousLuma);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedMotionVectorsId, data.DilatedMotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RInputExposureId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RwSpdGlobalAtomicId, data.SpdAtomicCounter);
            BindSpdMips(cmd, shader, kernel, data.SpdMips);
            cmd.DispatchCompute(shader, kernel, data.SpdDispatchSize.x, data.SpdDispatchSize.y, 1);
        }

        private static void DispatchShadingChange(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.ShadingChange;
            var kernel = data.Shaders.ShadingChangeKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RSpdMipsId, data.SpdMips);
            cmd.SetComputeTextureParam(shader, kernel, RwShadingChangeId, data.ShadingChange);
            cmd.DispatchCompute(
                shader,
                kernel,
                DivRoundUp(Mathf.Max(1, data.RenderSize.x / 2), KernelThreadGroupSize),
                DivRoundUp(Mathf.Max(1, data.RenderSize.y / 2), KernelThreadGroupSize),
                1);
        }

        private static void DispatchPrepareReactivity(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.PrepareReactivity;
            var kernel = data.Shaders.PrepareReactivityKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RReconstructedPrevNearestDepthId, data.ReconstructedPrevNearestDepth);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedMotionVectorsId, data.DilatedMotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedDepthId, data.DilatedDepth);
            cmd.SetComputeTextureParam(shader, kernel, RReactiveMaskId, Texture2D.blackTexture);
            cmd.SetComputeTextureParam(shader, kernel, RTransparencyAndCompositionMaskId, Texture2D.blackTexture);
            cmd.SetComputeTextureParam(shader, kernel, RAccumulationId, data.PreviousAccumulation);
            cmd.SetComputeTextureParam(shader, kernel, RShadingChangeId, data.ShadingChange);
            cmd.SetComputeTextureParam(shader, kernel, RCurrentLumaId, data.CurrentLuma);
            cmd.SetComputeTextureParam(shader, kernel, RInputExposureId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RwDilatedReactiveMasksId, data.DilatedReactiveMasks);
            cmd.SetComputeTextureParam(shader, kernel, RwNewLocksId, data.NewLocks);
            cmd.SetComputeTextureParam(shader, kernel, RwAccumulationId, data.CurrentAccumulation);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize), DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchLumaInstability(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.LumaInstability;
            var kernel = data.Shaders.LumaInstabilityKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RInputExposureId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedReactiveMasksId, data.DilatedReactiveMasks);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedMotionVectorsId, data.DilatedMotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RFrameInfoId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RLumaHistoryId, data.PreviousLumaHistory);
            cmd.SetComputeTextureParam(shader, kernel, RFarthestDepthMip1Id, data.FarthestDepthMip1);
            cmd.SetComputeTextureParam(shader, kernel, RCurrentLumaId, data.CurrentLuma);
            cmd.SetComputeTextureParam(shader, kernel, RwLumaHistoryId, data.CurrentLumaHistory);
            cmd.SetComputeTextureParam(shader, kernel, RwLumaInstabilityId, data.LumaInstability);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.RenderSize.x, KernelThreadGroupSize), DivRoundUp(data.RenderSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchAccumulate(CommandBuffer cmd, PassData data)
        {
            var shader = data.EnableSharpening
                ? data.Shaders.AccumulateSharpen
                : data.Shaders.Accumulate;
            var kernel = data.EnableSharpening
                ? data.Shaders.AccumulateSharpenKernel
                : data.Shaders.AccumulateKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeTextureParam(shader, kernel, RInputExposureId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedReactiveMasksId, data.DilatedReactiveMasks);
            cmd.SetComputeTextureParam(shader, kernel, RDilatedMotionVectorsId, data.DilatedMotionVectors);
            cmd.SetComputeTextureParam(shader, kernel, RInternalUpscaledColorId, data.PreviousInternalUpscaled);
            cmd.SetComputeTextureParam(shader, kernel, RLanczosLutId, Texture2D.blackTexture);
            cmd.SetComputeTextureParam(shader, kernel, RFarthestDepthMip1Id, data.FarthestDepthMip1);
            cmd.SetComputeTextureParam(shader, kernel, RCurrentLumaId, data.CurrentLuma);
            cmd.SetComputeTextureParam(shader, kernel, RLumaInstabilityId, data.LumaInstability);
            cmd.SetComputeTextureParam(shader, kernel, RInputColorId, data.Source);
            cmd.SetComputeTextureParam(shader, kernel, RwInternalUpscaledColorId, data.CurrentInternalUpscaled);
            cmd.SetComputeTextureParam(shader, kernel, RwUpscaledOutputId, data.Output);
            cmd.SetComputeTextureParam(shader, kernel, RwNewLocksId, data.NewLocks);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, KernelThreadGroupSize), DivRoundUp(data.OutputSize.y, KernelThreadGroupSize), 1);
        }

        private static void DispatchRcas(CommandBuffer cmd, PassData data)
        {
            var shader = data.Shaders.Rcas;
            var kernel = data.Shaders.RcasKernel;
            SetCommonConstants(cmd, shader, data);
            cmd.SetComputeIntParams(shader, RcasConfigId, data.RcasConfig);
            cmd.SetComputeTextureParam(shader, kernel, RInputExposureId, data.FrameInfo);
            cmd.SetComputeTextureParam(shader, kernel, RRcasInputId, data.CurrentInternalUpscaled);
            cmd.SetComputeTextureParam(shader, kernel, RwUpscaledOutputId, data.Output);
            cmd.DispatchCompute(shader, kernel, DivRoundUp(data.OutputSize.x, RcasThreadGroupSize), DivRoundUp(data.OutputSize.y, RcasThreadGroupSize), 1);
        }

        private static void SetCommonConstants(CommandBuffer cmd, ComputeShader shader, PassData data)
        {
            cmd.SetComputeIntParams(shader, IRenderSizeId, data.RenderSize.x, data.RenderSize.y);
            cmd.SetComputeIntParams(shader, IPreviousFrameRenderSizeId, data.PreviousRenderSize.x, data.PreviousRenderSize.y);
            cmd.SetComputeIntParams(shader, IUpscaleSizeId, data.OutputSize.x, data.OutputSize.y);
            cmd.SetComputeIntParams(shader, IPreviousFrameUpscaleSizeId, data.PreviousOutputSize.x, data.PreviousOutputSize.y);
            cmd.SetComputeIntParams(shader, IMaxRenderSizeId, data.RenderSize.x, data.RenderSize.y);
            cmd.SetComputeIntParams(shader, IMaxUpscaleSizeId, data.OutputSize.x, data.OutputSize.y);
            cmd.SetComputeVectorParam(shader, FDeviceToViewDepthId, data.DeviceToViewDepth);
            cmd.SetComputeVectorParam(shader, FJitterId, new Vector4(data.Jitter.x, data.Jitter.y, 0f, 0f));
            cmd.SetComputeVectorParam(shader, FPreviousFrameJitterId, new Vector4(data.PreviousJitter.x, data.PreviousJitter.y, 0f, 0f));
            cmd.SetComputeVectorParam(shader, FMotionVectorScaleId, new Vector4(data.MotionVectorScale.x, data.MotionVectorScale.y, 0f, 0f));
            cmd.SetComputeVectorParam(shader, FDownscaleFactorId, new Vector4(
                data.RenderSize.x / (float)data.OutputSize.x,
                data.RenderSize.y / (float)data.OutputSize.y,
                0f,
                0f));
            cmd.SetComputeVectorParam(shader, FMotionVectorJitterCancellationId, Vector4.zero);
            cmd.SetComputeFloatParam(shader, FTanHalfFovId, data.TanHalfFov);
            cmd.SetComputeFloatParam(shader, FJitterSequenceLengthId, data.JitterSequenceLength);
            cmd.SetComputeFloatParam(shader, FDeltaTimeId, data.DeltaTime);
            cmd.SetComputeFloatParam(shader, FDeltaPreExposureId, 1.0f);
            cmd.SetComputeFloatParam(shader, FViewSpaceToMetersFactorId, 1.0f);
            cmd.SetComputeFloatParam(shader, FFrameIndexId, data.FrameIndex);
            cmd.SetComputeFloatParam(shader, FVelocityFactorId, 1.0f);
            cmd.SetComputeFloatParam(shader, FReactivenessScaleId, 1.0f);
            cmd.SetComputeFloatParam(shader, FShadingChangeScaleId, 1.0f);
            cmd.SetComputeFloatParam(shader, FAccumulationAddedPerFrameId, 1.0f / 3.0f);
            cmd.SetComputeFloatParam(shader, FMinDisocclusionAccumulationId, -1.0f / 3.0f);
        }

        private static void SetSpdConstants(CommandBuffer cmd, ComputeShader shader, PassData data)
        {
            cmd.SetComputeIntParam(shader, SpdMipsId, data.SpdMipCount);
            cmd.SetComputeIntParam(shader, SpdNumWorkGroupsId, data.SpdDispatchSize.x * data.SpdDispatchSize.y);
            cmd.SetComputeIntParams(shader, SpdWorkGroupOffsetId, 0, 0);
            cmd.SetComputeIntParams(shader, SpdRenderSizeId, data.RenderSize.x, data.RenderSize.y);
        }

        private static void BindSpdMips(CommandBuffer cmd, ComputeShader shader, int kernel, TextureHandle texture)
        {
            for (var mip = 0; mip < RwSpdMipIds.Length; mip++)
                cmd.SetComputeTextureParam(shader, kernel, RwSpdMipIds[mip], texture, mip);
        }

        private static void ClearFrameResources(CommandBuffer cmd, PassData data)
        {
            ClearTexture(cmd, data.ReconstructedPrevNearestDepth, SystemInfo.usesReversedZBuffer ? Color.clear : Color.white);
            ClearTexture(cmd, data.SpdAtomicCounter, Color.clear);
            ClearTexture(cmd, data.NewLocks, Color.clear);
            ClearTexture(cmd, data.DilatedReactiveMasks, Color.clear);

            if (!data.ResetHistory)
                return;

            data.State.ClearHistory(cmd);
        }

        private static void ClearTexture(CommandBuffer cmd, TextureHandle handle, Color clearColor)
        {
            RenderTexture texture = handle;
            if (texture == null)
                return;

            CoreUtils.SetRenderTarget(cmd, texture);
            cmd.ClearRenderTarget(false, true, clearColor);
        }

        private static bool TryResolveShaderSet(VividRPCoreResources resources, out ShaderSet shaders)
        {
            shaders = default;
            if (resources == null
                || resources.FSR3PrepareInputsCompute == null
                || resources.FSR3LumaPyramidCompute == null
                || resources.FSR3ShadingChangePyramidCompute == null
                || resources.FSR3ShadingChangeCompute == null
                || resources.FSR3PrepareReactivityCompute == null
                || resources.FSR3LumaInstabilityCompute == null
                || resources.FSR3AccumulateCompute == null
                || resources.FSR3AccumulateSharpenCompute == null
                || resources.FSR3RCASCompute == null)
            {
                return false;
            }

            shaders = new ShaderSet(resources);
            return shaders.IsValid;
        }

        private static Vector2Int ResolveRenderSize(VividCameraData cameraData)
        {
            return new Vector2Int(
                CameraDimensionUtility.ResolveCameraDimension(cameraData.actualWidth, cameraData.pixelWidth, Screen.width),
                CameraDimensionUtility.ResolveCameraDimension(cameraData.actualHeight, cameraData.pixelHeight, Screen.height));
        }

        private static Vector2Int ResolveOutputSize(VividCameraData cameraData, Vector2Int renderSize)
        {
            var outputWidth = cameraData.pixelWidth > 0 ? cameraData.pixelWidth : renderSize.x;
            var outputHeight = cameraData.pixelHeight > 0 ? cameraData.pixelHeight : renderSize.y;
            return new Vector2Int(Mathf.Max(1, outputWidth), Mathf.Max(1, outputHeight));
        }

        private static Vector2 ResolveMotionVectorScale(Vector2Int renderSize)
        {
            return FSR3UpscalerUtility.GetMotionVectorScale(renderSize.x, renderSize.y);
        }

        private static Vector4 ResolveDeviceToViewDepth(Camera camera, Vector2Int renderSize)
        {
            if (camera == null)
                return new Vector4(1f, 0f, 1f, 1f);

            var aspect = renderSize.y > 0 ? renderSize.x / (float)renderSize.y : camera.aspect;
            return FSR3UpscalerUtility.GetDeviceToViewDepthConstants(
                camera.nearClipPlane,
                camera.farClipPlane,
                camera.fieldOfView * Mathf.Deg2Rad,
                aspect,
                SystemInfo.usesReversedZBuffer);
        }

        private static float ResolveTanHalfHorizontalFov(Camera camera)
        {
            if (camera == null)
                return 1.0f;

            var aspect = camera.aspect > 0f ? camera.aspect : 1.0f;
            var verticalFov = camera.fieldOfView * Mathf.Deg2Rad;
            var horizontalFov = Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * aspect) * 2.0f;
            return Mathf.Tan(horizontalFov * 0.5f);
        }

        private static RenderGraphTextureDesc CreateOutputDescriptor(
            RenderGraphTextureDesc sourceDescriptor,
            Vector2Int outputSize)
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

            descriptor.Name = "FSR3Output";
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

        private static RenderGraphTextureDesc CreateMipDescriptor(string name, int width, int height, GraphicsFormat format)
        {
            var descriptor = CreateColorDescriptor(name, width, height, format);
            descriptor.FilterMode = FilterMode.Bilinear;
            descriptor.UseMipMap = true;
            descriptor.AutoGenerateMips = false;
            descriptor.MipCount = MaxSpdMips;
            return descriptor;
        }

        private static int[] CreateRcasConfig(float sharpness)
        {
            var remappedSharpness = (-2.0f * Mathf.Clamp01(sharpness)) + 2.0f;
            var linearSharpness = Mathf.Pow(2.0f, -remappedSharpness);
            return new[]
            {
                BitConverter.SingleToInt32Bits(linearSharpness),
                unchecked((int)PackHalf2x16(linearSharpness, linearSharpness)),
                0,
                0,
            };
        }

        private static uint PackHalf2x16(float x, float y)
        {
            return FloatToHalfBits(x) | ((uint)FloatToHalfBits(y) << 16);
        }

        private static uint FloatToHalfBits(float value)
        {
            var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            var sign = (bits >> 16) & 0x8000u;
            var exponent = (int)((bits >> 23) & 0xff) - 127 + 15;
            var mantissa = bits & 0x7fffffu;

            if (exponent <= 0)
            {
                if (exponent < -10)
                    return sign;

                mantissa |= 0x800000u;
                var shift = 14 - exponent;
                var halfMantissa = mantissa >> shift;
                if (((mantissa >> (shift - 1)) & 1u) != 0)
                    halfMantissa++;

                return sign | halfMantissa;
            }

            if (exponent >= 31)
                return sign | 0x7c00u;

            var roundedMantissa = mantissa + 0x1000u;
            if ((roundedMantissa & 0x800000u) != 0)
            {
                roundedMantissa = 0;
                exponent++;
                if (exponent >= 31)
                    return sign | 0x7c00u;
            }

            return sign | ((uint)exponent << 10) | (roundedMantissa >> 13);
        }

        private static int DivRoundUp(int value, int divisor)
        {
            return (Mathf.Max(1, value) + divisor - 1) / divisor;
        }

        internal readonly struct ImportedHandles
        {
            public ImportedHandles(
                TextureHandle previousAccumulation,
                TextureHandle currentAccumulation,
                TextureHandle previousInternalUpscaled,
                TextureHandle currentInternalUpscaled,
                TextureHandle previousLumaHistory,
                TextureHandle currentLumaHistory,
                TextureHandle previousLuma,
                TextureHandle currentLuma,
                TextureHandle frameInfo)
            {
                PreviousAccumulation = previousAccumulation;
                CurrentAccumulation = currentAccumulation;
                PreviousInternalUpscaled = previousInternalUpscaled;
                CurrentInternalUpscaled = currentInternalUpscaled;
                PreviousLumaHistory = previousLumaHistory;
                CurrentLumaHistory = currentLumaHistory;
                PreviousLuma = previousLuma;
                CurrentLuma = currentLuma;
                FrameInfo = frameInfo;
            }

            public TextureHandle PreviousAccumulation { get; }
            public TextureHandle CurrentAccumulation { get; }
            public TextureHandle PreviousInternalUpscaled { get; }
            public TextureHandle CurrentInternalUpscaled { get; }
            public TextureHandle PreviousLumaHistory { get; }
            public TextureHandle CurrentLumaHistory { get; }
            public TextureHandle PreviousLuma { get; }
            public TextureHandle CurrentLuma { get; }
            public TextureHandle FrameInfo { get; }
        }

        private readonly struct ShaderSet
        {
            public readonly ComputeShader PrepareInputs;
            public readonly ComputeShader LumaPyramid;
            public readonly ComputeShader ShadingChangePyramid;
            public readonly ComputeShader ShadingChange;
            public readonly ComputeShader PrepareReactivity;
            public readonly ComputeShader LumaInstability;
            public readonly ComputeShader Accumulate;
            public readonly ComputeShader AccumulateSharpen;
            public readonly ComputeShader Rcas;
            public readonly int PrepareInputsKernel;
            public readonly int LumaPyramidKernel;
            public readonly int ShadingChangePyramidKernel;
            public readonly int ShadingChangeKernel;
            public readonly int PrepareReactivityKernel;
            public readonly int LumaInstabilityKernel;
            public readonly int AccumulateKernel;
            public readonly int AccumulateSharpenKernel;
            public readonly int RcasKernel;

            public ShaderSet(VividRPCoreResources resources)
            {
                PrepareInputs = resources.FSR3PrepareInputsCompute;
                LumaPyramid = resources.FSR3LumaPyramidCompute;
                ShadingChangePyramid = resources.FSR3ShadingChangePyramidCompute;
                ShadingChange = resources.FSR3ShadingChangeCompute;
                PrepareReactivity = resources.FSR3PrepareReactivityCompute;
                LumaInstability = resources.FSR3LumaInstabilityCompute;
                Accumulate = resources.FSR3AccumulateCompute;
                AccumulateSharpen = resources.FSR3AccumulateSharpenCompute;
                Rcas = resources.FSR3RCASCompute;
                PrepareInputsKernel = FindKernel(PrepareInputs);
                LumaPyramidKernel = FindKernel(LumaPyramid);
                ShadingChangePyramidKernel = FindKernel(ShadingChangePyramid);
                ShadingChangeKernel = FindKernel(ShadingChange);
                PrepareReactivityKernel = FindKernel(PrepareReactivity);
                LumaInstabilityKernel = FindKernel(LumaInstability);
                AccumulateKernel = FindKernel(Accumulate);
                AccumulateSharpenKernel = FindKernel(AccumulateSharpen);
                RcasKernel = FindKernel(Rcas);
            }

            public bool IsValid =>
                PrepareInputs != null && PrepareInputsKernel >= 0
                && LumaPyramid != null && LumaPyramidKernel >= 0
                && ShadingChangePyramid != null && ShadingChangePyramidKernel >= 0
                && ShadingChange != null && ShadingChangeKernel >= 0
                && PrepareReactivity != null && PrepareReactivityKernel >= 0
                && LumaInstability != null && LumaInstabilityKernel >= 0
                && Accumulate != null && AccumulateKernel >= 0
                && AccumulateSharpen != null && AccumulateSharpenKernel >= 0
                && Rcas != null && RcasKernel >= 0;

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
            public TextureHandle DilatedMotionVectors;
            public TextureHandle DilatedDepth;
            public TextureHandle ReconstructedPrevNearestDepth;
            public TextureHandle FarthestDepth;
            public TextureHandle SpdMips;
            public TextureHandle FarthestDepthMip1;
            public TextureHandle ShadingChange;
            public TextureHandle NewLocks;
            public TextureHandle DilatedReactiveMasks;
            public TextureHandle LumaInstability;
            public TextureHandle SpdAtomicCounter;
            public TextureHandle PreviousAccumulation;
            public TextureHandle CurrentAccumulation;
            public TextureHandle PreviousInternalUpscaled;
            public TextureHandle CurrentInternalUpscaled;
            public TextureHandle PreviousLumaHistory;
            public TextureHandle CurrentLumaHistory;
            public TextureHandle PreviousLuma;
            public TextureHandle CurrentLuma;
            public TextureHandle FrameInfo;
            public Vector2Int RenderSize;
            public Vector2Int PreviousRenderSize;
            public Vector2Int OutputSize;
            public Vector2Int PreviousOutputSize;
            public Vector2 Jitter;
            public Vector2 PreviousJitter;
            public Vector2 MotionVectorScale;
            public Vector4 DeviceToViewDepth;
            public int JitterSequenceLength;
            public float TanHalfFov;
            public float DeltaTime;
            public float FrameIndex;
            public bool ResetHistory;
            public bool EnableSharpening;
            public int[] RcasConfig;

            public Vector2Int SpdDispatchSize => new(
                DivRoundUp(RenderSize.x, SpdThreadGroupSize),
                DivRoundUp(RenderSize.y, SpdThreadGroupSize));

            public int SpdMipCount => Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Log(Mathf.Max(1, Mathf.Max(RenderSize.x, RenderSize.y)), 2.0f)),
                1,
                MaxSpdMips);
        }

        internal sealed class CameraState : IDisposable
        {
            private readonly RTHandle[] m_Accumulation = new RTHandle[2];
            private readonly RTHandle[] m_InternalUpscaled = new RTHandle[2];
            private readonly RTHandle[] m_LumaHistory = new RTHandle[2];
            private readonly RTHandle[] m_Luma = new RTHandle[2];
            private RTHandle m_FrameInfo;
            private int m_ResourceIndex;
            private Vector2Int m_RenderSize;
            private Vector2Int m_OutputSize;
            private VividFsr3QualityMode m_Quality;
            private bool m_HasValidHistory;

            public int LastUsedFrame { get; set; }
            public Vector2Int PreviousRenderSize { get; private set; } = Vector2Int.one;
            public Vector2Int PreviousOutputSize { get; private set; } = Vector2Int.one;
            public Vector2 PreviousJitter { get; private set; }
            public float AccumulatedFrameIndex { get; private set; }

            public bool Prepare(
                Vector2Int renderSize,
                Vector2Int outputSize,
                VividFsr3QualityMode quality,
                int frameIndex,
                bool forceResetHistory)
            {
                var resetHistory = forceResetHistory
                    || !m_HasValidHistory
                    || m_RenderSize != renderSize
                    || m_OutputSize != outputSize
                    || m_Quality != quality;

                EnsureTextures(renderSize, outputSize);

                if (resetHistory)
                {
                    m_ResourceIndex = 0;
                    AccumulatedFrameIndex = 0.0f;
                    PreviousRenderSize = renderSize;
                    PreviousOutputSize = outputSize;
                    PreviousJitter = Vector2.zero;
                }

                m_RenderSize = renderSize;
                m_OutputSize = outputSize;
                m_Quality = quality;
                m_HasValidHistory = true;
                LastUsedFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
                return resetHistory;
            }

            internal ImportedHandles Import(RenderGraph renderGraph)
            {
                var readIndex = m_ResourceIndex;
                var writeIndex = 1 - m_ResourceIndex;
                return new ImportedHandles(
                    renderGraph.ImportTexture(m_Accumulation[readIndex]),
                    renderGraph.ImportTexture(m_Accumulation[writeIndex]),
                    renderGraph.ImportTexture(m_InternalUpscaled[readIndex]),
                    renderGraph.ImportTexture(m_InternalUpscaled[writeIndex]),
                    renderGraph.ImportTexture(m_LumaHistory[readIndex]),
                    renderGraph.ImportTexture(m_LumaHistory[writeIndex]),
                    renderGraph.ImportTexture(m_Luma[readIndex]),
                    renderGraph.ImportTexture(m_Luma[writeIndex]),
                    renderGraph.ImportTexture(m_FrameInfo));
            }

            public void CommitFrame(Vector2Int renderSize, Vector2Int outputSize, Vector2 passDataJitter)
            {
                PreviousRenderSize = renderSize;
                PreviousOutputSize = outputSize;
                PreviousJitter = passDataJitter;
                AccumulatedFrameIndex += 1.0f;
                m_ResourceIndex = 1 - m_ResourceIndex;
            }

            public void ClearHistory(CommandBuffer cmd)
            {
                if (cmd == null)
                    return;

                for (var i = 0; i < 2; i++)
                {
                    ClearRTHandle(cmd, m_Accumulation[i], Color.clear);
                    ClearRTHandle(cmd, m_InternalUpscaled[i], Color.clear);
                    ClearRTHandle(cmd, m_LumaHistory[i], Color.clear);
                    ClearRTHandle(cmd, m_Luma[i], Color.clear);
                }

                ClearRTHandle(cmd, m_FrameInfo, new Color(-1.0f, 1.0f, 0.0f, 0.0f));
            }

            public void Dispose()
            {
                ReleaseArray(m_Accumulation);
                ReleaseArray(m_InternalUpscaled);
                ReleaseArray(m_LumaHistory);
                ReleaseArray(m_Luma);
                m_FrameInfo?.Release();
                m_FrameInfo = null;
                m_HasValidHistory = false;
            }

            private void EnsureTextures(Vector2Int renderSize, Vector2Int outputSize)
            {
                for (var i = 0; i < 2; i++)
                {
                    EnsureHandle(
                        ref m_Accumulation[i],
                        renderSize.x,
                        renderSize.y,
                        GraphicsFormat.R8_UNorm,
                        $"FSR3_Accumulation{i + 1}");
                    EnsureHandle(
                        ref m_InternalUpscaled[i],
                        outputSize.x,
                        outputSize.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        $"FSR3_InternalUpscaled{i + 1}");
                    EnsureHandle(
                        ref m_LumaHistory[i],
                        renderSize.x,
                        renderSize.y,
                        GraphicsFormat.R16G16B16A16_SFloat,
                        $"FSR3_LumaHistory{i + 1}");
                    EnsureHandle(
                        ref m_Luma[i],
                        renderSize.x,
                        renderSize.y,
                        GraphicsFormat.R16_SFloat,
                        $"FSR3_Luma{i + 1}");
                }

                EnsureHandle(ref m_FrameInfo, 1, 1, GraphicsFormat.R32G32B32A32_SFloat, "FSR3_FrameInfo");
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
