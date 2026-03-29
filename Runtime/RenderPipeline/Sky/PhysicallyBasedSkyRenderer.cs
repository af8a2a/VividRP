using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal sealed class PhysicallyBasedSkyRenderer : ISkyRenderer
    {
        private const int CubemapResolution = 64;
        private const int ViewSampleCount = 12;
        private const int LightSampleCount = 6;
        private const float ObserverHeight = 2.0f;
        private const float SunAngularDiameterDegrees = 0.53f;
        private const float SunIlluminanceScale = 20.0f;

        private Cubemap m_RuntimeSkyCubemap;
        private int m_RuntimeSkyHash;

        public SkyType Type => SkyType.PhysicallyBased;

        public void Build(VividRPCoreResources resources)
        {
        }

        public bool IsActive()
        {
            return VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume()?.IsActive() ?? false;
        }

        public int GetSkyHash(in SkyRendererContext context)
        {
            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null)
                return 0;

            return HashCode.Combine(
                volume.GetHashCode(),
                ResolveCameraPosition(context, volume.planetRadius.value),
                ResolveSunDirection(context),
                ResolveSunColor(context));
        }

        public void Update(in SkyRendererContext context, VividSkyData skyData)
        {
            if (skyData == null)
                return;

            var volume = VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume();
            if (volume == null || !volume.IsActive())
            {
                skyData.Reset();
                return;
            }

            var hash = GetSkyHash(context);
            if (m_RuntimeSkyCubemap == null || m_RuntimeSkyHash != hash)
            {
                EnsureRuntimeCubemap();
                RebuildRuntimeCubemap(volume, context);
                m_RuntimeSkyHash = hash;
            }

            skyData.activeSkyType = SkyType.PhysicallyBased;
            skyData.specularCubemap = m_RuntimeSkyCubemap;
            skyData.tint = Color.white;
            skyData.exposure = volume.exposure.value;
            skyData.rotation = 0.0f;
            skyData.hasDiffuseSH = SkyDiffuseSHUtility.TryProjectCubemapToSH(
                m_RuntimeSkyCubemap,
                Color.white,
                1.0f,
                0.0f,
                out skyData.diffuseSH);
        }

        public void Dispose()
        {
            if (m_RuntimeSkyCubemap != null)
            {
                CoreUtils.Destroy(m_RuntimeSkyCubemap);
                m_RuntimeSkyCubemap = null;
            }

            m_RuntimeSkyHash = 0;
        }

        private void EnsureRuntimeCubemap()
        {
            if (m_RuntimeSkyCubemap != null)
                return;

            m_RuntimeSkyCubemap = new Cubemap(CubemapResolution, TextureFormat.RGBAHalf, true)
            {
                name = "VividPhysicallyBasedSky",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void RebuildRuntimeCubemap(PhysicallyBasedSkyVolume volume, in SkyRendererContext context)
        {
            var cameraPosition = ResolveCameraPosition(context, volume.planetRadius.value);

            foreach (var face in SkyDiffuseSHUtility.ValidCubemapFaces)
            {
                var colors = new Color[CubemapResolution * CubemapResolution];
                for (var y = 0; y < CubemapResolution; y++)
                {
                    for (var x = 0; x < CubemapResolution; x++)
                    {
                        var direction = SkyDiffuseSHUtility.GetDirectionForCubemapFace(face, x, y, CubemapResolution);
                        colors[(y * CubemapResolution) + x] = EvaluateSky(direction, cameraPosition, volume, context);
                    }
                }

                m_RuntimeSkyCubemap.SetPixels(colors, face);
            }

            m_RuntimeSkyCubemap.Apply(true, false);
        }

        private static Color EvaluateSky(Vector3 direction, Vector3 cameraPosition, PhysicallyBasedSkyVolume volume, in SkyRendererContext context)
        {
            var normalizedDirection = direction.normalized;
            var planetRadius = Mathf.Max(volume.planetRadius.value, 1000.0f);
            var atmosphereRadius = Mathf.Max(volume.GetAtmosphereRadius(), planetRadius + 1.0f);
            var sunDirection = ResolveSunDirection(context);
            var sunColor = ToVector3(ResolveSunColor(context)) * SunIlluminanceScale;
            var airScattering = volume.GetAirScatteringCoefficient();
            var airExtinction = volume.GetAirExtinctionCoefficient();
            var aerosolExtinctionScalar = volume.GetAerosolExtinctionCoefficient();
            var aerosolScattering = volume.GetAerosolScatteringCoefficient();
            var aerosolExtinction = new Vector3(aerosolExtinctionScalar, aerosolExtinctionScalar, aerosolExtinctionScalar);
            var ozoneExtinction = volume.GetOzoneExtinctionCoefficient();
            var g = Mathf.Clamp(volume.aerosolAnisotropy.value, -0.95f, 0.95f);

            if (!IntersectAtmosphere(cameraPosition, normalizedDirection, atmosphereRadius, out _, out var atmosphereExit))
                return Color.black;

            var rayLength = atmosphereExit;
            if (IntersectGround(cameraPosition, normalizedDirection, planetRadius, out var groundHit) && groundHit > 0.0f)
                rayLength = Mathf.Min(rayLength, groundHit);

            var stepLength = rayLength / ViewSampleCount;
            if (stepLength <= 0.0f)
                return Color.black;

            var opticalDepthAir = 0.0f;
            var opticalDepthAerosol = 0.0f;
            var opticalDepthOzone = Vector3.zero;
            var mu = Mathf.Clamp(Vector3.Dot(normalizedDirection, sunDirection), -1.0f, 1.0f);
            var phaseRayleigh = 3.0f / (16.0f * Mathf.PI) * (1.0f + mu * mu);
            var phaseMieNumerator = 3.0f / (8.0f * Mathf.PI) * (1.0f - g * g) * (1.0f + mu * mu);
            var phaseMieDenominator = (2.0f + g * g) * Mathf.Pow(Mathf.Max(1.0f + g * g - 2.0f * g * mu, 1e-3f), 1.5f);
            var phaseMie = phaseMieNumerator / Mathf.Max(phaseMieDenominator, 1e-3f);
            var inscattered = Vector3.zero;

            for (var sampleIndex = 0; sampleIndex < ViewSampleCount; sampleIndex++)
            {
                var sampleDistance = (sampleIndex + 0.5f) * stepLength;
                var samplePosition = cameraPosition + normalizedDirection * sampleDistance;
                var height = Mathf.Max(samplePosition.magnitude - planetRadius, 0.0f);
                var localAirDensity = Mathf.Exp(-height / Mathf.Max(volume.GetAirScaleHeight(), 1.0f));
                var localAerosolDensity = Mathf.Exp(-height / Mathf.Max(volume.GetAerosolScaleHeight(), 1.0f));
                var localOzoneDensity = EvaluateOzoneDensity(height, volume.ozoneMinimumAltitude.value, volume.ozoneLayerWidth.value);

                opticalDepthAir += localAirDensity * stepLength;
                opticalDepthAerosol += localAerosolDensity * stepLength;
                opticalDepthOzone += ozoneExtinction * (localOzoneDensity * stepLength);

                var sunOpticalDepth = ComputeOpticalDepthToSun(samplePosition, sunDirection, planetRadius, atmosphereRadius, volume);
                var viewTransmittance = EvaluateTransmittance(airExtinction, aerosolExtinction, ozoneExtinction, opticalDepthAir, opticalDepthAerosol, opticalDepthOzone);
                var sunTransmittance = EvaluateTransmittance(airExtinction, aerosolExtinction, ozoneExtinction, sunOpticalDepth.air, sunOpticalDepth.aerosol, sunOpticalDepth.ozone);
                var scattering = Vector3.Scale(airScattering, Vector3.one * (localAirDensity * phaseRayleigh))
                                 + Vector3.Scale(aerosolScattering, Vector3.one * (localAerosolDensity * phaseMie));
                var attenuation = Vector3.Scale(viewTransmittance, sunTransmittance);
                inscattered += new Vector3(
                    attenuation.x * scattering.x,
                    attenuation.y * scattering.y,
                    attenuation.z * scattering.z) * stepLength;
            }

            var skyColor = new Vector3(
                inscattered.x * sunColor.x,
                inscattered.y * sunColor.y,
                inscattered.z * sunColor.z);

            if (rayLength < atmosphereExit)
            {
                var groundTransmittance = EvaluateTransmittance(airExtinction, aerosolExtinction, ozoneExtinction, opticalDepthAir, opticalDepthAerosol, opticalDepthOzone);
                var groundLighting = ToVector3(volume.groundTint.value.linear) * Mathf.Max(0.15f, sunDirection.y * 0.5f + 0.5f);
                skyColor += Vector3.Scale(groundLighting, groundTransmittance);
            }

            if (volume.renderSunDisk.value)
            {
                var sunAngularRadius = Mathf.Deg2Rad * SunAngularDiameterDegrees * Mathf.Max(volume.sunDiskSize.value, 0.01f) * 0.5f;
                var sunCosThreshold = Mathf.Cos(sunAngularRadius);
                var sunEdge = Mathf.InverseLerp(sunCosThreshold - 0.0025f, sunCosThreshold, Mathf.Clamp(Vector3.Dot(normalizedDirection, sunDirection), -1.0f, 1.0f));
                if (sunEdge > 0.0f)
                {
                    var sunTransmittance = ComputeSunDiskTransmittance(cameraPosition, sunDirection, planetRadius, atmosphereRadius, volume);
                    skyColor += Vector3.Scale(sunColor, sunTransmittance) * Mathf.SmoothStep(0.0f, 1.0f, sunEdge) * 2.0f;
                }
            }

            return new Color(
                Mathf.Max(0.0f, skyColor.x),
                Mathf.Max(0.0f, skyColor.y),
                Mathf.Max(0.0f, skyColor.z),
                1.0f);
        }

        private static Vector3 ResolveSunDirection(in SkyRendererContext context)
        {
            if (context.lightData != null && context.lightData.hasMainDirectionalLight)
                return context.lightData.mainDirectionalLight.directionWS.normalized;

            if (RenderSettings.sun != null)
                return (-RenderSettings.sun.transform.forward).normalized;

            return Vector3.up;
        }

        private static Color ResolveSunColor(in SkyRendererContext context)
        {
            if (context.lightData != null && context.lightData.hasMainDirectionalLight)
            {
                var color = context.lightData.mainDirectionalLight.color;
                return new Color(color.x, color.y, color.z, 1.0f);
            }

            if (RenderSettings.sun != null)
                return RenderSettings.sun.color.linear * Mathf.Max(RenderSettings.sun.intensity, 0.0f);

            return Color.white;
        }

        private static Vector3 ResolveCameraPosition(in SkyRendererContext context, float planetRadius)
        {
            var camera = context.cameraData?.camera;
            if (camera == null)
                return new Vector3(0.0f, planetRadius + ObserverHeight, 0.0f);

            var worldPosition = camera.transform.position;
            return new Vector3(
                worldPosition.x,
                Mathf.Max(worldPosition.y + planetRadius, planetRadius + 0.1f),
                worldPosition.z);
        }

        private static bool IntersectAtmosphere(Vector3 origin, Vector3 direction, float atmosphereRadius, out float entry, out float exit)
        {
            var b = Vector3.Dot(origin, direction);
            var c = Vector3.Dot(origin, origin) - atmosphereRadius * atmosphereRadius;
            var discriminant = b * b - c;
            if (discriminant < 0.0f)
            {
                entry = 0.0f;
                exit = 0.0f;
                return false;
            }

            var sqrtDiscriminant = Mathf.Sqrt(discriminant);
            entry = -b - sqrtDiscriminant;
            exit = -b + sqrtDiscriminant;
            return exit > 0.0f;
        }

        private static bool IntersectGround(Vector3 origin, Vector3 direction, float planetRadius, out float distance)
        {
            var b = Vector3.Dot(origin, direction);
            var c = Vector3.Dot(origin, origin) - planetRadius * planetRadius;
            var discriminant = b * b - c;
            if (discriminant < 0.0f)
            {
                distance = 0.0f;
                return false;
            }

            var sqrtDiscriminant = Mathf.Sqrt(discriminant);
            distance = -b - sqrtDiscriminant;
            return distance > 0.0f;
        }

        private static (float air, float aerosol, Vector3 ozone) ComputeOpticalDepthToSun(
            Vector3 samplePosition,
            Vector3 sunDirection,
            float planetRadius,
            float atmosphereRadius,
            PhysicallyBasedSkyVolume volume)
        {
            if (!IntersectAtmosphere(samplePosition, sunDirection, atmosphereRadius, out _, out var atmosphereExit))
                return default;

            if (IntersectGround(samplePosition, sunDirection, planetRadius, out var groundHit) && groundHit > 0.0f && groundHit < atmosphereExit)
            {
                const float blockedDepth = 100000.0f;
                return (blockedDepth, blockedDepth, Vector3.one * blockedDepth);
            }

            var stepLength = atmosphereExit / LightSampleCount;
            var opticalDepthAir = 0.0f;
            var opticalDepthAerosol = 0.0f;
            var opticalDepthOzone = Vector3.zero;

            for (var sampleIndex = 0; sampleIndex < LightSampleCount; sampleIndex++)
            {
                var sampleDistance = (sampleIndex + 0.5f) * stepLength;
                var lightSamplePosition = samplePosition + sunDirection * sampleDistance;
                var height = Mathf.Max(lightSamplePosition.magnitude - planetRadius, 0.0f);
                opticalDepthAir += Mathf.Exp(-height / Mathf.Max(volume.GetAirScaleHeight(), 1.0f)) * stepLength;
                opticalDepthAerosol += Mathf.Exp(-height / Mathf.Max(volume.GetAerosolScaleHeight(), 1.0f)) * stepLength;
                opticalDepthOzone += volume.GetOzoneExtinctionCoefficient() * (EvaluateOzoneDensity(height, volume.ozoneMinimumAltitude.value, volume.ozoneLayerWidth.value) * stepLength);
            }

            return (opticalDepthAir, opticalDepthAerosol, opticalDepthOzone);
        }

        private static Vector3 ComputeSunDiskTransmittance(
            Vector3 cameraPosition,
            Vector3 sunDirection,
            float planetRadius,
            float atmosphereRadius,
            PhysicallyBasedSkyVolume volume)
        {
            var opticalDepth = ComputeOpticalDepthToSun(cameraPosition, sunDirection, planetRadius, atmosphereRadius, volume);
            var airExtinction = volume.GetAirExtinctionCoefficient();
            var aerosolExtinctionScalar = volume.GetAerosolExtinctionCoefficient();
            var aerosolExtinction = new Vector3(aerosolExtinctionScalar, aerosolExtinctionScalar, aerosolExtinctionScalar);
            return EvaluateTransmittance(airExtinction, aerosolExtinction, volume.GetOzoneExtinctionCoefficient(), opticalDepth.air, opticalDepth.aerosol, opticalDepth.ozone);
        }

        private static Vector3 EvaluateTransmittance(
            Vector3 airExtinction,
            Vector3 aerosolExtinction,
            Vector3 ozoneExtinction,
            float opticalDepthAir,
            float opticalDepthAerosol,
            Vector3 opticalDepthOzone)
        {
            return new Vector3(
                Mathf.Exp(-(airExtinction.x * opticalDepthAir + aerosolExtinction.x * opticalDepthAerosol + opticalDepthOzone.x)),
                Mathf.Exp(-(airExtinction.y * opticalDepthAir + aerosolExtinction.y * opticalDepthAerosol + opticalDepthOzone.y)),
                Mathf.Exp(-(airExtinction.z * opticalDepthAir + aerosolExtinction.z * opticalDepthAerosol + opticalDepthOzone.z)));
        }

        private static float EvaluateOzoneDensity(float height, float minimumAltitude, float layerWidth)
        {
            if (layerWidth <= 0.0f)
                return 0.0f;

            var normalizedHeight = (height - minimumAltitude) / layerWidth;
            return Mathf.Clamp01(1.0f - Mathf.Abs(normalizedHeight * 2.0f - 1.0f));
        }

        private static Vector3 ToVector3(Color color)
        {
            return new Vector3(color.r, color.g, color.b);
        }
    }
}
