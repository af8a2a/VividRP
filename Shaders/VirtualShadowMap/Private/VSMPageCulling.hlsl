#ifndef VIVIDRP_VSM_PAGE_CULLING_INCLUDED
#define VIVIDRP_VSM_PAGE_CULLING_INCLUDED

#include "../Public/VividVirtualShadowMapAddressing.hlsl"

StructuredBuffer<uint3> _VSMPageCullHierarchy;

// Shared by source culling and the final per-page expansion. Directional VSM
// projections are orthographic; retain the same texel rounding at both stages.
bool VividVSMProjectCasterSphere(float4 sphereWS, float4x4 worldToShadow, uint resolution,
    out uint2 low, out uint2 high)
{
    float4 center = mul(worldToShadow, float4(sphereWS.xyz, 1.0));
    float inverseW = rcp(max(abs(center.w), 1e-6));
    float2 radius = sphereWS.w * float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz)) * inverseW;
    float2 minUV = center.xy * inverseW - radius, maxUV = center.xy * inverseW + radius;
    low = high = 0u;
    if (any(maxUV < 0.0) || any(minUV > 1.0)) return false;
    low = VividVSMUVToVirtualTexel(minUV, resolution);
    high = VividVSMUVToVirtualTexel(maxUV, resolution);
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
#include "../Public/VividVirtualShadowMapProjection.hlsl"
#include "VSMPageDefinitions.hlsl"
StructuredBuffer<uint4> _VSMUncachedPageRectBounds;
float4 _VSMCasterCullingParameters; // pages per axis, page size, resolution, caster layer
int _VSMReceiverMaskEnabled;

bool VividVSMCasterSphereOverlaps(uint level, float4 sphereWS)
{
    uint axis = (uint)_VSMCasterCullingParameters.x;
    uint pageSize = (uint)_VSMCasterCullingParameters.y;
    uint layer = (uint)_VSMCasterCullingParameters.w;
    uint2 low, high;
    if (!VividVSMProjectCasterSphere(sphereWS, _VSMProjections[level].worldToShadow,
            (uint)_VSMCasterCullingParameters.z, low, high)) return false;
    uint2 first = low / pageSize, last = high / pageSize;
    if (!VividVSMClipCasterRect(_VSMUncachedPageRectBounds[level * 2u + layer], pageSize,
            first, last, low, high)) return false;
    return VividVSMHierarchyOverlaps(level, axis, pageSize,
        layer == 0u ? kVSMPageDirty : kVSMPageDynamicDirty,
        layer != 0u && _VSMReceiverMaskEnabled != 0, low, high);
}
#endif
#endif
