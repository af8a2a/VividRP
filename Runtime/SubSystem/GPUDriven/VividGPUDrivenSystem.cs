using System;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.Bindless;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using VividRP.Runtime.PrimitiveScene;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSystem : VividSubsystem<VividGPUDrivenSystem>, IDisposable
    {
        internal const string VirtualTextureBackendKeyword = "VIVID_GPU_DRIVEN_TEXTURE_BACKEND_VIRTUAL_TEXTURE";

        private readonly struct BackendConfiguration
        {
            internal BackendConfiguration(
                IGPUDrivenTextureBackend activeBackend,
                BindlessGPUDrivenTextureBackend legacyBindlessBackend,
                GPUDrivenTextureBackendMode mode,
                bool terrainRuntimeVirtualTextureRequested = false)
            {
                ActiveBackend = activeBackend;
                LegacyBindlessBackend = legacyBindlessBackend;
                Mode = mode;
                TerrainRuntimeVirtualTextureRequested = terrainRuntimeVirtualTextureRequested;
            }

            internal IGPUDrivenTextureBackend ActiveBackend { get; }

            internal BindlessGPUDrivenTextureBackend LegacyBindlessBackend { get; }

            internal GPUDrivenTextureBackendMode Mode { get; }

            internal bool TerrainRuntimeVirtualTextureRequested { get; }

        }

        private static int s_PreparedFrameIndex = -1;

        private readonly VividGPUDrivenBufferSet m_BufferSet;
        private readonly VividGPUDrivenCullingDispatcher m_CullingDispatcher;
        private readonly VividGPUDrivenSceneDataBuilder m_SceneDataBuilder;
        private readonly VividPrimitiveSceneAdapter m_PrimitiveSceneAdapter;
        private readonly VividPrimitiveSceneBufferSet m_PrimitiveSceneBufferSet;
        private readonly VividPrimitiveDrawSetSystem m_PrimitiveDrawSetSystem;
        private readonly VividPrimitiveDrawSetSystem m_ShadowPrimitiveDrawSetSystem;
        private readonly IGPUDrivenTextureBackend m_TextureBackend;
        private readonly BindlessGPUDrivenTextureBackend m_LegacyBindlessBackend;
        private readonly GPUDrivenTextureBackendMode m_TextureBackendMode;
        private readonly bool m_TerrainRuntimeVirtualTextureRequested;
        private VividGPUDrivenCullingDispatcher m_ShadowCullingDispatcher;
        private VividPrimitiveDrawSet m_CurrentMainViewDrawSet;
        private EntityId m_ScheduledMainViewRenderingCameraId = EntityId.None;
        private EntityId m_ScheduledMainViewCullingCameraId = EntityId.None;
        private VividPrimitiveDrawSet m_ScheduledMainViewDrawSet;
        private int m_ScheduledMainViewFrameIndex = -1;
        private uint m_ScheduledMainViewSceneRevision;
        private int m_ShadowCullingContextCount;
        private bool m_IsDisposed;

        public VividGPUDrivenSystem()
            : this(CreateDefaultBackendConfiguration(), new VividGPUDrivenSceneDataBuilder())
        {
        }

        internal VividGPUDrivenSystem(IBindlessTextureDescriptorAllocator allocator)
            : this(CreateBindlessBackendConfiguration(allocator), new VividGPUDrivenSceneDataBuilder())
        {
        }

        internal VividGPUDrivenSystem(IGPUDrivenTextureBackend textureBackend)
            : this(
                textureBackend,
                new VividGPUDrivenSceneDataBuilder(),
                textureBackend as BindlessGPUDrivenTextureBackend,
                textureBackend is IGPUDrivenVirtualTextureBackend
                    ? GPUDrivenTextureBackendMode.VirtualTexture
                    : GPUDrivenTextureBackendMode.Bindless,
                textureBackend is VirtualTextureGPUDrivenTextureBackend virtualTextureBackend
                    && virtualTextureBackend.TerrainRuntimeVirtualTextureRequested)
        {
        }

        private VividGPUDrivenSystem(
            BackendConfiguration configuration,
            VividGPUDrivenSceneDataBuilder sceneDataBuilder)
            : this(
                configuration.ActiveBackend,
                sceneDataBuilder,
                configuration.LegacyBindlessBackend,
                configuration.Mode,
                configuration.TerrainRuntimeVirtualTextureRequested)
        {
        }

        private VividGPUDrivenSystem(
            IGPUDrivenTextureBackend textureBackend,
            VividGPUDrivenSceneDataBuilder sceneDataBuilder,
            BindlessGPUDrivenTextureBackend legacyBindlessBackend,
            GPUDrivenTextureBackendMode textureBackendMode,
            bool terrainRuntimeVirtualTextureRequested
        )
        {
            m_TextureBackend = textureBackend ?? throw new ArgumentNullException(nameof(textureBackend));
            m_LegacyBindlessBackend = legacyBindlessBackend;
            m_TextureBackendMode = textureBackendMode;
            m_TerrainRuntimeVirtualTextureRequested = terrainRuntimeVirtualTextureRequested;
            BindlessTextureContainer = legacyBindlessBackend?.TextureContainer;
            m_BufferSet = new VividGPUDrivenBufferSet();
            m_CullingDispatcher = new VividGPUDrivenCullingDispatcher();
            m_SceneDataBuilder = sceneDataBuilder ?? throw new ArgumentNullException(nameof(sceneDataBuilder));
            SceneData = new VividGPUDrivenSceneData();
            PrimitiveScene = new VividPrimitiveScene();
            m_PrimitiveSceneAdapter = new VividPrimitiveSceneAdapter();
            m_PrimitiveSceneBufferSet = new VividPrimitiveSceneBufferSet();
            m_PrimitiveDrawSetSystem = new VividPrimitiveDrawSetSystem();
            m_ShadowPrimitiveDrawSetSystem = new VividPrimitiveDrawSetSystem();
            ForcedMeshLODNodeDepth = VividGPUDrivenCullingContextUtility.DefaultForcedMeshLODNodeDepth;
            MeshLODErrorThreshold = VividGPUDrivenCullingContextUtility.DefaultMeshLODErrorThreshold;
        }

        private static BackendConfiguration CreateDefaultBackendConfiguration()
        {
            VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
            GPUDrivenTextureBackendMode mode = ResolveConfiguredTextureBackendMode(asset);
            if (mode == GPUDrivenTextureBackendMode.Bindless)
                return CreateBindlessBackendConfiguration(null);

            GPUDrivenVirtualTextureDescriptorProfile descriptorProfile =
                ResolveConfiguredVirtualTextureDescriptorProfile(asset);
            bool enableTerrainRVT = asset?.EnableTerrainRuntimeVirtualTexture == true;
            var legacyBindlessBackend = new BindlessGPUDrivenTextureBackend();
            var virtualTextureBackend = new VirtualTextureGPUDrivenTextureBackend(
                descriptorProfile,
                enableTerrainRVT);
            return new BackendConfiguration(
                virtualTextureBackend,
                legacyBindlessBackend,
                GPUDrivenTextureBackendMode.VirtualTexture,
                enableTerrainRVT);
        }

        internal static GPUDrivenTextureBackendMode ResolveConfiguredTextureBackendMode(
            VividRenderPipelineAsset asset)
        {
            return asset?.GPUDrivenTextureBackend
                   ?? GPUDrivenTextureBackendMode.VirtualTexture;
        }

        internal static GPUDrivenVirtualTextureDescriptorProfile
            ResolveConfiguredVirtualTextureDescriptorProfile(VividRenderPipelineAsset asset)
        {
            GPUDrivenVirtualTexturePhysicalPoolQuality quality =
                asset?.GPUDrivenVirtualTexturePhysicalPoolQuality
                ?? GPUDrivenVirtualTexturePhysicalPoolQuality.Medium;
            return VirtualTextureGPUDrivenTextureBackend.ResolveDescriptorProfile(quality);
        }

        internal static bool RequiresTextureBackendRecreation(
            GPUDrivenTextureBackendMode currentMode,
            VividRenderPipelineAsset asset)
        {
            GPUDrivenTextureBackendMode configuredMode = ResolveConfiguredTextureBackendMode(asset);
            return currentMode != configuredMode;
        }

        internal static bool RequiresTextureBackendRecreation(
            GPUDrivenTextureBackendMode currentMode,
            bool terrainRuntimeVirtualTextureRequested,
            VividRenderPipelineAsset asset)
        {
            return RequiresTextureBackendRecreation(currentMode, asset)
                   || (currentMode == GPUDrivenTextureBackendMode.VirtualTexture
                       && terrainRuntimeVirtualTextureRequested
                       != (asset?.EnableTerrainRuntimeVirtualTexture == true));
        }

        private static BackendConfiguration CreateBindlessBackendConfiguration(
            IBindlessTextureDescriptorAllocator allocator)
        {
            var bindlessBackend = allocator != null
                ? new BindlessGPUDrivenTextureBackend(allocator)
                : new BindlessGPUDrivenTextureBackend();
            return new BackendConfiguration(
                bindlessBackend,
                bindlessBackend,
                GPUDrivenTextureBackendMode.Bindless);
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

        internal VividPrimitiveScene PrimitiveScene { get; }

        internal VividPrimitiveSceneBufferSet PrimitiveSceneBufferSet => m_PrimitiveSceneBufferSet;

        internal VividPrimitiveDrawSet CurrentMainViewDrawSet => m_CurrentMainViewDrawSet;

        public bool IsAvailable => m_TextureBackend.IsAvailable;

        public string UnavailableReason => m_TextureBackend.UnavailableReason;

        public GPUDrivenTextureBackendMode TextureBackendMode => m_TextureBackendMode;

        public bool UsesVirtualTexture => m_TextureBackend is IGPUDrivenVirtualTextureBackend;

        internal bool IsMainViewRendererBatchActive(VividRendererListID batchKey)
        {
            return SceneData.IsMainViewRendererBatchActive(batchKey);
        }

        internal bool IsShadowRendererBatchActive(VividRendererListID batchKey)
        {
            return SceneData.IsShadowRendererBatchActive(batchKey);
        }

        internal VividGPUDrivenBufferSet BufferSet => m_BufferSet;

        internal VividGPUDrivenCullingBuffers CullingBufferSet => m_CullingDispatcher.BufferSet;

        public int ForcedMeshLODNodeDepth { get; set; }

        public float MeshLODErrorThreshold { get; set; }

        public GraphicsBuffer VisibleMeshletRenderRequestsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletRenderRequestsBuffer;

        public GraphicsBuffer VisibleMeshletIndirectDrawArgsBuffer => m_CullingDispatcher.BufferSet.VisibleMeshletIndirectDrawArgsBuffer;

        public GraphicsBuffer GetShadowVisibleMeshletRenderRequestsBuffer(int cascadeIndex)
        {
            if (m_ShadowCullingDispatcher == null
                || cascadeIndex < 0
                || cascadeIndex >= m_ShadowCullingContextCount)
            {
                return null;
            }

            return m_ShadowCullingDispatcher.BufferSet.VisibleMeshletRenderRequestsBuffer;
        }

        public GraphicsBuffer GetShadowVisibleMeshletIndirectDrawArgsBuffer(int cascadeIndex)
        {
            if (m_ShadowCullingDispatcher == null
                || cascadeIndex < 0
                || cascadeIndex >= m_ShadowCullingContextCount)
            {
                return null;
            }

            return m_ShadowCullingDispatcher.BufferSet.VisibleMeshletIndirectDrawArgsBuffer;
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

        internal static bool TryGetPrimitiveShadowCasterBounds(Camera camera, out Bounds worldBounds)
        {
            worldBounds = default;
            VividGPUDrivenSystem currentInstance = RawInstance;
            return camera != null
                && currentInstance != null
                && !currentInstance.m_IsDisposed
                && currentInstance.IsAvailable
                && currentInstance.PrimitiveScene.TryGetWorldBounds(
                    VividInstancePassMask.Shadows,
                    unchecked((uint) camera.cullingMask),
                    out worldBounds);
        }

        internal static bool TryGetVirtualTextureAllocationId(out int allocationId)
        {
            VividGPUDrivenSystem currentInstance = RawInstance;
            if (currentInstance?.m_TextureBackend is IGPUDrivenVirtualTextureBackend virtualTextureBackend
                && currentInstance.IsAvailable)
            {
                allocationId = virtualTextureBackend.VirtualTextureAllocationId;
                return allocationId > 0;
            }

            allocationId = 0;
            return false;
        }

        internal void ConfigureTextureBackendKeyword(Material material)
        {
            if (material != null)
                CoreUtils.SetKeyword(material, VirtualTextureBackendKeyword, UsesVirtualTexture);
        }

        internal static bool ScheduleCullForCamera(Camera camera, int frameIndex)
        {
            // A token belongs to exactly one beginCameraRendering invocation. Clear it
            // even when this camera is intentionally unsupported so a same-frame render
            // request cannot reuse an older DrawSet.
            RawInstance?.ClearScheduledMainViewDrawSet();
            if (camera == null
                || camera.cameraType == CameraType.Preview
                || camera.stereoEnabled)
            {
                return false;
            }

            try
            {
                VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
                if (asset == null || !asset.EnableGPUDriven)
                    return false;

                VividGPUDrivenSystem gpuDrivenSystem = instance;
                if (RequiresTextureBackendRecreation(
                        gpuDrivenSystem.TextureBackendMode,
                        gpuDrivenSystem.m_TerrainRuntimeVirtualTextureRequested,
                        asset))
                {
                    Shutdown();
                    gpuDrivenSystem = instance;
                }

                if (!gpuDrivenSystem.IsAvailable)
                    return false;

                Camera cullingCamera = ResolveCullingCameraForDebug(camera);
                if (cullingCamera == null || cullingCamera.stereoEnabled)
                    return false;

                int resolvedFrameIndex = ResolveFrameIndex(frameIndex);
                PrepareFrameIfNeeded(gpuDrivenSystem, resolvedFrameIndex, reportStats: false);
                if (!gpuDrivenSystem.IsAvailable)
                    return false;

                VividPrimitiveDrawSet drawSet = gpuDrivenSystem.m_PrimitiveDrawSetSystem.Schedule(
                    cullingCamera,
                    gpuDrivenSystem.PrimitiveScene.ActiveCullRecords,
                    gpuDrivenSystem.PrimitiveScene.DrawSetSources,
                    gpuDrivenSystem.PrimitiveScene.SceneRevision,
                    resolvedFrameIndex);
                gpuDrivenSystem.m_ScheduledMainViewRenderingCameraId = camera.GetEntityId();
                gpuDrivenSystem.m_ScheduledMainViewCullingCameraId = cullingCamera.GetEntityId();
                gpuDrivenSystem.m_ScheduledMainViewDrawSet = drawSet;
                gpuDrivenSystem.m_ScheduledMainViewFrameIndex = resolvedFrameIndex;
                gpuDrivenSystem.m_ScheduledMainViewSceneRevision =
                    gpuDrivenSystem.PrimitiveScene.SceneRevision;
                return true;
            }
            catch (Exception exception)
            {
                RawInstance?.ClearScheduledMainViewDrawSet();
                Debug.LogException(exception);
                return false;
            }
        }

        public void PrepareFrame(bool reportStats = true)
        {
            ThrowIfDisposed();
            // Scheduled culling jobs read PrimitiveScene NativeArrays directly. They
            // must be retired before the adapter can resize or rewrite those arrays.
            m_PrimitiveDrawSetSystem.CompleteAndInvalidateAllBuilds();
            m_ShadowPrimitiveDrawSetSystem.CompleteAndInvalidateAllBuilds();
            ClearScheduledMainViewDrawSet();
            m_CurrentMainViewDrawSet = null;

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameMarker.Auto())
            {
                using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameResetStatsMarker.Auto())
                {
                    m_TextureBackend.ResetPerFrameStats();
                    if (m_LegacyBindlessBackend != null && !ReferenceEquals(m_LegacyBindlessBackend, m_TextureBackend))
                        m_LegacyBindlessBackend.ResetPerFrameStats();
                }

                using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameTextureBackendMarker.Auto())
                {
                    m_TextureBackend.PrepareFrame();
                    if (m_LegacyBindlessBackend != null && !ReferenceEquals(m_LegacyBindlessBackend, m_TextureBackend))
                        m_LegacyBindlessBackend.PrepareFrame();
                }

                bool staticDataChanged;
                bool materialDataChanged;
                bool instanceDataChanged;
                using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameBuildSceneDataMarker.Auto())
                {
                    staticDataChanged = m_SceneDataBuilder.Build(
                        SceneData,
                        VividMeshletRendererDatabase.instance,
                        m_TextureBackend,
                        out materialDataChanged,
                        out instanceDataChanged
                    );
                }

                m_PrimitiveSceneAdapter.Synchronize(
                    PrimitiveScene,
                    VividMeshletRendererDatabase.instance,
                    SceneData,
                    staticDataChanged,
                    materialDataChanged,
                    Time.frameCount);

                m_PrimitiveSceneBufferSet.Upload(PrimitiveScene);

                using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenPrepareFrameUploadBuffersMarker.Auto())
                {
                    m_BufferSet.Upload(
                        SceneData,
                        uploadInstanceData: instanceDataChanged,
                        uploadMaterialData: materialDataChanged,
                        uploadStaticData: staticDataChanged
                    );
                }

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
            CullInternal(
                camera,
                cmd,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                passMask,
                default,
                cameraName,
                null);
        }

        internal void CullMainView(
            Camera camera,
            CommandBuffer cmd,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute,
            in VividGPUDrivenOcclusionCullingParameters occlusionParameters,
            string cameraName = null,
            int frameIndex = -1,
            VividPrimitiveDrawSet scheduledDrawSet = null,
            bool allowPrimitiveDrawSet = true)
        {
            m_CurrentMainViewDrawSet = BuildMainViewDrawSet(
                camera,
                frameIndex,
                scheduledDrawSet,
                allowPrimitiveDrawSet);
            CullInternal(
                camera,
                cmd,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                VividInstancePassMask.Main,
                occlusionParameters,
                cameraName,
                m_CurrentMainViewDrawSet);
        }

        private VividPrimitiveDrawSet BuildMainViewDrawSet(
            Camera camera,
            int frameIndex,
            VividPrimitiveDrawSet scheduledDrawSet,
            bool allowPrimitiveDrawSet)
        {
            ThrowIfDisposed();
            if (!allowPrimitiveDrawSet || camera == null || camera.stereoEnabled)
                return null;

            int resolvedFrameIndex = ResolveFrameIndex(frameIndex);
            if (scheduledDrawSet != null
                && scheduledDrawSet.cameraEntityID.Equals(camera.GetEntityId())
                && scheduledDrawSet.MatchesPendingBuild(
                    PrimitiveScene.SceneRevision,
                    resolvedFrameIndex))
            {
                scheduledDrawSet.CompleteScheduledBuild();
                return scheduledDrawSet;
            }

            return m_PrimitiveDrawSetSystem.Build(
                camera,
                PrimitiveScene.ActiveCullRecords,
                PrimitiveScene.DrawSetSources,
                PrimitiveScene.SceneRevision,
                resolvedFrameIndex);
        }

        private VividPrimitiveDrawSet ScheduleShadowDrawSet(
            Camera camera,
            VividShadowData shadowData,
            int frameIndex)
        {
            if (!PassRecorder.HasMeshletShadowPass
                || camera == null
                || shadowData == null
                || !shadowData.isCSMActive
                || shadowData.cascadeCount <= 0)
            {
                return null;
            }

            int cascadeCount = Mathf.Min(
                shadowData.cascadeCount,
                VividShadowData.MaxCascadeCount);
            return m_ShadowPrimitiveDrawSetSystem.Schedule(
                camera,
                shadowData.primitiveCullingViewMatrices,
                shadowData.primitiveCullingProjMatrices,
                cascadeCount,
                VividInstancePassMask.Shadows,
                cullAgainstNearPlane: false,
                PrimitiveScene.ActiveCullRecords,
                PrimitiveScene.DrawSetSources,
                PrimitiveScene.SceneRevision,
                ResolveFrameIndex(frameIndex));
        }

        internal VividPrimitiveDrawSet CompleteShadowDrawSet(
            VividPrimitiveDrawSet scheduledDrawSet,
            Camera camera,
            int frameIndex)
        {
            if (scheduledDrawSet == null
                || camera == null
                || !scheduledDrawSet.cameraEntityID.Equals(camera.GetEntityId())
                || !scheduledDrawSet.MatchesPendingBuild(
                    PrimitiveScene.SceneRevision,
                    ResolveFrameIndex(frameIndex)))
            {
                return null;
            }

            try
            {
                return scheduledDrawSet.CompleteScheduledBuild()
                    ? scheduledDrawSet
                    : null;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                scheduledDrawSet.CompleteAndInvalidate();
                return null;
            }
        }

        private bool TryConsumeScheduledMainViewDrawSet(
            Camera renderingCamera,
            Camera cullingCamera,
            int frameIndex,
            out VividPrimitiveDrawSet drawSet)
        {
            drawSet = m_ScheduledMainViewDrawSet;
            int resolvedFrameIndex = ResolveFrameIndex(frameIndex);
            bool matches = drawSet != null
                           && !renderingCamera.stereoEnabled
                           && !cullingCamera.stereoEnabled
                           && m_ScheduledMainViewRenderingCameraId.Equals(renderingCamera.GetEntityId())
                           && m_ScheduledMainViewCullingCameraId.Equals(cullingCamera.GetEntityId())
                           && m_ScheduledMainViewFrameIndex == resolvedFrameIndex
                           && m_ScheduledMainViewSceneRevision == PrimitiveScene.SceneRevision
                           && drawSet.MatchesPendingBuild(
                               m_ScheduledMainViewSceneRevision,
                               m_ScheduledMainViewFrameIndex);
            ClearScheduledMainViewDrawSet();
            if (matches)
                return true;

            drawSet = null;
            return false;
        }

        private void ClearScheduledMainViewDrawSet()
        {
            m_ScheduledMainViewRenderingCameraId = EntityId.None;
            m_ScheduledMainViewCullingCameraId = EntityId.None;
            m_ScheduledMainViewDrawSet = null;
            m_ScheduledMainViewFrameIndex = -1;
            m_ScheduledMainViewSceneRevision = 0u;
        }

        private void CullInternal(
            Camera camera,
            CommandBuffer cmd,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute,
            VividInstancePassMask passMask,
            in VividGPUDrivenOcclusionCullingParameters occlusionParameters,
            string cameraName,
            VividPrimitiveDrawSet drawSet)
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
                    MeshLODErrorThreshold,
                    occlusionParameters,
                    drawSet?.LegacyInstanceIndexBuffer,
                    drawSet?.DrawCount ?? -1
                );
            }

            ReportStats(camera, cameraName);
        }

        internal bool DispatchOcclusionRetest(
            CommandBuffer cmd,
            ComputeShader gpuMeshletCullingCompute,
            RTHandle currentOccluderDepthPyramid,
            Matrix4x4 currentViewProjectionMatrix,
            int width,
            int height,
            int textureWidth,
            int textureHeight,
            int mipCount)
        {
            ThrowIfDisposed();
            return m_CullingDispatcher.DispatchOcclusionRetest(
                cmd,
                m_BufferSet,
                gpuMeshletCullingCompute,
                currentOccluderDepthPyramid,
                currentViewProjectionMatrix,
                width,
                height,
                textureWidth,
                textureHeight,
                mipCount);
        }

        public void CullShadowCascades(
            CommandBuffer cmd,
            VividGPUCullingContext[] cullingContexts,
            int cullingContextCount,
            in VividGPULODSelectionContext lodSelectionContext,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute
        )
        {
            CullShadowCascades(
                cmd,
                cullingContexts,
                cullingContextCount,
                lodSelectionContext,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                null);
        }

        internal void CullShadowCascades(
            CommandBuffer cmd,
            VividGPUCullingContext[] cullingContexts,
            int cullingContextCount,
            in VividGPULODSelectionContext lodSelectionContext,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute,
            VividPrimitiveDrawSet drawSet
        )
        {
            ThrowIfDisposed();

            if (cullingContexts == null)
            {
                throw new ArgumentNullException(nameof(cullingContexts));
            }

            if (cullingContextCount <= 0 || cullingContextCount > cullingContexts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(cullingContextCount));
            }

            m_ShadowCullingDispatcher ??= new VividGPUDrivenCullingDispatcher(supportsOcclusion: false);
            m_ShadowCullingDispatcher.DispatchBatch(
                cmd,
                cullingContexts,
                cullingContextCount,
                lodSelectionContext,
                SceneData,
                m_BufferSet,
                gpuInstanceCullingCompute,
                meshletListBuildCompute,
                gpuMeshletCullingCompute,
                fixupVisibleMeshletIndirectDrawArgsCompute,
                ForcedMeshLODNodeDepth,
                MeshLODErrorThreshold,
                default,
                drawSet?.LegacyInstanceIndexBuffer,
                drawSet?.DrawCount ?? -1
            );
            m_ShadowCullingContextCount = cullingContextCount;
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            m_BufferSet.BindGlobals(cmd);
            m_PrimitiveSceneBufferSet.BindGlobals(cmd, PrimitiveScene);
            m_CullingDispatcher.BindGlobals(cmd);
            if (m_TextureBackend is IGPUDrivenTerrainRuntimeVirtualTextureBackend terrainRVTBackend)
                terrainRVTBackend.BindTerrainRuntimeVirtualTextureGlobals(cmd);
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
            m_CurrentMainViewDrawSet = null;
            ClearScheduledMainViewDrawSet();
            m_PrimitiveDrawSetSystem.Dispose();
            m_ShadowPrimitiveDrawSetSystem.Dispose();
            VividMeshletRendererDatabase.instance.InvalidatePrimitiveHandles();
            PrimitiveScene.Dispose();
            m_BufferSet.Dispose();
            m_PrimitiveSceneBufferSet.Dispose();
            m_CullingDispatcher.Dispose();
            m_ShadowCullingDispatcher?.Dispose();
            m_ShadowCullingDispatcher = null;
            m_ShadowCullingContextCount = 0;
            m_TextureBackend.Dispose();
            if (m_LegacyBindlessBackend != null && !ReferenceEquals(m_LegacyBindlessBackend, m_TextureBackend))
                m_LegacyBindlessBackend.Dispose();
            m_IsDisposed = true;
            VividGPUDrivenOcclusionHistorySystem.Clear();
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
            if (RequiresTextureBackendRecreation(
                    gpuDrivenSystem.TextureBackendMode,
                    gpuDrivenSystem.m_TerrainRuntimeVirtualTextureRequested,
                    asset))
            {
                Shutdown();
                gpuDrivenSystem = instance;
            }
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera, cameraData.cameraName);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            Camera cullingCamera = ResolveCullingCameraForDebug(camera);
            bool hasScheduledDrawSet = gpuDrivenSystem.TryConsumeScheduledMainViewDrawSet(
                camera,
                cullingCamera,
                cameraData.frameIndex,
                out VividPrimitiveDrawSet scheduledDrawSet);
            if (!hasScheduledDrawSet)
                PrepareFrameIfNeeded(gpuDrivenSystem, cameraData.frameIndex, reportStats: false);
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera, cameraData.cameraName);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            VividPrimitiveDrawSet scheduledShadowDrawSet =
                gpuDrivenSystem.ScheduleShadowDrawSet(
                    camera,
                    frameData.GetOrCreate<VividShadowData>(),
                    cameraData.frameIndex);

            if (gpuDrivenSystem.m_TextureBackend is IGPUDrivenTerrainRuntimeVirtualTextureBackend terrainRVTBackend)
            {
                terrainRVTBackend.UpdateTerrainRuntimeVirtualTextures(
                    camera,
                    cameraData.frameIndex);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenApplySettingsMarker.Auto())
            {
                ApplyResolvedSettings(gpuDrivenSystem);
            }

            VividGPUDrivenOcclusionHistorySystem.PurgeDestroyedCameras();

            VividRPCoreResources resources;
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenResolveResourcesMarker.Auto())
            {
                resources = PipelineResourceManager.Get<VividRPCoreResources>();
            }
            bool occlusionObservationMode = !ReferenceEquals(cullingCamera, camera);
            bool occlusionFeatureSupported = !PassRecorder.UsesExperimentalMeshShaderRasterization
                && asset.EnableGPUDrivenOcclusionCulling
                && !camera.stereoEnabled
                && !cullingCamera.stereoEnabled
                && resources?.GPUMeshletCullingCompute != null;
            var temporalData = frameData.GetOrCreate<VividTemporalData>();
            int occlusionWidth = cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : Mathf.Max(1, cameraData.pixelWidth > 0 ? cameraData.pixelWidth : Screen.width);
            int occlusionHeight = cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : Mathf.Max(1, cameraData.pixelHeight > 0 ? cameraData.pixelHeight : Screen.height);
            bool hasOcclusionHistory;
            bool occlusionCullingEnabled;
            VividGPUDrivenOcclusionCullingParameters occlusionParameters = default;
            VividGPUDrivenOcclusionCullingParameters observationRetestParameters = default;
            if (occlusionObservationMode)
            {
                hasOcclusionHistory = occlusionFeatureSupported
                    && VividGPUDrivenOcclusionHistorySystem.TryGetObservationParameters(
                        cullingCamera,
                        out occlusionParameters,
                        out observationRetestParameters);
                occlusionCullingEnabled = hasOcclusionHistory;
                if (!hasOcclusionHistory)
                    occlusionParameters = default;
            }
            else
            {
                occlusionCullingEnabled = occlusionFeatureSupported;
                hasOcclusionHistory = VividGPUDrivenOcclusionHistorySystem.TryGetPreviousParameters(
                    camera,
                    occlusionCullingEnabled,
                    temporalData.resetPostProcessingHistory,
                    occlusionWidth,
                    occlusionHeight,
                    out occlusionParameters);
                if (occlusionCullingEnabled && !hasOcclusionHistory)
                    VividGPUDrivenOcclusionHistorySystem.InvalidateSnapshots(camera);
            }

            gpuDrivenSystem.CullMainView(
                cullingCamera,
                cmd,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute,
                occlusionParameters,
                cameraName: cameraData.cameraName,
                frameIndex: cameraData.frameIndex,
                scheduledDrawSet: scheduledDrawSet,
                allowPrimitiveDrawSet: !camera.stereoEnabled && !cullingCamera.stereoEnabled);
            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenBindGlobalsMarker.Auto())
            {
                gpuDrivenSystem.BindGlobals(cmd);
            }

            using (RenderPassProfilingUtility.PrepareFrameSubsystemGPUDrivenSetFrameDataMarker.Auto())
            {
                PassRecorder.SetGPUDrivenFrameData(
                    gpuDrivenSystem.VisibleMeshletRenderRequestsBuffer,
                    gpuDrivenSystem.VisibleMeshletIndirectDrawArgsBuffer,
                    gpuDrivenSystem.CurrentMainViewDrawSet,
                    scheduledShadowDrawSet);
                PassRecorder.SetGPUDrivenOcclusionFrameData(
                    occlusionCullingEnabled,
                    hasOcclusionHistory,
                    gpuDrivenSystem.CullingBufferSet,
                    occlusionObservationMode,
                    observationRetestParameters);
            }
        }

        private static void PrepareFrameIfNeeded(VividGPUDrivenSystem gpuDrivenSystem, int frameIndex, bool reportStats = true)
        {
            int resolvedFrameIndex = ResolveFrameIndex(frameIndex);
            if (!ShouldPrepareFrame(s_PreparedFrameIndex, resolvedFrameIndex, Application.isPlaying))
                return;

            gpuDrivenSystem.PrepareFrame(reportStats);
            s_PreparedFrameIndex = resolvedFrameIndex;
        }

        private static int ResolveFrameIndex(int frameIndex)
        {
            return frameIndex >= 0 ? frameIndex : Time.frameCount;
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
                bool textureBackendAvailable = m_TextureBackend.IsAvailable;
                string statusMessage = textureBackendAvailable
                    ? string.Empty
                    : m_TextureBackend.UnavailableReason;
                GPUDrivenTextureBackendStats backendStats = m_TextureBackend.GetStats();

                VividGPUDrivenStatsRegistry.Report(
                    new VividGPUDrivenStats(
                        true,
                        statusMessage,
                        camera != null,
                        camera != null ? cameraName : null,
                        camera != null ? camera.cameraType : default,
                        Time.frameCount,
                        Time.realtimeSinceStartupAsDouble,
                        m_TextureBackend.DisplayName,
                        textureBackendAvailable,
                        VividMeshletRendererDatabase.instance.rendererCount,
                        SceneData.InstanceCount,
                        SceneData.MaterialCount,
                        SceneData.SurfaceBindingCount,
                        SceneData.MeshLODNodeCount,
                        SceneData.MeshletCount,
                        SceneData.VertexCount,
                        SceneData.IndexCount,
                        m_CullingDispatcher.BufferSet.MaxMeshletListBuildJobCount,
                        m_CullingDispatcher.BufferSet.MaxVisibleMeshletRenderRequestCount,
                        backendStats.PoolCount,
                        backendStats.ResourceCapacity,
                        backendStats.AllocatedResourceCount,
                        backendStats.CreateResourceCallCountThisFrame,
                        backendStats.RegisteredResourceCount,
                        ForcedMeshLODNodeDepth,
                        MeshLODErrorThreshold,
                        PrimitiveScene.GetStats(),
                        m_CurrentMainViewDrawSet?.GetStats() ?? default));
            }
        }
    }
}
