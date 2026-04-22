using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum VisibilityBufferResolveDebugMode
    {
        InstanceID = 0,
        MeshletID = 1,
        TriangleID = 2,
        Wireframe = 3,
        BarycentricCoordinates = 4,
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

        [SerializeField]
        private VisibilityBufferResolveDebugMode m_DebugMode = VisibilityBufferResolveDebugMode.MeshletID;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        [SerializeField, Min(0.1f)]
        private float m_WireframeThickness = 10f;

        private Material m_Material;
        private float m_ResolvedExposure;
        private float m_ResolvedWireframeThickness = 10f;
        private VisibilityBufferResolveDebugMode m_ResolvedDebugMode = VisibilityBufferResolveDebugMode.MeshletID;

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
            m_ResolvedDebugMode = m_DebugMode;
            m_ResolvedExposure = Mathf.Clamp(m_Exposure, -16f, 16f);
            m_ResolvedWireframeThickness = Mathf.Max(0.1f, m_WireframeThickness);

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_VisibilityBuffer?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
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

            var visibilityTexture = ResolveTexture(m_VisibilityBuffer.innerHandle);
            if (visibilityTexture == null)
                return;

            var depthTexture = ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;

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

        private static int ResolveOutputDimension(
            System.Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            var resolved = 0;

            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (!HasExplicitSize(descriptor))
                    continue;

                resolved = Mathf.Max(resolved, selector(descriptor));
            }

            if (resolved > 0)
                return resolved;

            return CameraDimensionUtility.ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

    }
}
