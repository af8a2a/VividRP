// Directional SMRT. Production uses the supplied Unreal fixed-step/depth-history
// trace; the layered DDA below remains only as a direct diagnostic reference.
// Production uses one normalized world ray, no per-level restart or parallel
// tail. The caller retains Vivid receiver bias, noise and world-length controls.

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

// The projection buffer is immutable for this dispatch. Prepare compact data
// once per 8x8 receiver group, rather than once per pixel/ray/clipmap segment.
// Match the current CPU MaxLevels; larger diagnostic sets use the direct path.
#if defined(VIVID_VSM_GROUP_PROJECTION_CACHE)
#define VIVID_VSM_SMRT_CACHED_PROJECTIONS 16
groupshared VSMSMRTProjection g_VSMSMRTProjections[VIVID_VSM_SMRT_CACHED_PROJECTIONS];
#endif

void InitializeVSMSMRTProjections(uint groupIndex)
{
#if defined(VIVID_VSM_GROUP_PROJECTION_CACHE)
    // Uniform dispatch parameters: every lane takes the same barrier path.
    // Call before pixel bounds, sky, or receiver-dependent early returns.
    if (_VSMPrototypeEnabled == 0 || !UseVSMSMRT()) return;
    if (groupIndex < min((uint)_VSMProjectionCount, (uint)VIVID_VSM_SMRT_CACHED_PROJECTIONS))
        g_VSMSMRTProjections[groupIndex] = PrepareVSMSMRTProjection(_VSMProjections[groupIndex]);
    GroupMemoryBarrierWithGroupSync();
#endif
}

VSMSMRTProjection GetVSMSMRTProjection(int index)
{
#if defined(VIVID_VSM_GROUP_PROJECTION_CACHE)
    if ((uint)index < VIVID_VSM_SMRT_CACHED_PROJECTIONS)
        return g_VSMSMRTProjections[index];
#endif
    return PrepareVSMSMRTProjection(_VSMProjections[index]);
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
        if (level != index) destination = GetVSMSMRTProjection(level);
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
    float stepOffset;
    float viewDistance;
    float rayStartOffset;
    bool ready;
};

Texture2D<float> _VSMSTBNScalar;
Texture2D<float2> _VSMSTBNVec2;

uint2 VSMSMRTNoiseAddress(uint2 pixel, uint frame)
{
    uint3 wrapped = uint3(pixel, frame) & uint3(127, 127, 63);
    // Unity imports PNG rows bottom-up; undo that to preserve UE atlas addresses.
    return uint2(wrapped.x, 8191u - (wrapped.z * 128 + wrapped.y));
}

float4 VSMSMRTRandomSample(uint2 pixel, uint frame, uint ray, uint maximum)
{
    // UE VirtualShadowMapGetRandomSample: R2 offsets index the STBN atlas.
    uint2 first = (uint2)(frac(float(ray) * float2(0.754877669, 0.569840296)) * 128);
    uint2 second = (uint2)(frac(float(ray + maximum) * float2(0.754877669, 0.569840296)) * 128);
    return float4(_VSMSTBNVec2.Load(int3(VSMSMRTNoiseAddress(pixel + first, frame), 0)),
        _VSMSTBNVec2.Load(int3(VSMSMRTNoiseAddress(pixel + second, frame), 0)));
}

float2 VSMSMRTConcentricDisk(float2 samplePoint)
{
    float2 p = 2 * samplePoint - 0.99999994;
    float2 a = abs(p);
    float hi = max(a.x, a.y), lo = min(a.x, a.y);
    float phi = (kPCSSTwoPi / 8) * (lo / (hi + 5.42101086243e-20) + 2 * float(a.y >= a.x));
    float2 disk = float2(cos(phi), sin(phi));
    return asfloat((asuint(disk) & ~0x80000000u) | (asuint(p) & 0x80000000u)) * hi;
}

uint2 VSMMortonPixel(uint2 dispatchPixel)
{
    uint lane = (dispatchPixel.x & 7u) + ((dispatchPixel.y & 7u) << 3u);
    uint2 xy = uint2((lane & 1u) | ((lane >> 1u) & 2u) | ((lane >> 2u) & 4u),
        ((lane >> 1u) & 1u) | ((lane >> 2u) & 2u) | ((lane >> 3u) & 4u));
    return (dispatchPixel & ~7u) + xy;
}

void PrepareVSMSMRTReceiverSamples(uint2 pixel, inout VSMSMRTReceiverSamples samples)
{
    if (samples.ready) return;
    samples.stepOffset = _VSMSTBNScalar.Load(int3(VSMSMRTNoiseAddress(pixel, (uint)_CSMFrameIndex), 0));
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

#if defined(VIVID_VSM_LEGACY_DEPTH_TESTS)
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

bool TryTraceVSMSMRTClipmapsLegacy(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, VSMSMRTProjection projection, out float visibility)
{
    visibility = 1;
    float startTime = 0;
    for (int level = index; level < _VSMProjectionCount; level++)
    {
        VSMSMRTProjection destination = projection;
        if (level != index) destination = GetVSMSMRTProjection(level);
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
bool TryTraceVSMSMRTClipmapsLegacy(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, out float visibility)
{
    return TryTraceVSMSMRTClipmapsLegacy(origin, texelsPerWorld, depthPerWorld, budget, index,
        GetVSMSMRTProjection(index), visibility);
}

#endif

bool VSMWaveCanFinish(int rayIndex, float visibilitySum, bool rayValid, inout bool waveComplete)
{
    // Latch failure before an unavailable lane leaves the loop. Remaining
    // lanes must not mistake its absence for unanimous visibility.
    waveComplete = WaveActiveAllTrue(waveComplete && rayValid);
    bool unanimous = WaveActiveAllTrue(rayIndex == 0 ? visibilitySum == 1 : visibilitySum == 0);
    // UE directional rule with AdaptiveRayCount = 1 (zero-based ray index):
    // first-ray all-miss, or from the second ray onward all-hit so far.
    // Mixed waves keep tracing; unavailable is never a valid miss.
    return waveComplete && unanimous && (rayIndex == 0 || rayIndex >= max(1, (int)_VSMSMRTSettings.z));
}

// UE trace sample contract. Depth is always in the starting clipmap's space.
struct VSMSMRTSample
{
    bool bValid;
    float SampleDepth;
    float ReferenceDepth;
    float ExtrapolateSlope;
    bool bResetExtrapolation;
};

struct VSMSMRTResult
{
    bool bValidHit;
    float HitDepth;
};

struct VSMSMRTClipmapRayState
{
    int index;
    VSMSMRTProjection projection;
    float3 RayStartUVZ;
    float3 RayStepUVZ;
    float ExtrapolateSlope;
};

// UE SampleVirtualShadowMapClipmap: one source PT lookup, at most one
// native destination PT lookup. No per-ray search through resident metadata.
bool ResolveVSMSMRTMappedTexel(float2 uv, int index, out int mappedIndex,
    out int2 physical, out float2 mappedUV)
{
    mappedIndex = index; physical = 0; mappedUV = uv;
    if (!UseVirtualShadowMapPrototype(index) || !all(isfinite(uv)) || any(uv < 0) || any(uv >= 1)) return false;
    uint axis = (uint)_VSMPrototypePagesPerAxis;
    uint2 basePage = min((uint2)(uv * axis), axis - 1u);
    uint entry = _VSMSamplingPageTable[((uint)index * axis + basePage.y) * axis + basePage.x];
    VSM_COST_ADD(11, 1u);
    if ((entry & 0x80000000u) == 0u) return false;
    uint offset = (entry >> 20u) & 63u;
    mappedIndex = index + (int)offset;
    if (mappedIndex >= _VSMProjectionCount) return false;
    int2 texel = (int2)(uv * _VSMPrototypeVirtualResolution);
    if (offset > 0u)
    {
        int2 delta = _VSMClipmapPageOffsets[index * _VSMProjectionCount + mappedIndex];
        int2 page = ((int2)basePage + delta) >> offset;
        if (any(page < 0) || any(page >= (int)axis)) return false;
        float scale = rcp(float(1u << offset));
        mappedUV = uv * scale + float2(delta) * (scale / axis);
        int2 low = page * _VSMPrototypePageSize;
        texel = clamp((int2)(mappedUV * _VSMPrototypeVirtualResolution), low, low + _VSMPrototypePageSize - 1);
        entry = _VSMSamplingPageTable[(mappedIndex * axis + (uint)page.y) * axis + (uint)page.x];
        VSM_COST_ADD(11, 1u);
        // Only THIS LOD is valid here; never follow a second alias.
        if ((entry & 0x83f00000u) != 0x80000000u) return false;
    }
    uint2 physicalPage = uint2(entry & 1023u, (entry >> 10u) & 1023u);
    physical = (int2)(physicalPage * (uint)_VSMPrototypePageSize) + texel % _VSMPrototypePageSize;
    return true;
}

bool FindVSMSMRTMappedClipmap(float2 uv, int index, VSMSMRTProjection projection,
    out int mappedIndex, out VSMSMRTProjection mappedProjection)
{
    int2 physical; float2 mappedUV;
    bool valid = ResolveVSMSMRTMappedTexel(uv, index, mappedIndex, physical, mappedUV);
    if (!valid) mappedIndex = index; // UE keeps requested level when the origin is unmapped.
    mappedProjection = projection;
    if (mappedIndex != index) mappedProjection = GetVSMSMRTProjection(mappedIndex);
    return valid;
}

VSMSMRTSample VSMSMRTFindSample(inout VSMSMRTClipmapRayState state, float sampleTime)
{
    VSMSMRTSample sample = (VSMSMRTSample)0;
    float3 uvz = state.RayStartUVZ + state.RayStepUVZ * sampleTime;
    sample.ReferenceDepth = uvz.z;
    sample.ExtrapolateSlope = state.ExtrapolateSlope;
    VSM_COST_ADD(10, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
    g_VSMDebugWork.x++;
#endif
    int level; int2 physical; float2 mappedUV;
    sample.bValid = ResolveVSMSMRTMappedTexel(uvz.xy, state.index, level, physical, mappedUV);
    if (sample.bValid)
    {
        VSMSMRTProjection destination = state.projection;
        if (level != state.index) destination = GetVSMSMRTProjection(level);
        // Vivid pins depth ranges independently of XY clipmap size. Preserve
        // its exact depth transform rather than assuming UE's power-of-two Z.
        float depthScale = destination.depthScale / state.projection.depthScale;
        VSM_COST_ADD(17, 1u);
        float depth = asfloat(_VSMPhysicalPagePool.Load(int4(physical, VIVID_VSM_FINAL_DEPTH_SLICE, 0)));
        sample.SampleDepth = (depth - destination.translation.z) / depthScale + state.projection.translation.z;
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugWork.y++;
        g_VSMDebugLevels.y = level;
#endif
    }
    VSM_COST_ADD(28, sample.bValid ? 0u : 1u);
    return sample;
}

// UE selects the slope permutation when ExtrapolateMaxSlope > 0.
#ifndef VIVID_SMRT_EXTRAPOLATE_SLOPE
#define VIVID_SMRT_EXTRAPOLATE_SLOPE 1
#endif
#define VIVID_SMRT_TEMPLATE_RAY_STRUCT VSMSMRTClipmapRayState
#include "VSMSMRTTraceTemplate.hlsl"
#undef VIVID_SMRT_TEMPLATE_RAY_STRUCT

bool TraceVSMSMRTClipmapsWorldLength(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, VSMSMRTProjection projection, float stepOffset, float worldLength, out float visibility)
{
    VSMSMRTClipmapRayState state;
    state.index = index;
    state.projection = projection;
    state.RayStartUVZ = origin;
    // The caller supplies lateral slopes relative to the central light axis.
    // UE traces a normalized light ray; z and xy must share the same length.
    float2 lateral = texelsPerWorld * projection.texelSize;
    float rayLength = worldLength * rsqrt(1.0 + dot(lateral, lateral));
    state.RayStepUVZ = float3(texelsPerWorld / _VSMPrototypeVirtualResolution, depthPerWorld) * rayLength;
    state.ExtrapolateSlope = abs(_VSMSMRTSettings.x * projection.depthScale);
    VSM_COST_ADD(9, 1u);
    VSMSMRTResult result = VSMSMRTRayCast(state, budget, stepOffset);
    visibility = result.bValidHit ? 0.0 : 1.0;
    VSM_COST_ADD(15, result.bValidHit ? 1u : 0u);
    // UE skips invalid samples, and reports an all-invalid ray as a miss.
    // This intentionally replaces the former complete-footprint/PCF policy.
    return true;
}

bool TryTraceVSMSMRTClipmaps(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, VSMSMRTProjection projection, float stepOffset, out float visibility)
{
    return TraceVSMSMRTClipmapsWorldLength(origin, texelsPerWorld, depthPerWorld, budget,
        index, projection, stepOffset, _VSMSMRTParameters.z, visibility);
}

bool TryTraceVSMSMRTClipmaps(float3 origin, float2 texelsPerWorld, float depthPerWorld,
    int budget, int index, out float visibility)
{
    return TryTraceVSMSMRTClipmaps(origin, texelsPerWorld, depthPerWorld, budget, index,
        GetVSMSMRTProjection(index), 0.5, visibility);
}

float VSMSMRTTexelDitherScale(float distance, float texelSize)
{
    return (0.5 * _VSMSMRTSettings.y) * distance * exp2(_VSMReceiverQuality.y)
        / (_VSMPrototypeVirtualResolution * (texelSize * _VSMPrototypeVirtualResolution / 8.0));
}

float3 InitializeUEVSMRayOrigin(float3 coord, float3 directionUVZ, float rayStartOffset,
    float depthScale, float2 depthSlopeUV, float2 texelOffset)
{
    coord += directionUVZ * rayStartOffset;
    coord.xy += texelOffset;
    coord.z += max(0.0, 2.0 * max(0.0, dot(clamp(depthSlopeUV, -0.05, 0.05), texelOffset))
        - abs(rayStartOffset * depthScale));
    return coord;
}

bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel, bool adaptive,
    VSMSMRTProjection projection, inout VSMSMRTReceiverSamples samples, out float shadow)
{
    shadow = 1;
    int mappedIndex;
    VSMSMRTProjection mappedProjection;
    // Match GetMappedClipmap: choose the starting mapped level at the receiver,
    // not by requiring the union of every ray's complete footprint to be ready.
    // UE keeps the requested starting level when the origin itself is unmapped;
    // later ray points may still find valid pages. Only a mapped parent promotes it.
    FindVSMSMRTMappedClipmap(coord.xy, index, projection, mappedIndex, mappedProjection);
    coord.z += bias.z;
    float3 scale = VSMSMRTProjectionScale(projection, mappedProjection);
    float2 mappedUV = coord.xy;
    if (mappedIndex != index)
    {
        int2 delta = _VSMClipmapPageOffsets[index * _VSMProjectionCount + mappedIndex];
        mappedUV = coord.xy * scale.xy + float2(delta) * (scale.xy / _VSMPrototypePagesPerAxis);
    }
    coord = float3(mappedUV, (coord.z - projection.translation.z) * scale.z + mappedProjection.translation.z);
    bias.xy *= scale.z / scale.x;
    index = mappedIndex;
    projection = mappedProjection;
    float depthScale = projection.depthScale;
#if !UNITY_REVERSED_Z
    depthScale = -depthScale;
#endif
    // UE default TexelDitherScaleDirectional=2. Half-width=2^(Level+2),
    // so 2^Level = texelSize * resolution / 8. The world-space dither is
    // continuous across both selected-level and mapped-parent boundaries.
    float ditherScale = VSMSMRTTexelDitherScale(samples.viewDistance, projection.texelSize);
    VividVSMProjection fullProjection = _VSMProjections[index];
    float3 axisX = normalize(fullProjection.worldToShadow[0].xyz);
    float3 axisY = normalize(fullProjection.worldToShadow[1].xyz);
    float3 lightDirection = normalize(fullProjection.worldToShadow[2].xyz);
#if !UNITY_REVERSED_Z
    lightDirection = -lightDirection;
#endif
    // UE GetRandomDirectionalLightRayDir deliberately does not normalize dPdu.
    float3 dPdu = cross(lightDirection, abs(lightDirection.x) > 1e-6 ? float3(1, 0, 0) : float3(0, 1, 0));
    float3 dPdv = cross(dPdu, lightDirection);
    float4 diskBasis = float4(dot(axisX, dPdu), dot(axisX, dPdv), dot(axisY, dPdu), dot(axisY, dPdv));
    float slope = _VSMSMRTParameters.w / projection.texelSize;
    int maximum = clamp((int)_VSMSMRTParameters.x, 1, 16);
    int steps = clamp((int)_VSMSMRTParameters.y, 1, 32);
    PrepareVSMSMRTReceiverSamples(pixel, samples);
    float sum = 0;
#if defined(VIVID_VSM_ADAPTIVE_RAYS)
    bool waveComplete = true;
#endif
    [loop]
    for (int ray = 0; ray < maximum; ray++)
    {
        VSM_COST_ADD(8, 1u);
#if defined(VIVID_VSM_RECEIVER_DEBUG)
        g_VSMDebugSMRT.x++;
#endif
        float4 randomSample = VSMSMRTRandomSample(pixel, (uint)_CSMFrameIndex, (uint)ray, (uint)maximum);
        float2 disk = VSMSMRTConcentricDisk(randomSample.xy);
        disk = float2(dot(diskBasis.xy, disk), dot(diskBasis.zw, disk));
        float3 origin = coord;
        {
            float2 lateral = disk * _VSMSMRTParameters.w;
            float3 rayDirectionUVZ = float3(disk * slope / _VSMPrototypeVirtualResolution, depthScale)
                * rsqrt(1.0 + dot(lateral, lateral));
            float2 offsetUV = (randomSample.zw - 0.5) * ditherScale;
            // Do not apply the part of slope bias already covered by screen ray.
            origin = InitializeUEVSMRayOrigin(coord, rayDirectionUVZ, samples.rayStartOffset,
                depthScale, bias.xy * _VSMPrototypeVirtualResolution, offsetUV);
        }
        float visibility;
        bool valid = TraceVSMSMRTClipmapsWorldLength(origin, disk * slope, depthScale, steps,
            index, projection, samples.stepOffset, _VSMSMRTParameters.z * samples.viewDistance, visibility);
        sum += visibility;
#if defined(VIVID_VSM_ADAPTIVE_RAYS)
        [branch]
        if (adaptive && VSMWaveCanFinish(ray, sum, valid, waveComplete))
        {
            shadow = sum / (ray + 1);
            return true;
        }
#endif
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
    samples.viewDistance = 1; // Standalone diagnostics interpret length scale at unit view distance.
    return TryFilterVSMSMRT(coord, bias, index, pixel, adaptive,
        GetVSMSMRTProjection(index), samples, shadow);
}

bool TryFilterVSMSMRT(float3 coord, float4 bias, int index, uint2 pixel, out float shadow)
{
    VSMSMRTReceiverSamples samples = (VSMSMRTReceiverSamples)0;
    samples.viewDistance = 1; // Standalone diagnostics interpret length scale at unit view distance.
    return TryFilterVSMSMRT(coord, bias, index, pixel,
        GetVSMSMRTProjection(index), samples, shadow);
}
