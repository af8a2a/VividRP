using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class DrawObjectPass : RasterPass, IDynamicPassResourceLayout
    {
        public const int MaxColorAttachmentCount = 8;

        private readonly RenderGraphTexture m_DefaultColorTarget;
        private readonly RenderGraphTexture m_DefaultDepthTarget;
        private bool m_IsPassResourceLayoutDirty;

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 1)]
        private readonly List<RenderGraphTexture> m_AdditionalColorTargets = new();

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTarget;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

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

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        public void SetRenderListDescriptor(RenderGraphRenderListDesc renderListDesc)
        {
            m_RenderList ??= new RenderGraphRenderList();
            m_RenderList.desc = renderListDesc ?? new RenderGraphRenderListDesc();
        }

        public void SetColorTarget(RenderGraphTexture colorTarget)
        {
            m_ColorTarget = colorTarget ?? throw new ArgumentNullException(nameof(colorTarget));
            MarkPassResourceLayoutDirty();
        }

        public void ResetColorTarget()
        {
            m_ColorTarget = m_DefaultColorTarget;
            MarkPassResourceLayoutDirty();
        }

        public void SetDepthTarget(RenderGraphTexture depthTarget)
        {
            m_DepthTarget = depthTarget ?? throw new ArgumentNullException(nameof(depthTarget));
            MarkPassResourceLayoutDirty();
        }

        public void ResetDepthTarget()
        {
            m_DepthTarget = m_DefaultDepthTarget;
            MarkPassResourceLayoutDirty();
        }

        public void SetColorTargets(params RenderGraphTexture[] colorTargets)
        {
            if (colorTargets == null || colorTargets.Length == 0)
            {
                m_ColorTarget = m_DefaultColorTarget;
                m_AdditionalColorTargets.Clear();
                MarkPassResourceLayoutDirty();
                return;
            }

            if (colorTargets.Length > MaxColorAttachmentCount)
                throw new InvalidOperationException($"Raster passes support up to {MaxColorAttachmentCount} color attachments.");

            if (colorTargets[0] == null)
                throw new ArgumentNullException(nameof(colorTargets), "Primary color target cannot be null.");

            m_ColorTarget = colorTargets[0];
            m_AdditionalColorTargets.Clear();

            for (var i = 1; i < colorTargets.Length; i++)
            {
                if (colorTargets[i] == null)
                    throw new ArgumentNullException(nameof(colorTargets), $"Color target at index {i} cannot be null.");

                m_AdditionalColorTargets.Add(colorTargets[i]);
            }

            MarkPassResourceLayoutDirty();
        }

        public void AddColorTarget(RenderGraphTexture colorTarget)
        {
            if (colorTarget == null)
                throw new ArgumentNullException(nameof(colorTarget));

            if (1 + m_AdditionalColorTargets.Count >= MaxColorAttachmentCount)
                throw new InvalidOperationException($"Raster passes support up to {MaxColorAttachmentCount} color attachments.");

            m_AdditionalColorTargets.Add(colorTarget);
            MarkPassResourceLayoutDirty();
        }

        public bool RemoveColorTarget(RenderGraphTexture colorTarget)
        {
            if (colorTarget == null)
                return false;

            var removed = m_AdditionalColorTargets.Remove(colorTarget);
            if (removed)
                MarkPassResourceLayoutDirty();

            return removed;
        }

        public void ClearAdditionalColorTargets()
        {
            if (m_AdditionalColorTargets.Count == 0)
                return;

            m_AdditionalColorTargets.Clear();
            MarkPassResourceLayoutDirty();
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

        private void MarkPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = true;
        }
    }
}
