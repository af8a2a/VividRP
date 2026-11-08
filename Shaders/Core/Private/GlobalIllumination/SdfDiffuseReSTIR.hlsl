#ifndef VIVID_SDF_DIFFUSE_RESTIR_INCLUDED
#define VIVID_SDF_DIFFUSE_RESTIR_INCLUDED

uint _SdfRestirEnabled;
uint _SdfRestirHasHistory;
Texture2D<float4> _SdfRestirPrevious;
Texture2D<float4> _SdfRestirTemporal;
Texture2D<float4> _SdfRestirSpatial;
RWTexture2D<float4> _SdfRestirOutput;
Texture2D<float> _SdfRestirVariance;
RWTexture2D<float> _SdfRestirVarianceOutput;
Texture2D<float4> _SdfRestirFilterInput;

#if defined(VIVID_SDF_RESTIR_SPATIAL)
#define RESTIR_SPATIAL_GROUP_SIZE 8
#define RESTIR_SPATIAL_GROUP_THREADS (RESTIR_SPATIAL_GROUP_SIZE * RESTIR_SPATIAL_GROUP_SIZE)

groupshared uint2 RestirSpatialVisibility[RESTIR_SPATIAL_GROUP_THREADS];
#endif

struct RestirSample
{
    float3 position;
    float3 normal;
    float3 radiance;
    bool fromScreen;
};

struct RestirReservoir
{
    RestirSample sample;
    float inversePdf;
    uint count;
    uint age;
};

struct RestirReuseDomain
{
    float4 geometry;
    uint count;
#if defined(VIVID_SDF_RESTIR_SPATIAL)
    uint groupLane;
#endif
};

float RestirLuminance(float3 radiance)
{
    return dot(radiance, float3(0.2126, 0.7152, 0.0722));
}

float RestirRandom(uint seed)
{
    return float(HashProbe(seed) >> 8u) * (1.0 / 16777216.0);
}

float RestirTarget(RestirSample sample, float3 origin, float3 normal)
{
    return RestirLuminance(sample.radiance) * saturate(dot(normal, SafeNormalize(sample.position - origin)));
}

bool RestirUpdate(inout RestirReservoir reservoir, inout float weightSum, RestirSample sample,
    float weight, uint age, float random)
{
    if (!(weight > 0.0) || !isfinite(weight))
        return false;
    weightSum += weight;
    if (random * weightSum < weight)
    {
        reservoir.sample = sample;
        reservoir.age = age;
        return true;
    }
    return false;
}

void RestirFinalize(inout RestirReservoir reservoir, float weightSum, float3 origin, float3 normal)
{
    float target = RestirTarget(reservoir.sample, origin, normal);
    reservoir.inversePdf = target > 0.0 && reservoir.count != 0u && isfinite(weightSum)
        ? weightSum / (float(reservoir.count) * target) : 0.0;
}

void RestirPack(RestirReservoir reservoir, out float4 geometry, out float4 lighting)
{
    // Two eight-bit octahedral normal coordinates, a six-bit count, and a two-bit age.
    // This numeric 24-bit integer survives both R32F and RGBA32F storage exactly.
    uint normal = reservoir.inversePdf > 0.0 ? uint(PackProbeNormal(reservoir.sample.normal)) : 0u;
    uint packed = normal | (reservoir.count << 16u) | (reservoir.age << 22u);
    // The sign tags screen endpoints without reducing normal, age, or count precision.
    geometry = float4(reservoir.sample.position, reservoir.sample.fromScreen ? -float(packed) : float(packed));
    lighting = float4(reservoir.sample.radiance, reservoir.inversePdf);
}

RestirReservoir RestirUnpack(float4 geometry, float4 lighting)
{
    RestirReservoir reservoir;
    uint packed = uint(abs(geometry.w));
    reservoir.sample.position = geometry.xyz;
    reservoir.sample.normal = UnpackProbeNormal(float(packed & 65535u));
    reservoir.sample.radiance = lighting.rgb;
    reservoir.sample.fromScreen = geometry.w < 0.0;
    reservoir.inversePdf = lighting.a;
    reservoir.count = (packed >> 16u) & 63u;
    reservoir.age = packed >> 22u;
    return reservoir;
}

void StoreInitialRestir(uint2 tile, RestirReservoir reservoir)
{
    // Reuse the hit-filter scratch UAV. Both paths use eight R32F texels per probe.
    float4 geometry, lighting;
    RestirPack(reservoir, geometry, lighting);
    uint2 address = _SdfRestirDense != 0u ? tile * uint2(4u, 2u) : uint2(tile.x * RaysPerProbe, tile.y);
    uint2 secondPlane = _SdfRestirDense != 0u ? uint2(0u, 1u) : uint2(4u, 0u);
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        _SdfDiffuseHitSamples[address + uint2(i, 0u)] = geometry[i];
        _SdfDiffuseHitSamples[address + secondPlane + uint2(i, 0u)] = lighting[i];
    }
}

RestirReservoir LoadInitialRestir(uint2 tile)
{
    float4 geometry, lighting;
    uint2 address = _SdfRestirDense != 0u ? tile * uint2(4u, 2u) : uint2(tile.x * RaysPerProbe, tile.y);
    uint2 secondPlane = _SdfRestirDense != 0u ? uint2(0u, 1u) : uint2(4u, 0u);
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        geometry[i] = _SdfDiffuseHitSamplesInput.Load(int3(address + uint2(i, 0u), 0));
        lighting[i] = _SdfDiffuseHitSamplesInput.Load(int3(address + secondPlane + uint2(i, 0u), 0));
    }
    return RestirUnpack(geometry, lighting);
}

RestirReservoir LoadRestir(Texture2D<float4> source, uint2 tile, uint width)
{
    return RestirUnpack(source.Load(int3(tile, 0)), source.Load(int3(tile + uint2(width, 0u), 0)));
}

void StoreRestir(uint2 tile, uint width, RestirReservoir reservoir)
{
    float4 geometry, lighting;
    RestirPack(reservoir, geometry, lighting);
    _SdfRestirOutput[tile] = geometry;
    _SdfRestirOutput[tile + uint2(width, 0u)] = lighting;
}

float RestirReceiverWeight(float4 other, float3 position, float3 normal, float eyeDepth, float radius)
{
    if (other.w < 0.0)
        return 0.0;
    float3 delta = other.xyz - position;
    return ProbeGeometryWeight(delta, normal, UnpackProbeNormal(other.w), eyeDepth)
        * saturate(1.0 - length(delta) / radius);
}

float3 RestirReceiverOrigin(float4 geometry)
{
    return geometry.xyz + UnpackProbeNormal(geometry.w) * _SdfDiffuseRayBias;
}

float RestirShiftJacobian(RestirSample sample, float3 origin, float3 normal, float3 sourceOrigin,
    out float3 direction, out float distance)
{
    float3 ray = sample.position - origin;
    float distanceSquared = dot(ray, ray);
    distance = sqrt(distanceSquared);
    direction = ray / max(distance, 1e-5);
    if (distance <= 2.0 * _SdfDiffuseRayBias || distance > _SdfDiffuseMaxDistance)
        return 0.0;
    float hitCos = -dot(sample.normal, direction);
    if (dot(normal, direction) <= 0.02 || hitCos <= 0.05)
        return 0.0;

    float3 sourceRay = sample.position - sourceOrigin;
    float sourceHitCos = -dot(sample.normal, SafeNormalize(sourceRay));
    if (sourceHitCos <= 0.05)
        return 0.0;
    float jacobian = hitCos * dot(sourceRay, sourceRay) / (sourceHitCos * distanceSquared);
    return jacobian >= 0.2 && jacobian <= 5.0 ? jacobian : 0.0;
}

bool RestirEndpointVisible(RestirSample sample, float3 origin, float3 direction, float distance)
{
    // Require the reconnected ray's first hit to agree with the cached endpoint.
    // Keeping this interval short limits reuse across opposite sides of thin geometry.
    float tolerance = clamp(0.5 * _SdfDiffuseRayBias, 0.01, 0.025);
    if (sample.fromScreen)
    {
        // A screen sample must still be the first visible surface in the current frame.
        if (_SdfDiffuseUseScreenRays == 0u)
            return false;
        float4 clip = mul(_SdfDiffuseVP, float4(sample.position, 1.0));
        if (clip.w <= 0.0 || clip.z <= 0.0 || clip.z > clip.w || any(abs(clip.xy) >= clip.w))
            return false;
        ScreenRayHit screenHit;
        if (!TraceScreenGeometry(origin, direction, distance + tolerance, screenHit)
            || length(screenHit.position - sample.position) > tolerance || dot(screenHit.normal, sample.normal) < 0.9)
            return false;
    }

    // Screen depth can omit occluders. Check the open SDF segment up to the tolerance
    // around the triangle endpoint. An SDF sample still requires the exact first hit.
    float limit = distance + (sample.fromScreen ? -tolerance : tolerance);
    VividMeshSdfHit hit;
    bool advancedReceiver, incomplete;
    bool traced = VividTraceMeshSdfSceneInternal(origin, direction, 0.0, limit, _SdfDiffuseLayerMask,
        0.0, sample.fromScreen, (VividMeshSdfReceiver)0, hit, advancedReceiver, incomplete);
    return sample.fromScreen ? !traced && !incomplete : traced
        && abs(hit.distance - distance) <= tolerance && dot(hit.normalWS, sample.normal) >= 0.8;
}

uint RestirMerge(inout RestirReservoir reservoir, inout float weightSum, RestirReservoir other,
    float4 otherGeometry, float3 origin, float3 normal, uint countLimit, uint ageIncrement, float random,
    uint sourceIndex, inout uint selectedSource
#if defined(VIVID_SDF_RESTIR_SPATIAL)
    , out uint visibility
#endif
    )
{
#if defined(VIVID_SDF_RESTIR_SPATIAL)
    visibility = 0u;
#endif
    uint count = min(other.count, countLimit);
    if (count == 0u || other.age + ageIncrement > 3u)
        return 0u;

    // Keep eligible source domains even when their particular sample cannot be reused.
    // The selected path is tested against these domains during normalization.
    reservoir.count += count;
    if (other.inversePdf > 0.0)
    {
        float3 sourceOrigin = RestirReceiverOrigin(otherGeometry);
        float3 direction;
        float distance;
        float jacobian = RestirShiftJacobian(other.sample, origin, normal, sourceOrigin, direction, distance);
#if defined(VIVID_SDF_RESTIR_SPATIAL)
        if (jacobian > 0.0)
        {
            if (other.sample.fromScreen || any(sourceOrigin != origin))
            {
                bool visible = RestirEndpointVisible(other.sample, origin, direction, distance);
                visibility = visible ? 3u : 2u;
                if (!visible)
                    return count;
            }
#else
        if (jacobian > 0.0 && ((!other.sample.fromScreen && all(sourceOrigin == origin))
            || RestirEndpointVisible(other.sample, origin, direction, distance)))
        {
#endif
            float target = RestirLuminance(other.sample.radiance) * dot(normal, direction);
            if (RestirUpdate(reservoir, weightSum, other.sample, target * jacobian * other.inversePdf * float(count),
                other.age + ageIncrement, random))
                selectedSource = sourceIndex;
        }
    }
    return count;
}

void RestirFinalizeReuse(inout RestirReservoir reservoir, float weightSum, float3 origin, float3 normal,
    uint localCount, uint selectedSource, RestirReuseDomain sources[2]
#if defined(VIVID_SDF_RESTIR_SPATIAL)
    , uint localSampleKey
#endif
    )
{
    float target = RestirTarget(reservoir.sample, origin, normal);
    if (!(target > 0.0 && weightSum > 0.0) || !isfinite(weightSum))
    {
        reservoir.inversePdf = 0.0;
        return;
    }
    float selectedTarget = target;
    float support = target * float(localCount);
    [unroll]
    for (uint i = 0u; i < 2u; ++i)
    {
        if (sources[i].count == 0u)
            continue;
        float3 sourceNormal = UnpackProbeNormal(sources[i].geometry.w);
        float3 sourceOrigin = sources[i].geometry.xyz + sourceNormal * _SdfDiffuseRayBias;
        float sourceTarget = RestirTarget(reservoir.sample, sourceOrigin, sourceNormal);
        if (selectedSource == i + 1u)
        {
            selectedTarget = sourceTarget;
        }
        else if (sourceTarget > 0.0)
        {
            float3 direction;
            float distance;
            float jacobian = RestirShiftJacobian(reservoir.sample, origin, normal, sourceOrigin, direction, distance);
            float3 sourceRay = reservoir.sample.position - sourceOrigin;
            float sourceDistance = length(sourceRay);
#if defined(VIVID_SDF_RESTIR_SPATIAL)
            if (jacobian == 0.0 || sourceDistance > _SdfDiffuseMaxDistance)
                sourceTarget = 0.0;
            else if (reservoir.sample.fromScreen || any(sourceOrigin != origin))
            {
                uint cached = 0u;
                if (localSampleKey != 0u && sources[i].groupLane < RESTIR_SPATIAL_GROUP_THREADS)
                {
                    // The source lane traced from this origin to our unmodified temporal sample.
                    uint2 queries = RestirSpatialVisibility[sources[i].groupLane];
                    cached = (queries.x >> 1u) == localSampleKey ? queries.x
                        : ((queries.y >> 1u) == localSampleKey ? queries.y : 0u);
                }
                bool visible;
                if (cached != 0u)
                    visible = (cached & 1u) != 0u;
                else
                    visible = RestirEndpointVisible(reservoir.sample, sourceOrigin,
                        sourceRay / max(sourceDistance, 1e-5), sourceDistance);
                if (!visible)
                    sourceTarget = 0.0;
            }
#else
            if (jacobian == 0.0 || sourceDistance > _SdfDiffuseMaxDistance
                || ((reservoir.sample.fromScreen || any(sourceOrigin != origin))
                    && !RestirEndpointVisible(reservoir.sample, sourceOrigin, sourceRay / max(sourceDistance, 1e-5), sourceDistance)))
                sourceTarget = 0.0;
#endif
        }
        support += sourceTarget * float(sources[i].count);
    }

    // Balance by the selected path's target on each source, rather than a uniform 1/M.
    reservoir.inversePdf = support > 0.0 ? (weightSum / target) * (selectedTarget / support) : 0.0;
}

#if !defined(VIVID_SDF_RESTIR_SPATIAL)
[numthreads(8, 8, 1)]
void ResampleSdfRestirTemporal(uint3 id : SV_DispatchThreadID)
{
    uint2 grid = ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy));
    if (any(id.xy >= grid * uint2(1u, 1u + _SdfDiffuseAdaptiveProbes)))
        return;
    float4 geometry = _SdfDiffuseGeometry[id.xy];
    RestirReservoir reservoir = (RestirReservoir)0;
    if (geometry.w >= 0.0)
    {
        reservoir = LoadInitialRestir(id.xy);
        float3 normal = UnpackProbeNormal(geometry.w);
        float3 origin = geometry.xyz + normal * _SdfDiffuseRayBias;
        float4 previousClip = mul(_SdfDiffusePreviousVP, float4(geometry.xyz, 1.0));
        if (_SdfRestirHasHistory != 0u && previousClip.z > 0.0 && previousClip.w >= previousClip.z)
        {
            float2 uv = ScreenUv(previousClip);
            if (all(uv >= 0.0) && all(uv < 1.0))
            {
                uint spacing = _SdfRestirDense != 0u ? RestirPixelSpacing : ProbeSpacing;
                uint2 tile = uint2(uv * _SdfDiffuseScreenSize.xy) / spacing;
                float eyeDepth = dot(float4(geometry.xyz, 1.0), _SdfProbeCameraForward);
                float radius = clamp(0.025 * eyeDepth, 0.08, 0.25);
                float4 previous = _SdfDiffusePreviousGeometry.Load(int3(tile, 0));
                float match = RestirReceiverWeight(previous, geometry.xyz, normal, eyeDepth, radius);
                if (_SdfDiffuseAdaptiveProbes != 0u)
                {
                    uint2 adaptive = tile + uint2(0u, grid.y);
                    float4 alternate = _SdfDiffusePreviousGeometry.Load(int3(adaptive, 0));
                    float score = RestirReceiverWeight(alternate, geometry.xyz, normal, eyeDepth, radius);
                    if (score > match)
                    {
                        tile = adaptive;
                        previous = alternate;
                        match = score;
                    }
                }
                if (match > 0.20)
                {
                    uint localCount = reservoir.count;
                    float weightSum = reservoir.inversePdf * float(localCount) * RestirTarget(reservoir.sample, origin, normal);
                    uint selectedSource = 0u;
                    RestirReuseDomain sources[2];
                    sources[0].geometry = previous;
                    sources[1] = (RestirReuseDomain)0;
                    sources[0].count = RestirMerge(reservoir, weightSum, LoadRestir(_SdfRestirPrevious, tile, grid.x),
                        previous, origin, normal, 2u * ProbeRayCount(), 1u,
                        RestirRandom(ProbeSeed(id.xy) ^ 0x99c55713u), 1u, selectedSource);
                    if (sources[0].count != 0u)
                        RestirFinalizeReuse(reservoir, weightSum, origin, normal, localCount, selectedSource, sources);
                }
            }
        }
    }
    StoreRestir(id.xy, grid.x, reservoir);
}

#else
[numthreads(RESTIR_SPATIAL_GROUP_SIZE, RESTIR_SPATIAL_GROUP_SIZE, 1)]
void ResampleSdfRestirSpatial(uint3 id : SV_DispatchThreadID, uint3 lane : SV_GroupThreadID, uint groupIndex : SV_GroupIndex)
{
    uint2 grid = ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy));
    bool dense = _SdfRestirDense != 0u;
    bool inBounds = all(id.xy < grid * uint2(1u, 1u + _SdfDiffuseAdaptiveProbes));

    float4 geometry = float4(0.0, 0.0, 0.0, -1.0);
    float confidence = 0.0;
    if (inBounds)
    {
        geometry = _SdfDiffuseGeometry[id.xy];
        confidence = _SdfDiffuseProbeInput.Load(int3(id.xy, 0)).a;
    }
    float3 lighting = 0.0;
    float3x3 moments = (float3x3)0.0;
    RestirReservoir reservoir = (RestirReservoir)0;
    bool valid = geometry.w >= 0.0 && (confidence > 0.0 || dense);
    float3 normal = 0.0;
    float3 origin = 0.0;
    uint localCount = 0u;
    float weightSum = 0.0;
    uint selectedSource = 0u;
    RestirReuseDomain sources[2];
    uint2 queries = 0u;
    if (valid)
    {
        normal = UnpackProbeNormal(geometry.w);
        origin = geometry.xyz + normal * _SdfDiffuseRayBias;
        reservoir = LoadRestir(_SdfRestirTemporal, id.xy, grid.x);
        localCount = reservoir.count;
        weightSum = reservoir.inversePdf * float(localCount) * RestirTarget(reservoir.sample, origin, normal);
        float eyeDepth = dot(float4(geometry.xyz, 1.0), _SdfProbeCameraForward);
        float radius = clamp(0.05 * eyeDepth, 0.15, 0.8);
        int2 uniformTile = int2(id.x, id.y % grid.y);
        uint seed = ProbeSeed(id.xy) ^ 0x98b16ab1u;
        uint firstNeighbor = HashProbe(seed) & 3u;
        [loop]
        for (uint i = 0u; i < 2u; ++i)
        {
            sources[i] = (RestirReuseDomain)0;
            uint index = (firstNeighbor + i * 2u) & 3u;
            int side = (index & 1u) == 0u ? -1 : 1;
            int2 adjacent = uniformTile + (index < 2u ? int2(side, 0) : int2(0, side));
            if (any(adjacent < 0) || any(adjacent >= int2(grid)))
                continue;
            uint2 tile = uint2(adjacent);
            float4 other = _SdfDiffuseGeometry[tile];
            float match = RestirReceiverWeight(other, geometry.xyz, normal, eyeDepth, radius);
            if (_SdfDiffuseAdaptiveProbes != 0u)
            {
                uint2 adaptive = tile + uint2(0u, grid.y);
                float4 alternate = _SdfDiffuseGeometry[adaptive];
                float score = RestirReceiverWeight(alternate, geometry.xyz, normal, eyeDepth, radius);
                if (score > match)
                {
                    tile = adaptive;
                    other = alternate;
                    match = score;
                }
            }
            if (match > 0.20)
            {
                sources[i].geometry = other;
                uint visibility;
                sources[i].count = RestirMerge(reservoir, weightSum, LoadRestir(_SdfRestirTemporal, tile, grid.x), other,
                    origin, normal, ProbeRayCount(), 0u, RestirRandom(seed + i + 1u), i + 1u, selectedSource, visibility);
                uint2 sourceLane = tile - (id.xy - lane.xy);
                sources[i].groupLane = RESTIR_SPATIAL_GROUP_THREADS;
                if (dense && all(sourceLane < RESTIR_SPATIAL_GROUP_SIZE))
                {
                    uint sourceIndex = sourceLane.y * RESTIR_SPATIAL_GROUP_SIZE + sourceLane.x;
                    sources[i].groupLane = sourceIndex;
                    // Zero means no exact query was issued. The low bit carries its result.
                    if (visibility != 0u)
                        queries = uint2(queries.y, ((sourceIndex + 1u) << 1u) | (visibility & 1u));
                }
            }
        }
    }
    if (dense)
    {
        // Include lanes outside the grid in the last partial group.
        RestirSpatialVisibility[groupIndex] = queries;
        GroupMemoryBarrierWithGroupSync();
    }
    if (!inBounds)
        return;
    if (valid)
    {
        if (sources[0].count != 0u || sources[1].count != 0u)
        {
            uint localSampleKey = dense && selectedSource == 0u ? groupIndex + 1u : 0u;
            RestirFinalizeReuse(reservoir, weightSum, origin, normal, localCount, selectedSource, sources, localSampleKey);
        }
        if (!dense)
        {
            float3 direction = SafeNormalize(reservoir.sample.position - origin);
            lighting = min(reservoir.sample.radiance * (reservoir.inversePdf * saturate(dot(normal, direction))), 65000.0);
            if (_SdfDiffuseDirectionalProbes != 0u)
                moments = ProbeSampleMoments(lighting, direction, normal);
        }
    }
    if (dense)
        StoreRestir(id.xy, grid.x, reservoir);
    else
        StoreProbe(id.xy, float4(lighting, confidence), moments);
}
#endif

void IntegrateRestirLighting(uint3 id)
{
    uint2 size = uint2(_SdfDiffuseScreenSize.xy);
    if (any(id.xy >= size))
        return;

    float4 result = 0.0;
    float variance = 0.0;
    float3 position, normal;
    if (LoadReceiver(id.xy, position, normal))
    {
        uint2 grid = ProbeGridSize(size);
        uint2 center = id.xy / RestirPixelSpacing;
        uint2 centerSample;
        LoadProbeReceiverPixel(center, size, centerSample);
        int2 start = int2(center) - 1 - int2(id.x < centerSample.x, id.y < centerSample.y);
        float eyeDepth = dot(float4(position, 1.0), _SdfProbeCameraForward);
        float radius = clamp(0.035 * eyeDepth, 0.08, 0.45);
        float total = 0.0;
        float mean = 0.0;
        float m2 = 0.0;
        [loop]
        for (int y = 0; y < 4; ++y)
        {
            [loop]
            for (int x = 0; x < 4; ++x)
            {
                int2 adjacent = start + int2(x, y);
                if (any(adjacent < 0) || any(adjacent >= int2(grid)))
                    continue;
                uint2 tile = uint2(adjacent);
                float4 geometry = _SdfDiffuseGeometry[tile];
                float weight = RestirReceiverWeight(geometry, position, normal, eyeDepth, radius);
                if (weight <= 0.0)
                    continue;

                uint2 samplePixel;
                LoadProbeReceiverPixel(tile, size, samplePixel);
                float2 offset = float2(samplePixel) - float2(id.xy);
                weight *= rcp(1.0 + 0.10 * dot(offset, offset));
                total += weight;
                float4 distant = _SdfDiffuseProbeInput.Load(int3(tile, 0));
                float coveredWeight = weight * distant.a;
                if (coveredWeight <= 0.0)
                    continue;

                RestirReservoir reservoir = LoadRestir(_SdfRestirSpatial, tile, grid.x);
                float3 sourceNormal = UnpackProbeNormal(geometry.w);
                float3 origin = geometry.xyz + sourceNormal * _SdfDiffuseRayBias;
                float3 direction = SafeNormalize(reservoir.sample.position - origin);
                float cosine = saturate(dot(normal, direction));
                // Limit the interpolation gain for a sample grazing the source receiver.
                cosine = min(cosine, 2.0 * max(dot(sourceNormal, direction), 0.01));
                float3 lighting = min(reservoir.sample.radiance * (reservoir.inversePdf * cosine) + distant.rgb, 65000.0);
                result += coveredWeight * float4(lighting, 1.0);

                // Weighted log-luminance variance keeps HDR outliers in a useful FP16 range.
                float value = log2(1.0 + RestirLuminance(lighting));
                float delta = value - mean;
                mean += (coveredWeight / result.a) * delta;
                m2 += coveredWeight * delta * (value - mean);
            }
        }
        float invCoveredWeight = rcp(max(result.a, 1e-5));
        result.rgb *= invCoveredWeight;
        variance = max(0.0, m2 * invCoveredWeight);
        result.a /= max(total, 1e-5);
    }
    _SdfDiffuseOutput[id.xy] = result;
    _SdfRestirVarianceOutput[id.xy] = variance;
}

void FilterRestirLighting(uint3 id)
{
    uint2 size = uint2(_SdfDiffuseScreenSize.xy);
    if (any(id.xy >= size))
        return;

    float4 result = _SdfDiffuseCurrentResolved.Load(int3(id.xy, 0));
    result.rgb = _SdfRestirFilterInput.Load(int3(id.xy, 0)).rgb;
    float variance = _SdfRestirVariance.Load(int3(id.xy, 0));
    float3 position, normal;
    if ((variance > 0.015 || _SdfDiffuseRealtimeOnly != 0u) && LoadReceiver(id.xy, position, normal))
    {
        if (result.a > 0.0 && variance > 0.015)
        {
            int step = variance > 0.5 ? 4 : variance > 0.10 ? 2 : 1;
            float eyeDepth = dot(float4(position, 1.0), _SdfProbeCameraForward);
            float centerLuminance = log2(1.0 + RestirLuminance(result.rgb));
            float lumaScale = rcp(max(0.12, 2.0 * sqrt(variance)));
            float3 sum = result.rgb;
            float total = 1.0;
            [loop]
            for (int y = -1; y <= 1; ++y)
            {
                [loop]
                for (int x = -1; x <= 1; ++x)
                {
                    int2 offset = int2(x, y);
                    int2 tap = int2(id.xy) + offset * step;
                    if (all(offset == 0) || any(tap < 0) || any(tap >= int2(size)))
                        continue;
                    float confidence = _SdfDiffuseCurrentResolved.Load(int3(tap, 0)).a;
                    float3 otherPosition, otherNormal;
                    if (confidence <= 0.0 || !LoadReceiver(uint2(tap), otherPosition, otherNormal))
                        continue;
                    float weight = ProbeGeometryWeight(otherPosition - position, normal, otherNormal, eyeDepth);
                    if (weight <= 0.0)
                        continue;
                    float3 lighting = _SdfRestirFilterInput.Load(int3(tap, 0)).rgb;
                    float difference = abs(log2(1.0 + RestirLuminance(lighting)) - centerLuminance);
                    weight *= confidence * exp2(-difference * lumaScale) / (1.0 + dot(float2(offset), float2(offset)));
                    sum += lighting * weight;
                    total += weight;
                }
            }
            result.rgb = sum / total;
        }
        if (_SdfDiffuseRealtimeOnly != 0u)
            result.a = 1.0;
    }
    _SdfDiffuseOutput[id.xy] = result;
}

#endif
