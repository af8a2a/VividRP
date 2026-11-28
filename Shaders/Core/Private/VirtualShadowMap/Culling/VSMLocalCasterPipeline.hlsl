#ifndef VIVIDRP_VSM_LOCAL_CASTER_PIPELINE_INCLUDED
#define VIVIDRP_VSM_LOCAL_CASTER_PIPELINE_INCLUDED

// Phase 2: Unified physical page allocation (with descriptor sync & local budget cap),
// per-(VSMId, Mip) UncachedPageRectBounds & H-mip hierarchy, GPU view compaction,
// and shared-append perspective instance/meshlet/page culling for Point & Spot VSMs.

int _VSMAllocatePassMode; // 0 = all, 1 = essential/coarse + directional, 2 = local primary
int _VSMAllocateStartVSMId;
int _VSMLocalMaxPhysicalPages;
int _VSMUnifiedMaxEntriesPerVSM;
int _VSMUnifiedTotalHierarchyNodes;
int _VSMUnifiedTotalBoundRects;
int _VSMCompactStartVSMId;
int _VSMUnifiedMaxActiveViews;

uint _VSMLocalSourceInstanceCount;
uint _VSMLocalMaxInstanceJobs;
uint _VSMLocalMaxPageRequestsPerList;
uint _VSMLocalForcedLODDepth;
float _VSMLocalLODErrorThreshold;
int _VSMLocalUseDrawSet;

StructuredBuffer<uint> _VSMPageRequestFlagsRO;
StructuredBuffer<uint> _VSMLocalDrawSetIndices;
RWStructuredBuffer<VividVSMActiveView> _VSMUnifiedActiveViewsRW;
StructuredBuffer<VividVSMActiveView> _VSMUnifiedActiveViews;
RWStructuredBuffer<uint> _VSMUnifiedActiveViewCountRW;
RWByteAddressBuffer _VSMUnifiedInstanceDispatchArgsRW;
RWStructuredBuffer<uint2> _VSMLocalInstanceJobsRW;
StructuredBuffer<uint2> _VSMLocalInstanceJobs;
RWByteAddressBuffer _VSMLocalJobDispatchArgsRW;
RWStructuredBuffer<uint4> _VSMLocalMeshletPageRequestsRW;
RWByteAddressBuffer _VSMLocalMeshletPageIndirectArgsRW;

// Slot 4 of _VSMPrototypeAllocatorCounters tracks retained + newly allocated local physical pages,
// keeping VSMAllocateNewPageMappingsUnified strictly within the 8-UAV hardware/Unity dispatch limit.
#define VIVID_VSM_ALLOC_COUNTER_LOCAL_PAGES 4u

// Decodes linear localEntry within a VSM's mip chain into (mipLevel, pageXY).
bool VividVSMDecodeProjectionLocalEntry(VividVSMProjection proj, uint localEntry,
    out uint mipLevel, out uint2 pageXY)
{
    mipLevel = 0u;
    pageXY = 0u;
    if (VividVSMProjectionIsSinglePage(proj))
    {
        return localEntry == 0u;
    }
    uint axis = max(proj.addressing.z, 1u);
    if (VividVSMProjectionIsDirectional(proj))
    {
        if (localEntry >= axis * axis)
            return false;
        pageXY = uint2(localEntry % axis, localEntry / axis);
        return true;
    }
    uint maxMips = max(proj.addressing.w, 1u);
    uint rem = localEntry;
    [loop]
    for (uint m = 0u; m < maxMips; m++)
    {
        uint mipAxis = VividVSMMipPagesPerAxis(axis, m);
        uint count = mipAxis * mipAxis;
        if (rem < count)
        {
            mipLevel = m;
            pageXY = uint2(rem % mipAxis, rem / mipAxis);
            return true;
        }
        rem -= count;
    }
    return false;
}

// Unified physical-page LRU update keeping _VSMPhysicalPageDescriptors in sync.
[numthreads(64, 1, 1)]
void VSMUpdatePhysicalPagesUnified(uint3 id : SV_DispatchThreadID)
{
    uint capacity = (uint)_VSMPrototypePhysicalPageCapacity;
    if (id.x >= capacity) return;
    if (id.x == 0u) _VSMPrototypeAllocatorCounters[VIVID_VSM_ALLOC_COUNTER_LOCAL_PAGES] = 0u;
    uint slot = _VSMPhysicalPageLists[id.x];
    uint owner = _VSMPrototypePhysicalPageOwners[slot];
    bool requested = false;
    bool isLocalRetained = false;
    if (owner != 0u)
    {
        uint page = owner - 1u;
        uint4 metadata = _VSMPrototypePageMetadata[page];
        metadata.w = metadata.x;
        requested = (_VSMPageRequestFlagsRO[page] & kVSMPageRequested) != 0u;
        if (!requested && (uint)_VSMPrototypeFeedbackFrameIndex - metadata.z > 1000u)
        {
            _VSMPrototypeWritablePageTable[page] = 0u;
            _VSMPrototypePageMetadata[page] = 0u;
            _VSMPrototypePhysicalPageOwners[slot] = 0u;
            _VSMPhysicalPageDescriptors[slot] = 0u;
            owner = 0u;
        }
        else
        {
            if (requested) metadata.z = (uint)_VSMPrototypeFeedbackFrameIndex;
            if (requested && _VSMReceiverMaskEnabled != 0)
                metadata.x = (metadata.x | kVSMPageDynamicDirty) & ~kVSMPageCached;
            _VSMPrototypePageMetadata[page] = metadata;
            uint vsmId, mip, flags;
            uint2 pageXY;
            VividVSMDecodePhysicalOwnerDesc(_VSMPhysicalPageDescriptors[slot], vsmId, mip, pageXY, flags);
            if ((flags & VIVID_VSM_OWNER_FLAG_VALID) != 0u
                && vsmId < (uint)_VSMProjectionCount
                && !VividVSMProjectionIsDirectional(_VSMProjections[vsmId]))
            {
                isLocalRetained = true;
            }
        }
    }
    if (owner != 0u) InterlockedAdd(_VSMPrototypeAllocatorCounters[0], 1u);
    if (isLocalRetained) InterlockedAdd(_VSMPrototypeAllocatorCounters[VIVID_VSM_ALLOC_COUNTER_LOCAL_PAGES], 1u);
    if (requested) VSMPushPhysicalPageList(2u * capacity, 4u * capacity + 1u, slot);
    if (owner == 0u) VSMPushPhysicalPageList(3u * capacity, 4u * capacity + 2u, slot);
    _VSMPhysicalPageLists[id.x] = requested || owner == 0u ? 0xffffffffu : slot;
}

// Unified 2D physical page allocator: (DivRoundUp(_VSMUnifiedMaxEntriesPerVSM, 64), _VSMProjectionCount, 1).
// Writes authoritative _VSMPhysicalPageDescriptors[slot] and enforces optional local page budget cap.
// Uses exactly 7 UAVs (<= 8 Unity dispatch limit) by reading _VSMPageRequestFlagsRO as SRV.
[numthreads(64, 1, 1)]
void VSMAllocateNewPageMappingsUnified(uint3 id : SV_DispatchThreadID)
{
    uint vsmId = id.y;
    if (vsmId >= (uint)_VSMProjectionCount || (int)vsmId < _VSMAllocateStartVSMId) return;
    VividVSMProjection proj = _VSMProjections[vsmId];
    uint mipLevel;
    uint2 pageXY;
    if (!VividVSMDecodeProjectionLocalEntry(proj, id.x, mipLevel, pageXY))
        return;

    uint page = VividVSMCalcProjectionPageIndex(proj, mipLevel, pageXY);
    if (page >= (uint)_VSMPrototypePageTableEntryCount)
        return;

    uint request = _VSMPageRequestFlagsRO[page];
    if ((request & kVSMPageRequested) == 0u)
        return;

    bool isDirectional = VividVSMProjectionIsDirectional(proj);
    bool isSinglePage = VividVSMProjectionIsSinglePage(proj);
    bool isEssential = (request & kVSMPageCoarseRequested) != 0u || isSinglePage;
    bool isPass1Eligible = isDirectional || isEssential;
    if (_VSMAllocatePassMode == 1 && !isPass1Eligible)
        return;
    if (_VSMAllocatePassMode == 2 && isPass1Eligible)
        return;

    uint capacity = (uint)_VSMPrototypePhysicalPageCapacity;
    InterlockedAdd(_VSMPrototypeAllocatorCounters[1], 1u);

    uint4 metadata = _VSMPrototypePageMetadata[page];
    if ((metadata.x & kVSMPageAllocated) == 0u)
    {
        bool allowAllocate = true;
        if (!isDirectional && !isEssential && _VSMLocalMaxPhysicalPages > 0)
        {
            uint prevLocal = 0u;
            InterlockedAdd(_VSMPrototypeAllocatorCounters[VIVID_VSM_ALLOC_COUNTER_LOCAL_PAGES], 1u, prevLocal);
            if (prevLocal >= (uint)_VSMLocalMaxPhysicalPages)
            {
                InterlockedAdd(_VSMPrototypeAllocatorCounters[VIVID_VSM_ALLOC_COUNTER_LOCAL_PAGES], 0xffffffffu);
                allowAllocate = false;
            }
        }

        int availableIndex = -1;
        if (allowAllocate)
        {
            uint previous = 0u;
            uint waveCount = WaveActiveCountBits(true);
            uint waveOffset = WavePrefixCountBits(true);
            if (WaveIsFirstLane())
                InterlockedAdd(_VSMPhysicalPageLists[4u * capacity], 0u - waveCount, previous);
            previous = WaveReadLaneFirst(previous);
            availableIndex = (int)previous - (int)waveOffset - 1;
        }

        if (availableIndex >= 0)
        {
            uint slot = _VSMPhysicalPageLists[capacity + (uint)availableIndex];
            uint owner = _VSMPrototypePhysicalPageOwners[slot];
            if (owner != 0u)
            {
                uint4 evicted = _VSMPrototypePageMetadata[owner - 1u];
                evicted.xy = 0u; evicted.w = kVSMPageDebugEvicted;
                _VSMPrototypePageMetadata[owner - 1u] = evicted;
                _VSMPrototypeWritablePageTable[owner - 1u] = 0u;
            }
            else
            {
                InterlockedAdd(_VSMPrototypeAllocatorCounters[0], 1u);
            }
            metadata.x = kVSMPageAllocated | kVSMPageDirty | kVSMPageDynamicDirty | kVSMPageStatic | kVSMPageDynamic;
            metadata.y = slot + 1u;
            metadata.z = (uint)_VSMPrototypeFeedbackFrameIndex;
            metadata.w = metadata.x;
            _VSMPrototypePageMetadata[page] = metadata;
            _VSMPrototypeWritablePageTable[page] = VividVSMEncodeNativePage(slot, (uint)_VSMPrototypePhysicalPagesPerRow, true);
            _VSMPrototypePhysicalPageOwners[slot] = page + 1u;
            uint ownerFlags = VIVID_VSM_OWNER_FLAG_VALID | (isSinglePage ? VIVID_VSM_OWNER_FLAG_SINGLE_PAGE : 0u);
            _VSMPhysicalPageDescriptors[slot] = VividVSMEncodePhysicalOwnerDesc(vsmId, mipLevel, pageXY, ownerFlags);
            if (_VSMReceiverMaskEnabled != 0) _VSMPhysicalReceiverMasks[slot] = 0u;

            uint offset = 0u;
            uint appendCount = WaveActiveCountBits(true);
            uint appendOffset = WavePrefixCountBits(true);
            if (WaveIsFirstLane())
                InterlockedAdd(_VSMPhysicalPageLists[4u * capacity + 1u], appendCount, offset);
            offset = WaveReadLaneFirst(offset) + appendOffset;
            _VSMPhysicalPageLists[2u * capacity + offset] = slot;
            InterlockedAdd(_VSMPrototypeAllocatorCounters[2], 1u);
        }
        else
        {
            metadata.w |= kVSMPageDebugOverflow;
            _VSMPrototypePageMetadata[page] = metadata;
            InterlockedAdd(_VSMPrototypeAllocatorCounters[3], 1u);
        }
    }
}

// Clears unified per-(VSMId, Mip) UncachedPageRectBounds and H-mip hierarchy nodes.
[numthreads(64, 1, 1)]
void VSMClearUnifiedPageBounds(uint3 id : SV_DispatchThreadID)
{
    if (id.x < (uint)_VSMUnifiedTotalHierarchyNodes)
        _VSMPageCullHierarchyRW[id.x] = 0u;
    if (id.x < (uint)_VSMUnifiedTotalBoundRects * 2u)
        _VSMUncachedPageRectBoundsRW[id.x] = uint4(0xFFFFu, 0xFFFFu, 0u, 0u);
}

// Builds per-(VSMId, Mip) UncachedPageRectBounds and mip-0 H-mip hierarchy from physical owners.
[numthreads(64, 1, 1)]
void VSMBuildUnifiedPageCullHierarchy(uint3 id : SV_DispatchThreadID)
{
    uint slot = id.x;
    if (slot >= (uint)_VSMPrototypePhysicalPageCapacity) return;
    uint owner = _VSMPrototypePhysicalPageOwners[slot];
    if (owner == 0u || owner > (uint)_VSMPrototypePageTableEntryCount) return;

    uint page = owner - 1u;
    uint4 metadata = _VSMPrototypePageMetadata[page];
    uint entry = _VSMPrototypePageTable[page];
    if (!VividVSMPageTableIsNative(entry)
        || VividVSMPageTableSlot(entry, (uint)_VSMPrototypePhysicalPagesPerRow) != slot
        || metadata.y != slot + 1u
        || (metadata.x & kVSMPageAllocated) == 0u)
    {
        return;
    }

    uint vsmId, mipLevel, ownerFlags;
    uint2 coord;
    VividVSMDecodePhysicalOwnerDesc(_VSMPhysicalPageDescriptors[slot], vsmId, mipLevel, coord, ownerFlags);
    if ((ownerFlags & VIVID_VSM_OWNER_FLAG_VALID) == 0u || vsmId >= (uint)_VSMProjectionCount)
        return;

    VividVSMProjection proj = _VSMProjections[vsmId];
    uint flags = kVSMPageAllocated;
    if ((metadata.x & kVSMPageDeferred) == 0u)
        flags |= metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty);
    uint2 mask = 0u;
    if ((flags & kVSMPageDynamicDirty) != 0u)
        mask = _VSMReceiverMaskEnabled != 0 ? _VSMPageReceiverMasks[page] : 0xffffffffu;

    uint boundRectSlot = proj.extended.y + mipLevel;
    for (uint layer = 0u; layer < 2u; layer++)
    {
        uint dirtyFlag = layer == 0u ? kVSMPageDirty : kVSMPageDynamicDirty;
        if ((flags & dirtyFlag) == 0u) continue;
        uint boundsIndex = boundRectSlot * 2u + layer;
        if (boundsIndex < (uint)_VSMUnifiedTotalBoundRects * 2u)
        {
            InterlockedMin(_VSMUncachedPageRectBoundsRW[boundsIndex].x, coord.x);
            InterlockedMin(_VSMUncachedPageRectBoundsRW[boundsIndex].y, coord.y);
            InterlockedMax(_VSMUncachedPageRectBoundsRW[boundsIndex].z, coord.x);
            InterlockedMax(_VSMUncachedPageRectBoundsRW[boundsIndex].w, coord.y);
        }
    }

    // Mip-0 spatial H-mip propagation (Single-Page VSMs skip H-mip propagation).
    if (mipLevel != 0u || VividVSMProjectionIsSinglePage(proj))
        return;
    uint axis = max(proj.addressing.z, 1u);
    uint hierarchyBase = proj.addressing.y;
    uint hierarchyAxis = VividVSMHierarchyAxis(axis);
    uint2 hCoord = coord;
    for (uint hMip = 0u; (hierarchyAxis >> hMip) > 0u; hMip++)
    {
        uint mipAxis = hierarchyAxis >> hMip;
        uint offset = (4u * (hierarchyAxis * hierarchyAxis - mipAxis * mipAxis)) / 3u;
        uint address = hierarchyBase + offset + hCoord.y * mipAxis + hCoord.x;
        if (address >= (uint)_VSMUnifiedTotalHierarchyNodes) break;
        uint previous;
        if (flags != 0u)
        {
            InterlockedOr(_VSMPageCullHierarchyRW[address].x, flags, previous);
            flags &= ~previous;
        }
        if (mask.x != 0u)
        {
            InterlockedOr(_VSMPageCullHierarchyRW[address].y, mask.x, previous);
            mask.x &= ~previous;
        }
        if (mask.y != 0u)
        {
            InterlockedOr(_VSMPageCullHierarchyRW[address].z, mask.y, previous);
            mask.y &= ~previous;
        }
        if ((flags | mask.x | mask.y) == 0u || mipAxis == 1u) break;
        if (any(mask != 0u)) mask = VividVSMReduceReceiverMask(mask, hCoord & 1u);
        hCoord >>= 1u;
    }
}

// Compacts active (VSMId, MipLevel) views that have non-empty uncached bounds for the current caster layer,
// and resets the shared local instance/meshlet indirect dispatch & draw argument buffers.
[numthreads(1, 1, 1)]
void VSMCompactUnifiedViews(uint3 id : SV_DispatchThreadID)
{
    uint activeCount = 0u;
    uint layer = (uint)_VSMPrototypeCasterLayer;
    uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    uint startVsm = (uint)max(_VSMCompactStartVSMId, 0);
    uint maxViews = (uint)max(_VSMUnifiedMaxActiveViews, 1);

    [loop]
    for (uint vsmId = startVsm; vsmId < (uint)_VSMProjectionCount; vsmId++)
    {
        VividVSMProjection proj = _VSMProjections[vsmId];
        if ((proj.meta.y & VIVID_VSM_PROJ_FLAG_UNREFERENCED) != 0u)
            continue;
        bool isSingle = VividVSMProjectionIsSinglePage(proj);
        uint minMip = isSingle ? 0u : proj.meta.z;
        uint maxMips = isSingle ? 1u : max(proj.addressing.w, 1u);
        [loop]
        for (uint mip = minMip; mip < maxMips; mip++)
        {
            uint boundRectIndex = proj.extended.y + mip;
            if (boundRectIndex >= (uint)_VSMUnifiedTotalBoundRects)
                break;
            uint4 bounds = _VSMUncachedPageRectBounds[boundRectIndex * 2u + layer];
            if (all(bounds.xy <= bounds.zw) && activeCount < maxViews)
            {
                uint mipPages = isSingle ? 1u : VividVSMMipPagesPerAxis(proj.addressing.z, mip);
                VividVSMActiveView view;
                view.vsmId = vsmId;
                view.mipLevel = mip;
                view.virtualResolution = mipPages * pageSize;
                view.boundRectIndex = boundRectIndex;
                _VSMUnifiedActiveViewsRW[activeCount++] = view;
            }
        }
    }

    _VSMUnifiedActiveViewCountRW[0] = activeCount;
    uint instGroups = (activeCount > 0u && _VSMLocalSourceInstanceCount > 0u)
        ? (_VSMLocalSourceInstanceCount + 63u) / 64u : 0u;
    _VSMUnifiedInstanceDispatchArgsRW.Store3(0u, uint3(instGroups, activeCount, 1u));
    // Reset shared job dispatch args: [0..2]=(0, 1, 1), [3]=jobCount=0.
    _VSMLocalJobDispatchArgsRW.Store4(0u, uint4(0u, 1u, 1u, 0u));

    // Reset per-renderer-list hardware raster draw args for local unified meshlet pages.
    for (uint listIdx = 0u; listIdx < VIVIDRENDERERLISTID_COUNT; listIdx++)
    {
        uint argsAddress = GetIndirectDrawArgsByteAddress(listIdx);
        uint startInstance = listIdx * _VSMLocalMaxPageRequestsPerList;
        _VSMLocalMeshletPageIndirectArgsRW.Store4(
            argsAddress,
            uint4(VIVID_MAX_MESHLET_INDICES, 0u, 0u, startInstance));
    }
}

bool VividVSMHierarchyOverlapsUnified(VividVSMProjection proj, uint mipLevel, uint dirtyFlag,
    bool useReceiverMask, uint2 low, uint2 high)
{
    if (mipLevel != 0u || VividVSMProjectionIsSinglePage(proj))
        return true;
    uint axis = max(proj.addressing.z, 1u);
    uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    uint hAxis = VividVSMHierarchyAxis(axis);
    uint hBase = proj.addressing.y;
    uint hMip = VividVSMHierarchyMipForRect(low / pageSize, high / pageSize);
    uint2 first = (low / pageSize) >> hMip;
    uint2 last = (high / pageSize) >> hMip;
    uint mipAxis = hAxis >> hMip;
    uint mipOffset = (4u * (hAxis * hAxis - mipAxis * mipAxis)) / 3u;
    [unroll]
    for (uint dy = 0u; dy < 2u; dy++)
    [unroll]
    for (uint dx = 0u; dx < 2u; dx++)
    {
        uint2 coord = first + uint2(dx, dy);
        if (any(coord > last)) continue;
        uint addr = hBase + mipOffset + coord.y * mipAxis + coord.x;
        if (addr >= (uint)_VSMUnifiedTotalHierarchyNodes) return true;
        uint3 node = _VSMPageCullHierarchy[addr];
        if ((node.x & dirtyFlag) != 0u
            && (!useReceiverMask || VividVSMReceiverMaskOverlapsRect(node.yz, coord, low, high, pageSize << hMip)))
            return true;
    }
    return false;
}

// Stage A: Perspective instance culling over compacted active (VSMId, Mip) views -> shared job append queue.
[numthreads(64, 1, 1)]
void VSMCullLocalInstancesCS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _VSMLocalSourceInstanceCount)
        return;

    uint instanceIndex = _VSMLocalUseDrawSet != 0 ? _VSMLocalDrawSetIndices[id.x] : id.x;
    VividInstanceCullHeader header;
    if (!TryPullInstanceCullHeader(instanceIndex, VIVIDINSTANCEPASSMASK_SHADOWS, header))
        return;

    VividVSMActiveView view = _VSMUnifiedActiveViews[id.y];
    if (view.vsmId >= (uint)_VSMProjectionCount)
        return;
    VividVSMProjection proj = _VSMProjections[view.vsmId];

    // 1. O(1) world-sphere vs light range + Spot cone pre-cull.
    float3 centerOS = 0.5 * (header.AABBMinOS + header.AABBMaxOS);
    float3 extentOS = 0.5 * (header.AABBMaxOS - header.AABBMinOS);
    float3 centerWS = mul(header.ObjectToWorldMatrix, float4(centerOS, 1.0)).xyz;
    float3 axisX = header.ObjectToWorldMatrix._m00_m10_m20 * extentOS.x;
    float3 axisY = header.ObjectToWorldMatrix._m01_m11_m21 * extentOS.y;
    float3 axisZ = header.ObjectToWorldMatrix._m02_m12_m22 * extentOS.z;
    float radiusWS = length(abs(axisX) + abs(axisY) + abs(axisZ)) + header.WpoPaddingWS;
    if (!VividVSMProjectionIsDirectional(proj)
        && !VividVSMInstanceIntersectsLocalLight(proj, float4(centerWS, radiusWS)))
    {
        return;
    }

    // 2. Perspective bounds projection + clip against (VSMId, Mip) UncachedPageRectBounds + H-mip.
    uint2 lowTexel, highTexel;
    float4x4 localToShadow = mul(proj.worldToShadow, header.ObjectToWorldMatrix);
    if (!VividVSMProjectCasterBoundsUnified(proj, centerOS, extentOS, 0.0, false,
            localToShadow, header.WpoPaddingWS, view.virtualResolution, lowTexel, highTexel))
    {
        return;
    }

    uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    uint layer = (uint)_VSMPrototypeCasterLayer;
    uint2 minPage = lowTexel / pageSize;
    uint2 maxPage = highTexel / pageSize;
    uint4 bounds = _VSMUncachedPageRectBounds[view.boundRectIndex * 2u + layer];
    if (!VividVSMClipCasterRect(bounds, pageSize, minPage, maxPage, lowTexel, highTexel))
        return;

    uint dirtyFlag = layer == 0u ? kVSMPageDirty : kVSMPageDynamicDirty;
    if (!VividVSMHierarchyOverlapsUnified(proj, view.mipLevel, dirtyFlag,
            layer != 0u && _VSMReceiverMaskEnabled != 0, lowTexel, highTexel))
    {
        return;
    }

    // Wave-aggregated append into shared global instance-view job queue.
    uint count = WaveActiveCountBits(true);
    uint laneOffset = WavePrefixCountBits(true);
    uint baseIdx = 0u;
    if (WaveIsFirstLane())
        _VSMLocalJobDispatchArgsRW.InterlockedAdd(12u, count, baseIdx);
    baseIdx = WaveReadLaneFirst(baseIdx);
    uint jobIdx = baseIdx + laneOffset;
    if (jobIdx < _VSMLocalMaxInstanceJobs)
    {
        _VSMLocalInstanceJobsRW[jobIdx] = uint2(instanceIndex, id.y);
        if (WaveIsFirstLane())
            _VSMLocalJobDispatchArgsRW.InterlockedMax(0u, min(baseIdx + count, _VSMLocalMaxInstanceJobs));
    }
}

void AppendVSMLocalPageWindow(uint rendererListIndex, uint instanceIndex, uint meshletId,
    uint originPageIndex, uint activeViewIndex, uint2 extent)
{
    uint argsAddress = GetIndirectDrawArgsByteAddress(rendererListIndex);
    uint localIdx = 0u;
    _VSMLocalMeshletPageIndirectArgsRW.InterlockedAdd(
        argsAddress + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET, 1u, localIdx);
    if (localIdx >= _VSMLocalMaxPageRequestsPerList)
    {
        _VSMLocalMeshletPageIndirectArgsRW.InterlockedAdd(
            argsAddress + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET, 0xffffffffu);
        return;
    }
    uint startInstance = _VSMLocalMeshletPageIndirectArgsRW.Load(
        argsAddress + VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET);
    _VSMLocalMeshletPageRequestsRW[startInstance + localIdx] = uint4(
        instanceIndex,
        meshletId,
        originPageIndex,
        VividVSMEncodeUnifiedPageWindow(activeViewIndex, extent));
}

// Stage B: Per-(instance, activeView) perspective LOD selection + meshlet perspective bounds +
// 4x4 windowed hardware raster request emission into shared queue.
// Dispatch indirect: (jobCount, 1, 1), threads: (32, 1, 1).
[numthreads(32, 1, 1)]
void VSMBuildLocalMeshletPagesCS(uint3 groupThreadID : SV_GroupThreadID, uint3 groupID : SV_GroupID)
{
    uint jobCount = _VSMLocalJobDispatchArgsRW.Load(12u);
    if (groupID.x >= min(jobCount, _VSMLocalMaxInstanceJobs))
        return;

    uint2 job = _VSMLocalInstanceJobs[groupID.x];
    uint instanceIndex = job.x;
    uint activeViewIndex = job.y;
    VividVSMActiveView view = _VSMUnifiedActiveViews[activeViewIndex];
    if (view.vsmId >= (uint)_VSMProjectionCount)
        return;
    VividVSMProjection proj = _VSMProjections[view.vsmId];

    VividInstanceLODTraversalData lodTraversal;
    if (!TryPullInstanceLODTraversalData(instanceIndex, lodTraversal))
        return;
    VividInstanceLODBuildData instanceData = PullInstanceLODBuildData(instanceIndex);
    uint rendererListIndex = min(
        ResolveRendererListID(instanceData.BaseRendererListID, instanceData.InstanceFlags, VIVIDINSTANCEPASSMASK_SHADOWS),
        VIVIDRENDERERLISTID_COUNT - 1u);

    VividGPULODSelectionContext localLod = (VividGPULODSelectionContext)0;
    localLod.ViewProjectionMatrix = proj.worldToClip;
    localLod.CameraPosition = float4(proj.selectionSphere.xyz, 1.0);
    localLod.ScreenSizePixels = float2((float)view.virtualResolution, (float)view.virtualResolution);

    uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    uint mipPages = max(view.virtualResolution / pageSize, 1u);
    uint layer = (uint)_VSMPrototypeCasterLayer;
    uint4 viewBounds = _VSMUncachedPageRectBounds[view.boundRectIndex * 2u + layer];
    float4x4 localToShadow = mul(proj.worldToShadow, lodTraversal.ObjectToWorldMatrix);
    uint windowSize = (uint)max(_VSMRasterWindowPages, 1);

    [loop]
    for (uint nodeOffset = groupThreadID.x; nodeOffset < lodTraversal.TotalMeshLODCount; nodeOffset += 32u)
    {
        uint nodeIndex = lodTraversal.TopMeshLODStartIndex + nodeOffset;
        if (nodeIndex >= _MeshLODNodeCount)
            continue;

        VividDecodedMeshLODNode node = PullMeshLODNode(nodeIndex);
        bool selected = ShouldSelectMeshLODNode(
            localLod,
            node,
            lodTraversal.ObjectToWorldMatrix,
            lodTraversal.MeshLODLevelCount,
            lodTraversal.LODErrorScale,
            _VSMLocalForcedLODDepth,
            _VSMLocalLODErrorThreshold,
            false);
        if (!selected)
            continue;

        // Node-level perspective bounds reject before walking child meshlets.
        uint2 nodeLow, nodeHigh;
        if (!VividVSMProjectCasterBoundsUnified(proj, node.Bounds.xyz, 0.0, node.Bounds.w, true,
                localToShadow, lodTraversal.WpoPaddingWS, view.virtualResolution, nodeLow, nodeHigh))
        {
            continue;
        }
        uint2 nodeMinPage = nodeLow / pageSize;
        uint2 nodeMaxPage = nodeHigh / pageSize;
        if (!VividVSMClipCasterRect(viewBounds, pageSize, nodeMinPage, nodeMaxPage, nodeLow, nodeHigh))
            continue;

        [loop]
        for (uint m = 0u; m < node.MeshletCount; m++)
        {
            uint meshletId = node.MeshletStartIndex + m;
            if (meshletId >= _MeshletCount)
                continue;
            VividDecodedMeshlet meshlet = PullMeshletData(meshletId);
            uint2 minTexel, maxTexel;
            if (!VividVSMProjectCasterBoundsUnified(proj, meshlet.BoundingSphere.xyz, 0.0, meshlet.BoundingSphere.w, true,
                    localToShadow, lodTraversal.WpoPaddingWS, view.virtualResolution, minTexel, maxTexel))
            {
                continue;
            }
            uint2 minPage = min(minTexel / pageSize, mipPages - 1u);
            uint2 maxPage = min(maxTexel / pageSize, mipPages - 1u);
            if (!VividVSMClipCasterRect(viewBounds, pageSize, minPage, maxPage, minTexel, maxTexel))
                continue;

            // Emit 4x4 (or _VSMRasterWindowPages) hardware raster windows covering only relevant pages.
            [loop]
            for (uint wy = minPage.y; wy <= maxPage.y; wy += windowSize)
            [loop]
            for (uint wx = minPage.x; wx <= maxPage.x; wx += windowSize)
            {
                uint2 low = uint2(wx, wy);
                uint2 high = min(low + windowSize - 1u, maxPage);
                bool nonempty = false;
                uint2 clippedLow = high;
                uint2 clippedHigh = low;
                [loop]
                for (uint py = low.y; py <= high.y; py++)
                [loop]
                for (uint px = low.x; px <= high.x; px++)
                {
                    uint pageIdx = VividVSMCalcProjectionPageIndex(proj, view.mipLevel, uint2(px, py));
                    if (IsVSMCasterPageRelevant(pageIdx)
                        && VSMCasterOverlapsReceiverMask(pageIdx, uint2(px, py), minTexel, maxTexel))
                    {
                        nonempty = true;
                        clippedLow = min(clippedLow, uint2(px, py));
                        clippedHigh = max(clippedHigh, uint2(px, py));
                    }
                }
                if (nonempty)
                {
                    uint originPage = VividVSMCalcProjectionPageIndex(proj, view.mipLevel, clippedLow);
                    AppendVSMLocalPageWindow(rendererListIndex, instanceIndex, meshletId,
                        originPage, activeViewIndex, clippedHigh - clippedLow + 1u);
                }
            }
        }
    }
}

#endif
