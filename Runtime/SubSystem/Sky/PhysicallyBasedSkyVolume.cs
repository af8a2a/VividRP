using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum PhysicallyBasedSkyModel
    {
        EarthSimple = 0,
        Custom = 1
    }
    

    [Serializable]
    [VolumeComponentMenu("VividRP/Physically Based Sky")]
    public sealed class PhysicallyBasedSkyVolume : VolumeComponent
    {
        private const float DefaultEarthRadius = 6.3781f * 1000000.0f;
        private const float DefaultAirScaleHeight = 8000.0f;
        private const float DefaultAerosolScaleHeight = 1200.0f;
        private const float DefaultAirScatteringR = 5.8f / 1000000.0f;
        private const float DefaultAirScatteringG = 13.5f / 1000000.0f;
        private const float DefaultAirScatteringB = 33.1f / 1000000.0f;

        public EnumParameter<PhysicallyBasedSkyModel> type = new(PhysicallyBasedSkyModel.EarthSimple);

        [Header("Planet")]
        public MinFloatParameter planetRadius = new(DefaultEarthRadius, 1000.0f);

        [Header("Air")]
        public MinFloatParameter airMaximumAltitude = new(LayerDepthFromScaleHeight(DefaultAirScaleHeight), 0.0f);
        public ClampedFloatParameter airDensityR = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringR, DefaultAirScaleHeight), 0.0f, 1.0f);
        public ClampedFloatParameter airDensityG = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringG, DefaultAirScaleHeight), 0.0f, 1.0f);
        public ClampedFloatParameter airDensityB = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringB, DefaultAirScaleHeight), 0.0f, 1.0f);
        public ColorParameter airTint = new(Color.white, false, false, true);

        [Header("Aerosol")]
        public MinFloatParameter aerosolMaximumAltitude = new(LayerDepthFromScaleHeight(DefaultAerosolScaleHeight), 0.0f);
        public ClampedFloatParameter aerosolDensity = new(ZenithOpacityFromExtinctionAndScaleHeight(10.0f / 1000000.0f, DefaultAerosolScaleHeight), 0.0f, 1.0f);
        public ColorParameter aerosolTint = new(new Color(0.9f, 0.9f, 0.9f), false, false, true);
        public ClampedFloatParameter aerosolAnisotropy = new(0.8f, -1.0f, 1.0f);

        [Header("Ozone")]
        public ClampedFloatParameter ozoneDensityDimmer = new(1.0f, 0.0f, 1.0f);
        public MinFloatParameter ozoneMinimumAltitude = new(20000.0f, 0.0f);
        public MinFloatParameter ozoneLayerWidth = new(20000.0f, 0.0f);

        [Header("Ground")]
        public ColorParameter groundTint = new(new Color(0.12f, 0.10f, 0.09f), false, false, false);

        [Header("Rendering")]
        public MinFloatParameter exposure = new(1.0f, 0.0f);
        public BoolParameter renderSunDisk = new(true);
        public MinFloatParameter sunDiskSize = new(1.0f, 0.0f);

        [Header("Height Fog")]
        public BoolParameter enableHeightFog = new(false);
        public FloatParameter fogBaseHeight = new(0.0f);
        public MinFloatParameter fogDensity = new(0.01f, 0.0f);
        public MinFloatParameter fogMaxDistance = new(5000.0f, 0.0f);

        public bool IsActive()
        {
            return airMaximumAltitude.value > 0.0f
                && (airDensityR.value > 0.0f || airDensityG.value > 0.0f || airDensityB.value > 0.0f || aerosolDensity.value > 0.0f);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + type.GetHashCode();
                hash = hash * 23 + planetRadius.GetHashCode();
                hash = hash * 23 + airMaximumAltitude.GetHashCode();
                hash = hash * 23 + airDensityR.GetHashCode();
                hash = hash * 23 + airDensityG.GetHashCode();
                hash = hash * 23 + airDensityB.GetHashCode();
                hash = hash * 23 + airTint.GetHashCode();
                hash = hash * 23 + aerosolMaximumAltitude.GetHashCode();
                hash = hash * 23 + aerosolDensity.GetHashCode();
                hash = hash * 23 + aerosolTint.GetHashCode();
                hash = hash * 23 + aerosolAnisotropy.GetHashCode();
                hash = hash * 23 + ozoneDensityDimmer.GetHashCode();
                hash = hash * 23 + ozoneMinimumAltitude.GetHashCode();
                hash = hash * 23 + ozoneLayerWidth.GetHashCode();
                hash = hash * 23 + groundTint.GetHashCode();
                hash = hash * 23 + exposure.GetHashCode();
                hash = hash * 23 + renderSunDisk.GetHashCode();
                hash = hash * 23 + sunDiskSize.GetHashCode();
                hash = hash * 23 + enableHeightFog.GetHashCode();
                hash = hash * 23 + fogBaseHeight.GetHashCode();
                hash = hash * 23 + fogDensity.GetHashCode();
                hash = hash * 23 + fogMaxDistance.GetHashCode();
                return hash;
            }
        }

        internal bool IsHeightFogActive()
        {
            return enableHeightFog.value
                && fogDensity.value > 0.0f
                && fogMaxDistance.value > 0.0f;
        }

        internal float GetPreExposureMultiplier()
        {
            return Mathf.Min(Mathf.Max(exposure.value, 0.0f), 1.0f);
        }

        internal float GetPostExposureMultiplier()
        {
            var totalExposure = Mathf.Max(exposure.value, 0.0f);
            var preExposure = GetPreExposureMultiplier();
            return preExposure > 0.0f ? totalExposure / preExposure : 0.0f;
        }

        internal float GetAirScaleHeight()
        {
            return type.value == PhysicallyBasedSkyModel.Custom
                ? ScaleHeightFromLayerDepth(airMaximumAltitude.value)
                : DefaultAirScaleHeight;
        }

        internal Vector3 GetAirExtinctionCoefficient()
        {
            if (type.value != PhysicallyBasedSkyModel.Custom)
                return new Vector3(DefaultAirScatteringR, DefaultAirScatteringG, DefaultAirScatteringB);

            var scaleHeight = GetAirScaleHeight();
            return new Vector3(
                ExtinctionFromZenithOpacityAndScaleHeight(airDensityR.value, scaleHeight),
                ExtinctionFromZenithOpacityAndScaleHeight(airDensityG.value, scaleHeight),
                ExtinctionFromZenithOpacityAndScaleHeight(airDensityB.value, scaleHeight));
        }

        internal Vector3 GetAirScatteringCoefficient()
        {
            var extinction = GetAirExtinctionCoefficient();
            return new Vector3(
                extinction.x * airTint.value.r,
                extinction.y * airTint.value.g,
                extinction.z * airTint.value.b);
        }

        internal float GetAerosolScaleHeight()
        {
            return type.value == PhysicallyBasedSkyModel.Custom
                ? ScaleHeightFromLayerDepth(aerosolMaximumAltitude.value)
                : DefaultAerosolScaleHeight;
        }

        internal float GetAerosolExtinctionCoefficient()
        {
            return ExtinctionFromZenithOpacityAndScaleHeight(aerosolDensity.value, GetAerosolScaleHeight());
        }

        internal Vector3 GetAerosolScatteringCoefficient()
        {
            var extinction = GetAerosolExtinctionCoefficient();
            return new Vector3(
                extinction * aerosolTint.value.r,
                extinction * aerosolTint.value.g,
                extinction * aerosolTint.value.b);
        }

        internal Vector3 GetOzoneExtinctionCoefficient()
        {
            return ozoneDensityDimmer.value * new Vector3(0.00065f, 0.00188f, 0.00008f) / 1000.0f;
        }

        internal float GetAtmosphereRadius()
        {
            var ozoneTop = ozoneMinimumAltitude.value + ozoneLayerWidth.value;
            return planetRadius.value + Mathf.Max(Mathf.Max(airMaximumAltitude.value, aerosolMaximumAltitude.value), ozoneTop);
        }

        internal static float ScaleHeightFromLayerDepth(float depth)
        {
            return depth * 0.144765f;
        }

        internal static float LayerDepthFromScaleHeight(float scaleHeight)
        {
            return scaleHeight / 0.144765f;
        }

        internal static float ExtinctionFromZenithOpacityAndScaleHeight(float opacity, float scaleHeight)
        {
            var saturatedOpacity = Mathf.Min(opacity, 0.999999f);
            var opticalDepth = -Mathf.Log(1.0f - saturatedOpacity);
            return opticalDepth / Mathf.Max(scaleHeight, 1.0f);
        }

        internal static float ZenithOpacityFromExtinctionAndScaleHeight(float extinction, float scaleHeight)
        {
            return 1.0f - Mathf.Exp(-extinction * scaleHeight);
        }
    }
}
