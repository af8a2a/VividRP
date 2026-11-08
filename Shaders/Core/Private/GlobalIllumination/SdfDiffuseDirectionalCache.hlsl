#ifndef VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED
#define VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED

// Four azimuth sectors and two radial bands have equal mass under the
// cosine hemisphere PDF. Bins are local to the probe's geometric frame.
static const uint DirectionalBinCount = 8u;
static const uint DirectionalHistoryFrames = 3u;

uint _SdfDirectionalCacheEnabled;
uint _SdfDirectionalGuideEnabled;
uint _SdfDirectionalHasHistory;
Texture2D<float4> _SdfDirectionalPrevious;
Texture2D<float4> _SdfDirectionalGuide;
Texture2D<float4> _SdfDirectionalRays;
RWTexture2D<float4> _SdfDirectionalGuideOutput;
RWTexture2D<float4> _SdfDirectionalCurrent;

float3x3 ProbeSampleMoments(float3 radiance, float3 direction, float3 normal);

uint2 DirectionalGrid()
{
    return ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy));
}

uint2 DirectionalAddress(uint2 tile, uint bin)
{
    return uint2(tile.x * DirectionalBinCount + bin, tile.y);
}

uint2 DirectionalMetadataOffset(uint2 grid)
{
    return uint2(0u, grid.y * (1u + _SdfDiffuseAdaptiveProbes));
}

float3 DirectionalBinDirection(uint bin, float2 sampleUv, float3x3 basis)
{
    float u = (float(bin >> 2u) + sampleUv.x) * 0.5;
    float phi = (float(bin & 3u) + sampleUv.y) * (0.25 * TWO_PI);
    float s, c;
    sincos(phi, s, c);
    return mul(float3(sqrt(u) * c, sqrt(u) * s, sqrt(1.0 - u)), basis);
}

uint DirectionalBin(float3 direction, float3x3 basis)
{
    float3 local = mul(basis, direction);
    uint sector = local.y >= 0.0 ? (local.x >= 0.0 ? 0u : 1u) : (local.x < 0.0 ? 2u : 3u);
    return sector + (local.z * local.z < 0.5 ? 4u : 0u);
}

float DirectionalRandom(uint value)
{
    return float(HashProbe(value) >> 8u) * (1.0 / 16777216.0);
}

float3x3 DiffuseProbeFrame(float3 normal)
{
    // The guide, tracing, and cache must use the same frame at basis seams.
    return GetLocalFrame(_SdfDirectionalCacheEnabled != 0u ? UnpackProbeNormal(PackProbeNormal(normal)) : normal);
}

float3 SampleDiffuseProbeRay(uint2 tile, uint ray, uint seed, float3x3 basis, out uint bin, out float inverseRelativePdf)
{
    bin = ray;
    inverseRelativePdf = 1.0;
    if (_SdfDirectionalCacheEnabled == 0u)
        return ProbeRayDirection(ray, seed, basis);

    if (_SdfDirectionalGuideEnabled != 0u)
    {
        // Stratify the mixture CDF. Both screen and SDF traces call this same sampler.
        float u = (float(ray) + DirectionalRandom(seed ^ 0x47a6d32bu)) / float(DirectionalBinCount);
        float low = 0.0;
        [loop]
        for (uint candidate = 0u; candidate < DirectionalBinCount; ++candidate)
        {
            float high = _SdfDirectionalGuide.Load(int3(DirectionalAddress(tile, candidate), 0)).x;
            if (u < high || candidate == DirectionalBinCount - 1u)
            {
                bin = candidate;
                inverseRelativePdf = rcp(float(DirectionalBinCount) * max(high - low, 1e-5));
                break;
            }
            low = high;
        }
    }

    uint sampleSeed = seed ^ (0xb5297a4du * (ray + 1u));
    float2 sampleUv = float2(DirectionalRandom(sampleSeed), DirectionalRandom(sampleSeed ^ 0x68e31da4u));
    return DirectionalBinDirection(bin, clamp(sampleUv, 1e-5, 1.0 - 1e-5), basis);
}

void StoreDirectionalRay(uint2 tile, uint ray, uint bin, float inverseRelativePdf, uint state,
    float distance, float3 radiance, float3 direction)
{
    uint2 address = DirectionalAddress(tile, ray);
    uint source = state & 7u;
    float hit = 0.0;
    if (source == RayScreenHit || source == RaySdfHit || (state & RayFarCache) != 0u)
        hit = clamp(distance, 1e-3, 65000.0);
    else if (source == RaySkyVisible)
        hit = -1.0;
    // This path writes traces where the legacy gather writes its integrated probe.
    _SdfDiffuseProbes[address] = float4(clamp(radiance, 0.0, 65000.0), hit);
    _SdfDiffuseProbes[address + DirectionalMetadataOffset(DirectionalGrid())] =
        float4(bin, inverseRelativePdf, VividEncodeSurfaceSummaryNormal(direction));
}

bool DirectionalHistoryCandidate(uint2 candidate, float3 position, float3 normal,
    inout float bestDistance, inout uint2 bestTile)
{
    float4 geometry = _SdfDiffusePreviousGeometry.Load(int3(candidate, 0));
    if (geometry.w < 0.0 || dot(normal, UnpackProbeNormal(geometry.w)) < 0.95)
        return false;
    float3 delta = geometry.xyz - position;
    float distance = length(delta);
    if (abs(dot(delta, normal)) > 0.035 || distance > bestDistance)
        return false;
    bestDistance = distance;
    bestTile = candidate;
    return true;
}

[numthreads(8, 8, 1)]
void PrepareSdfDirectionalGuide(uint3 id : SV_DispatchThreadID)
{
    uint2 grid = DirectionalGrid();
    if (any(id.xy >= grid * uint2(1u, 1u + _SdfDiffuseAdaptiveProbes)))
        return;

    float3 position, normal;
    uint2 previousTile = 0u;
    bool hasPrevious = false;
    float3x3 basis = (float3x3)0.0;
    float3x3 previousBasis = (float3x3)0.0;
    if (_SdfDirectionalHasHistory != 0u && LoadProbeReceiver(id.xy, uint2(_SdfDiffuseScreenSize.xy), position, normal))
    {
        float4 clip = mul(_SdfDiffusePreviousVP, float4(position, 1.0));
        float2 uv = ScreenUv(clip);
        if (clip.w > 0.0 && all(uv >= 0.0) && all(uv < 1.0))
        {
            float bestDistance = clamp(0.015 * clip.w, 0.08, 0.35);
            int2 lower = int2(floor(uv * _SdfDiffuseScreenSize.xy / ProbeSpacing - 0.5));
            [unroll]
            for (uint tap = 0u; tap < 4u; ++tap)
            {
                int2 tile = lower + int2(tap & 1u, tap >> 1u);
                if (any(tile < 0) || any(tile >= int2(grid)))
                    continue;
                if (DirectionalHistoryCandidate(uint2(tile), position, normal, bestDistance, previousTile))
                    hasPrevious = true;
                if (_SdfDiffuseAdaptiveProbes != 0u)
                {
                    if (DirectionalHistoryCandidate(uint2(tile) + uint2(0u, grid.y),
                        position, normal, bestDistance, previousTile))
                        hasPrevious = true;
                }
            }
            if (hasPrevious)
            {
                basis = DiffuseProbeFrame(normal);
                previousBasis = GetLocalFrame(UnpackProbeNormal(_SdfDiffusePreviousGeometry.Load(int3(previousTile, 0)).w));
            }
        }
    }

    float weights[DirectionalBinCount];
    float total = 0.0;
    [unroll]
    for (uint bin = 0u; bin < DirectionalBinCount; ++bin)
    {
        float weight = 0.0;
        if (hasPrevious && _SdfDirectionalGuideEnabled != 0u)
        {
            uint oldBin = DirectionalBin(DirectionalBinDirection(bin, 0.5, basis), previousBasis);
            float4 previous = _SdfDirectionalPrevious.Load(int3(DirectionalAddress(previousTile, oldBin), 0));
            weight = sqrt(max(0.0, dot(previous.rgb, float3(0.2126, 0.7152, 0.0722)) * previous.a));
        }
        weights[bin] = weight;
        total += weight;
    }
    float cdf = 0.0;
    [unroll]
    for (uint bin = 0u; bin < DirectionalBinCount; ++bin)
    {
        // Reserve half the proposal mass for cosine exploration.
        cdf += total > 0.0 ? 0.5 / DirectionalBinCount + 0.5 * weights[bin] / total : rcp(float(DirectionalBinCount));
        _SdfDirectionalGuideOutput[DirectionalAddress(id.xy, bin)] =
            float4(bin + 1u == DirectionalBinCount ? 1.0 : cdf, hasPrevious ? float2(previousTile + 1u) : 0.0, 0.0);
    }
}

float DirectionalHitWeight(float4 center, float4 other, float3 delta)
{
    if (center.x == 0.0 || other.x == 0.0 || (center.x < 0.0) != (other.x < 0.0))
        return 0.0;
    float3 direction = VividDecodeSurfaceSummaryNormal(center.zw);
    float3 otherDirection = VividDecodeSurfaceSummaryNormal(other.zw);
    if (center.x < 0.0)
        return saturate((dot(direction, otherDirection) - 0.85) / 0.15);

    float3 hit = delta + otherDirection * other.x;
    float distance = length(hit);
    float tolerance = clamp(0.18 * min(center.x, distance), 0.04, 0.4);
    return saturate(1.0 - abs(center.x - distance) / tolerance)
        * saturate((dot(direction, hit / max(distance, 1e-5)) - 0.92) / 0.08);
}

[numthreads(8, 8, 1)]
void AccumulateSdfDirectionalRadiance(uint3 id : SV_DispatchThreadID)
{
    uint2 grid = DirectionalGrid();
    if (id.x >= grid.x * DirectionalBinCount || id.y >= grid.y * (1u + _SdfDiffuseAdaptiveProbes))
        return;
    uint2 tile = uint2(id.x / DirectionalBinCount, id.y);
    uint bin = id.x % DirectionalBinCount;
    uint2 metadataOffset = DirectionalMetadataOffset(grid);
    float4 geometry = _SdfDiffuseGeometry[tile];
    float4 lighting = 0.0;
    float4 metadata = 0.0;
    if (geometry.w >= 0.0)
    {
        float samples = 0.0;
        float resolved = 0.0;
        float proposalWeight = 0.0;
        [unroll]
        for (uint ray = 0u; ray < DirectionalBinCount; ++ray)
        {
            uint2 address = DirectionalAddress(tile, ray);
            float4 info = _SdfDirectionalRays.Load(int3(address + metadataOffset, 0));
            if (uint(info.x) != bin)
                continue;
            float4 sample = _SdfDirectionalRays.Load(int3(address, 0));
            samples++;
            proposalWeight += info.y;
            if (sample.a == 0.0)
                continue;
            lighting.rgb += sample.rgb;
            resolved++;
            if (metadata.x == 0.0 || (sample.a > 0.0 && (metadata.x < 0.0 || sample.a < metadata.x)))
                metadata = float4(sample.a, 0.0, info.zw);
        }
        // Within a bin every draw has the same PDF, so its radiance is a conditional mean.
        // Metadata Y stores negative proposal mass for current draws and positive age for reused bins.
        lighting = float4(lighting.rgb / max(resolved, 1.0), resolved / max(samples, 1.0));
        metadata.y = -proposalWeight;
        float2 previous = _SdfDirectionalGuide.Load(int3(id.xy, 0)).yz;
        if (all(previous > 0.0))
        {
            uint2 previousTile = uint2(previous) - 1u;
            float4 previousGeometry = _SdfDiffusePreviousGeometry.Load(int3(previousTile, 0));
            float3x3 basis = GetLocalFrame(UnpackProbeNormal(geometry.w));
            float3x3 previousBasis = GetLocalFrame(UnpackProbeNormal(previousGeometry.w));
            uint oldBin = DirectionalBin(DirectionalBinDirection(bin, 0.5, basis), previousBasis);
            uint2 address = DirectionalAddress(previousTile, oldBin);
            float4 oldMetadata = _SdfDirectionalPrevious.Load(int3(address + metadataOffset, 0));
            float4 oldLighting = _SdfDirectionalPrevious.Load(int3(address, 0));
            float3 delta = previousGeometry.xyz - geometry.xyz;
            if (samples == 0.0 && oldLighting.a > 0.0 && oldMetadata.y < DirectionalHistoryFrames
                && length(delta) < clamp(0.12 * abs(oldMetadata.x), 0.015, 0.15))
            {
                float3 direction = VividDecodeSurfaceSummaryNormal(oldMetadata.zw);
                float3 hit = delta + oldMetadata.x * direction;
                float distance = length(hit);
                if (oldMetadata.x > 0.0)
                    direction = hit / max(distance, 1e-5);
                if (dot(direction, basis[2]) > 0.0 && DirectionalBin(direction, basis) == bin)
                {
                    lighting = oldLighting;
                    metadata = float4(oldMetadata.x > 0.0 ? distance : -1.0, max(0.0, oldMetadata.y) + 1.0,
                        VividEncodeSurfaceSummaryNormal(direction));
                }
            }
            else if (lighting.a > 0.0 && oldLighting.a > 0.0)
            {
                float weight = 0.65 * oldLighting.a * DirectionalHitWeight(metadata, oldMetadata, delta);
                lighting.rgb = lerp(lighting.rgb, oldLighting.rgb, weight);
            }
        }
    }
    _SdfDirectionalCurrent[id.xy] = lighting;
    _SdfDirectionalCurrent[id.xy + metadataOffset] = metadata;
}

float DirectionalNeighborWeight(uint2 tile, float4 center, float3 normal, float eyeDepth)
{
    float4 other = _SdfDiffuseGeometry[tile];
    return other.w < 0.0 ? 0.0 : ProbeGeometryWeight(other.xyz - center.xyz, normal, UnpackProbeNormal(other.w), eyeDepth);
}

static const int2 DirectionalNeighborOffsets[4] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1) };

[numthreads(8, 8, 1)]
void IntegrateSdfDirectionalRadiance(uint3 id : SV_DispatchThreadID)
{
    uint2 grid = DirectionalGrid();
    if (any(id.xy >= grid * uint2(1u, 1u + _SdfDiffuseAdaptiveProbes)))
        return;
    float4 center = _SdfDiffuseGeometry[id.xy];
    float4 integrated = 0.0;
    float proposalWeight = 0.0;
    float3x3 moments = (float3x3)0.0;
    if (center.w >= 0.0)
    {
        float3 normal = UnpackProbeNormal(center.w);
        float3x3 basis = GetLocalFrame(normal);
        uint2 metadataOffset = DirectionalMetadataOffset(grid);
        uint2 neighbors[4];
        float neighborWeights[4];
        [unroll]
        for (uint tap = 0u; tap < 4u; ++tap)
        {
            int2 tile = int2(id.x, id.y % grid.y) + DirectionalNeighborOffsets[tap];
            neighbors[tap] = 0u;
            neighborWeights[tap] = 0.0;
            if (_SdfDiffuseSpatialFilter == 0u || any(tile < 0) || any(tile >= int2(grid)))
                continue;
            float eyeDepth = dot(float4(center.xyz, 1.0), _SdfProbeCameraForward);
            uint2 selected = uint2(tile);
            float weight = DirectionalNeighborWeight(selected, center, normal, eyeDepth);
            if (_SdfDiffuseAdaptiveProbes != 0u)
            {
                uint2 adaptive = selected + uint2(0u, grid.y);
                float alternate = DirectionalNeighborWeight(adaptive, center, normal, eyeDepth);
                if (alternate > weight)
                {
                    weight = alternate;
                    selected = adaptive;
                }
            }
            neighbors[tap] = selected;
            neighborWeights[tap] = 0.65 * weight;
        }

        [loop]
        for (uint bin = 0u; bin < DirectionalBinCount; ++bin)
        {
            uint2 address = DirectionalAddress(id.xy, bin);
            float4 metadata = _SdfDirectionalCurrent[address + metadataOffset];
            float drawWeight = max(0.0, -metadata.y);
            proposalWeight += drawWeight;
            float4 lighting = _SdfDirectionalCurrent[address];
            float coveredWeight = drawWeight * lighting.a;
            if (coveredWeight <= 0.0)
                continue;
            float3 direction = DirectionalBinDirection(bin, 0.5, basis);
            float3 sum = lighting.rgb;
            float total = 1.0;
            [unroll]
            for (uint tap = 0u; tap < 4u; ++tap)
            {
                if (neighborWeights[tap] <= 0.0)
                    continue;
                uint2 neighbor = neighbors[tap];
                float4 geometry = _SdfDiffuseGeometry[neighbor];
                float3x3 neighborBasis = GetLocalFrame(UnpackProbeNormal(geometry.w));
                uint2 other = DirectionalAddress(neighbor, DirectionalBin(direction, neighborBasis));
                float4 neighborLight = _SdfDirectionalCurrent[other];
                float4 neighborMetadata = _SdfDirectionalCurrent[other + metadataOffset];
                float weight = neighborWeights[tap] * neighborLight.a * DirectionalHitWeight(metadata, neighborMetadata, geometry.xyz - center.xyz);
                sum += weight * neighborLight.rgb;
                total += weight;
            }
            float3 covered = coveredWeight * sum / total;
            integrated += float4(covered, coveredWeight);
            moments += ProbeSampleMoments(covered, direction, normal);
        }
    }
    float inverseCovered = rcp(max(integrated.a, 1e-5));
    uint2 stride = uint2(grid.x, 0u);
    // Normalize by the realized proposal mass so a constant fully resolved field
    // retains full coverage even when the number of draws per bin varies.
    _SdfDiffuseFilteredProbes[id.xy] = float4(min(integrated.rgb * inverseCovered, 65000.0),
        integrated.a / max(proposalWeight, 1e-5));
    _SdfDiffuseFilteredProbes[id.xy + stride] = float4(clamp(moments[0] * inverseCovered, -65000.0, 65000.0), 0.0);
    _SdfDiffuseFilteredProbes[id.xy + 2u * stride] = float4(clamp(moments[1] * inverseCovered, -65000.0, 65000.0), 0.0);
    _SdfDiffuseFilteredProbes[id.xy + 3u * stride] = float4(clamp(moments[2] * inverseCovered, -65000.0, 65000.0), 0.0);
}

#endif
