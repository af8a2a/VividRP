#ifndef VIVID_SDF_DIFFUSE_RESTIR_INCLUDED
#define VIVID_SDF_DIFFUSE_RESTIR_INCLUDED

uint _SdfRestirEnabled;
uint _SdfRestirHasHistory;
Texture2D<float4> _SdfRestirPrevious;
Texture2D<float4> _SdfRestirTemporal;
RWTexture2D<float4> _SdfRestirOutput;

struct RestirSample
{
    float3 position;
    float3 normal;
    float3 radiance;
};

struct RestirReservoir
{
    RestirSample sample;
    float inversePdf;
    uint count;
    uint age;
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

void RestirUpdate(inout RestirReservoir reservoir, inout float weightSum, RestirSample sample,
    float weight, uint count, uint age, float random)
{
    reservoir.count += count;
    weightSum += weight;
    if (random * weightSum < weight)
    {
        reservoir.sample = sample;
        reservoir.age = age;
    }
}

void RestirFinalize(inout RestirReservoir reservoir, float weightSum, float3 origin, float3 normal)
{
    float target = RestirTarget(reservoir.sample, origin, normal);
    reservoir.inversePdf = target > 0.0 && reservoir.count != 0u ? weightSum / (float(reservoir.count) * target) : 0.0;
}

void RestirPack(RestirReservoir reservoir, out float4 geometry, out float4 lighting)
{
    // Two eight-bit octahedral normal coordinates, a six-bit count, and a two-bit age.
    // This numeric 24-bit integer survives both R32F and RGBA32F storage exactly.
    uint normal = reservoir.inversePdf > 0.0 ? uint(PackProbeNormal(reservoir.sample.normal)) : 0u;
    uint packed = normal | (reservoir.count << 16u) | (reservoir.age << 22u);
    geometry = float4(reservoir.sample.position, float(packed));
    lighting = float4(reservoir.sample.radiance, reservoir.inversePdf);
}

RestirReservoir RestirUnpack(float4 geometry, float4 lighting)
{
    RestirReservoir reservoir;
    uint packed = uint(geometry.w);
    reservoir.sample.position = geometry.xyz;
    reservoir.sample.normal = UnpackProbeNormal(float(packed & 65535u));
    reservoir.sample.radiance = lighting.rgb;
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
    uint2 address = uint2(tile.x * RaysPerProbe, tile.y);
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        _SdfDiffuseHitSamples[address + uint2(i, 0u)] = geometry[i];
        _SdfDiffuseHitSamples[address + uint2(i + 4u, 0u)] = lighting[i];
    }
}

RestirReservoir LoadInitialRestir(uint2 tile)
{
    float4 geometry, lighting;
    uint2 address = uint2(tile.x * RaysPerProbe, tile.y);
    [unroll]
    for (uint i = 0u; i < 4u; ++i)
    {
        geometry[i] = _SdfDiffuseHitSamplesInput.Load(int3(address + uint2(i, 0u), 0));
        lighting[i] = _SdfDiffuseHitSamplesInput.Load(int3(address + uint2(i + 4u, 0u), 0));
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

void RestirMerge(inout RestirReservoir reservoir, inout float weightSum, RestirReservoir other,
    float4 otherGeometry, float3 origin, float3 normal, uint countLimit, uint ageIncrement, float random)
{
    uint count = min(other.count, countLimit);
    if (count == 0u || other.age + ageIncrement > 3u)
        return;
    if (other.inversePdf <= 0.0)
    {
        // Black proposals are represented in the candidate count even without a selected hit.
        reservoir.count += count;
        return;
    }

    float3 ray = other.sample.position - origin;
    float distanceSquared = dot(ray, ray);
    float distance = sqrt(distanceSquared);
    if (distance <= 2.0 * _SdfDiffuseRayBias || distance > _SdfDiffuseMaxDistance)
        return;
    float3 direction = ray / distance;
    float receiverCos = dot(normal, direction);
    float hitCos = -dot(other.sample.normal, direction);
    if (receiverCos <= 0.02 || hitCos <= 0.05)
        return;

    float3 otherOrigin = otherGeometry.xyz + UnpackProbeNormal(otherGeometry.w) * _SdfDiffuseRayBias;
    float3 oldRay = other.sample.position - otherOrigin;
    float oldDistanceSquared = dot(oldRay, oldRay);
    float oldHitCos = -dot(other.sample.normal, SafeNormalize(oldRay));
    if (oldHitCos <= 0.05)
        return;
    float jacobian = hitCos * oldDistanceSquared / (oldHitCos * distanceSquared);
    if (jacobian < 0.2 || jacobian > 5.0)
        return;

    // Require the reconnected ray's first hit to agree with the cached endpoint.
    // Keeping this interval short limits reuse across opposite sides of thin geometry.
    float tolerance = clamp(0.5 * _SdfDiffuseRayBias, 0.01, 0.025);
    VividMeshSdfHit hit;
    if (!VividTraceMeshSdfScene(origin, direction, 0.0, distance + tolerance, _SdfDiffuseLayerMask, hit)
        || abs(hit.distance - distance) > tolerance || dot(hit.normalWS, other.sample.normal) < 0.8)
        return;

    float target = RestirLuminance(other.sample.radiance) * receiverCos;
    RestirUpdate(reservoir, weightSum, other.sample, target * jacobian * other.inversePdf * float(count),
        count, other.age + ageIncrement, random);
}

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
                uint2 tile = uint2(uv * _SdfDiffuseScreenSize.xy) / ProbeSpacing;
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
                    float weightSum = reservoir.inversePdf * float(reservoir.count) * RestirTarget(reservoir.sample, origin, normal);
                    RestirMerge(reservoir, weightSum, LoadRestir(_SdfRestirPrevious, tile, grid.x), previous,
                        origin, normal, 2u * RaysPerProbe, 1u, RestirRandom(ProbeSeed(id.xy) ^ 0x99c55713u));
                    RestirFinalize(reservoir, weightSum, origin, normal);
                }
            }
        }
    }
    StoreRestir(id.xy, grid.x, reservoir);
}

[numthreads(8, 8, 1)]
void ResampleSdfRestirSpatial(uint3 id : SV_DispatchThreadID)
{
    uint2 grid = ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy));
    if (any(id.xy >= grid * uint2(1u, 1u + _SdfDiffuseAdaptiveProbes)))
        return;

    float4 geometry = _SdfDiffuseGeometry[id.xy];
    float confidence = _SdfDiffuseProbeInput.Load(int3(id.xy, 0)).a;
    float3 lighting = 0.0;
    float3x3 moments = (float3x3)0.0;
    if (geometry.w >= 0.0 && confidence > 0.0)
    {
        float3 normal = UnpackProbeNormal(geometry.w);
        float3 origin = geometry.xyz + normal * _SdfDiffuseRayBias;
        RestirReservoir reservoir = LoadRestir(_SdfRestirTemporal, id.xy, grid.x);
        float weightSum = reservoir.inversePdf * float(reservoir.count) * RestirTarget(reservoir.sample, origin, normal);
        float eyeDepth = dot(float4(geometry.xyz, 1.0), _SdfProbeCameraForward);
        float radius = clamp(0.05 * eyeDepth, 0.15, 0.8);
        int2 uniformTile = int2(id.x, id.y % grid.y);
        uint seed = ProbeSeed(id.xy) ^ 0x98b16ab1u;
        uint firstNeighbor = HashProbe(seed) & 3u;
        [loop]
        for (uint i = 0u; i < 2u; ++i)
        {
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
                RestirMerge(reservoir, weightSum, LoadRestir(_SdfRestirTemporal, tile, grid.x), other,
                    origin, normal, RaysPerProbe, 0u, RestirRandom(seed + i + 1u));
        }
        RestirFinalize(reservoir, weightSum, origin, normal);
        float3 direction = SafeNormalize(reservoir.sample.position - origin);
        lighting = min(reservoir.sample.radiance * (reservoir.inversePdf * saturate(dot(normal, direction))), 65000.0);
        if (_SdfDiffuseDirectionalProbes != 0u)
            moments = ProbeSampleMoments(lighting, direction, normal);
    }
    StoreProbe(id.xy, float4(lighting, confidence), moments);
}

#endif
