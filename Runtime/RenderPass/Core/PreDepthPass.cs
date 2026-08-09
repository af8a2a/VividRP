using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class PreDepthPass : RasterPass
    {
        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthAttachment;

        public PreDepthPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque("VividPreDepth")
            };

            m_DepthAttachment = RenderGraphTexture.CreateDepthTarget("PreDepth");
        }

        public override void Create()
        {
        }

        public override void Resize(int width, int height)
        {
            m_DepthAttachment.Resize(width, height);
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterPassContext context)
        {
            if (m_RenderList == null || !m_RenderList.IsValid)
                return;

            context.cmd.DrawRendererList(m_RenderList);
        }

        public override void Dispose()
        {
        }
    }
}
