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
        private static readonly int SkyParamId = Shader.PropertyToID("_SkyParam");
        private static readonly int DepthTextureID = Shader.PropertyToID("_DepthTexture");

        private Material m_Material;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        private Matrix4x4 m_PixelCoordToViewDirMatrix;

        public HDRISkyPass()
        {
            m_ColorTarget = CreateColorTarget("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = CreateDepthTarget("SkyDepth", DepthBits.Depth32);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.HDRISkyShader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            m_PixelCoordToViewDirMatrix = cameraData.GetPixelCoordToViewDirWSMatrix();
        }

        public override void Record(RasterGraphContext context)
        {
            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            var skySettings = VolumeManager.instance.stack?.GetComponent<HDRISkyVolume>();
            var cubemap = skySettings?.GetSkyCubemapOrDefault();
            var tint = skySettings?.tint.value ?? Color.white;
            var exposure = skySettings?.exposure.value ?? 1f;
            var rotation = skySettings?.rotation.value ?? 0f;

            if (cubemap != null)
            {
                mpb.SetTexture(SkyCubemapId, cubemap);
            }
            mpb.SetColor(SkyTintId, tint);
            mpb.SetVector(SkyParamId, BuildSkyParam(exposure, rotation));
            mpb.SetMatrix("_PixelCoordToViewDirWS", m_PixelCoordToViewDirMatrix);

            if (!m_Material)
                return;

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

        internal static Vector4 BuildSkyParam(float exposure, float rotation)
        {
            return new Vector4(0f, exposure, -rotation, 0f);
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


    }
}
