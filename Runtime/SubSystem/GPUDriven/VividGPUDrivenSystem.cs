using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSystem : VividSubsystem<VividGPUDrivenSystem>, IDisposable
    {
        private static int s_PreparedFrameIndex = -1;

        private readonly VividGPUDrivenBufferSet m_BufferSet;
        private readonly VividGPUDrivenCullingDispatcher m_CullingDispatcher;
        private readonly VividGPUDrivenObjectTracker m_ObjectTracker;
        private readonly VividGPUDrivenSceneDataBuilder m_SceneDataBuilder;
        private VividGPUDrivenCullingDispatcher[] m_ShadowCullingDispatchers;
        private bool m_IsDisposed;

        public VividGPUDrivenSystem()
            : this(new BindlessTextureContainer(), new VividGPUDrivenSceneDataBuilder())
        {
        }

        internal VividGPUDrivenSystem(IBindlessTextureDescriptorAllocator allocator)
            : this(new BindlessTextureContainer(allocator), new VividGPUDrivenSceneDataBuilder())
        {
        }

        private VividGPUDrivenSystem(
            BindlessTextureContainer bindlessTextureContainer,
            VividGPUDrivenSceneDataBuilder sceneDataBuilder
        )
        {
            BindlessTextureContainer = bindlessTextureContainer ?? throw new ArgumentNullException(nameof(bindlessTextureContainer));
            m_ObjectTracker = new VividGPUDrivenObjectTracker(BindlessTextureContainer);
            m_BufferSet = new VividGPUDrivenBufferSet();
            m_CullingDispatcher = new VividGPUDrivenCullingDispatcher();
            m_SceneDataBuilder = sceneDataBuilder ?? throw new ArgumentNullException(nameof(sceneDataBuilder));
            SceneData = new VividGPUDrivenSceneData();
            ForcedMeshLODNodeDepth = VividGPUDrivenCullingContextUtility.DefaultForcedMeshLODNodeDepth;
            MeshLODErrorThreshold = VividGPUDrivenCullingContextUtility.DefaultMeshLODErrorThreshold;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void AutoInitialize()
        {
            Initialize();
        }

        protected override void OnInitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
        }

        protected override void OnDeinitialize()
        {
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            Shutdown();
        }

        public new static void Deinitialize()
        {
            VividSubsystem<VividGPUDrivenSystem>.Deinitialize();

#if UNITY_EDITOR
            // In editor we keep the FrameContext callbacks alive so the next preview render rebuilds
            // a fresh singleton lazily; the heavy GPU resources owned by the previous instance were
            // already released by Shutdown() inside OnDeinitialize.
            EnsurePreRenderSubscribed();
            FrameContextSystem.SubsystemDispose -= OnSubsystemDispose;
            FrameContextSystem.SubsystemDispose += OnSubsystemDispose;
#endif
        }

        private static void OnSubsystemDispose()
        {
            Deinitialize();
        }

        public static VividGPUDrivenSystem instance
        {
            get
            {
                Initialize();
                return Instance;
            }
        }

        public static new bool HasInstance => RawInstance != null && !RawInstance.m_IsDisposed;

        public BindlessTextureContainer BindlessTextureContainer { get; }

        public VividGPUDrivenSceneData SceneData { get; }

        public bool IsAvailable => BindlessTextureContainer.IsAvailable;

        public string UnavailableReason => BindlessTextureContainer.UnavailableReason;

        internal VividGPUDrivenBufferSet BufferSet => m_BufferSet;

        internal VividGPUDrivenCullingBuffers CullingBufferSet => m_CullingDispatcher.BufferSet;

        public int ForcedMeshLODNodeDepth { get; set; }

        public float MeshLODErrorThreshold { get; set; }

        public GraphicsBuffer VisibleMeshletRenderRequestsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletRenderRequestsBuffer;

        public GraphicsBuffer VisibleMeshletIndirectDrawArgsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletIndirectDrawArgsBuffer;

        public GraphicsBuffer GetShadowVisibleMeshletRenderRequestsBuffer(int cascadeIndex)
        {
            if (m_ShadowCullingDispatchers == null
                || cascadeIndex < 0
                || cascadeIndex >= m_ShadowCullingDispatchers.Length)
            {
                return null;
            }

            return m_ShadowCullingDispatchers[cascadeIndex]?.BufferSet.VisibleMeshletRenderRequestsBuffer;
        }

        public GraphicsBuffer GetShadowVisibleMeshletIndirectDrawArgsBuffer(int cascadeIndex)
        {
            if (m_ShadowCullingDispatchers == null
                || cascadeIndex < 0
                || cascadeIndex >= m_ShadowCullingDispatchers.Length)
            {
                return null;
            }

            return m_ShadowCullingDispatchers[cascadeIndex]?.BufferSet.VisibleMeshletIndirectDrawArgsBuffer;
        }

        public static bool TryGetCurrentVisibleMeshletBuffers(
            out GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            out GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer)
        {
            VividGPUDrivenSystem currentInstance = RawInstance;
            if (currentInstance == null || currentInstance.m_IsDisposed || !currentInstance.IsAvailable)
            {
                visibleMeshletRenderRequestsBuffer = null;
                visibleMeshletIndirectDrawArgsBuffer = null;
                return false;
            }

            visibleMeshletRenderRequestsBuffer = currentInstance.VisibleMeshletRenderRequestsBuffer;
            visibleMeshletIndirectDrawArgsBuffer = currentInstance.VisibleMeshletIndirectDrawArgsBuffer;
            return visibleMeshletRenderRequestsBuffer != null && visibleMeshletIndirectDrawArgsBuffer != null;
        }

        public void PrepareFrame(bool reportStats = true)
        {
            ThrowIfDisposed();

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameMarker.Auto())
            {
                BindlessTextureContainer.ResetPerFrameStats();
                BindlessTextureContainer.PreRender();
                bool staticDataChanged = m_SceneDataBuilder.Build(
                    SceneData,
                    VividMeshletRendererDatabase.instance,
                    BindlessTextureContainer,
                    out bool materialDataChanged,
                    out bool instanceDataChanged
                );
                m_BufferSet.Upload(
                    SceneData,
                    uploadInstanceData: instanceDataChanged,
                    uploadMaterialData: materialDataChanged,
                    uploadStaticData: staticDataChanged
                );
                if (reportStats)
                {
                    ReportStats(null);
                }
            }
        }

        public void Cull(
            Camera camera,
            CommandBuffer cmd,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute = null,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute = null,
            VividInstancePassMask passMask = VividInstancePassMask.Main,
            string cameraName = null
        )
        {
            ThrowIfDisposed();

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCullMarker.Auto())
            {
                m_CullingDispatcher.Dispatch(
                    cmd,
                    camera,
                    SceneData,
                    m_BufferSet,
                    gpuInstanceCullingCompute,
                    meshletListBuildCompute,
                    gpuMeshletCullingCompute,
                    fixupVisibleMeshletIndirectDrawArgsCompute,
                    passMask,
                    ForcedMeshLODNodeDepth,
                    MeshLODErrorThreshold
                );
            }

            ReportStats(camera, cameraName);
        }

        public void CullShadowCascade(
            int cascadeIndex,
            CommandBuffer cmd,
            in VividGPUCullingContext cullingContext,
            in VividGPULODSelectionContext lodSelectionContext,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute
        )
        {
            ThrowIfDisposed();

            if (cascadeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cascadeIndex));
            }

            EnsureShadowDispatcherCapacity(cascadeIndex + 1);

            var dispatcher = m_ShadowCullingDispatchers[cascadeIndex];
            dispatcher.Dispatch(
                cmd,
                cullingContext,
                lodSelectionContext,
                SceneData,
                m_BufferSet,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                ForcedMeshLODNodeDepth,
                MeshLODErrorThreshold
            );
        }

        private void EnsureShadowDispatcherCapacity(int requiredCount)
        {
            if (m_ShadowCullingDispatchers != null && m_ShadowCullingDispatchers.Length >= requiredCount)
            {
                return;
            }

            int previousCount = m_ShadowCullingDispatchers?.Length ?? 0;
            Array.Resize(ref m_ShadowCullingDispatchers, requiredCount);
            for (int i = previousCount; i < requiredCount; i++)
            {
                m_ShadowCullingDispatchers[i] = new VividGPUDrivenCullingDispatcher();
            }
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            m_BufferSet.BindGlobals(cmd);
            m_CullingDispatcher.BindGlobals(cmd);
        }

        public static void Shutdown()
        {
            RawInstance?.Dispose();
            ClearInstance();
            s_PreparedFrameIndex = -1;
            VividGPUDrivenStatsRegistry.Clear();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            SceneData.Clear();
            m_BufferSet.Dispose();
            m_CullingDispatcher.Dispose();
            if (m_ShadowCullingDispatchers != null)
            {
                for (int i = 0; i < m_ShadowCullingDispatchers.Length; i++)
                {
                    m_ShadowCullingDispatchers[i]?.Dispose();
                    m_ShadowCullingDispatchers[i] = null;
                }
                m_ShadowCullingDispatchers = null;
            }
            m_ObjectTracker.Dispose();
            BindlessTextureContainer.Dispose();
            m_IsDisposed = true;
            VividGPUDrivenStatsRegistry.Clear();
        }

        protected override void OnUpdate(ContextContainer frameData, CommandBuffer cmd)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenMarker.Auto())
            {
                UpdateCore(frameData, cmd);
            }
        }

        private static void UpdateCore(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!IsInitialized)
                Initialize();

            if (frameData == null || cmd == null)
                return;

            VividRenderPipelineAsset asset;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenResolveAssetMarker.Auto())
            {
                asset = VividRenderPipelineAsset.GetActiveAsset();
            }

            if (asset == null || !asset.EnableGPUDriven)
            {
                PassRecorder.SetGPUDrivenFrameData(null, null);
                Shutdown();
                return;
            }

            VividCameraData cameraData;
            Camera camera;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenCameraDataMarker.Auto())
            {
                cameraData = frameData.GetOrCreate<VividCameraData>();
                camera = cameraData.camera;
            }

            if (camera == null)
            {
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            VividGPUDrivenSystem gpuDrivenSystem = instance;
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera, cameraData.cameraName);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            PrepareFrameIfNeeded(gpuDrivenSystem, cameraData.frameIndex, reportStats: false);
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera, cameraData.cameraName);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenApplySettingsMarker.Auto())
            {
                ApplyResolvedSettings(gpuDrivenSystem);
            }

            Camera cullingCamera = ResolveCullingCameraForDebug(camera);

            VividRPCoreResources resources;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenResolveResourcesMarker.Auto())
            {
                resources = PipelineResourceManager.Get<VividRPCoreResources>();
            }
            gpuDrivenSystem.Cull(
                cullingCamera,
                cmd,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute,
                cameraName: cameraData.cameraName);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenBindGlobalsMarker.Auto())
            {
                gpuDrivenSystem.BindGlobals(cmd);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenSetFrameDataMarker.Auto())
            {
                PassRecorder.SetGPUDrivenFrameData(
                    gpuDrivenSystem.VisibleMeshletRenderRequestsBuffer,
                    gpuDrivenSystem.VisibleMeshletIndirectDrawArgsBuffer);
            }
        }

        private static void PrepareFrameIfNeeded(VividGPUDrivenSystem gpuDrivenSystem, int frameIndex, bool reportStats = true)
        {
            int resolvedFrameIndex = frameIndex >= 0 ? frameIndex : Time.frameCount;
            if (!ShouldPrepareFrame(s_PreparedFrameIndex, resolvedFrameIndex, Application.isPlaying))
                return;

            gpuDrivenSystem.PrepareFrame(reportStats);
            s_PreparedFrameIndex = resolvedFrameIndex;
        }

        internal static bool ShouldPrepareFrame(int preparedFrameIndex, int frameIndex, bool isPlaying)
        {
#if UNITY_EDITOR
            if (!isPlaying)
                return true;
#endif
            return preparedFrameIndex != frameIndex;
        }

        private static void ApplyResolvedSettings(VividGPUDrivenSystem gpuDrivenSystem)
        {
            if (gpuDrivenSystem == null)
                return;

            GPUDrivenSettingsVolume.GPUDrivenSettingsData settings =
                GPUDrivenSettingsVolume.ResolveSettings(VividVolumeManagerUtility.GetGPUDrivenSettingsVolume());
            gpuDrivenSystem.ForcedMeshLODNodeDepth = settings.forcedMeshLODNodeDepth;
            gpuDrivenSystem.MeshLODErrorThreshold = settings.meshLODErrorThreshold;
        }

        internal static Camera ResolveCullingCameraForDebug(Camera renderingCamera)
        {
            if (!VividRenderingDebugDisplaySettings.Data.forceMeshletCullingFromMainCamera)
            {
                return renderingCamera;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera;
            }

            Camera fallbackCamera = UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
            return fallbackCamera != null ? fallbackCamera : renderingCamera;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenSystem));
            }
        }

        private void ReportStats(Camera camera, string cameraName = null)
        {
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenReportStatsMarker.Auto())
            {
                bool bindlessAvailable = BindlessTextureContainer.IsAvailable;
                string statusMessage = bindlessAvailable
                    ? string.Empty
                    : BindlessTextureContainer.UnavailableReason;

                VividGPUDrivenStatsRegistry.Report(
                    new VividGPUDrivenStats(
                        true,
                        statusMessage,
                        camera != null,
                        camera != null ? cameraName : null,
                        camera != null ? camera.cameraType : default,
                        Time.frameCount,
                        Time.realtimeSinceStartupAsDouble,
                        bindlessAvailable,
                        VividMeshletRendererDatabase.instance.rendererCount,
                        SceneData.InstanceCount,
                        SceneData.MaterialCount,
                        SceneData.MeshLODNodeCount,
                        SceneData.MeshletCount,
                        SceneData.VertexCount,
                        SceneData.IndexCount,
                        m_CullingDispatcher.BufferSet.MaxMeshletListBuildJobCount,
                        m_CullingDispatcher.BufferSet.MaxVisibleMeshletRenderRequestCount,
                        BindlessTextureContainer.DescriptorHeapCount,
                        BindlessTextureContainer.DescriptorCapacity,
                        BindlessTextureContainer.AllocatedDescriptorCount,
                        BindlessTextureContainer.CreateSRVDescriptorCallCountThisFrame,
                        BindlessTextureContainer.RegisteredTextureCount,
                        ForcedMeshLODNodeDepth,
                        MeshLODErrorThreshold));
            }
        }
    }
}
