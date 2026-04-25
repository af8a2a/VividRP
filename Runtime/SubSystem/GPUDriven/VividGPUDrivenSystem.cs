using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven.Bindless;

namespace VividRP.Runtime.GPUDriven
{
    public sealed class VividGPUDrivenSystem : IDisposable
    {
        private static VividGPUDrivenSystem s_Instance;
        private static VividGPUDrivenDebugOverlayRenderer s_DebugOverlayRenderer;
        private static bool s_Initialized;
        private static int s_PreparedFrameIndex = -1;

        private readonly VividGPUDrivenBufferSet m_BufferSet;
        private readonly VividGPUDrivenCullingDispatcher m_CullingDispatcher;
        private readonly VividGPUDrivenObjectTracker m_ObjectTracker;
        private readonly VividGPUDrivenSceneDataBuilder m_SceneDataBuilder;
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
        internal static void Initialize()
        {
            if (s_Initialized)
                return;

            RegisterFrameContextCallbacks();
            s_Initialized = true;
        }

        internal static void Deinitialize()
        {
#if UNITY_EDITOR
            RegisterFrameContextCallbacks();
            s_Initialized = true;
#else
            if (s_Initialized)
            {
                UnregisterFrameContextCallbacks();
                s_Initialized = false;
            }
#endif

            DisposeDebugOverlayRenderer();
            Shutdown();
        }

        public static VividGPUDrivenSystem instance
        {
            get
            {
                Initialize();
                return s_Instance ??= new VividGPUDrivenSystem();
            }
        }

        public static bool HasInstance => s_Instance != null && !s_Instance.m_IsDisposed;

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

        private static void RegisterFrameContextCallbacks()
        {
            FrameContextSystem.SubsystemPreRender -= Update;
            FrameContextSystem.SubsystemPreRender += Update;
            FrameContextSystem.SubsystemPostRender -= RenderDebugOverlay;
            FrameContextSystem.SubsystemPostRender += RenderDebugOverlay;
            FrameContextSystem.SubsystemDispose -= Deinitialize;
            FrameContextSystem.SubsystemDispose += Deinitialize;
        }

        private static void UnregisterFrameContextCallbacks()
        {
            FrameContextSystem.SubsystemPreRender -= Update;
            FrameContextSystem.SubsystemPostRender -= RenderDebugOverlay;
            FrameContextSystem.SubsystemDispose -= Deinitialize;
        }

        public static bool TryGetCurrentVisibleMeshletBuffers(
            out GraphicsBuffer visibleMeshletRenderRequestsBuffer,
            out GraphicsBuffer visibleMeshletIndirectDrawArgsBuffer)
        {
            if (s_Instance == null || s_Instance.m_IsDisposed || !s_Instance.IsAvailable)
            {
                visibleMeshletRenderRequestsBuffer = null;
                visibleMeshletIndirectDrawArgsBuffer = null;
                return false;
            }

            visibleMeshletRenderRequestsBuffer = s_Instance.VisibleMeshletRenderRequestsBuffer;
            visibleMeshletIndirectDrawArgsBuffer = s_Instance.VisibleMeshletIndirectDrawArgsBuffer;
            return visibleMeshletRenderRequestsBuffer != null && visibleMeshletIndirectDrawArgsBuffer != null;
        }

        public void PrepareFrame()
        {
            ThrowIfDisposed();

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
            ReportStats(null);
        }

        public void Cull(
            Camera camera,
            CommandBuffer cmd,
            ComputeShader gpuInstanceCullingCompute,
            ComputeShader meshletListBuildCompute,
            ComputeShader gpuMeshletCullingCompute = null,
            ComputeShader fixupVisibleMeshletIndirectDrawArgsCompute = null,
            VividInstancePassMask passMask = VividInstancePassMask.Main
        )
        {
            ThrowIfDisposed();

            cmd.BeginSample("GPUDrivenCulling");
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
            cmd.EndSample("GPUDrivenCulling");
            ReportStats(camera);
        }

        public void BindGlobals(CommandBuffer cmd)
        {
            ThrowIfDisposed();
            m_BufferSet.BindGlobals(cmd);
            m_CullingDispatcher.BindGlobals(cmd);
        }

        public static void Shutdown()
        {
            s_Instance?.Dispose();
            s_Instance = null;
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
            m_ObjectTracker.Dispose();
            BindlessTextureContainer.Dispose();
            m_IsDisposed = true;
            VividGPUDrivenStatsRegistry.Clear();
        }

        private static void Update(ContextContainer frameData, CommandBuffer cmd)
        {
            if (!s_Initialized)
                Initialize();

            if (frameData == null || cmd == null)
                return;

            VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset == null || !asset.EnableGPUDriven)
            {
                PassRecorder.SetGPUDrivenFrameData(null, null);
                DisposeDebugOverlayRenderer();
                Shutdown();
                return;
            }

            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            Camera camera = cameraData.camera;
            if (camera == null)
            {
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            VividGPUDrivenSystem gpuDrivenSystem = instance;
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            PrepareFrameIfNeeded(cameraData.frameIndex);
            if (!gpuDrivenSystem.IsAvailable)
            {
                gpuDrivenSystem.ReportStats(camera);
                PassRecorder.SetGPUDrivenFrameData(null, null);
                return;
            }

            ApplyResolvedSettings(gpuDrivenSystem);

            VividRPCoreResources resources = PipelineResourceManager.Get<VividRPCoreResources>();
            gpuDrivenSystem.Cull(
                camera,
                cmd,
                resources.GPUInstanceCullingCompute,
                resources.MeshletListBuildCompute,
                resources.GPUMeshletCullingCompute,
                resources.FixupVisibleMeshletIndirectDrawArgsCompute);
            gpuDrivenSystem.BindGlobals(cmd);
            PassRecorder.SetGPUDrivenFrameData(
                gpuDrivenSystem.VisibleMeshletRenderRequestsBuffer,
                gpuDrivenSystem.VisibleMeshletIndirectDrawArgsBuffer);
        }

        private static void RenderDebugOverlay(ContextContainer frameData, CommandBuffer cmd)
        {
            if (frameData == null || cmd == null)
                return;

            VividRenderPipelineAsset asset = VividRenderPipelineAsset.GetActiveAsset();
            if (asset is not { EnableGPUDriven: true, EnableGPUDrivenDebugOverlay: true })
                return;

            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            Camera camera = cameraData.camera;
            if (camera == null
                || !TryGetCurrentVisibleMeshletBuffers(out _, out GraphicsBuffer indirectDrawArgsBuffer)
                || indirectDrawArgsBuffer == null)
            {
                return;
            }

            VividGPUDrivenDebugOverlayRenderer overlayRenderer = GetOrCreateDebugOverlayRenderer();
            if (overlayRenderer == null || !overlayRenderer.IsAvailable)
                return;

            instance.BindGlobals(cmd);
            overlayRenderer.Draw(cmd, camera, indirectDrawArgsBuffer);
        }

        private static void PrepareFrameIfNeeded(int frameIndex)
        {
            int resolvedFrameIndex = frameIndex >= 0 ? frameIndex : Time.frameCount;
            if (!ShouldPrepareFrame(s_PreparedFrameIndex, resolvedFrameIndex, Application.isPlaying))
                return;

            instance.PrepareFrame();
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

        private static VividGPUDrivenDebugOverlayRenderer GetOrCreateDebugOverlayRenderer()
        {
            if (s_DebugOverlayRenderer != null)
                return s_DebugOverlayRenderer;

            VividRPCoreResources resources = PipelineResourceManager.Get<VividRPCoreResources>();
            s_DebugOverlayRenderer = new VividGPUDrivenDebugOverlayRenderer(resources?.GPUDrivenMeshletDebugShader);
            return s_DebugOverlayRenderer;
        }

        private static void DisposeDebugOverlayRenderer()
        {
            if (s_DebugOverlayRenderer == null)
                return;

            s_DebugOverlayRenderer.Dispose();
            s_DebugOverlayRenderer = null;
        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VividGPUDrivenSystem));
            }
        }

        private void ReportStats(Camera camera)
        {
            string statusMessage = BindlessTextureContainer.IsAvailable
                ? string.Empty
                : BindlessTextureContainer.UnavailableReason;

            VividGPUDrivenStatsRegistry.Report(
                new VividGPUDrivenStats(
                    true,
                    statusMessage,
                    camera != null,
                    camera != null ? camera.name : null,
                    camera != null ? camera.cameraType : default,
                    Time.frameCount,
                    Time.realtimeSinceStartupAsDouble,
                    BindlessTextureContainer.IsAvailable,
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

    internal sealed class VividGPUDrivenDebugOverlayRenderer : IDisposable
    {
        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_OverlayAlphaId = Shader.PropertyToID("_OverlayAlpha");

        private readonly Material[] m_Materials = new Material[(int)VividRendererListID.Count];
        private readonly ProfilingSampler m_ProfilingSampler = new(nameof(VividGPUDrivenDebugOverlayRenderer));

        public VividGPUDrivenDebugOverlayRenderer(Shader shader)
        {
            if (shader == null)
                return;

            for (int rendererListIndex = 0; rendererListIndex < m_Materials.Length; rendererListIndex++)
            {
                Material material = CoreUtils.CreateEngineMaterial(shader);
                material.name = $"{nameof(VividGPUDrivenDebugOverlayRenderer)}_{(VividRendererListID)rendererListIndex}";
                ConfigureMaterial(material, (VividRendererListID)rendererListIndex);
                m_Materials[rendererListIndex] = material;
            }
        }

        public bool IsAvailable => m_Materials[0] != null;

        public void Draw(CommandBuffer cmd, Camera camera, GraphicsBuffer indirectDrawArgsBuffer)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));

            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (!IsAvailable || indirectDrawArgsBuffer == null)
                return;

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
                        continue;

                    cmd.DrawProceduralIndirect(
                        Matrix4x4.identity,
                        material,
                        0,
                        MeshTopology.Triangles,
                        indirectDrawArgsBuffer,
                        rendererListIndex * argsStride);
                }
            }
        }

        public void Dispose()
        {
            for (int index = 0; index < m_Materials.Length; index++)
            {
                if (m_Materials[index] == null)
                    continue;

                CoreUtils.Destroy(m_Materials[index]);
                m_Materials[index] = null;
            }
        }

        private static void ConfigureMaterial(Material material, VividRendererListID rendererListID)
        {
            material.SetFloat(s_CullId, (float)GetCullMode(rendererListID));
            material.SetFloat(s_OverlayAlphaId, 0.35f);
        }

        private static CullMode GetCullMode(VividRendererListID rendererListID)
        {
            if ((rendererListID & VividRendererListID.CullFront) != 0)
                return CullMode.Front;

            if ((rendererListID & VividRendererListID.CullOff) != 0)
                return CullMode.Off;

            return CullMode.Back;
        }
    }
}
