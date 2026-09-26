bool UseVSMSMRT()
{
    return _VSMSMRTParameters.x >= 4 && _VSMSMRTParameters.w > 0;
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
    if (smrt)
        radius += VSMSMRTRayLength(index) * _VSMSMRTParameters.w
            / _VSMProjections[index].parameters.x + 0.001;
    return radius / _VSMPrototypeVirtualResolution;
}

bool TryResolveVSMPhysicalTexel(int2 virtualTexel, int cascadeIndex, out int2 physicalTexel,
    out uint pageFlags)
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
    uint encoded = _VSMPrototypePageTable[page];
    uint4 metadata = _VSMPrototypePageMetadata[page];
    // An empty but completed page is valid (lit); unmapped or dirty pages are
    // unavailable, not an implicit lit sample to blend into an existing shadow.
    if (encoded == 0u || metadata.y != encoded
        || (metadata.x & (kVSMPageAllocated | kVSMPageDirty | kVSMPageDynamicDirty)) != kVSMPageAllocated)
    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugMissing |= encoded == 0u ? 1u : 0u;
        g_VSMDebugMissing |= (metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty)) != 0u ? 2u : 0u;
        g_VSMDebugMissing |= metadata.y != encoded
            || (encoded != 0u && (metadata.x & kVSMPageAllocated) == 0u) ? 4u : 0u;
#endif
        return false;
    }
    uint slot = encoded - 1u;
    uint rowSize = (uint)_VSMPrototypePhysicalPagesPerRow;
    physicalTexel = (int2)(uint2(slot % rowSize, slot / rowSize) * pageSize + texelInPage);
    pageFlags = _VSMPageOccupancySkipDisabled != 0 ? 0u : metadata.x;
    return true;
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

uint LoadCombinedVSMDepth(int2 physicalTexel, uint pageFlags)
{
    uint2 depths = LoadVSMDepthLayer(physicalTexel, 0, pageFlags);
    return max(depths.x, depths.y);
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

