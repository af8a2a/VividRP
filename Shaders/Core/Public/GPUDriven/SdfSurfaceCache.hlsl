#ifndef VIVID_SDF_SURFACE_CACHE_INCLUDED
#define VIVID_SDF_SURFACE_CACHE_INCLUDED

#include "MeshSdfTrace.hlsl"

struct DiffuseSurface
{
    float4 albedoAndValid;
    float4 emission;
    float4 cardMinAndFirst;
    float4 cardExtent; // w is the 1-based runtime lighting instance slot.
};

StructuredBuffer<DiffuseSurface> _SdfDiffuseSurfaces;

#if defined(VIVID_SDF_SURFACE_CACHE)
Texture2DArray<float4> _SdfSurfaceAlbedoDepth;
Texture2DArray<float4> _SdfSurfaceNormalCoverage;

static const uint VividSdfCardMissing = 1u;
static const uint VividSdfCardOutsideProjection = 2u;
static const uint VividSdfCardNoCoverage = 3u;
static const uint VividSdfCardNormalRejected = 4u;
static const uint VividSdfCardDepthRejected = 5u;

bool VividTrySampleSdfSurface(VividMeshSdfHit hit, float3 positionWS, inout DiffuseSurface surface,
    out float3 normalWS, out uint3 cardTexel, out float surfaceError, bool diagnose, out uint rejection)
{
    normalWS = hit.normalWS;
    cardTexel = 0u;
    surfaceError = 0.0;
    rejection = VividSdfCardMissing;
    if (surface.cardMinAndFirst.w == 0.0)
        return false;

    VividMeshSdfInstance instance = _MeshSdfInstances[hit.instanceIndex];
    VividMeshSdfField field = _MeshSdfFields[instance.fieldIndex];
    float4 p = float4(positionWS, 1.0);
    float3 local = float3(dot(instance.worldToLocal0, p), dot(instance.worldToLocal1, p), dot(instance.worldToLocal2, p));
    float3 relative = (local - surface.cardMinAndFirst.xyz) / surface.cardExtent.xyz;
    uint width, height, slices;
    _SdfSurfaceAlbedoDepth.GetDimensions(width, height, slices);
    float best = 2.0;
    float3 bestNormal = 0.0;
    float3 bestAlbedo = 0.0;
    rejection = VividSdfCardOutsideProjection;
    [loop]
    for (uint axis = 0u; axis < 3u; axis++)
    {
        float facing = hit.normalLS[axis];
        if (abs(facing) < 0.3)
            continue;
        uint2 tangent = axis == 0u ? uint2(1, 2) : axis == 1u ? uint2(0, 2) : uint2(0, 1);
        float2 uv = float2(relative[tangent.x], relative[tangent.y]);
        float2 texel = uv * float2(width, height) - 0.5;
        if (any(texel < -1.0) || any(texel >= float2(width, height)))
            continue;
        if (diagnose)
            rejection = max(rejection, VividSdfCardNoCoverage);
        int2 base = int2(floor(texel));
        uint slice = (uint)surface.cardMinAndFirst.w - 1u + axis * 2u + (facing > 0.0 ? 1u : 0u);
        // Match the original surface within the SDF sampling error. The extra
        // shell offset is intentional and is evaluated on the hit-facing card.
        float tolerance = field.originAndVoxelSize.w * 1.75 + field.shellThickness * 0.5;
        [unroll]
        for (uint sampleIndex = 0u; sampleIndex < 4u; sampleIndex++)
        {
            int2 pixel = clamp(base + int2(sampleIndex & 1u, sampleIndex >> 1u), 0, int2(width, height) - 1);
            float4 packedNormal = _SdfSurfaceNormalCoverage.Load(int4(pixel, slice, 0));
            if (packedNormal.a == 0.0)
                continue;
            if (diagnose)
                rejection = max(rejection, VividSdfCardNormalRejected);
            float3 normal = SafeNormalize(packedNormal.xyz * 2.0 - 1.0);
            float agreement = dot(normal, hit.normalLS);
            if (agreement < 0.4)
                continue;
            if (diagnose)
                rejection = max(rejection, VividSdfCardDepthRejected);
            float4 texelData = _SdfSurfaceAlbedoDepth.Load(int4(pixel, slice, 0));
            float depthError = abs(texelData.a - relative[axis]) * surface.cardExtent[axis];
            if (depthError > tolerance)
                continue;
            float score = depthError / tolerance + 0.25 * (1.0 - agreement)
                + 0.1 * length(float2(pixel) - texel) + 0.1 * (1.0 - abs(facing));
            if (score < best)
            {
                best = score;
                bestNormal = normal;
                bestAlbedo = texelData.rgb;
                cardTexel = uint3(pixel, slice - ((uint)surface.cardMinAndFirst.w - 1u));
                surfaceError = depthError;
            }
        }
    }
    if (best == 2.0)
        return false;
    rejection = 0u;
    surface.albedoAndValid.rgb = bestAlbedo;
    normalWS = SafeNormalize(bestNormal.x * instance.worldToLocal0.xyz
        + bestNormal.y * instance.worldToLocal1.xyz + bestNormal.z * instance.worldToLocal2.xyz);
    return true;
}

bool VividTrySampleSdfSurface(VividMeshSdfHit hit, float3 positionWS, inout DiffuseSurface surface,
    out float3 normalWS, out uint3 cardTexel, out float surfaceError)
{
    uint rejection;
    return VividTrySampleSdfSurface(hit, positionWS, surface, normalWS, cardTexel, surfaceError, false, rejection);
}

bool VividTrySampleSdfSurface(VividMeshSdfHit hit, float3 positionWS, inout DiffuseSurface surface, out float3 normalWS)
{
    uint3 cardTexel;
    float surfaceError;
    return VividTrySampleSdfSurface(hit, positionWS, surface, normalWS, cardTexel, surfaceError);
}
#endif

#if defined(VIVID_SDF_CACHED_SUN) || defined(VIVID_SDF_SUN_UPDATE)
// xy: baked card resolution, z: atlas columns. Each resident instance owns
// six consecutive cards in the same face order as the material capture.
float4 _SdfSunLayout;

uint2 VividSdfSunAtlasPixel(uint card, uint2 pixel)
{
    uint columns = (uint)_SdfSunLayout.z;
    return uint2(card % columns, card / columns) * uint2(_SdfSunLayout.xy) + pixel;
}
#endif

#endif
