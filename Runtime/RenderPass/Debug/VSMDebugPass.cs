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
        [InspectorName("Page States (Shared Map / Static Cache)")]
        PageStates = 3,
        Requested = 4,
        Allocated = 5,
        [InspectorName("Dirty / Redrawn")]
        Dirty = 6,
        Cached = 7,
        Unmapped = 8,
        Evicted = 9,
        Overflow = 10,
    }

    public enum VSMDebugPoolMode
    {
        Combined = 0,
        Static = 1,
        Dynamic = 2,
    }

    public sealed class VSMDebugPass : RasterPass
    {
        internal const string VSMDebugShaderName = "Hidden/VividRP/VSMDebug";

        private static readonly int VSMPrototypeStaticPhysicalPageId =
            Shader.PropertyToID("_VSMPrototypeStaticPhysicalPage");
        private static readonly int VSMPrototypeDynamicPhysicalPageId =
            Shader.PropertyToID("_VSMPrototypeDynamicPhysicalPage");
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
        private static readonly int AllocatorCountersId = Shader.PropertyToID("_VSMPrototypeAllocatorCounters");
        private static readonly int PageLayoutId = Shader.PropertyToID("_VSMDebugPageLayout");
        private static readonly int OutputSizeId = Shader.PropertyToID("_VSMDebugOutputSize");

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

        private Material m_Material;
        private TextureHandle m_StaticPhysicalPageHandle;
        private TextureHandle m_DynamicPhysicalPageHandle;
        private bool m_PhysicalPageAvailable;
        private bool m_ShouldSkipExecution;
        private bool m_PageStateResourcesAvailable;
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
            m_StaticPhysicalPageHandle = default;
            m_DynamicPhysicalPageHandle = default;
            m_PhysicalPageAvailable = false;
            m_PageStateResourcesAvailable = false;

            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            bool prototypeEnabled = settings != null
                && settings.enableVirtualShadowMapPrototype.value;
            if (prototypeEnabled
                && VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform()
                && VirtualShadowMapPrototypeRuntime.EnsurePhysicalPageForBinding())
            {
                if (IsPageStateMode(m_VisualizationMode))
                {
                    if (VirtualShadowMapPrototypeRuntime.HasPageRequestResources)
                    {
                        bool tableValid = PassRecorder.ImportBufferForPass(
                            this, VirtualShadowMapPrototypeRuntime.PageTable, AccessFlags.Read).IsValid();
                        bool metadataValid = PassRecorder.ImportBufferForPass(
                            this, VirtualShadowMapPrototypeRuntime.PageMetadata, AccessFlags.Read).IsValid();
                        bool countersValid = PassRecorder.ImportBufferForPass(
                            this, VirtualShadowMapPrototypeRuntime.AllocatorCounters, AccessFlags.Read).IsValid();
                        m_PageStateResourcesAvailable = tableValid && metadataValid && countersValid;
                    }
                }
                else
                {
                    m_StaticPhysicalPageHandle = PassRecorder.ImportTextureForPass(
                        this,
                        VirtualShadowMapPrototypeRuntime.StaticPhysicalPage,
                        AccessFlags.Read);
                    m_DynamicPhysicalPageHandle = PassRecorder.ImportTextureForPass(
                        this,
                        VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage,
                        AccessFlags.Read);
                }
            }

            m_PhysicalPageAvailable = m_StaticPhysicalPageHandle.IsValid()
                && m_DynamicPhysicalPageHandle.IsValid();

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
            if (IsPageStateMode(m_VisualizationMode)
                && m_PageStateResourcesAvailable
                && VirtualShadowMapPrototypeRuntime.HasPageRequestResources
                && VirtualShadowMapPrototypeRuntime.HasPageDebugSnapshot(m_CameraEntityId, m_FrameIndex))
            {
                BindPageStateResources(mpb);
                mpb.SetInt(VSMDebugVisualizationModeId, (int)NormalizeVisualizationMode(m_VisualizationMode));
                mpb.SetVector(OutputSizeId, new Vector4(
                    m_OutputTexture.desc.Width, m_OutputTexture.desc.Height, 0f, 0f));
                CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 1);
                return;
            }

            RTHandle staticPhysicalPage =
                VirtualShadowMapPrototypeRuntime.StaticPhysicalPage;
            RTHandle dynamicPhysicalPage =
                VirtualShadowMapPrototypeRuntime.DynamicPhysicalPage;
            Texture staticPhysicalPageTexture = staticPhysicalPage != null
                ? staticPhysicalPage.ResolveTexture()
                : null;
            Texture dynamicPhysicalPageTexture = dynamicPhysicalPage != null
                ? dynamicPhysicalPage.ResolveTexture()
                : null;
            bool pageAvailable = !IsPageStateMode(m_VisualizationMode) && m_PhysicalPageAvailable
                && staticPhysicalPageTexture != null
                && dynamicPhysicalPageTexture != null;

            mpb.SetTexture(
                VSMPrototypeStaticPhysicalPageId,
                staticPhysicalPageTexture != null
                    ? staticPhysicalPageTexture
                    : Texture2D.blackTexture);
            mpb.SetTexture(
                VSMPrototypeDynamicPhysicalPageId,
                dynamicPhysicalPageTexture != null
                    ? dynamicPhysicalPageTexture
                    : Texture2D.blackTexture);
            mpb.SetInt(VSMPrototypeAvailableId, pageAvailable ? 1 : 0);
            mpb.SetInt(
                VSMDebugVisualizationModeId,
                (int)NormalizeVisualizationMode(m_VisualizationMode));
            mpb.SetFloat(VSMDebugExposureId, Mathf.Clamp(m_Exposure, -16f, 16f));
            mpb.SetInt(VSMDebugPoolModeId, (int)NormalizePoolMode(m_PoolMode));

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_StaticPhysicalPageHandle = default;
            m_DynamicPhysicalPageHandle = default;
            m_PhysicalPageAvailable = false;
            m_ShouldSkipExecution = false;
            m_PageStateResourcesAvailable = false;
            m_CameraEntityId = 0ul;
            m_FrameIndex = -1;
        }

        internal static bool IsPageStateMode(VSMDebugVisualizationMode mode)
        {
            return mode >= VSMDebugVisualizationMode.PageStates && mode <= VSMDebugVisualizationMode.Overflow;
        }

        internal static void BindPageStateResources(MaterialPropertyBlock properties)
        {
            properties.SetBuffer(PageTableId, VirtualShadowMapPrototypeRuntime.PageTable);
            properties.SetBuffer(PageMetadataId, VirtualShadowMapPrototypeRuntime.PageMetadata);
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
