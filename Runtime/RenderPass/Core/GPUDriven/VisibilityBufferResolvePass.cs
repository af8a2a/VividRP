using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    [Obsolete("Use VisibilityBufferDebugVisualizationMode from RenderingDebugger instead.")]
    public enum VisibilityBufferResolveDebugMode
    {
        InstanceID = 0,
        MeshletID = 1,
        TriangleID = 2,
        Wireframe = 3,
        BarycentricCoordinates = 4,
        [InspectorName("Cluster LOD")]
        ClusterLOD = 5,
    }

    public sealed class VisibilityBufferResolvePass : RasterPass
    {
        internal const string VisibilityBufferResolveShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferResolve";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int DepthTextureScaleBiasId = Shader.PropertyToID("_DepthTextureScaleBias");
        private static readonly int ResolveDebugModeId = Shader.PropertyToID("_ResolveDebugMode");
        private static readonly int ResolveExposureId = Shader.PropertyToID("_ResolveExposure");
        private static readonly int WireframeThicknessId = Shader.PropertyToID("_WireframeThickness");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private float m_ResolvedExposure;
        private float m_ResolvedWireframeThickness =
            VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness;
        private VisibilityBufferDebugVisualizationMode m_ResolvedDebugMode =
            VisibilityBufferDebugVisualizationMode.Cluster;

        internal readonly struct VisibilityBufferResolveDebugSettingsData
        {
            public readonly VisibilityBufferDebugVisualizationMode debugMode;
            public readonly float exposure;
            public readonly float wireframeThickness;

            public VisibilityBufferResolveDebugSettingsData(
                VisibilityBufferDebugVisualizationMode debugMode,
                float exposure,
                float wireframeThickness)
            {
                this.debugMode = debugMode;
                this.exposure = exposure;
                this.wireframeThickness = wireframeThickness;
            }
        }

        public VisibilityBufferResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferResolvePass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;

            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;

            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            var shader = Shader.Find(VisibilityBufferResolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{VisibilityBufferResolveShaderName}' for {nameof(VisibilityBufferResolvePass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var resolvedSettings = ResolveSettings(VividRenderingDebugDisplaySettings.Data);
            m_ResolvedDebugMode = resolvedSettings.debugMode;
            m_ResolvedExposure = resolvedSettings.exposure;
            m_ResolvedWireframeThickness = resolvedSettings.wireframeThickness;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
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
            if (m_Material == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var visibilityTexture = TextureResolveUtility.ResolveTexture(m_VisibilityBuffer.innerHandle);
            if (visibilityTexture == null)
                return;

            var depthTexture = TextureResolveUtility.ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(VisibilityBufferId, visibilityTexture);
            mpb.SetTexture(DepthTextureId, depthTexture);
            mpb.SetVector(VisibilityBufferScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_VisibilityBuffer.innerHandle));
            mpb.SetVector(DepthTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_DepthTexture.innerHandle));
            mpb.SetInt(ResolveDebugModeId, (int)m_ResolvedDebugMode);
            mpb.SetFloat(ResolveExposureId, m_ResolvedExposure);
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
            m_OutputTexture.desc.FilterMode = FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";
        }

        internal static VisibilityBufferResolveDebugSettingsData ResolveSettings(
            VividRenderingDebugSettingsData data)
        {
            if (data == null)
            {
                return new VisibilityBufferResolveDebugSettingsData(
                    VisibilityBufferDebugVisualizationMode.Cluster,
                    0f,
                    VividRenderingDebugSettingsData.DefaultVisibilityBufferWireframeThickness);
            }

            return new VisibilityBufferResolveDebugSettingsData(
                data.visibilityBufferDebugMode,
                Mathf.Clamp(data.visibilityBufferDebugExposure, -16f, 16f),
                data.visibilityBufferWireframeThickness);
        }
    }
}
