using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum RenderingSpace
    {
        Camera,
        World
    }

    public enum PlanetMode
    {
        Automatic,
        Manual
    }

    public enum SkyGeneratedCubemapQuality
    {
        PlatformDefault = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4
    }
    /// <summary>
    /// Sky Intensity Mode.
    /// </summary>
    public enum SkyIntensityMode
    {
        /// <summary>Intensity is expressed as an exposure.</summary>
        Exposure,
        /// <summary>Intensity is expressed in lux.</summary>
        Lux,
        /// <summary>Intensity is expressed as a multiplier.</summary>
        Multiplier,
    }

    [Serializable]
    public sealed class SkyIntensityParameter : VolumeParameter<SkyIntensityMode>
    {
        public SkyIntensityParameter(SkyIntensityMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    internal static class SkyIntensityUtility
    {
        internal static float GetExposureMultiplier(float exposureValue)
        {
            return ColorUtils.ConvertEV100ToExposure(-exposureValue);
        }

        internal static float GetIntensityFromSettings(
            SkyIntensityMode intensityMode,
            float exposureValue,
            float multiplierValue,
            float upperHemisphereLux,
            float desiredLux)
        {
            var skyIntensity = 1.0f;
            switch (intensityMode)
            {
                case SkyIntensityMode.Exposure:
                    skyIntensity *= GetExposureMultiplier(exposureValue);
                    break;
                case SkyIntensityMode.Multiplier:
                    skyIntensity *= multiplierValue;
                    break;
                case SkyIntensityMode.Lux:
                    skyIntensity *= desiredLux / Mathf.Max(upperHemisphereLux, 1e-5f);
                    break;
            }

            return skyIntensity;
        }
    }

    [Serializable]
    [VolumeComponentMenu("VividRP/Sky Settings")]
    public sealed class SkySettingsVolume : VolumeComponent
    {
        internal const float DefaultEarthRadius = 6.3781f * 1000000.0f;

        public EnumParameter<SkyType> skyType = new(SkyType.HDRI);
        public EnumParameter<SkyUpdateMode> updateMode = new(SkyUpdateMode.OnChanged);
        public MinFloatParameter updatePeriod = new(0.0f, 0.0f);
        [Tooltip("When enabled, VividRP uses the Sun Disk in baked lighting.")]
        public BoolParameter includeSunInBaking = new(false);
        public EnumParameter<SkyGeneratedCubemapQuality> generatedCubemapQuality = new(SkyGeneratedCubemapQuality.PlatformDefault);
        public EnumParameter<RenderingSpace> renderingSpace = new(RenderingSpace.World);
        [AdditionalProperty]
        public EnumParameter<PlanetMode> centerMode = new(PlanetMode.Automatic);
        [AdditionalProperty]
        public Vector3Parameter planetCenter = new(new Vector3(0.0f, -DefaultEarthRadius, 0.0f));

        protected override void OnEnable()
        {
            skyType ??= new EnumParameter<SkyType>(SkyType.HDRI);
            updateMode ??= new EnumParameter<SkyUpdateMode>(SkyUpdateMode.OnChanged);
            updatePeriod ??= new MinFloatParameter(0.0f, 0.0f);
            includeSunInBaking ??= new BoolParameter(false);
            generatedCubemapQuality ??= new EnumParameter<SkyGeneratedCubemapQuality>(SkyGeneratedCubemapQuality.PlatformDefault);
            renderingSpace ??= new EnumParameter<RenderingSpace>(RenderingSpace.World);
            centerMode ??= new EnumParameter<PlanetMode>(PlanetMode.Automatic);
            planetCenter ??= new Vector3Parameter(new Vector3(0.0f, -DefaultEarthRadius, 0.0f));
            base.OnEnable();
        }

        internal static RenderingSpace GetRenderingSpace(SkySettingsVolume settings = null)
        {
            return settings?.renderingSpace?.value ?? RenderingSpace.World;
        }

        internal static PlanetMode GetPlanetCenterMode(SkySettingsVolume settings = null)
        {
            return settings?.centerMode?.value ?? PlanetMode.Automatic;
        }

        internal static bool GetIncludeSunInBaking(SkySettingsVolume settings = null)
        {
            return settings?.includeSunInBaking?.value ?? false;
        }

        internal static Vector3 GetPlanetCenter(SkySettingsVolume settings = null, float planetRadius = DefaultEarthRadius)
        {
            if (GetPlanetCenterMode(settings) == PlanetMode.Manual)
                return settings?.planetCenter?.value ?? new Vector3(0.0f, -planetRadius, 0.0f);

            return new Vector3(0.0f, -planetRadius, 0.0f);
        }


        internal static int GetGeneratedCubemapViewSampleCount(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 8,
                SkyGeneratedCubemapQuality.High => 16,
                SkyGeneratedCubemapQuality.Ultra => 24,
                _ => 12
            };
        }

        internal static int GetGeneratedCubemapLightSampleCount(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 4,
                SkyGeneratedCubemapQuality.High => 8,
                SkyGeneratedCubemapQuality.Ultra => 12,
                _ => 6
            };
        }

        internal static int GetGeneratedCubemapResolution(SkySettingsVolume settings = null)
        {
            var quality = settings?.generatedCubemapQuality?.value ?? SkyGeneratedCubemapQuality.PlatformDefault;
            return quality switch
            {
                SkyGeneratedCubemapQuality.Low => 16,
                SkyGeneratedCubemapQuality.High => 64,
                SkyGeneratedCubemapQuality.Ultra => 128,
                _ => 32
            };
        }
    }

    internal readonly struct SkyPlanet
    {
        private const float MinimumPlanetRadius = 1000.0f;
        private const float MinimumCameraAltitude = 1.0f;

        internal SkyPlanet(float radius, Vector3 center, RenderingSpace renderingSpace)
        {
            this.radius = radius;
            this.center = center;
            this.renderingSpace = renderingSpace;
        }

        internal float radius { get; }

        internal Vector3 center { get; }

        internal RenderingSpace renderingSpace { get; }

        internal static SkyPlanet Resolve(
            PhysicallyBasedSkyVolume volume,
            SkySettingsVolume settings,
            Vector3 cameraPositionWS)
        {
            var planetRadius = Mathf.Max(volume?.planetRadius.value ?? SkySettingsVolume.DefaultEarthRadius, MinimumPlanetRadius);
            return Resolve(planetRadius, settings, cameraPositionWS);
        }

        internal static SkyPlanet Resolve(
            float planetRadius,
            SkySettingsVolume settings,
            Vector3 cameraPositionWS)
        {
            var radius = Mathf.Max(planetRadius, MinimumPlanetRadius);
            var renderingSpace = SkySettingsVolume.GetRenderingSpace(settings);
            var center = ResolveCenter(radius, renderingSpace, settings, cameraPositionWS);
            return new SkyPlanet(radius, center, renderingSpace);
        }

        internal Vector4 GetPlanetCenterRadius()
        {
            return new Vector4(center.x, center.y, center.z, radius);
        }

        internal Vector4 GetPlanetUpAltitude(Vector3 cameraPositionWS)
        {
            var cameraToPlanetCenter = cameraPositionWS - center;
            if (cameraToPlanetCenter.sqrMagnitude <= 1e-6f)
                cameraToPlanetCenter = Vector3.up * (radius + MinimumCameraAltitude);

            var radialDistance = cameraToPlanetCenter.magnitude;
            var planetUp = cameraToPlanetCenter / radialDistance;
            var altitude = radialDistance - radius;

            return new Vector4(planetUp.x, planetUp.y, planetUp.z, altitude);
        }

        internal Vector3 GetCameraPositionPS(Vector3 cameraPositionWS)
        {
            var planetUpAltitude = GetPlanetUpAltitude(cameraPositionWS);
            var cameraPositionPS = cameraPositionWS - center;
            if (planetUpAltitude.w < MinimumCameraAltitude)
            {
                var planetUp = new Vector3(planetUpAltitude.x, planetUpAltitude.y, planetUpAltitude.z);
                cameraPositionPS -= (planetUpAltitude.w - MinimumCameraAltitude) * planetUp;
            }

            return cameraPositionPS;
        }

        internal int ComputeHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + radius.GetHashCode();
                hash = hash * 23 + renderingSpace.GetHashCode();
                if (renderingSpace != RenderingSpace.Camera)
                    hash = hash * 23 + center.GetHashCode();
                return hash;
            }
        }

        private static Vector3 ResolveCenter(
            float planetRadius,
            RenderingSpace renderingSpace,
            SkySettingsVolume settings,
            Vector3 cameraPositionWS)
        {
            if (renderingSpace == RenderingSpace.Camera)
                return cameraPositionWS + new Vector3(0.0f, -planetRadius, 0.0f);

            return SkySettingsVolume.GetPlanetCenter(settings, planetRadius);
        }
    }
}
