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
// Path Tracing Payload - carries material data from closesthit to raygen (DXR 1.0 compatible)
//--------------------------------------------------------------------------------------------------

struct PathTracingPayload
{
    // Hit information
    float hitDistance;          // >0 if hit, <0 if miss

    // Material data (evaluated in closesthit, used in raygen)
    float3 albedo;
    float3 normalWS;
    float3 emission;
    float metallic;
    float roughness;
    float occlusion;

    // Hit position for next bounce
    float3 hitPositionWS;

    bool Hit() { return hitDistance > 0.0f; }
};

//--------------------------------------------------------------------------------------------------
// Shadow Ray Payload
//--------------------------------------------------------------------------------------------------

struct ShadowRayPayload
{
    float visibility;           // 1.0 = visible, 0.0 = shadowed
};

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
