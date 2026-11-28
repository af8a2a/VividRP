// UE VirtualShadowMapThrottle.usf: previous raster load -> per-VSM resolution bias.
// Vivid has HW meshlet raster only: one cluster per nonempty culled window.
// Overflow fanout and padding are transport details, not additional clusters.
// Header 0: total HW/SW, max HW/SW. Header 1.x: global intensity.
// Entry [2 + level]: HW, SW, intensity bits, applied bias bits.
StructuredBuffer<uint4> _VSMPrevThrottle;
RWStructuredBuffer<uint4> _VSMThrottleRW;
RWStructuredBuffer<uint2> _VSMRasterFeedbackRW; // total, then per-level HW/SW
RWStructuredBuffer<VividVSMProjection> _VSMThrottleProjectionsRW;
int _VSMThrottlePreviousValid, _VSMThrottleEntriesValid;
float4 _VSMThrottleParameters; // load budget, history weight, max compute bias, time strength (-1 = load)
uint _VSMRasterFeedbackDrawMask;

groupshared uint g_VSMThrottleMaxHW, g_VSMThrottleMaxSW;

[numthreads(64, 1, 1)]
void VSMProcessPreviousPerformance(uint lane : SV_GroupIndex)
{
    if (lane == 0u) { g_VSMThrottleMaxHW = 0u; g_VSMThrottleMaxSW = 0u; }
    GroupMemoryBarrierWithGroupSync();
    if (lane < (uint)_VSMProjectionCount)
    {
        uint2 count = _VSMThrottlePreviousValid != 0 && _VSMThrottleEntriesValid != 0
            ? _VSMRasterFeedbackRW[1u + lane] : 0u;
        uint intensity = _VSMThrottlePreviousValid != 0 && _VSMThrottleEntriesValid != 0
            ? _VSMPrevThrottle[2u + lane].z : 0u;
        _VSMThrottleRW[2u + lane] = uint4(count, intensity, 0u);
        InterlockedMax(g_VSMThrottleMaxHW, count.x);
        InterlockedMax(g_VSMThrottleMaxSW, count.y);
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0u)
        _VSMThrottleRW[0] = uint4(_VSMThrottlePreviousValid != 0 ? _VSMRasterFeedbackRW[0] : 0u,
            g_VSMThrottleMaxHW, g_VSMThrottleMaxSW);
    // The previous counts have all been consumed before the next producer starts.
    if (lane <= (uint)_VSMProjectionCount) _VSMRasterFeedbackRW[lane] = 0u;
}

float VSMRasterCostHeuristic(uint2 clusters)
{
    // UE coefficients are workload units; they are NOT milliseconds on this GPU.
    return 0.00012176 * (float)clusters.x + 0.00002700 * (float)clusters.y;
}

[numthreads(64, 1, 1)]
void VSMUpdatePerformanceThrottle(uint lane : SV_DispatchThreadID)
{
    uint4 header = _VSMThrottleRW[0];
    float global = 0.0;
    if (_VSMThrottlePreviousValid != 0)
    {
        global = _VSMThrottleParameters.w >= 0.0 ? _VSMThrottleParameters.w
            : saturate(asfloat(_VSMPrevThrottle[1].x)
                + clamp((VSMRasterCostHeuristic(header.xy) - _VSMThrottleParameters.x) * 0.5, -0.01, 0.2));
    }
    if (lane == 0u) _VSMThrottleRW[1] = uint4(asuint(global), 0u, 0u, 0u);
    if (lane >= (uint)_VSMProjectionCount) return;
    uint4 entry = _VSMThrottleRW[2u + lane];
    float intensity = global;
    if (entry.x + entry.y != 0u)
    {
        float contribution = VSMRasterCostHeuristic(entry.xy) / VSMRasterCostHeuristic(header.zw);
        intensity = lerp(saturate(contribution * global), asfloat(entry.z), _VSMThrottleParameters.y);
    }
    float memoryBias = _VSMReceiverQuality.x > 1.5 ? asfloat(_VSMPagePressure[0].x) : 0.0;
    // UE: min(global max, directional compute max) - memory bias. Global max = 2.
    float bias = intensity * max(0.0, min(2.0, _VSMThrottleParameters.z) - memoryBias);
    entry.zw = uint2(asuint(intensity), asuint(bias));
    _VSMThrottleRW[2u + lane] = entry;
    // CPU upload resets z each frame; the former blend-border lane is unused by UE selection.
    VividVSMProjection projection = _VSMThrottleProjectionsRW[lane];
    projection.parameters.z = bias;
    _VSMThrottleProjectionsRW[lane] = projection;
}

// Reset per submission: main/post and static/dynamic reuse the same scratch.
// This kernel runs BEFORE culling. Capture runs AFTER the matching draws.
[numthreads(64, 1, 1)]
void VSMPrepareRasterFeedback(uint3 id : SV_DispatchThreadID)
{
    if (id.x < VIVIDRENDERERLISTID_COUNT * 2u * VIVID_VSM_RASTER_MAX_LEVELS)
        _VSMRasterClusterCountsRW[id.x] = 0u;
}

groupshared uint g_VSMRasterTotalClusters;
[numthreads(64, 1, 1)]
void VSMCaptureRasterFeedback(uint lane : SV_GroupIndex)
{
    if (lane == 0u) g_VSMRasterTotalClusters = 0u;
    GroupMemoryBarrierWithGroupSync();
    if (lane < (uint)_VSMProjectionCount)
    {
        uint clusters = 0u;
        for (uint draw = 0u; draw < VIVIDRENDERERLISTID_COUNT * 2u; draw++)
            if ((_VSMRasterFeedbackDrawMask & (1u << draw)) != 0u)
                clusters += _VSMRasterClusterCountsRW[draw * VIVID_VSM_RASTER_MAX_LEVELS + lane];
        // Each lane owns one VSM entry; all four raster submissions accumulate.
        _VSMRasterFeedbackRW[1u + lane] += uint2(clusters, 0u);
        InterlockedAdd(g_VSMRasterTotalClusters, clusters);
    }
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0u) _VSMRasterFeedbackRW[0] += uint2(g_VSMRasterTotalClusters, 0u);
}

// ============================================================================
// Phase 4: Local Light (Point / Spot) Per-VSM GPU Raster Performance Throttle
// Supports arbitrarily many unified local projections (strided 64-lane reduction).
// ============================================================================
RWStructuredBuffer<uint> _VSMLocalRasterClusterCountsRW;
int _VSMLocalThrottleStartVSMId;
int _VSMLocalThrottleCount;
int _VSMLocalThrottleMemoryPressureEnabled;
groupshared uint g_VSMLocalThrottleTotalHW;
groupshared uint g_VSMLocalThrottleMaxHW;

[numthreads(64, 1, 1)]
void VSMProcessLocalPerformance(uint lane : SV_GroupIndex)
{
    if (lane == 0u)
    {
        g_VSMLocalThrottleTotalHW = 0u;
        g_VSMLocalThrottleMaxHW = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    uint count = (uint)max(_VSMLocalThrottleCount, 0);
    uint startVsm = (uint)max(_VSMLocalThrottleStartVSMId, 0);
    uint localTotal = 0u;
    uint localMax = 0u;
    [loop]
    for (uint v = lane; v < count; v += 64u)
    {
        uint countHw = (_VSMThrottlePreviousValid != 0 && _VSMThrottleEntriesValid != 0 && v >= startVsm)
            ? _VSMLocalRasterClusterCountsRW[v] : 0u;
        uint intensity = (_VSMThrottlePreviousValid != 0 && _VSMThrottleEntriesValid != 0 && v >= startVsm)
            ? _VSMPrevThrottle[2u + v].z : 0u;
        _VSMThrottleRW[2u + v] = uint4(countHw, 0u, intensity, 0u);
        localTotal += countHw;
        localMax = max(localMax, countHw);
        // Clear feedback slot so the current frame accumulates fresh raster clusters.
        _VSMLocalRasterClusterCountsRW[v] = 0u;
    }
    InterlockedAdd(g_VSMLocalThrottleTotalHW, localTotal);
    InterlockedMax(g_VSMLocalThrottleMaxHW, localMax);
    GroupMemoryBarrierWithGroupSync();

    if (lane == 0u)
    {
        _VSMThrottleRW[0] = uint4(
            _VSMThrottlePreviousValid != 0 ? g_VSMLocalThrottleTotalHW : 0u,
            0u,
            g_VSMLocalThrottleMaxHW,
            0u);
    }
}

[numthreads(64, 1, 1)]
void VSMUpdateLocalPerformanceThrottle(uint3 id : SV_DispatchThreadID)
{
    uint vsmId = id.x;
    uint count = (uint)max(_VSMLocalThrottleCount, 0);
    uint startVsm = (uint)max(_VSMLocalThrottleStartVSMId, 0);

    uint4 header = _VSMThrottleRW[0];
    float global = 0.0;
    if (_VSMThrottlePreviousValid != 0)
    {
        global = _VSMThrottleParameters.w >= 0.0 ? _VSMThrottleParameters.w
            : saturate(asfloat(_VSMPrevThrottle[1].x)
                + clamp((VSMRasterCostHeuristic(header.xy) - _VSMThrottleParameters.x) * 0.5, -0.01, 0.2));
    }
    if (vsmId == 0u)
        _VSMThrottleRW[1] = uint4(asuint(global), 0u, 0u, 0u);
    if (vsmId >= count || vsmId < startVsm)
        return;

    uint4 entry = _VSMThrottleRW[2u + vsmId];
    float intensity = global;
    if (entry.x != 0u && header.z != 0u)
    {
        float contribution = VSMRasterCostHeuristic(uint2(entry.x, 0u)) / max(VSMRasterCostHeuristic(uint2(header.z, 0u)), 1e-8);
        intensity = lerp(saturate(contribution * global), asfloat(entry.z), _VSMThrottleParameters.y);
    }
    float memoryBias = _VSMLocalThrottleMemoryPressureEnabled != 0 ? asfloat(_VSMPagePressure[0].x) : 0.0;
    // Local VSMs support up to +4 mips of combined pressure + performance throttle bias.
    float bias = intensity * max(0.0, min(4.0, _VSMThrottleParameters.z) - memoryBias);
    entry.zw = uint2(asuint(intensity), asuint(bias));
    _VSMThrottleRW[2u + vsmId] = entry;

    VividVSMProjection projection = _VSMThrottleProjectionsRW[vsmId];
    if (!VividVSMProjectionIsSinglePage(projection))
    {
        projection.parameters.z += bias;
        _VSMThrottleProjectionsRW[vsmId] = projection;
    }
}
