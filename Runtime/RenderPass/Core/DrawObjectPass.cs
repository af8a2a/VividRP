using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DrawObjectPass : RasterPass
    {
        private readonly RenderGraphTexture m_DefaultColorTarget;
        private readonly RenderGraphTexture m_DefaultDepthTarget;

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Write, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTarget;

        public DrawObjectPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque()
            };

            m_ColorTarget = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, GraphicsFormat.R8G8B8A8_SRGB)
            };

            m_DepthTarget = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
            };

            m_DefaultColorTarget = m_ColorTarget;
            m_DefaultDepthTarget = m_DepthTarget;
        }

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            if (ReferenceEquals(m_ColorTarget, m_DefaultColorTarget) && m_ColorTarget?.desc != null)
            {
                m_ColorTarget.desc.Width = width;
                m_ColorTarget.desc.Height = height;
            }

            if (ReferenceEquals(m_DepthTarget, m_DefaultDepthTarget) && m_DepthTarget?.desc != null)
            {
                m_DepthTarget.desc.Width = width;
                m_DepthTarget.desc.Height = height;
            }
        }

        public override void Record(RasterGraphContext context)
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
