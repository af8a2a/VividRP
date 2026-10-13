#ifndef VIVID_MESH_LOD_ERROR_INCLUDED
#define VIVID_MESH_LOD_ERROR_INCLUDED

static const float VIVID_LOD_UNBOUNDED_RADIUS_SQ = 3.402823466e+38f;

float VividLODScaledLength(float3 value)
{
    float scale = max(abs(value.x), max(abs(value.y), abs(value.z)));
    return scale > 0.0f ? length(value / scale) * scale : 0.0f;
}

float VividLODProjectedSphereRadiusSq(float4 sphereOS, float4x4 localToClip, float2 screenSize)
{
    // Packed ParentBounds is recentered on the child and expanded to contain
    // the original parent sphere. The metric MUST be monotone under containment:
    // otherwise a parent can request refinement while its children reject it.
    // Work directly in local space; this also preserves shear/nonuniform scale.
    float4 centerCS = mul(localToClip, float4(sphereOS.xyz, 1.0f));
    float extentW = sphereOS.w * VividLODScaledLength(localToClip[3].xyz);
    float errorW = 8e-6f * (dot(abs(localToClip[3].xyz), abs(sphereOS.xyz))
        + abs(localToClip[3].w) + extentW) + 1e-30f;
    float nearestW = centerCS.w - extentW - errorW;
    if (!all(isfinite(sphereOS)) || sphereOS.w < 0.0f || !all(isfinite(centerCS))
        || !isfinite(nearestW) || nearestW <= 0.0f) return VIVID_LOD_UNBOUNDED_RADIUS_SQ;

    // An enclosing sphere has both a larger radius and a no-greater nearest W.
    // Unlike center +/- camera-right/up projection, this remains monotone when
    // a large LOD sphere intersects the eye plane. Such a sphere must refine.
    // Orthographic projection naturally has zero W extent and constant W.
    float pixelScale = 0.5f * max(VividLODScaledLength(localToClip[0].xyz) * screenSize.x,
        VividLODScaledLength(localToClip[1].xyz) * screenSize.y);
    float radiusPixels = sphereOS.w * pixelScale / nearestW;
    float radiusSq = radiusPixels * radiusPixels;
    // Keep the sentinel finite: leaf error is zero, and 0 * infinity is NaN.
    return isfinite(radiusSq) ? radiusSq : VIVID_LOD_UNBOUNDED_RADIUS_SQ;
}

#endif
