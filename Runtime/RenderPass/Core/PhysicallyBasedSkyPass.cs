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

        [RenderGraphResource(Name = "Color", Access = AccessFlags.ReadWrite, AttachmentIndex = 0)]
        private RenderGraphTexture m_ColorTarget;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.ReadWrite, IsDepthAttachment = true)]
        private RenderGraphTexture m_DepthTexture;

        private Material m_Material;
        private bool m_IsActive;
        private Matrix4x4 m_PixelCoordToViewDirMatrix = Matrix4x4.identity;
        private Vector4 m_SkyCameraPositionPS;
        private Vector4 m_SkySunDirection;
        private Vector4 m_SkySunColor;
        private Vector4 m_SkyPlanetParams;
        private Vector4 m_SkyAirScattering;
        private Vector4 m_SkyAirExtinction;
        private Vector4 m_SkyAerosolScattering;
        private Vector4 m_SkyAerosolExtinction;
        private Vector4 m_SkyOzoneExtinction;
        private Vector4 m_SkyOzoneParams;
        private Vector4 m_SkyGroundTint;

        public PhysicallyBasedSkyPass()
        {
            m_ColorTarget = RenderGraphTexture.CreateInput("SkyColor", GraphicsFormat.R8G8B8A8_SRGB);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
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
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            var context = new SkyRendererContext(cameraData, lightData);

            m_IsActive = skyData.activeSkyType == SkyType.PhysicallyBased
                && volume != null
                && volume.IsActive();

            m_PixelCoordToViewDirMatrix = cameraData.camera != null
                ? cameraData.GetPixelCoordToViewDirWSMatrix()
                : Matrix4x4.identity;

            if (m_IsActive)
            {
                var planetRadius = Mathf.Max(volume.planetRadius.value, 1000.0f);
                var atmosphereRadius = Mathf.Max(volume.GetAtmosphereRadius(), planetRadius + 1.0f);
                var cameraPosition = PhysicallyBasedSkyRenderer.ResolveCameraPosition(context, volume.planetRadius.value);
                var sunDirection = PhysicallyBasedSkyRenderer.ResolveSunDirection(context);
                var sunColor = PhysicallyBasedSkyRenderer.ResolveSunColor(context);
                var aerosolExtinction = volume.GetAerosolExtinctionCoefficient();
                var sunAngularRadius = Mathf.Deg2Rad
                                       * PhysicallyBasedSkyRenderer.SunAngularDiameterDegrees
                                       * Mathf.Max(volume.sunDiskSize.value, 0.01f)
                                       * 0.5f;
                var aerosolScattering = volume.GetAerosolScatteringCoefficient();
                var ozoneExtinction = volume.GetOzoneExtinctionCoefficient();

                m_SkyCameraPositionPS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
                m_SkySunDirection = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0.0f);
                m_SkySunColor = ToVector4(sunColor.linear * PhysicallyBasedSkyRenderer.SunIlluminanceScale);
                m_SkyPlanetParams = new Vector4(
                    planetRadius,
                    atmosphereRadius,
                    Mathf.Max(volume.exposure.value, 0.0f),
                    volume.renderSunDisk.value ? 1.0f : 0.0f);
                m_SkyAirScattering = ToVector4(volume.GetAirScatteringCoefficient());
                m_SkyAirExtinction = ToVector4(volume.GetAirExtinctionCoefficient());
                m_SkyAerosolScattering = new Vector4(
                    aerosolScattering.x,
                    aerosolScattering.y,
                    aerosolScattering.z,
                    volume.GetAerosolScaleHeight());
                m_SkyAerosolExtinction = new Vector4(
                    aerosolExtinction,
                    aerosolExtinction,
                    aerosolExtinction,
                    Mathf.Clamp(volume.aerosolAnisotropy.value, -0.95f, 0.95f));
                m_SkyOzoneExtinction = new Vector4(
                    ozoneExtinction.x,
                    ozoneExtinction.y,
                    ozoneExtinction.z,
                    volume.ozoneMinimumAltitude.value);
                m_SkyOzoneParams = new Vector4(
                    volume.ozoneLayerWidth.value,
                    volume.GetAirScaleHeight(),
                    sunAngularRadius,
                    volume.GetAerosolScaleHeight());
                m_SkyGroundTint = ToVector4(volume.groundTint.value.linear);
            }
            else
            {
                m_SkyCameraPositionPS = Vector4.zero;
                m_SkySunDirection = new Vector4(0.0f, 1.0f, 0.0f, 0.0f);
                m_SkySunColor = Vector4.zero;
                m_SkyPlanetParams = Vector4.zero;
                m_SkyAirScattering = Vector4.zero;
                m_SkyAirExtinction = Vector4.zero;
                m_SkyAerosolScattering = Vector4.zero;
                m_SkyAerosolExtinction = Vector4.zero;
                m_SkyOzoneExtinction = Vector4.zero;
                m_SkyOzoneParams = Vector4.zero;
                m_SkyGroundTint = Vector4.zero;
            }

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

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetMatrix(PixelCoordToViewDirWSId, m_PixelCoordToViewDirMatrix);
            mpb.SetVector(SkyCameraPositionPsId, m_SkyCameraPositionPS);
            mpb.SetVector(SkySunDirectionId, m_SkySunDirection);
            mpb.SetVector(SkySunColorId, m_SkySunColor);
            mpb.SetVector(SkyPlanetParamsId, m_SkyPlanetParams);
            mpb.SetVector(SkyAirScatteringId, m_SkyAirScattering);
            mpb.SetVector(SkyAirExtinctionId, m_SkyAirExtinction);
            mpb.SetVector(SkyAerosolScatteringId, m_SkyAerosolScattering);
            mpb.SetVector(SkyAerosolExtinctionId, m_SkyAerosolExtinction);
            mpb.SetVector(SkyOzoneExtinctionId, m_SkyOzoneExtinction);
            mpb.SetVector(SkyOzoneParamsId, m_SkyOzoneParams);
            mpb.SetVector(SkyGroundTintId, m_SkyGroundTint);

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

        private static Vector4 ToVector4(Vector3 value)
        {
            return new Vector4(value.x, value.y, value.z, 0.0f);
        }

        private static Vector4 ToVector4(Color value)
        {
            return new Vector4(value.r, value.g, value.b, value.a);
        }
    }
}
