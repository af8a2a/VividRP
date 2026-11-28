using VividRP.Runtime.VirtualShadowMap;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum VSMDebugVisualizationMode
    {
        [InspectorName("Device Depth")]
        DeviceDepth = 0,
        [InspectorName("Depth Coverage (Occupancy)")]
        Occupancy = 1,
        [InspectorName("Depth Heat Map")]
        DepthHeatMap = 2,
        [InspectorName("Page States (Static + Dynamic)")]
        PageStates = 3,
        Requested = 4,
        Allocated = 5,
        [InspectorName("Dirty / Redrawn")]
        Dirty = 6,
        Cached = 7,
        Unmapped = 8,
        Evicted = 9,
        Overflow = 10,
        Deferred = 11,
        RequestRole = 12,
        PhysicalPage = 13,
        [InspectorName("Frames Since Last Request (0-120)")]
        PageAge = 14,
        StaticCache = 15,
        DynamicCache = 16,
        [InspectorName("Page Occupancy (Static / Final)")]
        PageOccupancy = 17,
    }

    public enum VSMDebugPoolMode
    {
        [InspectorName("Final Depth")]
        Combined = 0,
        Static = 1,
        [InspectorName("Final Depth (Legacy Dynamic)")]
        Dynamic = 2,
    }

    public enum VSMDebugDepthLayer
    {
        Layer0, Layer1, Layer2, Layer3, Layer4, Layer5, Layer6, Layer7,
        Layer8, Layer9, Layer10, Layer11, Layer12, Layer13, Layer14, Layer15,
    }

    public enum VSMDebugLightScope
    {
        [InspectorName("Directional Clipmap")]
        Directional = 0,
        [InspectorName("Local Lights (Point / Spot)")]
        Local = 1,
    }

    public sealed class VSMDebugPass : RasterPass
    {
        internal const string VSMDebugShaderName = "Hidden/VividRP/VSMDebug";

        private static readonly int VSMPhysicalPagePoolId =
            Shader.PropertyToID("_VSMPhysicalPagePool");

        private static readonly int VSMPrototypeAvailableId =
            Shader.PropertyToID("_VSMPrototypeAvailable");
        private static readonly int VSMDebugVisualizationModeId =
            Shader.PropertyToID("_VSMDebugVisualizationMode");
        private static readonly int VSMDebugExposureId =
            Shader.PropertyToID("_VSMDebugExposure");
        private static readonly int VSMDebugPoolModeId =
            Shader.PropertyToID("_VSMDebugPoolMode");
        private static readonly int PageTableId = Shader.PropertyToID("_VSMPrototypePageTable");
        private static readonly int PageMetadataId = Shader.PropertyToID("_VSMPrototypePageMetadata");
        private static readonly int PageRequestFlagsId = Shader.PropertyToID("_VSMPageRequestFlags");
        private static readonly int AllocatorCountersId = Shader.PropertyToID("_VSMPrototypeAllocatorCounters");
        private static readonly int PageLayoutId = Shader.PropertyToID("_VSMDebugPageLayout");
        private static readonly int OutputSizeId = Shader.PropertyToID("_VSMDebugOutputSize");
        private static readonly int DepthLayerId = Shader.PropertyToID("_VSMDebugDepthLayer");
        private static readonly int FrameIndexId = Shader.PropertyToID("_VSMDebugFrameIndex");
        private static readonly int LocalInfoId = Shader.PropertyToID("_VSMDebugLocalInfo");
        private static readonly int LocalPagePressureId = Shader.PropertyToID("_VSMPagePressure");

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField, Tooltip("Page modes show the current camera's completed VSM submission. "
            + "Pool and Exposure affect only depth views. Header: RES resident, REQ requested, "
            + "NEW assignments (including reuse), OVF overflow. Hatched output means no current snapshot.")]
        private VSMDebugVisualizationMode m_VisualizationMode =
            VSMDebugVisualizationMode.DeviceDepth;

        [SerializeField]
        private VSMDebugPoolMode m_PoolMode = VSMDebugPoolMode.Combined;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        [SerializeField, Tooltip("Legacy depth rank; clamped to zero for UE single-depth storage.")]
        private VSMDebugDepthLayer m_DepthLayer;

        [SerializeField, Tooltip("Inspect either the Directional Clipmap pool or the Local Point/Spot VSM pool.")]
        private VSMDebugLightScope m_LightScope = VSMDebugLightScope.Directional;

        [SerializeField, Tooltip("When Light Scope is Local: -1 displays all active local VSMs (Mip 0 / Single-Page overview); >= 0 focuses on that local VSM's full Mip pyramid.")]
        private int m_LocalVSMIndex = -1;

        public VSMDebugLightScope LightScope
        {
            get => NormalizeLightScope(m_LightScope);
            set => m_LightScope = NormalizeLightScope(value);
        }

        public int LocalVSMIndex
        {
            get => Mathf.Max(m_LocalVSMIndex, -1);
            set => m_LocalVSMIndex = Mathf.Max(value, -1);
        }

        public int DepthLayer
        {
            get => Mathf.Clamp((int)m_DepthLayer, 0, VirtualShadowMapPrototypeRuntime.DepthLayerCount - 1);
            set => m_DepthLayer = (VSMDebugDepthLayer)Mathf.Clamp(value, 0, VirtualShadowMapPrototypeRuntime.DepthLayerCount - 1);
        }

        private Material m_Material;
        private TextureHandle m_PhysicalPagePoolHandle;

        private bool m_PhysicalPageAvailable;
        private bool m_ShouldSkipExecution;
        private bool m_PageStateResourcesAvailable;
        private bool m_LocalPageResourcesAvailable;
        private ulong m_CameraEntityId;
        private int m_FrameIndex;

        public VSMDebugVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = NormalizeVisualizationMode(value);
        }

        public float Exposure
        {
            get => m_Exposure;
            set => m_Exposure = Mathf.Clamp(value, -16f, 16f);
        }

        public VSMDebugPoolMode PoolMode
        {
            get => m_PoolMode;
            set => m_PoolMode = NormalizePoolMode(value);
        }

        public VSMDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VSMDebugPass));
            m_OutputTexture = RenderGraphTexture.CreateColorTarget(
                "VSMDebugOutput",
                GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.FilterMode = FilterMode.Point;
            m_OutputTexture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.VSMDebugShader;
            shader ??= Shader.Find(VSMDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find shader '{VSMDebugShaderName}' for {nameof(VSMDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_PhysicalPagePoolHandle = default;

            m_PhysicalPageAvailable = false;
            m_PageStateResourcesAvailable = false;
            m_LocalPageResourcesAvailable = false;

            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            bool prototypeEnabled = settings != null
                && settings.enableVirtualShadowMapPrototype.value;
            bool localScope = NormalizeLightScope(m_LightScope) == VSMDebugLightScope.Local;
            if (prototypeEnabled
                && VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform())
            {
                if (localScope)
                {
                    var localPipeline = VirtualShadowMapPrototypeRuntime.LocalCasterPipeline;
                    var localGrid = VirtualShadowMapPrototypeRuntime.LocalLightGrid;
                    if (localPipeline.Active && localGrid.Active)
                    {
                        if (IsPageStateMode(m_VisualizationMode))
                        {
                            bool valid = localPipeline.PageTable != null && localPipeline.PageTable.IsValid()
                                && localPipeline.PageMetadata != null && localPipeline.PageMetadata.IsValid()
                                && localGrid.LocalPageRequestFlags != null && localGrid.LocalPageRequestFlags.IsValid()
                                && localPipeline.AllocatorCounters != null && localPipeline.AllocatorCounters.IsValid()
                                && localGrid.UnifiedProjections != null && localGrid.UnifiedProjections.IsValid()
                                && localPipeline.PagePressure != null && localPipeline.PagePressure.IsValid();
                            if (valid)
                            {
                                bool t = PassRecorder.ImportBufferForPass(this, localPipeline.PageTable, AccessFlags.Read).IsValid();
                                bool m = PassRecorder.ImportBufferForPass(this, localPipeline.PageMetadata, AccessFlags.Read).IsValid();
                                bool r = PassRecorder.ImportBufferForPass(this, localGrid.LocalPageRequestFlags, AccessFlags.Read).IsValid();
                                bool c = PassRecorder.ImportBufferForPass(this, localPipeline.AllocatorCounters, AccessFlags.Read).IsValid();
                                bool p = PassRecorder.ImportBufferForPass(this, localGrid.UnifiedProjections, AccessFlags.Read).IsValid();
                                bool pr = PassRecorder.ImportBufferForPass(this, localPipeline.PagePressure, AccessFlags.Read).IsValid();
                                m_LocalPageResourcesAvailable = t && m && r && c && p && pr;
                            }
                        }
                        else if (localPipeline.PhysicalPagePool?.rt != null)
                        {
                            m_PhysicalPagePoolHandle = PassRecorder.ImportTextureForPass(
                                this,
                                localPipeline.PhysicalPagePool,
                                AccessFlags.Read);
                        }
                    }
                }
                else if (VirtualShadowMapPrototypeRuntime.EnsurePhysicalPageForBinding())
                {
                    if (IsPageStateMode(m_VisualizationMode))
                    {
                        if (VirtualShadowMapPrototypeRuntime.HasPageRequestResources)
                        {
                            bool tableValid = PassRecorder.ImportBufferForPass(
                                this, VirtualShadowMapPrototypeRuntime.PageTable, AccessFlags.Read).IsValid();
                            bool metadataValid = PassRecorder.ImportBufferForPass(
                                this, VirtualShadowMapPrototypeRuntime.PageMetadata, AccessFlags.Read).IsValid();
                            bool requestsValid = PassRecorder.ImportBufferForPass(
                                this, VirtualShadowMapPrototypeRuntime.PageRequestFlags, AccessFlags.Read).IsValid();
                            bool countersValid = PassRecorder.ImportBufferForPass(
                                this, VirtualShadowMapPrototypeRuntime.AllocatorCounters, AccessFlags.Read).IsValid();
                            m_PageStateResourcesAvailable = tableValid && metadataValid && requestsValid && countersValid;
                        }
                    }
                    else
                    {
                        m_PhysicalPagePoolHandle = PassRecorder.ImportTextureForPass(
                            this,
                            VirtualShadowMapPrototypeRuntime.PhysicalPagePool,
                            AccessFlags.Read);
                    }
                }
            }

            m_PhysicalPageAvailable = m_PhysicalPagePoolHandle.IsValid();

            var cameraData = frameData?.GetOrCreate<VividCameraData>();
            m_CameraEntityId = cameraData?.camera != null
                ? EntityId.ToULong(cameraData.camera.GetEntityId()) : 0ul;
            m_FrameIndex = cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex : Time.frameCount;
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            int width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData?.actualWidth ?? 0,
                cameraData?.pixelWidth ?? 0,
                Screen.width);
            int height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData?.actualHeight ?? 0,
                cameraData?.pixelHeight ?? 0,
                Screen.height);
            ConfigureOutputTexture(width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution
                || m_Material == null
                || m_OutputTexture?.innerHandle.IsValid() != true)
            {
                return;
            }

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            bool localScope = NormalizeLightScope(m_LightScope) == VSMDebugLightScope.Local;
            if (localScope
                && IsPageStateMode(m_VisualizationMode)
                && m_LocalPageResourcesAvailable
                && VirtualShadowMapPrototypeRuntime.LocalCasterPipeline.Active
                && VirtualShadowMapPrototypeRuntime.LocalLightGrid.Active
                && VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(m_CameraEntityId, m_FrameIndex))
            {
                var localPipeline = VirtualShadowMapPrototypeRuntime.LocalCasterPipeline;
                var localGrid = VirtualShadowMapPrototypeRuntime.LocalLightGrid;
                var registry = VirtualShadowMapPrototypeRuntime.Registry;
                mpb.SetBuffer(PageTableId, localPipeline.PageTable);
                mpb.SetBuffer(PageMetadataId, localPipeline.PageMetadata);
                mpb.SetBuffer(PageRequestFlagsId, localGrid.LocalPageRequestFlags);
                mpb.SetBuffer(AllocatorCountersId, localPipeline.AllocatorCounters);
                mpb.SetBuffer(VirtualShadowMapProjectionSet.BufferId, localGrid.UnifiedProjections);
                mpb.SetBuffer(LocalPagePressureId, localPipeline.PagePressure);
                mpb.SetVector(PageLayoutId, new Vector4(
                    localPipeline.PhysicalPagesPerRow,
                    localGrid.UnifiedPageTableEntryCount,
                    localPipeline.PhysicalPageCapacity,
                    0f));
                mpb.SetVector(LocalInfoId, new Vector4(
                    registry.DirectionalCount,
                    registry.ProjectionCount,
                    Mathf.Max(m_LocalVSMIndex, -1),
                    localPipeline.PhysicalPageCapacity));
                mpb.SetInt(VSMDebugVisualizationModeId, (int)NormalizeVisualizationMode(m_VisualizationMode));
                mpb.SetInt(FrameIndexId, m_FrameIndex);
                mpb.SetVector(OutputSizeId, new Vector4(
                    m_OutputTexture.desc.Width, m_OutputTexture.desc.Height, 0f, 0f));
                CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 2);
                return;
            }

            if (!localScope
                && IsPageStateMode(m_VisualizationMode)
                && m_PageStateResourcesAvailable
                && VirtualShadowMapPrototypeRuntime.HasPageRequestResources
                && VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(m_CameraEntityId, m_FrameIndex))
            {
                BindPageStateResources(mpb);
                mpb.SetInt(VSMDebugVisualizationModeId, (int)NormalizeVisualizationMode(m_VisualizationMode));
                mpb.SetInt(FrameIndexId, m_FrameIndex);
                mpb.SetVector(OutputSizeId, new Vector4(
                    m_OutputTexture.desc.Width, m_OutputTexture.desc.Height, 0f, 0f));
                CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 1);
                return;
            }

            RTHandle physicalPagePool = localScope
                ? VirtualShadowMapPrototypeRuntime.LocalCasterPipeline.PhysicalPagePool
                : VirtualShadowMapPrototypeRuntime.PhysicalPagePool;
            Texture physicalPagePoolTexture = physicalPagePool != null
                ? physicalPagePool.ResolveTexture()
                : null;
            bool pageAvailable = !IsPageStateMode(m_VisualizationMode) && m_PhysicalPageAvailable
                && VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(m_CameraEntityId, m_FrameIndex)
                && physicalPagePoolTexture != null;

            mpb.SetTexture(
                VSMPhysicalPagePoolId,
                physicalPagePoolTexture != null
                    ? physicalPagePoolTexture
                    : Texture2D.blackTexture);

            mpb.SetInt(VSMPrototypeAvailableId, pageAvailable ? 1 : 0);
            mpb.SetInt(
                VSMDebugVisualizationModeId,
                (int)NormalizeVisualizationMode(m_VisualizationMode));
            mpb.SetFloat(VSMDebugExposureId, Mathf.Clamp(m_Exposure, -16f, 16f));
            mpb.SetInt(VSMDebugPoolModeId, (int)NormalizePoolMode(m_PoolMode));
            mpb.SetInt(DepthLayerId, DepthLayer);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_PhysicalPagePoolHandle = default;

            m_PhysicalPageAvailable = false;
            m_ShouldSkipExecution = false;
            m_PageStateResourcesAvailable = false;
            m_LocalPageResourcesAvailable = false;
            m_CameraEntityId = 0ul;
            m_FrameIndex = -1;
        }

        internal static VSMDebugLightScope NormalizeLightScope(VSMDebugLightScope scope)
        {
            return scope == VSMDebugLightScope.Local
                ? VSMDebugLightScope.Local
                : VSMDebugLightScope.Directional;
        }

        internal static bool IsPageStateMode(VSMDebugVisualizationMode mode)
        {
            return mode >= VSMDebugVisualizationMode.PageStates && mode <= VSMDebugVisualizationMode.PageOccupancy;
        }

        internal static void BindPageStateResources(MaterialPropertyBlock properties)
        {
            properties.SetBuffer(PageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
            properties.SetBuffer(PageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
            properties.SetBuffer(PageRequestFlagsId, VirtualShadowMapPrototypeRuntime.PageRequestFlags);
            properties.SetBuffer(AllocatorCountersId, VirtualShadowMapPrototypeRuntime.AllocatorCounters);
            properties.SetVector(PageLayoutId, new Vector4(
                VirtualShadowMapPrototypeRuntime.PagesPerAxis,
                VirtualShadowMapPrototypeRuntime.PageTableEntryCount,
                VirtualShadowMapPrototypeRuntime.PhysicalPageCapacity, 0f));
        }

        internal static VSMDebugVisualizationMode NormalizeVisualizationMode(
            VSMDebugVisualizationMode mode)
        {
            if (IsPageStateMode(mode))
                return mode;
            return mode switch
            {
                VSMDebugVisualizationMode.DeviceDepth => VSMDebugVisualizationMode.DeviceDepth,
                VSMDebugVisualizationMode.Occupancy => VSMDebugVisualizationMode.Occupancy,
                VSMDebugVisualizationMode.DepthHeatMap => VSMDebugVisualizationMode.DepthHeatMap,
                _ => VSMDebugVisualizationMode.DeviceDepth,
            };
        }

        internal static VSMDebugPoolMode NormalizePoolMode(VSMDebugPoolMode mode)
        {
            return mode switch
            {
                VSMDebugPoolMode.Combined => VSMDebugPoolMode.Combined,
                VSMDebugPoolMode.Static => VSMDebugPoolMode.Static,
                VSMDebugPoolMode.Dynamic => VSMDebugPoolMode.Dynamic,
                _ => VSMDebugPoolMode.Combined,
            };
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = Mathf.Max(1, width);
            m_OutputTexture.desc.Height = Mathf.Max(1, height);
            m_OutputTexture.desc.ColorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = FilterMode.Point;
            m_OutputTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "VSMDebugOutput";
        }
    }
}
