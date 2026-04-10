using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class PhysicallyBasedSkyPass : RasterPass
    {
        internal const string PhysicallyBasedSkyShaderName = "Hidden/VividRP/PhysicallyBasedSky";

        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int SkyViewLutId = Shader.PropertyToID("_SkyViewLUT");
        private static readonly int SkyUseLutId = Shader.PropertyToID("_SkyUseLUT");
        private static readonly int SkyCameraPositionPsId = Shader.PropertyToID("_SkyCameraPositionPS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int SkyPlanetParamsId = Shader.PropertyToID("_SkyPlanetParams");
        private static readonly int SkyAirScatteringId = Shader.PropertyToID("_SkyAirScattering");
        private static readonly int SkyAirExtinctionId = Shader.PropertyToID("_SkyAirExtinction");
        private static readonly int SkyAerosolScatteringId = Shader.PropertyToID("_SkyAerosolScattering");
        private static readonly int SkyAerosolExtinctionId = Shader.PropertyToID("_SkyAerosolExtinction");
        private static readonly int SkyOzoneExtinctionId = Shader.PropertyToID("_SkyOzoneExtinction");
        private static readonly int SkyOzoneParamsId = Shader.PropertyToID("_SkyOzoneParams");
        private static readonly int SkyGroundTintId = Shader.PropertyToID("_SkyGroundTint");
        private static readonly int CelestialBodyDatasId = Shader.PropertyToID("_CelestialBodyDatas");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "SkyViewLUT", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SkyViewLUT;

        private Material m_Material;
        private bool m_IsActive;
        private PhysicallyBasedSkyShaderParameters m_Parameters;
        private PhysicallyBasedSkyMaterialParameters m_MaterialParameters;
        private bool m_HasMaterialParameters;
        private readonly PhysicallyBasedSkyCelestialBodyBuffer m_CelestialBodyBuffer = new();

        public PhysicallyBasedSkyPass()
        {
            m_ColorTarget = RenderGraphTexture.CreateInput("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_SkyViewLUT = RenderGraphTexture.CreateInput("SkyViewLUT", GraphicsFormat.R16G16B16A16_SFloat);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.PhysicallyBasedSkyShader;
            shader ??= Shader.Find(PhysicallyBasedSkyShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{PhysicallyBasedSkyShaderName}' for {nameof(PhysicallyBasedSkyPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var skyContext = new SkyRendererContext(cameraData, lightData);
            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters);
            m_HasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters);
            if (m_IsActive)
                m_CelestialBodyBuffer.Update(skyContext);

            var width = cameraData.actualWidth > 0 ? cameraData.actualWidth : cameraData.pixelWidth;
            var height = cameraData.actualHeight > 0 ? cameraData.actualHeight : cameraData.pixelHeight;

            if (width <= 0)
                width = cameraData.camera != null ? Mathf.Max(1, cameraData.camera.scaledPixelWidth) : Mathf.Max(1, Screen.width);

            if (height <= 0)
                height = cameraData.camera != null ? Mathf.Max(1, cameraData.camera.scaledPixelHeight) : Mathf.Max(1, Screen.height);

            m_ColorTarget.Resize(width, height);
            m_DepthTexture.Resize(width, height);
        }

        public override void Record(RasterGraphContext context)
        {
            if (!m_IsActive || m_Material == null)
                return;

            m_Material.SetBuffer(CelestialBodyDatasId, m_CelestialBodyBuffer.Buffer);

            var skyViewTexture = m_SkyViewLUT != null
                ? ResolveTexture(m_SkyViewLUT.innerHandle)
                : null;
            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetMatrix(PixelCoordToViewDirWSId, m_Parameters.pixelCoordToViewDirWS);
            mpb.SetTexture(SkyViewLutId, skyViewTexture ?? Texture2D.blackTexture);
            mpb.SetFloat(SkyUseLutId, skyViewTexture != null ? 1.0f : 0.0f);
            mpb.SetVector(SkyCameraPositionPsId, m_Parameters.skyCameraPositionPS);
            mpb.SetVector(SkySunDirectionId, m_Parameters.skySunDirection);
            mpb.SetVector(SkySunColorId, m_Parameters.skySunColor);
            mpb.SetVector(SkyPlanetParamsId, m_Parameters.skyPlanetParams);
            mpb.SetVector(SkyAirScatteringId, m_Parameters.skyAirScattering);
            mpb.SetVector(SkyAirExtinctionId, m_Parameters.skyAirExtinction);
            mpb.SetVector(SkyAerosolScatteringId, m_Parameters.skyAerosolScattering);
            mpb.SetVector(SkyAerosolExtinctionId, m_Parameters.skyAerosolExtinction);
            mpb.SetVector(SkyOzoneExtinctionId, m_Parameters.skyOzoneExtinction);
            mpb.SetVector(SkyOzoneParamsId, m_Parameters.skyOzoneParams);
            mpb.SetVector(SkyGroundTintId, m_Parameters.skyGroundTint);
            if (m_HasMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            m_CelestialBodyBuffer.Dispose();
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }
    }
}
