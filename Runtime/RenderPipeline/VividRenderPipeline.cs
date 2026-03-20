using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private const string RenderGraphName = "VividRP RenderGraph";

        private readonly VividRenderPipelineAsset m_Asset;
        private readonly bool m_PreviousUseScriptableRenderPipelineBatching;
        private RenderGraph m_RenderGraph;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;
            m_PreviousUseScriptableRenderPipelineBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
            ApplySRPBatcherSetting(asset);

            VividVolumeManagerUtility.Initialize();
            PipelineResourceManager.Initialize();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            Blitter.Initialize(resources.CoreBlitShader, resources.CoreBlitColorAndDepthShader);
            BlueNoise.Initialize();

            m_RenderGraph = new RenderGraph(RenderGraphName);
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            ApplySRPBatcherSetting(m_Asset);

            foreach (var camera in cameras)
                RenderCamera(context, camera);
            m_RenderGraph.EndFrame();
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            BeginCameraRendering(context, camera);

            CommandBuffer cmdBuffer = null;
            var shouldSubmit = false;

            try
            {
                if (!camera.TryGetCullingParameters(out var cullingParameters))
                    return;

                var cullingResults = context.Cull(ref cullingParameters);
                context.SetupCameraProperties(camera);
                VividVolumeManagerUtility.Update(camera);

                cmdBuffer = CommandBufferPool.Get("VividRP");

                PassRecorder.InitializeContext(context, camera, cullingResults);
                var graphAsset = m_Asset.RenderGraphAsset;
                PassRecorder.PrepareFrame(graphAsset, cmdBuffer);

                shouldSubmit = true;
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

                if (!TryRecordAndExecuteRenderGraph(
                        m_RenderGraph,
                        renderGraphParams,
                        () => PassRecorder.RecordRenderGraph(
                            m_RenderGraph,
                            context,
                            graphAsset,
                            m_Asset != null && m_Asset.EnableAsyncCompute),
                        PassRecorder.AbortFrame))
                {
                    return;
                }

                context.ExecuteCommandBuffer(cmdBuffer);
            }
            finally
            {
                if (cmdBuffer != null)
                {
                    cmdBuffer.Clear();
                    CommandBufferPool.Release(cmdBuffer);
                }

                if (shouldSubmit)
                    context.Submit();

                EndCameraRendering(context, camera);
            }
        }

        internal static bool TryRecordAndExecuteRenderGraph(
            RenderGraph renderGraph,
            in RenderGraphParameters renderGraphParams,
            Action recordRenderGraph,
            Action onException = null)
        {
            if (renderGraph == null)
                throw new ArgumentNullException(nameof(renderGraph));
            if (recordRenderGraph == null)
                throw new ArgumentNullException(nameof(recordRenderGraph));

            try
            {
                renderGraph.BeginRecording(renderGraphParams);
                recordRenderGraph();
                renderGraph.EndRecordingAndExecute();
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    onException?.Invoke();
                }
                catch (Exception cleanupException)
                {
                    Debug.LogException(cleanupException);
                }

                renderGraph.ResetGraphAndLogException(exception);
                return false;
            }
        }

        internal static void ReleaseConstantBuffersForShutdown()
        {
            ConstantBuffer.ReleaseAll();
        }

        internal static void ApplySRPBatcherSetting(VividRenderPipelineAsset asset)
        {
            GraphicsSettings.useScriptableRenderPipelineBatching = asset != null && asset.EnableSRPBatcher;
        }

        protected override void Dispose(bool disposing)
        {
            PassRecorder.Dispose();
            VividVolumeManagerUtility.Deinitialize();

            m_RenderGraph?.Cleanup();
            m_RenderGraph = null;
            BlueNoise.Cleanup();
            Blitter.Cleanup();
            PipelineResourceManager.Cleanup();
            ReleaseConstantBuffersForShutdown();

            var currentPipeline = RenderPipelineManager.currentPipeline;
            if (currentPipeline == null || ReferenceEquals(currentPipeline, this))
                GraphicsSettings.useScriptableRenderPipelineBatching = m_PreviousUseScriptableRenderPipelineBatching;

            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public bool isImmediateModeSupported => false;
    }
}
