bool UseVSMSMRT()
{
    return _VSMSMRTParameters.x > 0 && _VSMSMRTParameters.w > 0;
}

float VSMSMRTRayLength(int index, float texelSize)
{
    // End of this clipmap segment, measured from the original ray origin.
    // The next clipmap continues the SAME ray; only the configured world length
    // starts the parallel tail. The phase-independent bound limits each segment.
    // The terminal map must finish the requested length even without another LOD.
    if (index == _VSMProjectionCount - 1) return _VSMSMRTParameters.z;
    float radius = (clamp(_VSMSMRTParameters.y, 4, 8) - 2.001) * 0.70710678;
    return min(_VSMSMRTParameters.z, radius * texelSize
        / max(_VSMSMRTParameters.w, 1e-8));
}

float VSMSMRTRayLength(int index)
{
    return VSMSMRTRayLength(index, _VSMProjections[index].parameters.x);
}

float VSMFilterGuard(int index, bool smrt)
{
    float radius = _VSMReceiverParameters.x >= 0.5 ? 1.5 : 0;
    // UE traces through coarser pages per sample. Only the receiver/dither
    // support constrains the starting map; the full ray is not an edge guard.
    if (smrt) radius += 0.001;
    return radius / _VSMPrototypeVirtualResolution;
}

bool TryResolveVSMPhysicalTexelInternal(int2 virtualTexel, int cascadeIndex, out int2 physicalTexel,
    out uint pageFlags, bool checkReceiverMask)
{
    physicalTexel = 0;
    pageFlags = 0u;
    if (!UseVirtualShadowMapPrototype(cascadeIndex)
        || any(virtualTexel < 0) || any(virtualTexel >= _VSMPrototypeVirtualResolution))
        return false;
    uint pageSize = (uint)_VSMPrototypePageSize;
    uint pagesPerAxis = (uint)_VSMPrototypePagesPerAxis;
    uint2 virtualPage = (uint2)virtualTexel / pageSize;
    uint2 texelInPage = (uint2)virtualTexel % pageSize;
    uint page = (uint)cascadeIndex * pagesPerAxis * pagesPerAxis
        + virtualPage.y * pagesPerAxis + virtualPage.x;
    uint entry = _VSMPrototypePageTable[page];
    if (!VividVSMPageTableIsNative(entry))
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        uint flags = _VSMPrototypePageMetadata[page].x;
        g_VSMDebugMissing |= (flags & (kVSMPageDirty | kVSMPageDynamicDirty)) != 0u ? 2u : 1u;
#endif
        return false;
    }
    uint slot = VividVSMPageTableSlot(entry, (uint)_VSMPrototypePhysicalPagesPerRow);
#if defined(VIVID_VSM_RECEIVER_DEBUG) || defined(VIVID_VSM_LEGACY_DEPTH_TESTS)
    // Diagnostic flags/legacy layered-depth oracle only. Production validity
    // and physical coordinates come entirely from the packed page-table entry.
    uint4 metadata = _VSMPrototypePageMetadata[page];
    if (metadata.y != slot + 1u
        || (metadata.x & (kVSMPageAllocated | kVSMPageDirty | kVSMPageDynamicDirty)) != kVSMPageAllocated)
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= (metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty)) != 0u ? 2u : 0u;
        g_VSMDebugMissing |= metadata.y != slot + 1u || (metadata.x & kVSMPageAllocated) == 0u ? 4u : 0u;
#endif
        return false;
    }
    pageFlags = _VSMPageOccupancySkipDisabled != 0 ? 0u : metadata.x;
#endif
    if (checkReceiverMask && _VSMReceiverMaskEnabled != 0
        && !VividVSMReceiverMaskTexel(_VSMPhysicalReceiverMasks[slot], texelInPage, pageSize))
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= 16u;
#endif
        return false;
    }
    physicalTexel = (int2)(VividVSMPageTableAddress(entry) * pageSize + texelInPage);
    return true;
}

bool TryResolveVSMPhysicalTexel(int2 virtualTexel, int cascadeIndex, out int2 physicalTexel,
    out uint pageFlags)
{
    return TryResolveVSMPhysicalTexelInternal(virtualTexel, cascadeIndex, physicalTexel, pageFlags, true);
}

uint2 LoadVSMPhysicalReceiverMask(int2 physicalTexel)
{
    if (_VSMReceiverMaskEnabled == 0) return 0xffffffffu;
    uint2 page = (uint2)physicalTexel / (uint)_VSMPrototypePageSize;
    return _VSMPhysicalReceiverMasks[page.y * (uint)_VSMPrototypePhysicalPagesPerRow + page.x];
}

bool TryResolveVSMPhysicalTexel(int2 virtualTexel, int cascadeIndex, out int2 physicalTexel)
{
    uint pageFlags;
    return TryResolveVSMPhysicalTexel(virtualTexel, cascadeIndex, physicalTexel, pageFlags);
}

bool TryResolveVSMPhysicalTexel(float2 shadowUV, int cascadeIndex, out int2 physicalTexel)
{
    physicalTexel = 0;
    int2 virtualTexel;
    if (!VividVSMTryOffsetVirtualTexel(shadowUV, int2(0, 0),
            (uint)_VSMPrototypeVirtualResolution, virtualTexel))
        return false;
    return TryResolveVSMPhysicalTexel(virtualTexel, cascadeIndex, physicalTexel);
}

#if defined(VIVID_VSM_LEGACY_DEPTH_TESTS)
uint2 LoadVSMDepthLayer(int2 physicalTexel, uint layer, uint pageFlags)
{
    uint2 depths = 0u;
    [branch] if ((pageFlags & kVSMPageStaticEmpty) == 0u)
    {
        VSM_COST_ADD(16, layer == 0u ? 1u : 0u);
        VSM_COST_ADD(18, layer != 0u ? 1u : 0u);
        depths.x = _VSMPrototypeStaticPhysicalPage.Load(int4(physicalTexel, layer, 0));
    }
    [branch] if ((pageFlags & kVSMPageDynamicEmpty) == 0u)
    {
        VSM_COST_ADD(17, layer == 0u ? 1u : 0u);
        VSM_COST_ADD(19, layer != 0u ? 1u : 0u);
        depths.y = _VSMPrototypeDynamicPhysicalPage.Load(int4(physicalTexel, layer, 0));
    }
    VSM_COST_ADD(20, (pageFlags & kVSMPageStaticEmpty) != 0u ? 1u : 0u);
    VSM_COST_ADD(21, (pageFlags & kVSMPageDynamicEmpty) != 0u ? 1u : 0u);
    return depths;
}

#endif

uint LoadCombinedVSMDepth(int2 physicalTexel, uint pageFlags)
{
    // The final slice already contains max(static, dynamic), as in UE.
    // DynamicEmpty now describes this final slice, not a separate dynamic-only pool.
    VSM_COST_ADD(21, (pageFlags & kVSMPageDynamicEmpty) != 0u ? 1u : 0u);
    if ((pageFlags & kVSMPageDynamicEmpty) != 0u) return 0u;
    VSM_COST_ADD(17, 1u);
    return _VSMPhysicalPagePool.Load(int4(physicalTexel, VIVID_VSM_FINAL_DEPTH_SLICE, 0));
}

// One hard comparison. Every offset tap resolves its own virtual page; this is
// the cross-page contract used by both the hard reference and PCF.
bool TrySampleVSMVirtualTap(float2 shadowUV, int2 offset, float depth, int index, out float shadow)
{
    VSM_COST_ADD(23, 1u);
    shadow = 1.0;
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugWork.x++;
#endif
    int2 virtualTexel, physicalTexel;
    if (!VividVSMTryOffsetVirtualTexel(shadowUV, offset,
            (uint)_VSMPrototypeVirtualResolution, virtualTexel))
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= 8u;
#endif
        return false;
    }
    uint pageFlags;
    if (!TryResolveVSMPhysicalTexel(virtualTexel, index, physicalTexel, pageFlags))
        return false;
    uint rawDepth = LoadCombinedVSMDepth(physicalTexel, pageFlags);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugWork.y++;
#endif
    shadow = rawDepth != 0u && IsShadowMapDepthCloser(asfloat(rawDepth), depth) ? 0.0 : 1.0;
    return true;
}


uint LoadVSMPossibleMappedLevels(float3 positionWS, float3 normal, int index)
{
#if defined(VIVID_VSM_AVAILABLE_LEVEL_HINTS)
    if (_VSMAvailableLevelHintsEnabled == 0 || _VSMProjectionCount > VIVID_VSM_RASTER_MAX_LEVELS
        || !all(isfinite(positionWS)) || any(abs(positionWS) > kVSMHintWorldLimit)
        || !all(isfinite(normal)) || any(abs(normal) > 1.001)) return 0xffffffffu;
    // Key by the UNBIASED receiver. Bias is reapplied at each candidate level.
    float2 uv = mul(_VSMProjections[index].worldToShadow, float4(positionWS, 1)).xy;
    if (!all(isfinite(uv)) || any(uv < 0) || any(uv >= 1)) return 0xffffffffu;
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint2 page = (uint2)floor(uv * axis);
    return _VSMPossibleMappedLevels[((uint)index * axis + page.y) * axis + page.x];
#else
    return 0xffffffffu;
#endif
}
