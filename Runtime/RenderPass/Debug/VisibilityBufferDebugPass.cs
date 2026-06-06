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
    }

    public sealed class VisibilityBufferDebugPass : RasterPass
    {
        internal const string VisibilityBufferDebugShaderName = "Hidden/VividRP/VisibilityBufferDebug";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VisualizationMode");
        private static readonly int DebugExposureId = Shader.PropertyToID("_DebugExposure");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField]
        private VisibilityBufferDebugVisualizationMode m_VisualizationMode = VisibilityBufferDebugVisualizationMode.Cluster;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        private Material m_Material;
        private VisibilityBufferDebugVisualizationMode m_ResolvedVisualizationMode = VisibilityBufferDebugVisualizationMode.Cluster;
        private float m_ResolvedExposure;
        private bool m_ShouldSkipExecution;

        internal readonly struct VisibilityBufferDebugSettingsData
        {
            public readonly VisibilityBufferDebugVisualizationMode visualizationMode;
            public readonly float exposure;

            public VisibilityBufferDebugSettingsData(
                VisibilityBufferDebugVisualizationMode visualizationMode,
                float exposure)
            {
                this.visualizationMode = visualizationMode;
                this.exposure = exposure;
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

        public VisibilityBufferDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferDebugPass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_VisibilityBuffer.desc.WrapMode = TextureWrapMode.Clamp;

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

            if (m_Material == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var visibilityBuffer = TextureResolveUtility.ResolveTexture(m_VisibilityBuffer.innerHandle);
            if (visibilityBuffer == null)
                return;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(VisibilityBufferId, visibilityBuffer);
            mpb.SetVector(VisibilityBufferScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_VisibilityBuffer.innerHandle));
            mpb.SetInt(VisualizationModeId, (int)m_ResolvedVisualizationMode);
            mpb.SetFloat(DebugExposureId, m_ResolvedExposure);

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
                    Mathf.Clamp(defaultExposure, -16f, 16f));
            }

            return new VisibilityBufferDebugSettingsData(
                data.visibilityBufferDebugMode,
                Mathf.Clamp(data.visibilityBufferDebugExposure, -16f, 16f));
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
