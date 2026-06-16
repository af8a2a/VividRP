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
        private static readonly ProfilerMarker s_RenderMarker = new("VividRP.RenderPipeline.Render");
        private static readonly ProfilerMarker s_ApplySRPBatcherMarker = new("VividRP.RenderPipeline.ApplySRPBatcherSetting");
        private static readonly ProfilerMarker s_EndFrameMarker = new("VividRP.RenderPipeline.RenderGraph.EndFrame");
        private static readonly ProfilerMarker s_RenderCameraMarker = new("VividRP.RenderPipeline.RenderCamera");
        private static readonly ProfilerMarker s_BeginCameraRenderingMarker = new("VividRP.RenderPipeline.RenderCamera.BeginCameraRendering");
        private static readonly ProfilerMarker s_RestoreProjectionStateMarker = new("VividRP.RenderPipeline.RenderCamera.RestoreProjectionState");
        private static readonly ProfilerMarker s_HDRStateMarker = new("VividRP.RenderPipeline.RenderCamera.HDRState");
        private static readonly ProfilerMarker s_VolumeUpdateMarker = new("VividRP.RenderPipeline.RenderCamera.VolumeUpdate");
        private static readonly ProfilerMarker s_CullingParametersMarker = new("VividRP.RenderPipeline.RenderCamera.CullingParameters");
        private static readonly ProfilerMarker s_EmitGeometryMarker = new("VividRP.RenderPipeline.RenderCamera.EmitGeometry");
        private static readonly ProfilerMarker s_ShadowDistanceMarker = new("VividRP.RenderPipeline.RenderCamera.ShadowDistance");
        private static readonly ProfilerMarker s_CullMarker = new("VividRP.RenderPipeline.RenderCamera.Cull");
        private static readonly ProfilerMarker s_CommandBufferGetMarker = new("VividRP.RenderPipeline.RenderCamera.CommandBuffer.Get");
        private static readonly ProfilerMarker s_PreviewCameraMarker = new("VividRP.RenderPipeline.RenderCamera.Preview");
        private static readonly ProfilerMarker s_SetupCameraPropertiesMarker = new("VividRP.RenderPipeline.RenderCamera.SetupCameraProperties");
        private static readonly ProfilerMarker s_PrepareFrameMarker = new("VividRP.RenderPipeline.RenderCamera.PrepareFrame");
        private static readonly ProfilerMarker s_ExecutePreGraphCommandsMarker = new("VividRP.RenderPipeline.RenderCamera.ExecutePreGraphCommands");
        private static readonly ProfilerMarker s_RenderGraphParametersMarker = new("VividRP.RenderPipeline.RenderCamera.RenderGraphParameters");
        private static readonly ProfilerMarker s_RecordAndExecuteRenderGraphMarker = new("VividRP.RenderPipeline.RenderCamera.RecordAndExecuteRenderGraph");
        private static readonly ProfilerMarker s_CommitFrameMarker = new("VividRP.RenderPipeline.RenderCamera.CommitFrame");
        private static readonly ProfilerMarker s_PostRenderMarker = new("VividRP.RenderPipeline.RenderCamera.PostRender");
        private static readonly ProfilerMarker s_RequestEditorTemporalRepaintMarker = new("VividRP.RenderPipeline.RenderCamera.RequestEditorTemporalRepaint");
        private static readonly ProfilerMarker s_ExecutePostGraphCommandsMarker = new("VividRP.RenderPipeline.RenderCamera.ExecutePostGraphCommands");
        private static readonly ProfilerMarker s_RenderSubmittedGizmosMarker = new("VividRP.RenderPipeline.RenderCamera.RenderSubmittedGizmos");
        private static readonly ProfilerMarker s_CommandBufferReleaseMarker = new("VividRP.RenderPipeline.RenderCamera.CommandBuffer.Release");
        private static readonly ProfilerMarker s_ContextSubmitMarker = new("VividRP.RenderPipeline.RenderCamera.ContextSubmit");
        private static readonly ProfilerMarker s_EndCameraRenderingMarker = new("VividRP.RenderPipeline.RenderCamera.EndCameraRendering");
        private static readonly ProfilerMarker s_RenderGraphBeginRecordingMarker = new("VividRP.RenderPipeline.RenderGraph.BeginRecording");
        private static readonly ProfilerMarker s_RenderGraphRecordMarker = new("VividRP.RenderPipeline.RenderGraph.Record");
        private static readonly ProfilerMarker s_RenderGraphEndRecordingAndExecuteMarker = new("VividRP.RenderPipeline.RenderGraph.EndRecordingAndExecute");
        private static readonly ProfilerMarker s_RenderGraphAbortFrameMarker = new("VividRP.RenderPipeline.RenderGraph.AbortFrame");
        private static readonly ProfilerMarker s_RenderGraphResetAfterExceptionMarker = new("VividRP.RenderPipeline.RenderGraph.ResetAfterException");
        private static readonly Action<ScriptableRenderContext, Camera> s_BeginCameraRenderingDispatcher =
            CreateRenderPipelineManagerCameraRenderingDispatcher("BeginCameraRendering");
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
        private bool m_EnableHdrOnce = true;
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
            VividReflectionProbeAtlasSystem.Initialize();
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
            using var renderScope = s_RenderMarker.Auto();
            using (s_ApplySRPBatcherMarker.Auto())
            {
                ApplySRPBatcherSetting(m_Asset);
            }

            foreach (var camera in cameras)
                RenderCamera(context, camera);

            using (s_EndFrameMarker.Auto())
            {
                m_RenderGraph.EndFrame();
            }
        }

        private void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            using var renderCameraScope = s_RenderCameraMarker.Auto();
            using (s_BeginCameraRenderingMarker.Auto())
            {
                DispatchBeginCameraRendering(context, camera);
            }

            DecalSystem.ScheduleCullForCamera(camera);

            CommandBuffer cmdBuffer = null;
            var shouldSubmit = false;
            var projectionState = CameraProjectionMatrixUtility.CaptureProjectionState(camera);

            try
            {
                using (s_RestoreProjectionStateMarker.Auto())
                {
                    CameraProjectionMatrixUtility.RestoreProjectionState(camera, projectionState);
                }

                using (s_HDRStateMarker.Auto())
                {
                    VividHDROutputUtility.SetHDRState(camera, ref m_EnableHdrOnce);
                }

                using (s_VolumeUpdateMarker.Auto())
                {
                    VividVolumeManagerUtility.Update(camera);
                }

                ScriptableCullingParameters cullingParameters;
                using (s_CullingParametersMarker.Auto())
                {
                    if (!camera.TryGetCullingParameters(out cullingParameters))
                        return;
                }

                using (s_EmitGeometryMarker.Auto())
                {
                    EmitGeometryForCamera(camera);
                }

                using (s_ShadowDistanceMarker.Auto())
                {
                    ApplyShadowDistanceOverride(camera, ref cullingParameters);
                }

                cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling
                    | CullingOptions.NeedsReflectionProbes;

                CullingResults cullingResults;
                using (s_CullMarker.Auto())
                {
                    cullingResults = context.Cull(ref cullingParameters);
                }

                using (s_CommandBufferGetMarker.Auto())
                {
                    cmdBuffer = CommandBufferPool.Get("VividRP");
                }

                if (ShouldUsePreviewCameraRenderPath(camera.cameraType))
                {
                    using (s_PreviewCameraMarker.Auto())
                    {
                        RenderPreviewCamera(context, camera, cullingResults, cmdBuffer);
                    }

                    shouldSubmit = true;
                    return;
                }

                var graphAsset = m_Asset.RenderGraphAsset;
                PassRecorder.InitializeContext(context, camera, cullingResults, graphAsset);
                using (s_SetupCameraPropertiesMarker.Auto())
                {
                    context.SetupCameraProperties(camera);
                }

                using (s_PrepareFrameMarker.Auto())
                {
                    PassRecorder.PrepareFrame(graphAsset, cmdBuffer);
                }

                shouldSubmit = true;
                using (s_ExecutePreGraphCommandsMarker.Auto())
                {
                    context.ExecuteCommandBuffer(cmdBuffer);
                }

                cmdBuffer.Clear();

                RenderGraphParameters renderGraphParams;
                using (s_RenderGraphParametersMarker.Auto())
                {
                    renderGraphParams = new RenderGraphParameters
                    {
                        scriptableRenderContext = context,
                        commandBuffer = cmdBuffer,
                        currentFrameIndex = PassRecorder.GetFrameData().Get<VividCameraData>()?.frameIndex ?? Time.frameCount,
                        executionId = camera.GetEntityId(),
                        generateDebugData = camera.cameraType != CameraType.Preview && !camera.isProcessingRenderRequest,
                    };
                }

                using (s_RecordAndExecuteRenderGraphMarker.Auto())
                {
                    if (!TryRecordAndExecuteRenderGraph(
                            m_RenderGraph,
                            renderGraphParams,
                            context,
                            graphAsset,
                            m_Asset != null && m_Asset.EnableAsyncCompute))
                    {
                        return;
                    }
                }

                using (s_CommitFrameMarker.Auto())
                {
                    PassRecorder.CommitFrame(graphAsset);
                }

                using (s_SetupCameraPropertiesMarker.Auto())
                {
                    context.SetupCameraProperties(camera);
                }

                using (s_PostRenderMarker.Auto())
                {
                    FrameContextSystem.ExecutePostRender(PassRecorder.GetFrameData(), cmdBuffer);
                }

                using (s_RequestEditorTemporalRepaintMarker.Auto())
                {
                    RequestEditorTemporalRepaint();
                }

                using (s_ExecutePostGraphCommandsMarker.Auto())
                {
                    context.ExecuteCommandBuffer(cmdBuffer);
                }

                cmdBuffer.Clear();
                using (s_RenderSubmittedGizmosMarker.Auto())
                {
                    RenderSubmittedGizmos(context, camera, graphAsset);
                }
            }
            finally
            {
                if (cmdBuffer != null)
                {
                    using (s_CommandBufferReleaseMarker.Auto())
                    {
                        cmdBuffer.Clear();
                        CommandBufferPool.Release(cmdBuffer);
                    }
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
                    using (s_RestoreProjectionStateMarker.Auto())
                    {
                        CameraProjectionMatrixUtility.RestoreProjectionState(camera, projectionState);
                    }
                }

                using (s_EndCameraRenderingMarker.Auto())
                {
                    EndCameraRendering(context, camera);
                }
            }
        }

        private void DispatchBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_BeginCameraRenderingDispatcher != null)
            {
                s_BeginCameraRenderingDispatcher(context, camera);
                return;
            }

            BeginCameraRendering(context, camera);
        }

        private static Action<ScriptableRenderContext, Camera> CreateRenderPipelineManagerCameraRenderingDispatcher(string methodName)
        {
            var method = typeof(RenderPipelineManager).GetMethod(
                methodName,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                null,
                new[] { typeof(ScriptableRenderContext), typeof(Camera) },
                null);

            if (method == null)
                return null;

            try
            {
                return (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(
                    typeof(Action<ScriptableRenderContext, Camera>),
                    method);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (MemberAccessException)
            {
                return null;
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
                using (s_RenderGraphBeginRecordingMarker.Auto())
                {
                    renderGraph.BeginRecording(renderGraphParams);
                }

                using (s_RenderGraphRecordMarker.Auto())
                {
                    recordRenderGraph();
                }

                using (s_RenderGraphEndRecordingAndExecuteMarker.Auto())
                {
                    renderGraph.EndRecordingAndExecute();
                }

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    using (s_RenderGraphAbortFrameMarker.Auto())
                    {
                        onException?.Invoke();
                    }
                }
                catch (Exception cleanupException)
                {
                    Debug.LogException(cleanupException);
                }

                using (s_RenderGraphResetAfterExceptionMarker.Auto())
                {
                    renderGraph.ResetGraphAndLogException(exception);
                }

                return false;
            }
        }

        private static bool TryRecordAndExecuteRenderGraph(
            RenderGraph renderGraph,
            in RenderGraphParameters renderGraphParams,
            ScriptableRenderContext context,
            RenderGraphData graphAsset,
            bool enableAsyncCompute)
        {
            if (renderGraph == null)
                throw new ArgumentNullException(nameof(renderGraph));

            try
            {
                using (s_RenderGraphBeginRecordingMarker.Auto())
                {
                    renderGraph.BeginRecording(renderGraphParams);
                }

                using (s_RenderGraphRecordMarker.Auto())
                {
                    PassRecorder.RecordRenderGraph(
                        renderGraph,
                        context,
                        graphAsset,
                        enableAsyncCompute);
                }

                using (s_RenderGraphEndRecordingAndExecuteMarker.Auto())
                {
                    renderGraph.EndRecordingAndExecute();
                }

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    using (s_RenderGraphAbortFrameMarker.Auto())
                    {
                        PassRecorder.AbortFrame();
                    }
                }
                catch (Exception cleanupException)
                {
                    Debug.LogException(cleanupException);
                }

                using (s_RenderGraphResetAfterExceptionMarker.Auto())
                {
                    renderGraph.ResetGraphAndLogException(exception);
                }

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
            VividReflectionProbeAtlasSystem.Deinitialize();
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
