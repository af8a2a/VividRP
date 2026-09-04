#ifndef VIVIDRP_SLAB_LUT_INTEGRATION_INCLUDED
#define VIVIDRP_SLAB_LUT_INTEGRATION_INCLUDED

#include "VividSimpleSlabEnergy.hlsl"

// Visible-normal sampling (Heitz 2018), in the normal's local frame.
float3 VividSlabSampleVisibleNormal(float3 view, float alpha, float2 xi)
{
    float3 stretchedView = normalize(float3(alpha * view.xy, view.z));
    float lensq = dot(stretchedView.xy, stretchedView.xy);
    float3 tangent = lensq > 0.0f
        ? float3(-stretchedView.y, stretchedView.x, 0.0f) * rsqrt(lensq)
        : float3(1.0f, 0.0f, 0.0f);
    float3 bitangent = cross(stretchedView, tangent);
    float radius = sqrt(xi.x);
    float phi = 2.0f * VIVID_SIMPLE_SLAB_PI * xi.y;
    float x = radius * cos(phi);
    float y = radius * sin(phi);
    float blend = 0.5f * (1.0f + stretchedView.z);
    y = lerp(sqrt(saturate(1.0f - x * x)), y, blend);
    float3 normal = x * tangent + y * bitangent
        + sqrt(saturate(1.0f - x * x - y * y)) * stretchedView;
    return normalize(float3(alpha * normal.xy, max(normal.z, 0.0f)));
}

float2 VividIntegrateSlabAlbedo(float nDotV, float perceptualRoughness, uint sampleCount)
{
    nDotV = max(saturate(nDotV), 1e-5f);
    float alpha = VividSimpleSlabPerceptualRoughnessToAlpha(perceptualRoughness);
    float a2 = alpha * alpha;
    float3 view = float3(sqrt(saturate(1.0f - nDotV * nDotV)), 0.0f, nDotV);
    float smithV = sqrt(nDotV * nDotV * (1.0f - a2) + a2);
    float2 integral = 0.0f;
    for (uint i = 0u; i < sampleCount; ++i)
    {
        float2 xi = float2((i + 0.5f) / sampleCount, reversebits(i) * 2.3283064365386963e-10f);
        float3 halfVector = VividSlabSampleVisibleNormal(view, alpha, xi);
        float vDotH = saturate(dot(view, halfVector));
        float3 light = 2.0f * vDotH * halfVector - view;
        if (light.z > 0.0f)
        {
            float smithL = sqrt(light.z * light.z * (1.0f - a2) + a2);
            // G2 / G1(V); correlated Smith, not separable Smith or Schlick-GGX.
            float weight = saturate(light.z * (nDotV + smithV)
                / max(light.z * smithV + nDotV * smithL, 1e-8f));
            float fc = pow(1.0f - vDotH, 5.0f);
            integral += float2(weight * fc, 1.0f - weight);
        }
        else
            integral.y += 1.0f;
    }
    return VividSlabNormalizeAlbedoBasis(integral / sampleCount);
}

// Exact integral of a linear LUT segment in u = sqrt(NdotV): 4 * integral E(u) u^3 du.
float2 VividSlabIntegrateAlbedoSegment(float2 left, float2 right, uint index)
{
    float a = (float)index / (VIVID_SLAB_LUT_RESOLUTION - 1);
    float b = (float)(index + 1u) / (VIVID_SLAB_LUT_RESOLUTION - 1);
    float width = b - a;
    float totalWeight = width * (b + a) * (b * b + a * a);
    float rightWeight = width * (2.0f * a * a * a + width * (4.0f * a * a
        + width * (3.0f * a + 0.8f * width)));
    return left * (totalWeight - rightWeight) + right * rightWeight;
}

#endif
