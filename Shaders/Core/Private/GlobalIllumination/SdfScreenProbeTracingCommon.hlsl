#ifndef VIVID_SDF_SCREEN_PROBE_TRACING_COMMON_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_TRACING_COMMON_INCLUDED

struct ScreenRayHit
{
    float3 position;
    float3 normal;
    float distance;
    float thickness;
    uint2 pixel;
};

bool LoadScreenHitGeometry(uint2 pixel, float depth, out float3 position, out float3 normal)
{
    float4 packedNormal = _SdfDiffuseGBuffer1.Load(int3(pixel, 0));
    normal = VividDecodeSurfaceSummaryNormal(packedNormal.xy);
    float2 uv = (float2(pixel) + 0.5) * _SdfDiffuseScreenSize.zw;
    position = ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP);
    return depth != UNITY_RAW_FAR_CLIP_VALUE && VividIsSurfaceSummaryGBufferABIValid(packedNormal.a);
}

bool ReadScreenRadiance(uint2 pixel, float3 position, float3 normal, float thickness, out float3 radiance)
{
    radiance = 0.0;
    float4 previousClip = mul(_SdfDiffusePreviousVP, float4(position, 1.0));
    if (previousClip.w <= 0.0 || previousClip.z > previousClip.w || previousClip.z <= 0.0)
        return false;

    float4 packedMotion = float4(_SdfScreenMotion.Load(int3(pixel, 0)), 0.0, 0.0);
    if (PixelSetAsNoMotionVectors(packedMotion) || !all(isfinite(packedMotion.xy)))
        return false;
    float2 motion;
    DecodeMotionVector(packedMotion, motion);
    float2 previousUv = (float2(pixel) + 0.5) * _SdfDiffuseScreenSize.zw - motion + _SdfScreenJitterDelta;
    if (any(previousUv < 0.0) || any(previousUv >= 1.0))
        return false;

    uint2 previousPixel = uint2(previousUv * _SdfDiffuseScreenSize.xy);
    float2 geometry = _SdfScreenPreviousGeometry.Load(int3(previousPixel, 0));
    if (geometry.y < 0.0)
        return false;
    uint oct = (uint)geometry.y;
    float3 previousNormal = VividDecodeSurfaceSummaryNormal(float2(oct & 1023u, oct >> 10u) / 1023.0);
    if (dot(normal, previousNormal) < 0.92)
        return false;

    float2 sampleUv = (float2(previousPixel) + 0.5) * _SdfDiffuseScreenSize.zw;
    float3 previousPosition = ComputeWorldSpacePosition(sampleUv, geometry.x, _SdfScreenPreviousInverseVP);
    float3 expectedDepthPosition = ComputeWorldSpacePosition(sampleUv, previousClip.z / previousClip.w,
        _SdfScreenPreviousInverseVP);
    if (distance(previousPosition, expectedDepthPosition) > thickness)
        return false;

    radiance = _SdfScreenPreviousRadiance.Load(int3(previousPixel, 0)).rgb;
    return all(isfinite(radiance));
}

// Traverse a reversed-Z closest-depth pyramid in projected pixel space.
bool TraceScreenGeometry(float3 origin, float3 direction, float maxDistance, out ScreenRayHit hit,
    uint maxIterations = 48u)
{
    hit = (ScreenRayHit)0;
    float4 start = mul(_SdfDiffuseVP, float4(origin, 1.0));
    float4 end = mul(_SdfDiffuseVP, float4(origin + direction * maxDistance, 1.0));
    if (start.w <= 0.0 || start.z > start.w || start.z <= 0.0)
        return false;
    if (end.z > end.w)
        end = lerp(start, end, 0.999 * (start.w - start.z) / (start.w - start.z + end.z - end.w));
    if (end.w <= 0.0)
        return false;

    float2 screenSize = _SdfDiffuseScreenSize.xy;
    float2 startPixel = ScreenUv(start) * screenSize;
    float2 delta = ScreenUv(end) * screenSize - startPixel;
    float major = max(abs(delta.x), abs(delta.y));
    if (major < 1.0)
        return false;

    float epsilon = 0.002 / major;
    float startDepth = start.z / start.w;
    float endDepth = end.z / end.w;
    float t = epsilon;
    uint width, height, mips;
    _SdfScreenHzb.GetDimensions(0, width, height, mips);
    int maxMip = min(4, int(mips) - 1);
    int mip = maxMip;
    [loop]
    for (uint iteration = 0u; iteration < maxIterations && t < 1.0; iteration++)
    {
        float2 pixel = startPixel + delta * t;
        if (any(pixel < 0.0) || any(pixel >= screenSize))
            break;
        uint cellSize = 1u << mip;
        uint2 cell = uint2(pixel) >> mip;
        if (mip > 0 && any(cell >= (uint2(width, height) >> mip)))
        {
            // A floor-sized mip can omit the odd edge texel.
            mip--;
            continue;
        }

        float2 boundary = (float2(cell) + step(0.0, delta)) * cellSize;
        float2 exitT = float2(abs(delta.x) > 1e-6 ? (boundary.x - startPixel.x) / delta.x : 2.0,
            abs(delta.y) > 1e-6 ? (boundary.y - startPixel.y) / delta.y : 2.0);
        float nextT = min(1.0, max(t, min(exitT.x, exitT.y)));
        float closest = mip == 0 ? _SdfDiffuseDepth.Load(int3(cell, 0)) : _SdfScreenHzb.Load(int3(cell, mip));
        if (closest == UNITY_RAW_FAR_CLIP_VALUE
            || min(lerp(startDepth, endDepth, t), lerp(startDepth, endDepth, nextT)) > closest)
        {
            t = nextT + epsilon;
            mip = min(mip + 1, maxMip);
            continue;
        }
        if (mip > 0)
        {
            mip--;
            continue;
        }

        if (!LoadScreenHitGeometry(cell, closest, hit.position, hit.normal))
            return false;
        float2 uv = (float2(cell) + 0.5) * _SdfDiffuseScreenSize.zw;
        float3 footprint = ComputeWorldSpacePosition(uv + _SdfDiffuseScreenSize.zw, closest, _SdfDiffuseInverseVP);
        hit.thickness = clamp(1.25 * distance(footprint, hit.position), 0.02, 0.15);
        float travel = dot(hit.position - origin, direction);
        if (travel <= _SdfDiffuseRayBias || travel > maxDistance
            || dot(hit.normal, direction) >= -0.05
            || distance(hit.position, origin + direction * travel) > hit.thickness)
            return false;

        hit.pixel = cell;
        hit.distance = travel;
        return true;
    }
    return false;
}

// A positive finite bit pattern carries an exact linear pixel index in the dense RGBA32F scratch.
static const uint ScreenHitPixelBase = 0x3f800000u;

#endif
