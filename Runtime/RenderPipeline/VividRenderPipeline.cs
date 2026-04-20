using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.SubSystem.Decal;

namespace VividRP.Runtime
{
    public class VividRenderPipeline : RenderPipeline, IRenderGraphEnabledRenderPipeline
    {
        private const string RenderGraphName = "VividRP RenderGraph";

        private readonly VividRenderPipelineAsset m_Asset;
        private readonly bool m_PreviousUseScriptableRenderPipelineBatching;
        private readonly VividGPUDrivenDebugOverlayRenderer m_GPUDrivenDebugOverlayRenderer;
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
            m_GPUDrivenDebugOverlayRenderer = new VividGPUDrivenDebugOverlayRenderer(resources.GPUDrivenMeshletDebugShader);
            VividAdaptiveProbeVolumeUtility.Initialize(asset);

            m_RenderGraph = new RenderGraph(RenderGraphName);
            m_DebugDisplaySettingsUI = new DebugDisplaySettingsUI();
            m_DebugDisplaySettingsUI.RegisterDebug(VividRenderingDebugDisplaySettings.Instance);
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            ApplySRPBatcherSetting(m_Asset);

            if (m_Asset != null && m_Asset.EnableGPUDriven)
            {
                VividGPUDrivenSystem.instance.PrepareFrame();
            }
            else
            {
                VividGPUDrivenSystem.Shutdown();
            }

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
                VividVolumeManagerUtility.Update(camera);

                if (!camera.TryGetCullingParameters(out var cullingParameters))
                    return;

                EmitGeometryForCamera(camera);
                ApplyShadowDistanceOverride(camera, ref cullingParameters);
                var cullingResults = context.Cull(ref cullingParameters);
                context.SetupCameraProperties(camera);

                cmdBuffer = CommandBufferPool.Get("VividRP");

                PassRecorder.InitializeContext(context, camera, cullingResults);

                if (m_Asset != null && m_Asset.EnableGPUDriven)
                {
                    VividRPCoreResources resources = PipelineResourceManager.Get<VividRPCoreResources>();
                    VividGPUDrivenSystem gpuDrivenSystem = VividGPUDrivenSystem.instance;
                    ApplyGPUDrivenSettings(
                        gpuDrivenSystem,
                        GPUDrivenSettingsVolume.ResolveSettings(VividVolumeManagerUtility.GetGPUDrivenSettingsVolume()));
                    gpuDrivenSystem.Cull(
                        camera,
                        cmdBuffer,
                        resources.GPUInstanceCullingCompute,
                        resources.MeshletListBuildCompute,
                        resources.GPUMeshletCullingCompute,
                        resources.FixupVisibleMeshletIndirectDrawArgsCompute
                    );
                    gpuDrivenSystem.BindGlobals(cmdBuffer);
                    PassRecorder.SetGPUDrivenFrameData(
                        gpuDrivenSystem.VisibleMeshletRenderRequestsBuffer,
                        gpuDrivenSystem.VisibleMeshletIndirectDrawArgsBuffer);
                }


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

                if (m_Asset is { EnableGPUDriven: true, EnableGPUDrivenDebugOverlay: true })
                {
                    context.SetupCameraProperties(camera);
                    VividGPUDrivenSystem.instance.BindGlobals(cmdBuffer);
                    m_GPUDrivenDebugOverlayRenderer.Draw(cmdBuffer, camera, VividGPUDrivenSystem.instance.VisibleMeshletIndirectDrawArgsBuffer);
                }

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

                if (shouldSubmit)
                    context.Submit();


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

        private static void ApplyGPUDrivenSettings(
            VividGPUDrivenSystem gpuDrivenSystem,
            in GPUDrivenSettingsVolume.GPUDrivenSettingsData settings)
        {
            if (gpuDrivenSystem == null)
            {
                return;
            }

            gpuDrivenSystem.ForcedMeshLODNodeDepth = settings.forcedMeshLODNodeDepth;
            gpuDrivenSystem.MeshLODErrorThreshold = settings.meshLODErrorThreshold;
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
            VividGPUDrivenSystem.Shutdown();
            PassRecorder.Dispose();
            VirtualTextureSystem.Deinitialize();
            SkyManager.Deinitialize();
            LTCAreaLightSystem.Deinitialize();
            VividVolumeManagerUtility.Deinitialize();
            DecalSystem.Deinitialize();
            m_DebugDisplaySettingsUI?.UnregisterDebug();
            m_DebugDisplaySettingsUI = null;

            m_RenderGraph?.Cleanup();
            m_RenderGraph = null;
            m_GPUDrivenDebugOverlayRenderer?.Dispose();
            BlueNoise.Cleanup();
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

    internal sealed class VividGPUDrivenDebugOverlayRenderer : IDisposable
    {
        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_OverlayAlphaId = Shader.PropertyToID("_OverlayAlpha");

        private readonly Material[] m_Materials = new Material[(int) VividRendererListID.Count];
        private readonly ProfilingSampler m_ProfilingSampler = new(nameof(VividGPUDrivenDebugOverlayRenderer));

        public VividGPUDrivenDebugOverlayRenderer(Shader shader)
        {
            if (shader == null)
            {
                return;
            }

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(VividGPUDrivenDebugOverlayRenderer)}_{(VividRendererListID) rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID) rendererListIndex);
                m_Materials[rendererListIndex] = material;
            }
        }

        public bool IsAvailable => m_Materials[0] != null;

        public void Draw(CommandBuffer cmd, Camera camera, GraphicsBuffer indirectDrawArgsBuffer)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (!IsAvailable || indirectDrawArgsBuffer == null)
            {
                return;
            }

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                RenderTargetIdentifier colorTarget = camera.targetTexture != null
                    ? new RenderTargetIdentifier(camera.targetTexture)
                    : BuiltinRenderTextureType.CameraTarget;
                cmd.SetRenderTarget(colorTarget);
                cmd.SetViewport(camera.pixelRect);

                int argsStride = UnsafeUtility.SizeOf<VividIndirectDrawArgs>();
                for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
                {
                    Material material = m_Materials[rendererListIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    cmd.DrawProceduralIndirect(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        indirectDrawArgsBuffer,
                        rendererListIndex * argsStride
                    );
                }
            }
        }

        public void Dispose()
        {
            for (int index = 0; index < m_Materials.Length; index++)
            {
                if (m_Materials[index] != null)
                {
                    CoreUtils.Destroy(m_Materials[index]);
                    m_Materials[index] = null;
                }
            }
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            material.SetFloat(s_CullId, (float) GetCullMode(rendererListID));
            material.SetFloat(s_OverlayAlphaId, 0.35f);
        }

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
            {
                return CullMode.Front;
            }

            if ((rendererListID & VividRendererListID.CullOff) != 0)
            {
                return CullMode.Off;
            }

            return CullMode.Back;
        }
    }
}
