#ifndef VIVID_BLOOM_COMMON
#define VIVID_BLOOM_COMMON

// Quadratic color thresholding with a soft knee.
// curve = (threshold - knee, knee * 2, 0.25 / knee)
float3 QuadraticThreshold(float3 color, float threshold, float3 curve)
{
    float br = Max3(color.r, color.g, color.b);

    float rq = clamp(br - curve.x, 0.0, curve.y);
    rq = curve.z * rq * rq;

    color *= max(rq, br - threshold) / max(br, 1e-4);
    return color;
}

#endif // VIVID_BLOOM_COMMON
