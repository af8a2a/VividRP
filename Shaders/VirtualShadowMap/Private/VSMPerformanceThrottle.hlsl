// UE VirtualShadowMapThrottle.usf: previous raster load -> per-VSM resolution bias.
// Vivid has HW meshlet raster only. Counts are submitted raster instances, not vertices.
// Header 0: total HW/SW, max HW/SW. Header 1.x: global intensity.
// Entry [2 + level]: HW, SW, intensity bits, applied bias bits.
StructuredBuffer<uint4> _VSMPrevThrottle;
RWStructuredBuffer<uint4> _VSMThrottleRW;
RWStructuredBuffer<uint2> _VSMRasterFeedbackRW; // total, then per-level HW/SW
RWStructuredBuffer<VividVSMProjection> _VSMThrottleProjectionsRW;
RWByteAddressBuffer _VSMRasterFeedbackArgs;
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

[numthreads(1, 1, 1)]
void VSMPrepareRasterFeedback(uint3 id : SV_DispatchThreadID)
{
    uint maxRecords = 0u, totalHW = 0u;
    uint fanout = _VSMPrototypeMeshletRasterPages[0];
    for (uint draw = 0u; draw < VIVIDRENDERERLISTID_COUNT * 2u; draw++)
    {
        if ((_VSMRasterFeedbackDrawMask & (1u << draw)) == 0u) continue;
        uint instances = _VSMPrototypeMeshletPageIndirectArgs.Load(
            GetVSMPageDrawArgsAddress(draw) + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET);
        totalHW += instances;
        uint records = draw < VIVIDRENDERERLISTID_COUNT ? instances : instances / max(fanout, 1u);
        maxRecords = max(maxRecords, records);
    }
    _VSMRasterFeedbackRW[0] += uint2(totalHW, 0u);
    _VSMRasterFeedbackArgs.Store3(0u, uint3((maxRecords + 63u) / 64u, VIVIDRENDERERLISTID_COUNT * 2u, 1u));
}

[numthreads(64, 1, 1)]
void VSMCaptureRasterFeedback(uint3 id : SV_DispatchThreadID)
{
    uint draw = id.y;
    if ((_VSMRasterFeedbackDrawMask & (1u << draw)) == 0u) return;
    uint address = GetVSMPageDrawArgsAddress(draw);
    uint count = _VSMPrototypeMeshletPageIndirectArgs.Load(address + VIVID_INDIRECT_DRAW_ARGS_INSTANCE_COUNT_OFFSET);
    uint start = _VSMPrototypeMeshletPageIndirectArgs.Load(address + VIVID_INDIRECT_DRAW_ARGS_START_INSTANCE_OFFSET);
    bool overflow = draw >= VIVIDRENDERERLISTID_COUNT;
    uint weight = overflow ? _VSMPrototypeMeshletRasterPages[0] : 1u;
    if (id.x >= count / max(weight, 1u)) return;
    uint4 request = _VSMPrototypeMeshletPageRequests[overflow ? start - 1u - id.x : start + id.x];
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint level = request.z / (axis * axis);
    // Large-record fanout really issues every raster-list instance, including VS rejects.
    // Charge that HW work to its source VSM, including main + post and static + dynamic.
    InterlockedAdd(_VSMRasterFeedbackRW[1u + level].x, weight);
}
