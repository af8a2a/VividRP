#ifndef VIVID_MESH_LOD_ERROR_INCLUDED
#define VIVID_MESH_LOD_ERROR_INCLUDED

static const float VIVID_LOD_UNBOUNDED_ERROR = 3.402823466e+38f;

float VividLODScaledLength(float3 value)
{
    float scale = max(abs(value.x), max(abs(value.y), abs(value.z)));
    return scale > 0.0f ? length(value / scale) * scale : 0.0f;
}

float VividLODOrthographicPixelScale(float4x4 localToClip, float2 screenSize)
{
    return 0.5f * max(VividLODScaledLength(localToClip[0].xyz) * screenSize.x,
        VividLODScaledLength(localToClip[1].xyz) * screenSize.y);
}

float VividLODProjectedError(float4 sphereOS, float objectError, float4x4 localToClip, float2 screenSize)
{
    // meshoptimizer supplies a LINEAR object-space deviation. Like Nanite, compare
    // its projected pixel size with a pixel budget, not distance/radius squared.
    if (objectError == 0.0f) return 0.0f;
    if (!isfinite(objectError) || objectError < 0.0f) return VIVID_LOD_UNBOUNDED_ERROR;
    float4 centerCS = mul(localToClip, float4(sphereOS.xyz, 1.0f));
    float wScale = VividLODScaledLength(localToClip[3].xyz);
    float extentW = sphereOS.w * wScale;
    float errorW = 8e-6f * (dot(abs(localToClip[3].xyz), abs(sphereOS.xyz))
        + abs(localToClip[3].w) + extentW) + 1e-30f;
    float nearestW = centerCS.w - extentW - errorW;
    if (!all(isfinite(sphereOS)) || sphereOS.w < 0.0f || !all(isfinite(centerCS))
        || !isfinite(nearestW) || nearestW <= 0.0f) return VIVID_LOD_UNBOUNDED_ERROR;

    // Conservative bound on d(clip.xy / clip.w) throughout the shared sphere.
    // The W derivative also accounts for off-axis perspective displacement.
    // Orthographic W is constant: this reduces to error * pixels/object-unit.
    float2 xyScale = float2(VividLODScaledLength(localToClip[0].xyz),
        VividLODScaledLength(localToClip[1].xyz));
    float2 maxAbsNDC = (abs(centerCS.xy) + sphereOS.w * xyScale) / nearestW;
    float2 pixelScale = (xyScale + maxAbsNDC * wScale) * (0.5f * screenSize) / nearestW;
    float pixels = objectError * max(pixelScale.x, pixelScale.y);
    return isfinite(pixels) ? pixels : VIVID_LOD_UNBOUNDED_ERROR;
}

#endif
