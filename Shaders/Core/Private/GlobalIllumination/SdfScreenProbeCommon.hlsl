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
static const uint RaysPerProbe = 8u;
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

Texture2D<float> _SdfDiffuseDepth;
Texture2D<float4> _SdfDiffuseGBuffer0;
Texture2D<float4> _SdfDiffuseGBuffer1;
Texture2D<float4> _SdfDiffusePreviousProbes;
Texture2D<float4> _SdfDiffusePreviousGeometry;
Texture2D<float> _SdfScreenHzb;
Texture2D<float2> _SdfScreenMotion;
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
uint _SdfDiffuseRecordRayDiagnostics;
uint _SdfDiffuseRecordFirstHitDiagnostics;

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
    return min(tile * ProbeSpacing + ProbeSpacing / 2u, screenSize - 1u);
}

uint2 ProbeGridSize(uint2 screenSize)
{
    uint spacing = _SdfRestirDense != 0u ? RestirPixelSpacing : ProbeSpacing;
    return (screenSize + spacing - 1u) / spacing;
}

uint ProbeRayCount()
{
    return _SdfRestirDense != 0u ? 1u : RaysPerProbe;
}

bool LoadAdaptiveProbePixel(uint2 tile, out uint2 pixel)
{
    uint encoded = (uint)_SdfAdaptivePlacement.Load(int3(tile, 0));
    uint offset = encoded - 1u;
    pixel = tile * ProbeSpacing + uint2(offset % ProbeSpacing, offset / ProbeSpacing);
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
