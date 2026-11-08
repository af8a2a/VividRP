#ifndef VIVID_GLOBAL_SDF_INCLUDED
#define VIVID_GLOBAL_SDF_INCLUDED

#include "MeshSdfTrace.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

static const int VividGlobalSdfResolution = 128;
static const uint VividGlobalSdfLevelCount = 3u;
static const float VividGlobalSdfBand = 8.0;
static const uint VividGlobalSdfBrickShift = 3u;
static const uint VividGlobalSdfBrickSize = 1u << VividGlobalSdfBrickShift;
static const uint VividGlobalSdfBrickResolution = uint(VividGlobalSdfResolution) >> VividGlobalSdfBrickShift;
static const uint VividGlobalSdfPagesPerSlice = VividGlobalSdfBrickResolution * VividGlobalSdfBrickResolution;
static const uint VividGlobalSdfPagesPerLevel = VividGlobalSdfPagesPerSlice * VividGlobalSdfBrickResolution;
static const int VividGlobalSdfThinBrickSize = 8;
static const int VividGlobalSdfThinBrickResolution = VividGlobalSdfResolution / VividGlobalSdfThinBrickSize + 1;
static const uint VividGlobalSdfThinBrickWords =
    (VividGlobalSdfThinBrickResolution * VividGlobalSdfThinBrickResolution * VividGlobalSdfThinBrickResolution + 31) / 32;

Texture3D<float> _GlobalSdfNear;
Texture3D<float> _GlobalSdfMiddle;
Texture3D<float> _GlobalSdfFar;
StructuredBuffer<uint> _GlobalSdfThinBricks;
StructuredBuffer<uint> _GlobalSdfPages;
float4 _GlobalSdfLevels[VividGlobalSdfLevelCount];
float4 _GlobalSdfScroll[VividGlobalSdfLevelCount];
uint _GlobalSdfLayerMask;

struct VividGlobalSdfHit
{
    float distance;
    float3 normalWS;
    uint level;
};

uint VividGlobalSdfPage(uint3 physical, uint level)
{
    uint3 brick = physical >> VividGlobalSdfBrickShift;
    return _GlobalSdfPages[level * VividGlobalSdfPagesPerLevel
        + brick.x + VividGlobalSdfBrickResolution * brick.y + VividGlobalSdfPagesPerSlice * brick.z];
}

uint3 VividGlobalSdfAtlasCell(uint slot, uint3 physical)
{
    uint3 tile = uint3(slot % VividGlobalSdfBrickResolution,
        (slot / VividGlobalSdfBrickResolution) % VividGlobalSdfBrickResolution,
        slot / VividGlobalSdfPagesPerSlice);
    return tile * VividGlobalSdfBrickSize + (physical & (VividGlobalSdfBrickSize - 1u));
}

float VividGlobalSdfLoadCell(Texture3D<float> atlas, uint3 cell, uint level)
{
    uint3 physical = cell & (uint(VividGlobalSdfResolution) - 1u);
    uint page = VividGlobalSdfPage(physical, level);
    if (page == 0u)
        return VividGlobalSdfBand;
    return atlas.Load(int4(VividGlobalSdfAtlasCell(page - 1u, physical), 0));
}

float VividGlobalSdfSampleAtlas(Texture3D<float> atlas, float3 cell, uint level)
{
    uint3 low = uint3(cell);
    float3 blend = frac(cell);
    uint3 local = low & (VividGlobalSdfBrickSize - 1u);
    if (all(local < VividGlobalSdfBrickSize - 1u))
    {
        uint3 physical = low & (uint(VividGlobalSdfResolution) - 1u);
        uint page = VividGlobalSdfPage(physical, level);
        if (page == 0u)
            return VividGlobalSdfBand;
        uint3 size;
        atlas.GetDimensions(size.x, size.y, size.z);
        float3 atlasCell = float3(VividGlobalSdfAtlasCell(page - 1u, physical)) + blend;
        return atlas.SampleLevel(sampler_LinearClamp, (atlasCell + 0.5) / float3(size), 0);
    }

    // Interpolate across pages in clipmap space so atlas neighbors cannot bleed into a sample.
    float x00 = lerp(VividGlobalSdfLoadCell(atlas, low, level),
        VividGlobalSdfLoadCell(atlas, low + uint3(1, 0, 0), level), blend.x);
    float x10 = lerp(VividGlobalSdfLoadCell(atlas, low + uint3(0, 1, 0), level),
        VividGlobalSdfLoadCell(atlas, low + uint3(1, 1, 0), level), blend.x);
    float x01 = lerp(VividGlobalSdfLoadCell(atlas, low + uint3(0, 0, 1), level),
        VividGlobalSdfLoadCell(atlas, low + uint3(1, 0, 1), level), blend.x);
    float x11 = lerp(VividGlobalSdfLoadCell(atlas, low + uint3(0, 1, 1), level),
        VividGlobalSdfLoadCell(atlas, low + uint3(1, 1, 1), level), blend.x);
    return lerp(lerp(x00, x10, blend.y), lerp(x01, x11, blend.y), blend.z);
}

float VividGlobalSdfSampleLevel(float3 positionWS, uint level)
{
    level = min(level, VividGlobalSdfLevelCount - 1u);
    float4 layout = _GlobalSdfLevels[level];
    float3 grid = clamp((positionWS - layout.xyz) / layout.w, 0.0, VividGlobalSdfResolution - 1.0);
    float3 cell = grid + _GlobalSdfScroll[level].xyz;
    float value;
    if (level == 0u)
        value = VividGlobalSdfSampleAtlas(_GlobalSdfNear, cell, level);
    else if (level == 1u)
        value = VividGlobalSdfSampleAtlas(_GlobalSdfMiddle, cell, level);
    else
        value = VividGlobalSdfSampleAtlas(_GlobalSdfFar, cell, level);
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

void VividGlobalSdfThinBrickBit(int3 brick, uint level, out uint word, out uint bit)
{
    const uint size = uint(VividGlobalSdfThinBrickResolution);
    uint3 slot = uint3(abs(brick)) % size;
    slot.x = brick.x < 0 && slot.x != 0u ? size - slot.x : slot.x;
    slot.y = brick.y < 0 && slot.y != 0u ? size - slot.y : slot.y;
    slot.z = brick.z < 0 && slot.z != 0u ? size - slot.z : slot.z;
    uint index = slot.x + size * (slot.y + size * slot.z);
    word = level * VividGlobalSdfThinBrickWords + (index >> 5);
    bit = 1u << (index & 31u);
}

bool VividGlobalSdfHasThinBrick(int3 brick, uint level)
{
    uint word, bit;
    VividGlobalSdfThinBrickBit(brick, level, word, bit);
    return (_GlobalSdfThinBricks[word] & bit) != 0u;
}

bool VividGlobalSdfSegmentHasThinShell(float3 origin, float3 direction, float start, float end, uint level)
{
    float brickSize = VividGlobalSdfThinBrickSize * _GlobalSdfLevels[level].w;
    float3 gridPosition = (origin + start * direction) / brickSize;
    int3 brick = int3(floor(gridPosition));
    int3 step = int3(sign(direction));
    float3 face = float3(brick + max(step, 0));
    float3 inverseDirection = rcp(direction);
    float3 boundary = (face - gridPosition) * brickSize * inverseDirection;
    float3 delta = abs(brickSize * inverseDirection);
    boundary.x = step.x == 0 ? FLT_MAX : max(0.0, boundary.x);
    boundary.y = step.y == 0 ? FLT_MAX : max(0.0, boundary.y);
    boundary.z = step.z == 0 ? FLT_MAX : max(0.0, boundary.z);

    // The segment is clipped to a level and therefore crosses at most one ring extent per axis.
    [loop]
    for (int i = 0; i < 3 * VividGlobalSdfThinBrickResolution; i++)
    {
        if (VividGlobalSdfHasThinBrick(brick, level))
            return true;
        float next = min(boundary.x, min(boundary.y, boundary.z));
        if (next > end - start)
            break;
        if (boundary.x == next) { brick.x += step.x; boundary.x += delta.x; }
        if (boundary.y == next) { brick.y += step.y; boundary.y += delta.y; }
        if (boundary.z == next) { brick.z += step.z; boundary.z += delta.z; }
    }
    return false;
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
    out VividGlobalSdfHit hit, bool computeNormal = true)
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
    VividGlobalSdfHit thinHit = (VividGlobalSdfHit)0;
    thinHit.distance = FLT_MAX;
    [loop]
    for (int step = 0; step < 256 && t < exit.z; step++)
    {
        uint level = 2u;
        if (t >= entry.x && t < exit.x)
            level = 0u;
        else if (t >= entry.y && t < exit.y)
            level = 1u;

        float voxel = _GlobalSdfLevels[level].w;
        float tolerance = 0.18 * voxel;
        float3 position = originWS + t * directionWS;
        float distance = VividGlobalSdfSampleLevel(position, level);
        bool crossed = previousLevel == level && (distance < 0.0) != (previousDistance < 0.0);
        if (crossed || abs(distance) <= tolerance)
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
            if (thinHit.distance <= t)
                hit = thinHit;
            else
            {
                hit.distance = t;
                hit.level = level;
                if (computeNormal)
                    hit.normalWS = VividGlobalSdfNormal(originWS + t * directionWS, level);
            }
            return true;
        }

        if (thinHit.distance <= t)
        {
            hit = thinHit;
            return true;
        }

        // A ray only uses this level up to its exit or the next finer entry.
        float segmentEnd = exit[level];
        if (level > 0u && entry.x > t)
            segmentEnd = min(segmentEnd, entry.x);
        if (level > 1u && entry.y > t)
            segmentEnd = min(segmentEnd, entry.y);

        if (level != previousLevel && VividGlobalSdfSegmentHasThinShell(originWS, directionWS, t, segmentEnd, level))
        {
            VividMeshSdfHit meshHit;
            if (VividTraceMeshSdfSceneFiltered(originWS, directionWS, t, segmentEnd,
                _GlobalSdfLayerMask, voxel, meshHit))
            {
                thinHit.distance = meshHit.distance;
                thinHit.normalWS = meshHit.normalWS;
                thinHit.level = level;
                if (thinHit.distance <= t)
                {
                    hit = thinHit;
                    return true;
                }
            }
        }

        previousLevel = level;
        previousT = t;
        previousDistance = distance;
        float advance = max(tolerance, 0.65 * abs(distance));
        float next = min(t + advance, min(segmentEnd, thinHit.distance));
        if (next <= t)
            break;
        t = next;
    }
    if (thinHit.distance <= t && thinHit.distance < FLT_MAX)
    {
        hit = thinHit;
        return true;
    }
    return false;
}

#endif
