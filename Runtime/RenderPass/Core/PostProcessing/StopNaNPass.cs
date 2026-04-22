using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class StopNaNPass : UnsafePass, IDynamicPassResourceLayout
    {
        [RenderGraphResource(Access = AccessFlags.Read)]
        private RenderGraphTexture m_Source = new();

        [RenderGraphResource(
            Name = "StopNaNOutput",
            Access = AccessFlags.Write)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private bool m_IsPassResourceLayoutDirty;

        public bool IsPassResourceLayoutDirty => m_IsPassResourceLayoutDirty;

        public StopNaNPass()
        {
            profilingSampler = new ProfilingSampler(nameof(StopNaNPass));
            m_OutputTexture = CreatePassOwnedTexture("StopNaNOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);
        }

        public void ClearPassResourceLayoutDirty()
        {
            m_IsPassResourceLayoutDirty = false;
        }

        internal void SetInput(RenderGraphTexture sourceTexture)
        {
            if (sourceTexture == null)
                throw new ArgumentNullException(nameof(sourceTexture));

            UpdateOutputDescriptor(sourceTexture);

            if (ReferenceEquals(m_Source, sourceTexture))
                return;

            m_Source = sourceTexture;
            m_IsPassResourceLayoutDirty = true;
        }

        internal RenderGraphTexture GetOutputTexture()
        {
            return m_OutputTexture;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.BlitShader);

            if (m_Material != null)
                CoreUtils.SetKeyword(m_Material, "_STOP_NANS", true);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var width = cameraData != null && cameraData.actualWidth > 0
                ? cameraData.actualWidth
                : cameraData != null && cameraData.pixelWidth > 0
                    ? cameraData.pixelWidth
                    : Mathf.Max(1, Screen.width);
            var height = cameraData != null && cameraData.actualHeight > 0
                ? cameraData.actualHeight
                : cameraData != null && cameraData.pixelHeight > 0
                    ? cameraData.pixelHeight
                    : Mathf.Max(1, Screen.height);

            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = Mathf.Max(1, width);
            m_OutputTexture.desc.Height = Mathf.Max(1, height);
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.Name = "StopNaNOutput";
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null)
                return;

            if (m_Source == null || m_Source.innerHandle.IsValid() != true || m_OutputTexture?.innerHandle.IsValid() != true)
                return;

            var cmd = context.cmd;
            var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            RTHandle sourceHandle = m_Source.innerHandle;
            var scale = Vector2.one;

            if (sourceHandle.useScaling)
            {
                scale.x = sourceHandle.rtHandleProperties.rtHandleScale.x;
                scale.y = sourceHandle.rtHandleProperties.rtHandleScale.y;
            }

            var scaleBias = GetBlitScaleBias(
                scale,
                context.GetTextureUVOrigin(m_Source.innerHandle),
                context.GetTextureUVOrigin(m_OutputTexture.innerHandle));

            cmd.SetRenderTarget(m_OutputTexture.innerHandle);
            Blitter.BlitTexture(unsafeCmd, sourceHandle, scaleBias, m_Material, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_IsPassResourceLayoutDirty = false;
        }

        private void UpdateOutputDescriptor(RenderGraphTexture sourceTexture)
        {
            if (m_OutputTexture == null)
                m_OutputTexture = CreatePassOwnedTexture("StopNaNOutput", 1, 1, GraphicsFormat.R16G16B16A16_SFloat);

            var sourceDesc = sourceTexture?.desc;
            if (sourceDesc == null)
                return;

            m_OutputTexture.desc = sourceDesc.Clone();
            m_OutputTexture.desc.Name = "StopNaNOutput";
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.FilterMode = FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = TextureWrapMode.Clamp;
        }

        private static RenderGraphTexture CreatePassOwnedTexture(
            string name,
            int width,
            int height,
            GraphicsFormat format)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateColorTarget(width, height, format)
            };
            texture.desc.Name = name;
            texture.desc.ClearBuffer = false;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static Vector4 GetBlitScaleBias(
            Vector2 scale,
            TextureUVOrigin sourceTextureUVOrigin,
            TextureUVOrigin destinationTextureUVOrigin)
        {
            var yFlip = sourceTextureUVOrigin != destinationTextureUVOrigin;
            return yFlip
                ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                : new Vector4(scale.x, scale.y, 0f, 0f);
        }
    }
}
