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

// Upper bound over every parent sphere inside the symmetric object-space box.
// Retain near-plane/nonfinite ranges; final cluster tests resolve equality.
float VividLODRangeProjectedError(float3 magnitude, float maxError, float4x4 localToClip, float2 screenSize)
{
    if (!all(isfinite(magnitude)) || !isfinite(maxError) || maxError < 0.0f)
        return VIVID_LOD_UNBOUNDED_ERROR;
    float wExtent = dot(abs(localToClip[3].xyz), magnitude);
    float margin = 16e-6f * (abs(localToClip[3].w) + wExtent) + 1e-30f;
    float nearestW = localToClip[3].w - wExtent - margin;
    if (!isfinite(nearestW) || nearestW <= 0.0f) return VIVID_LOD_UNBOUNDED_ERROR;
    float2 xyScale = float2(VividLODScaledLength(localToClip[0].xyz), VividLODScaledLength(localToClip[1].xyz));
    float2 maxXY = float2(abs(localToClip[0].w) + dot(abs(localToClip[0].xyz), magnitude),
        abs(localToClip[1].w) + dot(abs(localToClip[1].xyz), magnitude));
    float2 scale = (xyScale + maxXY / nearestW * VividLODScaledLength(localToClip[3].xyz))
        * (0.5f * screenSize) / nearestW;
    float pixels = maxError * max(scale.x, scale.y);
    return isfinite(pixels) ? pixels : VIVID_LOD_UNBOUNDED_ERROR;
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
