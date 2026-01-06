// Path Tracing Ray Hit Shaders (Unified)
// Contains: Single ClosestHit and AnyHit that handle both path tracing and shadow rays
// Used by material shaders (Lit.shader, etc.) for DXR 1.0 compatible ray tracing
// Note: Miss and RayGen shaders are in ReferencedPathTracingRayGen.hlsl (.raytrace file only)

#ifndef REFERENCED_PATH_TRACING_RAY_HIT_INCLUDED
#define REFERENCED_PATH_TRACING_RAY_HIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/LitInputPathTracing.hlsl"

// Include common payload definitions (uber payload)
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/PathTracingCommon.hlsl"

//--------------------------------------------------------------------------------------------------
// Unified Closest Hit Shader
// Handles both path tracing rays (full material evaluation) and shadow rays (visibility only)
//--------------------------------------------------------------------------------------------------

[shader("closesthit")]
void ClosestHitPathTracing(inout PathTracingPayload payload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Get hit distance
    float hitDistance = RayTCurrent();
    payload.hitDistance = hitDistance;

    // For shadow rays, we just need to mark as occluded
    if (payload.IsShadowRay())
    {
        payload.visibility = 0.0;
        return;
    }

    // Full material evaluation for path tracing rays
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
    if (!IsFinite3_PT(normalWS))
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
    payload.roughness = max(PerceptualRoughnessToRoughness(perceptualRoughness), PT_MIN_ROUGHNESS);

    payload.occlusion = saturate(surfaceData.occlusion);
    payload.emission = SanitizeValue_PT(surfaceData.emission, PT_MAX_RADIANCE);

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
// Unified Any Hit Shader
// Handles alpha testing for both path tracing and shadow rays
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
        // Alpha test passed, this pixel is opaque
    #endif

    // For shadow rays, any opaque hit means the light is occluded
    // End search immediately for better performance
    if (payload.IsShadowRay())
    {
        payload.visibility = 0.0;
        AcceptHitAndEndSearch();
        return;
    }

    // For path tracing rays, continue to find closest hit
    // (default behavior - no AcceptHitAndEndSearch)
}

#endif // REFERENCED_PATH_TRACING_RAY_HIT_INCLUDED
