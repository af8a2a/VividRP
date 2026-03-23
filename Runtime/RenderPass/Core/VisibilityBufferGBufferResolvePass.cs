using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VisibilityBufferGBufferResolvePass : RasterPass
    {
        internal const string VisibilityBufferGBufferResolveShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferGBufferResolve";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int DepthTextureScaleBiasId = Shader.PropertyToID("_DepthTextureScaleBias");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.Write,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.Write,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.Write,
            AttachmentIndex = 3,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_GBuffer3;

        private Material m_Material;

        public VisibilityBufferGBufferResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferGBufferResolvePass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;

            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
        }

        public override void Create()
        {
            var shader = Shader.Find(VisibilityBufferGBufferResolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find shader '{VisibilityBufferGBufferResolveShaderName}' for {nameof(VisibilityBufferGBufferResolvePass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
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

            ConfigureTarget(m_GBuffer0, width, height, GraphicsFormat.R8G8B8A8_UNorm, false);
            ConfigureTarget(m_GBuffer1, width, height, GraphicsFormat.R16G16_SFloat, false);
            ConfigureTarget(m_GBuffer2, width, height, GraphicsFormat.R8G8B8A8_UNorm, false);
            ConfigureTarget(m_GBuffer3, width, height, GraphicsFormat.B10G11R11_UFloatPack32, true);
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Material == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || !m_GBuffer0.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_GBuffer2.innerHandle.IsValid()
                || !m_GBuffer3.innerHandle.IsValid())
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
            mpb.SetVector(VisibilityBufferScaleBiasId, GetScaleBias(m_VisibilityBuffer.innerHandle));
            mpb.SetVector(DepthTextureScaleBiasId, GetScaleBias(m_DepthTexture.innerHandle));

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

        private static void ConfigureTarget(
            RenderGraphTexture texture,
            int width,
            int height,
            GraphicsFormat format,
            bool enableRandomWrite)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = false;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.BindTextureMS = false;
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

            return ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
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

        private static int ResolveCameraDimension(int actualCameraDimension, int cameraDimension, int screenDimension)
        {
            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static Vector4 GetScaleBias(RTHandle handle)
        {
            if (handle == null || !handle.useScaling)
                return new Vector4(1f, 1f, 0f, 0f);

            var scale = handle.rtHandleProperties.rtHandleScale;
            return new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
