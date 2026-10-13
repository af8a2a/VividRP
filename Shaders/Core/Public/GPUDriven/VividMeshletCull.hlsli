#ifndef VIVID_MESHLET_CULL_INCLUDED
#define VIVID_MESHLET_CULL_INCLUDED

#include "VividInstanceFrustumCull.hlsli"

VividInstanceCullBounds VividBuildMeshletCullBounds(float4 sphereOS, float4x4 objectToWorld)
{
    // Meshlet metadata stores a local sphere. Its enclosing local box remains
    // conservative under arbitrary affine transforms, including shear/reflection.
    // Do not form center +/- radius: large centers can round away a small radius.
    return VividBuildCullBoundsFromCenterExtent(sphereOS.xyz, sphereOS.www, objectToWorld);
}

bool VividMeshletConeVisible(float4 sphereOS, float3 coneAxis, float cutoff, bool coneValid,
    bool cullFrontFaces, bool perspective, float3 cameraWS, float3 forwardWS, float4x4 worldToObject)
{
    if (!coneValid || !(cutoff >= 0.0f && cutoff < 1.0f)
        || !all(isfinite(sphereOS)) || sphereOS.w < 0.0f || !all(isfinite(coneAxis))) return true;

    float3x3 inverseLinear = (float3x3)worldToObject;
    float3 inverseMaximum = max(abs(inverseLinear[0]), max(abs(inverseLinear[1]), abs(inverseLinear[2])));
    float inverseScale = max(inverseMaximum.x, max(inverseMaximum.y, inverseMaximum.z));
    if (!isfinite(inverseScale) || inverseScale <= 0.0f) return true;
    float inverseDeterminant = determinant(inverseLinear / inverseScale);
    if (!isfinite(inverseDeterminant) || inverseDeterminant == 0.0f
        || any(worldToObject[3] != float4(0, 0, 0, 1))) return true;

    // Keep rays and the cone in object space: transforming just the normal axis
    // does not preserve its opening angle under nonuniform scale or shear.
    float3 viewVector = perspective
        ? sphereOS.xyz - mul(worldToObject, float4(cameraWS, 1.0f)).xyz
        : mul(inverseLinear, forwardWS);
    float3 viewMagnitude = perspective
        ? abs(sphereOS.xyz) + mul(abs(worldToObject), float4(abs(cameraWS), 1.0f)).xyz
        : mul(abs(inverseLinear), abs(forwardWS));
    if (!all(isfinite(viewVector)) || !all(isfinite(viewMagnitude))) return true;
    float distance = VividInstanceScaledLength(viewVector);
    float axisLength = VividInstanceScaledLength(coneAxis);
    float viewError = 8e-6f * VividInstanceScaledLength(viewMagnitude) + 1e-30f;
    if (!isfinite(distance) || !isfinite(axisLength) || !isfinite(viewError)
        || distance <= viewError || axisLength <= 1e-20f) return true;

    // meshoptimizer's sphere-cone sufficient rejection condition is
    // dot(center-camera, axis) > cutoff * distance + radius. The extra terms
    // bound direction normalization and camera-transform roundoff. Inside-sphere
    // and tangent cases cannot reject, including orthographic zero directions.
    float threshold = cutoff + 2.0f * viewError / (distance - viewError);
    if (perspective) threshold += sphereOS.w / (distance - viewError);
    float16_t3 viewDirection = (float16_t3)(viewVector / distance);
    float16_t3 axis = (float16_t3)(coneAxis / axisLength * (cullFrontFaces ? -1.0f : 1.0f));
    float16_t facing = dot(viewDirection, axis);
    // Six bounded input conversions, three products, two sums, possible FP16
    // denormal flushing and FP32 normalization fit within 1/128 absolute error.
    return !(float(facing) > threshold + 0.0078125f);
}

bool VividBuildMeshletOcclusionQuery(VividInstanceCullBounds bounds, float4x4 viewProjection,
    float2 viewportSize, bool uvStartsAtTop,
    out uint2 minPixel, out uint2 maxPixel, out float closestDepth)
{
    minPixel = maxPixel = 0u;
    closestDepth = 0.0f;
    if (!bounds.Valid || !all(isfinite(viewportSize)) || any(viewportSize < 1.0f)) return false;

    // Like Nanite's BoxCullFrustum/HZB path, project the transformed local box.
    // Positions, clip W, screen rectangles and depth must remain FP32. Quantizing
    // them to half can move a silhouette inward or round the nearest depth away.
    float4 centerCS = mul(viewProjection, float4(bounds.CenterWS, 1.0f));
    float4 axisX = mul(viewProjection, float4(bounds.AxisX, 0.0f));
    float4 axisY = mul(viewProjection, float4(bounds.AxisY, 0.0f));
    float4 axisZ = mul(viewProjection, float4(bounds.AxisZ, 0.0f));
    float4 clipError = 8e-6f * mul(abs(viewProjection), float4(bounds.WorldMagnitude, 1.0f)) + 1e-30f;
    if (!all(isfinite(clipError))) return false;
    float2 minUv = 1.0f, maxUv = 0.0f;
    [unroll] for (uint corner = 0u; corner < 8u; ++corner)
    {
        float4 clip = centerCS + ((corner & 1u) ? axisX : -axisX)
            + ((corner & 2u) ? axisY : -axisY) + ((corner & 4u) ? axisZ : -axisZ);
        // Nanite skips HZB at near-plane intersections. Fail open at either clip
        // depth boundary (0 <= Z <= W on the supported D3D/Vulkan path), at W=0,
        // or when rounding could cross one. The target always uses reversed Z.
        if (!all(isfinite(clip)) || clip.w <= clipError.w
            || clip.z <= clipError.z || clip.w - clip.z <= clipError.w + clipError.z) return false;
        if (uvStartsAtTop) clip.y = -clip.y;
        float3 ndc = clip.xyz / clip.w;
        float3 ndcError = (clipError.xyz + abs(ndc) * clipError.w) / (clip.w - clipError.w)
            + 4e-6f * abs(ndc) + 1e-7f;
        if (!all(isfinite(ndc)) || !all(isfinite(ndcError))) return false;
        minUv = min(minUv, (ndc.xy - ndcError.xy) * 0.5f + 0.5f);
        maxUv = max(maxUv, (ndc.xy + ndcError.xy) * 0.5f + 0.5f);
        closestDepth = max(closestDepth, ndc.z + ndcError.z);
    }

    float2 expandedMin = minUv * viewportSize - 2.0f;
    float2 expandedMax = maxUv * viewportSize + 2.0f;
    // The first pass uses the previous view. Pixels outside that view have no
    // occlusion evidence; clamping to its border could hide newly exposed geometry.
    if (any(expandedMin < 0.0f) || any(expandedMax >= viewportSize)
        || any(expandedMax < expandedMin)) return false;
    minPixel = uint2(floor(expandedMin));
    maxPixel = uint2(floor(expandedMax));
    return true;
}

bool VividMeshletHZBFootprint(uint2 minPixel, uint2 maxPixel, uint2 textureSize, uint mipCount,
    out uint mipLevel, out uint2 minCoord, out uint2 maxCoord)
{
    mipLevel = 0u;
    [unroll] for (uint mip = 0u; mip < 16u; ++mip)
    {
        mipLevel = mip;
        if (all((maxPixel >> mip) - (minPixel >> mip) <= 1u)) break;
    }
    minCoord = minPixel >> mipLevel;
    maxCoord = maxPixel >> mipLevel;
    uint2 mipSize = max(1u, textureSize >> mipLevel);
    // Hardware mip chains truncate odd tails. Never clamp those queries back
    // onto the preceding texel: it does not bound the missing depth samples.
    return mipLevel < mipCount && all(maxPixel >= minPixel) && all(textureSize > 0u)
        && all(maxCoord < mipSize) && all(maxCoord - minCoord <= 1u);
}

bool VividMeshletVisibleAgainstHZB(float4 depths, float closestDepth, float bias)
{
    if (!all(isfinite(depths)) || any(depths < 0.0f) || any(depths > 1.0f)
        || !isfinite(closestDepth) || !isfinite(bias)) return true;
    bias = max(bias, 0.0f);
    // Equality is visible, as in Nanite's HZB test. This also preserves geometry
    // against a clear far-depth texel when saturation reaches zero.
    return saturate(closestDepth + bias) >= min(min(depths.x, depths.y), min(depths.z, depths.w));
}

#endif
