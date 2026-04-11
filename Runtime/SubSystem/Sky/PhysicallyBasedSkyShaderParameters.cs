using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct PhysicallyBasedSkyShaderParameters
    {
        internal Matrix4x4 pixelCoordToViewDirWS;
        internal Vector4 skyCameraPositionPS;
        internal Vector4 skySunDirection;
        internal Vector4 skySunColor;
        internal Vector4 skyPlanetParams;
        internal Vector4 skyAirScattering;
        internal Vector4 skyAirExtinction;
        internal Vector4 skyAerosolScattering;
        internal Vector4 skyAerosolExtinction;
        internal Vector4 skyOzoneExtinction;
        internal Vector4 skyOzoneParams;
        internal Vector4 skyGroundTint;
        internal Vector4 skyFogParams;
    }

    internal static class PhysicallyBasedSkyShaderParameterBuilder
    {
        internal static bool TryBuild(ContextContainer frameData, out PhysicallyBasedSkyShaderParameters parameters)
        {
            if (frameData == null)
            {
                parameters = default;
                return false;
            }

            return TryBuild(
                frameData.GetOrCreate<VividCameraData>(),
                frameData.GetOrCreate<VividSkyData>(),
                frameData.GetOrCreate<VividLightData>(),
                out parameters);
        }

        internal static bool TryBuild(
            VividCameraData cameraData,
            VividSkyData skyData,
            VividLightData lightData,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            parameters = default;
            parameters.pixelCoordToViewDirWS = cameraData?.camera != null
                ? cameraData.GetPixelCoordToViewDirWSMatrix()
                : Matrix4x4.identity;

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (skyData == null
                || skyData.activeSkyType != SkyType.PhysicallyBased
                || volume == null
                || !volume.IsActive())
            {
                return false;
            }

            var context = new SkyRendererContext(cameraData, lightData);
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

            parameters.skyCameraPositionPS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            parameters.skySunDirection = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0.0f);
            parameters.skySunColor = ToVector4(sunColor.linear * PhysicallyBasedSkyRenderer.SunIlluminanceScale);
            parameters.skyPlanetParams = new Vector4(
                planetRadius,
                atmosphereRadius,
                Mathf.Max(volume.exposure.value, 0.0f),
                volume.renderSunDisk.value ? 1.0f : 0.0f);
            parameters.skyAirScattering = ToVector4(volume.GetAirScatteringCoefficient());
            parameters.skyAirExtinction = ToVector4(volume.GetAirExtinctionCoefficient());
            parameters.skyAerosolScattering = new Vector4(
                aerosolScattering.x,
                aerosolScattering.y,
                aerosolScattering.z,
                volume.GetAerosolScaleHeight());
            parameters.skyAerosolExtinction = new Vector4(
                aerosolExtinction,
                aerosolExtinction,
                aerosolExtinction,
                Mathf.Clamp(volume.aerosolAnisotropy.value, -0.95f, 0.95f));
            parameters.skyOzoneExtinction = new Vector4(
                ozoneExtinction.x,
                ozoneExtinction.y,
                ozoneExtinction.z,
                volume.ozoneMinimumAltitude.value);
            parameters.skyOzoneParams = new Vector4(
                volume.ozoneLayerWidth.value,
                volume.GetAirScaleHeight(),
                sunAngularRadius,
                volume.GetAerosolScaleHeight());
            parameters.skyGroundTint = ToVector4(volume.groundTint.value.linear);
            parameters.skyFogParams = new Vector4(
                volume.IsHeightFogActive() ? 1.0f : 0.0f,
                volume.fogBaseHeight.value,
                Mathf.Max(volume.fogDensity.value, 0.0f),
                Mathf.Max(volume.fogMaxDistance.value, 0.0f));
            return true;
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
