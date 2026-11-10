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

float RestirLuminance(float3 radiance)
{
    return dot(radiance, float3(0.2126, 0.7152, 0.0722));
}

float PackRestirPixelHistory(float age, float coverage, bool openPbr)
{
    if (age <= 0.0)
        return 0.0;

    // Keep the material domain in the sign. The finite FP16 payload stores age and coverage.
    uint ageBits = uint(round(clamp(age, 1.0, 32.0))) - 1u;
    uint coverageBits = max(1u, uint(round(saturate(coverage) * 511.0)));
    float packed = f16tof32(0x0400u + (ageBits << 9u) + coverageBits);
    return openPbr ? -packed : packed;
}

float2 UnpackRestirPixelHistory(float packed)
{
    uint bits = f32tof16(abs(packed));
    if (bits < 0x0400u)
        return 0.0;

    bits -= 0x0400u;
    return float2(1.0 + (bits >> 9u), (bits & 511u) * (1.0 / 511.0));
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

#endif
