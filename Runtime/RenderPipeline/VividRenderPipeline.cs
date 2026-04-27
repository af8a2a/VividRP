using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private const string RenderGraphName = "VividRP RenderGraph";

        private readonly VividRenderPipelineAsset m_Asset;
        private readonly bool m_PreviousUseScriptableRenderPipelineBatching;
        private DebugDisplaySettingsUI m_DebugDisplaySettingsUI;
        private RenderGraph m_RenderGraph;

        public VividRenderPipeline(VividRenderPipelineAsset asset)
        {
            m_Asset = asset;
            m_PreviousUseScriptableRenderPipelineBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
            ApplySRPBatcherSetting(asset);

            VividVolumeManagerUtility.Initialize();
            // PipelineResourceManager.Initialize();
            // SkyManager.Initialize();
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            Blitter.Initialize(resources.CoreBlitShader, resources.CoreBlitColorAndDepthShader);
            BlueNoise.Initialize();
            Hammersley.Initialize();
            RTHandles.Initialize(Screen.width, Screen.height);
            LensFlareCommonSRP.Initialize();
            VividAdaptiveProbeVolumeUtility.Initialize(asset);
            VividGPUDrivenSystem.Initialize();
            DLSSExtension.Initialize();

            m_RenderGraph = new RenderGraph(RenderGraphName);
            m_DebugDisplaySettingsUI = new DebugDisplaySettingsUI();
            m_DebugDisplaySettingsUI.RegisterDebug(VividRenderingDebugDisplaySettings.Instance);
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
            var projectionState = CameraProjectionMatrixUtility.CaptureProjectionState(camera);

            try
            {
                CameraProjectionMatrixUtility.RestoreProjectionState(camera, projectionState);
                VividVolumeManagerUtility.Update(camera);

                if (!camera.TryGetCullingParameters(out var cullingParameters))
                    return;

                EmitGeometryForCamera(camera);
                ApplyShadowDistanceOverride(camera, ref cullingParameters);
                var cullingResults = context.Cull(ref cullingParameters);

                cmdBuffer = CommandBufferPool.Get("VividRP");

                PassRecorder.InitializeContext(context, camera, cullingResults);
                context.SetupCameraProperties(camera);

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

                PassRecorder.CommitFrame(graphAsset);
                context.SetupCameraProperties(camera);
                FrameContextSystem.ExecutePostRender(PassRecorder.GetFrameData(), cmdBuffer);

                context.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();
                RenderSubmittedGizmos(context, camera, graphAsset);
            }
            finally
            {
                if (cmdBuffer != null)
                {
                    cmdBuffer.Clear();
                    CommandBufferPool.Release(cmdBuffer);
                }

                try
                {
                    if (shouldSubmit)
                        context.Submit();
                }
                finally
                {
                    CameraProjectionMatrixUtility.RestoreProjectionState(camera, projectionState);
                }

                EndCameraRendering(context, camera);
            }
        }

        private static void ApplyShadowDistanceOverride(Camera camera, ref ScriptableCullingParameters cullingParameters)
        {
            if (camera == null)
                return;

            var shadowDistance = Mathf.Min(cullingParameters.shadowDistance, camera.farClipPlane);
            var csmSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            if (csmSettings != null && csmSettings.IsActive())
                shadowDistance = Mathf.Min(shadowDistance, csmSettings.maxShadowDistance.value);

            cullingParameters.shadowDistance = Mathf.Max(0.0f, shadowDistance);
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

        internal static bool ShouldEmitWorldGeometry(CameraType cameraType)
        {
            return cameraType == CameraType.SceneView;
        }

        internal static bool CanRenderGizmos(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        internal static bool CanRenderGizmos(Camera camera)
        {
            return camera != null && CanRenderGizmos(camera.cameraType);
        }

        private static void EmitGeometryForCamera(Camera camera)
        {
#if UNITY_EDITOR
            if (camera == null)
                return;

            if (ShouldEmitWorldGeometry(camera.cameraType))
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif
        }

        private static void RenderSubmittedGizmos(
            ScriptableRenderContext context,
            Camera camera,
            RenderGraphData graphAsset)
        {
#if UNITY_EDITOR
            if (!CanRenderGizmos(camera) || !UnityEditor.Handles.ShouldRenderGizmos())
                return;

            context.SetupCameraProperties(camera);

            if (PassRecorder.ShouldRenderPreImageEffectGizmosOutsideRenderGraph(graphAsset))
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);

            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
#endif
        }

        protected override void Dispose(bool disposing)
        {
            PassRecorder.Dispose();
            VirtualTextureSystem.Deinitialize();
            SkyManager.Deinitialize();
            LTCAreaLightSystem.Deinitialize();
            VividVolumeManagerUtility.Deinitialize();
            DecalSystem.Deinitialize();
            VividGPUDrivenSystem.Deinitialize();
            VividLocalVolumetricFogManager.Dispose();
            DLSSExtension.Shutdown();
            m_DebugDisplaySettingsUI?.UnregisterDebug();
            m_DebugDisplaySettingsUI = null;

            m_RenderGraph?.Cleanup();
            m_RenderGraph = null;
            BlueNoise.Cleanup();
            LensFlareCommonSRP.Dispose();
            Blitter.Cleanup();
            PipelineResourceManager.Cleanup();
            VividAdaptiveProbeVolumeUtility.Cleanup(m_Asset);
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
