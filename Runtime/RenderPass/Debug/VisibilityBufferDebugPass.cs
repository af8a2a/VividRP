using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum VisibilityBufferDebugVisualizationMode
    {
        Instance = 0,
        Cluster = 1,
        ClusterLOD = 2,
        Triangle = 3,
        Wireframe = 4,
        BarycentricCoordinates = 5,
    }

    public enum VisibilityBufferAttributeComparisonMode
    {
        Disabled = 0,
        Attributes0Error = 1,
    }

    public sealed class VisibilityBufferDebugPass : RasterPass
    {
        internal const string VisibilityBufferDebugShaderName = "Hidden/VividRP/VisibilityBufferDebug";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int VisibilityBufferAttributes0Id = Shader.PropertyToID("_VisibilityBufferAttributes0");
        private static readonly int VisibilityBufferAttributes0ScaleBiasId = Shader.PropertyToID("_VisibilityBufferAttributes0ScaleBias");
        private static readonly int AttributeComparisonModeId = Shader.PropertyToID("_AttributeComparisonMode");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VisualizationMode");
        private static readonly int DebugExposureId = Shader.PropertyToID("_DebugExposure");
        private static readonly int WireframeThicknessId = Shader.PropertyToID("_WireframeThickness");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "VisibilityBufferAttributes0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField]
        private VisibilityBufferDebugVisualizationMode m_VisualizationMode = VisibilityBufferDebugVisualizationMode.Cluster;

        [SerializeField]
        private VisibilityBufferAttributeComparisonMode m_AttributeComparisonMode;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        private Material m_Material;
        private VisibilityBufferDebugVisualizationMode m_ResolvedVisualizationMode = VisibilityBufferDebugVisualizationMode.Cluster;
        private float m_ResolvedExposure;
        private float m_ResolvedWireframeThickness =
            VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness;
        private bool m_ShouldSkipExecution;

        internal readonly struct VisibilityBufferDebugSettingsData
        {
            public readonly VisibilityBufferDebugVisualizationMode visualizationMode;
            public readonly float exposure;
            public readonly float wireframeThickness;

            public VisibilityBufferDebugSettingsData(
                VisibilityBufferDebugVisualizationMode visualizationMode,
                float exposure,
                float wireframeThickness)
            {
                this.visualizationMode = visualizationMode;
                this.exposure = exposure;
                this.wireframeThickness = wireframeThickness;
            }
        }

        public VisibilityBufferDebugVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = value;
        }

        public float Exposure
        {
            get => m_Exposure;
            set => m_Exposure = Mathf.Clamp(value, -16f, 16f);
        }

        public VisibilityBufferAttributeComparisonMode AttributeComparisonMode
        {
            get => m_AttributeComparisonMode;
            set => m_AttributeComparisonMode = value;
        }

        public VisibilityBufferDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferDebugPass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_VisibilityBuffer.desc.WrapMode = TextureWrapMode.Clamp;

            m_Attributes0 = RenderGraphTexture.CreateInput(
                "VisibilityBufferAttributes0",
                GraphicsFormat.R32G32B32A32_SFloat);
            m_Attributes0.desc.FilterMode = FilterMode.Point;
            m_Attributes0.desc.WrapMode = TextureWrapMode.Clamp;

            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.FilterMode = FilterMode.Point;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.VisibilityBufferDebugShader;
            shader ??= Shader.Find(VisibilityBufferDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{VisibilityBufferDebugShaderName}' for {nameof(VisibilityBufferDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var resolvedSettings = ResolveSettings(
                VividRenderingDebugDisplaySettings.Data,
                m_VisualizationMode,
                m_Exposure);
            m_ResolvedVisualizationMode = resolvedSettings.visualizationMode;
            m_ResolvedExposure = resolvedSettings.exposure;
            m_ResolvedWireframeThickness = resolvedSettings.wireframeThickness;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_VisibilityBuffer?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_VisibilityBuffer?.desc);

            ConfigureOutputTexture(width, height);
        }

        public override void Record(RasterPassContext context)
        {
            if (m_ShouldSkipExecution)
                return;

            bool compareAttributes0 = m_AttributeComparisonMode
                == VisibilityBufferAttributeComparisonMode.Attributes0Error;
            if (m_Material == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || (compareAttributes0 && !m_Attributes0.innerHandle.IsValid())
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var visibilityBuffer = m_VisibilityBuffer.innerHandle.ResolveTexture();
            if (visibilityBuffer == null)
                return;
            var attributes0 = compareAttributes0
                ? m_Attributes0.innerHandle.ResolveTexture()
                : null;
            if (compareAttributes0 && attributes0 == null)
                return;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(VisibilityBufferId, visibilityBuffer);
            mpb.SetVector(VisibilityBufferScaleBiasId, m_VisibilityBuffer.innerHandle.GetScaleBias());
            if (attributes0 != null)
            {
                mpb.SetTexture(VisibilityBufferAttributes0Id, attributes0);
                mpb.SetVector(
                    VisibilityBufferAttributes0ScaleBiasId,
                    m_Attributes0.innerHandle.GetScaleBias());
            }
            mpb.SetInt(AttributeComparisonModeId, (int)m_AttributeComparisonMode);
            mpb.SetInt(VisualizationModeId, (int)m_ResolvedVisualizationMode);
            mpb.SetFloat(DebugExposureId, m_ResolvedExposure);
            mpb.SetFloat(WireframeThicknessId, m_ResolvedWireframeThickness);

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

        internal static VisibilityBufferDebugSettingsData ResolveSettings(
            VividRenderingDebugSettingsData data,
            VisibilityBufferDebugVisualizationMode defaultVisualizationMode,
            float defaultExposure)
        {
            if (data == null)
            {
                return new VisibilityBufferDebugSettingsData(
                    defaultVisualizationMode,
                    Mathf.Clamp(defaultExposure, -16f, 16f),
                    VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness);
            }

            return new VisibilityBufferDebugSettingsData(
                data.visibilityBufferDebugMode,
                Mathf.Clamp(data.visibilityBufferDebugExposure, -16f, 16f),
                data.visibilityBufferWireframeThickness);
        }

        private void ConfigureOutputTexture(int width, int height)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
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
            m_OutputTexture.desc.Name = "OutputTexture";
        }
    }
}
