using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class GBufferPass : RasterPass
    {
        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

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

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_GBufferDepth;

        public GBufferPass()
        {
            m_RenderList = new RenderGraphRenderList
            {
                desc = RenderGraphRenderListDesc.CreateOpaque("VividGBuffer")
            };

            m_GBuffer0 = CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer1 = CreateColorTarget("GBuffer1", GraphicsFormat.R16G16_SFloat);
            m_GBuffer2 = CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBufferDepth = CreateDepthTarget("GBufferDepth", DepthBits.Depth32);
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

            ResizeTexture(m_GBuffer0, width, height);
            ResizeTexture(m_GBuffer1, width, height);
            ResizeTexture(m_GBuffer2, width, height);
            ResizeTexture(m_GBuffer3, width, height);
            ResizeTexture(m_GBufferDepth, width, height);
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

        private static RenderGraphTexture CreateColorTarget(string name, GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(1, 1, format)
            };
            texture.desc.Name = name;
            return texture;
        }

        private static RenderGraphTexture CreateDepthTarget(string name, DepthBits depthBits)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, depthBits)
            };
            texture.desc.Name = name;
            return texture;
        }

        private static void ResizeTexture(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
        }
    }
}
