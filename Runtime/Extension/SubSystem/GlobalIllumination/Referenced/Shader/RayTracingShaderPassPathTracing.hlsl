#ifndef UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED
#define UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED

// ClosestHit shader for Path Tracing
// Evaluates material at hit point and returns data in payload (DXR 1.0 compatible)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/LitInputPathTracing.hlsl"

//--------------------------------------------------------------------------------------------------
// Constants
//--------------------------------------------------------------------------------------------------

#define MIN_ROUGHNESS       0.04
#define MAX_RADIANCE        10.0

//--------------------------------------------------------------------------------------------------
// Path Tracing Payload - carries material data back to raygen (DXR 1.0 compatible)
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

bool IsFinite3(float3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }

float3 SanitizeValue(float3 v, float maxValue)
{
    if (!IsFinite3(v)) return float3(0, 0, 0);
    return clamp(v, 0.0, maxValue);
}

//--------------------------------------------------------------------------------------------------
// Closest Hit Shader - Evaluates material and populates payload
//--------------------------------------------------------------------------------------------------

[shader("closesthit")]
void ClosestHitPathTracing(inout PathTracingPayload payload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Get hit distance
    float hitDistance = RayTCurrent();
    payload.hitDistance = hitDistance;

    // Compute hit position
    float3 hitPositionWS = WorldRayOrigin() + WorldRayDirection() * hitDistance;
    payload.hitPositionWS = hitPositionWS;

    // Build fragment inputs from hit (uses DXR intrinsics available in closesthit)
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

    // Calculate texture LOD based on ray distance
    float textureLOD = ComputeTextureLODFromDistance(hitDistance, 1.0);
    textureLOD = min(textureLOD, 8.0);

    // Sample surface data with LOD
    SurfaceData surfaceData;
    InitializeStandardLitSurfaceDataRT(fragInput.texCoord0, textureLOD, surfaceData);

    // Get normal
    float3 normalWS = normalize(fragInput.tangentToWorld[2]);
    #ifdef _NORMALMAP
        float3 normalTS = SampleNormalRT(fragInput.texCoord0, textureLOD, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
        normalWS = normalize(TransformTangentToWorld(normalTS, fragInput.tangentToWorld));
    #endif

    // Validate normal
    if (!IsFinite3(normalWS))
    {
        normalWS = normalize(fragInput.tangentToWorld[2]);
    }

    // Flip normal if back-facing
    float3 viewDir = -WorldRayDirection();
    if (dot(normalWS, viewDir) < 0.0)
    {
        normalWS = -normalWS;
    }

    // Extract and sanitize material properties
    payload.albedo = saturate(surfaceData.albedo);
    payload.normalWS = normalWS;
    payload.metallic = saturate(surfaceData.metallic);

    float smoothness = saturate(surfaceData.smoothness);
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    payload.roughness = max(PerceptualRoughnessToRoughness(perceptualRoughness), MIN_ROUGHNESS);

    payload.occlusion = saturate(surfaceData.occlusion);
    payload.emission = SanitizeValue(surfaceData.emission, MAX_RADIANCE);

    // Alpha testing
    #ifdef _ALPHATEST_ON
        if (surfaceData.alpha < surfaceData.alphaClipThreshold)
        {
            // This shouldn't happen if anyhit is working, but safety check
            payload.hitDistance = -1.0;
        }
    #endif
}

//--------------------------------------------------------------------------------------------------
// Any Hit Shader - Alpha testing and opaque handling
//--------------------------------------------------------------------------------------------------

[shader("anyhit")]
void AnyHitPathTracing(inout PathTracingPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    #ifdef _ALPHATEST_ON
        // Alpha-tested material: sample alpha and test
        IntersectionVertex currentVertex;
        FragInputs fragInput;
        GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

        SurfaceData surfaceData;
        InitializeStandardLitSurfaceDataRT(fragInput.texCoord0, 0.0, surfaceData);

        if (surfaceData.alpha < surfaceData.alphaClipThreshold)
        {
            IgnoreHit();  // Transparent pixel, continue ray
            return;
        }
        // Alpha test passed, accept this hit
    #endif

    // For opaque materials (no _ALPHATEST_ON) or alpha-tested materials that passed the test:
    // Accept this hit as valid geometry
    // Note: We don't call AcceptHitAndEndSearch() here because we want to find the closest hit
}

//--------------------------------------------------------------------------------------------------
// Shadow Ray Shaders
//--------------------------------------------------------------------------------------------------

[shader("closesthit")]
void ClosestHitShadow(inout ShadowRayPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    payload.visibility = 0.0;
}

[shader("anyhit")]
void AnyHitShadow(inout ShadowRayPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    #ifdef _ALPHATEST_ON
        // Alpha-tested material: sample alpha and test
        IntersectionVertex currentVertex;
        FragInputs fragInput;
        GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

        SurfaceData surfaceData;
        InitializeStandardLitSurfaceDataRT(fragInput.texCoord0, 0.0, surfaceData);

        if (surfaceData.alpha < surfaceData.alphaClipThreshold)
        {
            IgnoreHit();  // Transparent pixel, light can pass through
            return;
        }
        // Alpha test passed, this pixel is opaque and blocks light
    #endif

    // For shadow rays, any opaque hit (either opaque material or alpha-tested pixel that passed)
    // means the light is occluded. Set visibility to 0 and end search immediately.
    payload.visibility = 0.0;
    AcceptHitAndEndSearch();
}

[shader("miss")]
void MissShadow(inout ShadowRayPayload payload : SV_RayPayload)
{
    payload.visibility = 1.0;
}


#endif // UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED
