using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalVisibilityBufferPass : UnsafePass
    {
        internal const string ShaderTagName = "ExperimentalVisibilityBuffer";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(
            Name = "ExperimentalVisibilityBuffer",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(
            Name = "ExperimentalVisibilityAttributes0",
            Access = AccessFlags.Write,
            AttachmentIndex = 1,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(
            Name = "ExperimentalVisibilityAttributes1",
            Access = AccessFlags.Write,
            AttachmentIndex = 2,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_Attributes1;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read, IsDepthAttachment = true)]
        private RenderGraphTexture m_Depth;

        private readonly RenderTargetIdentifier[] m_ColorTargets = new RenderTargetIdentifier[3];

        public ExperimentalVisibilityBufferPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ExperimentalVisibilityBufferPass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(ShaderTagName),
            };
            m_VisibilityBuffer = CreateTarget(
                "ExperimentalVisibilityBuffer",
                GraphicsFormat.R32G32_UInt);
            m_Attributes0 = CreateTarget(
                "ExperimentalVisibilityAttributes0",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Attributes1 = CreateTarget(
                "ExperimentalVisibilityAttributes1",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Depth = RenderGraphTexture.CreateInput(
                "Depth",
                GraphicsFormat.None,
                DepthBits.Depth32);
        }

        public override void Create()
        {
        }

        public override void Resize(int width, int height)
        {
            ResizeTarget(m_VisibilityBuffer, width, height);
            ResizeTarget(m_Attributes0, width, height);
            ResizeTarget(m_Attributes1, width, height);
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_RenderList?.IsValid != true
                || m_VisibilityBuffer?.IsValid() != true
                || m_Attributes0?.IsValid() != true
                || m_Attributes1?.IsValid() != true
                || m_Depth?.IsValid() != true)
            {
                return;
            }

            CommandBuffer cmd = context.GetNativeCommandBuffer();
            m_ColorTargets[0] = m_VisibilityBuffer;
            m_ColorTargets[1] = m_Attributes0;
            m_ColorTargets[2] = m_Attributes1;
            cmd.SetRenderTarget(m_ColorTargets, m_Depth);
            cmd.ClearRenderTarget(clearDepth: false, clearColor: true, Color.clear);
            cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
        }

        private static RenderGraphTexture CreateTarget(string name, GraphicsFormat format)
        {
            RenderGraphTexture texture = RenderGraphTexture.CreateColorTarget(name, format);
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            texture.desc.MsaaSamples = MSAASamples.None;
            return texture;
        }

        private static void ResizeTarget(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;
            texture.Resize(Mathf.Max(1, width), Mathf.Max(1, height));
        }
    }
}
