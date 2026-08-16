using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VirtualTextureDemoPass : UnsafePass
    {
        internal const string VirtualTextureShaderTagName = "VividVT";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTarget;

        [SerializeField]
        private VirtualTextureDebugMode m_DefaultDebugMode = VirtualTextureDebugMode.None;

        [SerializeField, Min(1)]
        private int m_FeedbackSampleRate = 4;

        public VirtualTextureDemoPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VirtualTextureDemoPass));
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque(VirtualTextureShaderTagName),
            };
            m_ColorTarget = RenderGraphTexture.CreateColorTarget("Color", GraphicsFormat.R8G8B8A8_UNorm);
            m_ColorTarget.desc.ClearBuffer = false;
            m_DepthTarget = RenderGraphTexture.CreateDepthTarget("Depth", DepthBits.Depth32);
            m_DepthTarget.desc.ClearBuffer = false;
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            // Compatibility-only node. VirtualTextureDemo now renders through the regular
            // MeshletRenderer -> VisibilityBuffer -> VisibilityBufferGBufferResolve path.
            _ = m_DefaultDebugMode;
            _ = m_FeedbackSampleRate;
        }

        public override void Record(UnsafePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }
}
