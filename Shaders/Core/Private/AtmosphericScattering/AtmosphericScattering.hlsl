#ifndef VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED
#define VIVIDRP_ATMOSPHERIC_SCATTERING_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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

float4 FragOpaqueAtmosphericScattering(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    int2 pixelCoord = int2(input.positionCS.xy);
    float4 inputColor = LOAD_TEXTURE2D_X(_InputColor, pixelCoord);

    if (_SkyFogParams.x <= 0.5f)
        return inputColor;

    float deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r;
    if (IsFarDepth(deviceDepth))
        return inputColor;

    float2 positionNDC = saturate(input.positionCS.xy * _ScreenSize.zw);
    float3 positionWS = ComputeWorldSpacePosition(positionNDC, deviceDepth, UNITY_MATRIX_I_VP);
    if (any(isnan(positionWS)) || any(isinf(positionWS)))
        return inputColor;

    float tFrag = distance(positionWS, _WorldSpaceCameraPos);
    if (isnan(tFrag) || isinf(tFrag) || tFrag <= 1e-4f)
        return inputColor;

    if (_SkyFogParams.w > 0.0f)
        tFrag = min(tFrag, _SkyFogParams.w);

    if (tFrag <= 1e-4f)
        return inputColor;

    float3 viewDirectionWS = -GetSkyViewDirWS(input.positionCS.xy);
    float3 fogColor;
    float3 fogOpacity;
    EvaluateCameraAtmosphericScattering(viewDirectionWS, positionNDC, tFrag, fogColor, fogOpacity);

    fogColor = SanitizeSkyRadiance(fogColor);
    fogOpacity = saturate(fogOpacity);

    float3 composedColor = fogColor + (1.0f - fogOpacity) * inputColor.rgb;
    return float4(composedColor, inputColor.a);
}

#endif
