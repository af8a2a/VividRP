// VirtualShadowMapPhysicalPageManagement.usf / NaniteHZBCull.ush semantics:
// reversed-Z farthest reduction, half-resolution mip 0, seven mips per page.
Texture2DArray<float> _VSMHZB;
RWTexture2DArray<float> _VSMHZBOut0, _VSMHZBOut1, _VSMHZBOut2, _VSMHZBOut3;
RWTexture2DArray<float> _VSMHZBOut4, _VSMHZBOut5, _VSMHZBOut6;
StructuredBuffer<uint> _VSMHZBPreviousTable;
RWStructuredBuffer<uint> _VSMHZBPreviousTableRW;
StructuredBuffer<uint> _VSMHZBOwnersRead;
StructuredBuffer<uint4> _VSMHZBMetadataRead;
StructuredBuffer<VividVSMProjection> _VSMHZBPreviousProjections;
RWStructuredBuffer<VividVSMProjection> _VSMHZBPreviousProjectionsRW;
RWStructuredBuffer<uint4> _VSMHZBDeferred;
// Dispatch xyz, deferred count, recovered count. A source emits at most once.
RWByteAddressBuffer _VSMHZBDeferredArgs;
int _VSMHZBEnabled, _VSMHZBHistoryValid, _VSMHZBBuildLayer, _VSMHZBFullBuild;

ByteAddressBuffer _VSMHZBPageWorkArgs;
RWByteAddressBuffer _VSMHZBBuildArgs;
groupshared float g_VSMHZBDepth[256];

[numthreads(1, 1, 1)]
void VSMPrepareShadowHZB(uint3 id : SV_DispatchThreadID)
{
    uint pages = _VSMHZBFullBuild != 0 ? (uint)_VSMPrototypePhysicalPageCapacity
        : _VSMHZBPageWorkArgs.Load(12u);
    _VSMHZBBuildArgs.Store3(0u, uint3(pages * 16u, 1u, 1u));
    _VSMHZBBuildArgs.Store3(12u, uint3(pages, 1u, 1u));
}

bool VSMHZBPage(uint index, out uint2 page)
{
    uint slot = _VSMHZBFullBuild != 0 ? index
        : _VSMPageWorkList[(uint)_VSMPrototypePhysicalPageCapacity + index];
    uint owner = _VSMHZBOwnersRead[slot];
    page = uint2(slot % (uint)_VSMPrototypePhysicalPagesPerRow,
        slot / (uint)_VSMPrototypePhysicalPagesPerRow);
    if (owner == 0u) return false;
    return (_VSMHZBMetadataRead[owner - 1u].x & kVSMPageDeferred) == 0u;
}

void VSMHZBStore(uint mip, uint3 pos, float depth)
{
    switch (mip)
    {
    case 0: _VSMHZBOut0[pos] = depth; break;
    case 1: _VSMHZBOut1[pos] = depth; break;
    case 2: _VSMHZBOut2[pos] = depth; break;
    case 3: _VSMHZBOut3[pos] = depth; break;
    default: _VSMHZBOut4[pos] = depth; break;
    }
}

// UE BuildHZBPerPageCS: sixteen 32x32 source tiles per physical page.
// Each 16x16 group produces mips 64, 32, 16, 8, 4, using 1 KiB LDS.
[numthreads(16, 16, 1)]
void VSMBuildShadowHZB(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint2 page;
    if (!VSMHZBPage(group.x / 16u, page)) return;
    uint tile = group.x % 16u;
    uint2 origin = page * 128u + uint2(tile & 3u, tile >> 2u) * 32u;
    // ReductionCommon.ush: reverse Morton bits so every reduction reads
    // contiguous LDS banks and the surviving lanes stay together.
    uint2 pixel = 0u;
    [unroll] for (uint bit = 0u; bit < 4u; bit++)
        pixel |= uint2((lane >> (2u * bit)) & 1u, (lane >> (2u * bit + 1u)) & 1u) << (3u - bit);
    uint2 source = origin + pixel * 2u;
    float4 staticDepth;
    staticDepth.x = asfloat(_VSMPhysicalPagePool.Load(int4(source, 1, 0)));
    staticDepth.y = asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(1, 0), 1, 0)));
    staticDepth.z = asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(0, 1), 1, 0)));
    staticDepth.w = asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(1, 1), 1, 0)));
    // Static first, retaining four depths for the pointwise dynamic merge.
    for (int slice = 1; slice >= 0; slice--)
    {
        if (_VSMHZBBuildLayer < 2 && slice != _VSMHZBBuildLayer) continue;
        float4 values = staticDepth;
        if (slice == 0)
        {
            values.x = max(values.x, asfloat(_VSMPhysicalPagePool.Load(int4(source, 0, 0))));
            values.y = max(values.y, asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(1, 0), 0, 0))));
            values.z = max(values.z, asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(0, 1), 0, 0))));
            values.w = max(values.w, asfloat(_VSMPhysicalPagePool.Load(int4(source + uint2(1, 1), 0, 0))));
        }
        float depth = min(min(values.x, values.y), min(values.z, values.w));
        g_VSMHZBDepth[lane] = depth;
        uint2 output = source >> 1u;
        _VSMHZBOut0[uint3(output, slice)] = depth;
        uint lanes = WaveGetLaneCount();
        [unroll] for (uint mip = 1u; mip < 5u; mip++)
        {
            uint dim = 16u >> mip, bank = dim * dim;
            if ((bank << 2u) > lanes) GroupMemoryBarrierWithGroupSync();
            if (lane < bank)
            {
                depth = min(min(depth, g_VSMHZBDepth[lane + bank]),
                    min(g_VSMHZBDepth[lane + 2u * bank], g_VSMHZBDepth[lane + 3u * bank]));
                output >>= 1u;
                VSMHZBStore(mip, uint3(output, slice), depth);
                g_VSMHZBDepth[lane] = depth;
            }
        }
    }
}

// UE BuildHZBPerPageTopCS: one 2x2 group per page produces mips 2 and 1.
[numthreads(2, 2, 1)]
void VSMBuildShadowHZBTop(uint3 group : SV_GroupID, uint3 tid : SV_GroupThreadID)
{
    uint2 page;
    if (!VSMHZBPage(group.x, page)) return;
    uint2 source = page * 4u + tid.xy * 2u;
    for (uint slice = 0u; slice < 2u; slice++)
    {
        if (_VSMHZBBuildLayer < 2 && slice != (uint)_VSMHZBBuildLayer) continue;
        float depth = min(min(_VSMHZB.Load(int4(source, slice, 4)),
            _VSMHZB.Load(int4(source + uint2(1, 0), slice, 4))),
            min(_VSMHZB.Load(int4(source + uint2(0, 1), slice, 4)),
                _VSMHZB.Load(int4(source + uint2(1, 1), slice, 4))));
        g_VSMHZBDepth[tid.y * 2u + tid.x] = depth;
        _VSMHZBOut5[uint3(page * 2u + tid.xy, slice)] = depth;
        GroupMemoryBarrierWithGroupSync();
        if (all(tid.xy == 0u))
            _VSMHZBOut6[uint3(page, slice)] = min(min(g_VSMHZBDepth[0], g_VSMHZBDepth[1]),
                min(g_VSMHZBDepth[2], g_VSMHZBDepth[3]));
        GroupMemoryBarrierWithGroupSync();
    }
}

[numthreads(64, 1, 1)]
void VSMSnapshotShadowHZB(uint3 id : SV_DispatchThreadID)
{
    if (id.x < (uint)_VSMProjectionCount)
        _VSMHZBPreviousProjectionsRW[id.x] = _VSMProjections[id.x];
    if (id.x >= (uint)_VSMPrototypePageTableEntryCount) return;
    uint flags = _VSMPrototypePageMetadata[id.x].x;
    bool complete = (flags & kVSMPageAllocated) != 0u
        && (flags & (kVSMPageDirty | kVSMPageDynamicDirty | kVSMPageDeferred)) == 0u;
    uint entry = _VSMPrototypePageTable[id.x];
    _VSMHZBPreviousTableRW[id.x] = complete && VividVSMPageTableIsNative(entry) ? entry : 0u;
}

[numthreads(1, 1, 1)]
void VSMResetHZBDeferred(uint3 id : SV_DispatchThreadID)
{
    _VSMHZBDeferredArgs.Store4(0u, uint4(0u, 1u, 1u, 0u));
    _VSMHZBDeferredArgs.Store(16u, 0u);
}

[numthreads(64, 1, 1)]
void VSMResetHZBPostDraws(uint3 id : SV_DispatchThreadID)
{
    if (id.x < 2u * VIVIDRENDERERLISTID_COUNT)
        _VSMPrototypeMeshletPageIndirectArgs.Store(GetIndirectDrawArgsByteAddress(id.x)
            + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET, 0u);
}

#if defined(VIVID_VSM_SHADOW_HZB)
// Nanite GetScreenRect(..., 4) + VirtualShadowMapPageOverlap.ush.
// Missing previous pages are only a MAIN-pass guess; the POST pass tests the
// current dirty pages and recovers the candidate before those pages are finalized.
bool VSMHZBRectOccluded(float2 low, float2 high, float depth, uint level, bool previous,
    bool maskToRenderedPages = false)
{
    uint resolution = (uint)_VSMPrototypeVirtualResolution;
    int2 pixelLow = clamp((int2)(saturate(low) * resolution + 0.5), 0, (int)resolution - 1);
    int2 pixelHigh = min((int2)(saturate(high) * resolution - 0.5), (int)resolution - 1);
    if (any(pixelHigh < pixelLow)) return true; // No pixel center can be rasterized.
    uint2 baseLow = (uint2)pixelLow >> 1u, baseHigh = (uint2)pixelHigh >> 1u;
    uint2 delta = baseHigh - baseLow;
    int2 mipXY = firstbithigh(delta);
    int mip = max(max(mipXY.x, mipXY.y) - 1, 0);
    mip += any((baseHigh >> mip) - (baseLow >> mip) > 3u) ? 1 : 0;
    // Cluster path: don't clamp a large rectangle down to the page level.
    // UE's instance/node traversal has a separate clamped-page variant.
    if (mip > 4) return false;
    uint2 first = baseLow >> mip, last = baseHigh >> mip;
    uint size = 64u >> mip, axis = (uint)_VSMPrototypePagesPerAxis;
    uint2 firstPage = first / size, lastPage = last / size;
    for (uint py = firstPage.y; py <= lastPage.y; py++)
    for (uint px = firstPage.x; px <= lastPage.x; px++)
    {
        uint index = level * axis * axis + py * axis + px;
        uint encoded;
        if (previous) encoded = _VSMHZBPreviousTable[index];
        else
        {
            uint flags = _VSMPrototypePageMetadata[index].x;
            uint dirty = _VSMPrototypeCasterLayer == 0 ? kVSMPageDirty : kVSMPageDynamicDirty;
            if (maskToRenderedPages && ((flags & dirty) == 0u || (flags & kVSMPageDeferred) != 0u)) continue;
            if ((flags & kVSMPageAllocated) == 0u || (flags & kVSMPageDeferred) != 0u) return false;
            encoded = _VSMPrototypePageTable[index];
        }
        if (!VividVSMPageTableIsNative(encoded)
            || VividVSMPageTableSlot(encoded, (uint)_VSMPrototypePhysicalPagesPerRow) >= (uint)_VSMPrototypePhysicalPageCapacity)
        {
            if (maskToRenderedPages) continue;
            return false;
        }
        uint2 pageOrigin = uint2(px, py) * size;
        uint2 cellLow = max(first, pageOrigin) - pageOrigin;
        uint2 cellHigh = min(last, pageOrigin + size - 1u) - pageOrigin;
        uint2 physical = VividVSMPageTableAddress(encoded) * size;
        uint slice = _VSMPrototypeCasterLayer == 0 ? 1u : 0u;
        float furthest = 1.0;
        for (uint y = cellLow.y; y <= cellHigh.y; y++)
        for (uint x = cellLow.x; x <= cellHigh.x; x++)
            furthest = min(furthest, _VSMHZB.Load(int4(physical + uint2(x, y), slice, mip)));
        if (depth >= furthest) return false;
    }
    return true;
}

bool VSMHZBOccluded(VividMeshletRenderRequestPacked request, uint level, bool previous)
{
    if (_VSMHZBEnabled == 0 || (previous && _VSMHZBHistoryValid == 0)) return false;
    VividInstanceData instance = PullInstanceData(request.InstanceID_LOD);
    VividDecodedMeshlet meshlet = PullMeshletData(request.MeshletID);
    VividVSMLocalBounds bounds = VividVSMLoadMeshletBounds(request.MeshletID, meshlet.BoundingSphere);
    float4x4 projection = previous ? _VSMHZBPreviousProjections[level].worldToShadow
        : _VSMProjections[level].worldToShadow;
    float4x4 transform = mul(projection, instance.ObjectToWorldMatrix);
    float4 center = mul(transform, float4(bounds.center, 1.0));
    float3 support = bounds.sphere ? bounds.radius * float3(length(transform[0].xyz),
        length(transform[1].xyz), length(transform[2].xyz)) : float3(
        dot(abs(transform[0].xyz), bounds.extent), dot(abs(transform[1].xyz), bounds.extent),
        dot(abs(transform[2].xyz), bounds.extent));
    float3 magnitude = float3(dot(abs(transform[0].xyz), abs(bounds.center)) + abs(transform[0].w),
        dot(abs(transform[1].xyz), abs(bounds.center)) + abs(transform[1].w),
        dot(abs(transform[2].xyz), abs(bounds.center)) + abs(transform[2].w)) + support;
    support += 1e-6 * magnitude + 1e-7;
    if (!all(isfinite(center)) || !all(isfinite(support)) || center.w <= 0.0
        || any(transform[3].xyz != 0.0)) return false;
    float3 low = (center.xyz - support) / center.w, high = (center.xyz + support) / center.w;
    if (high.z >= 1.0 || high.z <= 0.0) return false;
    return VSMHZBRectOccluded(low.xy, high.xy, high.z, level, previous, true);
}
#endif
