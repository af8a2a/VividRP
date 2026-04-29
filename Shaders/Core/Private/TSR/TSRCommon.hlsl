#ifndef VIVIDRP_TSR_COMMON_INCLUDED
#define VIVIDRP_TSR_COMMON_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"

#if defined(VIVID_TSR_WAVE_OPS) && defined(UNITY_COMPILER_DXC)
#define VIVID_TSR_USE_WAVE_OPS 1
#else
#define VIVID_TSR_USE_WAVE_OPS 0
#endif

float TSR_WaveActiveMaxOrSelf(float value)
{
#if VIVID_TSR_USE_WAVE_OPS
    return WaveActiveMax(value);
#else
    return value;
#endif
}

float TSR_WaveActiveAverageOrSelf(float value)
{
#if VIVID_TSR_USE_WAVE_OPS
    return WaveActiveSum(value) / max((float)WaveActiveCountBits(true), 1.0);
#else
    return value;
#endif
}

bool TSR_WaveActiveAnyTrueOrSelf(bool value)
{
#if VIVID_TSR_USE_WAVE_OPS
    return WaveActiveAnyTrue(value);
#else
    return value;
#endif
}

float3 TSR_RGBToYCoCg(float3 rgb)
{
    float y = dot(rgb, float3(0.25, 0.5, 0.25));
    float co = dot(rgb, float3(0.5, 0.0, -0.5));
    float cg = dot(rgb, float3(-0.25, 0.5, -0.25));
    return float3(y, co, cg);
}

float3 TSR_YCoCgToRGB(float3 ycocg)
{
    float y = ycocg.x;
    float co = ycocg.y;
    float cg = ycocg.z;
    return float3(y + co - cg, y + cg, y - co - cg);
}

bool TSR_IsDepthCloser(float candidateDepth, float bestDepth)
{
#if defined(UNITY_REVERSED_Z)
    return candidateDepth > bestDepth;
#else
    return candidateDepth < bestDepth;
#endif
}

float3 TSR_ClipToAABB(float3 color, float3 aabbMin, float3 aabbMax)
{
    float3 center = 0.5 * (aabbMax + aabbMin);
    float3 extents = 0.5 * (aabbMax - aabbMin) + 1e-6;
    float3 offset = color - center;
    float3 scale = abs(extents) / max(abs(offset), 1e-6);
    float t = saturate(min(min(scale.x, scale.y), scale.z));
    return center + offset * t;
}

float4 TSR_SampleCatmullRom(Texture2D<float4> textureSource, float2 uv, float2 textureSize)
{
    float2 position = uv * textureSize;
    float2 center = floor(position - 0.5) + 0.5;
    float2 f = position - center;
    float2 f2 = f * f;
    float2 f3 = f2 * f;

    float2 w0 = f2 - 0.5 * (f3 + f);
    float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    float2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
    float2 w3 = 0.5 * (f3 - f2);

    float2 w12 = w1 + w2;
    float2 tc0 = (center - 1.0) / textureSize;
    float2 tc12 = (center + w2 / max(w12, float2(1e-6, 1e-6))) / textureSize;
    float2 tc3 = (center + 2.0) / textureSize;

    float4 result =
        textureSource.SampleLevel(sampler_LinearClamp, float2(tc12.x, tc0.y), 0) * (w12.x * w0.y) +
        textureSource.SampleLevel(sampler_LinearClamp, float2(tc0.x, tc12.y), 0) * (w0.x * w12.y) +
        textureSource.SampleLevel(sampler_LinearClamp, float2(tc12.x, tc12.y), 0) * (w12.x * w12.y) +
        textureSource.SampleLevel(sampler_LinearClamp, float2(tc3.x, tc12.y), 0) * (w3.x * w12.y) +
        textureSource.SampleLevel(sampler_LinearClamp, float2(tc12.x, tc3.y), 0) * (w12.x * w3.y);

    return max(result, 0.0);
}

int2 TSR_OutputToRenderCoord(int2 outputCoord, int2 outputSize, int2 renderSize)
{
    float2 uv = (float2(outputCoord) + 0.5) / float2(outputSize);
    return clamp(int2(uv * float2(renderSize)), int2(0, 0), renderSize - 1);
}

float2 TSR_OutputUV(int2 outputCoord, int2 outputSize)
{
    return (float2(outputCoord) + 0.5) / float2(outputSize);
}

void TSR_NeighborhoodYCoCgBounds(
    Texture2D<float4> inputColor,
    int2 centerCoord,
    int2 renderSize,
    out float3 clampMin,
    out float3 clampMax)
{
    float3 m1 = 0.0;
    float3 m2 = 0.0;
    float3 neighborMin = 1e10;
    float3 neighborMax = -1e10;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            int2 sampleCoord = clamp(centerCoord + int2(x, y), int2(0, 0), renderSize - 1);
            float3 color = TSR_RGBToYCoCg(inputColor[sampleCoord].rgb);
            neighborMin = min(neighborMin, color);
            neighborMax = max(neighborMax, color);
            m1 += color;
            m2 += color * color;
        }
    }

    float3 mean = m1 * (1.0 / 9.0);
    float3 stddev = sqrt(abs(m2 * (1.0 / 9.0) - mean * mean));
    clampMin = max(mean - 1.25 * stddev, neighborMin);
    clampMax = min(mean + 1.25 * stddev, neighborMax);
}

#endif
