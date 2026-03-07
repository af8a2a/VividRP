using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private VividRenderPipelineAsset m_Asset;
        private RenderGraph m_RenderGraph;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;

            PipelineResourceManager.Initialize();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            Blitter.Initialize(resources.CoreBlitShader, resources.CoreBlitColorAndDepthShader);

            m_RenderGraph = new RenderGraph("VividRP RenderGraph");
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras)
                RenderCamera(context, camera);

            m_RenderGraph.EndFrame();
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            BeginCameraRendering(context, camera);

            if (!camera.TryGetCullingParameters(out var cullingParameters))
            {
                EndCameraRendering(context, camera);
                return;
            }

            var cullingResults = context.Cull(ref cullingParameters);
            context.SetupCameraProperties(camera);

            var cmdBuffer = CommandBufferPool.Get("VividRP");

            PassRecorder.InitializeContext(context, camera, cullingResults);
            var graphAsset = m_Asset.RenderGraphAsset;
            PassRecorder.PrepareFrame(graphAsset, cmdBuffer);
            context.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();

            var renderGraphParams = new RenderGraphParameters
            {
                scriptableRenderContext = context,
                commandBuffer = cmdBuffer,
                currentFrameIndex = Time.frameCount,
                executionId = camera.GetEntityId(),
                generateDebugData = camera.cameraType != CameraType.Preview && !camera.isProcessingRenderRequest,
            };

            m_RenderGraph.BeginRecording(renderGraphParams);
            PassRecorder.RecordRenderGraph(m_RenderGraph, context, graphAsset);
            m_RenderGraph.EndRecordingAndExecute();

            context.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);

            context.Submit();
            EndCameraRendering(context, camera);
        }

        protected override void Dispose(bool disposing)
        {
            PassRecorder.Dispose();

            m_RenderGraph?.Cleanup();
            m_RenderGraph = null;
            Blitter.Cleanup();
            PipelineResourceManager.Cleanup();
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public bool isImmediateModeSupported => false;
    }
}

