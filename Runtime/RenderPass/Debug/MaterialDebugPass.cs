using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum MaterialDebugVisualizationMode
    {
        None = 0,
        Depth = 14,
        BakeDiffuseLightingWithAlbedoPlusEmissive = 15,
        BaseColor = 1,
        DiffuseColor = 16,
        NormalWS = 2,
        NormalViewSpace = 17,
        LinearRoughness = 3,
        PerceptualRoughness = 4,
        Smoothness = 5,
        Metallic = 6,
        AmbientOcclusion = 7,
        SpecularOcclusion = 18,
        Fresnel0 = 19,
        Fresnel90 = 20,
        CoatMask = 21,
        CoatRoughness = 22,
        MaterialFeatures = 23,
        CustomData = 8,
        CustomData1 = 9,
        MaterialId = 10,
        Emissive = 11,
        BakedGI = 12,
        HasBakedGI = 13,
    }

    public sealed class MaterialDebugPass : RasterPass
    {
        internal const string MaterialDebugShaderName = "Hidden/VividRP/MaterialDebug";
        private const int MaterialFeatureTileSize = 8;

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int GBuffer0Id = Shader.PropertyToID("_GBuffer0");
        private static readonly int GBuffer1Id = Shader.PropertyToID("_GBuffer1");
        private static readonly int GBuffer2Id = Shader.PropertyToID("_GBuffer2");
        private static readonly int GBuffer3Id = Shader.PropertyToID("_GBuffer3");
        private static readonly int GBuffer4Id = Shader.PropertyToID("_GBuffer4");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int CameraDepthTextureScaleBiasId = Shader.PropertyToID("_CameraDepthTextureScaleBias");
        private static readonly int GBuffer0ScaleBiasId = Shader.PropertyToID("_GBuffer0ScaleBias");
        private static readonly int GBuffer1ScaleBiasId = Shader.PropertyToID("_GBuffer1ScaleBias");
        private static readonly int GBuffer2ScaleBiasId = Shader.PropertyToID("_GBuffer2ScaleBias");
        private static readonly int GBuffer3ScaleBiasId = Shader.PropertyToID("_GBuffer3ScaleBias");
        private static readonly int GBuffer4ScaleBiasId = Shader.PropertyToID("_GBuffer4ScaleBias");
        private static readonly int MaterialDebugModeId = Shader.PropertyToID("_MaterialDebugMode");
        private static readonly int MaterialDebugExposureId = Shader.PropertyToID("_MaterialDebugExposure");
        private static readonly int MaterialTileFeatureFlagsId = Shader.PropertyToID("_MaterialTileFeatureFlags");
        private static readonly int MaterialTileCountId = Shader.PropertyToID("_MaterialTileCount");
        private static readonly int MaterialTileCountXId = Shader.PropertyToID("_MaterialTileCountX");
        private static readonly int MaterialFeatureDebugAvailableId = Shader.PropertyToID("_MaterialFeatureDebugAvailable");
        private static readonly int MaterialFeatureDebugScreenSizeId = Shader.PropertyToID("_MaterialFeatureDebugScreenSize");

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "GBuffer0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(Name = "GBuffer1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(Name = "GBuffer2", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(Name = "GBuffer3", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(Name = "GBuffer4", Access = AccessFlags.Read)]
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(Name = "MaterialTileFeatureFlags", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_MaterialTileFeatureFlags;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        [PassBypass(nameof(m_SourceTexture))]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField]
        private MaterialDebugVisualizationMode m_VisualizationMode = MaterialDebugVisualizationMode.None;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        private Material m_Material;
        private MaterialDebugSettingsData m_ResolvedSettings;
        private Vector4 m_MaterialFeatureDebugScreenSize = new(1f, 1f, 1f, 1f);
        private int m_MaterialTileCount = 1;
        private int m_MaterialTileCountX = 1;
        private bool m_ShouldSkipExecution;

        internal readonly struct MaterialDebugSettingsData
        {
            public readonly MaterialDebugVisualizationMode visualizationMode;
            public readonly float exposure;

            public MaterialDebugSettingsData(
                MaterialDebugVisualizationMode visualizationMode,
                float exposure)
            {
                this.visualizationMode = visualizationMode;
                this.exposure = exposure;
            }
        }

        public MaterialDebugVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = value;
        }

        public float Exposure
        {
            get => m_Exposure;
            set => m_Exposure = Mathf.Clamp(value, -16f, 16f);
        }

        public MaterialDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(MaterialDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_DepthTexture = RenderGraphTexture.CreateInput("DepthTexture", GraphicsFormat.R32_SFloat);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_GBuffer0 = RenderGraphTexture.CreateInput("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateInput("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateInput("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateInput("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer4 = RenderGraphTexture.CreateInput("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);
            m_MaterialTileFeatureFlags = RenderGraphBuffer.CreateStructured("MaterialTileFeatureFlags", 1, sizeof(uint));
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override bool IsActive(ContextContainer frameData)
        {
            return VividRenderingDebugDisplaySettings.Data.materialDebugMode != MaterialDebugVisualizationMode.None;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.MaterialDebugShader;
            shader ??= Shader.Find(MaterialDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{MaterialDebugShaderName}' for {nameof(MaterialDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(
                VividRenderingDebugDisplaySettings.Data,
                m_VisualizationMode,
                m_Exposure);

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc,
                m_GBuffer0?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc,
                m_GBuffer0?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
            ConfigureMaterialFeatureTileDebug(width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution
                || m_Material == null
                || !AreTextureHandlesValid())
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            var sourceTexture = m_SourceTexture.innerHandle.ResolveTexture();
            var depthTexture = m_DepthTexture.innerHandle.ResolveTexture();
            var gBuffer0 = m_GBuffer0.innerHandle.ResolveTexture();
            var gBuffer1 = m_GBuffer1.innerHandle.ResolveTexture();
            var gBuffer2 = m_GBuffer2.innerHandle.ResolveTexture();
            var gBuffer3 = m_GBuffer3.innerHandle.ResolveTexture();
            var gBuffer4 = m_GBuffer4.innerHandle.ResolveTexture();

            if (sourceTexture == null
                || depthTexture == null
                || gBuffer0 == null
                || gBuffer1 == null
                || gBuffer2 == null
                || gBuffer3 == null
                || gBuffer4 == null)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            m_ResolvedSettings = ResolveSettings(
                VividRenderingDebugDisplaySettings.Data,
                m_VisualizationMode,
                m_Exposure);
            var resolvedMode = (int)m_ResolvedSettings.visualizationMode;
            var resolvedExposure = m_ResolvedSettings.exposure;

            m_Material.SetInt(MaterialDebugModeId, resolvedMode);
            m_Material.SetFloat(MaterialDebugExposureId, resolvedExposure);

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(CameraDepthTextureId, depthTexture);
            mpb.SetTexture(GBuffer0Id, gBuffer0);
            mpb.SetTexture(GBuffer1Id, gBuffer1);
            mpb.SetTexture(GBuffer2Id, gBuffer2);
            mpb.SetTexture(GBuffer3Id, gBuffer3);
            mpb.SetTexture(GBuffer4Id, gBuffer4);
            mpb.SetVector(SourceTextureScaleBiasId, m_SourceTexture.innerHandle.GetScaleBias());
            mpb.SetVector(CameraDepthTextureScaleBiasId, m_DepthTexture.innerHandle.GetScaleBias());
            mpb.SetVector(GBuffer0ScaleBiasId, m_GBuffer0.innerHandle.GetScaleBias());
            mpb.SetVector(GBuffer1ScaleBiasId, m_GBuffer1.innerHandle.GetScaleBias());
            mpb.SetVector(GBuffer2ScaleBiasId, m_GBuffer2.innerHandle.GetScaleBias());
            mpb.SetVector(GBuffer3ScaleBiasId, m_GBuffer3.innerHandle.GetScaleBias());
            mpb.SetVector(GBuffer4ScaleBiasId, m_GBuffer4.innerHandle.GetScaleBias());
            mpb.SetInt(MaterialDebugModeId, resolvedMode);
            mpb.SetFloat(MaterialDebugExposureId, resolvedExposure);
            mpb.SetVector(MaterialFeatureDebugScreenSizeId, m_MaterialFeatureDebugScreenSize);
            mpb.SetInt(MaterialTileCountId, m_MaterialTileCount);
            mpb.SetInt(MaterialTileCountXId, m_MaterialTileCountX);
            mpb.SetInt(MaterialFeatureDebugAvailableId, 0);

            if (m_MaterialTileFeatureFlags?.innerHandle.IsValid() == true)
            {
                mpb.SetBuffer(MaterialTileFeatureFlagsId, m_MaterialTileFeatureFlags);
                mpb.SetInt(MaterialFeatureDebugAvailableId, 1);
            }

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_ShouldSkipExecution = false;
        }

        internal static MaterialDebugSettingsData ResolveSettings(
            VividRenderingDebugSettingsData data,
            MaterialDebugVisualizationMode defaultVisualizationMode,
            float defaultExposure)
        {
            if (data == null)
            {
                return new MaterialDebugSettingsData(
                    defaultVisualizationMode,
                    Mathf.Clamp(defaultExposure, -16f, 16f));
            }

            return new MaterialDebugSettingsData(
                data.materialDebugMode,
                Mathf.Clamp(data.materialDebugExposure, -16f, 16f));
        }

        private bool AreTextureHandlesValid()
        {
            return m_SourceTexture?.innerHandle.IsValid() == true
                && m_DepthTexture?.innerHandle.IsValid() == true
                && m_GBuffer0?.innerHandle.IsValid() == true
                && m_GBuffer1?.innerHandle.IsValid() == true
                && m_GBuffer2?.innerHandle.IsValid() == true
                && m_GBuffer3?.innerHandle.IsValid() == true
                && m_GBuffer4?.innerHandle.IsValid() == true
                && m_OutputTexture?.innerHandle.IsValid() == true;
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = sourceDescriptor.ResolveColorFormat();
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private void ConfigureMaterialFeatureTileDebug(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            m_MaterialTileCountX = Mathf.Max(1, (width + MaterialFeatureTileSize - 1) / MaterialFeatureTileSize);
            var materialTileCountY = Mathf.Max(1, (height + MaterialFeatureTileSize - 1) / MaterialFeatureTileSize);
            m_MaterialTileCount = Mathf.Max(1, m_MaterialTileCountX * materialTileCountY);
            m_MaterialFeatureDebugScreenSize = new Vector4(width, height, 1f / width, 1f / height);

            if (m_MaterialTileFeatureFlags?.desc == null)
                return;

            m_MaterialTileFeatureFlags.desc.Count = m_MaterialTileCount;
            m_MaterialTileFeatureFlags.desc.Stride = sizeof(uint);
            m_MaterialTileFeatureFlags.desc.Target = GraphicsBuffer.Target.Structured;
        }

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if ((m_SourceTexture?.desc).HasExplicitSize())
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }
    }
}
