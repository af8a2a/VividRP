using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum VividReflectionProbeAtlasResolution
    {
        [InspectorName("512x512")]
        Resolution512x512 = 512,
        [InspectorName("1024x512")]
        Resolution1024x512 = 1024 << 16 | 512,
        [InspectorName("1024x1024")]
        Resolution1024x1024 = 1024,
        [InspectorName("2048x1024")]
        Resolution2048x1024 = 2048 << 16 | 1024,
        [InspectorName("2048x2048")]
        Resolution2048x2048 = 2048,
        [InspectorName("4096x2048")]
        Resolution4096x2048 = 4096 << 16 | 2048,
        [InspectorName("4096x4096")]
        Resolution4096x4096 = 4096,
        [InspectorName("8192x4096")]
        Resolution8192x4096 = 8192 << 16 | 4096,
        [InspectorName("8192x8192")]
        Resolution8192x8192 = 8192,
        [InspectorName("16384x8192")]
        Resolution16384x8192 = 16384 << 16 | 8192,
        [InspectorName("16384x16384")]
        Resolution16384x16384 = 16384,
    }

    public enum VividReflectionProbeAtlasFormat
    {
        [InspectorName("R11G11B10")]
        R11G11B10 = (int)GraphicsFormat.B10G11R11_UFloatPack32,
        [InspectorName("R16G16B16A16")]
        R16G16B16A16 = (int)GraphicsFormat.R16G16B16A16_SFloat,
    }

    public enum ColorGradingSpace
    {
        AcesCg,
        [InspectorName("sRGB")]
        sRGB
    }

    public enum AutoExposureImplementationPath
    {
        Unreal,
        HDRP,
    }

    [CreateAssetMenu(menuName = "VividRP/Vivid Render Pipeline")]
    public class VividRenderPipelineAsset : RenderPipelineAsset<VividRenderPipeline>, IProbeVolumeEnabledRenderPipeline, ISTPEnabledRenderPipeline
    {
        private const string DefaultShaderName = "VividRP/Material/StandardLit";
        private const string DefaultMaterialResourcePath = "DefaultMaterial";

        public RenderGraphData RenderGraphAsset;

        [SerializeField]
        private bool m_EnableAsyncCompute = true;

        [SerializeField]
        private bool m_EnableGPUDriven;

        [SerializeField]
        private bool m_EnableGPUDrivenOcclusionCulling = true;

        [SerializeField]
        private GPUDriven.GPUDrivenTextureBackendMode m_GPUDrivenTextureBackend =
            GPUDriven.GPUDrivenTextureBackendMode.VirtualTexture;

        [SerializeField]
        private GPUDriven.VirtualTexture.GPUDrivenVirtualTexturePhysicalPoolQuality
            m_GPUDrivenVirtualTexturePhysicalPoolQuality =
                GPUDriven.VirtualTexture.GPUDrivenVirtualTexturePhysicalPoolQuality.Medium;

        [SerializeField]
        private VividVirtualTextureIOBackendMode m_VirtualTextureIOBackend = VividVirtualTextureIOBackendMode.Auto;

        [SerializeField, Min(0)]
        private int m_VirtualTextureMaxResidencyAllocationsPerFrame = 64;

        [SerializeField, Min(0)]
        private int m_VirtualTextureMaxPrefetchAllocationsPerFrame;

        [SerializeField, Min(0)]
        private int m_VirtualTextureMaxPageUploadsPerFrame = 64;

        [SerializeField, Min(0)]
        private int m_VirtualTextureMaxUploadBytesPerFrameMiB;

        [SerializeField, Min(1)]
        private int m_VirtualTextureMaxInFlightChunks = 64;

        [SerializeField, Min(0)]
        private int m_VirtualTextureDecodeConcurrency;

        [SerializeField, Min(0)]
        private int m_VirtualTextureDecodedCacheBudgetMiB = 32;

        [SerializeField]
        private bool m_EnableGPUDrivenDecal;

        [SerializeField]
        private bool m_EnableSRPBatcher = true;

        [SerializeField]
        private bool m_SupportProbeVolume;

        [SerializeField]
        private ProbeVolumeSHBands m_ProbeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL2;

        [SerializeField]
        private VividReflectionProbeAtlasResolution m_ReflectionProbeAtlasResolution =
            VividReflectionProbeAtlasResolution.Resolution4096x4096;

        [SerializeField]
        private VividReflectionProbeAtlasFormat m_ReflectionProbeAtlasFormat =
            VividReflectionProbeAtlasFormat.R16G16B16A16;

        [SerializeField]
        private int m_ReflectionProbeAtlasLastValidCubeMip = 3;

        [SerializeField]
        private bool m_ReflectionProbeAtlasDecreaseResToFit = true;

        [SerializeField]
        private ColorGradingSpace m_ColorGradingSpace = ColorGradingSpace.sRGB;

        [SerializeField]
        private AutoExposureImplementationPath m_AutoExposureImplementation = AutoExposureImplementationPath.Unreal;

        public bool EnableAsyncCompute
        {
            get => m_EnableAsyncCompute;
            set => m_EnableAsyncCompute = value;
        }

        public bool EnableSRPBatcher
        {
            get => m_EnableSRPBatcher;
            set => m_EnableSRPBatcher = value;
        }

        public bool SupportProbeVolume
        {
            get => m_SupportProbeVolume;
            set => m_SupportProbeVolume = value;
        }

        public ProbeVolumeSHBands ProbeVolumeSHBands
        {
            get => m_ProbeVolumeSHBands;
            set => m_ProbeVolumeSHBands = value;
        }

        public VividReflectionProbeAtlasResolution ReflectionProbeAtlasResolution
        {
            get => m_ReflectionProbeAtlasResolution;
            set => m_ReflectionProbeAtlasResolution = value;
        }

        public VividReflectionProbeAtlasFormat ReflectionProbeAtlasFormat
        {
            get => m_ReflectionProbeAtlasFormat;
            set => m_ReflectionProbeAtlasFormat = value;
        }

        public int ReflectionProbeAtlasLastValidCubeMip
        {
            get => Mathf.Clamp(m_ReflectionProbeAtlasLastValidCubeMip, 0, VividReflectionProbeTextureCache.ConvolutionMipCount - 1);
            set => m_ReflectionProbeAtlasLastValidCubeMip = Mathf.Clamp(value, 0, VividReflectionProbeTextureCache.ConvolutionMipCount - 1);
        }

        public bool ReflectionProbeAtlasDecreaseResToFit
        {
            get => m_ReflectionProbeAtlasDecreaseResToFit;
            set => m_ReflectionProbeAtlasDecreaseResToFit = value;
        }

        public GraphicsFormat ReflectionProbeAtlasGraphicsFormat => (GraphicsFormat)(int)m_ReflectionProbeAtlasFormat;

        public Vector2Int ReflectionProbeAtlasDimensions =>
            VividReflectionProbeAtlasSettings.ResolveDimensions(m_ReflectionProbeAtlasResolution);

        public ColorGradingSpace ColorGradingSpace
        {
            get => m_ColorGradingSpace;
            set => m_ColorGradingSpace = value;
        }

        public bool EnableGPUDriven
        {
            get => m_EnableGPUDriven;
            set => m_EnableGPUDriven = value;
        }

        public bool EnableGPUDrivenOcclusionCulling
        {
            get => m_EnableGPUDrivenOcclusionCulling;
            set => m_EnableGPUDrivenOcclusionCulling = value;
        }

        public GPUDriven.GPUDrivenTextureBackendMode GPUDrivenTextureBackend
        {
            get => m_GPUDrivenTextureBackend;
            set => m_GPUDrivenTextureBackend = value;
        }

        public GPUDriven.VirtualTexture.GPUDrivenVirtualTexturePhysicalPoolQuality
            GPUDrivenVirtualTexturePhysicalPoolQuality
        {
            get => m_GPUDrivenVirtualTexturePhysicalPoolQuality;
            set => m_GPUDrivenVirtualTexturePhysicalPoolQuality = value;
        }

        public VividVirtualTextureIOBackendMode VirtualTextureIOBackend
        {
            get => m_VirtualTextureIOBackend;
            set => m_VirtualTextureIOBackend = value;
        }

        public int VirtualTextureMaxResidencyAllocationsPerFrame
        {
            get => Mathf.Max(0, m_VirtualTextureMaxResidencyAllocationsPerFrame);
            set => m_VirtualTextureMaxResidencyAllocationsPerFrame = Mathf.Max(0, value);
        }

        public int VirtualTextureMaxPrefetchAllocationsPerFrame
        {
            get => Mathf.Max(0, m_VirtualTextureMaxPrefetchAllocationsPerFrame);
            set => m_VirtualTextureMaxPrefetchAllocationsPerFrame = Mathf.Max(0, value);
        }

        public int VirtualTextureMaxPageUploadsPerFrame
        {
            get => Mathf.Max(0, m_VirtualTextureMaxPageUploadsPerFrame);
            set => m_VirtualTextureMaxPageUploadsPerFrame = Mathf.Max(0, value);
        }

        public int VirtualTextureMaxUploadBytesPerFrameMiB
        {
            get => Mathf.Max(0, m_VirtualTextureMaxUploadBytesPerFrameMiB);
            set => m_VirtualTextureMaxUploadBytesPerFrameMiB = Mathf.Max(0, value);
        }

        public int VirtualTextureMaxInFlightChunks
        {
            get => Mathf.Max(1, m_VirtualTextureMaxInFlightChunks);
            set => m_VirtualTextureMaxInFlightChunks = Mathf.Max(1, value);
        }

        public int VirtualTextureDecodeConcurrency
        {
            get => m_VirtualTextureDecodeConcurrency > 0
                ? Mathf.Clamp(m_VirtualTextureDecodeConcurrency, 1, 64)
                : Mathf.Clamp(SystemInfo.processorCount / 4, 2, 8);
            set => m_VirtualTextureDecodeConcurrency = Mathf.Clamp(value, 0, 64);
        }

        public int VirtualTextureDecodedCacheBudgetMiB
        {
            get => Mathf.Max(0, m_VirtualTextureDecodedCacheBudgetMiB);
            set => m_VirtualTextureDecodedCacheBudgetMiB = Mathf.Max(0, value);
        }

        public bool EnableGPUDrivenDecal
        {
            get => m_EnableGPUDrivenDecal;
            set => m_EnableGPUDrivenDecal = value;
        }

        public AutoExposureImplementationPath AutoExposureImplementation
        {
            get => m_AutoExposureImplementation;
            set => m_AutoExposureImplementation = value;
        }

        public override Shader defaultShader => Shader.Find(DefaultShaderName);

        public override Material defaultMaterial => Resources.Load<Material>(DefaultMaterialResourcePath);

        bool IProbeVolumeEnabledRenderPipeline.supportProbeVolume => m_SupportProbeVolume;

        ProbeVolumeSHBands IProbeVolumeEnabledRenderPipeline.maxSHBands => m_ProbeVolumeSHBands;

        bool ISTPEnabledRenderPipeline.isStpUsed => true;

#pragma warning disable 618
        ProbeVolumeSceneData IProbeVolumeEnabledRenderPipeline.probeVolumeSceneData =>
            VividRenderPipelineGlobalSettings.instance?.GetOrCreateAPVSceneData();
#pragma warning restore 618

        internal static VividRenderPipelineAsset GetActiveAsset()
        {
            return GraphicsSettings.currentRenderPipeline as VividRenderPipelineAsset
                ?? QualitySettings.renderPipeline as VividRenderPipelineAsset
                ?? GraphicsSettings.defaultRenderPipeline as VividRenderPipelineAsset;
        }

        protected override UnityEngine.Rendering.RenderPipeline CreatePipeline()
        {
#if UNITY_EDITOR
            VividRenderPipelineGlobalSettings.Ensure();
#endif
            return new VividRenderPipeline(this);
        }
    }
}
