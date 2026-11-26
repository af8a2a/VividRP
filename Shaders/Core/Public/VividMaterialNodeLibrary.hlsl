#ifndef VIVIDRP_MATERIAL_NODE_LIBRARY_INCLUDED
#define VIVIDRP_MATERIAL_NODE_LIBRARY_INCLUDED

// Phase 3.3 deterministic, branchless HLSL node library helpers for
// Material Graph AOT Coverage & Surface programs.

float3 VividAOTUnpackNormalTS(float4 packedNormal, float strength)
{
    float3 n;
    n.xy = packedNormal.wy * 2.0f - 1.0f;
    n.xy *= strength;
    n.z = sqrt(saturate(1.0f - dot(n.xy, n.xy)));
    return n;
}

float3 VividAOTUnpackNormalTS(float3 packedNormal, float strength)
{
    float3 n;
    n.xy = packedNormal.xy * 2.0f - 1.0f;
    n.xy *= strength;
    n.z = sqrt(saturate(1.0f - dot(n.xy, n.xy)));
    return n;
}

float VividAOTHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031f);
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.x + p3.y) * p3.z);
}

float2 VividAOTHash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031f, 0.1030f, 0.0973f));
    p3 += dot(p3, p3.yzx + 33.33f);
    return frac((p3.xx + p3.yz) * p3.zy) * 2.0f - 1.0f;
}

float VividAOTValueNoise2D(float2 uv, float scale)
{
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0f - 2.0f * f);

    float a = VividAOTHash21(i + float2(0.0f, 0.0f));
    float b = VividAOTHash21(i + float2(1.0f, 0.0f));
    float c = VividAOTHash21(i + float2(0.0f, 1.0f));
    float d = VividAOTHash21(i + float2(1.0f, 1.0f));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float VividAOTGradientNoise2D(float2 uv, float scale)
{
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0f - 15.0f) + 10.0f);

    float n00 = dot(VividAOTHash22(i + float2(0.0f, 0.0f)), f - float2(0.0f, 0.0f));
    float n10 = dot(VividAOTHash22(i + float2(1.0f, 0.0f)), f - float2(1.0f, 0.0f));
    float n01 = dot(VividAOTHash22(i + float2(0.0f, 1.0f)), f - float2(0.0f, 1.0f));
    float n11 = dot(VividAOTHash22(i + float2(1.0f, 1.0f)), f - float2(1.0f, 1.0f));
    float val = lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y);
    return saturate(val * 0.5f + 0.5f);
}

#endif // VIVIDRP_MATERIAL_NODE_LIBRARY_INCLUDED
