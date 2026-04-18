#ifndef VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED
#define VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"

#include "AtmosphericScattering.cs.hlsl"
#include "ShaderVariablesAtmosphericScattering.hlsl"
#include "../Sky/CelestialBodyData.hlsl"
#include "../Sky/PhysicallyBasedSkyEvaluation.hlsl"
#include "../Sky/SkyUtils.hlsl"

TEXTURE2D_X(_InputColor);
TEXTURE2D_X_FLOAT(_DepthTexture);

float4 _SkyFogParams;

static const float MaxSkyRadiance = 60000.0f;

#ifndef SHADEROPTIONS_PRECOMPUTED_ATMOSPHERIC_ATTENUATION
#define SHADEROPTIONS_PRECOMPUTED_ATMOSPHERIC_ATTENUATION 1
#endif

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
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    return output;
}

float3 SanitizeSkyRadiance(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
        return 0.0f;

    return clamp(max(color, 0.0f), 0.0f, MaxSkyRadiance);
}

bool IsFarDepth(float deviceDepth)
{
    return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-5f;
}

float3 GetViewForwardDir()
{
    float4x4 viewMatrix = UNITY_MATRIX_V;
    return -viewMatrix[2].xyz;
}

float3 GetCameraPositionWS()
{
    return _WorldSpaceCameraPos;
}

float GetCurrentExposureMultiplier()
{
    return VividGetPreExposure();
}

float3 RotateSkyDirectionAroundYAxis(float3 directionWS, float rotationDegrees)
{
    float rotationRadians = radians(rotationDegrees);
    float s = 0.0f;
    float c = 1.0f;
    sincos(rotationRadians, s, c);

    return float3(
        c * directionWS.x - s * directionWS.z,
        directionWS.y,
        s * directionWS.x + c * directionWS.z);
}

bool HasSkyTexture()
{
    return _SkyTextureEnabled > 0.5f;
}

float ComputeMipFogLevel(float fragDist)
{
    float fogRange = max(_MipFogFar - _MipFogNear, 1e-4f);
    return (1.0f - saturate((fragDist - _MipFogNear) / fogRange)) * max(_SkyTextureMaxMip, 0.0f);
}

float3 SampleSkyTexture(float3 directionWS, float mipLevel)
{
    float3 rotatedDirectionWS = RotateSkyDirectionAroundYAxis(directionWS, _SkyTextureRotation);
    float3 skyRadiance = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, rotatedDirectionWS, mipLevel).rgb;
    return skyRadiance * _SkyTextureTint.rgb * _SkyTextureExposure;
}

float3 GetFogColor(float3 V, float fragDist)
{
    float3 color = _FogColor.rgb;

    if (_FogColorMode == FOGCOLORMODE_SKY_COLOR && HasSkyTexture())
    {
        float mipLevel = ComputeMipFogLevel(fragDist);
        color *= SampleSkyTexture(-V, mipLevel);
    }

    return color;
}

float ResolveMaxFogDistance()
{
    return max(_SkyFogParams.w, 0.0f);
}

float ComputeAtmosphericScatteringDistance(float3 V, float linearDepth, bool isSky)
{
    float maxFogDistance = ResolveMaxFogDistance();
    if (isSky)
        return maxFogDistance;

    float viewForwardDot = dot(-V, GetViewForwardDir());
    if (isnan(viewForwardDot) || isinf(viewForwardDot) || viewForwardDot <= 1e-5f)
        return -1.0f;

    float tFrag = linearDepth * rcp(viewForwardDot);
    if (maxFogDistance > 0.0f)
        tFrag = min(tFrag, maxFogDistance);

    return tFrag;
}

float ComputeHDRISkyFogOpacity(float tFrag)
{
    float maxFogDistance = ResolveMaxFogDistance();
    if (maxFogDistance <= 1e-4f)
        return 0.0f;

    float normalizedDistance = saturate(tFrag / maxFogDistance);
    float fogDensity = max(_SkyFogParams.z, 0.0f);
    return saturate(1.0f - exp(-fogDensity * normalizedDistance * maxFogDistance));
}

bool TryGetAtmosphericScatteringRay(float3 V, float linearDepth, bool isSky, out float tFrag)
{
    if (!isSky && (isnan(linearDepth) || isinf(linearDepth) || linearDepth <= 1e-4f))
    {
        tFrag = 0.0f;
        return false;
    }

    tFrag = ComputeAtmosphericScatteringDistance(V, linearDepth, isSky);
    return !(isnan(tFrag) || isinf(tFrag) || tFrag <= 1e-4f);
}

// All units in meters!
// Assumes that there is NO sky occlusion along the ray AT ALL.
// We evaluate atmospheric scattering for the sky and other celestial bodies
// during the sky pass. The opaque atmospheric scattering pass applies atmospheric
// scattering to all other opaque geometry.
void EvaluatePbrAtmosphere(float3 positionPS, float3 V, float distAlongRay, bool renderSunDisk,
                           out float3 skyColor, out float3 skyOpacity)
{
    skyColor = skyOpacity = 0;

    const float  R = _PlanetaryRadius;
    const float2 n = float2(_AirDensityFalloff, _AerosolDensityFalloff);
    const float2 H = float2(_AirScaleHeight,    _AerosolScaleHeight);
    const float3 O = positionPS;

    const float  tFrag = abs(distAlongRay);

    float3 N;
    float r;
    float tEntry = IntersectAtmosphere(O, V, N, r).x;
    float tExit  = IntersectAtmosphere(O, V, N, r).y;

    float NdotV  = dot(N, V);
    float cosChi = -NdotV;
    float cosHor = ComputeCosineOfHorizonAngle(r);

    bool rayIntersectsAtmosphere = (tEntry >= 0);
    bool rayEndsInsideAtmosphere = (tFrag < tExit) && (distAlongRay >= 0);

    if (rayIntersectsAtmosphere)
    {
        float2 Z = R * n;
        float r0 = r;
        float cosChi0 = cosChi;

        float r1 = 0;
        float cosChi1 = 0;
        float3 N1 = 0;

        if (tFrag < tExit)
        {
            float3 P1 = O + tFrag * -V;

            r1      = length(P1);
            N1      = P1 * rcp(r1);
            cosChi1 = dot(P1, -V) * rcp(r1);
            cosChi0 = (cosChi1 >= 0) ? cosChi0 : -cosChi0;
        }

        float2 ch0;
        float2 ch1 = 0;

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

        float3 optDepth = ch.x * H.x * _AirSeaLevelExtinction.xyz
                        + ch.y * H.y * _AerosolSeaLevelExtinction;

        skyOpacity = 1 - TransmittanceFromOpticalDepth(optDepth);

        for (uint i = 0; i < _CelestialLightCount; i++)
        {
            CelestialBodyData light = _CelestialBodyDatas[i];
            float3 L = -light.forward.xyz;

            if (renderSunDisk && asint(light.angularRadius) != 0 && light.distanceFromCamera <= tFrag)
            {
                float c = dot(L, -V);

                if (-0.99999 < c && c < 0.99999)
                {
                    float alpha = light.angularRadius;
                    float beta = acos(c);
                    float gamma = min(alpha, beta);
                    gamma *= (PI - beta) * rcp(PI - gamma);

                    float3 A = normalize(cross(L, -V));
                    float3x3 Rm = RotationFromAxisAngle(A, sin(gamma), cos(gamma));
                    L = mul(Rm, L);
                }
            }

            float height = r - R;
            float NdotL = dot(N, L);
            float3 projL = L - N * NdotL;
            float3 projV = V - N * NdotV;
            float phiL = acos(clamp(dot(projL, projV) * rsqrt(max(dot(projL, projL) * dot(projV, projV), FLT_EPS)), -1, 1));

            TexCoord4D tc = ConvertPositionAndOrientationToTexCoords(height, NdotV, NdotL, phiL);
            float3 radiance = 0;
            float LdotV = dot(L, V);

            radiance += lerp(
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                tc.a) * AirPhase(LdotV);

            radiance += lerp(
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                tc.a) * AerosolPhase(LdotV);

            radiance += lerp(
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                tc.a) * MS_EXPOSURE_INV;

            if (rayEndsInsideAtmosphere)
            {
                float3 radiance1 = 0;
                float height1 = r1 - R;
                float NdotV1 = -cosChi1;
                float NdotL1 = dot(N1, L);
                float3 projL1 = L - N1 * NdotL1;
                float3 projV1 = V - N1 * NdotV1;
                float phiL1 = acos(clamp(dot(projL1, projV1) * rsqrt(max(dot(projL1, projL1) * dot(projV1, projV1), FLT_EPS)), -1, 1));

                tc = ConvertPositionAndOrientationToTexCoords(height1, NdotV1, NdotL1, phiL1);

                radiance1 += lerp(
                    SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                    SAMPLE_TEXTURE3D_LOD(_AirSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                    tc.a) * AirPhase(LdotV);

                radiance1 += lerp(
                    SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                    SAMPLE_TEXTURE3D_LOD(_AerosolSingleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                    tc.a) * AerosolPhase(LdotV);

                radiance1 += lerp(
                    SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w0), 0).rgb,
                    SAMPLE_TEXTURE3D_LOD(_MultipleScatteringTexture, s_linear_clamp_sampler, float3(tc.u, tc.v, tc.w1), 0).rgb,
                    tc.a) * MS_EXPOSURE_INV;

                radiance = max(0, radiance - (1 - skyOpacity) * radiance1);
            }

            radiance *= light.color.rgb;
            skyColor += radiance;
        }

        #ifndef DISABLE_ATMOS_EVALUATE_ARTIST_OVERRIDE
        AtmosphereArtisticOverride(cosHor, cosChi, skyColor, skyOpacity);
        #endif
    }
}

void EvaluateAtmosphericScattering(float3 V, float2 positionNDC, float tFrag, out float3 skyColor, out float3 skyOpacity)
{
#if SHADEROPTIONS_PRECOMPUTED_ATMOSPHERIC_ATTENUATION
    EvaluateCameraAtmosphericScattering(V, positionNDC, tFrag, skyColor, skyOpacity);
#else
    float3 O = GetCameraPositionWS() - _PlanetCenterPosition;
    EvaluatePbrAtmosphere(O, -V, tFrag, false, skyColor, skyOpacity);
    skyColor *= _IntensityMultiplier * GetCurrentExposureMultiplier();
#endif
}

float4 FragOpaqueAtmosphericScattering(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 positionSS = input.positionCS.xy;
    int2 pixelCoord = int2(positionSS);
    float4 inputColor = LOAD_TEXTURE2D_X(_InputColor, pixelCoord);

    if (_SkyFogParams.x <= 0.5f)
        return inputColor;

    float deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r;
    bool isSky = IsFarDepth(deviceDepth);
    float2 positionNDC = positionSS * _ScreenSize.zw;
    float3 V = GetSkyViewDirWS(positionSS);
    float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);

    float tFrag = 0.0f;
    if (!TryGetAtmosphericScatteringRay(V, linearDepth, isSky, tFrag))
        return inputColor;

    float3 fogColor;
    float3 fogOpacity;
    EvaluateAtmosphericScattering(-V, positionNDC, tFrag, fogColor, fogOpacity);

    fogColor = SanitizeSkyRadiance(fogColor);
    fogOpacity = saturate(fogOpacity);
    if (_FogColorMode == FOGCOLORMODE_SKY_COLOR && HasSkyTexture())
        fogColor = lerp(fogColor, SanitizeSkyRadiance(GetFogColor(V, tFrag)), fogOpacity);

    float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;
    return float4(composedColor, inputColor.a);
}

float4 FragOpaqueAtmosphericScatteringForHDRISky(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 positionSS = input.positionCS.xy;
    int2 pixelCoord = int2(positionSS);
    float4 inputColor = LOAD_TEXTURE2D_X(_InputColor, pixelCoord);

    if (_SkyFogParams.x <= 0.5f)
        return inputColor;

    float deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r;
    bool isSky = IsFarDepth(deviceDepth);
    float3 V = GetSkyViewDirWS(positionSS);
    float linearDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);

    float tFrag = 0.0f;
    if (!TryGetAtmosphericScatteringRay(V, linearDepth, isSky, tFrag))
        return inputColor;

    float fogOpacity = ComputeHDRISkyFogOpacity(tFrag);
    float3 volOpacity = fogOpacity.xxx;
    float3 volColor = SanitizeSkyRadiance(GetFogColor(V, tFrag)) * volOpacity;

    float3 composedColor = volColor + (1.0f - volOpacity) * inputColor.rgb;
    return float4(composedColor, inputColor.a);
}

#endif
