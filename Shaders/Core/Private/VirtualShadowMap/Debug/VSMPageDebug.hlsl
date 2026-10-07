#ifndef VIVID_VSM_PAGE_DEBUG_INCLUDED
#define VIVID_VSM_PAGE_DEBUG_INCLUDED

// Read-only helpers. Include VSMPageDefinitions.hlsl before this file.
// Green: both cached; blue: static cached / dynamic rebuilt; red: static rebuilt.
// Orange: deferred; grey: unmapped. Snapshot w survives finalize; x has live validity.
float3 VSMDebugCacheColor(uint4 metadata, bool allocated, uint pool)
{
    if (!allocated) return float3(0.3, 0.3, 0.3);
    uint dirtyMask = pool == 1u ? kVSMPageDirty
        : pool == 2u ? kVSMPageDynamicDirty : (kVSMPageDirty | kVSMPageDynamicDirty);
    if ((metadata.x & kVSMPageDeferred) != 0u && (metadata.x & dirtyMask) != 0u)
        return float3(1.0, 0.6, 0.0);
    uint dirty = (metadata.x | metadata.w) & dirtyMask;
    if (dirty == 0u) return float3(0.1, 0.8, 0.2);
    if (pool == 0u && (dirty & kVSMPageDirty) == 0u) return float3(0.1, 0.4, 1.0);
    return float3(1.0, 0.1, 0.1);
}

float3 VSMDebugIndexColor(uint index)
{
    return 0.2 + 0.8 * frac(float3(0.618034, 0.381966, 0.754878) * (index + 1u));
}

float3 VSMDebugRequestColor(uint flags)
{
    if ((flags & kVSMPageRequested) == 0u) return float3(0.015, 0.015, 0.015);
    if ((flags & kVSMPageCoarseRequested) != 0u) return float3(0.2, 0.5, 1.0);
    if ((flags & kVSMPageParentRequested) != 0u) return float3(0.2, 1.0, 1.0);
    if ((flags & kVSMPagePrimaryRequested) != 0u) return float3(1.0, 0.85, 0.1);
    if ((flags & kVSMPageTransitionRequested) != 0u) return float3(0.8, 0.3, 1.0);
    return 1.0;
}

float3 VSMDebugOccupancyColor(uint flags, bool allocated)
{
    if (!allocated) return float3(0.3, 0.3, 0.3);
    if ((flags & (kVSMPageDirty | kVSMPageDynamicDirty | kVSMPageDeferred)) != 0u)
        return float3(1.0, 0.6, 0.0);
    uint known = kVSMPageStaticOccupancyKnown | kVSMPageDynamicOccupancyKnown;
    if ((flags & known) != known) return float3(1.0, 0.0, 1.0);
    bool occupiedStatic = (flags & kVSMPageStaticEmpty) == 0u;
    bool occupiedDynamic = (flags & kVSMPageDynamicEmpty) == 0u;
    return float3(occupiedStatic ? 0.1 : 0.0, occupiedStatic ? 0.8 : 0.0,
        occupiedDynamic ? 1.0 : 0.0);
}
#endif
