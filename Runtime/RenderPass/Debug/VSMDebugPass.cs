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
        Occupancy = 1,
        [InspectorName("Depth Heat Map")]
        DepthHeatMap = 2,
    }

    public sealed class VSMDebugPass : RasterPass
    {
        internal const string VSMDebugShaderName = "Hidden/VividRP/VSMDebug";

        private static readonly int VSMPrototypePhysicalPageId =
            Shader.PropertyToID("_VSMPrototypePhysicalPage");
        private static readonly int VSMPrototypeAvailableId =
            Shader.PropertyToID("_VSMPrototypeAvailable");
        private static readonly int VSMDebugVisualizationModeId =
            Shader.PropertyToID("_VSMDebugVisualizationMode");
        private static readonly int VSMDebugExposureId =
            Shader.PropertyToID("_VSMDebugExposure");

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField]
        private VSMDebugVisualizationMode m_VisualizationMode =
            VSMDebugVisualizationMode.DeviceDepth;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        private Material m_Material;
        private TextureHandle m_PhysicalPageHandle;
        private bool m_PhysicalPageAvailable;
        private bool m_ShouldSkipExecution;

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
            m_PhysicalPageHandle = default;
            m_PhysicalPageAvailable = false;

            var settings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();
            bool prototypeEnabled = settings != null
                && settings.enableVirtualShadowMapPrototype.value;
            if (prototypeEnabled
                && VirtualShadowMapPrototypeRuntime.IsSupportedOnCurrentPlatform()
                && VirtualShadowMapPrototypeRuntime.EnsurePhysicalPageForBinding())
            {
                m_PhysicalPageHandle = PassRecorder.ImportTextureForPass(
                    this,
                    VirtualShadowMapPrototypeRuntime.PhysicalPage,
                    AccessFlags.Read);
            }

            m_PhysicalPageAvailable = m_PhysicalPageHandle.IsValid();

            var cameraData = frameData?.GetOrCreate<VividCameraData>();
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

            RTHandle physicalPage = VirtualShadowMapPrototypeRuntime.PhysicalPage;
            Texture physicalPageTexture = physicalPage != null
                ? physicalPage.ResolveTexture()
                : null;
            bool pageAvailable = m_PhysicalPageAvailable && physicalPageTexture != null;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(
                VSMPrototypePhysicalPageId,
                physicalPageTexture != null ? physicalPageTexture : Texture2D.blackTexture);
            mpb.SetInt(VSMPrototypeAvailableId, pageAvailable ? 1 : 0);
            mpb.SetInt(
                VSMDebugVisualizationModeId,
                (int)NormalizeVisualizationMode(m_VisualizationMode));
            mpb.SetFloat(VSMDebugExposureId, Mathf.Clamp(m_Exposure, -16f, 16f));

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_PhysicalPageHandle = default;
            m_PhysicalPageAvailable = false;
            m_ShouldSkipExecution = false;
        }

        internal static VSMDebugVisualizationMode NormalizeVisualizationMode(
            VSMDebugVisualizationMode mode)
        {
            return mode switch
            {
                VSMDebugVisualizationMode.DeviceDepth => VSMDebugVisualizationMode.DeviceDepth,
                VSMDebugVisualizationMode.Occupancy => VSMDebugVisualizationMode.Occupancy,
                VSMDebugVisualizationMode.DepthHeatMap => VSMDebugVisualizationMode.DepthHeatMap,
                _ => VSMDebugVisualizationMode.DeviceDepth,
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
