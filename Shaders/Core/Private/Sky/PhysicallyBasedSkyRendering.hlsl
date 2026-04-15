#ifndef VIVIDRP_PHYSICALLY_BASED_SKY_RENDERING_INCLUDED
#define VIVIDRP_PHYSICALLY_BASED_SKY_RENDERING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "CelestialBodyData.hlsl"
#include "PhysicallyBasedSkyCommon.hlsl"
#include "SkyUtils.hlsl"

static const float MAX_SKY_RADIANCE = 60000.0f;

float3 _PBRSkyCameraPosPS;
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

float3 SanitizeSkyRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0f;

    return clamp(max(color, 0.0f), 0.0f, MAX_SKY_RADIANCE);
}

float3 SampleGroundAlbedo(float3 groundNormal)
{
    float3 albedo = _GroundAlbedo;
    if (_HasGroundAlbedoTexture != 0)
        albedo *= SAMPLE_TEXTURECUBE(_GroundAlbedoTexture, sampler_TrilinearClamp, mul(groundNormal, (float3x3)_PlanetRotation)).rgb;
    return albedo;
}

float3 SampleGroundEmission(float3 groundNormal)
{
    if (_HasGroundEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_GroundEmissionTexture, sampler_TrilinearClamp, mul(groundNormal, (float3x3)_PlanetRotation)).rgb * _GroundEmissionMultiplier;
}

float3 SampleSpaceEmission(float3 viewDirection)
{
    if (_HasSpaceEmissionTexture == 0)
        return 0.0f;

    return SAMPLE_TEXTURECUBE(_SpaceEmissionTexture, sampler_TrilinearClamp, mul(viewDirection, (float3x3)_SpaceRotation)).rgb * _SpaceEmissionMultiplier;
}

float ComputeMoonPhase(CelestialBodyData moon, float3 viewDirectionWS)
{
    float3 moonCenter = -moon.forward.xyz * moon.distanceFromCamera;
    float radialDistance = moon.distanceFromCamera;
    float rcpRadialDistance = rcp(radialDistance);
    float2 t = IntersectSphere(moon.radius, dot(-moon.forward.xyz, viewDirectionWS), radialDistance, rcpRadialDistance);
    float3 N = normalize(t.x * viewDirectionWS - moonCenter);

    return saturate(-dot(N, moon.sunDirection));
}

float ComputeEarthshine(CelestialBodyData moon)
{
    float sinPhase = sqrt(max(1.0f - dot(moon.sunDirection, moon.forward), 0.0f)) * INV_SQRT2;
    float earthshine = 1.0f - sinPhase * sqrt(sinPhase);

    return earthshine * moon.earthshine;
}

bool CanSampleCelestialSurfaceTexture(CelestialBodyData light)
{
#if defined(VIVIDRP_SKY_BINDLESS_SURFACE_TEXTURES)
    return light.surfaceTextureScaleOffset.x > 0.0f && light.surfaceTextureIndex != 0xffffffffu;
#else
    return false;
#endif
}

float3 SampleCelestialSurfaceTexture(CelestialBodyData light, float3 viewDirectionWS)
{
#if defined(VIVIDRP_SKY_BINDLESS_SURFACE_TEXTURES)
    float2 projection = float2(dot(viewDirectionWS, light.right), dot(viewDirectionWS, light.up));
    float2 angles = float2(FastASin(projection.x), FastASin(-projection.y));
    float2 uv = saturate(angles * rcp(max(light.angularRadius, 1e-6f)) * 0.5f + 0.5f);
    Texture2D surfaceTexture = GetBindlessTexture2D(NonUniformResourceIndex(light.surfaceTextureIndex));
    return SAMPLE_TEXTURE2D(surfaceTexture, sampler_LinearClamp, uv).rgb;
#else
    return 1.0f.xxx;
#endif
}

float3 RenderSunDisk(inout float tFrag, float tExit, float3 V)
{
    float3 radiance = 0.0f;

    for (uint i = 0; i < _CelestialBodyCount; i++)
    {
        CelestialBodyData light = _CelestialBodyDatas[i];
        float lightDist = max(light.distanceFromCamera, tExit);

        if (asint(light.angularRadius) != 0 && lightDist < tFrag)
        {
            float3 L = -light.forward.xyz;
            float LdotV = -dot(L, V);
            float radInner = light.angularRadius;

            if (LdotV >= light.flareCosInner)
            {
                tFrag = lightDist;
                float3 color = light.surfaceColor;

                if (light.type != 0)
                    color *= ComputeMoonPhase(light, V) * INV_PI + ComputeEarthshine(light);

                if (CanSampleCelestialSurfaceTexture(light))
                    color *= SampleCelestialSurfaceTexture(light, V);

                radiance = color;
            }
            else if (LdotV >= light.flareCosOuter)
            {
                float rad = acos(LdotV);
                float r = max(0.0f, rad - radInner);
                float w = saturate(1.0f - r * rcp(light.flareSize));

                float3 color = light.flareColor;
                color *= SafePositivePow(w, light.flareFalloff);
                radiance += color;
            }
        }
    }

    return radiance;
}

void EvaluatePbrAtmosphere(float3 positionPS, float3 V, float distAlongRay, bool renderSunDisk, out float3 skyColor, out float3 skyOpacity)
{
    skyColor = skyOpacity = 0.0f;

    const float R = _PlanetaryRadius;
    const float2 n = float2(_AirDensityFalloff, _AerosolDensityFalloff);
    const float2 H = float2(_AirScaleHeight, _AerosolScaleHeight);
    const float3 O = positionPS;
    const float tFrag = abs(distAlongRay);

    float3 N;
    float r;
    float tEntry = IntersectAtmosphere(O, V, N, r).x;
    float tExit = IntersectAtmosphere(O, V, N, r).y;

    float NdotV = dot(N, V);
    float cosChi = -NdotV;
    float cosHor = ComputeCosineOfHorizonAngle(r);
    bool rayIntersectsAtmosphere = (tEntry >= 0.0f);
    bool hitGround = distAlongRay < 0.0f;
    bool rayEndsInsideAtmosphere = (tFrag < tExit) && !hitGround;

    if (!rayIntersectsAtmosphere)
        return;

    float2 Z = R * n;
    float r0 = r;
    float cosChi0 = cosChi;
    float r1 = 0.0f;
    float cosChi1 = 0.0f;
    float3 N1 = 0.0f;

    if (tFrag < tExit)
    {
        float3 P1 = O + tFrag * -V;
        r1 = length(P1);
        N1 = P1 * rcp(r1);
        cosChi1 = dot(P1, -V) * rcp(r1);
        cosChi0 = (cosChi1 >= 0.0f) ? cosChi0 : -cosChi0;
    }

    float2 ch0;
    float2 ch1 = 0.0f;

    {
        float2 z0 = r0 * n;
        ch0.x = RescaledChapmanFunction(z0.x, Z.x, cosChi0);
        ch0.y = RescaledChapmanFunction(z0.y, Z.y, cosChi0);
    }

    if (tFrag < tExit)
    {
        float2 z1 = r1 * n;
        ch1.x = ChapmanUpperApprox(z1.x, abs(cosChi1)) * exp(Z.x - z1.x);
        ch1.y = ChapmanUpperApprox(z1.y, abs(cosChi1)) * exp(Z.y - z1.y);
    }

    float2 ch = abs(ch0 - ch1);
    float3 opticalDepth = ch.x * H.x * _AirSeaLevelExtinction.xyz
                        + ch.y * H.y * _AerosolSeaLevelExtinction;
    skyOpacity = 1.0f - TransmittanceFromOpticalDepth(opticalDepth);

    for (uint i = 0; i < _CelestialLightCount; i++)
    {
        CelestialBodyData light = _CelestialBodyDatas[i];
        float3 L = -light.forward.xyz;

        float height = r - R;
        float NdotL = dot(N, L);
        float3 projL = L - N * NdotL;
        float3 projV = V - N * NdotV;
        float phiL = acos(clamp(dot(projL, projV) * rsqrt(max(dot(projL, projL) * dot(projV, projV), FLT_EPS)), -1.0f, 1.0f));

        TexCoord4D texCoord = ConvertPositionAndOrientationToTexCoords(height, NdotV, NdotL, phiL);
        float LdotV = dot(L, V);
        float3 radiance = 0.0f;

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * AirPhase(LdotV);

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * AerosolPhase(LdotV);

        radiance += lerp(
            SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
            SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
            texCoord.a) * MS_EXPOSURE_INV;

        if (rayEndsInsideAtmosphere)
        {
            float height1 = r1 - R;
            float NdotV1 = -cosChi1;
            float NdotL1 = dot(N1, L);
            float3 projL1 = L - N1 * NdotL1;
            float3 projV1 = V - N1 * NdotV1;
            float phiL1 = acos(clamp(dot(projL1, projV1) * rsqrt(max(dot(projL1, projL1) * dot(projV1, projV1), FLT_EPS)), -1.0f, 1.0f));
            texCoord = ConvertPositionAndOrientationToTexCoords(height1, NdotV1, NdotL1, phiL1);

            float3 radiance1 = 0.0f;
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * AirPhase(LdotV);
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * AerosolPhase(LdotV);
            radiance1 += lerp(
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(texCoord.u, texCoord.v, texCoord.w1), 0).rgb,
                texCoord.a) * MS_EXPOSURE_INV;

            radiance = max(0.0f, radiance - (1.0f - skyOpacity) * radiance1);
        }

        skyColor += radiance * light.color.rgb;
    }

    AtmosphereArtisticOverride(cosHor, cosChi, skyColor, skyOpacity);
}

#endif
