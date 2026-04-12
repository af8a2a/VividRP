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

    internal struct PhysicallyBasedSkyMaterialParameters
    {
        internal Vector4 pbrSkyCameraPositionPS;
        internal Vector4 planetCenterRadius;
        internal Vector4 planetUpAltitude;
        internal Vector4 airSeaLevelExtinction;
        internal Vector4 airSeaLevelScattering;
        internal Vector4 aerosolSeaLevelScattering;
        internal Vector4 ozoneSeaLevelExtinction;
        internal Vector4 groundAlbedoPlanetRadius;
        internal Vector4 horizonTint;
        internal Vector4 zenithTint;
        internal Vector4 ozoneScaleOffset;
        internal float atmosphericRadius;
        internal float aerosolAnisotropy;
        internal float aerosolPhasePartConstant;
        internal float aerosolSeaLevelExtinction;
        internal float airDensityFalloff;
        internal float airScaleHeight;
        internal float aerosolDensityFalloff;
        internal float aerosolScaleHeight;
        internal float ozoneLayerStart;
        internal float ozoneLayerEnd;
        internal float intensityMultiplier;
        internal float colorSaturation;
        internal float alphaSaturation;
        internal float alphaMultiplier;
        internal float horizonZenithShiftPower;
        internal float horizonZenithShiftScale;
        internal int celestialLightCount;
        internal int celestialBodyCount;
        internal float atmosphericDepth;
        internal float rcpAtmosphericDepth;
        internal float celestialLightExposure;
        internal float volumetricCloudsBottomAltitude;
        internal int renderSunDisk;
    }

    internal static class PhysicallyBasedSkyShaderParameterBuilder
    {
        private const float MaxSkyRadiance = 60000.0f;

        internal static bool TryBuild(ContextContainer frameData, out PhysicallyBasedSkyShaderParameters parameters)
        {
            if (frameData == null)
            {
                parameters = default;
                return false;
            }

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            var lightData = frameData.GetOrCreate<VividLightData>();

            if (skyData == null || skyData.activeSkyType != SkyType.PhysicallyBased)
            {
                parameters = default;
                return false;
            }

            return TryBuild(
                VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume(),
                new SkyRendererContext(cameraData, lightData),
                cameraData?.camera != null
                    ? cameraData.GetPixelCoordToViewDirWSMatrix()
                    : Matrix4x4.identity,
                1.0f,
                out parameters);
        }

        internal static bool TryBuild(
            VividCameraData cameraData,
            VividSkyData skyData,
            VividLightData lightData,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            if (skyData == null
                || skyData.activeSkyType != SkyType.PhysicallyBased
                || VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume() == null)
            {
                parameters = default;
                return false;
            }

            return TryBuild(
                VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume(),
                new SkyRendererContext(cameraData, lightData),
                cameraData?.camera != null
                    ? cameraData.GetPixelCoordToViewDirWSMatrix()
                    : Matrix4x4.identity,
                1.0f,
                out parameters);
        }

        internal static bool TryBuild(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            return TryBuild(volume, context, Matrix4x4.identity, 1.0f, out parameters);
        }

        internal static bool TryBuildForSkyBaking(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            // Sky baking must stay independent from camera exposure adaptation.
            return TryBuild(volume, context, Matrix4x4.identity, 1.0f, out parameters);
        }

        internal static bool TryBuildForAmbientProbe(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            return TryBuildForSkyBaking(volume, context, out parameters);
        }

        private static bool TryBuild(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            Matrix4x4 pixelCoordToViewDirWS,
            float skyExposureMultiplier,
            out PhysicallyBasedSkyShaderParameters parameters)
        {
            parameters = default;
            parameters.pixelCoordToViewDirWS = pixelCoordToViewDirWS;

            if (volume == null || !volume.IsActive())
            {
                return false;
            }

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
            var exposedSunColor = ClampRadiance(ToVector3(sunColor.linear) * PhysicallyBasedSkyRenderer.SunIlluminanceScale);
            var exposedGroundTint = ClampRadiance(ToVector3(volume.groundTint.value.linear));
            var ozoneMinimumAltitude = volume.GetOzoneLayerMinimumAltitude();
            var ozoneLayerWidth = volume.GetOzoneLayerWidth();

            parameters.skyCameraPositionPS = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1.0f);
            parameters.skySunDirection = new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0.0f);
            parameters.skySunColor = ToVector4(exposedSunColor);
            parameters.skyPlanetParams = new Vector4(
                planetRadius,
                atmosphereRadius,
                Mathf.Max(skyExposureMultiplier, 0.0f),
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
                ozoneMinimumAltitude);
            parameters.skyOzoneParams = new Vector4(
                ozoneLayerWidth,
                volume.GetAirScaleHeight(),
                sunAngularRadius,
                volume.GetAerosolScaleHeight());
            parameters.skyGroundTint = ToVector4(exposedGroundTint);
            parameters.skyFogParams = new Vector4(
                volume.IsHeightFogActive() ? 1.0f : 0.0f,
                volume.fogBaseHeight.value,
                Mathf.Max(volume.fogDensity.value, 0.0f),
                Mathf.Max(volume.fogMaxDistance.value, 0.0f));
            return true;
        }

        internal static bool TryBuildMaterialParameters(
            ContextContainer frameData,
            out PhysicallyBasedSkyMaterialParameters parameters)
        {
            parameters = default;
            if (frameData == null)
                return false;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var lightData = frameData.GetOrCreate<VividLightData>();
            var skyData = frameData.GetOrCreate<VividSkyData>();
            if (skyData == null || skyData.activeSkyType != SkyType.PhysicallyBased)
                return false;

            return TryBuildMaterialParameters(
                VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume(),
                new SkyRendererContext(cameraData, lightData),
                out parameters);
        }

        internal static bool TryBuildMaterialParameters(
            PhysicallyBasedSkyVolume volume,
            in SkyRendererContext context,
            out PhysicallyBasedSkyMaterialParameters parameters)
        {
            parameters = default;
            if (volume == null || !volume.IsActive())
                return false;

            var planetRadius = Mathf.Max(volume.planetRadius.value, 1000.0f);
            var atmosphericDepth = Mathf.Max(volume.GetMaximumAltitude(), 1.0f);
            var airScaleHeight = Mathf.Max(volume.GetAirScaleHeight(), 1.0f);
            var aerosolScaleHeight = Mathf.Max(volume.GetAerosolScaleHeight(), 1.0f);
            var aerosolAnisotropy = Mathf.Clamp(volume.aerosolAnisotropy.value, -0.95f, 0.95f);
            var ozoneLayerMinimumAltitude = volume.GetOzoneLayerMinimumAltitude();
            var ozoneLayerWidth = Mathf.Max(volume.GetOzoneLayerWidth(), 1.0f);
            var atmosphericRadius = planetRadius + atmosphericDepth;
            var exponentialInterpolation = ComputeExponentialInterpolationParams(volume.horizonZenithShift.value);
            var worldCameraPosition = context.cameraData?.camera != null
                ? context.cameraData.camera.transform.position
                : Vector3.zero;
            var planetCenter = new Vector3(0.0f, -planetRadius, 0.0f);
            var cameraToPlanetCenter = worldCameraPosition - planetCenter;
            if (cameraToPlanetCenter.sqrMagnitude <= 1e-6f)
                cameraToPlanetCenter = Vector3.up * (planetRadius + 1.0f);

            var radialDistance = cameraToPlanetCenter.magnitude;
            if (radialDistance < planetRadius + 1.0f)
            {
                cameraToPlanetCenter = cameraToPlanetCenter.normalized * (planetRadius + 1.0f);
                radialDistance = cameraToPlanetCenter.magnitude;
            }

            var planetUp = cameraToPlanetCenter / radialDistance;
            var altitude = radialDistance - planetRadius;
            var lightExposure = ResolveCelestialLightExposure(context);
            var pbrSkyCameraPosition = PhysicallyBasedSkyRenderer.ResolveCameraPosition(context, planetRadius);

            parameters.pbrSkyCameraPositionPS = new Vector4(
                pbrSkyCameraPosition.x,
                pbrSkyCameraPosition.y,
                pbrSkyCameraPosition.z,
                1.0f);
            parameters.planetCenterRadius = new Vector4(
                planetCenter.x,
                planetCenter.y,
                planetCenter.z,
                planetRadius);
            parameters.planetUpAltitude = new Vector4(
                planetUp.x,
                planetUp.y,
                planetUp.z,
                altitude);
            parameters.airSeaLevelExtinction = ToVector4(volume.GetAirExtinctionCoefficient());
            parameters.airSeaLevelScattering = ToVector4(volume.GetAirScatteringCoefficient());
            parameters.aerosolSeaLevelScattering = ToVector4(volume.GetAerosolScatteringCoefficient());
            parameters.ozoneSeaLevelExtinction = ToVector4(volume.GetOzoneExtinctionCoefficient());
            parameters.groundAlbedoPlanetRadius = new Vector4(
                volume.groundTint.value.linear.r,
                volume.groundTint.value.linear.g,
                volume.groundTint.value.linear.b,
                planetRadius);
            parameters.horizonTint = ToVector4(volume.horizonTint.value.linear);
            parameters.zenithTint = ToVector4(volume.zenithTint.value.linear);
            parameters.ozoneScaleOffset = new Vector4(
                2.0f / ozoneLayerWidth,
                -2.0f * ozoneLayerMinimumAltitude / ozoneLayerWidth - 1.0f,
                0.0f,
                0.0f);
            parameters.atmosphericRadius = atmosphericRadius;
            parameters.aerosolAnisotropy = aerosolAnisotropy;
            parameters.aerosolPhasePartConstant = CornetteShanksPhasePartConstant(aerosolAnisotropy);
            parameters.aerosolSeaLevelExtinction = volume.GetAerosolExtinctionCoefficient();
            parameters.airDensityFalloff = 1.0f / airScaleHeight;
            parameters.airScaleHeight = airScaleHeight;
            parameters.aerosolDensityFalloff = 1.0f / aerosolScaleHeight;
            parameters.aerosolScaleHeight = aerosolScaleHeight;
            parameters.ozoneLayerStart = planetRadius + ozoneLayerMinimumAltitude;
            parameters.ozoneLayerEnd = planetRadius + ozoneLayerMinimumAltitude + ozoneLayerWidth;
            parameters.intensityMultiplier = 1.0f;
            parameters.colorSaturation = volume.colorSaturation.value;
            parameters.alphaSaturation = volume.alphaSaturation.value;
            parameters.alphaMultiplier = volume.alphaMultiplier.value;
            parameters.horizonZenithShiftPower = exponentialInterpolation.x;
            parameters.horizonZenithShiftScale = exponentialInterpolation.y;
            parameters.celestialLightCount = ResolveCelestialLightCount(context);
            parameters.celestialBodyCount = PhysicallyBasedSkyCelestialBodyUtility.ResolveCelestialBodyCount(context);
            parameters.atmosphericDepth = atmosphericDepth;
            parameters.rcpAtmosphericDepth = 1.0f / atmosphericDepth;
            parameters.celestialLightExposure = lightExposure;
            parameters.volumetricCloudsBottomAltitude = 0.0f;
            parameters.renderSunDisk = volume.renderSunDisk.value ? 1 : 0;
            return true;
        }

        private static Vector3 ClampRadiance(Vector3 value)
        {
            return new Vector3(
                Mathf.Clamp(value.x, 0.0f, MaxSkyRadiance),
                Mathf.Clamp(value.y, 0.0f, MaxSkyRadiance),
                Mathf.Clamp(value.z, 0.0f, MaxSkyRadiance));
        }

        private static Vector3 ToVector3(Color value)
        {
            return new Vector3(value.r, value.g, value.b);
        }

        private static Vector4 ToVector4(Vector3 value)
        {
            return new Vector4(value.x, value.y, value.z, 0.0f);
        }

        private static Vector4 ToVector4(Color value)
        {
            return new Vector4(value.r, value.g, value.b, value.a);
        }

        private static float CornetteShanksPhasePartConstant(float anisotropy)
        {
            var g = anisotropy;
            return (3.0f / (8.0f * Mathf.PI)) * (1.0f - g * g) / (2.0f + g * g);
        }

        private static Vector2 ComputeExponentialInterpolationParams(float k)
        {
            if (Mathf.Abs(k) <= 1e-6f)
                k = 1e-6f;

            var x = 10.0f * k;
            var y = 1.0f / (Mathf.Exp(x) - 1.0f);
            return new Vector2(x, y);
        }

        private static int ResolveCelestialLightCount(in SkyRendererContext context)
        {
            return PhysicallyBasedSkyCelestialBodyUtility.ResolveCelestialLightCount(context);
        }

        private static float ResolveCelestialLightExposure(in SkyRendererContext context)
        {
            return Mathf.Max(PhysicallyBasedSkyCelestialBodyUtility.ResolveCelestialLightExposure(context), 1.0f);
        }
    }

    internal static class PhysicallyBasedSkyComputeParameterBinder
    {
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int PlanetCenterRadiusId = Shader.PropertyToID("_PlanetCenterRadius");
        private static readonly int PlanetUpAltitudeId = Shader.PropertyToID("_PlanetUpAltitude");
        private static readonly int AtmosphericRadiusId = Shader.PropertyToID("_AtmosphericRadius");
        private static readonly int AerosolAnisotropyId = Shader.PropertyToID("_AerosolAnisotropy");
        private static readonly int AerosolPhasePartConstantId = Shader.PropertyToID("_AerosolPhasePartConstant");
        private static readonly int AerosolSeaLevelExtinctionId = Shader.PropertyToID("_AerosolSeaLevelExtinction");
        private static readonly int AirDensityFalloffId = Shader.PropertyToID("_AirDensityFalloff");
        private static readonly int AirScaleHeightId = Shader.PropertyToID("_AirScaleHeight");
        private static readonly int AerosolDensityFalloffId = Shader.PropertyToID("_AerosolDensityFalloff");
        private static readonly int AerosolScaleHeightId = Shader.PropertyToID("_AerosolScaleHeight");
        private static readonly int OzoneScaleOffsetId = Shader.PropertyToID("_OzoneScaleOffset");
        private static readonly int OzoneLayerStartId = Shader.PropertyToID("_OzoneLayerStart");
        private static readonly int OzoneLayerEndId = Shader.PropertyToID("_OzoneLayerEnd");
        private static readonly int AirSeaLevelExtinctionId = Shader.PropertyToID("_AirSeaLevelExtinction");
        private static readonly int AirSeaLevelScatteringId = Shader.PropertyToID("_AirSeaLevelScattering");
        private static readonly int AerosolSeaLevelScatteringId = Shader.PropertyToID("_AerosolSeaLevelScattering");
        private static readonly int OzoneSeaLevelExtinctionId = Shader.PropertyToID("_OzoneSeaLevelExtinction");
        private static readonly int GroundAlbedoPlanetRadiusId = Shader.PropertyToID("_GroundAlbedo_PlanetRadius");
        private static readonly int HorizonTintId = Shader.PropertyToID("_HorizonTint");
        private static readonly int ZenithTintId = Shader.PropertyToID("_ZenithTint");
        private static readonly int IntensityMultiplierId = Shader.PropertyToID("_IntensityMultiplier");
        private static readonly int ColorSaturationId = Shader.PropertyToID("_ColorSaturation");
        private static readonly int AlphaSaturationId = Shader.PropertyToID("_AlphaSaturation");
        private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");
        private static readonly int HorizonZenithShiftPowerId = Shader.PropertyToID("_HorizonZenithShiftPower");
        private static readonly int HorizonZenithShiftScaleId = Shader.PropertyToID("_HorizonZenithShiftScale");
        private static readonly int CelestialLightCountId = Shader.PropertyToID("_CelestialLightCount");
        private static readonly int CelestialBodyCountId = Shader.PropertyToID("_CelestialBodyCount");
        private static readonly int AtmosphericDepthId = Shader.PropertyToID("_AtmosphericDepth");
        private static readonly int RcpAtmosphericDepthId = Shader.PropertyToID("_RcpAtmosphericDepth");
        private static readonly int CelestialLightExposureId = Shader.PropertyToID("_CelestialLightExposure");
        private static readonly int VolumetricCloudsBottomAltitudeId = Shader.PropertyToID("_VolumetricCloudsBottomAltitude");

        internal static void Apply(
            ComputeCommandBuffer commandBuffer,
            ComputeShader computeShader,
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            if (commandBuffer == null || computeShader == null)
                return;

            commandBuffer.SetComputeMatrixParam(computeShader, PixelCoordToViewDirWSId, skyParameters.pixelCoordToViewDirWS);
            commandBuffer.SetComputeVectorParam(computeShader, SkySunDirectionId, skyParameters.skySunDirection);
            commandBuffer.SetComputeVectorParam(computeShader, SkySunColorId, skyParameters.skySunColor);
            commandBuffer.SetComputeVectorParam(computeShader, PlanetCenterRadiusId, materialParameters.planetCenterRadius);
            commandBuffer.SetComputeVectorParam(computeShader, PlanetUpAltitudeId, materialParameters.planetUpAltitude);
            commandBuffer.SetComputeFloatParam(computeShader, AtmosphericRadiusId, materialParameters.atmosphericRadius);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolAnisotropyId, materialParameters.aerosolAnisotropy);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolPhasePartConstantId, materialParameters.aerosolPhasePartConstant);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolSeaLevelExtinctionId, materialParameters.aerosolSeaLevelExtinction);
            commandBuffer.SetComputeFloatParam(computeShader, AirDensityFalloffId, materialParameters.airDensityFalloff);
            commandBuffer.SetComputeFloatParam(computeShader, AirScaleHeightId, materialParameters.airScaleHeight);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolDensityFalloffId, materialParameters.aerosolDensityFalloff);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolScaleHeightId, materialParameters.aerosolScaleHeight);
            commandBuffer.SetComputeVectorParam(computeShader, OzoneScaleOffsetId, materialParameters.ozoneScaleOffset);
            commandBuffer.SetComputeFloatParam(computeShader, OzoneLayerStartId, materialParameters.ozoneLayerStart);
            commandBuffer.SetComputeFloatParam(computeShader, OzoneLayerEndId, materialParameters.ozoneLayerEnd);
            commandBuffer.SetComputeVectorParam(computeShader, AirSeaLevelExtinctionId, materialParameters.airSeaLevelExtinction);
            commandBuffer.SetComputeVectorParam(computeShader, AirSeaLevelScatteringId, materialParameters.airSeaLevelScattering);
            commandBuffer.SetComputeVectorParam(computeShader, AerosolSeaLevelScatteringId, materialParameters.aerosolSeaLevelScattering);
            commandBuffer.SetComputeVectorParam(computeShader, OzoneSeaLevelExtinctionId, materialParameters.ozoneSeaLevelExtinction);
            commandBuffer.SetComputeVectorParam(computeShader, GroundAlbedoPlanetRadiusId, materialParameters.groundAlbedoPlanetRadius);
            commandBuffer.SetComputeVectorParam(computeShader, HorizonTintId, materialParameters.horizonTint);
            commandBuffer.SetComputeVectorParam(computeShader, ZenithTintId, materialParameters.zenithTint);
            commandBuffer.SetComputeFloatParam(computeShader, IntensityMultiplierId, materialParameters.intensityMultiplier);
            commandBuffer.SetComputeFloatParam(computeShader, ColorSaturationId, materialParameters.colorSaturation);
            commandBuffer.SetComputeFloatParam(computeShader, AlphaSaturationId, materialParameters.alphaSaturation);
            commandBuffer.SetComputeFloatParam(computeShader, AlphaMultiplierId, materialParameters.alphaMultiplier);
            commandBuffer.SetComputeFloatParam(computeShader, HorizonZenithShiftPowerId, materialParameters.horizonZenithShiftPower);
            commandBuffer.SetComputeFloatParam(computeShader, HorizonZenithShiftScaleId, materialParameters.horizonZenithShiftScale);
            commandBuffer.SetComputeIntParam(computeShader, CelestialLightCountId, materialParameters.celestialLightCount);
            commandBuffer.SetComputeIntParam(computeShader, CelestialBodyCountId, materialParameters.celestialBodyCount);
            commandBuffer.SetComputeFloatParam(computeShader, AtmosphericDepthId, materialParameters.atmosphericDepth);
            commandBuffer.SetComputeFloatParam(computeShader, RcpAtmosphericDepthId, materialParameters.rcpAtmosphericDepth);
            commandBuffer.SetComputeFloatParam(computeShader, CelestialLightExposureId, materialParameters.celestialLightExposure);
            commandBuffer.SetComputeFloatParam(computeShader, VolumetricCloudsBottomAltitudeId, materialParameters.volumetricCloudsBottomAltitude);
        }

        internal static void Apply(
            CommandBuffer commandBuffer,
            ComputeShader computeShader,
            in PhysicallyBasedSkyShaderParameters skyParameters,
            in PhysicallyBasedSkyMaterialParameters materialParameters)
        {
            if (commandBuffer == null || computeShader == null)
                return;

            commandBuffer.SetComputeMatrixParam(computeShader, PixelCoordToViewDirWSId, skyParameters.pixelCoordToViewDirWS);
            commandBuffer.SetComputeVectorParam(computeShader, SkySunDirectionId, skyParameters.skySunDirection);
            commandBuffer.SetComputeVectorParam(computeShader, SkySunColorId, skyParameters.skySunColor);
            commandBuffer.SetComputeVectorParam(computeShader, PlanetCenterRadiusId, materialParameters.planetCenterRadius);
            commandBuffer.SetComputeVectorParam(computeShader, PlanetUpAltitudeId, materialParameters.planetUpAltitude);
            commandBuffer.SetComputeFloatParam(computeShader, AtmosphericRadiusId, materialParameters.atmosphericRadius);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolAnisotropyId, materialParameters.aerosolAnisotropy);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolPhasePartConstantId, materialParameters.aerosolPhasePartConstant);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolSeaLevelExtinctionId, materialParameters.aerosolSeaLevelExtinction);
            commandBuffer.SetComputeFloatParam(computeShader, AirDensityFalloffId, materialParameters.airDensityFalloff);
            commandBuffer.SetComputeFloatParam(computeShader, AirScaleHeightId, materialParameters.airScaleHeight);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolDensityFalloffId, materialParameters.aerosolDensityFalloff);
            commandBuffer.SetComputeFloatParam(computeShader, AerosolScaleHeightId, materialParameters.aerosolScaleHeight);
            commandBuffer.SetComputeVectorParam(computeShader, OzoneScaleOffsetId, materialParameters.ozoneScaleOffset);
            commandBuffer.SetComputeFloatParam(computeShader, OzoneLayerStartId, materialParameters.ozoneLayerStart);
            commandBuffer.SetComputeFloatParam(computeShader, OzoneLayerEndId, materialParameters.ozoneLayerEnd);
            commandBuffer.SetComputeVectorParam(computeShader, AirSeaLevelExtinctionId, materialParameters.airSeaLevelExtinction);
            commandBuffer.SetComputeVectorParam(computeShader, AirSeaLevelScatteringId, materialParameters.airSeaLevelScattering);
            commandBuffer.SetComputeVectorParam(computeShader, AerosolSeaLevelScatteringId, materialParameters.aerosolSeaLevelScattering);
            commandBuffer.SetComputeVectorParam(computeShader, OzoneSeaLevelExtinctionId, materialParameters.ozoneSeaLevelExtinction);
            commandBuffer.SetComputeVectorParam(computeShader, GroundAlbedoPlanetRadiusId, materialParameters.groundAlbedoPlanetRadius);
            commandBuffer.SetComputeVectorParam(computeShader, HorizonTintId, materialParameters.horizonTint);
            commandBuffer.SetComputeVectorParam(computeShader, ZenithTintId, materialParameters.zenithTint);
            commandBuffer.SetComputeFloatParam(computeShader, IntensityMultiplierId, materialParameters.intensityMultiplier);
            commandBuffer.SetComputeFloatParam(computeShader, ColorSaturationId, materialParameters.colorSaturation);
            commandBuffer.SetComputeFloatParam(computeShader, AlphaSaturationId, materialParameters.alphaSaturation);
            commandBuffer.SetComputeFloatParam(computeShader, AlphaMultiplierId, materialParameters.alphaMultiplier);
            commandBuffer.SetComputeFloatParam(computeShader, HorizonZenithShiftPowerId, materialParameters.horizonZenithShiftPower);
            commandBuffer.SetComputeFloatParam(computeShader, HorizonZenithShiftScaleId, materialParameters.horizonZenithShiftScale);
            commandBuffer.SetComputeIntParam(computeShader, CelestialLightCountId, materialParameters.celestialLightCount);
            commandBuffer.SetComputeIntParam(computeShader, CelestialBodyCountId, materialParameters.celestialBodyCount);
            commandBuffer.SetComputeFloatParam(computeShader, AtmosphericDepthId, materialParameters.atmosphericDepth);
            commandBuffer.SetComputeFloatParam(computeShader, RcpAtmosphericDepthId, materialParameters.rcpAtmosphericDepth);
            commandBuffer.SetComputeFloatParam(computeShader, CelestialLightExposureId, materialParameters.celestialLightExposure);
            commandBuffer.SetComputeFloatParam(computeShader, VolumetricCloudsBottomAltitudeId, materialParameters.volumetricCloudsBottomAltitude);
        }
    }

    internal static class PhysicallyBasedSkyMaterialPropertyBinder
    {
        private static readonly int PBRSkyCameraPosPsId = Shader.PropertyToID("_PBRSkyCameraPosPS");
        private static readonly int PlanetCenterRadiusId = Shader.PropertyToID("_PlanetCenterRadius");
        private static readonly int PlanetUpAltitudeId = Shader.PropertyToID("_PlanetUpAltitude");
        private static readonly int AtmosphericRadiusId = Shader.PropertyToID("_AtmosphericRadius");
        private static readonly int AerosolAnisotropyId = Shader.PropertyToID("_AerosolAnisotropy");
        private static readonly int AerosolPhasePartConstantId = Shader.PropertyToID("_AerosolPhasePartConstant");
        private static readonly int AerosolSeaLevelExtinctionId = Shader.PropertyToID("_AerosolSeaLevelExtinction");
        private static readonly int AirDensityFalloffId = Shader.PropertyToID("_AirDensityFalloff");
        private static readonly int AirScaleHeightId = Shader.PropertyToID("_AirScaleHeight");
        private static readonly int AerosolDensityFalloffId = Shader.PropertyToID("_AerosolDensityFalloff");
        private static readonly int AerosolScaleHeightId = Shader.PropertyToID("_AerosolScaleHeight");
        private static readonly int OzoneScaleOffsetId = Shader.PropertyToID("_OzoneScaleOffset");
        private static readonly int OzoneLayerStartId = Shader.PropertyToID("_OzoneLayerStart");
        private static readonly int OzoneLayerEndId = Shader.PropertyToID("_OzoneLayerEnd");
        private static readonly int AirSeaLevelExtinctionId = Shader.PropertyToID("_AirSeaLevelExtinction");
        private static readonly int AirSeaLevelScatteringId = Shader.PropertyToID("_AirSeaLevelScattering");
        private static readonly int AerosolSeaLevelScatteringId = Shader.PropertyToID("_AerosolSeaLevelScattering");
        private static readonly int OzoneSeaLevelExtinctionId = Shader.PropertyToID("_OzoneSeaLevelExtinction");
        private static readonly int GroundAlbedoPlanetRadiusId = Shader.PropertyToID("_GroundAlbedo_PlanetRadius");
        private static readonly int HorizonTintId = Shader.PropertyToID("_HorizonTint");
        private static readonly int ZenithTintId = Shader.PropertyToID("_ZenithTint");
        private static readonly int IntensityMultiplierId = Shader.PropertyToID("_IntensityMultiplier");
        private static readonly int ColorSaturationId = Shader.PropertyToID("_ColorSaturation");
        private static readonly int AlphaSaturationId = Shader.PropertyToID("_AlphaSaturation");
        private static readonly int AlphaMultiplierId = Shader.PropertyToID("_AlphaMultiplier");
        private static readonly int HorizonZenithShiftPowerId = Shader.PropertyToID("_HorizonZenithShiftPower");
        private static readonly int HorizonZenithShiftScaleId = Shader.PropertyToID("_HorizonZenithShiftScale");
        private static readonly int CelestialLightCountId = Shader.PropertyToID("_CelestialLightCount");
        private static readonly int CelestialBodyCountId = Shader.PropertyToID("_CelestialBodyCount");
        private static readonly int AtmosphericDepthId = Shader.PropertyToID("_AtmosphericDepth");
        private static readonly int RcpAtmosphericDepthId = Shader.PropertyToID("_RcpAtmosphericDepth");
        private static readonly int CelestialLightExposureId = Shader.PropertyToID("_CelestialLightExposure");
        private static readonly int VolumetricCloudsBottomAltitudeId = Shader.PropertyToID("_VolumetricCloudsBottomAltitude");
        private static readonly int RenderSunDiskId = Shader.PropertyToID("_RenderSunDisk");
        private static readonly int HasGroundAlbedoTextureId = Shader.PropertyToID("_HasGroundAlbedoTexture");
        private static readonly int HasGroundEmissionTextureId = Shader.PropertyToID("_HasGroundEmissionTexture");
        private static readonly int HasSpaceEmissionTextureId = Shader.PropertyToID("_HasSpaceEmissionTexture");
        private static readonly int GroundEmissionMultiplierId = Shader.PropertyToID("_GroundEmissionMultiplier");
        private static readonly int SpaceEmissionMultiplierId = Shader.PropertyToID("_SpaceEmissionMultiplier");
        private static readonly int PlanetRotationId = Shader.PropertyToID("_PlanetRotation");
        private static readonly int SpaceRotationId = Shader.PropertyToID("_SpaceRotation");
        private static readonly int GroundAlbedoTextureId = Shader.PropertyToID("_GroundAlbedoTexture");
        private static readonly int GroundEmissionTextureId = Shader.PropertyToID("_GroundEmissionTexture");
        private static readonly int SpaceEmissionTextureId = Shader.PropertyToID("_SpaceEmissionTexture");

        internal static void Apply(
            MaterialPropertyBlock properties,
            in PhysicallyBasedSkyMaterialParameters parameters,
            PhysicallyBasedSkyVolume volume)
        {
            if (properties == null || volume == null)
                return;

            properties.SetVector(PBRSkyCameraPosPsId, parameters.pbrSkyCameraPositionPS);
            properties.SetVector(PlanetCenterRadiusId, parameters.planetCenterRadius);
            properties.SetVector(PlanetUpAltitudeId, parameters.planetUpAltitude);
            properties.SetFloat(AtmosphericRadiusId, parameters.atmosphericRadius);
            properties.SetFloat(AerosolAnisotropyId, parameters.aerosolAnisotropy);
            properties.SetFloat(AerosolPhasePartConstantId, parameters.aerosolPhasePartConstant);
            properties.SetFloat(AerosolSeaLevelExtinctionId, parameters.aerosolSeaLevelExtinction);
            properties.SetFloat(AirDensityFalloffId, parameters.airDensityFalloff);
            properties.SetFloat(AirScaleHeightId, parameters.airScaleHeight);
            properties.SetFloat(AerosolDensityFalloffId, parameters.aerosolDensityFalloff);
            properties.SetFloat(AerosolScaleHeightId, parameters.aerosolScaleHeight);
            properties.SetVector(OzoneScaleOffsetId, parameters.ozoneScaleOffset);
            properties.SetFloat(OzoneLayerStartId, parameters.ozoneLayerStart);
            properties.SetFloat(OzoneLayerEndId, parameters.ozoneLayerEnd);
            properties.SetVector(AirSeaLevelExtinctionId, parameters.airSeaLevelExtinction);
            properties.SetVector(AirSeaLevelScatteringId, parameters.airSeaLevelScattering);
            properties.SetVector(AerosolSeaLevelScatteringId, parameters.aerosolSeaLevelScattering);
            properties.SetVector(OzoneSeaLevelExtinctionId, parameters.ozoneSeaLevelExtinction);
            properties.SetVector(GroundAlbedoPlanetRadiusId, parameters.groundAlbedoPlanetRadius);
            properties.SetVector(HorizonTintId, parameters.horizonTint);
            properties.SetVector(ZenithTintId, parameters.zenithTint);
            properties.SetFloat(IntensityMultiplierId, parameters.intensityMultiplier);
            properties.SetFloat(ColorSaturationId, parameters.colorSaturation);
            properties.SetFloat(AlphaSaturationId, parameters.alphaSaturation);
            properties.SetFloat(AlphaMultiplierId, parameters.alphaMultiplier);
            properties.SetFloat(HorizonZenithShiftPowerId, parameters.horizonZenithShiftPower);
            properties.SetFloat(HorizonZenithShiftScaleId, parameters.horizonZenithShiftScale);
            properties.SetInt(CelestialLightCountId, parameters.celestialLightCount);
            properties.SetInt(CelestialBodyCountId, parameters.celestialBodyCount);
            properties.SetFloat(AtmosphericDepthId, parameters.atmosphericDepth);
            properties.SetFloat(RcpAtmosphericDepthId, parameters.rcpAtmosphericDepth);
            properties.SetFloat(CelestialLightExposureId, parameters.celestialLightExposure);
            properties.SetFloat(VolumetricCloudsBottomAltitudeId, parameters.volumetricCloudsBottomAltitude);
            properties.SetInt(RenderSunDiskId, parameters.renderSunDisk);

            var simpleEarthMode = volume.type.value == PhysicallyBasedSkyModel.EarthSimple;
            var planetRotation = Quaternion.Euler(volume.planetRotation.value);
            var spaceRotation = Quaternion.Euler(volume.spaceRotation.value);
            var planetRotationMatrix = Matrix4x4.Rotate(planetRotation);
            planetRotationMatrix[0] *= -1.0f;
            planetRotationMatrix[1] *= -1.0f;
            planetRotationMatrix[2] *= -1.0f;
            properties.SetMatrix(PlanetRotationId, planetRotationMatrix);
            properties.SetMatrix(SpaceRotationId, Matrix4x4.Rotate(spaceRotation));

            var hasGroundAlbedoTexture = volume.groundColorTexture.value != null && !simpleEarthMode;
            properties.SetInt(HasGroundAlbedoTextureId, hasGroundAlbedoTexture ? 1 : 0);
            if (hasGroundAlbedoTexture)
                properties.SetTexture(GroundAlbedoTextureId, volume.groundColorTexture.value);

            var hasGroundEmissionTexture = volume.groundEmissionTexture.value != null && !simpleEarthMode;
            properties.SetInt(HasGroundEmissionTextureId, hasGroundEmissionTexture ? 1 : 0);
            properties.SetFloat(GroundEmissionMultiplierId, volume.groundEmissionMultiplier.value);
            if (hasGroundEmissionTexture)
                properties.SetTexture(GroundEmissionTextureId, volume.groundEmissionTexture.value);

            var hasSpaceEmissionTexture = volume.spaceEmissionTexture.value != null && !simpleEarthMode;
            properties.SetInt(HasSpaceEmissionTextureId, hasSpaceEmissionTexture ? 1 : 0);
            properties.SetFloat(SpaceEmissionMultiplierId, volume.spaceEmissionMultiplier.value);
            if (hasSpaceEmissionTexture)
                properties.SetTexture(SpaceEmissionTextureId, volume.spaceEmissionTexture.value);
        }
    }
}
