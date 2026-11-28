using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class VividSdfScreenTraceData : ContextItem, IFrameTextureExtractor
    {
        internal CameraHistoryTexture radiance;
        internal CameraHistoryTexture geometry;

        internal CameraHistoryTexture sceneColor;
        internal CameraHistoryTexture sceneDepthExposure;
        internal TextureHandle currentColor, currentDepthExposure;
        internal BufferHandle preExposure;
        internal RenderGraphTexture depth;
        internal ComputeShader shader;
        internal int kernel;

        bool IFrameTextureExtractor.TryExtractTexture(
            RenderGraph renderGraph,
            FrameResourceSlotId slot,
            RenderGraphTexture sourceTexture,
            TextureHandle sourceHandle,
            in FrameResourceExtractionContext context)
        {
            if (!currentColor.IsValid() || !currentDepthExposure.IsValid() || !preExposure.IsValid())
                return false;
            if (sourceTexture?.desc == null || depth?.desc == null || !sourceHandle.IsValid())
                return false;
            if (sourceTexture.desc.Width != depth.desc.Width || sourceTexture.desc.Height != depth.desc.Height)
                return false;

            var depthHandle = context.GetOrCreateTextureHandle(depth);
            if (!depthHandle.IsValid())
                return false;

            RecordSceneColor(renderGraph, sourceHandle, depthHandle);
            return true;
        }

        private sealed class CaptureData
        {
            internal TextureHandle source, depth, color, depthExposure;
            internal BufferHandle preExposure;
            internal ComputeShader shader;
            internal int kernel, width, height;
            internal CameraHistoryTexture colorHistory, depthHistory;
        }

        private static readonly int Source = Shader.PropertyToID("_SdfResolvedSceneColor");
        private static readonly int Depth = Shader.PropertyToID("_SdfDiffuseDepth");
        private static readonly int Output = Shader.PropertyToID("_SdfSceneColorOutput");
        private static readonly int DepthExposure = Shader.PropertyToID("_SdfDepthExposureOutput");
        private static readonly BaseRenderFunc<CaptureData, ComputeGraphContext> Capture = ExecuteCapture;

        internal void RecordSceneColor(RenderGraph graph, TextureHandle source, TextureHandle depthInput)
        {
            using var builder = graph.AddComputePass<CaptureData>("Screen Probe SceneColor History", out var data);
            data.source = source;
            data.depth = depthInput;
            data.color = currentColor;
            data.depthExposure = currentDepthExposure;
            data.preExposure = preExposure;
            data.shader = shader;
            data.kernel = kernel;
            data.width = depth.desc.Width;
            data.height = depth.desc.Height;
            data.colorHistory = sceneColor;
            data.depthHistory = sceneDepthExposure;
            builder.UseBuffer(preExposure, AccessFlags.Read);
            builder.UseTexture(source, AccessFlags.Read);
            builder.UseTexture(depthInput, AccessFlags.Read);
            builder.UseTexture(data.color, AccessFlags.Write);
            builder.UseTexture(data.depthExposure, AccessFlags.Write);
            builder.SetRenderFunc(Capture);
        }

        private static void ExecuteCapture(CaptureData data, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            cmd.SetComputeBufferParam(data.shader, data.kernel, VividAutoExposureSystem.PreExposureBufferId, data.preExposure);
            cmd.SetComputeTextureParam(data.shader, data.kernel, Source, data.source);
            cmd.SetComputeTextureParam(data.shader, data.kernel, Depth, data.depth);
            cmd.SetComputeTextureParam(data.shader, data.kernel, Output, data.color);
            cmd.SetComputeTextureParam(data.shader, data.kernel, DepthExposure, data.depthExposure);
            cmd.DispatchCompute(data.shader, data.kernel, CoreUtils.DivRoundUp(data.width, 8), CoreUtils.DivRoundUp(data.height, 8), 1);
            data.colorHistory.MarkWritten();
            data.depthHistory.MarkWritten();
        }

        public override void Reset()
        {
            radiance = null;
            geometry = sceneColor = sceneDepthExposure = null;
            currentColor = currentDepthExposure = default;
            preExposure = default;
            depth = null;
            shader = null;
        }
    }
}
