// VSM has no PCSS penumbra tile list. Filter all receivers, including samples
// that happened to be fully lit/shadowed in this low-ray-count frame.
[numthreads(8, 8, 1)]
void VSMShadowBilateralFilterH(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    if (dispatchThreadID.x >= (uint)_CSMOutputWidth || dispatchThreadID.y >= (uint)_CSMOutputHeight)
        return;
    _CSMShadowFilterTexture[dispatchThreadID.xy] = FilterCSMShadow(dispatchThreadID.xy, int2(1, 0));
}

[numthreads(8, 8, 1)]
void VSMShadowBilateralFilterV(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    if (dispatchThreadID.x >= (uint)_CSMOutputWidth || dispatchThreadID.y >= (uint)_CSMOutputHeight)
        return;
    _CSMShadowFilterTexture[dispatchThreadID.xy] = FilterCSMShadow(dispatchThreadID.xy, int2(0, 1));
}

// Validate each bilinear tap before interpolation. Screen-space shadow motion
// is additionally rejected/clipped against this frame in the temporal resolve.
bool LoadVSMHistory(uint2 pixel, float3 world, float3 normal, out float2 history)
{
    history = 0;
    if (_VSMHistoryParameters.x < 0.5) return false;
#if defined(VIVID_VSM_PAGE_PRESSURE)
    if (_VSMReceiverQuality.x > 1.5 && _VSMPagePressure[0].x != _VSMPagePressure[1].z) return false;
#endif
    float4 previousCS = mul(_VSMPreviousViewProjection, float4(world, 1));
    if (previousCS.w <= 0) return false;
    float2 uv = previousCS.xy / previousCS.w * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1 - uv.y;
#endif
    float2 coord = uv * float2(_CSMOutputWidth, _CSMOutputHeight) - 0.5;
    int2 base = int2(floor(coord));
    float2 fraction = frac(coord);
    float expectedDepth = -mul(_VSMPreviousView, float4(world, 1)).z;
    float weight = 0;
    float age = _VSMHistoryParameters.z;
    [unroll]
    for (int y = 0; y < 2; y++)
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            int2 p = base + int2(x, y);
            if (any(p < 0) || any(p >= int2(_CSMOutputWidth, _CSMOutputHeight))) continue;
            float w = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
            if (w < 0.001) continue;
            float4 signal = _VSMHistoryPrevious.Load(int3(p, 0));
            float depth = _VSMDepthPrevious.Load(int3(p, 0));
            if (signal.y < 1 || abs(depth - expectedDepth) > max(0.02, expectedDepth * 0.002)
                || dot(normal, DecodeVividNormalOct(signal.zw)) < 0.95) continue;
            history.x += signal.x * w;
            age = min(age, signal.y);
            weight += w;
        }
    // Partial coverage at a silhouette must not authorize a low ray budget.
    if (weight < 0.99) { history = 0; return false; }
    history = float2(history.x / weight, age);
    return true;
}

[numthreads(8, 8, 1)]
void VSMShadowTemporalV(uint3 id : SV_DispatchThreadID)
{
    uint2 pixel = id.xy;
    if (any(pixel >= uint2(_CSMOutputWidth, _CSMOutputHeight))) return;
    float current = FilterCSMShadow(pixel, int2(0, 1));
    float deviceDepth = _DepthTexture.Load(int3(pixel, 0));
    float2 octNormal = _GBuffer1.Load(int3(pixel, 0)).xy;
    float age = 1;
    float depth = 0;
    if (!IsSkyPixel(deviceDepth))
    {
        float3 world = ReconstructWorldPosition(pixel, deviceDepth);
        depth = -mul(_VSMCurrentView, float4(world, 1)).z;
        float3 normal = DecodeVividNormalOct(octNormal);
        float2 history;
        if (LoadVSMHistory(pixel, world, normal, history))
        {
            float low = current, high = current;
            [unroll]
            for (int y = -1; y <= 1; y++)
                [unroll]
                for (int x = -1; x <= 1; x++)
                {
                    int2 p = clamp(int2(pixel) + int2(x, y), 0, int2(_CSMOutputWidth, _CSMOutputHeight) - 1);
                    if (ComputeCSMShadowBilateralWeight(deviceDepth, normal, uint2(p)) < 0.5) continue;
                    float value = _CSMShadowFilterSource.Load(int3(p, 0));
                    low = min(low, value); high = max(high, value);
                }
            // React to moving casters even when receiver depth/normal are static.
            // Uniform newly lit/shadowed neighborhoods clamp history immediately.
            // Relax noisy bounds only for small innovations. Larger changes
            // retain the original clamp, and a uniform newly lit/shadowed
            // neighborhood still discards stale history immediately.
            float margin = min((high - low) * 0.5,
                max(0, 0.05 - abs(history.x - current)));
            float old = clamp(history.x, max(0, low - margin), min(1, high + margin));
            age = abs(history.x - current) > 0.2 ? 1 : min(history.y + 1, _VSMHistoryParameters.z);
            current = lerp(old, current, rcp(age));
        }
    }
    else age = 0;
    _CSMShadowFilterTexture[pixel] = current;
    _VSMHistoryCurrent[pixel] = float4(current, age, octNormal);
    _VSMDepthCurrent[pixel] = depth;
}

// Bias lives at the receiver for both caster backends. The pool contains raw
// depth, so neither raster-bias state nor a fixed device-depth offset is part of
// VSM cache content. xy is dz per virtual texel; z is signed comparison bias;
// w is the angle-scaled world normal offset. normalWS is normalized (or zero).
