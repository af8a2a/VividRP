#ifndef VIVID_SDF_RADIANCE_CACHE_COMMON_INCLUDED
#define VIVID_SDF_RADIANCE_CACHE_COMMON_INCLUDED

// The last atlas plane holds exact world-cell keys, so toroidal reuse
// never exposes an evicted probe. Keep the layout in sync with the pass.
static const uint FarGridSize = 16u;
static const uint FarOctSize = 8u;
static const uint FarDirections = FarOctSize * FarOctSize;
static const float FarCellSize = 2.5;
Texture2D<float4> _SdfFarRadiance;

uint2 FarAtlasAddress(int3 cell, uint bin)
{
    uint3 slot = uint3(cell) & (FarGridSize - 1u);
    return uint2(slot.x + FarGridSize * slot.z, slot.y + FarGridSize * bin);
}

float3 FarProbePosition(int3 cell)
{
    return (float3(cell) + 0.5) * FarCellSize;
}

float3 FarProbeDirection(uint bin)
{
    return VividDecodeSurfaceSummaryNormal((float2(bin % FarOctSize, bin / FarOctSize) + 0.5) / FarOctSize);
}

bool HasFarProbe(int3 cell)
{
    if (any(uint3(cell - int3(_SdfFarWindow.xyz)) >= FarGridSize))
        return false;
    float4 key = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, FarDirections), 0));
    return key.w != 0.0 && all(key.xyz == float3(cell));
}

uint FarDirectionBin(float3 direction)
{
    uint2 oct = min(uint2(saturate(VividEncodeSurfaceSummaryNormal(direction)) * FarOctSize), FarOctSize - 1u);
    return oct.x + FarOctSize * oct.y;
}

#if defined(VIVID_SDF_SURFACE_CACHE)
StructuredBuffer<uint> _SdfFarHitIds;

#if defined(VIVID_SDF_FAR_UPDATE)
float4 TraceFarProbe(float3 origin, float3 direction, out uint instance)
{
    instance = 0u;
    VividMeshSdfHit hit;
    bool advancedReceiver, incomplete;
    bool traced = VividTraceMeshSdfSceneInternal(origin, direction, 0.0, _SdfFarTraceDistance - _SdfDiffuseMaxDistance,
        _SdfDiffuseLayerMask, 0.0, false, (VividMeshSdfReceiver)0, hit, advancedReceiver, incomplete);
    if (!traced || incomplete || hit.distance < 0.5 * FarCellSize || dot(hit.normalWS, direction) >= -0.05)
        return 0.0;

    float3 position = origin + direction * hit.distance;
    DiffuseSurface surface = _SdfDiffuseSurfaces[hit.instanceIndex];
    float3 normal, originalNormal;
    uint3 texel;
    float error;
    if (surface.albedoAndValid.a == 0.0 || !VividTrySampleSdfSurface(hit, position, surface, normal, originalNormal, texel, error))
        return 0.0;
    float3 radiance = TraceSdfSurfaceRadiance(position, normal, originalNormal, direction, hit, surface, texel, -1.0);
    instance = hit.instanceIndex + 1u;
    return float4(clamp(radiance, 0.0, 65000.0), hit.distance);
}

#endif

bool TryLoadFarRadiance(float3 origin, float3 direction, out float3 radiance, out float distance)
{
    radiance = 0.0;
    distance = 0.0;
    float3 start = origin + _SdfDiffuseMaxDistance * direction;
    int3 cell = int3(floor(start / FarCellSize));
    if (!HasFarProbe(cell))
        return false;

    uint bin = FarDirectionBin(direction);
    float4 cached = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, bin), 0));
    if (cached.a <= 0.0)
        return false;
    float3 cacheDirection = FarProbeDirection(bin);
    float3 end = FarProbePosition(cell) + cacheDirection * cached.a;
    float3 delta = end - start;
    float travel = length(delta);
    float3 reconnect = delta / max(travel, 1e-5);
    if (travel <= 0.5 * FarCellSize || travel + _SdfDiffuseMaxDistance > _SdfFarTraceDistance
        || dot(reconnect, direction) < 0.965)
        return false;

    // Match the cached endpoint after the near trace. An intervening hit or
    // an unfinished march leaves the far sample unresolved.
    float tolerance = clamp(0.5 * _SdfDiffuseRayBias, 0.01, 0.025);
    VividMeshSdfHit hit;
    bool advancedReceiver, incomplete;
    bool traced = VividTraceMeshSdfSceneInternal(start, reconnect, 0.0, travel + tolerance,
        _SdfDiffuseLayerMask, 0.0, false, (VividMeshSdfReceiver)0, hit, advancedReceiver, incomplete);
    if (!traced || incomplete || abs(hit.distance - travel) > tolerance
        || dot(hit.normalWS, reconnect) >= -0.05 || dot(hit.normalWS, cacheDirection) >= -0.05)
        return false;
    DiffuseSurface surface = _SdfDiffuseSurfaces[hit.instanceIndex];
    float3 normal, originalNormal;
    uint3 texel;
    float error;
    if (!VividTrySampleSdfSurface(hit, start + reconnect * hit.distance, surface,
        normal, originalNormal, texel, error))
        return false;
    radiance = VividSdfMaterialSideVisible(surface.emission.w, originalNormal, -reconnect) ? cached.rgb : 0.0;
    distance = travel + _SdfDiffuseMaxDistance;
    return true;
}

uint FarHitIndex(uint2 address)
{
    return address.x + address.y * (FarGridSize * FarGridSize);
}

float3 ReconstructFarRadiance(float3 start, float3 hitPosition, VividMeshSdfHit hit, out float coverage)
{
    float3 grid = start / FarCellSize - 0.5;
    int3 lower = int3(floor(grid));
    float3 fraction = frac(grid);
    float3 sum = 0.0;
    float total = 0.0;
    [loop]
    for (uint corner = 0u; corner < 8u; ++corner)
    {
        uint3 offset = uint3(corner & 1u, (corner >> 1u) & 1u, corner >> 2u);
        int3 cell = lower + int3(offset);
        if (any(uint3(cell - int3(_SdfFarWindow.xyz)) >= FarGridSize))
            continue;
        float4 key = _SdfFarRadiance.Load(int3(FarAtlasAddress(cell, FarDirections), 0));
        if (key.w == 0.0 || any(key.xyz != float3(cell)))
            continue;

        float3 delta = hitPosition - FarProbePosition(cell);
        float range = length(delta);
        if (range < 0.5 * FarCellSize || dot(delta / range, hit.normalWS) >= -0.05)
            continue;
        float3 spatial = lerp(1.0 - fraction, fraction, float3(offset));
        float2 oct = VividEncodeSurfaceSummaryNormal(delta / range) * FarOctSize - 0.5;
        int2 low = int2(floor(oct));
        float2 angularFraction = frac(oct);
        float radius = clamp(0.15 * range, 0.30, 1.25);
        [unroll]
        for (uint tap = 0u; tap < 4u; ++tap)
        {
            uint2 angularOffset = uint2(tap & 1u, tap >> 1u);
            uint2 binXY = uint2(clamp(low + int2(angularOffset), 0, int(FarOctSize) - 1));
            uint bin = binXY.x + FarOctSize * binXY.y;
            uint2 address = FarAtlasAddress(cell, bin);
            if (_SdfFarHitIds[FarHitIndex(address)] != hit.instanceIndex + 1u)
                continue;
            float4 cached = _SdfFarRadiance.Load(int3(address, 0));
            float3 sampleDirection = FarProbeDirection(bin);
            float3 error = cached.a * sampleDirection - delta;
            float depth = abs(dot(error, hit.normalWS));
            if (dot(sampleDirection, hit.normalWS) >= -0.05 || depth >= 0.10 || length(error) >= radius)
                continue;
            float2 angular = lerp(1.0 - angularFraction, angularFraction, float2(angularOffset));
            float weight = spatial.x * spatial.y * spatial.z * angular.x * angular.y
                * saturate(1.0 - depth / 0.10) * saturate(1.0 - length(error) / radius);
            sum += weight * cached.rgb;
            total += weight;
        }
    }
    // Fade to exact hit lighting where a probe is warming up or the local footprint is incomplete.
    coverage = smoothstep(0.05, 0.55, total);
    return sum / max(total, 1e-5);
}
#endif

#endif
