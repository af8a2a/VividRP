#ifndef VIVID_GLOBAL_SDF_INCLUDED
#define VIVID_GLOBAL_SDF_INCLUDED

#include "MeshSdfTrace.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

static const int VividGlobalSdfResolution = 128;
static const uint VividGlobalSdfLevelCount = 3u;
static const float VividGlobalSdfBand = 8.0;

Texture3D<float> _GlobalSdfNear;
Texture3D<float> _GlobalSdfMiddle;
Texture3D<float> _GlobalSdfFar;
float4 _GlobalSdfLevels[VividGlobalSdfLevelCount];
float4 _GlobalSdfScroll[VividGlobalSdfLevelCount];

struct VividGlobalSdfHit
{
    float distance;
    float3 normalWS;
    uint level;
};

float VividGlobalSdfSampleLevel(float3 positionWS, uint level)
{
    level = min(level, VividGlobalSdfLevelCount - 1u);
    float4 layout = _GlobalSdfLevels[level];
    float3 grid = clamp((positionWS - layout.xyz) / layout.w, 0.0, VividGlobalSdfResolution - 1.0);
    float3 uvw = (grid + _GlobalSdfScroll[level].xyz + 0.5) / VividGlobalSdfResolution;
    float value;
    if (level == 0u)
        value = _GlobalSdfNear.SampleLevel(sampler_LinearRepeat, uvw, 0);
    else if (level == 1u)
        value = _GlobalSdfMiddle.SampleLevel(sampler_LinearRepeat, uvw, 0);
    else
        value = _GlobalSdfFar.SampleLevel(sampler_LinearRepeat, uvw, 0);
    return value * layout.w;
}

bool VividGlobalSdfTrySample(float3 positionWS, out float distance, out uint level)
{
    level = VividGlobalSdfLevelCount;
    [unroll]
    for (uint i = 0u; i < VividGlobalSdfLevelCount; i++)
    {
        float4 layout = _GlobalSdfLevels[i];
        float3 grid = (positionWS - layout.xyz) / layout.w;
        if (level == VividGlobalSdfLevelCount && all(grid >= 1.0) && all(grid <= VividGlobalSdfResolution - 2.0))
            level = i;
    }
    bool valid = level < VividGlobalSdfLevelCount;
    distance = valid ? VividGlobalSdfSampleLevel(positionWS, level) : 0.0;
    return valid;
}

float3 VividGlobalSdfNormal(float3 positionWS, uint level)
{
    float2 offset = float2(_GlobalSdfLevels[level].w, 0.0);
    return SafeNormalize(float3(
        VividGlobalSdfSampleLevel(positionWS + offset.xyy, level) - VividGlobalSdfSampleLevel(positionWS - offset.xyy, level),
        VividGlobalSdfSampleLevel(positionWS + offset.yxy, level) - VividGlobalSdfSampleLevel(positionWS - offset.yxy, level),
        VividGlobalSdfSampleLevel(positionWS + offset.yyx, level) - VividGlobalSdfSampleLevel(positionWS - offset.yyx, level)));
}

// directionWS is normalized. Distances are in world units.
bool VividTraceGlobalSdf(float3 originWS, float3 directionWS, float minDistance, float maxDistance,
    out VividGlobalSdfHit hit)
{
    hit = (VividGlobalSdfHit)0;
    float3 entry = FLT_MAX;
    float3 exit = 0.0;
    [unroll]
    for (uint i = 0u; i < VividGlobalSdfLevelCount; i++)
    {
        float4 layout = _GlobalSdfLevels[i];
        float a = minDistance;
        float b = maxDistance;
        if (!VividMeshSdfClipRay(originWS, directionWS, layout.xyz + layout.w,
            layout.xyz + (VividGlobalSdfResolution - 2.0) * layout.w, a, b))
        {
            a = FLT_MAX;
            b = 0.0;
        }
        entry[i] = a;
        exit[i] = b;
    }

    float t = entry.z;
    float previousT = t;
    float previousDistance = 0.0;
    uint previousLevel = VividGlobalSdfLevelCount;
    [loop]
    for (int step = 0; step < 256 && t < exit.z; step++)
    {
        uint level = 2u;
        if (t >= entry.x && t < exit.x)
            level = 0u;
        else if (t >= entry.y && t < exit.y)
            level = 1u;

        float voxel = _GlobalSdfLevels[level].w;
        float3 position = originWS + t * directionWS;
        float distance = VividGlobalSdfSampleLevel(position, level);
        bool crossed = previousLevel == level && (distance < 0.0) != (previousDistance < 0.0);
        if (crossed || abs(distance) <= 0.18 * voxel)
        {
            if (crossed)
            {
                float a = previousT;
                float b = t;
                [unroll]
                for (int refine = 0; refine < 5; refine++)
                {
                    float middle = 0.5 * (a + b);
                    float middleDistance = VividGlobalSdfSampleLevel(originWS + middle * directionWS, level);
                    if ((middleDistance < 0.0) == (previousDistance < 0.0))
                        a = middle;
                    else
                        b = middle;
                }
                t = 0.5 * (a + b);
            }
            hit.distance = t;
            hit.level = level;
            hit.normalWS = VividGlobalSdfNormal(originWS + t * directionWS, level);
            return true;
        }

        previousLevel = level;
        previousT = t;
        previousDistance = distance;
        float next = min(t + max(0.18 * voxel, 0.65 * abs(distance)), exit[level]);
        // Visit a finer level's entry before a coarse step can cross it.
        if (level > 0u && entry.x > t)
            next = min(next, entry.x);
        if (level > 1u && entry.y > t)
            next = min(next, entry.y);
        if (next <= t)
            break;
        t = next;
    }
    return false;
}

#endif
