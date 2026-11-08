#ifndef VIVID_SDF_SURFACE_CACHE_INCLUDED
#define VIVID_SDF_SURFACE_CACHE_INCLUDED

#include "MeshSdfTrace.hlsl"

struct DiffuseSurface
{
    float4 albedoAndValid;
    float4 emission;
    float4 cardMinAndFirst;
    float4 cardExtent;
};

StructuredBuffer<DiffuseSurface> _SdfDiffuseSurfaces;

#if defined(VIVID_SDF_SURFACE_CACHE)
Texture2DArray<float4> _SdfSurfaceAlbedoDepth;
Texture2DArray<float4> _SdfSurfaceNormalCoverage;

bool VividTrySampleSdfSurface(VividMeshSdfHit hit, float3 positionWS, inout DiffuseSurface surface, out float3 normalWS)
{
    normalWS = hit.normalWS;
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
            float3 normal = SafeNormalize(packedNormal.xyz * 2.0 - 1.0);
            float agreement = dot(normal, hit.normalLS);
            if (agreement < 0.4)
                continue;
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
            }
        }
    }
    if (best == 2.0)
        return false;
    surface.albedoAndValid.rgb = bestAlbedo;
    normalWS = SafeNormalize(bestNormal.x * instance.worldToLocal0.xyz
        + bestNormal.y * instance.worldToLocal1.xyz + bestNormal.z * instance.worldToLocal2.xyz);
    return true;
}
#endif

#endif
