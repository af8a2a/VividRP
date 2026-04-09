#ifndef VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED
#define VIVIDRP_PHYSICALLY_BASED_SKY_BRIDGE_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

static const int VIEW_SAMPLE_COUNT = 12;
static const int LIGHT_SAMPLE_COUNT = 6;
static const float BLOCKED_OPTICAL_DEPTH = 100000.0f;
static const float MAX_SKY_RADIANCE = 60000.0f;

float4x4 _PixelCoordToViewDirWS;
TEXTURE2D(_SkyViewLUT);
SAMPLER(sampler_SkyViewLUT);
float _SkyUseLUT;
float4 _SkyCameraPositionPS;
float4 _SkySunDirection;
float4 _SkySunColor;
float4 _SkyPlanetParams;
float4 _SkyAirScattering;
float4 _SkyAirExtinction;
float4 _SkyAerosolScattering;
float4 _SkyAerosolExtinction;
float4 _SkyOzoneExtinction;
float4 _SkyOzoneParams;
float4 _SkyGroundTint;

float _AtmosphericRadius;
float4 _GroundAlbedo_PlanetRadius;
float4 _HorizonTint;
float4 _ZenithTint;
float _IntensityMultiplier;
float _ColorSaturation;
float _AlphaSaturation;
float _AlphaMultiplier;
float _HorizonZenithShiftPower;
float _HorizonZenithShiftScale;
int _RenderSunDisk;
int _HasGroundAlbedoTexture;
int _HasGroundEmissionTexture;
int _HasSpaceEmissionTexture;
float _GroundEmissionMultiplier;
float _SpaceEmissionMultiplier;
float4x4 _PlanetRotation;
float4x4 _SpaceRotation;

TEXTURECUBE(_GroundAlbedoTexture);
TEXTURECUBE(_GroundEmissionTexture);
TEXTURECUBE(_SpaceEmissionTexture);

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
    return output;
}

float3 GetSkyViewDirWS(float2 positionCS)
{
    float4 viewDirWS = mul(float4(positionCS.xy, 1.0f, 1.0f), _PixelCoordToViewDirWS);
    return normalize(viewDirWS.xyz);
}

float GetPlanetRadius()
{
    float boundPlanetRadius = _GroundAlbedo_PlanetRadius.w > 0.0f ? _GroundAlbedo_PlanetRadius.w : 0.0f;
    return max(max(boundPlanetRadius, _SkyPlanetParams.x), 1000.0f);
}

float GetAtmosphereRadius()
{
    float planetRadius = GetPlanetRadius();
    float boundAtmosphereRadius = _AtmosphericRadius > planetRadius ? _AtmosphericRadius : 0.0f;
    return max(max(boundAtmosphereRadius, _SkyPlanetParams.y), planetRadius + 1.0f);
}

float GetSkyExposureMultiplier()
{
    return max(_SkyPlanetParams.z, 0.0f);
}

bool HasMaterialOverrides()
{
    return _AtmosphericRadius > 0.0f;
}

bool GetRenderSunDiskEnabled()
{
    return _RenderSunDisk != 0 || _SkyPlanetParams.w > 0.5f;
}

float3 GetGroundAlbedoTint()
{
    return _GroundAlbedo_PlanetRadius.w > 0.0f
        ? _GroundAlbedo_PlanetRadius.xyz
        : _SkyGroundTint.rgb;
}

float3 GetHorizonTint()
{
    return HasMaterialOverrides() ? _HorizonTint.rgb : 1.0f.xxx;
}

float3 GetZenithTint()
{
    return HasMaterialOverrides() ? _ZenithTint.rgb : 1.0f.xxx;
}

float GetColorSaturationValue()
{
    return HasMaterialOverrides() ? _ColorSaturation : 1.0f;
}

float2 EncodeSkyViewUv(float3 directionWS)
{
    float azimuth = atan2(directionWS.z, directionWS.x);
    return float2(frac(azimuth / (2.0f * PI) + 0.5f), saturate(directionWS.y * 0.5f + 0.5f));
}

bool IntersectAtmosphere(float3 origin, float3 direction, float atmosphereRadius, out float entryDistance, out float exitDistance)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - atmosphereRadius * atmosphereRadius;
    float discriminant = b * b - c;

    if (discriminant < 0.0f)
    {
        entryDistance = 0.0f;
        exitDistance = 0.0f;
        return false;
    }

    float sqrtDiscriminant = sqrt(discriminant);
    entryDistance = -b - sqrtDiscriminant;
    exitDistance = -b + sqrtDiscriminant;
    return exitDistance > 0.0f;
}

bool IntersectGround(float3 origin, float3 direction, float planetRadius, out float distance)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - planetRadius * planetRadius;
    float discriminant = b * b - c;

    if (discriminant < 0.0f)
    {
        distance = 0.0f;
        return false;
    }

    distance = -b - sqrt(discriminant);
    return distance > 0.0f;
}

float EvaluateOzoneDensity(float height, float minimumAltitude, float layerWidth)
{
    if (layerWidth <= 0.0f)
        return 0.0f;

    float normalizedHeight = (height - minimumAltitude) / layerWidth;
    return saturate(1.0f - abs(normalizedHeight * 2.0f - 1.0f));
}

float3 EvaluateTransmittance(
    float3 airExtinction,
    float3 aerosolExtinction,
    float opticalDepthAir,
    float opticalDepthAerosol,
    float3 opticalDepthOzone)
{
    return exp(-(airExtinction * opticalDepthAir + aerosolExtinction * opticalDepthAerosol + opticalDepthOzone));
}

float3 SanitizeSkyRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0f;

    return clamp(max(color, 0.0f), 0.0f, MAX_SKY_RADIANCE);
}

float ComputeCosineOfHorizonAngle(float radialDistance)
{
    float planetRadius = GetPlanetRadius();
    float sinHorizon = planetRadius * rcp(radialDistance);
    return -sqrt(saturate(1.0f - sinHorizon * sinHorizon));
}

float3 ExpLerp(float3 a, float3 b, float t, float x, float y)
{
    t = exp(x * t) * y - y;
    return lerp(a, b, t);
}

void ComputeOpticalDepthToSun(
    float3 samplePosition,
    float3 sunDirection,
    float planetRadius,
    float atmosphereRadius,
    out float opticalDepthAir,
    out float opticalDepthAerosol,
    out float3 opticalDepthOzone)
{
    opticalDepthAir = 0.0f;
    opticalDepthAerosol = 0.0f;
    opticalDepthOzone = float3(0.0f, 0.0f, 0.0f);

    float atmosphereEntry;
    float atmosphereExit;
    if (!IntersectAtmosphere(samplePosition, sunDirection, atmosphereRadius, atmosphereEntry, atmosphereExit))
        return;

    float groundHit;
    bool sunRayHitsGround = IntersectGround(samplePosition, sunDirection, planetRadius, groundHit);
    if (sunRayHitsGround && groundHit > 0.0f && groundHit < atmosphereExit)
    {
        opticalDepthAir = BLOCKED_OPTICAL_DEPTH;
        opticalDepthAerosol = BLOCKED_OPTICAL_DEPTH;
        opticalDepthOzone = float3(BLOCKED_OPTICAL_DEPTH, BLOCKED_OPTICAL_DEPTH, BLOCKED_OPTICAL_DEPTH);
        return;
    }

    float stepLength = atmosphereExit / LIGHT_SAMPLE_COUNT;
    if (stepLength <= 0.0f)
        return;

    float airScaleHeight = max(_SkyOzoneParams.y, 1.0f);
    float aerosolScaleHeight = max(_SkyOzoneParams.w, 1.0f);
    float ozoneMinimumAltitude = _SkyOzoneExtinction.w;
    float ozoneLayerWidth = _SkyOzoneParams.x;

    [loop]
    for (int sampleIndex = 0; sampleIndex < LIGHT_SAMPLE_COUNT; sampleIndex++)
    {
        float sampleDistance = (sampleIndex + 0.5f) * stepLength;
        float3 lightSamplePosition = samplePosition + sunDirection * sampleDistance;
        float height = max(length(lightSamplePosition) - planetRadius, 0.0f);
        float localAirDensity = exp(-height / airScaleHeight);
        float localAerosolDensity = exp(-height / aerosolScaleHeight);
        float localOzoneDensity = EvaluateOzoneDensity(height, ozoneMinimumAltitude, ozoneLayerWidth);

        opticalDepthAir += localAirDensity * stepLength;
        opticalDepthAerosol += localAerosolDensity * stepLength;
        opticalDepthOzone += _SkyOzoneExtinction.rgb * (localOzoneDensity * stepLength);
    }
}

float3 ComputeViewTransmittance(
    float3 origin,
    float3 direction,
    float rayLength,
    float planetRadius,
    float airScaleHeight,
    float aerosolScaleHeight,
    float ozoneMinimumAltitude,
    float ozoneLayerWidth)
{
    float opticalDepthAir = 0.0f;
    float opticalDepthAerosol = 0.0f;
    float3 opticalDepthOzone = float3(0.0f, 0.0f, 0.0f);
    float stepLength = rayLength / VIEW_SAMPLE_COUNT;

    [loop]
    for (int sampleIndex = 0; sampleIndex < VIEW_SAMPLE_COUNT; sampleIndex++)
    {
        float sampleDistance = (sampleIndex + 0.5f) * stepLength;
        float3 samplePosition = origin + direction * sampleDistance;
        float height = max(length(samplePosition) - planetRadius, 0.0f);
        float airDensity = exp(-height / airScaleHeight);
        float aerosolDensity = exp(-height / aerosolScaleHeight);
        float ozoneDensity = EvaluateOzoneDensity(height, ozoneMinimumAltitude, ozoneLayerWidth);

        opticalDepthAir += airDensity * stepLength;
        opticalDepthAerosol += aerosolDensity * stepLength;
        opticalDepthOzone += _SkyOzoneExtinction.rgb * (ozoneDensity * stepLength);
    }

    return EvaluateTransmittance(_SkyAirExtinction.rgb, _SkyAerosolExtinction.rgb, opticalDepthAir, opticalDepthAerosol, opticalDepthOzone);
}

float3 ComputeSunDiskTransmittance(float3 cameraPosition, float3 sunDirection, float planetRadius, float atmosphereRadius)
{
    float opticalDepthAir;
    float opticalDepthAerosol;
    float3 opticalDepthOzone;
    ComputeOpticalDepthToSun(
        cameraPosition,
        sunDirection,
        planetRadius,
        atmosphereRadius,
        opticalDepthAir,
        opticalDepthAerosol,
        opticalDepthOzone);
    return EvaluateTransmittance(
        _SkyAirExtinction.rgb,
        _SkyAerosolExtinction.rgb,
        opticalDepthAir,
        opticalDepthAerosol,
        opticalDepthOzone);
}

float EvaluateSunDiskMask(float3 directionWS, float3 sunDirection)
{
    if (!GetRenderSunDiskEnabled())
        return 0.0f;

    float sunAngularRadius = max(_SkyOzoneParams.z, 1e-5f);
    float sunDot = clamp(dot(normalize(directionWS), sunDirection), -1.0f, 1.0f);
    float sunCosThreshold = cos(sunAngularRadius);
    float edgeSoftness = max(fwidth(sunDot) * 2.0f, 1e-4f);
    return smoothstep(sunCosThreshold - edgeSoftness, sunCosThreshold + edgeSoftness, sunDot);
}

float3 SampleGroundAlbedo(float3 groundNormal)
{
    float3 albedo = GetGroundAlbedoTint();
    if (_HasGroundAlbedoTexture != 0)
        albedo *= SAMPLE_TEXTURECUBE(_GroundAlbedoTexture, s_trilinear_clamp_sampler, mul(groundNormal, (float3x3)_PlanetRotation)).rgb;
    return albedo;
}

float3 SampleGroundEmission(float3 groundNormal)
{
    if (_HasGroundEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_GroundEmissionTexture, s_trilinear_clamp_sampler, mul(groundNormal, (float3x3)_PlanetRotation)).rgb * _GroundEmissionMultiplier;
}

float3 SampleSpaceEmission(float3 viewDirection)
{
    if (_HasSpaceEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_SpaceEmissionTexture, s_trilinear_clamp_sampler, mul(viewDirection, (float3x3)_SpaceRotation)).rgb * _SpaceEmissionMultiplier;
}

void ApplyArtisticOverrides(float3 viewDirection, inout float3 skyColor)
{
    if (!HasMaterialOverrides())
        return;

    skyColor = Desaturate(skyColor, GetColorSaturationValue());

    float3 cameraPosition = _SkyCameraPositionPS.xyz;
    float radialDistance = max(length(cameraPosition), GetPlanetRadius() + 1.0f);
    float3 planetUp = normalize(cameraPosition);
    float cosHor = ComputeCosineOfHorizonAngle(radialDistance);
    float cosChi = clamp(dot(planetUp, normalize(viewDirection)), -1.0f, 1.0f);
    float horAngle = acos(clamp(cosHor, -1.0f, 1.0f));
    float chiAngle = acos(clamp(cosChi, -1.0f, 1.0f));
    float rcpLength = abs(horAngle) > 1e-5f ? -rcp(horAngle) : -1.0f;
    float normalizedAngle = saturate(chiAngle * rcpLength - horAngle * rcpLength);

    skyColor *= ExpLerp(
        GetHorizonTint(),
        GetZenithTint(),
        normalizedAngle,
        _HorizonZenithShiftPower,
        _HorizonZenithShiftScale);
}

float3 EvaluateSunDisk(float3 directionWS)
{
    float3 sunDirection = normalize(_SkySunDirection.xyz);
    float sunMask = EvaluateSunDiskMask(directionWS, sunDirection);
    if (sunMask <= 0.0f)
        return 0.0f;

    float planetRadius = GetPlanetRadius();
    float atmosphereRadius = GetAtmosphereRadius();
    float3 sunTransmittance = ComputeSunDiskTransmittance(
        _SkyCameraPositionPS.xyz,
        sunDirection,
        planetRadius,
        atmosphereRadius);
    return SanitizeSkyRadiance(_SkySunColor.rgb * sunTransmittance * sunMask * 2.0f);
}

float3 EvaluateGroundColor(float3 groundPoint, float3 viewTransmittance)
{
    float3 groundNormal = normalize(groundPoint);
    float3 sunDirection = normalize(_SkySunDirection.xyz);
    float planetRadius = GetPlanetRadius();
    float atmosphereRadius = GetAtmosphereRadius();
    float3 groundColor = SampleGroundEmission(groundNormal);

    float opticalDepthAir;
    float opticalDepthAerosol;
    float3 opticalDepthOzone;
    ComputeOpticalDepthToSun(
        groundPoint + groundNormal,
        sunDirection,
        planetRadius,
        atmosphereRadius,
        opticalDepthAir,
        opticalDepthAerosol,
        opticalDepthOzone);

    float3 sunTransmittance = EvaluateTransmittance(
        _SkyAirExtinction.rgb,
        _SkyAerosolExtinction.rgb,
        opticalDepthAir,
        opticalDepthAerosol,
        opticalDepthOzone);
    float ndotl = saturate(dot(groundNormal, sunDirection));
    groundColor += SampleGroundAlbedo(groundNormal) * INV_PI * ndotl * sunTransmittance * _SkySunColor.rgb;
    return SanitizeSkyRadiance(groundColor * viewTransmittance);
}

float3 EvaluateSpaceColor(float3 viewDirection, float3 viewTransmittance)
{
    return SanitizeSkyRadiance(SampleSpaceEmission(viewDirection) * viewTransmittance);
}

float3 EvaluateSky(float3 directionWS)
{
    float3 normalizedDirection = normalize(directionWS);
    float3 cameraPosition = _SkyCameraPositionPS.xyz;
    float3 sunDirection = normalize(_SkySunDirection.xyz);
    float3 sunColor = _SkySunColor.rgb;
    float planetRadius = GetPlanetRadius();
    float atmosphereRadius = GetAtmosphereRadius();
    float airScaleHeight = max(_SkyOzoneParams.y, 1.0f);
    float aerosolScaleHeight = max(_SkyOzoneParams.w, 1.0f);
    float ozoneMinimumAltitude = _SkyOzoneExtinction.w;
    float ozoneLayerWidth = _SkyOzoneParams.x;
    float g = clamp(_SkyAerosolExtinction.w, -0.95f, 0.95f);

    float atmosphereEntry;
    float atmosphereExit;
    if (!IntersectAtmosphere(cameraPosition, normalizedDirection, atmosphereRadius, atmosphereEntry, atmosphereExit))
        return EvaluateSpaceColor(normalizedDirection, 1.0f.xxx);

    float rayLength = atmosphereExit;
    float groundHit;
    bool hitGround = IntersectGround(cameraPosition, normalizedDirection, planetRadius, groundHit);
    hitGround = hitGround && groundHit > 0.0f;
    if (hitGround)
        rayLength = min(rayLength, groundHit);

    float stepLength = rayLength / VIEW_SAMPLE_COUNT;
    if (stepLength <= 0.0f)
        return 0.0f;

    float opticalDepthAir = 0.0f;
    float opticalDepthAerosol = 0.0f;
    float3 opticalDepthOzone = 0.0f;
    float mu = clamp(dot(normalizedDirection, sunDirection), -1.0f, 1.0f);
    float phaseRayleigh = 3.0f / (16.0f * PI) * (1.0f + mu * mu);
    float phaseMieNumerator = 3.0f / (8.0f * PI) * (1.0f - g * g) * (1.0f + mu * mu);
    float phaseMieDenominator = (2.0f + g * g) * pow(max(1.0f + g * g - 2.0f * g * mu, 1e-3f), 1.5f);
    float phaseMie = phaseMieNumerator / max(phaseMieDenominator, 1e-3f);
    float3 inscattered = 0.0f;

    [loop]
    for (int sampleIndex = 0; sampleIndex < VIEW_SAMPLE_COUNT; sampleIndex++)
    {
        float sampleDistance = (sampleIndex + 0.5f) * stepLength;
        float3 samplePosition = cameraPosition + normalizedDirection * sampleDistance;
        float height = max(length(samplePosition) - planetRadius, 0.0f);
        float localAirDensity = exp(-height / airScaleHeight);
        float localAerosolDensity = exp(-height / aerosolScaleHeight);
        float localOzoneDensity = EvaluateOzoneDensity(height, ozoneMinimumAltitude, ozoneLayerWidth);

        opticalDepthAir += localAirDensity * stepLength;
        opticalDepthAerosol += localAerosolDensity * stepLength;
        opticalDepthOzone += _SkyOzoneExtinction.rgb * (localOzoneDensity * stepLength);

        float sunOpticalDepthAir;
        float sunOpticalDepthAerosol;
        float3 sunOpticalDepthOzone;
        ComputeOpticalDepthToSun(
            samplePosition,
            sunDirection,
            planetRadius,
            atmosphereRadius,
            sunOpticalDepthAir,
            sunOpticalDepthAerosol,
            sunOpticalDepthOzone);

        float3 viewTransmittance = EvaluateTransmittance(
            _SkyAirExtinction.rgb,
            _SkyAerosolExtinction.rgb,
            opticalDepthAir,
            opticalDepthAerosol,
            opticalDepthOzone);
        float3 sunTransmittance = EvaluateTransmittance(
            _SkyAirExtinction.rgb,
            _SkyAerosolExtinction.rgb,
            sunOpticalDepthAir,
            sunOpticalDepthAerosol,
            sunOpticalDepthOzone);
        float3 scattering =
            _SkyAirScattering.rgb * (localAirDensity * phaseRayleigh)
            + _SkyAerosolScattering.rgb * (localAerosolDensity * phaseMie);
        float3 attenuation = viewTransmittance * sunTransmittance;
        inscattered += attenuation * scattering * stepLength;
    }

    float3 skyColor = inscattered * sunColor;
    ApplyArtisticOverrides(normalizedDirection, skyColor);

    float3 viewTransmittance = EvaluateTransmittance(
        _SkyAirExtinction.rgb,
        _SkyAerosolExtinction.rgb,
        opticalDepthAir,
        opticalDepthAerosol,
        opticalDepthOzone);

    if (hitGround)
    {
        float3 groundPoint = cameraPosition + normalizedDirection * rayLength;
        skyColor += EvaluateGroundColor(groundPoint, viewTransmittance);
    }
    else
    {
        skyColor += EvaluateSpaceColor(normalizedDirection, viewTransmittance);
    }

    return SanitizeSkyRadiance(skyColor * GetSkyExposureMultiplier());
}

float3 EvaluateSkyColor(float2 positionCS)
{
    float3 viewDirWS = -GetSkyViewDirWS(positionCS);
    float3 skyColor = _SkyUseLUT > 0.5f
        ? SAMPLE_TEXTURE2D(_SkyViewLUT, sampler_SkyViewLUT, EncodeSkyViewUv(normalize(viewDirWS))).rgb
        : EvaluateSky(viewDirWS);

    if (_SkyUseLUT > 0.5f)
    {
        ApplyArtisticOverrides(viewDirWS, skyColor);

        float planetRadius = GetPlanetRadius();
        float atmosphereRadius = GetAtmosphereRadius();
        float airScaleHeight = max(_SkyOzoneParams.y, 1.0f);
        float aerosolScaleHeight = max(_SkyOzoneParams.w, 1.0f);
        float ozoneMinimumAltitude = _SkyOzoneExtinction.w;
        float ozoneLayerWidth = _SkyOzoneParams.x;
        float atmosphereEntry;
        float atmosphereExit;
        if (IntersectAtmosphere(_SkyCameraPositionPS.xyz, normalize(viewDirWS), atmosphereRadius, atmosphereEntry, atmosphereExit))
        {
            float3 transmittance = ComputeViewTransmittance(
                _SkyCameraPositionPS.xyz,
                normalize(viewDirWS),
                atmosphereExit,
                planetRadius,
                airScaleHeight,
                aerosolScaleHeight,
                ozoneMinimumAltitude,
                ozoneLayerWidth);
            skyColor += EvaluateSpaceColor(normalize(viewDirWS), transmittance);
        }
        else
        {
            skyColor += EvaluateSpaceColor(normalize(viewDirWS), 1.0f.xxx);
        }
    }

    skyColor += EvaluateSunDisk(viewDirWS);
    return SanitizeSkyRadiance(skyColor);
}

#endif
