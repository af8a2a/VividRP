using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum ReGIRDebugVisualizationMode
    {
        None = 0,
        Cells = 1,
        ReservoirOccupancy = 2,
        ReservoirWeight = 3,
    }

    public sealed class ReGIRDebugPass : RasterPass
    {
        internal const string ReGIRDebugShaderName = "Hidden/VividRP/ReGIRDebug";

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int CameraDepthTextureScaleBiasId = Shader.PropertyToID("_CameraDepthTextureScaleBias");
        private static readonly int ReGIRParametersId = Shader.PropertyToID("_ReGIRParameters");
        private static readonly int ReGIRReservoirsId = Shader.PropertyToID("_ReGIRReservoirs");
        private static readonly int ReGIRDebugModeId = Shader.PropertyToID("_ReGIRDebugMode");
        private static readonly int ReGIRDebugOpacityId = Shader.PropertyToID("_ReGIRDebugOpacity");
        private static readonly int ReGIRDebugViewportSizeId = Shader.PropertyToID("_ReGIRDebugViewportSize");

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "DepthTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [RenderGraphResource(Name = "ReGIRParameters", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRParameterBuffer;

        [RenderGraphResource(Name = "ReGIRReservoirs", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_ReGIRReservoirBuffer;

        private Material m_Material;
        private ReGIRDebugSettingsData m_ResolvedSettings;
        private Vector4 m_ReGIRDebugViewportSize = new(1f, 1f, 1f, 1f);
        private bool m_ShouldSkipExecution;

        internal readonly struct ReGIRDebugSettingsData
        {
            public readonly ReGIRDebugVisualizationMode visualizationMode;
            public readonly float opacity;
            public readonly bool enabled;

            public ReGIRDebugSettingsData(ReGIRDebugVisualizationMode visualizationMode, float opacity)
            {
                this.visualizationMode = NormalizeVisualizationMode(visualizationMode);
                this.opacity = Mathf.Clamp01(opacity);
                enabled = this.visualizationMode != ReGIRDebugVisualizationMode.None && this.opacity > 0f;
            }
        }

        public ReGIRDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ReGIRDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_DepthTexture = RenderGraphTexture.CreateInput("DepthTexture", GraphicsFormat.R32_SFloat);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
            m_ReGIRParameterBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRParameters",
                1,
                VividReGIRParameters.Stride);
            m_ReGIRReservoirBuffer = RenderGraphBuffer.CreateStructured(
                "ReGIRReservoirs",
                1,
                VividReGIRReservoir.Stride);
            m_ResolvedSettings = ResolveSettings(null);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.ReGIRDebugShader;
            shader ??= Shader.Find(ReGIRDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ReGIRDebugShaderName}' for {nameof(ReGIRDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(VividRenderingDebugDisplaySettings.Data);

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
            m_ReGIRDebugViewportSize = new Vector4(
                width,
                height,
                1f / Mathf.Max(1, width),
                1f / Mathf.Max(1, height));
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution || !m_ResolvedSettings.enabled)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            if (m_Material == null
                || !m_SourceTexture.innerHandle.IsValid()
                || !m_DepthTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            var sourceTexture = m_SourceTexture.innerHandle.ResolveTexture();
            var depthTexture = m_DepthTexture.innerHandle.ResolveTexture();
            var parameterBuffer = m_ReGIRParameterBuffer?.ImportedGraphicsBuffer;
            var reservoirBuffer = m_ReGIRReservoirBuffer?.ImportedGraphicsBuffer;

            if (sourceTexture == null
                || depthTexture == null
                || parameterBuffer == null
                || reservoirBuffer == null)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            m_Material.SetBuffer(ReGIRParametersId, parameterBuffer);
            m_Material.SetBuffer(ReGIRReservoirsId, reservoirBuffer);

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(CameraDepthTextureId, depthTexture);
            mpb.SetVector(SourceTextureScaleBiasId, m_SourceTexture.innerHandle.GetScaleBias());
            mpb.SetVector(CameraDepthTextureScaleBiasId, m_DepthTexture.innerHandle.GetScaleBias());
            mpb.SetVector(ReGIRDebugViewportSizeId, m_ReGIRDebugViewportSize);
            mpb.SetInt(ReGIRDebugModeId, (int)m_ResolvedSettings.visualizationMode);
            mpb.SetFloat(ReGIRDebugOpacityId, m_ResolvedSettings.opacity);

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
            m_ReGIRDebugViewportSize = new Vector4(1f, 1f, 1f, 1f);
        }

        internal static ReGIRDebugSettingsData ResolveSettings(VividRenderingDebugSettingsData data)
        {
            if (data == null)
            {
                return new ReGIRDebugSettingsData(
                    VividRenderingDebugSettingsData.DefaultReGIRDebugMode,
                    VividRenderingDebugSettingsData.DefaultReGIRDebugOpacity);
            }

            return new ReGIRDebugSettingsData(
                data.reGIRDebugMode,
                data.reGIRDebugOpacity);
        }

        internal static ReGIRDebugVisualizationMode NormalizeVisualizationMode(ReGIRDebugVisualizationMode value)
        {
            return value switch
            {
                ReGIRDebugVisualizationMode.None => ReGIRDebugVisualizationMode.None,
                ReGIRDebugVisualizationMode.Cells => ReGIRDebugVisualizationMode.Cells,
                ReGIRDebugVisualizationMode.ReservoirOccupancy => ReGIRDebugVisualizationMode.ReservoirOccupancy,
                ReGIRDebugVisualizationMode.ReservoirWeight => ReGIRDebugVisualizationMode.ReservoirWeight,
                _ => ReGIRDebugVisualizationMode.None,
            };
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

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if ((m_SourceTexture?.desc).HasExplicitSize())
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }
    }
}
