#if DLSS_PLUGIN_INTEGRATE

using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class DLSSNeuralRenderingPass : IDisposable
    {
        private const int CameraStateExpirationFrames = 400;

        private static readonly ProfilerMarker s_RecordGraphMarker =
            new("VividRP.RenderPass.RecordGraph/DLSS 5 Neural Rendering");
        private static readonly ProfilerMarker s_RecordMarker =
            new("VividRP.RenderPass.Record/DLSS 5 Neural Rendering");
        private static readonly ProfilingSampler s_ProfilingSampler =
            new("DLSS 5 Neural Rendering");
        private static readonly BaseRenderFunc<PassData, UnsafeGraphContext> s_RenderFunc =
            ExecutePass;

        private readonly Dictionary<EntityId, CameraState> m_CameraStates = new();
        private readonly List<EntityId> m_ExpiredCameraIds = new();

        public bool IsSupported =>
            DLSSExtension.Initialize() && DLSSExtension.IsNeuralRenderingSupported;

        public bool Record(
            RenderGraph renderGraph,
            VividCameraData cameraData,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture depthTexture,
            RenderGraphTexture motionTexture,
            RenderGraphTexture outputTexture,
            Vector2Int inputSize,
            Vector2Int outputSize,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache,
            bool resetHistory)
        {
            using var recordGraphScope = s_RecordGraphMarker.Auto();
            if (!IsSupported
                || renderGraph == null
                || cameraData?.camera == null
                || sourceTexture?.innerHandle.IsValid() != true
                || depthTexture?.innerHandle.IsValid() != true
                || motionTexture?.innerHandle.IsValid() != true
                || outputTexture?.desc == null
                || inputSize.x <= 0
                || inputSize.y <= 0
                || outputSize.x <= 0
                || outputSize.y <= 0)
            {
                return false;
            }

            VividAdditionalCameraData additionalData = cameraData.additionalData;
            bool upscaling = additionalData != null
                && additionalData.dlssNeuralRenderingUpscaling
                && outputSize.x == inputSize.x * 2
                && outputSize.y == inputSize.y * 2;
            CameraState cameraState = GetOrCreateCameraState(
                cameraData.camera,
                cameraData.frameIndex);
            CleanupExpiredCameraStates(cameraData.frameIndex);

            TextureHandle outputHandle = renderGraph.CreateTexture(outputTexture.desc);
            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "DLSS 5 Neural Rendering",
                       out PassData passData,
                       s_ProfilingSampler))
            {
                passData.State = cameraState;
                passData.Source = sourceTexture.innerHandle;
                passData.Depth = depthTexture.innerHandle;
                passData.MotionVectors = motionTexture.innerHandle;
                passData.Output = outputHandle;
                passData.ResetHistory = resetHistory;
                passData.Upscaling = upscaling;
                passData.Preset = additionalData != null
                    ? additionalData.dlssNeuralRenderingPreset
                    : DLSSNeuralRenderingPreset.Default;
                passData.Style = additionalData != null
                    ? additionalData.dlssNeuralRenderingStyle
                    : DLSSNeuralRenderingStyle.Default;
                passData.Intensity = additionalData?.dlssNeuralRenderingIntensity ?? 1.0f;
                passData.LocalToneStrength = additionalData?.dlssNeuralRenderingLocalToneStrength ?? 1.0f;
                passData.LocalStructureStrength = additionalData?.dlssNeuralRenderingLocalStructureStrength ?? 1.0f;
                passData.SkinStructureStrength = additionalData?.dlssNeuralRenderingSkinStructureStrength ?? -1.0f;
                passData.UseAutoMask = additionalData != null && additionalData.dlssNeuralRenderingUseAutoMask;
                passData.UICorrection = additionalData != null && additionalData.dlssNeuralRenderingUICorrection;

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.SetRandomAccessAttachment(passData.Output, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(s_RenderFunc);
            }

            outputTexture.innerHandle = outputHandle;
            textureCache[outputTexture] = outputHandle;
            return true;
        }

        public void Dispose()
        {
            foreach (CameraState state in m_CameraStates.Values)
                state.Dispose();

            m_CameraStates.Clear();
            m_ExpiredCameraIds.Clear();
        }

        private CameraState GetOrCreateCameraState(Camera camera, int frameIndex)
        {
            EntityId cameraId = camera.GetEntityId();
            if (!m_CameraStates.TryGetValue(cameraId, out CameraState state))
            {
                state = new CameraState();
                m_CameraStates.Add(cameraId, state);
            }

            state.LastUsedFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
            return state;
        }

        private void CleanupExpiredCameraStates(int frameIndex)
        {
            int currentFrame = frameIndex >= 0 ? frameIndex : Time.frameCount;
            m_ExpiredCameraIds.Clear();

            foreach (KeyValuePair<EntityId, CameraState> pair in m_CameraStates)
            {
                if (currentFrame - pair.Value.LastUsedFrame > CameraStateExpirationFrames)
                    m_ExpiredCameraIds.Add(pair.Key);
            }

            for (int index = 0; index < m_ExpiredCameraIds.Count; index++)
            {
                EntityId cameraId = m_ExpiredCameraIds[index];
                if (!m_CameraStates.TryGetValue(cameraId, out CameraState state))
                    continue;

                state.Dispose();
                m_CameraStates.Remove(cameraId);
            }
        }

        private static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            using var recordScope = s_RecordMarker.Auto();
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            data.State.Execute(cmd, data);
        }

        internal sealed class PassData
        {
            public CameraState State;
            public TextureHandle Source;
            public TextureHandle Depth;
            public TextureHandle MotionVectors;
            public TextureHandle Output;
            public bool ResetHistory;
            public bool Upscaling;
            public DLSSNeuralRenderingPreset Preset;
            public DLSSNeuralRenderingStyle Style;
            public float Intensity;
            public float LocalToneStrength;
            public float LocalStructureStrength;
            public float SkinStructureStrength;
            public bool UseAutoMask;
            public bool UICorrection;
        }

        internal sealed class CameraState : IDisposable
        {
            private readonly DLSSNeuralRenderingSettings m_Settings = new();
            private DLSSNeuralRendering m_NeuralRendering;

            public int LastUsedFrame { get; set; }

            public void Execute(CommandBuffer cmd, PassData data)
            {
                if (cmd == null || data == null)
                    return;

                RenderTexture source = data.Source;
                RenderTexture depth = data.Depth;
                RenderTexture motionVectors = data.MotionVectors;
                RenderTexture output = data.Output;
                if (source == null || depth == null || motionVectors == null || output == null)
                    return;

                m_NeuralRendering ??= new DLSSNeuralRendering();
                m_Settings.Preset = data.Preset;
                m_Settings.Style = data.Style;
                m_Settings.Intensity = data.Intensity;
                m_Settings.LocalToneStrength = data.LocalToneStrength;
                m_Settings.LocalStructureStrength = data.LocalStructureStrength;
                m_Settings.SkinStructureStrength = data.SkinStructureStrength;
                m_Settings.DepthInverted = SystemInfo.usesReversedZBuffer;
                m_Settings.UseAutoMask = data.UseAutoMask;
                m_Settings.UICorrection = data.UICorrection;
                m_Settings.Upscaling = data.Upscaling;
                m_Settings.MotionVectorScale = Vector2.one;

                m_NeuralRendering.Render(
                    cmd,
                    source,
                    output,
                    depth,
                    motionVectors,
                    DLSSMotionVectorEncoding.VividNormalizedUV,
                    m_Settings,
                    data.ResetHistory);
            }

            public void Dispose()
            {
                m_NeuralRendering?.Dispose();
                m_NeuralRendering = null;
            }
        }
    }
}

#endif
