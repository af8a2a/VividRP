#ifndef VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED
#define VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

#include "AtmosphericScattering.cs.hlsl"
#include "ShaderVariablesAtmosphericScattering.hlsl"
#include "../Sky/PhysicallyBasedSkyEvaluation.hlsl"
#include "../Sky/SkyUtils.hlsl"

TEXTURE2D_X(_InputColor);
TEXTURE2D_X_FLOAT(_DepthTexture);

float4 _SkyFogParams;

static const float MaxSkyRadiance = 60000.0f;

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

    if (!isSky && (isnan(linearDepth) || isinf(linearDepth) || linearDepth <= 1e-4f))
        return inputColor;

    float tFrag = ComputeAtmosphericScatteringDistance(V, linearDepth, isSky);
    if (isnan(tFrag) || isinf(tFrag) || tFrag <= 1e-4f)
        return inputColor;

    float3 fogColor;
    float3 fogOpacity;
    EvaluateCameraAtmosphericScattering(-V, positionNDC, tFrag, fogColor, fogOpacity);

    fogColor = SanitizeSkyRadiance(fogColor);
    fogOpacity = saturate(fogOpacity);
    if (_FogColorMode == FOGCOLORMODE_SKY_COLOR && HasSkyTexture())
        fogColor = lerp(fogColor, SanitizeSkyRadiance(GetFogColor(V, tFrag)), fogOpacity);

    float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;
    return float4(composedColor, inputColor.a);
}

#endif
