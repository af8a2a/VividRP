#ifndef VIVID_SDF_SCREEN_PROBE_COMMON_INCLUDED
#define VIVID_SDF_SCREEN_PROBE_COMMON_INCLUDED

#include "../../Public/Input.hlsl"
#include "../../Public/GPUDriven/SdfSurfaceCache.hlsl"
#if !defined(VIVID_SDF_SCREEN_ONLY)
#include "../../Public/GPUDriven/GlobalSdf.hlsl"
#endif
#include "../../Public/SurfaceSummaryGBuffer.hlsl"
#include "../../Public/MotionVectorsCommon.hlsl"
#include "../../Public/GPUDriven/VividVisibilityBuffer.hlsl"
#include "../../Public/VividSkyLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#if defined(VIVID_SDF_RESTIR_OPENPBR)
#include "../../Public/ShaderVariablesFunctions.hlsl"
#include "../../Public/VividOpenPBROpaque.hlsl"
#include "../../Public/VividOpenPBROpaqueGBuffer.hlsl"
Texture2D<float4> _SdfOpenPbrParameters;
Texture2D<float4> _SdfOpenPbrAux0;
Texture2D<float4> _SdfOpenPbrAux1;
#endif

static const uint ProbeSpacing = 16u;
static const uint RestirPixelSpacing = 2u;
static const uint RaysPerProbe = 8u; // Legacy hemisphere and ReSTIR layouts.
uint _SdfScreenProbeFullSphere;
static const float ProbeMomentScale = 0.25;
static const uint RayScreenHit = 1u;
static const uint RaySdfHit = 2u;
static const uint RayMissingSurface = 3u;
static const uint RayMiss = 4u;
static const uint RayBackface = 5u;
static const uint RayOffscreenOnlyRejected = 6u;
static const uint RaySkyVisible = 7u;
static const uint RayScreenHistoryRejected = 8u;
static const uint RaySkyOccluded = 16u;
static const uint RaySkyUnresolved = 32u;
static const uint RayFarCache = 64u;
static const uint RayScreenCardIndirect = 128u;
static const uint RayTraceIncomplete = 256u;
static const uint RayGlobalCandidate = 512u;
static const uint RayFarSegment = 1024u;
static const uint RayMaterialBackface = 2048u; // Accepted geometric hit, covered black.

Texture2D<float> _SdfDiffuseDepth;
Texture2D<float4> _SdfDiffuseGBuffer0;
Texture2D<float4> _SdfDiffuseGBuffer1;
Texture2D<float4> _SdfDiffusePreviousProbes;
Texture2D<float4> _SdfDiffusePreviousGeometry;
Texture2D<float> _SdfScreenHzb;
Texture2D<float4> _SdfScreenMotion;
Texture2D<float4> _SdfScreenPreviousRadiance;
Texture2D<float2> _SdfScreenPreviousGeometry;
Texture2D<float4> _SdfScreenRays;
Texture2D<float> _SdfAdaptivePlacement;
Texture2D<float4> _SdfDiffuseProbeInput;
Texture2D<float> _SdfDiffuseHitSamplesInput;
Texture2D<float4> _SdfDiffuseCurrentResolved;
Texture2D<float4> _SdfDiffusePreviousPixelLighting;
Texture2D<uint> _SdfDiffusePreviousPixelGeometry;
RWTexture2D<float> _SdfAdaptivePlacementOutput;
RWTexture2D<float4> _SdfDiffuseFilteredProbes;
RWTexture2D<float> _SdfDiffuseHitSamples;
RWTexture2D<float4> _SdfScreenRaysOutput;
RWTexture2D<float4> _SdfDiffuseProbes;
RWTexture2D<float4> _SdfDiffuseGeometry;
RWTexture2D<float4> _SdfDiffuseHitLighting;
RWTexture2D<float4> _SdfDiffuseOutput;
RWTexture2D<float4> _SdfDiffuseTraceSources;
RWTexture2D<float4> _SdfDiffuseRayDiagnostics;
RWTexture2D<float4> _SdfDiffuseFirstHitDiagnostics;
RWTexture2D<float4> _SdfDiffusePixelLightingOutput;
RWTexture2D<uint> _SdfDiffusePixelGeometryOutput;

float4x4 _SdfDiffuseInverseVP;
float4x4 _SdfDiffuseVP;
float4x4 _SdfDiffusePreviousVP;
float4x4 _SdfScreenPreviousInverseVP;
float2 _SdfScreenJitterDelta;
float4 _SdfDiffuseScreenSize;
float4 _SdfProbeCameraForward;
float4 _SdfDiffuseLightPositionRange;
float4 _SdfDiffuseLightColorShadow;
float4 _SdfHistoryLightScale;
float _SdfDiffuseMaxDistance;
float _SdfDiffuseSkyTraceDistance;
float _SdfFarTraceDistance;
float _SdfMeshDetailDistance;
uint _SdfUseGlobalClipmap;
float4 _SdfFarWindow;
float _SdfDiffuseRayBias;
float4 _SdfNearOcclusion; // Radius, strength, and whether to integrate directional visibility.
uint _SdfDiffuseLayerMask;
uint _SdfDiffuseLightLayerMask;
uint _SdfDiffuseFrame;
uint _SdfProbePreviousFrame;
uint _SdfDiffuseHasHistory;
uint _SdfDiffuseDirectionalProbes;
uint _SdfDiffuseAdaptiveProbes;
uint _SdfRestirDense;
uint _SdfRestirOpenPbrDiffuse;
uint _SdfDiffuseSpatialFilter;
uint _SdfProbeSpatialKernel;
float _SdfProbeHistoryBlend;
uint _SdfDiffuseHitFilterEnabled;
uint _SdfDiffuseOffscreenOnly;
uint _SdfDiffuseUseScreenRays;
uint _SdfScreenCardLighting;
uint _SdfScreenMaterialLookup;
uint _SdfDiffuseRecordRayDiagnostics;
uint _SdfDiffuseRecordFirstHitDiagnostics;
// Bit 32 selects full-precision anchor storage. Other bits are Editor ablations.
// Normal rendering derives precision from anchor age and retains the 0.85 blend.
uint _SdfGatherHistoryDiagnostics;
float _SdfProbeAnchorHistoryBlend;

uint2 ProbeTileJitter(uint frame)
{
    // 64-frame R2 low-discrepancy sequence inside the 16x16 probe tile to eliminate
    // 8-frame (7.5 Hz) global strobe limit cycles while keeping uniform tile coverage.
    uint index = frame & 63u;
    float2 r2 = frac(float2(index, index) * float2(0.754877666f, 0.569840291f));
    return min(uint2(r2 * float(ProbeSpacing)), ProbeSpacing - 1u);
}

float2 ProbeGridPosition(float2 pixel, uint2 jitter)
{
    float2 probe = (pixel - float2(jitter)) / ProbeSpacing;
    uint2 grid = (uint2(_SdfDiffuseScreenSize.xy) + ProbeSpacing - 1u) / ProbeSpacing;
    float2 lower = max(float2(grid) - 2.0, 0.0);
    float2 lowerPixel = min(lower * ProbeSpacing + jitter, _SdfDiffuseScreenSize.xy - 1.0);
    float2 lastPixel = min(float2(grid - 1u) * ProbeSpacing + jitter, _SdfDiffuseScreenSize.xy - 1.0);
    float2 tail = lower + (pixel - lowerPixel) / max(lastPixel - lowerPixel, 1.0);
    probe = lerp(probe, tail, float2(pixel >= lowerPixel));
    return probe * float2(grid > 1u);
}

float2 ProbeHistoryGridPosition(float2 uv)
{
    // History belongs to the last rendered sample, including fixed-frame captures.
    // ReceiverPixel is an integer texel index; its projected UV is at index + 0.5.
    float halfTexel = (_SdfGatherHistoryDiagnostics & 256u) != 0u ? 0.0 : 0.5;
    return ProbeGridPosition(uv * _SdfDiffuseScreenSize.xy - halfTexel,
        ProbeTileJitter(_SdfProbePreviousFrame));
}

float4 SdfRoundToNearestHalf(float4 value)
{
    // Do not assume either f32tof16 or a typed R16F UAV store rounds to nearest.
    // Bracket the magnitude using adjacent half values, then resolve ties to even.
    float4 magnitude = min(abs(value), 65504.0);
    uint4 lower = f32tof16(magnitude);
    lower -= uint4(f16tof32(lower) > magnitude);
    float4 low = f16tof32(lower);
    float4 high = f16tof32(min(lower + 1u, 0x7bffu));
    float4 below = magnitude - low;
    float4 above = high - magnitude;
    uint4 up = uint4(below > above) | (uint4(below == above) & (lower & 1u));
    up &= uint4(lower < 0x7bffu);
    float4 rounded = f16tof32(lower + up);
    return asfloat(asuint(rounded) | (asuint(value) & 0x80000000u));
}

float4 RoundSdfHistory(float4 value)
{
    return (_SdfGatherHistoryDiagnostics & 128u) != 0u ? value : SdfRoundToNearestHalf(value);
}

float4 RoundSdfAnchorHistory(float4 value)
{
    return (_SdfGatherHistoryDiagnostics & 32u) != 0u ? value : RoundSdfHistory(value);
}

bool LoadReceiver(uint2 pixel, out float3 position, out float3 normal)
{
    float depth = _SdfDiffuseDepth.Load(int3(pixel, 0));
    float4 rt1 = _SdfDiffuseGBuffer1.Load(int3(pixel, 0));
    uint surfaceClass = VividGetDeferredExportClass(
        VividDecodeDeferredExportHeader(_SdfDiffuseGBuffer0.Load(int3(pixel, 0)).a));
    position = 0.0;
    normal = 0.0;
    if (depth == UNITY_RAW_FAR_CLIP_VALUE || !VividIsSurfaceSummaryGBufferABIValid(rt1.a)
        || (surfaceClass != VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB
            && surfaceClass != VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB
            && surfaceClass != VIVID_DEFERRED_EXPORT_CLASS_OPENPBR_OPAQUE))
        return false;

    float2 uv = (float2(pixel) + 0.5) * _SdfDiffuseScreenSize.zw;
    position = ComputeWorldSpacePosition(uv, depth, _SdfDiffuseInverseVP);
    normal = VividDecodeSurfaceSummaryNormal(rt1.xy);
    return true;
}

float PackProbeNormal(float3 normal)
{
    uint2 oct = uint2(round(saturate(VividEncodeSurfaceSummaryNormal(normal)) * 255.0));
    return float(oct.x + 256u * oct.y);
}

float3 UnpackProbeNormal(float packed)
{
    uint oct = (uint)packed;
    return VividDecodeSurfaceSummaryNormal(float2(oct & 255u, oct >> 8u) / 255.0);
}

uint HashProbe(uint value)
{
    value ^= value >> 16u;
    value *= 0x7feb352du;
    value ^= value >> 15u;
    value *= 0x846ca68bu;
    return value ^ (value >> 16u);
}

uint2 ProbeReceiverPixel(uint2 tile, uint2 screenSize)
{
    return min(tile * ProbeSpacing + ProbeTileJitter(_SdfDiffuseFrame), screenSize - 1u);
}

uint2 ProbeGridSize(uint2 screenSize)
{
    uint spacing = _SdfRestirDense != 0u ? RestirPixelSpacing : ProbeSpacing;
    return (screenSize + spacing - 1u) / spacing;
}

uint ProbeRayCount()
{
    return _SdfRestirDense != 0u ? 1u : (_SdfScreenProbeFullSphere != 0u ? 64u : RaysPerProbe);
}

uint2 ProbeTraceAddress(uint2 tile, uint ray)
{
    if (_SdfScreenProbeFullSphere != 0u)
        return tile * 8u + uint2(ray & 7u, ray >> 3u);
    return uint2(tile.x * ProbeRayCount() + ray, tile.y);
}

// Equal solid angle per texel. Unity Z/X/Y correspond to Unreal forward/right/up.
float3 ProbeSphereDirection(float2 uv)
{
    float2 p = 2.0 * uv - 1.0;
    float z = 1.0 - abs(p.x) - abs(p.y);
    float r = 1.0 - abs(z);
    float phi = (PI * 0.25) * (1.0 + (abs(p.y) - abs(p.x)) / max(r, 1e-8));
    float s, c;
    sincos(phi, s, c);
    float xy = r * sqrt(2.0 - r * r);
    return float3(sign(p.y) * xy * s, sign(z) * (1.0 - r * r), sign(p.x) * xy * c);
}

float2 ProbeSphereUV(float3 direction)
{
    float3 d = normalize(direction).zxy;
    float r = sqrt(saturate(1.0 - abs(d.z)));
    float phi = atan2(abs(d.y), max(abs(d.x), 1e-20)) * (2.0 / PI);
    float2 p = r * float2(1.0 - phi, phi);
    if (d.z < 0.0)
        p = 1.0 - p.yx;
    p *= float2(d.x < 0.0 ? -1.0 : 1.0, d.y < 0.0 ? -1.0 : 1.0);
    return saturate(p * 0.5 + 0.5);
}


// Compact placement texture: mark/pruned masks, tile prefix, row prefix,
// and a reverse map from assigned atlas slots to their receiver samples.
uint ProbeAtlasHeight(uint2 grid)
{
    return grid.y + _SdfDiffuseAdaptiveProbes * (_SdfScreenProbeFullSphere != 0u ? (grid.y + 1u) / 2u : grid.y);
}

uint2 AdaptiveCandidatePixel(uint2 tile, uint index)
{
    // Match the 64-frame temporal index of the uniform probe lattice.
    uint temporalIndex = _SdfDiffuseFrame & 63u;
    uint3 v = uint3(tile, temporalIndex) * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    uint2 seed = v.xy >> 16u;
    float2 h = float2(frac(float(index) / 8.0 + float(seed.x) * (1.0 / 65536.0)),
        float((reversebits(index) >> 16u) ^ seed.y) * (1.0 / 65536.0));
    return tile * ProbeSpacing + ProbeTileJitter(temporalIndex) + uint2(clamp(h * ProbeSpacing, 0.0, ProbeSpacing - 1u));
}

uint2 AdaptiveAtlasSlot(uint slot, uint2 grid)
{
    return uint2(slot % grid.x, grid.y + slot / grid.x);
}

uint2 CompactAdaptiveRange(uint2 tile, uint2 grid)
{
    uint start = (uint)_SdfAdaptivePlacement.Load(int3(tile + uint2(0u, 2u * grid.y), 0))
        + (uint)_SdfAdaptivePlacement.Load(int3(0u, 3u * grid.y + tile.y, 0));
    uint count = countbits((uint)_SdfAdaptivePlacement.Load(int3(tile + uint2(0u, grid.y), 0)));
    uint budget = grid.x * grid.y / 2u;
    return uint2(start, min(count, budget - min(start, budget)));
}

bool CompactReceiver(uint2 atlas, uint2 grid, out uint2 owner, out uint2 pixel)
{
    uint index = (atlas.y - grid.y) * grid.x + atlas.x;
    owner = pixel = 0u;
    if (index >= (uint)_SdfAdaptivePlacement.Load(int3(0u, 5u * grid.y, 0))) return false;
    uint code = (uint)_SdfAdaptivePlacement.Load(int3(atlas + uint2(0u, 3u * grid.y), 0));
    uint tile = code >> 3u;
    owner = uint2(tile % grid.x, tile / grid.x);
    pixel = AdaptiveCandidatePixel(owner, code & 7u);
    return true;
}

bool LoadAdaptiveProbePixel(uint2 tile, out uint2 pixel)
{
    uint encoded = (uint)_SdfAdaptivePlacement.Load(int3(tile, 0));
    uint offset = encoded - 1u;
    pixel = tile * ProbeSpacing + ProbeTileJitter(_SdfDiffuseFrame)
        + uint2(offset % ProbeSpacing, offset / ProbeSpacing);
    return encoded != 0u;
}

bool LoadProbeReceiverPixel(uint2 atlas, uint2 screenSize, out uint2 pixel)
{
    if (_SdfRestirDense != 0u)
    {
        uint2 jitter = uint2(_SdfDiffuseFrame & 1u, (_SdfDiffuseFrame >> 1u) & 1u);
        pixel = min(atlas * RestirPixelSpacing + jitter, screenSize - 1u);
        return true;
    }
    uint gridHeight = ProbeGridSize(screenSize).y;
    if (atlas.y < gridHeight)
    {
        pixel = ProbeReceiverPixel(atlas, screenSize);
        return true;
    }
    if (_SdfScreenProbeFullSphere != 0u)
    {
        uint2 owner;
        return CompactReceiver(atlas, ProbeGridSize(screenSize), owner, pixel);
    }
    return LoadAdaptiveProbePixel(atlas - uint2(0, gridHeight), pixel);
}

bool LoadProbeReceiver(uint2 atlas, uint2 screenSize, out float3 position, out float3 normal)
{
    uint2 pixel;
    if (LoadProbeReceiverPixel(atlas, screenSize, pixel))
        return LoadReceiver(pixel, position, normal);
    position = normal = 0.0;
    return false;
}

float ProbeGeometryWeight(float3 delta, float3 normal, float3 otherNormal, float eyeDepth)
{
    float plane = max(abs(dot(delta, normal)), abs(dot(delta, otherNormal)));
    float planeWeight = saturate(1.0 - plane / clamp(0.006 * eyeDepth, 0.035, 0.10));
    float normalWeight = saturate((dot(normal, otherNormal) - 0.88) / 0.12);
    float depthWeight = saturate(1.0 - abs(dot(delta, _SdfProbeCameraForward.xyz)) / max(0.20, 0.16 * eyeDepth));
    return planeWeight * normalWeight * depthWeight;
}

uint ProbeSeed(uint2 tile)
{
    return HashProbe(tile.x + tile.y * 65537u + _SdfDiffuseFrame * 0x9e3779b9u);
}

float3 CosineRayDirection(uint ray, uint rayCount, uint seed, float3x3 basis)
{
    float u = (float(ray) + float(seed >> 16u) / 65536.0) / float(rayCount);
    float phi = TWO_PI * frac(float(seed & 65535u) / 65536.0 + float(ray) * 0.61803398875);
    float s, c;
    sincos(phi, s, c);
    return mul(float3(sqrt(u) * c, sqrt(u) * s, sqrt(1.0 - u)), basis);
}

float3 ProbeRayDirection(uint ray, uint seed, float3x3 basis)
{
    return CosineRayDirection(ray, ProbeRayCount(), seed, basis);
}

float2 ScreenUv(float4 clip)
{
    float2 uv = clip.xy / clip.w * 0.5 + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
        uv.y = 1.0 - uv.y;
    #endif
    return uv;
}

uint2 ProbePlaneStride()
{
    return uint2((uint(_SdfDiffuseScreenSize.x) + ProbeSpacing - 1u) / ProbeSpacing, 0u);
}

uint2 DistantProbeDirectionAddress(uint2 tile)
{
    return tile + uint2(ProbeGridSize(uint2(_SdfDiffuseScreenSize.xy)).x, 0u);
}

float3x3 ProbeSampleMoments(float3 radiance, float3 direction, float3 normal)
{
    // Differentiate the cosine response in the tangent plane. Limit inverse-PDF
    // weights near the horizon, where eight rays cannot estimate the slope reliably.
    float cosine = dot(direction, normal);
    float3 tangent = (direction - cosine * normal) * (ProbeMomentScale / max(cosine, 0.25));
    return float3x3(tangent.x * radiance, tangent.y * radiance, tangent.z * radiance);
}

void StoreProbe(uint2 tile, float4 lighting, float3x3 moments)
{
    _SdfDiffuseProbes[tile] = lighting;
    if (_SdfDiffuseDirectionalProbes != 0u)
    {
        uint2 stride = ProbePlaneStride();
        _SdfDiffuseProbes[tile + stride] = float4(clamp(moments[0], -65000.0, 65000.0), 0.0);
        _SdfDiffuseProbes[tile + 2u * stride] = float4(clamp(moments[1], -65000.0, 65000.0), 0.0);
        _SdfDiffuseProbes[tile + 3u * stride] = float4(clamp(moments[2], -65000.0, 65000.0), 0.0);
    }
}

#endif
