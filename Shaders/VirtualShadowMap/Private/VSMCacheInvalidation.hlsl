// CacheGPUInvalidation / CacheInvalidation.ush adaptation for Vivid's one
// primitive per slot and one retained directional view. CPU owns primitive age;
// GPU owns the last consumed generation, footprint and cache/deformation state.
struct VSMInvalidationSource
{
    float4 BoundsMin, BoundsMax;
    uint4 State; // revision, generation, flags, camera mask (queue: x = dirty bits)
};
StructuredBuffer<VSMInvalidationSource> _VSMInvalidationSources;
RWStructuredBuffer<VSMInvalidationSource> _VSMInvalidationStates;
RWStructuredBuffer<VSMInvalidationSource> _VSMInvalidationQueue;
RWByteAddressBuffer _VSMInvalidationArgs;
RWByteAddressBuffer _VSMInvalidationDispatchArgs;
uint _VSMInvalidationSourceCount, _VSMInvalidationCameraMask;
int _VSMInvalidationReset, _VSMDeformableMeshesInvalidate, _VSMInvalidateUseHZB;

[numthreads(1, 1, 1)]
void VSMResetInvalidationQueue(uint3 id : SV_DispatchThreadID)
{
    _VSMInvalidationArgs.Store4(0u, uint4(0u, 1u, 1u, 0u));
    _VSMInvalidationArgs.Store4(16u, 0u);
}

uint VSMInvalidationDirtyMask(VSMInvalidationSource source)
{
    return (source.State.z & 2u) != 0u ? kVSMPageDirty : kVSMPageDynamicDirty;
}

void VSMEnqueueInvalidation(VSMInvalidationSource source, uint mask, bool emit)
{
    uint count = WaveActiveCountBits(emit), base = 0u;
    if (WaveIsFirstLane() && count != 0u) _VSMInvalidationArgs.InterlockedAdd(12u, count, base);
    base = WaveReadLaneFirst(base);
    uint offset = WavePrefixCountBits(emit);
    if (emit)
    {
        source.State.x = mask;
        _VSMInvalidationQueue[base + offset] = source;
    }
}

[numthreads(64, 1, 1)]
void VSMUpdateInvalidationInstances(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _VSMInvalidationSourceCount) return;
    VSMInvalidationSource current = _VSMInvalidationSources[id.x];
    if ((current.State.w & _VSMInvalidationCameraMask) == 0u) current.State.z &= ~1u;
    if (_VSMInvalidationReset != 0)
    {
        _VSMInvalidationStates[id.x] = current;
        return; // Caller invalidates both pools on reset/growth/view replacement.
    }
    VSMInvalidationSource previous = _VSMInvalidationStates[id.x];
    bool changed = any(previous.State != current.State);
    bool oldActive = (previous.State.z & 1u) != 0u;
    bool active = (current.State.z & 1u) != 0u;
    bool deforming = active && (current.State.z & 4u) != 0u && _VSMDeformableMeshesInvalidate != 0;
    bool emitOld = oldActive && changed;
    bool emitNew = active && (changed || deforming);
    bool sameFootprint = all(previous.BoundsMin == current.BoundsMin)
        && all(previous.BoundsMax == current.BoundsMax)
        && ((previous.State.z ^ current.State.z) & 8u) == 0u;
    // Material/VT changes and state transitions commonly share a footprint.
    uint oldMask = VSMInvalidationDirtyMask(previous), newMask = VSMInvalidationDirtyMask(current);
    if (emitOld && emitNew && sameFootprint) { oldMask |= newMask; emitNew = false; }
    VSMEnqueueInvalidation(previous, oldMask, emitOld);
    VSMEnqueueInvalidation(current, newMask, emitNew);
    if (changed) _VSMInvalidationStates[id.x] = current;
}

[numthreads(1, 1, 1)]
void VSMPrepareInvalidationQueue(uint3 id : SV_DispatchThreadID)
{
    uint jobs = _VSMInvalidationArgs.Load(12u) * (uint)_VSMProjectionCount;
    _VSMInvalidationArgs.Store3(0u, uint3(min(jobs, 512u), 1u, 1u));
    _VSMInvalidationDispatchArgs.Store3(0u, uint3(min(jobs, 512u), 1u, 1u));
}

groupshared uint g_VSMInvalidationJob;
groupshared uint4 g_VSMInvalidationRect;
groupshared float4 g_VSMInvalidationUV;
groupshared float g_VSMInvalidationDepth;
groupshared uint g_VSMInvalidationMask;

void VSMSetupInvalidation(VSMInvalidationSource source, uint level)
{
    g_VSMInvalidationMask = source.State.x;
    g_VSMInvalidationUV = float4(0, 0, 1, 1);
    g_VSMInvalidationDepth = 1.0;
    float3 center = (source.BoundsMin.xyz + source.BoundsMax.xyz) * 0.5;
    float3 extent = (source.BoundsMax.xyz - source.BoundsMin.xyz) * 0.5;
    float4x4 matrix = _VSMProjections[level].worldToShadow;
    bool bounded = (source.State.z & 8u) == 0u && all(extent >= 0.0)
        && all(isfinite(center)) && all(isfinite(extent)) && all(matrix[3].xyz == 0.0) && matrix[3].w > 0.0;
    if (bounded)
    {
        float3 p = mul(matrix, float4(center, 1)).xyz;
        float3 support = float3(dot(abs(matrix[0].xyz), extent), dot(abs(matrix[1].xyz), extent),
            dot(abs(matrix[2].xyz), extent));
        float3 magnitude = float3(dot(abs(matrix[0].xyz), abs(center)) + abs(matrix[0].w),
            dot(abs(matrix[1].xyz), abs(center)) + abs(matrix[1].w),
            dot(abs(matrix[2].xyz), abs(center)) + abs(matrix[2].w)) + support;
        support += 1e-6 * magnitude + 1e-7;
        float3 low = (p - support) / matrix[3].w, high = (p + support) / matrix[3].w;
        if (all(isfinite(low)) && all(isfinite(high)))
        {
            if (any(high.xy < 0) || any(low.xy > 1) || high.z < 0 || low.z > 1)
                g_VSMInvalidationMask = 0u;
            g_VSMInvalidationUV = saturate(float4(low.xy, high.xy));
            // UE's epsilon prevents self-occlusion of nearly coplanar geometry.
            g_VSMInvalidationDepth = high.z + 1e-8;
        }
    }
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    g_VSMInvalidationRect = min((uint4)(g_VSMInvalidationUV * axis), axis - 1u);
}

bool VSMInvalidationPageOccluded(uint index, uint2 page)
{
    if (_VSMInvalidateUseHZB == 0 || _VSMHZBHistoryValid == 0 || g_VSMInvalidationDepth >= 1.0) return false;
    uint encoded = _VSMHZBPreviousTable[index];
    if (encoded == 0u || encoded > (uint)_VSMPrototypePhysicalPageCapacity) return false;
    // Per-page clamped hierarchical test. Expand to whole intersecting texels;
    // empty/missing history stays visible. Invalidation always uses STATIC HZB.
    uint resolution = (uint)_VSMPrototypeVirtualResolution;
    uint4 pixels = min((uint4)(g_VSMInvalidationUV * resolution), resolution - 1u);
    uint2 baseLow = pixels.xy >> 1u, baseHigh = pixels.zw >> 1u;
    int2 logSize = firstbithigh(baseHigh - baseLow);
    uint mip = (uint)max(max(logSize.x, logSize.y) - 1, 0);
    mip += any((baseHigh >> mip) - (baseLow >> mip) > 3u) ? 1u : 0u;
    mip = min(mip, 6u);
    uint size = 64u >> mip;
    uint2 origin = page * size;
    uint2 lo = max(baseLow >> mip, origin) - origin;
    uint2 hi = min(baseHigh >> mip, origin + size - 1u) - origin;
    uint slot = encoded - 1u;
    uint2 physical = uint2(slot % (uint)_VSMPrototypePhysicalPagesPerRow,
        slot / (uint)_VSMPrototypePhysicalPagesPerRow) * size;
    float farthest = 1.0;
    for (uint y = lo.y; y <= hi.y; y++)
    for (uint x = lo.x; x <= hi.x; x++)
        farthest = min(farthest, _VSMHZB.Load(int4(physical + uint2(x, y), 1, mip)));
    return g_VSMInvalidationDepth < farthest;
}

// Bounded persistent worker groups pull instance/clipmap jobs dynamically.
// Wave compaction combines sparse events; 64 lanes share each projected rect,
// unlike the legacy single-thread static bounds loop. No oversized job buffer.
[numthreads(64, 1, 1)]
void VSMProcessInvalidationQueue(uint lane : SV_GroupIndex)
{
    uint count = _VSMInvalidationArgs.Load(12u) * (uint)_VSMProjectionCount;
    for (;;)
    {
        if (lane == 0u) _VSMInvalidationArgs.InterlockedAdd(16u, 1u, g_VSMInvalidationJob);
        GroupMemoryBarrierWithGroupSync();
        uint job = g_VSMInvalidationJob;
        if (job >= count) return;
        uint level = job % (uint)_VSMProjectionCount;
        if (lane == 0u) VSMSetupInvalidation(_VSMInvalidationQueue[job / (uint)_VSMProjectionCount], level);
        GroupMemoryBarrierWithGroupSync();
        uint width = g_VSMInvalidationRect.z - g_VSMInvalidationRect.x + 1u;
        uint pages = g_VSMInvalidationMask != 0u ? width * (g_VSMInvalidationRect.w - g_VSMInvalidationRect.y + 1u) : 0u;
        uint axis = (uint)_VSMPrototypePagesPerAxis;
        uint tested = 0u, culled = 0u, written = 0u;
        for (uint p = lane; p < pages; p += 64u)
        {
            uint2 page = g_VSMInvalidationRect.xy + uint2(p % width, p / width);
            uint index = level * axis * axis + page.y * axis + page.x;
            uint flags = _VSMPrototypePageMetadata[index].x;
            if ((flags & kVSMPageAllocated) == 0u) continue;
            tested++;
            if (VSMInvalidationPageOccluded(index, page))
            {
                culled++;
                continue;
            }
            uint ignored;
            InterlockedOr(_VSMPrototypePageMetadata[index].x, g_VSMInvalidationMask, ignored);
            if ((g_VSMInvalidationMask & kVSMPageDirty) != 0u)
                InterlockedAnd(_VSMPrototypePageMetadata[index].x, ~kVSMPageCached, ignored);
            written++;
        }
        tested = WaveActiveSum(tested); culled = WaveActiveSum(culled); written = WaveActiveSum(written);
        if (WaveIsFirstLane())
        {
            _VSMInvalidationArgs.InterlockedAdd(20u, tested);
            _VSMInvalidationArgs.InterlockedAdd(24u, culled);
            _VSMInvalidationArgs.InterlockedAdd(28u, written);
        }
        GroupMemoryBarrierWithGroupSync(); // Don't overwrite the rect while another wave consumes it.
    }
}
