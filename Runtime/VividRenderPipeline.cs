using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph;
using VividRP.Runtime.Utility;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline
    {
        private readonly VividRenderPipelineAsset m_Asset;
        private readonly UnityEngine.Rendering.RenderGraphModule.RenderGraph m_RenderGraph;
        private readonly RenderGraphExecutor m_Executor;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;
            VividResourceManager.Initialize();
            m_RenderGraph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph("VividRP RenderGraph");
            m_Executor = new RenderGraphExecutor();
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
                RenderCamera(context, camera);
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras)
                RenderCamera(context, camera);
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            BeginCameraRendering(context, camera);

            if (!camera.TryGetCullingParameters(out var cullingParams))
            {
                EndCameraRendering(context, camera);
                return;
            }

            var cullingResults = context.Cull(ref cullingParams);

            context.SetupCameraProperties(camera);

            var cmdBuffer = CommandBufferPool.Get("VividRP");

            var graphAsset = m_Asset.RenderGraphAsset;
            if (graphAsset != null)
            {
                var renderGraphParams = new RenderGraphParameters
                {
                    scriptableRenderContext = context,
                    commandBuffer = cmdBuffer,
                    currentFrameIndex = Time.frameCount
                };

                m_RenderGraph.BeginRecording(renderGraphParams);
                m_Executor.Execute(m_RenderGraph, graphAsset, camera, cullingResults);
                m_RenderGraph.EndRecordingAndExecute();
            }

            context.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);

            context.Submit();

            EndCameraRendering(context, camera);
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderGraph?.Cleanup();
            base.Dispose(disposing);
        }
    }
}
