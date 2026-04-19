#ifndef VIVIDRP_LTC_AREA_LIGHT_INCLUDED
#define VIVIDRP_LTC_AREA_LIGHT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AreaLighting.hlsl"

TEXTURE2D_ARRAY(_LtcData);

#define VIVID_LTC_LUT_SIZE (64)
#define VIVID_LTC_LIGHTING_MODEL_GGX 0u
#define VIVID_LTC_LIGHTING_MODEL_DISNEY_DIFFUSE 1u
#define VIVID_LTC_LIGHTING_MODEL_CHARLIE 2u

float3x3 SampleLtcMatrix(float perceptualRoughness, float clampedNdotV, uint bsdfIndex)
{
    float2 uv = Remap01ToHalfTexelCoord(float2(perceptualRoughness, sqrt(1.0 - clampedNdotV)), VIVID_LTC_LUT_SIZE);

    float3x3 invM = 0.0;
    invM._m22 = 1.0;
    invM._m00_m02_m11_m20 = SAMPLE_TEXTURE2D_ARRAY_LOD(_LtcData, sampler_LinearClamp, uv, bsdfIndex, 0);
    return invM;
}

float4 EvaluateLTC_Area(
    bool isRectLight,
    float3 center,
    float3 right,
    float3 up,
    float halfLength,
    float halfHeight,
    float3x3 invM)
{
    float3 ortho = cross(center, right);
    float orthoSq = dot(ortho, ortho);
    bool quit = orthoSq <= 1e-6f;
    quit = quit || (center.z + halfLength * abs(right.z) + halfHeight * abs(up.z) <= 0.0f);

    float4 ltcValue = float4(1.0, 1.0, 1.0, 0.0);

    if (quit)
        return ltcValue;

    float3 C = mul(invM, center);
    float3 A = mul(invM, right);
    float3 B = mul(invM, up);

    if (C.z + halfLength * abs(A.z) + halfHeight * abs(B.z) <= 0.0f)
        return ltcValue;

    if (isRectLight)
    {
        float4x3 lightVerts;
        lightVerts[0] = C - halfLength * A - halfHeight * B;
        lightVerts[1] = lightVerts[0] + (2.0 * halfHeight) * B;
        lightVerts[2] = lightVerts[1] + (2.0 * halfLength) * A;
        lightVerts[3] = lightVerts[2] - (2.0 * halfHeight) * B;

        float3 formFactor;
        ltcValue.a = PolygonIrradiance(lightVerts, formFactor);
        return ltcValue;
    }

    float widthFactor = ComputeLineWidthFactor(invM, ortho, orthoSq);
    ltcValue.a = I_diffuse_line(C, A, halfLength) * widthFactor;
    return ltcValue;
}

#endif
