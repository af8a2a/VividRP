using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private const string RenderGraphName = "VividRP RenderGraph";
        private static readonly ProfilerMarker s_ContextSubmitMarker = new("VividRP.RenderPipeline.RenderCamera.ContextSubmit");
        private static readonly ShaderTagId[] s_PreviewCameraShaderTagIds =
        {
            new(RenderGraphRenderListDesc.ForwardShaderTagName),
            new("ForwardOnly"),
            new("Forward"),
            new(RenderGraphRenderListDesc.DefaultUnlitShaderTagName),
            new("UniversalForwardOnly"),
            new("UniversalForward"),
            new("ForwardBase"),
            new("Always"),
        };

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
#if DLSS_PLUGIN_INTEGRATE
            DLSSExtension.Initialize();
#endif

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
                cullingParameters.cullingOptions = ResolveCullingOptions(camera.cameraType, cullingParameters.cullingOptions);

                var cullingResults = context.Cull(ref cullingParameters);

                cmdBuffer = CommandBufferPool.Get("VividRP");

                if (ShouldUsePreviewCameraRenderPath(camera.cameraType))
                {
                    RenderPreviewCamera(context, camera, cullingResults, cmdBuffer);
                    shouldSubmit = true;
                    return;
                }

                var graphAsset = m_Asset.RenderGraphAsset;
                PassRecorder.InitializeContext(context, camera, cullingResults, graphAsset);
                context.SetupCameraProperties(camera);

                PassRecorder.PrepareFrame(graphAsset, cmdBuffer);

                shouldSubmit = true;
                context.ExecuteCommandBuffer(cmdBuffer);
                cmdBuffer.Clear();

                var renderGraphParams = new RenderGraphParameters
                {
                    scriptableRenderContext = context,
                    commandBuffer = cmdBuffer,
                    currentFrameIndex = PassRecorder.GetFrameData().Get<VividCameraData>()?.frameIndex ?? Time.frameCount,
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
                RequestEditorTemporalRepaint();

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
                    {
                        using (s_ContextSubmitMarker.Auto())
                        {
                            context.Submit();
                        }
                    }
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
            // SceneView editor world geometry participates in culling and would pollute SSR inputs.
            return false;
        }

        internal static bool CanRenderGizmos(CameraType cameraType)
        {
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        internal static bool CanRenderGizmos(Camera camera)
        {
            return camera != null && CanRenderGizmos(camera.cameraType);
        }

        internal static bool ShouldUsePreviewCameraRenderPath(CameraType cameraType)
        {
            return cameraType == CameraType.Preview;
        }

        internal static CullingOptions ResolveCullingOptions(CameraType cameraType, CullingOptions cullingOptions)
        {
            return ShouldUsePreviewCameraRenderPath(cameraType)
                ? cullingOptions | CullingOptions.DisablePerObjectCulling
                : cullingOptions;
        }

        internal static bool ShouldRenderPreImageEffectGizmosInRenderGraph(CameraType cameraType)
        {
            return cameraType == CameraType.Game;
        }

        internal static bool ShouldRenderPreImageEffectGizmosInRenderGraph(Camera camera)
        {
            return camera != null && ShouldRenderPreImageEffectGizmosInRenderGraph(camera.cameraType);
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

        private static void RenderPreviewCamera(
            ScriptableRenderContext context,
            Camera camera,
            CullingResults cullingResults,
            CommandBuffer cmdBuffer)
        {
            context.SetupCameraProperties(camera);
            ClearPreviewCameraTarget(cmdBuffer, camera);

            DrawPreviewCameraRenderers(
                context,
                cmdBuffer,
                camera,
                cullingResults,
                RenderQueueRange.opaque,
                SortingCriteria.CommonOpaque);

            if (camera.clearFlags == CameraClearFlags.Skybox)
                DrawPreviewCameraSkybox(context, cmdBuffer, camera);

            DrawPreviewCameraRenderers(
                context,
                cmdBuffer,
                camera,
                cullingResults,
                RenderQueueRange.transparent,
                SortingCriteria.CommonTransparent);

            context.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();
        }

        private static void ClearPreviewCameraTarget(CommandBuffer cmdBuffer, Camera camera)
        {
            if (cmdBuffer == null || camera == null)
                return;

            cmdBuffer.ClearRenderTarget(true, true, ResolvePreviewCameraClearColor(camera));
        }

        private static Color ResolvePreviewCameraClearColor(Camera camera)
        {
            if (camera != null && camera.clearFlags == CameraClearFlags.SolidColor)
                return camera.backgroundColor;

#if UNITY_EDITOR
            return CoreRenderPipelinePreferences.previewBackgroundColor;
#else
            return camera != null ? camera.backgroundColor : Color.black;
#endif
        }

        private static void DrawPreviewCameraRenderers(
            ScriptableRenderContext context,
            CommandBuffer cmdBuffer,
            Camera camera,
            CullingResults cullingResults,
            RenderQueueRange renderQueueRange,
            SortingCriteria sortingCriteria)
        {
            if (cmdBuffer == null || camera == null)
                return;

            var desc = new RendererListDesc(s_PreviewCameraShaderTagIds, cullingResults, camera)
            {
                renderQueueRange = renderQueueRange,
                sortingCriteria = sortingCriteria,
                layerMask = camera.cullingMask,
                renderingLayerMask = uint.MaxValue,
                rendererConfiguration = PerObjectData.None
            };

            if (!desc.IsValid())
                return;

            var rendererList = context.CreateRendererList(desc);
            CoreUtils.DrawRendererList(cmdBuffer, rendererList);
        }

        private static void DrawPreviewCameraSkybox(
            ScriptableRenderContext context,
            CommandBuffer cmdBuffer,
            Camera camera)
        {
            if (cmdBuffer == null || camera == null)
                return;

            var rendererList = context.CreateSkyboxRendererList(camera);
            CoreUtils.DrawRendererList(cmdBuffer, rendererList);
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

        private static void RequestEditorTemporalRepaint()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            var antialiasingData = PassRecorder.GetFrameData().Get<VividAntialiasingData>();
            if (antialiasingData == null || !antialiasingData.usesTemporalJitter)
                return;

            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
#if DLSS_PLUGIN_INTEGRATE
            DLSSExtension.Shutdown();
#endif
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
