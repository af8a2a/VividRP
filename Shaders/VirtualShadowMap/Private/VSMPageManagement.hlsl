uint GetVSMSourceDrawArgsIndex(
    uint cascadeIndex,
    uint rendererListIndex)
{
    return cascadeIndex * VIVIDRENDERERLISTID_COUNT
        + rendererListIndex;
}

uint GetVSMPageDrawArgsAddress(uint rendererListIndex)
{
    return GetIndirectDrawArgsByteAddress(rendererListIndex);
}

bool IsVSMCasterPageRelevant(uint virtualPageIndex)
{
    if (virtualPageIndex >= (uint)_VSMPrototypePageTableEntryCount
        || _VSMPrototypePageTable[virtualPageIndex] == 0u)
    {
        return false;
    }

    const uint flags = _VSMPrototypePageMetadata[virtualPageIndex].x;
    if ((flags & kVSMPageAllocated) == 0u || (flags & kVSMPageDeferred) != 0u)
        return false;

    return (flags & (_VSMPrototypeCasterLayer == 0 ? kVSMPageDirty : kVSMPageDynamicDirty)) != 0u;
}

void AppendVSMPageMeshletRequest(
    uint rendererListIndex,
    VividMeshletRenderRequestPacked sourceRequest,
    uint virtualPageIndex,
    uint cascadeIndex)
{
    const uint drawArgsAddress = GetVSMPageDrawArgsAddress(
        rendererListIndex);
    uint localRequestIndex;
    _VSMPrototypeMeshletPageIndirectArgs.InterlockedAdd(
        drawArgsAddress + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET,
        1u,
        localRequestIndex);
    const uint startInstance = _VSMPrototypeMeshletPageIndirectArgs.Load(
        drawArgsAddress + VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET);
    _VSMPrototypeMeshletPageRequests[startInstance + localRequestIndex] =
        uint4(
            sourceRequest.InstanceID_LOD,
            sourceRequest.MeshletID,
            virtualPageIndex,
            cascadeIndex);
}

// Large records retain one fixed instance stride so their record address is
// arithmetic. Use the largest per-clipmap page count, not the sum across all
// clipmaps; each record only visits its own level's list and skips padding.
groupshared uint g_VSMRasterPageCount;
groupshared uint g_VSMRasterLevelCounts[VIVID_VSM_RASTER_MAX_LEVELS];
groupshared uint g_VSMRasterLevelOffsets[VIVID_VSM_RASTER_MAX_LEVELS];

[numthreads(64, 1, 1)]
void VSMPrototypePrepareMeshletPageRequests(uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0u) g_VSMRasterPageCount = 0u;
    if (groupIndex < VIVID_VSM_RASTER_MAX_LEVELS) g_VSMRasterLevelCounts[groupIndex] = 0u;
    GroupMemoryBarrierWithGroupSync();
    uint pagesPerLevel = (uint)(_VSMPrototypePagesPerAxis * _VSMPrototypePagesPerAxis);
    for (uint slot = groupIndex; slot < (uint)_VSMPrototypePhysicalPageCapacity; slot += 64u)
    {
        uint owner = _VSMPrototypePhysicalPageOwners[slot];
        if (owner != 0u && IsVSMCasterPageRelevant(owner - 1u))
        {
            uint level = (owner - 1u) / pagesPerLevel;
            InterlockedAdd(g_VSMRasterLevelCounts[level], 1u);
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if (groupIndex == 0u)
    {
        uint offset = VIVID_VSM_RASTER_PAGE_HEADER_SIZE;
        for (uint level = 0u; level < VIVID_VSM_RASTER_MAX_LEVELS; level++)
        {
            uint count = g_VSMRasterLevelCounts[level];
            g_VSMRasterPageCount = max(g_VSMRasterPageCount, count);
            g_VSMRasterLevelOffsets[level] = offset;
            _VSMPrototypeMeshletRasterPages[1u + level] = count;
            _VSMPrototypeMeshletRasterPages[1u + VIVID_VSM_RASTER_MAX_LEVELS + level] = offset;
            offset += count;
            g_VSMRasterLevelCounts[level] = 0u;
        }
        _VSMPrototypeMeshletRasterPages[0] = g_VSMRasterPageCount;
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint slot = groupIndex; slot < (uint)_VSMPrototypePhysicalPageCapacity; slot += 64u)
    {
        uint owner = _VSMPrototypePhysicalPageOwners[slot];
        if (owner != 0u && IsVSMCasterPageRelevant(owner - 1u))
        {
            uint level = (owner - 1u) / pagesPerLevel;
            uint index;
            InterlockedAdd(g_VSMRasterLevelCounts[level], 1u, index);
            index += g_VSMRasterLevelOffsets[level];
            _VSMPrototypeMeshletRasterPages[index] = owner - 1u;
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if (groupIndex != 0u) return;

    uint outputStartInstance = 0u;
    for (uint rendererListIndex = 0u;
         rendererListIndex < VIVIDRENDERERLISTID_COUNT;
         rendererListIndex++)
    {
        uint sourceRequestCount = 0u;
        for (uint cascadeIndex = 0u;
             cascadeIndex < (uint)_VSMProjectionCount;
             cascadeIndex++)
        {
            const uint sourceArgsIndex = GetVSMSourceDrawArgsIndex(
                cascadeIndex,
                rendererListIndex);
            const uint sourceArgsAddress = GetIndirectDrawArgsByteAddress(
                sourceArgsIndex);
            sourceRequestCount += _VSMPrototypeSourceMeshletIndirectArgs.Load(
                sourceArgsAddress
                    + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET);
        }

        const uint outputArgsAddress = GetVSMPageDrawArgsAddress(
            rendererListIndex);
        _VSMPrototypeMeshletPageIndirectArgs.Store4(
            outputArgsAddress,
            uint4(
                VIVID_MAX_MESHLET_INDICES,
                0u,
                0u,
                outputStartInstance));
        outputStartInstance += sourceRequestCount
            * kVSMMaxPagesPerMeshletRequest;
        // Small requests grow from the front; large records grow from the back.
        // A source emits either <= 4 small records or one large record, never both.
        _VSMPrototypeMeshletPageIndirectArgs.Store4(
            GetVSMPageDrawArgsAddress(rendererListIndex + VIVIDRENDERERLISTID_COUNT),
            uint4(VIVID_MAX_MESHLET_INDICES, 0u, 0u, outputStartInstance));
    }
}

bool GetVSMPageRange(
    VividMeshletRenderRequestPacked sourceRequest,
    uint cascadeIndex,
    out uint2 minPage,
    out uint2 maxPage)
{
    minPage = 0u;
    maxPage = 0u;
    if (sourceRequest.InstanceID_LOD >= _InstanceDataCount
        || sourceRequest.MeshletID >= _MeshletCount)
    {
        return false;
    }

    const VividInstanceData instanceData = PullInstanceData(
        sourceRequest.InstanceID_LOD);
    const VividDecodedMeshlet meshlet = PullMeshletData(
        sourceRequest.MeshletID);
    const float4 sphereWS = TransformSphere(
        meshlet.BoundingSphere,
        instanceData.ObjectToWorldMatrix);
    const float4x4 worldToShadow = _VSMProjections[cascadeIndex].worldToShadow;
    const float4 centerShadow = mul(
        worldToShadow,
        float4(sphereWS.xyz, 1.0));
    const float inverseW = rcp(max(abs(centerShadow.w), 1e-6));
    const float2 centerUV = centerShadow.xy * inverseW;
    const float2 radiusUV = sphereWS.w * float2(
        length(float3(
            worldToShadow._m00,
            worldToShadow._m01,
            worldToShadow._m02)),
        length(float3(
            worldToShadow._m10,
            worldToShadow._m11,
            worldToShadow._m12))) * inverseW;
    const float2 minUV = centerUV - radiusUV;
    const float2 maxUV = centerUV + radiusUV;
    if (any(maxUV < 0.0) || any(minUV > 1.0))
        return false;

    const uint virtualResolution = (uint)max(
        _VSMPrototypeVirtualResolution,
        1);
    const uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    const uint pagesPerAxis = (uint)max(_VSMPrototypePagesPerAxis, 1);
    const uint2 minVirtualTexel = VividVSMUVToVirtualTexel(
        minUV, virtualResolution);
    const uint2 maxVirtualTexel = VividVSMUVToVirtualTexel(
        maxUV, virtualResolution);
    minPage = min(minVirtualTexel / pageSize, pagesPerAxis - 1u);
    maxPage = min(maxVirtualTexel / pageSize, pagesPerAxis - 1u);
    return all(maxPage >= minPage);
}

[numthreads(64, 1, 1)]
void VSMPrototypeCullMeshletsToPages(
    uint3 dispatchThreadID : SV_DispatchThreadID)
{
    const uint localRequestIndex = dispatchThreadID.x;
    const uint cascadeIndex = dispatchThreadID.y;
    const uint rendererListIndex = dispatchThreadID.z;
    if (localRequestIndex
            >= (uint)_VSMPrototypeSourceRequestsPerCascadeCapacity
        || cascadeIndex >= (uint)_VSMProjectionCount
        || rendererListIndex >= VIVIDRENDERERLISTID_COUNT)
    {
        return;
    }

    const uint sourceArgsIndex = GetVSMSourceDrawArgsIndex(
        cascadeIndex,
        rendererListIndex);
    const uint sourceArgsAddress = GetIndirectDrawArgsByteAddress(
        sourceArgsIndex);
    const uint sourceRequestCount =
        _VSMPrototypeSourceMeshletIndirectArgs.Load(
            sourceArgsAddress
                + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET);
    if (localRequestIndex >= sourceRequestCount)
        return;

    const uint sourceStartInstance =
        _VSMPrototypeSourceMeshletIndirectArgs.Load(
            sourceArgsAddress
                + VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET);
    const VividMeshletRenderRequestPacked sourceRequest =
        _VSMPrototypeSourceMeshletRequests[
            sourceStartInstance + localRequestIndex];

    uint2 minPage;
    uint2 maxPage;
    if (!GetVSMPageRange(
            sourceRequest,
            cascadeIndex,
            minPage,
            maxPage))
    {
        return;
    }

    const uint pagesPerAxis = (uint)max(_VSMPrototypePagesPerAxis, 1);
    const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
    const uint coveredPageCount = (maxPage.x - minPage.x + 1u)
        * (maxPage.y - minPage.y + 1u);
    if (coveredPageCount > kVSMMaxPagesPerMeshletRequest)
    {
        for (uint pageY = minPage.y; pageY <= maxPage.y; pageY++)
        {
            for (uint pageX = minPage.x; pageX <= maxPage.x; pageX++)
            {
                const uint virtualPageIndex = cascadeIndex
                        * pagesPerCascade
                    + pageY * pagesPerAxis
                    + pageX;
                if (!IsVSMCasterPageRelevant(virtualPageIndex))
                    continue;

                const uint rasterPageCount = _VSMPrototypeMeshletRasterPages[0];
                if (rasterPageCount == 0u)
                    return;
                const uint argsAddress = GetVSMPageDrawArgsAddress(
                    rendererListIndex + VIVIDRENDERERLISTID_COUNT);
                uint firstInstance;
                _VSMPrototypeMeshletPageIndirectArgs.InterlockedAdd(
                    argsAddress + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET,
                    rasterPageCount,
                    firstInstance);
                const uint requestEnd = _VSMPrototypeMeshletPageIndirectArgs.Load(
                    argsAddress + VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET);
                _VSMPrototypeMeshletPageRequests[
                    requestEnd - 1u - firstInstance / rasterPageCount] = uint4(
                        sourceRequest.InstanceID_LOD,
                        sourceRequest.MeshletID,
                        cascadeIndex * pagesPerCascade + minPage.y * pagesPerAxis + minPage.x,
                        cascadeIndex * pagesPerCascade + maxPage.y * pagesPerAxis + maxPage.x);
                return;
            }
        }
        return;
    }

    for (uint pageY = minPage.y; pageY <= maxPage.y; pageY++)
    {
        for (uint pageX = minPage.x; pageX <= maxPage.x; pageX++)
        {
            const uint virtualPageIndex = cascadeIndex * pagesPerCascade
                + pageY * pagesPerAxis
                + pageX;
            if (!IsVSMCasterPageRelevant(virtualPageIndex))
                continue;

            AppendVSMPageMeshletRequest(
                rendererListIndex,
                sourceRequest,
                virtualPageIndex,
                cascadeIndex);
        }
    }
}

void UpdateVSMPagePressure(bool reset)
{
    uint4 state = _VSMPagePressureRW[0];
    uint4 detail = _VSMPagePressureRW[1];
    uint4 recovery = _VSMPagePressureRW[2];
    float previous = asfloat(state.x);
    float bias = previous;
    uint age = state.y;
    // A new producer, changed target, disabled policy or recreated pool starts
    // with no inherited demand. Never feed retained cache occupancy into control.
    if (reset || _VSMReceiverQuality.x < 1.5 || detail.w != asuint(_VSMReceiverQuality.y))
    {
        state = 0u;
        detail = 0u;
        recovery = 0u;
        bias = 0;
        age = 0u;
    }
    else if (state.z > 0u)
    {
        float pressure = (float)state.z / max((float)_VSMPrototypePhysicalPageCapacity, 1.0);
        if (state.w > 0u || pressure > 0.95)
        {
            float priorBias = asfloat(detail.z);
            if (bias < priorBias && recovery.w > 0u)
            {
                // A finer-level probe crossed a discrete page-demand step.
                // Restore the last feasible choice and remember its demand ratio.
                recovery.xyz = uint3(detail.z, recovery.w, state.z);
                bias = priorBias;
            }
            else
                bias += clamp(0.5 * log2(max(pressure / 0.85, 1.0)), 1.0 / 32.0, 1.0 / 8.0);
            age = 0u;
        }
        else if (pressure < 0.85)
        {
            bool atBoundary = recovery.z > 0u && bias <= asfloat(recovery.x);
            float predictedDemand = (float)recovery.z * state.z / max((float)recovery.y, 1.0);
            if (atBoundary && predictedDemand > 0.90 * _VSMPrototypePhysicalPageCapacity)
                age = 0u;
            else
            {
                if (atBoundary) recovery.xyz = 0u;
                age = min(age + 1u, 60u);
                if (age == 60u)
                    bias = max(bias - 1.0 / 64.0, recovery.z > 0u ? asfloat(recovery.x) : 0.0);
            }
        }
        else age = 0u;
    }
    else age = 0u; // No receivers is not evidence that finer demand fits.
    state.xy = uint2(asuint(clamp(bias, 0.0, max((float)_VSMProjectionCount - 1.0, 0.0))), age);
    detail.zw = uint2(asuint(previous), asuint(_VSMReceiverQuality.y));
    recovery.w = state.z;
    _VSMPagePressureRW[0] = state;
    _VSMPagePressureRW[1] = detail;
    _VSMPagePressureRW[2] = recovery;
}

[numthreads(64, 1, 1)]
void VSMPrototypeClearReceiverRequests(uint3 id : SV_DispatchThreadID)
{
    if (id.x == 0u) UpdateVSMPagePressure(false);
    if (id.x < (uint)_VSMPrototypePageTableEntryCount)
        _VSMPrototypePageMetadata[id.x].x &= ~kVSMPageRequestMask;
}

[numthreads(64, 1, 1)]
void VSMPrototypeResetReceiverFeedback(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    if (dispatchThreadID.x == 0u) UpdateVSMPagePressure(true);
    uint virtualPageIndex = dispatchThreadID.x;
    if (virtualPageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return;

    uint4 metadata = _VSMPrototypePageMetadata[virtualPageIndex];
    metadata.x &= ~kVSMPageRequestMask;
    // A different camera's same-frame requests must not survive as either
    // receiver demand or eviction protection. Keep depth/cache ownership intact.
    metadata.z = 0u;
    _VSMPrototypePageMetadata[virtualPageIndex] = metadata;
}

// Update cached virtual addresses in physical-slot order, matching UE's
// UpdatePhysicalPageAddresses. Depth texels never move. Preserve all metadata
// (including dirty/deferred flags, request age and debug state) for retained pages.
[numthreads(64, 1, 1)]
void VSMUpdatePhysicalPageAddresses(uint3 id : SV_DispatchThreadID)
{
    uint slot = id.x;
    if (slot >= (uint)_VSMPrototypePhysicalPageCapacity) return;
    uint owner = _VSMPrototypePhysicalPageOwners[slot];
    uint nextOwner = 0u;
    uint4 metadata = 0u;
    if (owner != 0u)
    {
        uint source = owner - 1u;
        uint axis = (uint)_VSMPrototypePagesPerAxis;
        uint perLevel = axis * axis;
        uint level = source / perLevel;
        uint page = source % perLevel;
        int4 remap = _VSMProjectionRemap[level];
        // CPU delta is current origin minus previous origin, so an old page's
        // address moves by -delta (the inverse of the former destination gather).
        int2 destXY = int2(page % axis, page / axis) - remap.xy;
        if (remap.z == 0 && all(destXY >= 0) && all(destXY < (int)axis))
        {
            nextOwner = level * perLevel + (uint)destXY.y * axis + (uint)destXY.x + 1u;
            metadata = _VSMPrototypePageMetadata[source];
        }
    }
    _VSMRemapPageMetadata[slot] = metadata;
    _VSMPrototypePhysicalPageOwners[slot] = nextOwner;
}

[numthreads(64, 1, 1)]
void VSMClearVirtualPageMappings(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_VSMPrototypePageTableEntryCount) return;
    // Unmapped pages have no persistent cache state. Current receiver demand
    // is generated after layout recording, independently of previous requests.
    _VSMPrototypeWritablePageTable[id.x] = 0u;
    _VSMPrototypePageMetadata[id.x] = 0u;
}

[numthreads(64, 1, 1)]
void VSMRemapPages(uint3 id : SV_DispatchThreadID)
{
    uint slot = id.x;
    if (slot >= (uint)_VSMPrototypePhysicalPageCapacity) return;
    uint owner = _VSMPrototypePhysicalPageOwners[slot];
    if (owner == 0u) return;
    // Translation is one-to-one within each level; retained owners cannot collide.
    _VSMPrototypeWritablePageTable[owner - 1u] = slot + 1u;
    _VSMPrototypePageMetadata[owner - 1u] = _VSMRemapPageMetadata[slot];
}

// Each word has one writer. Dispatch across the complete table rather than
// making one group process every virtual page before serial allocation.
[numthreads(64, 1, 1)]
void VSMPrototypePrepareAllocation(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint pageCount = (uint)_VSMPrototypePageTableEntryCount;
    uint levelCount = (uint)max(_VSMProjectionCount, 1);
    uint pagesPerLevel = pageCount / levelCount;
    uint wordCount = (pageCount + 31u) / 32u;
    // Snapshot and clear stale feedback in parallel. Each word/page has one writer;
    // the bitset records coarse-to-fine order, including sub-32-page test layouts.
    if (dispatchThreadID.x < wordCount)
    {
        uint word = dispatchThreadID.x;
        uint requests = 0u;
        for (uint bit = 0u; bit < 32u && word * 32u + bit < pageCount; bit++)
        {
            uint index = word * 32u + bit;
            uint page = (levelCount - 1u - index / pagesPerLevel) * pagesPerLevel + index % pagesPerLevel;
            uint4 metadata = _VSMPrototypePageMetadata[page];
            bool current = metadata.z == (uint)_VSMPrototypeFeedbackFrameIndex;
            metadata.w = metadata.x & ~kVSMPageRequestMask;
            if (current)
            {
                metadata.w |= metadata.x & kVSMPageRequestMask;
                if ((metadata.x & kVSMPageRequested) != 0u) requests |= 1u << bit;
            }
            else metadata.x &= ~kVSMPageRequestMask;
            _VSMPrototypePageMetadata[page] = metadata;
        }
        _VSMAllocationRequests[word] = requests;
    }
}

// Free slots first, then lower-value demand, oldest age and physical slot.
// This is the original victim order, sorted once instead of scanning the pool
// on every miss. New allocations are protected by coarse-to-fine role order.
groupshared uint4 g_VSMAllocationSlots[1024];
bool VSMAllocationKeyLess(uint4 a, uint4 b)
{
    return a.x < b.x || (a.x == b.x && (a.y < b.y || (a.y == b.y && a.z < b.z)));
}

// One group preserves allocation order while parallelizing request gathering and
// page writes. A batch contains only one role/level: none of its existing owners
// can be a victim, so its page writes cannot race another lane's eviction.
groupshared uint g_VSMRequestPrefix[64];
groupshared uint g_VSMRequestPages[2048];
groupshared uint g_VSMMissingRequest[64];
groupshared uint g_VSMAssignedSlot[64];
groupshared uint g_VSMSlotCursor;
groupshared uint4 g_VSMAllocationCounts[64];
groupshared uint4 g_VSMAllocationPressure[64];

[numthreads(64, 1, 1)]
void VSMPrototypeAllocatePages(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint lane = dispatchThreadID.x;
    uint capacity = (uint)_VSMPrototypePhysicalPageCapacity;
    uint sortCount = 1u;
    while (sortCount < capacity) sortCount <<= 1u;
    uint4 counts = 0u; // allocated, requested, newly allocated, overflow
    uint4 pressureCounts = 0u; // essential demand/misses, primary demand/resident
    for (uint slot = lane; slot < sortCount; slot += 64u)
    {
        uint4 key = uint4(0xffffffffu, 0u, slot, 0u);
        if (slot < capacity)
        {
            uint owner = _VSMPrototypePhysicalPageOwners[slot];
            key = uint4(0u, 0u, slot, owner);
            if (owner != 0u)
            {
                counts.x++;
                uint4 metadata = _VSMPrototypePageMetadata[owner - 1u];
                bool current = (metadata.x & kVSMPageRequested) != 0u
                    && metadata.z == (uint)_VSMPrototypeFeedbackFrameIndex;
                key.xy = uint2(current ? 5u - VSMPageRequestPriority(metadata.x) : 1u, metadata.z);
            }
        }
        g_VSMAllocationSlots[slot] = key;
    }
    if (lane == 0u) g_VSMSlotCursor = 0u;
    GroupMemoryBarrierWithGroupSync();
    for (uint width = 2u; width <= sortCount; width <<= 1u)
    {
        for (uint distance = width >> 1u; distance > 0u; distance >>= 1u)
        {
            for (uint slot = lane; slot < sortCount; slot += 64u)
            {
                uint partner = slot ^ distance;
                if (partner > slot)
                {
                    uint4 a = g_VSMAllocationSlots[slot], b = g_VSMAllocationSlots[partner];
                    if (((slot & width) == 0u) ? VSMAllocationKeyLess(b, a) : VSMAllocationKeyLess(a, b))
                    {
                        g_VSMAllocationSlots[slot] = b;
                        g_VSMAllocationSlots[partner] = a;
                    }
                }
            }
            GroupMemoryBarrierWithGroupSync();
        }
    }

    uint pageCount = (uint)_VSMPrototypePageTableEntryCount;
    uint levelCount = (uint)max(_VSMProjectionCount, 1);
    uint pagesPerLevel = pageCount / levelCount;
    for (uint phase = 0u; phase < 4u; phase++)
    for (uint orderedLevel = 0u; orderedLevel < levelCount; orderedLevel++)
    {
        uint level = levelCount - 1u - orderedLevel;
        uint levelStart = orderedLevel * pagesPerLevel;
        uint levelEnd = levelStart + pagesPerLevel;
        uint firstWord = levelStart / 32u;
        uint endWord = (levelEnd + 31u) / 32u;
        for (uint first = firstWord; first < endWord; first += 64u)
        {
            uint word = first + lane;
            uint requests = word < endWord ? _VSMAllocationRequests[word] : 0u;
            uint matching = 0u;
            while (requests != 0u)
            {
                uint bit = (uint)firstbitlow(requests);
                requests &= requests - 1u;
                uint index = word * 32u + bit;
                // A word may span levels in small diagnostic layouts.
                if (index < levelStart || index >= levelEnd) continue;
                uint page = level * pagesPerLevel + index - levelStart;
                uint flags = _VSMPrototypePageMetadata[page].x;
                if ((flags & kVSMPageRequested) != 0u && VSMPageRequestPriority(flags) == phase)
                    matching |= 1u << bit;
            }

            // Stable inclusive scan: word order then ascending bit order is the
            // original coarse-to-fine request order, independent of wave size.
            uint count = countbits(matching);
            g_VSMRequestPrefix[lane] = count;
            GroupMemoryBarrierWithGroupSync();
            for (uint offset = 1u; offset < 64u; offset <<= 1u)
            {
                uint add = lane >= offset ? g_VSMRequestPrefix[lane - offset] : 0u;
                GroupMemoryBarrierWithGroupSync();
                g_VSMRequestPrefix[lane] += add;
                GroupMemoryBarrierWithGroupSync();
            }
            uint output = g_VSMRequestPrefix[lane] - count;
            uint batchCount = g_VSMRequestPrefix[63];
            while (matching != 0u)
            {
                uint bit = (uint)firstbitlow(matching);
                matching &= matching - 1u;
                g_VSMRequestPages[output++] = level * pagesPerLevel + word * 32u + bit - levelStart;
            }
            GroupMemoryBarrierWithGroupSync();

            for (uint batch = 0u; batch < batchCount; batch += 64u)
            {
                bool active = batch + lane < batchCount;
                uint page = active ? g_VSMRequestPages[batch + lane] : 0u;
                uint4 metadata = 0u;
                if (active) metadata = _VSMPrototypePageMetadata[page];
                g_VSMMissingRequest[lane] = active && (metadata.x & kVSMPageAllocated) == 0u ? 1u : 0u;
                g_VSMAssignedSlot[lane] = capacity;
                GroupMemoryBarrierWithGroupSync();
                if (lane == 0u)
                {
                    // Only matching remains ordered. The cursor consumes at
                    // most capacity candidates in the entire dispatch.
                    for (uint request = 0u; request < min(64u, batchCount - batch)
                            && g_VSMSlotCursor < capacity; request++)
                    {
                        if (g_VSMMissingRequest[request] == 0u) continue;
                        while (g_VSMSlotCursor < capacity)
                        {
                            uint sortedSlot = g_VSMSlotCursor++;
                            uint4 candidate = g_VSMAllocationSlots[sortedSlot];
                            if (candidate.w != 0u)
                            {
                                uint priority = 5u - candidate.x;
                                uint ownerLevel = (candidate.w - 1u) / pagesPerLevel;
                                if (priority < phase || (priority == phase && ownerLevel >= level)) continue;
                            }
                            g_VSMAssignedSlot[request] = sortedSlot;
                            break;
                        }
                    }
                }
                GroupMemoryBarrierWithGroupSync();
                if (active)
                {
                    counts.y++;
                    if (phase < 3u) pressureCounts.x++;
                    bool primary = (metadata.x & kVSMPagePrimaryRequested) != 0u;
                    if (primary) pressureCounts.z++;
                    if (g_VSMMissingRequest[lane] != 0u)
                    {
                        uint sortedSlot = g_VSMAssignedSlot[lane];
                        if (sortedSlot < capacity)
                        {
                            uint4 candidate = g_VSMAllocationSlots[sortedSlot];
                            if (candidate.w != 0u)
                            {
                                uint evicted = candidate.w - 1u;
                                uint4 evictedMetadata = _VSMPrototypePageMetadata[evicted];
                                evictedMetadata.x &= kVSMPageRequestMask;
                                evictedMetadata.y = 0u;
                                evictedMetadata.w = kVSMPageDebugEvicted | (evictedMetadata.w & kVSMPageRequestMask);
                                _VSMPrototypePageMetadata[evicted] = evictedMetadata;
                                _VSMPrototypeWritablePageTable[evicted] = 0u;
                            }
                            else counts.x++;
                            uint encoded = candidate.z + 1u;
                            metadata.x = (metadata.x | kVSMPageAllocated | kVSMPageDirty
                                | kVSMPageDynamicDirty | kVSMPageStatic | kVSMPageDynamic) & ~kVSMPageCached;
                            metadata.y = encoded;
                            metadata.w = metadata.x;
                            _VSMPrototypeWritablePageTable[page] = encoded;
                            _VSMPrototypePhysicalPageOwners[candidate.z] = page + 1u;
                            counts.z++;
                        }
                        else
                        {
                            counts.w++;
                            if (phase < 3u) pressureCounts.y++;
                            metadata.w |= kVSMPageDebugOverflow;
                        }
                    }
                    if (primary && (metadata.x & kVSMPageAllocated) != 0u) pressureCounts.w++;
                    if ((metadata.x & kVSMPageAllocated) == 0u) metadata.x &= ~kVSMPageRequestMask;
                    _VSMPrototypePageMetadata[page] = metadata;
                }
                // Later roles/levels must see evictions before loading metadata.
                AllMemoryBarrierWithGroupSync();
            }
        }
    }
    for (uint slot = lane; slot < capacity; slot += 64u)
    {
        uint owner = _VSMPrototypePhysicalPageOwners[slot];
        if (owner != 0u) _VSMPrototypePageMetadata[owner - 1u].x &= ~kVSMPageRequestMask;
    }
    g_VSMAllocationCounts[lane] = counts;
    g_VSMAllocationPressure[lane] = pressureCounts;
    GroupMemoryBarrierWithGroupSync();
    for (uint stride = 32u; stride > 0u; stride >>= 1u)
    {
        if (lane < stride)
        {
            g_VSMAllocationCounts[lane] += g_VSMAllocationCounts[lane + stride];
            g_VSMAllocationPressure[lane] += g_VSMAllocationPressure[lane + stride];
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0u)
    {
        counts = g_VSMAllocationCounts[0];
        for (uint i = 0u; i < 4u; i++) _VSMPrototypeAllocatorCounters[i] = counts[i];
        uint4 pressure = _VSMPagePressureRW[0];
        pressure.zw = g_VSMAllocationPressure[0].xy;
        _VSMPagePressureRW[0] = pressure;
        uint4 detail = _VSMPagePressureRW[1];
        detail.xy = g_VSMAllocationPressure[0].zw;
        _VSMPagePressureRW[1] = detail;
    }
}

[numthreads(64, 1, 1)]
void VSMPrototypeMarkAllAllocatedPagesDirty(
    uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint virtualPageIndex = dispatchThreadID.x;
    if (virtualPageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return;

    uint4 metadata = _VSMPrototypePageMetadata[virtualPageIndex];
    if ((metadata.x & kVSMPageAllocated) == 0u)
        return;

    metadata.x = (metadata.x | kVSMPageDirty) & ~kVSMPageCached;
    _VSMPrototypePageMetadata[virtualPageIndex] = metadata;
}

[numthreads(64, 1, 1)]
void VSMPrototypeMarkDynamicPagesDirty(uint3 id : SV_DispatchThreadID)
{
    if (id.x < (uint)_VSMPrototypePageTableEntryCount
        && (_VSMPrototypePageMetadata[id.x].x & kVSMPageAllocated) != 0u)
        _VSMPrototypePageMetadata[id.x].x |= kVSMPageDynamicDirty;
}

void InvalidateVSMPageBounds(uint3 boundsID, uint dirtyMask, uint lane, uint stride)
{
    const uint boundsIndex = boundsID.x;
    const uint cascadeIndex = boundsID.y;
    if (boundsIndex >= (uint)_VSMPrototypeStaticInvalidationBoundsCount
        || cascadeIndex >= (uint)_VSMProjectionCount)
    {
        return;
    }

    const VSMPrototypeStaticInvalidationBounds invalidation =
        _VSMPrototypeStaticInvalidationBounds[boundsIndex];
    float2 minimumUV = float2(FLT_MAX, FLT_MAX);
    float2 maximumUV = float2(-FLT_MAX, -FLT_MAX);
    [unroll]
    for (uint cornerIndex = 0u; cornerIndex < 8u; cornerIndex++)
    {
        const float3 cornerWS = float3(
            (cornerIndex & 1u) != 0u
                ? invalidation.BoundsMax.x
                : invalidation.BoundsMin.x,
            (cornerIndex & 2u) != 0u
                ? invalidation.BoundsMax.y
                : invalidation.BoundsMin.y,
            (cornerIndex & 4u) != 0u
                ? invalidation.BoundsMax.z
                : invalidation.BoundsMin.z);
        const float4 cornerShadow = mul(
            _VSMProjections[cascadeIndex].worldToShadow,
            float4(cornerWS, 1.0));
        const float inverseW = rcp(max(abs(cornerShadow.w), 1e-6));
        const float2 cornerUV = cornerShadow.xy * inverseW;
        minimumUV = min(minimumUV, cornerUV);
        maximumUV = max(maximumUV, cornerUV);
    }

    if (!all(isfinite(minimumUV))
        || !all(isfinite(maximumUV))
        || any(maximumUV < 0.0)
        || any(minimumUV > 1.0))
    {
        return;
    }

    const uint virtualResolution = (uint)max(
        _VSMPrototypeVirtualResolution,
        1);
    const uint pageSize = (uint)max(_VSMPrototypePageSize, 1);
    const uint pagesPerAxis = (uint)max(_VSMPrototypePagesPerAxis, 1);
    const uint2 minimumVirtualTexel = VividVSMUVToVirtualTexel(
        minimumUV, virtualResolution);
    const uint2 maximumVirtualTexel = VividVSMUVToVirtualTexel(
        maximumUV, virtualResolution);
    const uint2 minimumPage = min(
        minimumVirtualTexel / pageSize,
        pagesPerAxis - 1u);
    const uint2 maximumPage = min(
        maximumVirtualTexel / pageSize,
        pagesPerAxis - 1u);
    const uint pagesPerCascade = pagesPerAxis * pagesPerAxis;
    const uint width = maximumPage.x - minimumPage.x + 1u;
    const uint pageCount = width * (maximumPage.y - minimumPage.y + 1u);
    for (uint page = lane; page < pageCount; page += stride)
    {
        const uint pageX = minimumPage.x + page % width;
        const uint pageY = minimumPage.y + page / width;
        const uint virtualPageIndex = cascadeIndex * pagesPerCascade
            + pageY * pagesPerAxis
            + pageX;
        if (virtualPageIndex >= (uint)_VSMPrototypePageTableEntryCount)
            continue;

        const uint flags =
            _VSMPrototypePageMetadata[virtualPageIndex].x;
        if ((flags & kVSMPageAllocated) == 0u)
            continue;

        uint originalFlags;
        InterlockedOr(
            _VSMPrototypePageMetadata[virtualPageIndex].x,
            dirtyMask,
            originalFlags);
        if (dirtyMask == kVSMPageDirty)
        {
            InterlockedAnd(
                _VSMPrototypePageMetadata[virtualPageIndex].x,
                ~kVSMPageCached,
                originalFlags);
        }
    }
}

[numthreads(1, 1, 1)]
void VSMPrototypeInvalidateStaticPages(uint3 id : SV_DispatchThreadID)
{
    InvalidateVSMPageBounds(id, kVSMPageDirty, 0u, 1u);
}

[numthreads(64, 1, 1)]
void VSMPrototypeInvalidateDynamicPages(uint3 id : SV_GroupID, uint lane : SV_GroupIndex)
{
    // One group per bounds/projection; distribute its page rectangle across lanes.
    InvalidateVSMPageBounds(id, kVSMPageDynamicDirty, lane, 64u);
}

groupshared uint g_VSMClearPageCount;
groupshared uint g_VSMOccupancyPageCount;

// Build after allocation/remap and all invalidation, before either pool is
// cleared. Owners and dirty bits remain stable until occupancy is reduced.
// Two capacity-sized ranges: selected dirty pages, then pages safe to scan.
// Deferred pages retain dirty bits and all previous texels; receivers reject them.
[numthreads(64, 1, 1)]
void VSMBuildPageWorkLists(uint lane : SV_GroupIndex)
{
    if (lane == 0u)
    {
        g_VSMClearPageCount = 0u;
        g_VSMOccupancyPageCount = 0u;
    }
    GroupMemoryBarrierWithGroupSync();
    uint capacity = (uint)_VSMPrototypePhysicalPageCapacity;
    uint budget = _VSMPageUpdateBudget > 0 ? min((uint)_VSMPageUpdateBudget, capacity) : capacity;
    uint levelCount = (uint)max(_VSMProjectionCount, 1);
    uint pagesPerLevel = max((uint)_VSMPrototypePageTableEntryCount / levelCount, 1u);
    uint sortCount = 1u;
    while (sortCount < capacity) sortCount <<= 1u;
    // Reuse the allocation kernel's shared scratch (kernels execute separately).
    // Only current demand consumes a finite update budget. Unlimited mode keeps
    // the original all-dirty-page behavior, including unrequested cached pages.
    for (uint slot = lane; slot < sortCount; slot += 64u)
    {
        uint4 key = uint4(0xffffffffu, 0u, slot, 0u);
        if (slot < capacity)
        {
            uint owner = _VSMPrototypePhysicalPageOwners[slot];
            key.w = owner;
            if (owner != 0u)
            {
                uint4 metadata = _VSMPrototypePageMetadata[owner - 1u];
                bool dirty = (metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty)) != 0u;
                bool requested = metadata.z == (uint)_VSMPrototypeFeedbackFrameIndex
                    && (metadata.w & kVSMPageRequested) != 0u;
                if (dirty && (requested || _VSMPageUpdateBudget <= 0))
                    key.x = _VSMPageUpdateBudget > 0 ? levelCount - 1u - (owner - 1u) / pagesPerLevel : 0u;
                // Rotate equal-level priority; physical ownership is re-read every
                // frame, so eviction/remap never leaves a stale queued slot behind.
                key.y = (slot + (uint)_VSMPrototypeFeedbackFrameIndex % capacity) % capacity;
            }
        }
        g_VSMAllocationSlots[slot] = key;
    }
    GroupMemoryBarrierWithGroupSync();
    for (uint width = 2u; width <= sortCount; width <<= 1u)
    for (uint distance = width >> 1u; distance > 0u; distance >>= 1u)
    {
        for (uint slot = lane; slot < sortCount; slot += 64u)
        {
            uint partner = slot ^ distance;
            if (partner > slot)
            {
                uint4 a = g_VSMAllocationSlots[slot], b = g_VSMAllocationSlots[partner];
                if (((slot & width) == 0u) ? VSMAllocationKeyLess(b, a) : VSMAllocationKeyLess(a, b))
                {
                    g_VSMAllocationSlots[slot] = b;
                    g_VSMAllocationSlots[partner] = a;
                }
            }
        }
        GroupMemoryBarrierWithGroupSync();
    }
    for (uint rank = lane; rank < sortCount; rank += 64u)
    {
        uint4 key = g_VSMAllocationSlots[rank];
        uint slot = key.z, owner = key.w;
        if (slot >= capacity || owner == 0u) continue;
        uint flags = _VSMPrototypePageMetadata[owner - 1u].x & ~kVSMPageDeferred;
        bool dirty = (flags & (kVSMPageDirty | kVSMPageDynamicDirty)) != 0u;
        bool selected = dirty && rank < budget && key.x != 0xffffffffu;
        if (dirty && !selected) flags |= kVSMPageDeferred;
        _VSMPrototypePageMetadata[owner - 1u].x = flags;
        uint index;
        if (selected)
        {
            InterlockedAdd(g_VSMClearPageCount, 1u, index);
            _VSMPageWorkListRW[index] = slot;
        }
        uint known = kVSMPageStaticOccupancyKnown | kVSMPageDynamicOccupancyKnown;
        if (selected || (!dirty && ((flags & known) != known || _VSMPageOccupancySkipDisabled != 0)))
        {
            InterlockedAdd(g_VSMOccupancyPageCount, 1u, index);
            _VSMPageWorkListRW[capacity + index] = slot;
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0u)
    {
        uint tiles = ((uint)_VSMPrototypePageSize + 7u) / 8u;
        // Always overwrite all arguments, including zero-work frames.
        _VSMPageWorkDispatchArgsRW.Store3(0u, uint3(tiles, tiles, g_VSMClearPageCount));
        _VSMPageWorkDispatchArgsRW.Store3(12u, uint3(g_VSMOccupancyPageCount, 1u, 1u));
    }
}

void ClearVSMPhysicalPage(uint physicalPageIndex, uint2 texel)
{
    if (physicalPageIndex >= (uint)_VSMPrototypePhysicalPageCapacity
        || texel.x >= (uint)_VSMPrototypePageSize
        || texel.y >= (uint)_VSMPrototypePageSize)
    {
        return;
    }

    uint encodedVirtualPage =
        _VSMPrototypePhysicalPageOwners[physicalPageIndex];
    if (encodedVirtualPage == 0u)
        return;

    uint virtualPageIndex = encodedVirtualPage - 1u;
    uint flags = _VSMPrototypePageMetadata[virtualPageIndex].x;
    if ((flags & (kVSMPageDirty | kVSMPageDynamicDirty)) == 0u || (flags & kVSMPageDeferred) != 0u) return;
    uint2 physicalPage = uint2(
        physicalPageIndex % (uint)_VSMPrototypePhysicalPagesPerRow,
        physicalPageIndex / (uint)_VSMPrototypePhysicalPagesPerRow);
    uint2 physicalTexel = physicalPage * (uint)_VSMPrototypePageSize
        + texel;
    for (uint layer = 0; layer < VIVID_VSM_DEPTH_LAYER_COUNT; layer++)
    {
        if ((flags & kVSMPageDynamicDirty) != 0u)
            _VSMPrototypeDynamicPhysicalPageRW[uint3(physicalTexel, layer)] = 0u;
        if ((flags & kVSMPageDirty) != 0u)
            _VSMPrototypeStaticPhysicalPageRW[uint3(physicalTexel, layer)] = 0u;
    }
}

[numthreads(8, 8, 1)]
void VSMPrototypeClearPhysicalPages(uint3 id : SV_DispatchThreadID)
{
    ClearVSMPhysicalPage(id.z, id.xy);
}

[numthreads(8, 8, 1)]
void VSMClearPhysicalPagesIndirect(uint3 id : SV_DispatchThreadID)
{
    ClearVSMPhysicalPage(_VSMPageWorkList[id.z], id.xy);
}

[numthreads(64, 1, 1)]
void VSMPrototypeFinalizeDirtyPages(
    uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint virtualPageIndex = dispatchThreadID.x;
    if (virtualPageIndex >= (uint)_VSMPrototypePageTableEntryCount)
        return;

    uint4 metadata = _VSMPrototypePageMetadata[virtualPageIndex];
    if ((metadata.x & kVSMPageAllocated) == 0u
        || (metadata.x & kVSMPageDeferred) != 0u
        || (metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty)) == 0u)
    {
        return;
    }

    uint redrawn = metadata.x & (kVSMPageDirty | kVSMPageDynamicDirty);
    metadata.x = (metadata.x | kVSMPageCached) & ~(kVSMPageDirty | kVSMPageDynamicDirty);
    // Preserve local/full invalidation as dirty/redrawn, not an immediate cache hit.
    metadata.w = (metadata.w | redrawn) & ~(kVSMPageCached | kVSMPageDeferred);
    _VSMPrototypePageMetadata[virtualPageIndex] = metadata;
}

groupshared uint g_VSMPageNonempty;

// Run after both raster pools and before finalizing dirty pages. Ordered depth
// insertion guarantees that an empty first layer has no hidden nonzero layers.
void ReduceVSMPageOccupancy(uint physicalPageIndex, uint lane)
{
    if (physicalPageIndex >= (uint)_VSMPrototypePhysicalPageCapacity) return;
    uint owner = _VSMPrototypePhysicalPageOwners[physicalPageIndex];
    if (owner == 0u) return;
    uint page = owner - 1u;
    uint flags = _VSMPrototypePageMetadata[page].x;
    if ((flags & kVSMPageDeferred) != 0u) return;
    // Baseline diagnostics must not warm either depth pool. Drop the cached
    // proof so re-enabling the optimization rescans retained static storage.
    if (_VSMPageOccupancySkipDisabled != 0)
    {
        if (lane == 0u) _VSMPrototypePageMetadata[page].x = flags
            & ~(kVSMPageStaticEmpty | kVSMPageDynamicEmpty | kVSMPageStaticOccupancyKnown | kVSMPageDynamicOccupancyKnown);
        return;
    }
    bool scanStatic = (flags & kVSMPageDirty) != 0u
        || (flags & kVSMPageStaticOccupancyKnown) == 0u;
    bool scanDynamic = (flags & kVSMPageDynamicDirty) != 0u
        || (flags & kVSMPageDynamicOccupancyKnown) == 0u;
    if (!scanStatic && !scanDynamic) return;
    uint size = (uint)_VSMPrototypePageSize;
    uint row = (uint)_VSMPrototypePhysicalPagesPerRow;
    uint2 origin = uint2(physicalPageIndex % row, physicalPageIndex / row) * size;
    if (lane == 0u) g_VSMPageNonempty = 0u;
    GroupMemoryBarrierWithGroupSync();
    uint nonempty = 0u;
    for (uint texel = lane; texel < size * size; texel += 64u)
    {
        int4 address = int4(origin + uint2(texel % size, texel / size), 0, 0);
        if (scanStatic && (nonempty & 1u) == 0u
            && _VSMPrototypeStaticPhysicalPage.Load(address) != 0u) nonempty |= 1u;
        if (scanDynamic && (nonempty & 2u) == 0u
            && _VSMPrototypeDynamicPhysicalPage.Load(address) != 0u) nonempty |= 2u;
        if ((!scanDynamic || (nonempty & 2u) != 0u) && (!scanStatic || (nonempty & 1u) != 0u)) break;
    }
    InterlockedOr(g_VSMPageNonempty, nonempty);
    GroupMemoryBarrierWithGroupSync();
    if (lane != 0u) return;
    if (scanStatic)
        flags = (flags & ~kVSMPageStaticEmpty) | kVSMPageStaticOccupancyKnown
            | ((g_VSMPageNonempty & 1u) == 0u ? kVSMPageStaticEmpty : 0u);
    if (scanDynamic)
        flags = (flags & ~kVSMPageDynamicEmpty) | kVSMPageDynamicOccupancyKnown
            | ((g_VSMPageNonempty & 2u) == 0u ? kVSMPageDynamicEmpty : 0u);
    _VSMPrototypePageMetadata[page].x = flags;
}

[numthreads(64, 1, 1)]
void VSMPrototypeReducePageOccupancy(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    ReduceVSMPageOccupancy(group.x, lane);
}

[numthreads(64, 1, 1)]
void VSMReducePageOccupancyIndirect(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    ReduceVSMPageOccupancy(_VSMPageWorkList[(uint)_VSMPrototypePhysicalPageCapacity + group.x], lane);
}

