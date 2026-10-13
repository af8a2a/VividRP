#ifndef VIVID_INSTANCE_FRUSTUM_CULL_INCLUDED
#define VIVID_INSTANCE_FRUSTUM_CULL_INCLUDED

// Nanite's BoxCullFrustum tests the transformed local box, not a scaled sphere.
// With no screen rectangle/HZB needed here, its corner extrema are evaluated as
// plane support distances. This also handles shear and asymmetric projections.
struct VividInstanceCullBounds
{
    float3 CenterWS;
    float3 AxisX;
    float3 AxisY;
    float3 AxisZ;
    float3 WorldMagnitude;
    float AxisScale;
    float16_t3 UnitAxisX;
    float16_t3 UnitAxisY;
    float16_t3 UnitAxisZ;
    bool Valid;
};

VividInstanceCullBounds VividBuildCullBoundsFromCenterExtent(float3 center, float3 extent, float4x4 objectToWorld)
{
    VividInstanceCullBounds bounds;
    bounds.CenterWS = mul(objectToWorld, float4(center, 1.0f)).xyz;
    bounds.AxisX = float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20) * extent.x;
    bounds.AxisY = float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21) * extent.y;
    bounds.AxisZ = float3(objectToWorld._m02, objectToWorld._m12, objectToWorld._m22) * extent.z;
    bounds.WorldMagnitude = mul(abs((float3x3)objectToWorld), abs(center) + extent)
        + abs(float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23));
    float3 maximumAxis = max(abs(bounds.AxisX), max(abs(bounds.AxisY), abs(bounds.AxisZ)));
    bounds.AxisScale = max(1e-30f, max(maximumAxis.x, max(maximumAxis.y, maximumAxis.z)));
    bounds.Valid = all(isfinite(center)) && all(isfinite(extent)) && all(extent >= 0.0f)
        && all(isfinite(bounds.CenterWS)) && all(isfinite(bounds.WorldMagnitude))
        && isfinite(bounds.AxisScale) && bounds.AxisScale < 1e30f
        && all(objectToWorld[3] == float4(0, 0, 0, 1));
    // Cast only bounded values. World coordinates, translations and plane offsets
    // never enter FP16 (in particular, worlds larger than 65504 remain supported).
    // Use the same clamped scale for normalization and reconstruction, including
    // tiny/degenerate boxes where an unclamped reciprocal could overflow.
    float inverseScale = rcp(bounds.AxisScale);
    bounds.UnitAxisX = (float16_t3)(bounds.AxisX * inverseScale);
    bounds.UnitAxisY = (float16_t3)(bounds.AxisY * inverseScale);
    bounds.UnitAxisZ = (float16_t3)(bounds.AxisZ * inverseScale);
    return bounds;
}

VividInstanceCullBounds VividBuildInstanceCullBounds(float3 boundsMin, float3 boundsMax, float4x4 objectToWorld)
{
    // Separate half products avoid overflow in min + max / max - min.
    VividInstanceCullBounds bounds = VividBuildCullBoundsFromCenterExtent(boundsMin * 0.5f + boundsMax * 0.5f,
        boundsMax * 0.5f - boundsMin * 0.5f, objectToWorld);
    bounds.Valid = bounds.Valid && all(isfinite(boundsMin)) && all(isfinite(boundsMax)) && all(boundsMax >= boundsMin);
    return bounds;
}

bool VividInstanceOutsidePlane(VividInstanceCullBounds bounds, float4 plane)
{
    float normalMaximum = max(abs(plane.x), max(abs(plane.y), abs(plane.z)));
    // Disabled near planes are all zero. Invalid planes conservatively retain.
    if (!all(isfinite(plane)) || normalMaximum == 0.0f) return false;
    float distance = dot(plane.xyz, bounds.CenterWS) + plane.w;
    float allowance = 4e-6f * (dot(abs(plane.xyz), bounds.WorldMagnitude) + abs(plane.w)) + 1e-30f;
    float support;
    if (normalMaximum <= 1.0f)
    {
        float16_t3 normal = (float16_t3)plane.xyz;
        float16_t radius = abs(dot(normal, bounds.UnitAxisX))
            + abs(dot(normal, bounds.UnitAxisY)) + abs(dot(normal, bounds.UnitAxisZ));
        // All nine products have magnitude <= 1. Including both input conversions,
        // 3 dot products, the final sum and possible FP16 denormal flushing, their
        // absolute error is < 1/32 in this normalized domain. Ambiguous boxes stay.
        support = (float(radius) + 0.03125f) * bounds.AxisScale;
    }
    else
    {
        // Custom unnormalized planes retain the same geometric semantics.
        support = abs(dot(plane.xyz, bounds.AxisX)) + abs(dot(plane.xyz, bounds.AxisY))
            + abs(dot(plane.xyz, bounds.AxisZ));
    }
    return isfinite(distance) && isfinite(support) && isfinite(allowance)
        && distance < -support - allowance;
}

bool VividInstanceFrustumVisible(VividInstanceCullBounds bounds, float4 planes[6])
{
    if (!bounds.Valid) return true;
    [unroll] for (uint i = 0u; i < 6u; i++)
    {
        if (VividInstanceOutsidePlane(bounds, planes[i])) return false;
    }
    return true;
}

float VividInstanceScaledLength(float3 value)
{
    float scale = max(abs(value.x), max(abs(value.y), abs(value.z)));
    return scale > 0.0f ? length(value / scale) * scale : 0.0f;
}

float4 VividInstanceConservativeSphereWS(VividInstanceCullBounds bounds)
{
    // Only used by the existing light-sphere / non-affine VSM page tests. The
    // transformed box's enclosing AABB also encloses sheared and mirrored boxes.
    float radius = VividInstanceScaledLength(abs(bounds.AxisX) + abs(bounds.AxisY) + abs(bounds.AxisZ));
    radius += 4e-6f * VividInstanceScaledLength(bounds.WorldMagnitude) + 1e-30f;
    return float4(bounds.CenterWS, radius);
}

#endif
