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
        private Material m_Material;

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        private Matrix4x4 m_PixelCoordToViewDirMatrix;
        private Cubemap m_Cubemap;
        private Color m_Tint = Color.white;
        private float m_Exposure;
        private float m_Rotation;

        public HDRISkyPass()
        {
            m_ColorTarget = RenderGraphTexture.CreateInput("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_Material = CoreUtils.CreateEngineMaterial(resources.HDRISkyShader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.Get<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            m_PixelCoordToViewDirMatrix = cameraData.GetPixelCoordToViewDirWSMatrix();
            m_Cubemap = skyData.specularCubemap;
            m_Tint = skyData.tint;
            m_Exposure = skyData.exposure;
            m_Rotation = skyData.rotation;

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = Mathf.Max(1, Screen.height);

            m_ColorTarget.Resize(width, height);
            m_DepthTexture.Resize(width, height);
        }

        public override void Record(RasterGraphContext context)
        {
            if (!m_Material || m_Cubemap == null)
                return;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(SkyCubemapId, m_Cubemap);
            mpb.SetColor(SkyTintId, m_Tint);
            mpb.SetVector(SkyParamId, BuildSkyParam(m_Exposure, m_Rotation));
            mpb.SetMatrix("_PixelCoordToViewDirWS", m_PixelCoordToViewDirMatrix);

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
            return new Vector4(exposure, 1f, -rotation, 0f);
        }
    }
}
