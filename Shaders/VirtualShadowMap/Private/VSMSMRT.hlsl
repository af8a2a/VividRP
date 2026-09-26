// Directional layered-depth-field ray tracing. This is a bounded texel-cell
// approximation, not geometry ray tracing or a copy of Unreal's private SMRT.
// t is world distance along the central light axis. Clipmap segments keep the
// original light-disk direction and world origin, including its receiver bias.
// Only the configured maximum world length starts the parallel-light tail.

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/BlueNoise.hlsl"

// Keep the starting projection in registers across footprint validation and rays.
// Destination clipmaps may scroll independently, so retain the affine translation.
struct VSMSMRTProjection
{
    float texelSize;
    float depthScale;
    float3 translation;
};

VSMSMRTProjection PrepareVSMSMRTProjection(VividVSMProjection projection)
{
    VSMSMRTProjection result;
    result.texelSize = projection.parameters.x;
    result.depthScale = length(projection.worldToShadow[2].xyz);
    result.translation = float3(projection.worldToShadow._m03,
        projection.worldToShadow._m13, projection.worldToShadow._m23);
    return result;
}

float3 VSMSMRTProjectionScale(VSMSMRTProjection from, VSMSMRTProjection to)
{
    float xy = from.texelSize / to.texelSize;
    return float3(xy, xy, to.depthScale / from.depthScale);
}

float3 VSMSMRTReproject(float3 coord, VSMSMRTProjection from, VSMSMRTProjection to, float3 scale)
{
    // Preserve the subtraction/multiply/add order, including the starting level.
    return (coord - from.translation) * scale + to.translation;
}

bool HasVSMSMRTFootprint(float2 uv, int index, VSMSMRTProjection projection)
{
    VSM_COST_ADD(4, 1u);
    float originRadius = _VSMReceiverParameters.x >= 0.5 ? 1.5 * projection.texelSize : 0;
    for (int level = index; level < _VSMProjectionCount; level++)
    {
        VSM_COST_ADD(5, 1u);
        VSMSMRTProjection destination = projection;
        if (level != index) destination = PrepareVSMSMRTProjection(_VSMProjections[level]);
        float end = VSMSMRTRayLength(level, destination.texelSize);
        float3 scale = VSMSMRTProjectionScale(projection, destination);
        float2 levelUV = VSMSMRTReproject(float3(uv, 0), projection, destination, scale).xy;
        int halo = (int)ceil((end * _VSMSMRTParameters.w + originRadius)
            / destination.texelSize + 0.001);
        int2 center = int2(floor(levelUV * _VSMPrototypeVirtualResolution));
        int2 low = center - halo, high = center + halo;
        if (any(low < 0) || any(high >= _VSMPrototypeVirtualResolution)) return false;
        int2 lowPage = low / _VSMPrototypePageSize, highPage = high / _VSMPrototypePageSize;
        for (int y = lowPage.y; y <= highPage.y; y++)
            for (int x = lowPage.x; x <= highPage.x; x++)
            {
                int2 physical;
                uint flags;
                VSM_COST_ADD(6, 1u);
                int2 pageOrigin = int2(x, y) * _VSMPrototypePageSize;
                if (!TryResolveVSMPhysicalTexelInternal(pageOrigin, level, physical, flags, false))
                    return false;
                if (_VSMReceiverMaskEnabled != 0)
                {
                    uint2 mask = VividVSMReceiverMaskRect((uint2)(max(low, pageOrigin) - pageOrigin),
                        (uint2)(min(high, pageOrigin + _VSMPrototypePageSize - 1) - pageOrigin),
                        (uint)_VSMPrototypePageSize);
                    if (!VividVSMReceiverMaskContains(LoadVSMPhysicalReceiverMask(physical), mask))
                    {
#if defined(VIVID_VSM_RECEIVER_DEBUG)
                        g_VSMDebugMissing |= 16u;
#endif
                        return false;
                    }
                }
            }
        if (end >= _VSMSMRTParameters.z) return true;
    }
    // No projection support is unavailable, not a shorter soft ray.
    return false;
}

// Receiver-only BND index offset, shared by every ray in the stratified set.
uint _VSMSMRTSampleIndexOffset;

float2 VSMSMRTPhase(uint2 pixel, uint frame, uint dimension)
{
    frame += _VSMSMRTSampleIndexOffset;
    // Reuse the 1SPP tiles with the full 256-frame sequence, not the static
    // 1SPP sample-index mask. These are phases for a stratified ray set, not STBN.
    return float2(GetBNDSequenceSample1SPPTemporal(pixel, frame, dimension),
        GetBNDSequenceSample1SPPTemporal(pixel, frame, dimension + 1u));
}

struct VSMSMRTReceiverSamples
{
    float2 diskPhase;
    float2 receiverPhase;
    bool ready;
};

void PrepareVSMSMRTReceiverSamples(uint2 pixel, inout VSMSMRTReceiverSamples samples)
{
    if (samples.ready) return;
    samples.diskPhase = VSMSMRTPhase(pixel, (uint)_CSMFrameIndex, 0u);
    samples.receiverPhase = 0;
    if (_VSMReceiverParameters.x >= 0.5)
        samples.receiverPhase = VSMSMRTPhase(pixel, (uint)_CSMFrameIndex, 2u);
    samples.ready = true;
}

float2 VSMSMRTProgressiveSample(float2 phase, uint ray)
{
    // Every ray has full-domain support under the random phase, including ray
    // zero. Power-of-two prefixes stratify the first dimension; no coordinate
    // depends on the eventual ray count, so an early exit keeps a valid prefix.
    return frac(phase + float2(reversebits(ray) * 2.3283064365386963e-10,
        frac(ray * 0.618033989)));
}

float2 VSMSMRTDiskSample(float2 phase, uint ray)
{
    float2 samplePoint = VSMSMRTProgressiveSample(phase, ray);
    float radius = sqrt(samplePoint.x);
    float sine, cosine;
    sincos(kPCSSTwoPi * samplePoint.y, sine, cosine);
    return radius * float2(cosine, sine);
}

float2 VSMSMRTReceiverOffset(float2 phase, uint ray)
{
    // A separate phase integrates the same two-texel receiver support.
    return 2 * VSMSMRTProgressiveSample(phase, ray) - 1;
}

// Diagnostic A/B only; zero uses the ordered search.
int _VSMDepthSearchLinear;

bool VSMSMRTHasDepthInInterval(Texture2DArray<uint> pool, int2 texel, uint frontDepth,
    float originDepth, float depthPerWorld, float enter, float exitTime, float thickness, uint costCounter = 18u)
{
    if (frontDepth == 0u) return false;
    float surface = (asfloat(frontDepth) - originDepth) / depthPerWorld;
    if (surface <= enter) return false;
    if (surface - thickness <= exitTime) return true;
    // Atomic insertion leaves a descending prefix of distinct reverse-Z depths,
    // followed by zeros. Search each pool independently: their layer ranks do
    // not correspond. Keep the original floating-point interval comparisons.
    uint low = 1u, high = VIVID_VSM_DEPTH_LAYER_COUNT;
    // Fifteen hidden layers require at most four binary probes. Unroll this
    // fixed bound while retaining the same probe order and early termination.
    [unroll]
    for (uint probe = 0u; probe < 4u; probe++)
    {
        if (low >= high) break;
        uint layer = (low + high) >> 1u;
        uint depth = pool.Load(int4(texel, layer, 0));
        VSM_COST_ADD(costCounter, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugWork.y++;
#endif
        surface = depth == 0u ? -1 : (asfloat(depth) - originDepth) / depthPerWorld;
        if (surface <= enter) high = layer;
        else if (surface - thickness <= exitTime) return true;
        else low = layer + 1u;
    }
    return false;
}

bool TryTraceVSMSMRTRay(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    float startTime, float rayLength, bool parallelTail, float thickness, int budget, int index, out float visibility)
{
    visibility = 1;
    float2 start = origin.xy * _VSMPrototypeVirtualResolution + texelsPerWorld * startTime;
    int2 cell = int2(floor(start));
    int2 direction = int2(texelsPerWorld.x >= 0 ? 1 : -1, texelsPerWorld.y >= 0 ? 1 : -1);
    // At an exact boundary, a negative ray enters the cell on its left.
    cell -= int2(direction.x < 0 && start.x == cell.x ? 1 : 0,
                 direction.y < 0 && start.y == cell.y ? 1 : 0);
    float2 stepTime = 1 / max(abs(texelsPerWorld), 1e-20);
    float2 boundary = float2(cell) + float2(direction.x > 0 ? 1 : 0, direction.y > 0 ? 1 : 0);
    float2 nextTime = startTime + abs(boundary - start) * stepTime;
    if (abs(texelsPerWorld.x) < 1e-10) nextTime.x = 1e20;
    if (abs(texelsPerWorld.y) < 1e-10) nextTime.y = 1e20;
    float enter = startTime;
    float previousSurface = -1;
    int2 pageLow = 0, pageHigh = 0, physicalOffset = 0;
    uint pageFlags = 0u;
    uint2 receiverMask = 0u;
    [loop]
    for (int sampleIndex = 0; sampleIndex < budget; sampleIndex++)
    {
        VSM_COST_ADD(10, 1u);
        int2 physical;
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugWork.x++;
#endif
        // Mappings are immutable during the resolve dispatch. Adjacent DDA
        // cells reuse the validated physical page, including empty depth cells.
        if (any(cell < pageLow) || any(cell >= pageHigh))
        {
            VSM_COST_ADD(11, 1u);
            if (!TryResolveVSMPhysicalTexel(cell, index, physical, pageFlags))
            {
                VSM_COST_ADD(28, 1u);
                return false;
            }
            pageLow = (cell / _VSMPrototypePageSize) * _VSMPrototypePageSize;
            pageHigh = pageLow + _VSMPrototypePageSize;
            physicalOffset = physical - cell;
            receiverMask = LoadVSMPhysicalReceiverMask(physical);
        }
        else
        {
            VSM_COST_ADD(12, 1u);
            physical = cell + physicalOffset;
        }
        // Mapping reuse must not imply coverage of another cell in the page.
        if (!VividVSMReceiverMaskTexel(receiverMask, (uint2)(cell - pageLow), (uint)_VSMPrototypePageSize))
        {
            VSM_COST_ADD(28, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
            g_VSMDebugMissing |= 16u;
#endif
            return false;
        }
        uint2 frontDepths = LoadVSMDepthLayer(physical, 0, pageFlags);
        uint rawDepth = max(frontDepths.x, frontDepths.y);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugWork.y++;
#endif
        float exitTime = min(nextTime.x, nextTime.y);
        bool segmentEnd = exitTime >= rayLength;
        bool tail = segmentEnd && parallelTail;
        exitTime = min(exitTime, rayLength);
        float surface = rawDepth == 0 ? -1 : (asfloat(rawDepth) - origin.z) / depthPerWorld;
        // A texel represents a finite slab behind its front depth. Test the
        // actual cell interval, not "in shadow at any step". Never interpolate
        // a crossing across a depth discontinuity or a valid empty texel.
        bool hit = surface > enter && (tail || surface - thickness <= exitTime);
        // One-cell gap fill behind a nearer foreground layer. It cannot extend
        // through empty depth or recursively propagate across multiple cells.
        bool gap = rawDepth != 0 && surface > previousSurface + thickness
            && previousSurface > enter && previousSurface - thickness <= min(exitTime, rayLength);
        if (!hit && !gap && surface > enter)
        {
            [branch]
            if (_VSMDepthSearchLinear == 0)
            {
                hit = VSMSMRTHasDepthInInterval(_VSMPrototypeStaticPhysicalPage,
                    physical, frontDepths.x, origin.z, depthPerWorld, enter, exitTime, thickness);
                if (!hit)
                    hit = VSMSMRTHasDepthInInterval(_VSMPrototypeDynamicPhysicalPage,
                        physical, frontDepths.y, origin.z, depthPerWorld, enter, exitTime, thickness, 19u);
            }
            else
            {
                // Static and dynamic pools have independent ordering. Combining
                // their layer ranks with max would lose a hidden moving surface.
                uint2 depths = frontDepths;
                for (uint layer = 0; layer < VIVID_VSM_DEPTH_LAYER_COUNT; layer++)
                {
                    if (layer != 0)
                    {
                        depths = LoadVSMDepthLayer(physical, layer, pageFlags);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
                        g_VSMDebugWork.y++;
#endif
                    }
                    float2 surfaces = float2(depths.x == 0 ? -1 : (asfloat(depths.x) - origin.z) / depthPerWorld,
                        depths.y == 0 ? -1 : (asfloat(depths.y) - origin.z) / depthPerWorld);
                    if (all(surfaces <= enter)) break;
                    hit = any((surfaces > enter) & (tail | (surfaces - thickness <= exitTime)));
                    if (hit) break;
                }
            }
        }
        if (hit || gap)
        {
            VSM_COST_ADD(15, 1u);
            VSM_COST_ADD(27, gap ? 1u : 0u);
            visibility = 0; return true;
        }
        if (segmentEnd)
        {
            VSM_COST_ADD(14, parallelTail ? 1u : 0u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
            if (parallelTail) g_VSMDebugSMRT.y++;
#endif
            return true;
        }
        previousSurface = surface;
        enter = exitTime;
        // Cross both axes at a corner; never sample the two zero-length cells.
        bool crossX = nextTime.x <= nextTime.y;
        bool crossY = nextTime.y <= nextTime.x;
        if (crossX) { cell.x += direction.x; nextTime.x += stepTime.x; }
        if (crossY) { cell.y += direction.y; nextTime.y += stepTime.y; }
    }
    // Numerical/budget failure is unavailable, never an implicit clear ray.
    VSM_COST_ADD(26, 1u);
    return false;
}

// Single-level contract used by focused cell-intersection diagnostics.
bool TryTraceVSMSMRTRay(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    float rayLength, float thickness, int budget, int index, out float visibility)
{
    return TryTraceVSMSMRTRay(origin, texelsPerWorld, depthPerWorld,
        0, rayLength, true, thickness, budget, index, visibility);
}

bool TryTraceVSMSMRTClipmaps(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, VSMSMRTProjection projection, out float visibility)
{
    visibility = 1;
    float startTime = 0;
    for (int level = index; level < _VSMProjectionCount; level++)
    {
        VSMSMRTProjection destination = projection;
        if (level != index) destination = PrepareVSMSMRTProjection(_VSMProjections[level]);
        float endTime = VSMSMRTRayLength(level, destination.texelSize);
        bool last = endTime >= _VSMSMRTParameters.z;
        float3 scale = VSMSMRTProjectionScale(projection, destination);
        float3 levelOrigin = VSMSMRTReproject(origin, projection, destination, scale);
        int segmentBudget = budget;
        if (level == _VSMProjectionCount - 1)
        {
            // DDA cell count <= ceil(Manhattan displacement) + 2. The last
            // available map finishes the world length instead of shortening it
            // or forcing every long ray into PCF solely for lack of another LOD.
            float distanceInTexels = (endTime - startTime) * _VSMSMRTParameters.w
                / destination.texelSize;
            segmentBudget = max(segmentBudget, (int)ceil(distanceInTexels * 1.41421357 + 0.001) + 2);
        }
        // Bias/jitter are already in origin. Do not apply a coarser receiver
        // bias or restart at t=0, and never carry gap history across clipmaps.
        VSM_COST_ADD(9, 1u);
        if (!TryTraceVSMSMRTRay(levelOrigin, texelsPerWorld * scale.xy, depthPerWorld * scale.z,
                startTime, endTime, last, 0.5 * destination.texelSize,
                segmentBudget, level, visibility)) return false;
        if (visibility == 0 || last) return true;
        startTime = endTime;
    }
    return false;
}

// Standalone tracing diagnostics prepare their starting projection once too.
bool TryTraceVSMSMRTClipmaps(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, out float visibility)
{
    return TryTraceVSMSMRTClipmaps(origin, texelsPerWorld, depthPerWorld, budget, index,
        PrepareVSMSMRTProjection(_VSMProjections[index]), visibility);
}

bool VSMWaveCanFinish(int rayIndex, float visibilitySum, bool rayValid, inout bool waveComplete)
{
    // Latch failure before an unavailable lane leaves the loop. Remaining
    // lanes must not mistake its absence for unanimous visibility.
    waveComplete = WaveActiveAllTrue(waveComplete && rayValid);
    bool unanimous = WaveActiveAllTrue(rayIndex == 0 ? visibilitySum == 1 : visibilitySum == 0);
    // UE directional rule with AdaptiveRayCount = 1 (zero-based ray index):
    // first-ray all-miss, or from the second ray onward all-hit so far.
    // Mixed waves keep tracing; unavailable is never a valid miss.
    return waveComplete && unanimous;
}

bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel, bool adaptive,
    VSMSMRTProjection projection, inout VSMSMRTReceiverSamples samples, out float shadow)
{
    shadow = 1;
    bool footprintValid = HasVSMSMRTFootprint(coord.xy, index, projection);
#if defined(VIVID_VSM_ADAPTIVE_RAYS)
    bool waveComplete = false;
    [branch]
    if (adaptive) waveComplete = WaveActiveAllTrue(footprintValid);
#endif
    if (!footprintValid)
    {
        VSM_COST_ADD(7, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugSMRT.z++;
#endif
        return false;
    }
    float depthScale = projection.depthScale;
#if !UNITY_REVERSED_Z
    depthScale = -depthScale;
#endif
    // Apply the receiver bias once. PCF's receiver-plane gradient is not the
    // depth trajectory of a shadow ray leaving that surface.
    coord.z += bias.z;
    float slope = _VSMSMRTParameters.w / projection.texelSize;
    int maximum = clamp((int)_VSMSMRTParameters.x, 4, 8);
    int steps = clamp((int)_VSMSMRTParameters.y, 4, 8);
    PrepareVSMSMRTReceiverSamples(pixel, samples);
    float2 originalTexel = coord.xy * _VSMPrototypeVirtualResolution;
    float sum = 0;
    [loop]
    for (int ray = 0; ray < maximum; ray++)
    {
        VSM_COST_ADD(8, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugSMRT.x++;
#endif
        float2 disk = VSMSMRTDiskSample(samples.diskPhase, (uint)ray);
        float3 origin = coord;
        if (_VSMReceiverParameters.x >= 0.5)
        {
            float2 jitter = VSMSMRTReceiverOffset(samples.receiverPhase, (uint)ray);
            float2 sampleTexel = floor(originalTexel + jitter) + 0.5;
            origin.xy = sampleTexel / _VSMPrototypeVirtualResolution;
            // Reconstruct the new origin on the receiver plane, once. The ray
            // thereafter follows its light direction, not this depth gradient.
            origin.z += dot(bias.xy, sampleTexel - originalTexel);
        }
        float visibility;
        bool valid = TryTraceVSMSMRTClipmaps(origin, disk * slope, depthScale, steps, index, projection, visibility);
        VSM_COST_ADD(13, valid ? 0u : 1u);
        sum += visibility;
#if defined(VIVID_VSM_ADAPTIVE_RAYS)
        [branch]
        if (adaptive)
        {
            if (VSMWaveCanFinish(ray, sum, valid, waveComplete))
            {
                shadow = sum / (ray + 1);
                return true;
            }
        }
#endif
        if (!valid) return false;
    }
    shadow = sum / maximum;
    return true;
}

bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel,
    VSMSMRTProjection projection, inout VSMSMRTReceiverSamples samples, out float shadow)
{
    // Production selects a specialized kernel. Keep wave operations out of the
    // fixed-budget binary; runtime-disabled votes still increase its GPU cost.
#if defined(VIVID_VSM_ADAPTIVE_RAYS)
#if defined(VIVID_VSM_SMRT_COST) || defined(VIVID_VSM_RECEIVER_DEBUG)
    bool adaptive = _VSMHistoryParameters.y > 0; // Diagnostic replay of either mode.
#else
    const bool adaptive = true;
#endif
#else
    const bool adaptive = false;
#endif
    return TryFilterVSMSMRT(coord, bias, index, pixel, adaptive, projection, samples, shadow);
}


// Preserve the standalone filter contract used by sampling diagnostics.
bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel, bool adaptive, out float shadow)
{
    VSMSMRTReceiverSamples samples = (VSMSMRTReceiverSamples)0;
    return TryFilterVSMSMRT(coord, bias, index, pixel, adaptive,
        PrepareVSMSMRTProjection(_VSMProjections[index]), samples, shadow);
}

bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel, out float shadow)
{
    VSMSMRTReceiverSamples samples = (VSMSMRTReceiverSamples)0;
    return TryFilterVSMSMRT(coord, bias, index, pixel,
        PrepareVSMSMRTProjection(_VSMProjections[index]), samples, shadow);
}
