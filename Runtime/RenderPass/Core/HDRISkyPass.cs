using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class HDRISkyPass : RasterPass
    {
        private static readonly int SkyCubemapId = Shader.PropertyToID("_SkyCubemap");
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int SkyExposureId = Shader.PropertyToID("_SkyExposure");
        private static readonly int SkyRotationId = Shader.PropertyToID("_SkyRotation");
        private static readonly int DepthTextureID = Shader.PropertyToID("_DepthTexture");

        private Material m_Material;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Write, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        public HDRISkyPass()
        {
            m_ColorTarget = CreateColorTarget("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = CreateDepthTarget("SkyDepth");
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.HDRISkyShader);
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

            ResizeTexture(m_ColorTarget, width, height);
            ResizeTexture(m_DepthTexture, width, height);
            UpdateMaterialProperties();
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Material == null)
                return;

            Blitter.BlitTexture(context.cmd, Vector2.one, m_Material, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private void UpdateMaterialProperties()
        {
            if (m_Material == null)
                return;

            var skySettings = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
            var cubemap = skySettings?.skyCubemap.value;
            var tint = skySettings?.tint.value ?? Color.white;
            var exposure = skySettings?.exposure.value ?? 1f;
            var rotation = skySettings?.rotation.value ?? 0f;
            m_Material.SetTexture(SkyCubemapId, cubemap);

            m_Material.SetTexture(SkyCubemapId, cubemap);
            m_Material.SetColor(SkyTintId, tint);
            m_Material.SetFloat(SkyExposureId, exposure);
            m_Material.SetFloat(SkyRotationId, rotation);
            m_Material.SetTexture(DepthTextureID, m_DepthTexture.innerHandle);
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

        private static RenderGraphTexture CreateDepthTarget(string name)
        {
            var texture = new RenderGraphTexture
            {
                desc = RenderGraphTextureDesc.CreateDepthTarget(1, 1, DepthBits.Depth32)
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
