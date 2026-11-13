#ifndef VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED
#define VIVID_SDF_DIFFUSE_DIRECTIONAL_CACHE_INCLUDED

// Four azimuth sectors and two radial bands have equal mass under the
// cosine hemisphere PDF. Bins are local to the probe's geometric frame.
static const uint DirectionalBinCount = 8u;

uint _SdfDirectionalCacheEnabled;
uint _SdfDirectionalGuideEnabled;

// YZ holds the previous tile plus one. In the original guide X is the bin,
// and W selects the full bin, an azimuth half (-1/-2), or a merged bin (+1).
// Hierarchical rays pack the octahedral rectangle in X and its guide source in W.
Texture2D<float4> _SdfDirectionalGuide;

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

float4 PackDirectionalHitPlane(float3 normal, uint instanceIndex)
{
    // Two finite half payloads preserve 26 instance bits exactly. Unknown or
    // larger identities use the conservative range tier. Scene revision invalidates indices.
    if (instanceIndex >= 0x4000000u || dot(normal, normal) < 0.5)
        return 0.0;
    uint2 words = uint2(instanceIndex & 8191u, instanceIndex >> 13u) + 0x400u;
    return float4(VividEncodeSurfaceSummaryNormal(normal), f16tof32(words));
}

bool SameDirectionalHitPlane(float4 a, float4 b)
{
    return a.w > 0.0 && b.w > 0.0 && all(a.zw == b.zw);
}

float PackDirectionalHistoryAge(float age, float stale)
{
    uint mass = uint(round(clamp(age, 0.0, 8.0) * 32.0));
    uint undrawn = uint(round(clamp(stale, 0.0, 8.0)));
    return f16tof32(0x400u + mass + (undrawn << 9u));
}

float2 UnpackDirectionalHistoryAge(float packed)
{
    uint bits = f32tof16(packed);
    if (bits < 0x400u || bits >= 0x2400u)
        return 0.0;
    uint payload = bits - 0x400u;
    return float2(float(payload & 511u) / 32.0, float((payload >> 9u) & 15u));
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

float2 DirectionalTemporalSample(uint2 tile, uint ray, uint seed)
{
    // Keep independent per-frame sampling by default. Correlated temporal
    // samples remain an Editor experiment until regional energy is validated.
    if ((_SdfGatherHistoryDiagnostics & 262144u) == 0u)
    {
        uint sampleSeed = seed ^ (0xb5297a4du * (ray + 1u));
        return float2(DirectionalRandom(sampleSeed), DirectionalRandom(sampleSeed ^ 0x68e31da4u));
    }

    // A digitally shifted 2D Sobol net stratifies both dimensions over time.
    // Scramble each probe/ray independently, while sharing the sequence between
    // screen and SDF tracing. No short frame cycle or per-frame rotation.
    uint index = _SdfDiffuseFrame ^ (_SdfDiffuseFrame >> 1u);
    uint second = index;
    second ^= (second & 0xaaaaaaaau) >> 1u;
    second ^= (second & 0xccccccccu) >> 2u;
    second ^= (second & 0xf0f0f0f0u) >> 4u;
    second ^= (second & 0xff00ff00u) >> 8u;
    second ^= (second & 0xffff0000u) >> 16u;
    uint scramble = HashProbe(tile.x + tile.y * 65537u) ^ (0xb5297a4du * (ray + 1u));
    uint2 sample = uint2(reversebits(index), reversebits(second))
        ^ uint2(HashProbe(scramble), HashProbe(scramble ^ 0x68e31da4u));
    return float2(sample >> 8u) * (1.0 / 16777216.0);
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
static const uint DirectionalGatherBinCount = DirectionalOctSize * DirectionalOctSize;
static const int DirectionalOctScreen = -3;
static const int DirectionalOctWorld = -4;

uint2 DirectionalGatherAddress(uint2 tile, uint bin)
{
    return uint2(tile.x * DirectionalGatherBinCount + bin, tile.y);
}

float3 DirectionalGatherDirection(uint bin, float3x3 basis)
{
    return DirectionalOctDirection((float2(bin % DirectionalOctSize, bin / DirectionalOctSize) + 0.5)
        / DirectionalOctSize, basis);
}

uint DirectionalGatherBin(float3 direction, float3x3 basis)
{
    // Invert the concentric disk warp used by DirectionalOctDirection.
    float2 p = mul(basis, direction).xy;
    float radius = length(p);
    if (radius < 1e-5)
        return DirectionalOctSize * (DirectionalOctSize / 2u) + DirectionalOctSize / 2u;
    bool horizontal = abs(p.x) > abs(p.y);
    float dominant = horizontal ? p.x : p.y;
    float secondary = horizontal ? p.y : p.x;
    float signDominant = dominant >= 0.0 ? 1.0 : -1.0;
    float a = radius * signDominant;
    float b = a * atan2(secondary * signDominant, abs(dominant)) * (8.0 / TWO_PI);
    float2 uv = 0.5 + 0.5 * (horizontal ? float2(a, b) : float2(b, a));
    uint2 cell = min(uint2(saturate(uv) * DirectionalOctSize), DirectionalOctSize - 1u);
    return cell.x + cell.y * DirectionalOctSize;
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

    float2 sampleUv = DirectionalTemporalSample(tile, ray, seed);
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

#endif
