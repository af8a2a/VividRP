#ifndef VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED
#define VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED

// Four azimuth sectors and two radial bands have equal mass under the
// cosine hemisphere PDF. Bins are local to the probe's geometric frame.
static const uint DirectionalBinCount = 8u;
static const uint DirectionalHistoryFrames = 3u;

uint _SdfDirectionalCacheEnabled;
uint _SdfDirectionalGuideEnabled;
uint _SdfDirectionalWorldGuide;
uint _SdfDirectionalHasHistory;
Texture2D<float4> _SdfDirectionalPrevious;
// YZ holds the previous tile plus one. In the original guide X is the bin,
// and W selects the full bin, an azimuth half (-1/-2), or a merged bin (+1).
// Hierarchical rays pack the octahedral rectangle in X and its guide source in W.
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

float3 DirectionalOctDirection(float2 uv, float3x3 basis)
{
    // The square quadrants unfold the four upper octahedron faces. The
    // concentric disk warp makes area in this chart equal cosine mass.
    float2 p = 2.0 * uv - 1.0;
    bool horizontal = abs(p.x) > abs(p.y);
    float r = horizontal ? p.x : p.y;
    float slope = r == 0.0 ? 0.0 : (horizontal ? p.y : p.x) / r;
    float phi = (TWO_PI / 8.0) * (horizontal ? slope : 2.0 - slope);
    float s, c;
    sincos(phi, s, c);
    return mul(float3(r * c, r * s, sqrt(saturate(1.0 - r * r))), basis);
}

static const uint DirectionalOctSize = 4u;
static const int DirectionalOctScreen = -3;
static const int DirectionalOctWorld = -4;

uint PackDirectionalOctRect(uint4 rect)
{
    return rect.x | (rect.y << 2u) | ((rect.z - 1u) << 4u) | ((rect.w - 1u) << 5u);
}

uint4 UnpackDirectionalOctRect(uint code)
{
    return uint4(code & 3u, (code >> 2u) & 3u, 1u << ((code >> 4u) & 1u), 1u << ((code >> 5u) & 1u));
}

float3 SampleDiffuseProbeRay(uint2 tile, uint ray, uint seed, float3x3 basis, out uint bin, out float inverseRelativePdf)
{
    bin = ray;
    inverseRelativePdf = 1.0;
    if (_SdfDirectionalCacheEnabled == 0u)
        return ProbeRayDirection(ray, seed, basis);

    uint sampleSeed = seed ^ (0xb5297a4du * (ray + 1u));
    float2 sampleUv = float2(DirectionalRandom(sampleSeed), DirectionalRandom(sampleSeed ^ 0x68e31da4u));
    if (_SdfDirectionalGuideEnabled != 0u)
    {
        // One draw per disjoint stratum. Its inverse relative PDF is eight
        // times its cosine mass. Screen and SDF tracing share these descriptors.
        float4 stratum = _SdfDirectionalGuide.Load(int3(DirectionalAddress(tile, ray), 0));
        bin = uint(stratum.x);
        int subdivision = int(stratum.w);
        if (subdivision <= DirectionalOctScreen)
        {
            uint4 rect = UnpackDirectionalOctRect(bin);
            float2 uv = (float2(rect.xy) + float2(rect.zw) * clamp(sampleUv, 1e-5, 1.0 - 1e-5)) / DirectionalOctSize;
            float3 direction = DirectionalOctDirection(uv, basis);
            bin = DirectionalBin(direction, basis);
            inverseRelativePdf = 0.5 * float(rect.z * rect.w);
            return direction;
        }
        if (subdivision < 0)
        {
            sampleUv.y = 0.5 * (sampleUv.y + float(-subdivision - 1));
            inverseRelativePdf = 0.5;
        }
        else if (subdivision > 0)
        {
            if (sampleUv.x >= 0.5)
                bin = uint(subdivision - 1);
            sampleUv.x = frac(2.0 * sampleUv.x);
            inverseRelativePdf = 2.0;
        }
    }
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

bool FindDirectionalWorldProbe(float3 position, float3 normal, out int3 cell)
{
    cell = 0;
    if (_SdfDirectionalWorldGuide == 0u)
        return false;

    int3 lower = int3(floor(position / FarCellSize - 0.5));
    float bestDistanceSq = FarCellSize * FarCellSize;
    bool valid = false;
    [loop]
    for (uint tap = 0u; tap < 8u; ++tap)
    {
        int3 candidate = lower + int3(tap & 1u, (tap >> 1u) & 1u, tap >> 2u);
        float3 delta = FarProbePosition(candidate) - position;
        float distanceSq = dot(delta, delta);
        if (distanceSq >= bestDistanceSq || dot(delta, normal) < -0.05 || !HasFarProbe(candidate))
            continue;
        bestDistanceSq = distanceSq;
        cell = candidate;
        valid = true;
    }
    return valid;
}

float DirectionalWorldImportance(int3 cell, float3 offset, float3 direction)
{
    uint bin = FarDirectionBin(direction);
    float4 cached = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, bin), 0));
    if (cached.a <= 0.0)
        return 0.0;

    // Use the cache only to steer the proposal. A nearby cell can see a
    // different surface, so downweight endpoints that do not reproject.
    float3 delta = offset + cached.a * FarProbeDirection(bin);
    float travel = length(delta);
    float agreement = dot(delta, direction) / max(travel, 1e-5);
    float confidence = travel > 0.1 ? smoothstep(0.8, 0.97, agreement) : 0.0;
    return confidence * max(0.0, dot(cached.rgb, float3(0.2126, 0.7152, 0.0722)));
}

float DirectionalOctRegionImportance(float importance[16], uint4 rect)
{
    uint i = rect.x + DirectionalOctSize * rect.y;
    float weight = importance[i];
    if (rect.z == 2u)
        weight += importance[i + 1u];
    if (rect.w == 2u)
    {
        weight += importance[i + DirectionalOctSize];
        if (rect.z == 2u)
            weight += importance[i + DirectionalOctSize + 1u];
    }
    return weight;
}

bool BuildDirectionalOctGuide(uint2 tile, float3 position, float3 normal, float3x3 basis,
    bool hasPrevious, uint2 previousTile, float3x3 previousBasis)
{
    int3 worldCell = 0;
    if (!hasPrevious && !FindDirectionalWorldProbe(position, normal, worldCell))
        return false;

    float3 offset = FarProbePosition(worldCell) - position;
    float importance[16];
    float total = 0.0;
    float peak = 0.0;
    [loop]
    for (uint i = 0u; i < 16u; ++i)
    {
        float2 uv = (float2(i & 3u, i >> 2u) + 0.5) / DirectionalOctSize;
        float3 direction = DirectionalOctDirection(uv, basis);
        float weight;
        if (hasPrevious)
        {
            uint oldBin = DirectionalBin(direction, previousBasis);
            float4 previous = _SdfDirectionalPrevious.Load(int3(DirectionalAddress(previousTile, oldBin), 0));
            weight = max(0.0, dot(previous.rgb, float3(0.2126, 0.7152, 0.0722)) * previous.a);
        }
        else
            weight = DirectionalWorldImportance(worldCell, offset, direction);
        importance[i] = weight;
        total += weight;
        peak = max(peak, weight);
    }
    float exploration = total / 16.0;
    // Broad distributions gain little from trading away cosine strata.
    // World history is sparse, so require an evident directional peak there too.
    if (peak <= (hasPrevious ? 3.0 : 2.0) * exploration)
        return false;

    // Start with four upper octahedron faces. Spend each additional ray
    // subdividing the leaf with the most BRDF times radiance mass. Keep a
    // uniform term in that priority and every leaf in the final partition.
    uint4 rects[DirectionalBinCount];
    float scores[DirectionalBinCount];
    [unroll]
    for (uint face = 0u; face < 4u; ++face)
    {
        uint4 rect = uint4(2u * (face & 1u), 2u * (face >> 1u), 2u, 2u);
        rects[face] = rect;
        scores[face] = DirectionalOctRegionImportance(importance, rect) + 4.0 * exploration;
    }
    [loop]
    for (uint ray = 4u; ray < DirectionalBinCount; ++ray)
    {
        uint best = 0u;
        float priority = -1.0;
        [loop]
        for (uint i = 0u; i < ray; ++i)
        {
            if (rects[i].z * rects[i].w > 1u && scores[i] > priority)
            {
                best = i;
                priority = scores[i];
            }
        }
        uint4 a = rects[best];
        // Split the long axis. For a square, choose the split that best
        // separates the directional importance of its two children.
        bool horizontal = a.z > a.w;
        if (a.z == a.w)
        {
            float x = DirectionalOctRegionImportance(importance, uint4(a.xy, 1u, 2u));
            float y = DirectionalOctRegionImportance(importance, uint4(a.xy, 2u, 1u));
            float center = 0.5 * priority - 2.0 * exploration;
            horizontal = abs(x - center) >= abs(y - center);
        }
        uint4 b = a;
        if (horizontal)
        {
            a.z = b.z = 1u;
            b.x++;
        }
        else
        {
            a.w = b.w = 1u;
            b.y++;
        }
        rects[best] = a;
        rects[ray] = b;
        scores[best] = DirectionalOctRegionImportance(importance, a) + float(a.z * a.w) * exploration;
        scores[ray] = priority - scores[best];
    }
    float2 history = hasPrevious ? float2(previousTile + 1u) : 0.0;
    int source = hasPrevious ? DirectionalOctScreen : DirectionalOctWorld;
    [unroll]
    for (uint ray = 0u; ray < DirectionalBinCount; ++ray)
        _SdfDirectionalGuideOutput[DirectionalAddress(tile, ray)] = float4(PackDirectionalOctRect(rects[ray]), history, source);
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
    bool hierarchical = _SdfDirectionalGuideEnabled == 2u;
    bool receiver = (_SdfDirectionalHasHistory != 0u || (hierarchical && _SdfDirectionalWorldGuide != 0u))
        && LoadProbeReceiver(id.xy, uint2(_SdfDiffuseScreenSize.xy), position, normal);
    if (receiver && _SdfDirectionalHasHistory != 0u)
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

    if (hierarchical && receiver)
    {
        if (!hasPrevious)
            basis = DiffuseProbeFrame(normal);
        if (BuildDirectionalOctGuide(id.xy, position, normal, basis, hasPrevious, previousTile, previousBasis))
            return;
    }

    uint brightest = DirectionalBinCount;
    uint merge = DirectionalBinCount;
    uint mergeNeighbor = DirectionalBinCount;
    if (hasPrevious && _SdfDirectionalGuideEnabled == 1u)
    {
        float importance[DirectionalBinCount];
        float total = 0.0;
        float peak = 0.0;
        [unroll]
        for (uint bin = 0u; bin < DirectionalBinCount; ++bin)
        {
            uint oldBin = DirectionalBin(DirectionalBinDirection(bin, 0.5, basis), previousBasis);
            float4 previous = _SdfDirectionalPrevious.Load(int3(DirectionalAddress(previousTile, oldBin), 0));
            // Diffuse BRDF times cosine has equal integral in each base cell.
            // Multiply that measure by reprojected, coverage-weighted luminance.
            float weight = max(0.0, dot(previous.rgb, float3(0.2126, 0.7152, 0.0722)) * previous.a);
            importance[bin] = weight;
            total += weight;
            if (weight > peak)
            {
                peak = weight;
                brightest = bin;
            }
        }

        // With eight rays, refine one bright cell by merging two adjacent
        // dim cells in the same radial band. The union retains full support.
        if (peak > 0.25 * total)
        {
            float cheapest = 0.5 * peak;
            [unroll]
            for (uint bin = 0u; bin < DirectionalBinCount; ++bin)
            {
                uint neighbor = (bin & 4u) | ((bin + 1u) & 3u);
                float pair = importance[bin] + importance[neighbor];
                if (pair < cheapest)
                {
                    cheapest = pair;
                    merge = bin;
                    mergeNeighbor = neighbor;
                }
            }
        }
    }

    float2 history = hasPrevious ? float2(previousTile + 1u) : 0.0;
    [unroll]
    for (uint ray = 0u; ray < DirectionalBinCount; ++ray)
    {
        uint bin = ray;
        int subdivision = 0;
        if (merge < DirectionalBinCount)
        {
            if (ray == brightest)
                subdivision = -1;
            else if (ray == mergeNeighbor)
            {
                bin = brightest;
                subdivision = -2;
            }
            else if (ray == merge)
                subdivision = int(mergeNeighbor + 1u);
        }
        _SdfDirectionalGuideOutput[DirectionalAddress(id.xy, ray)] = float4(bin, history, subdivision);
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
        float drawWeight = 0.0;
        float coveredWeight = 0.0;
        [unroll]
        for (uint ray = 0u; ray < DirectionalBinCount; ++ray)
        {
            uint2 address = DirectionalAddress(tile, ray);
            float4 info = _SdfDirectionalRays.Load(int3(address + metadataOffset, 0));
            if (uint(info.x) != bin)
                continue;
            float4 sample = _SdfDirectionalRays.Load(int3(address, 0));
            drawWeight += info.y;
            if (sample.a == 0.0)
                continue;
            lighting.rgb += sample.rgb * info.y;
            coveredWeight += info.y;
            if (metadata.x == 0.0 || (sample.a > 0.0 && (metadata.x < 0.0 || sample.a < metadata.x)))
                metadata = float4(sample.a, 0.0, info.zw);
        }
        // Store conditional radiance and covered cosine mass separately.
        // Metadata Y stores negative draw weight and positive age for reused bins.
        lighting = float4(lighting.rgb / max(coveredWeight, 1e-5), coveredWeight / max(drawWeight, 1e-5));
        metadata.y = -drawWeight;
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
            if (drawWeight == 0.0 && oldLighting.a > 0.0 && oldMetadata.y < DirectionalHistoryFrames
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
    // The strata partition the cosine domain, so draw weights sum to eight.
    _SdfDiffuseFilteredProbes[id.xy] = float4(min(integrated.rgb * inverseCovered, 65000.0),
        integrated.a / float(DirectionalBinCount));
    _SdfDiffuseFilteredProbes[id.xy + stride] = float4(clamp(moments[0] * inverseCovered, -65000.0, 65000.0), 0.0);
    _SdfDiffuseFilteredProbes[id.xy + 2u * stride] = float4(clamp(moments[1] * inverseCovered, -65000.0, 65000.0), 0.0);
    _SdfDiffuseFilteredProbes[id.xy + 3u * stride] = float4(clamp(moments[2] * inverseCovered, -65000.0, 65000.0), 0.0);
}

#endif
