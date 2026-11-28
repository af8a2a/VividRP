#ifndef VIVIDRP_VSM_PAGE_CULLING_INCLUDED
#define VIVIDRP_VSM_PAGE_CULLING_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VirtualShadowMap/VividVirtualShadowMapAddressing.hlsl"
#include "VSMGeometryBounds.hlsl"

StructuredBuffer<uint3> _VSMPageCullHierarchy;

// Shared by source culling and the final per-page expansion. Directional VSM
// projections are orthographic; retain the same texel rounding at both stages.
bool VividVSMProjectCasterSphere(float4 sphereWS, float4x4 worldToShadow, uint resolution,
    out uint2 low, out uint2 high)
{
    float4 center = mul(worldToShadow, float4(sphereWS.xyz, 1.0));
    float inverseW = rcp(max(abs(center.w), 1e-6));
    float2 radius = sphereWS.w * float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz)) * inverseW;
    // A resident LOD overflow record can represent an unbounded sphere. Keep
    // the whole viewport instead of converting NaN/Inf coordinates to texels.
    if (!all(isfinite(center)) || !all(isfinite(radius)))
    {
        low = 0u; high = resolution - 1u;
        return true;
    }
    float2 minUV = center.xy * inverseW - radius, maxUV = center.xy * inverseW + radius;
    low = high = 0u;
    if (any(maxUV < 0.0) || any(minUV > 1.0)) return false;
    low = VividVSMUVToVirtualTexel(minUV, resolution);
    high = VividVSMUVToVirtualTexel(maxUV, resolution);
    return true;
}

// Project an affine local bound directly into the directional shadow plane.
// This avoids inflating a nonuniformly scaled sphere into a world-space sphere.
bool VividVSMProjectLocalBoundsWpo(float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, float4x4 worldToShadow, float wpoPadWS, uint resolution,
    out uint2 low, out uint2 high)
{
    float4 projected = mul(localToShadow, float4(center, 1.0));
    float2 support = sphere ? radius * float2(length(localToShadow[0].xyz), length(localToShadow[1].xyz))
        : float2(dot(abs(localToShadow[0].xyz), extent), dot(abs(localToShadow[1].xyz), extent));
    if (wpoPadWS > 0.0)
    {
        support += wpoPadWS * float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz));
    }
    low = 0u; high = resolution - 1u;
    // Only directional/affine projections are tightened. Invalid bounds or
    // projective transforms conservatively retain the full virtual viewport.
    if (!all(isfinite(projected)) || !all(isfinite(support)) || abs(projected.w) < 1e-6
        || any(localToShadow[3].xyz != 0.0)) return true;
    float2 magnitude = float2(dot(abs(localToShadow[0].xyz), abs(center)) + abs(localToShadow[0].w),
        dot(abs(localToShadow[1].xyz), abs(center)) + abs(localToShadow[1].w)) + support;
    support += 1e-6 * magnitude + 1e-7; // Outward allowance for affine arithmetic.
    float2 uv = projected.xy / projected.w, span = support / abs(projected.w);
    if (any(uv + span < 0.0) || any(uv - span > 1.0)) return false;
    low = VividVSMUVToVirtualTexel(uv - span, resolution);
    high = VividVSMUVToVirtualTexel(uv + span, resolution);
    return true;
}

bool VividVSMProjectLocalBounds(float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, uint resolution, out uint2 low, out uint2 high)
{
    return VividVSMProjectLocalBoundsWpo(center, extent, radius, sphere, localToShadow, (float4x4)0, 0.0, resolution, low, high);
}

#if defined(VIVIDRP_VIRTUAL_SHADOW_MAP_PROJECTION_INCLUDED)
// O(1) distance + exact Spot cone pre-cull for local Point/Spot VSMs.
bool VividVSMInstanceIntersectsLocalLight(VividVSMProjection proj, float4 sphereWS)
{
    float3 toCenter = sphereWS.xyz - proj.selectionSphere.xyz;
    float maxDist = proj.selectionSphere.w + sphereWS.w;
    float distSq = dot(toCenter, toCenter);
    if (distSq > maxDist * maxDist)
        return false;

    if (proj.meta.x == VIVID_VSM_LIGHT_TYPE_SPOT && proj.lightDirectionAndSpot.w > -1.0)
    {
        if (distSq <= sphereWS.w * sphereWS.w)
            return true;
        float invDist = rsqrt(max(distSq, 1e-12));
        float cosHalf = clamp(proj.lightDirectionAndSpot.w, -1.0, 1.0);
        float sinHalf = sqrt(max(1.0 - cosHalf * cosHalf, 0.0));
        float sinBeta = saturate(sphereWS.w * invDist);
        float cosBeta = sqrt(max(1.0 - sinBeta * sinBeta, 0.0));
        float cosAlpha = dot(toCenter * invDist, proj.lightDirectionAndSpot.xyz);
        if (cosAlpha < cosHalf * cosBeta - sinHalf * sinBeta)
            return false;
    }
    else if (proj.meta.x == VIVID_VSM_LIGHT_TYPE_RECT)
    {
        // Rectangle AreaLight only illuminates the front hemisphere (forwardWS).
        if (dot(toCenter, proj.lightDirectionAndSpot.xyz) + sphereWS.w < 0.0)
            return false;
    }
    return true;
}
#endif

// Perspective 8-corner AABB/sphere projection into [0,1] shadow UV and Reverse-Z depth
// with UE bNearClip=true behavior: behind-light returns false, near-plane straddling
// conservatively retains the full viewport [0,1].
bool VividVSMProjectPerspectiveUVZ(float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, float4x4 worldToShadow, float wpoPadWS,
    out float4 uvRect, out float minZ, out float maxZ)
{
    uvRect = float4(0.0, 0.0, 1.0, 1.0);
    minZ = 0.0;
    maxZ = 1.0;

    float3 halfExt = sphere ? radius.xxx : extent;
    if (!all(isfinite(center)) || !all(isfinite(halfExt)) || any(halfExt < 0.0))
        return true;

    float4 c = mul(localToShadow, float4(center, 1.0));
    float4 ex = mul(localToShadow, float4(halfExt.x, 0.0, 0.0, 0.0));
    float4 ey = mul(localToShadow, float4(0.0, halfExt.y, 0.0, 0.0));
    float4 ez = mul(localToShadow, float4(0.0, 0.0, halfExt.z, 0.0));

    bool anyInFront = false;
    bool anyNearClip = false;
    float2 minUV = float2(1e9, 1e9);
    float2 maxUV = float2(-1e9, -1e9);
    float minD = 1e9;
    float maxD = -1e9;
    float minW = 1e9;

    [unroll]
    for (uint corner = 0u; corner < 8u; corner++)
    {
        float sx = (corner & 1u) != 0u ? 1.0 : -1.0;
        float sy = (corner & 2u) != 0u ? 1.0 : -1.0;
        float sz = (corner & 4u) != 0u ? 1.0 : -1.0;
        float4 p = c + sx * ex + sy * ey + sz * ez;
        if (p.w > 1e-5)
        {
            anyInFront = true;
            minW = min(minW, p.w);
            if (p.w <= 1e-3 || p.z > p.w)
            {
                anyNearClip = true;
            }
            else
            {
                float3 uvw = p.xyz / p.w;
                minUV = min(minUV, uvw.xy);
                maxUV = max(maxUV, uvw.xy);
                minD = min(minD, uvw.z);
                maxD = max(maxD, uvw.z);
            }
        }
        else
        {
            anyNearClip = true;
        }
    }

    if (!anyInFront)
        return false;
    if (anyNearClip)
        return true;

    if (wpoPadWS > 0.0)
    {
        float invMinW = rcp(max(minW, 1e-4));
        float2 uvPad = wpoPadWS * float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz)) * invMinW;
        float zPad = wpoPadWS * length(worldToShadow[2].xyz) * invMinW;
        minUV -= uvPad;
        maxUV += uvPad;
        minD -= zPad;
        maxD += zPad;
    }

    if (!all(isfinite(minUV)) || !all(isfinite(maxUV)) || !isfinite(minD) || !isfinite(maxD))
        return true;
    if (any(maxUV < 0.0) || any(minUV > 1.0) || maxD < 0.0 || minD > 1.0)
        return false;

    uvRect = saturate(float4(minUV, maxUV));
    minZ = saturate(minD);
    maxZ = saturate(maxD);
    return true;
}

bool VividVSMProjectPerspectiveBounds(float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, float4x4 worldToShadow, float wpoPadWS, uint resolution,
    out uint2 low, out uint2 high, out float minZ, out float maxZ)
{
    low = 0u;
    high = resolution - 1u;
    float4 uvRect;
    if (!VividVSMProjectPerspectiveUVZ(center, extent, radius, sphere, localToShadow, worldToShadow, wpoPadWS,
            uvRect, minZ, maxZ))
    {
        high = 0u;
        return false;
    }
    low = VividVSMUVToVirtualTexel(uvRect.xy, resolution);
    high = VividVSMUVToVirtualTexel(uvRect.zw, resolution);
    return true;
}

#if defined(VIVIDRP_VIRTUAL_SHADOW_MAP_PROJECTION_INCLUDED)
bool VividVSMProjectCasterBoundsUnified(VividVSMProjection proj, float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, float wpoPadWS, uint resolution, out uint2 low, out uint2 high)
{
    if (VividVSMProjectionIsDirectional(proj))
        return VividVSMProjectLocalBoundsWpo(center, extent, radius, sphere, localToShadow, proj.worldToShadow, wpoPadWS,
            resolution, low, high);
    float minZ, maxZ;
    return VividVSMProjectPerspectiveBounds(center, extent, radius, sphere, localToShadow, proj.worldToShadow, wpoPadWS,
        resolution, low, high, minZ, maxZ);
}
#endif

bool VividVSMFrustumVsLocalBounds(float4 planes[6], float3 center, float3 extent,
    float radius, bool sphere, float4x4 objectToWorld)
{
    [unroll] for (uint i = 0u; i < 6u; i++)
    {
        float4 plane = mul(planes[i], objectToWorld);
        float distance = dot(plane.xyz, center) + plane.w;
        float support = sphere ? radius * length(plane.xyz) : dot(abs(plane.xyz), extent);
        float allowance = 1e-6 * (dot(abs(plane.xyz), abs(center)) + abs(plane.w) + support) + 1e-7;
        if (distance < -support - allowance) return false;
    }
    return true;
}

bool VividVSMClipCasterRect(uint4 bounds, uint pageSize, inout uint2 minPage, inout uint2 maxPage,
    inout uint2 minTexel, inout uint2 maxTexel)
{
    minPage = max(minPage, bounds.xy);
    maxPage = min(maxPage, bounds.zw);
    if (any(minPage > maxPage)) return false;
    minTexel = max(minTexel, minPage * pageSize);
    maxTexel = min(maxTexel, (maxPage + 1u) * pageSize - 1u);
    return true;
}

bool VividVSMHierarchyOverlaps(uint level, uint axis,
    uint pageSize, uint dirtyFlag, bool useReceiverMask, uint2 low, uint2 high)
{
    uint mip = VividVSMHierarchyMipForRect(low / pageSize, high / pageSize);
    uint2 first = (low / pageSize) >> mip, last = (high / pageSize) >> mip;
    // H-mip selection guarantees at most 2x2 nodes. Fixed bounds also avoid
    // legacy compiler forced-unroll failures inside instance/LOD traversal.
    [unroll]
    for (uint dy = 0u; dy < 2u; dy++)
        [unroll]
        for (uint dx = 0u; dx < 2u; dx++)
        {
            uint2 coord = first + uint2(dx, dy);
            if (any(coord > last)) continue;
            uint3 node = _VSMPageCullHierarchy[VividVSMHierarchyAddress(level, coord, mip, axis)];
            if ((node.x & dirtyFlag) != 0u && (!useReceiverMask
                || VividVSMReceiverMaskOverlapsRect(node.yz, coord, low, high, pageSize << mip)))
                return true;
        }
    return false;
}

// Only the VSM variants of the generic GPU-driven shaders own these bindings.
// Main-view and CSM kernels have no dependency on VSM resources or stale globals.
#if defined(VIVID_VSM_EARLY_CULL)
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Shadow/VirtualShadowMap/VividVirtualShadowMapProjection.hlsl"
#include "../Paging/VSMPageDefinitions.hlsl"
StructuredBuffer<uint4> _VSMUncachedPageRectBounds;
float4 _VSMCasterCullingParameters; // pages per axis, page size, resolution, caster layer
int _VSMReceiverMaskEnabled;

bool VividVSMCasterRectOverlaps(uint level, uint2 low, uint2 high)
{
    uint axis = (uint)_VSMCasterCullingParameters.x;
    uint pageSize = (uint)_VSMCasterCullingParameters.y;
    uint layer = (uint)_VSMCasterCullingParameters.w;
    uint2 first = low / pageSize, last = high / pageSize;
    if (!VividVSMClipCasterRect(_VSMUncachedPageRectBounds[level * 2u + layer], pageSize,
            first, last, low, high)) return false;
    return VividVSMHierarchyOverlaps(level, axis, pageSize,
        layer == 0u ? kVSMPageDirty : kVSMPageDynamicDirty,
        layer != 0u && _VSMReceiverMaskEnabled != 0, low, high);
}

bool VividVSMCasterSphereOverlaps(uint level, float4 sphereWS)
{
    uint2 low, high;
    return VividVSMProjectCasterSphere(sphereWS, _VSMProjections[level].worldToShadow,
        (uint)_VSMCasterCullingParameters.z, low, high) && VividVSMCasterRectOverlaps(level, low, high);
}

bool VividVSMCasterLocalBoundsOverlaps(uint level, float3 center, float3 extent, float radius, bool sphere,
    float4x4 localToShadow, float wpoPadWS = 0.0)
{
    uint2 low, high;
    return VividVSMProjectLocalBoundsWpo(center, extent, radius, sphere, localToShadow,
        _VSMProjections[level].worldToShadow, wpoPadWS,
        (uint)_VSMCasterCullingParameters.z, low, high) && VividVSMCasterRectOverlaps(level, low, high);
}

#endif
#endif
