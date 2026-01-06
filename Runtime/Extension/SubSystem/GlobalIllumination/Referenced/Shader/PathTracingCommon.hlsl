// Path Tracing Common Definitions
// Shared structs and constants used by both closesthit shaders and raygen shader

#ifndef PATH_TRACING_COMMON_INCLUDED
#define PATH_TRACING_COMMON_INCLUDED

//--------------------------------------------------------------------------------------------------
// Constants
//--------------------------------------------------------------------------------------------------

#define PT_MIN_ROUGHNESS           0.04    // Minimum roughness to avoid fireflies
#define PT_MAX_RADIANCE            10.0    // Maximum radiance per bounce
#define PT_THROUGHPUT_THRESHOLD    0.001   // Terminate paths with very low throughput
#define PT_BOUNCES_MIN             3       // Minimum bounces before Russian roulette

//--------------------------------------------------------------------------------------------------
// Ray Type Flags - Used to distinguish ray types in the uber payload
//--------------------------------------------------------------------------------------------------

#define PT_RAY_TYPE_PATH_TRACING   0       // Full path tracing ray (needs material data)
#define PT_RAY_TYPE_SHADOW         1       // Shadow ray (only needs visibility)

//--------------------------------------------------------------------------------------------------
// Path Tracing Payload - Uber payload for all ray types (DXR 1.0 compatible)
// Carries material data from closesthit to raygen for path tracing rays
// For shadow rays, only visibility is used
//--------------------------------------------------------------------------------------------------

struct PathTracingPayload
{
    // Ray type indicator (set before tracing, read in closesthit)
    uint rayType;               // PT_RAY_TYPE_PATH_TRACING or PT_RAY_TYPE_SHADOW

    // Hit information
    float hitDistance;          // >0 if hit, <0 if miss

    // Shadow ray result
    float visibility;           // 1.0 = visible (miss), 0.0 = occluded (hit)

    // Material data (only valid for PT_RAY_TYPE_PATH_TRACING)
    float3 albedo;
    float3 normalWS;
    float3 emission;
    float metallic;
    float roughness;
    float occlusion;

    // Hit position for next bounce
    float3 hitPositionWS;

    // Helper methods
    bool Hit() { return hitDistance > 0.0f; }
    bool IsShadowRay() { return rayType == PT_RAY_TYPE_SHADOW; }
    bool IsPathTracingRay() { return rayType == PT_RAY_TYPE_PATH_TRACING; }
};

//--------------------------------------------------------------------------------------------------
// Path Accumulator - Tracks diffuse/specular contributions separately for NRD denoising
// This structure accumulates radiance and hit distances for both lobes
//--------------------------------------------------------------------------------------------------

struct PathAccumulator
{
    // Diffuse lobe accumulation
    float3 diffuseRadiance;     // Accumulated diffuse radiance
    float diffuseHitDist;       // Hit distance for diffuse (sum of all bounces after first)
    float diffuseWeight;        // Weight for diffuse contribution

    // Specular lobe accumulation
    float3 specularRadiance;    // Accumulated specular radiance
    float specularHitDist;      // Hit distance for specular (first bounce hit distance)
    float specularWeight;       // Weight for specular contribution

    // Combined (for backward compatibility)
    float3 combinedRadiance;    // Total radiance (diffuse + specular)

    // Path state
    bool isFirstBounce;         // Track if this is the first bounce (for hit distance)
    bool lastBounceWasSpecular; // Track lobe type for hit distance attribution
};

// Initialize path accumulator
PathAccumulator InitPathAccumulator()
{
    PathAccumulator acc;
    acc.diffuseRadiance = float3(0, 0, 0);
    acc.diffuseHitDist = 0.0;
    acc.diffuseWeight = 0.0;
    acc.specularRadiance = float3(0, 0, 0);
    acc.specularHitDist = 0.0;
    acc.specularWeight = 0.0;
    acc.combinedRadiance = float3(0, 0, 0);
    acc.isFirstBounce = true;
    acc.lastBounceWasSpecular = false;
    return acc;
}

// Add diffuse contribution
void AccumulateDiffuse(inout PathAccumulator acc, float3 radiance, float hitDist, float weight)
{
    acc.diffuseRadiance += radiance * weight;
    acc.diffuseHitDist += hitDist * weight;
    acc.diffuseWeight += weight;
    acc.combinedRadiance += radiance * weight;
}

// Add specular contribution
void AccumulateSpecular(inout PathAccumulator acc, float3 radiance, float hitDist, float weight)
{
    acc.specularRadiance += radiance * weight;
    // For specular, use minimum hit distance (first hit is most important)
    if (acc.specularWeight == 0.0 || hitDist < acc.specularHitDist)
    {
        acc.specularHitDist = hitDist;
    }
    acc.specularWeight += weight;
    acc.combinedRadiance += radiance * weight;
}

// Finalize accumulator - normalize by weights
void FinalizeAccumulator(inout PathAccumulator acc)
{
    if (acc.diffuseWeight > 0.0)
    {
        acc.diffuseRadiance /= acc.diffuseWeight;
        acc.diffuseHitDist /= acc.diffuseWeight;
    }
    if (acc.specularWeight > 0.0)
    {
        acc.specularRadiance /= acc.specularWeight;
        // specularHitDist is already the minimum, no normalization needed
    }
}

//--------------------------------------------------------------------------------------------------
// Payload Initialization Helpers
//--------------------------------------------------------------------------------------------------

// Initialize payload for path tracing ray
PathTracingPayload InitPathTracingPayload()
{
    PathTracingPayload payload;
    payload.rayType = PT_RAY_TYPE_PATH_TRACING;
    payload.hitDistance = -1.0f;
    payload.visibility = 1.0f;
    payload.albedo = float3(0, 0, 0);
    payload.normalWS = float3(0, 1, 0);
    payload.emission = float3(0, 0, 0);
    payload.metallic = 0.0;
    payload.roughness = 1.0;
    payload.occlusion = 1.0;
    payload.hitPositionWS = float3(0, 0, 0);
    return payload;
}

// Initialize payload for shadow ray
PathTracingPayload InitShadowPayload()
{
    PathTracingPayload payload;
    payload.rayType = PT_RAY_TYPE_SHADOW;
    payload.hitDistance = -1.0f;
    payload.visibility = 1.0f;  // Assume visible until hit
    payload.albedo = float3(0, 0, 0);
    payload.normalWS = float3(0, 1, 0);
    payload.emission = float3(0, 0, 0);
    payload.metallic = 0.0;
    payload.roughness = 1.0;
    payload.occlusion = 1.0;
    payload.hitPositionWS = float3(0, 0, 0);
    return payload;
}

//--------------------------------------------------------------------------------------------------
// Utility Functions
//--------------------------------------------------------------------------------------------------

bool IsFinite3_PT(float3 v)
{
    return isfinite(v.x) && isfinite(v.y) && isfinite(v.z);
}

float3 SanitizeRadiance_PT(float3 radiance, float maxValue)
{
    if (!IsFinite3_PT(radiance)) return float3(0, 0, 0);
    return clamp(radiance, 0.0, maxValue);
}

float3 SanitizeValue_PT(float3 v, float maxValue)
{
    if (!IsFinite3_PT(v)) return float3(0, 0, 0);
    return clamp(v, 0.0, maxValue);
}

#endif // PATH_TRACING_COMMON_INCLUDED
