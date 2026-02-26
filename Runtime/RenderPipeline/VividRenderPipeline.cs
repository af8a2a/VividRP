using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph;
using VividRP.Runtime.RenderGraph.Resource;
using VividRP.Runtime.Utility.PipelineResource;

namespace VividRP.Runtime.RenderPipeline
{
    public class VividRenderPipeline : UnityEngine.Rendering.RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private readonly VividRenderPipelineAsset m_Asset;
        private readonly UnityEngine.Rendering.RenderGraphModule.RenderGraph m_RenderGraph;
        private readonly RenderGraphExecutor m_Executor;
        private readonly HistoryResourceManager m_HistoryManager;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;

            PipelineResourceManager.Initialize();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            // Blitter.Initialize(resources.CoreBlitShader, resources.CoreBlitColorAndDepthShader);

            m_RenderGraph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph("VividRP RenderGraph");
            m_Executor = new RenderGraphExecutor();
            m_HistoryManager = new HistoryResourceManager();
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

                m_HistoryManager.SwapBuffers();
                m_RenderGraph.BeginRecording(renderGraphParams);
                m_Executor.Execute(m_RenderGraph, graphAsset, camera, cullingResults, m_HistoryManager);
                m_RenderGraph.EndRecordingAndExecute();
            }


            context.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);

            context.Submit();

            EndCameraRendering(context, camera);
        }

        protected override void Dispose(bool disposing)
        {
            m_HistoryManager.ReleaseAll();
            m_RenderGraph?.Cleanup();
            Blitter.Cleanup();
            PipelineResourceManager.Cleanup();
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public bool isImmediateModeSupported => false;
    }
}