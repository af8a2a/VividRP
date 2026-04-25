using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    internal sealed class DLSSPass : IDisposable
    {
        private const int CameraStateExpirationFrames = 400;

        private static readonly ProfilingSampler s_ProfilingSampler = new("DLSS Super Resolution");

        private readonly Dictionary<EntityId, CameraState> m_CameraStates = new();
        private readonly List<EntityId> m_ExpiredCameraIds = new();

        public bool IsSupported => DLSSExtension.Initialize() && DLSSExtension.IsSuperResolutionSupported;

        public RenderGraphTexture Record(
            RenderGraph renderGraph,
            VividCameraData cameraData,
            CameraTemporalData temporalData,
            RenderGraphTexture sourceTexture,
            RenderGraphTexture depthTexture,
            RenderGraphTexture motionTexture,
            Dictionary<RenderGraphTexture, TextureHandle> textureCache)
        {
            if (!IsSupported
                || renderGraph == null
                || cameraData?.camera == null
                || sourceTexture?.innerHandle.IsValid() != true
                || depthTexture?.innerHandle.IsValid() != true
                || motionTexture?.innerHandle.IsValid() != true)
            {
                return null;
            }

            var currentImageSize = ResolveRenderSize(cameraData);
            var outputImageSize = ResolveOutputSize(cameraData, currentImageSize);
            if (currentImageSize.x <= 0 || currentImageSize.y <= 0 || outputImageSize.x <= 0 || outputImageSize.y <= 0)
                return null;

            var cameraState = GetOrCreateCameraState(cameraData.camera, cameraData.frameIndex);
            CleanupExpiredCameraStates(cameraData.frameIndex);

            var outputDescriptor = CreateOutputDescriptor(sourceTexture.desc, outputImageSize);
            var outputHandle = renderGraph.CreateTexture(outputDescriptor);

            using (var builder = renderGraph.AddUnsafePass<PassData>(
                       "DLSS Super Resolution",
                       out var passData,
                       s_ProfilingSampler))
            {
                passData.State = cameraState;
                passData.Quality = cameraData.additionalData != null
                    ? cameraData.additionalData.dlssQuality
                    : DLSSQuality.Balanced;
                passData.ResetHistory = temporalData == null || temporalData.IsFirstFrame;
                passData.Source = sourceTexture.innerHandle;
                passData.Depth = depthTexture.innerHandle;
                passData.MotionVectors = motionTexture.innerHandle;
                passData.Output = outputHandle;
                passData.InputWidth = currentImageSize.x;
                passData.InputHeight = currentImageSize.y;
                passData.JitterX = -cameraData.jitter.x * currentImageSize.x;
                passData.JitterY = -cameraData.jitter.y * currentImageSize.y;
                passData.PreExposure = 1.0f;

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Depth, AccessFlags.Read);
                builder.UseTexture(passData.MotionVectors, AccessFlags.Read);
                builder.SetRandomAccessAttachment(passData.Output, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    data.State.Execute(cmd, data);
                });
            }

            var outputTexture = new RenderGraphTexture
            {
                desc = outputDescriptor,
                innerHandle = outputHandle,
            };
            textureCache[outputTexture] = outputHandle;
            return outputTexture;
        }

        public void Dispose()
        {
            foreach (var state in m_CameraStates.Values)
                state.Dispose();

            m_CameraStates.Clear();
            m_ExpiredCameraIds.Clear();
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
            return new Vector2Int(outputWidth, outputHeight);
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

            descriptor.Name = "DLSSOutput";
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

        internal sealed class PassData
        {
            public CameraState State;
            public TextureHandle Source;
            public TextureHandle Depth;
            public TextureHandle MotionVectors;
            public TextureHandle Output;
            public DLSSQuality Quality;
            public bool ResetHistory;
            public int InputWidth;
            public int InputHeight;
            public float JitterX;
            public float JitterY;
            public float PreExposure;
        }

        internal sealed class CameraState : IDisposable
        {
            private DLSSSuperResolution m_SuperResolution;
            private DLSSQuality m_Quality = DLSSQuality.Balanced;

            public int LastUsedFrame { get; set; }

            public void Execute(CommandBuffer cmd, PassData data)
            {
                if (cmd == null || data == null)
                    return;

                m_SuperResolution ??= new DLSSSuperResolution(
                    NVSDK_NGX_DLSS_Feature_Flags.IsHDR | NVSDK_NGX_DLSS_Feature_Flags.DepthInverted,
                    data.Quality.ToNGXQuality());

                if (m_Quality != data.Quality)
                {
                    m_Quality = data.Quality;
                    m_SuperResolution.SetQuality(data.Quality.ToNGXQuality());
                }

                RenderTexture source = data.Source;
                RenderTexture depth = data.Depth;
                RenderTexture motionVectors = data.MotionVectors;
                RenderTexture output = data.Output;

                if (source == null || depth == null || motionVectors == null || output == null)
                    return;

                m_SuperResolution.Render(
                    cmd,
                    source,
                    output,
                    depth,
                    motionVectors,
                    data.JitterX,
                    data.JitterY,
                    -data.InputWidth,
                    -data.InputHeight,
                    data.ResetHistory,
                    data.PreExposure);
            }

            public void Dispose()
            {
                m_SuperResolution?.Dispose();
                m_SuperResolution = null;
            }
        }
    }
}
