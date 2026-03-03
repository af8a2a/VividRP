using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : UnityEngine.Rendering.RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private readonly VividRenderPipelineAsset m_Asset;
        private readonly UnityEngine.Rendering.RenderGraphModule.RenderGraph m_RenderGraph;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;

            PipelineResourceManager.Initialize();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            Blitter.Initialize(resources.CoreBlitShader, resources.CoreBlitColorAndDepthShader);

            m_RenderGraph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph("VividRP RenderGraph");
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

            // var graphAsset = m_Asset.RenderGraphAsset;
            // if (graphAsset != null)
            {
                var renderGraphParams = new RenderGraphParameters
                {
                    scriptableRenderContext = context,
                    commandBuffer = cmdBuffer,
                    currentFrameIndex = Time.frameCount
                };

                m_RenderGraph.BeginRecording(renderGraphParams);

                
                m_RenderGraph.EndRecordingAndExecute();
            }


            context.ExecuteCommandBuffer(cmdBuffer);
            CommandBufferPool.Release(cmdBuffer);

            context.Submit();
            m_RenderGraph.EndFrame();
            EndCameraRendering(context, camera);
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderGraph?.Cleanup();
            Blitter.Cleanup();
            PipelineResourceManager.Cleanup();
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public bool isImmediateModeSupported => false;
    }
}