using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum MaterialDebugVisualizationMode
    {
        None = 0,
        BaseColor = 1,
        NormalWS = 2,
        LinearRoughness = 3,
        PerceptualRoughness = 4,
        Smoothness = 5,
        Metallic = 6,
        AmbientOcclusion = 7,
        CustomData = 8,
        CustomData1 = 9,
        MaterialId = 10,
        Emissive = 11,
        BakedGI = 12,
        HasBakedGI = 13,
        Depth = 14,
    }

    public sealed class MaterialDebugPass : RasterPass
    {
        internal const string MaterialDebugShaderName = "Hidden/VividRP/MaterialDebug";

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
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc,
                m_GBuffer0?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc,
                m_GBuffer0?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
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

            var sourceTexture = TextureResolveUtility.ResolveTexture(m_SourceTexture.innerHandle);
            var depthTexture = TextureResolveUtility.ResolveTexture(m_DepthTexture.innerHandle);
            var gBuffer0 = TextureResolveUtility.ResolveTexture(m_GBuffer0.innerHandle);
            var gBuffer1 = TextureResolveUtility.ResolveTexture(m_GBuffer1.innerHandle);
            var gBuffer2 = TextureResolveUtility.ResolveTexture(m_GBuffer2.innerHandle);
            var gBuffer3 = TextureResolveUtility.ResolveTexture(m_GBuffer3.innerHandle);
            var gBuffer4 = TextureResolveUtility.ResolveTexture(m_GBuffer4.innerHandle);

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

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(CameraDepthTextureId, depthTexture);
            mpb.SetTexture(GBuffer0Id, gBuffer0);
            mpb.SetTexture(GBuffer1Id, gBuffer1);
            mpb.SetTexture(GBuffer2Id, gBuffer2);
            mpb.SetTexture(GBuffer3Id, gBuffer3);
            mpb.SetTexture(GBuffer4Id, gBuffer4);
            mpb.SetVector(SourceTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_SourceTexture.innerHandle));
            mpb.SetVector(CameraDepthTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_DepthTexture.innerHandle));
            mpb.SetVector(GBuffer0ScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_GBuffer0.innerHandle));
            mpb.SetVector(GBuffer1ScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_GBuffer1.innerHandle));
            mpb.SetVector(GBuffer2ScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_GBuffer2.innerHandle));
            mpb.SetVector(GBuffer3ScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_GBuffer3.innerHandle));
            mpb.SetVector(GBuffer4ScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_GBuffer4.innerHandle));
            mpb.SetInt(MaterialDebugModeId, (int)m_ResolvedSettings.visualizationMode);
            mpb.SetFloat(MaterialDebugExposureId, m_ResolvedSettings.exposure);

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
            m_OutputTexture.desc.ColorFormat = RenderGraphTextureDescUtility.ResolveColorFormat(sourceDescriptor);
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

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if (RenderGraphTextureDescUtility.HasExplicitSize(m_SourceTexture?.desc))
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }
    }
}
