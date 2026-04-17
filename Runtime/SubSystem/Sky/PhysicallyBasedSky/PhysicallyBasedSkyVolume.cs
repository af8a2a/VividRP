using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    public enum PhysicallyBasedSkyModel
    {
        EarthSimple = 0,
        Custom = 1,
        EarthAdvanced = 2
    }

    public enum PhysicallyBasedSkyRenderingMode
    {
        Default = 0,
        Material = 1
    }
    

    [Serializable]
    [VolumeComponentMenu("Sky/Physically Based Sky")]
    public sealed class PhysicallyBasedSkyVolume : VolumeComponent
    {
        private const float DefaultEarthRadius = 6.3781f * 1000000.0f;
        private const float DefaultAirScaleHeight = 8000.0f;
        private const float DefaultAerosolScaleHeight = 1200.0f;
        private const float DefaultAirScatteringR = 5.8f / 1000000.0f;
        private const float DefaultAirScatteringG = 13.5f / 1000000.0f;
        private const float DefaultAirScatteringB = 33.1f / 1000000.0f;
        private static readonly float DefaultAerosolMaximumAltitude = LayerDepthFromScaleHeight(DefaultAerosolScaleHeight);
        private const float DefaultOzoneMinimumAltitude = 20.0f * 1000.0f;
        private const float DefaultOzoneLayerWidth = 20.0f * 1000.0f;

        [Tooltip("Indicates a preset VividRP uses to simplify the Inspector.")]
        public EnumParameter<PhysicallyBasedSkyModel> type = new(PhysicallyBasedSkyModel.EarthAdvanced);

        [Tooltip("Enables atmospheric attenuation on objects when viewed from a distance.")]
        public BoolParameter atmosphericScattering = new(true);

        [Header("Material")]
        [Tooltip("Indicates whether VividRP should use the default shader parameters or a custom material for planet and space rendering.")]
        public EnumParameter<PhysicallyBasedSkyRenderingMode> renderingMode = new(PhysicallyBasedSkyRenderingMode.Default);

        [Tooltip("The custom material used to render the sky when Material mode is selected.")]
        public MaterialParameter material = new(null);

        [Header("Planet")]
        [Tooltip("Sets the planet radius in meters. VividRP uses this value directly when building atmospheric planet data.")]
        public MinFloatParameter planetRadius = new(DefaultEarthRadius, 1000.0f);

        [Header("Air")]
        [Tooltip("Sets the depth, in meters, of the atmospheric layer composed of air particles.")]
        public MinFloatParameter airMaximumAltitude = new(LayerDepthFromScaleHeight(DefaultAirScaleHeight), 0.0f);

        [Tooltip("Controls the red channel opacity of air at the zenith.")]
        public ClampedFloatParameter airDensityR = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringR, DefaultAirScaleHeight), 0.0f, 1.0f);

        [Tooltip("Controls the green channel opacity of air at the zenith.")]
        public ClampedFloatParameter airDensityG = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringG, DefaultAirScaleHeight), 0.0f, 1.0f);

        [Tooltip("Controls the blue channel opacity of air at the zenith.")]
        public ClampedFloatParameter airDensityB = new(ZenithOpacityFromExtinctionAndScaleHeight(DefaultAirScatteringB, DefaultAirScaleHeight), 0.0f, 1.0f);

        [Tooltip("Specifies the tint applied to air scattering albedo.")]
        public ColorParameter airTint = new(Color.white, false, false, true);

        [Header("Aerosol")]
        [Tooltip("Sets the depth, in meters, of the atmospheric layer composed of aerosol particles.")]
        public MinFloatParameter aerosolMaximumAltitude = new(DefaultAerosolMaximumAltitude, 0.0f);

        [Tooltip("Controls the opacity of aerosols at the zenith.")]
        public ClampedFloatParameter aerosolDensity = new(ZenithOpacityFromExtinctionAndScaleHeight(10.0f / 1000000.0f, DefaultAerosolScaleHeight), 0.0f, 1.0f);

        [Tooltip("Specifies the tint applied to aerosol scattering albedo.")]
        public ColorParameter aerosolTint = new(new Color(0.9f, 0.9f, 0.9f), false, false, true);

        [Tooltip("Controls the aerosol scattering anisotropy. Positive values bias forward scattering.")]
        public ClampedFloatParameter aerosolAnisotropy = new(0.8f, -1.0f, 1.0f);

        [Header("Ozone")]
        [Tooltip("Controls the ozone density in the atmosphere.")]
        public ClampedFloatParameter ozoneDensityDimmer = new(1.0f, 0.0f, 1.0f);

        [Tooltip("Controls the minimum altitude of the ozone layer in meters.")]
        public MinFloatParameter ozoneMinimumAltitude = new(DefaultOzoneMinimumAltitude, 0.0f);

        [Tooltip("Controls the width of the ozone layer in meters.")]
        public MinFloatParameter ozoneLayerWidth = new(DefaultOzoneLayerWidth, 0.0f);

        [Header("Ground")]
        [Tooltip("Specifies a color used to tint the planet surface.")]
        public ColorParameter groundTint = new(new Color(0.12f, 0.10f, 0.09f), false, false, false);

        [Tooltip("Specifies a cubemap that represents the planet surface.")]
        public CubemapParameter groundColorTexture = new(null);

        [Tooltip("Specifies a cubemap that represents emissive areas on the planet surface.")]
        public CubemapParameter groundEmissionTexture = new(null);

        [Tooltip("Sets the multiplier applied to the ground emission cubemap.")]
        public MinFloatParameter groundEmissionMultiplier = new(1.0f, 0.0f);

        [Tooltip("Sets the orientation of the planet surface cubemaps.")]
        public Vector3Parameter planetRotation = new(Vector3.zero);

        [Header("Space")]
        [Tooltip("Specifies a cubemap that represents emissive areas of space.")]
        public CubemapParameter spaceEmissionTexture = new(null);

        [Tooltip("Sets the multiplier applied to the space emission cubemap.")]
        public MinFloatParameter spaceEmissionMultiplier = new(1.0f, 0.0f);

        [Tooltip("Sets the orientation of the space cubemap.")]
        public Vector3Parameter spaceRotation = new(Vector3.zero);

        [Header("Artistic Overrides")]
        [Tooltip("Controls the saturation of the sky color.")]
        public ClampedFloatParameter colorSaturation = new(1.0f, 0.0f, 1.0f);

        [Tooltip("Controls the saturation of the sky opacity.")]
        public ClampedFloatParameter alphaSaturation = new(1.0f, 0.0f, 1.0f);

        [Tooltip("Sets the multiplier applied to the sky opacity.")]
        public ClampedFloatParameter alphaMultiplier = new(1.0f, 0.0f, 1.0f);

        [Tooltip("Specifies a tint applied at the horizon.")]
        public ColorParameter horizonTint = new(Color.white, false, false, true);

        [Tooltip("Specifies a tint applied at the zenith.")]
        public ColorParameter zenithTint = new(Color.white, false, false, true);

        [Tooltip("Controls the blend between the horizon tint and zenith tint.")]
        public ClampedFloatParameter horizonZenithShift = new(0.0f, -1.0f, 1.0f);

        [Header("Rendering")]
        [Tooltip("Sets the exposure compensation of the sky in EV.")]
        public FloatParameter exposure = new(0.0f);

        [Tooltip("Enables rendering of the sun disk in the physically based sky shader path.")]
        public BoolParameter renderSunDisk = new(true);

        [Tooltip("Scales the rendered sun disk size.")]
        public MinFloatParameter sunDiskSize = new(1.0f, 0.0f);

        [Header("Height Fog")]
        [Tooltip("Enables VividRP's additional height fog contribution on top of the physically based sky.")]
        public BoolParameter enableHeightFog = new(false);

        [Tooltip("Sets the world-space base height of the height fog layer.")]
        public FloatParameter fogBaseHeight = new(0.0f);

        [Tooltip("Controls the density of the additional height fog layer.")]
        public MinFloatParameter fogDensity = new(0.01f, 0.0f);

        [Tooltip("Limits how far the additional height fog contributes.")]
        public MinFloatParameter fogMaxDistance = new(5000.0f, 0.0f);

        [SerializeField, HideInInspector]
        private bool m_ExposureDefaultsMigrated;

        protected override void OnEnable()
        {
            exposure ??= new FloatParameter(0.0f);
            if (!m_ExposureDefaultsMigrated)
            {
                if (!exposure.overrideState && Mathf.Approximately(exposure.value, 1.0f))
                    exposure.value = 0.0f;

                m_ExposureDefaultsMigrated = true;
            }

            base.OnEnable();
        }

        public bool IsActive()
        {
            return airMaximumAltitude.value > 0.0f
                && (airDensityR.value > 0.0f || airDensityG.value > 0.0f || airDensityB.value > 0.0f || aerosolDensity.value > 0.0f);
        }

        internal float GetIntensityMultiplier()
        {
            return SkyIntensityUtility.GetExposureMultiplier(exposure.value);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = GetPrecomputationHashCode();
                hash = hash * 23 + renderingMode.GetHashCode();
                hash = hash * 23 + material.GetHashCode();
                hash = hash * 23 + planetRotation.GetHashCode();
                if (groundColorTexture.value != null)
                    hash = hash * 23 + groundColorTexture.GetHashCode();
                if (groundEmissionTexture.value != null)
                    hash = hash * 23 + groundEmissionTexture.GetHashCode();
                hash = hash * 23 + groundEmissionMultiplier.GetHashCode();
                hash = hash * 23 + spaceRotation.GetHashCode();
                if (spaceEmissionTexture.value != null)
                    hash = hash * 23 + spaceEmissionTexture.GetHashCode();
                hash = hash * 23 + spaceEmissionMultiplier.GetHashCode();
                hash = hash * 23 + colorSaturation.GetHashCode();
                hash = hash * 23 + alphaSaturation.GetHashCode();
                hash = hash * 23 + alphaMultiplier.GetHashCode();
                hash = hash * 23 + horizonTint.GetHashCode();
                hash = hash * 23 + zenithTint.GetHashCode();
                hash = hash * 23 + horizonZenithShift.GetHashCode();
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

        internal float GetAirScaleHeight()
        {
            return type.value == PhysicallyBasedSkyModel.Custom
                ? ScaleHeightFromLayerDepth(airMaximumAltitude.value)
                : DefaultAirScaleHeight;
        }

        internal float GetMaximumAltitude()
        {
            if (type.value == PhysicallyBasedSkyModel.Custom)
                return Mathf.Max(airMaximumAltitude.value, aerosolMaximumAltitude.value);

            var aerosolMaxAltitude = type.value == PhysicallyBasedSkyModel.EarthSimple
                ? DefaultAerosolMaximumAltitude
                : aerosolMaximumAltitude.value;
            return Mathf.Max(LayerDepthFromScaleHeight(DefaultAirScaleHeight), aerosolMaxAltitude);
        }

        internal Vector3 GetAirAlbedo()
        {
            if (type.value != PhysicallyBasedSkyModel.Custom)
                return Vector3.one;

            return new Vector3(airTint.value.r, airTint.value.g, airTint.value.b);
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
            var albedo = GetAirAlbedo();
            return new Vector3(
                extinction.x * albedo.x,
                extinction.y * albedo.y,
                extinction.z * albedo.z);
        }

        internal float GetAerosolScaleHeight()
        {
            return type.value == PhysicallyBasedSkyModel.EarthSimple
                ? DefaultAerosolScaleHeight
                : ScaleHeightFromLayerDepth(aerosolMaximumAltitude.value);
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
            var absorption = new Vector3(0.00065f, 0.00188f, 0.00008f) / 1000.0f;
            if (type.value != PhysicallyBasedSkyModel.EarthSimple)
                absorption *= ozoneDensityDimmer.value;
            return absorption;
        }

        internal float GetOzoneLayerWidth()
        {
            return type.value == PhysicallyBasedSkyModel.Custom
                ? ozoneLayerWidth.value
                : DefaultOzoneLayerWidth;
        }

        internal float GetOzoneLayerMinimumAltitude()
        {
            return type.value == PhysicallyBasedSkyModel.Custom
                ? ozoneMinimumAltitude.value
                : DefaultOzoneMinimumAltitude;
        }

        internal int GetPrecomputationHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + type.GetHashCode();
                hash = hash * 23 + atmosphericScattering.GetHashCode();
                hash = hash * 23 + planetRadius.GetHashCode();
                hash = hash * 23 + groundTint.GetHashCode();
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
                return hash;
            }
        }

        internal float GetAtmosphereRadius()
        {
            var ozoneTop = GetOzoneLayerMinimumAltitude() + GetOzoneLayerWidth();
            return planetRadius.value + Mathf.Max(GetMaximumAltitude(), ozoneTop);
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
