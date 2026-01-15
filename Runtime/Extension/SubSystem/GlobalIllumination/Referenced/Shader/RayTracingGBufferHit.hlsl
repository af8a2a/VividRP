//------------------------------------------------------------------------------
// RayTracingGBufferHit.hlsl - DLSS-RR GBuffer Ray Hit Shaders
//------------------------------------------------------------------------------
// Contains ClosestHit, AnyHit, and Miss shaders for the raytracing GBuffer pass.
// Outputs DLSS-RR native format directly (world-space normals, sqrt(alphaRoughness),
// EnvBRDFApprox2 specular albedo).
//------------------------------------------------------------------------------

#ifndef RAYTRACING_GBUFFER_HIT_INCLUDED
#define RAYTRACING_GBUFFER_HIT_INCLUDED

// Core includes
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"

// Raytracing includes
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"

// Material sampling for ray tracing
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/LitInputPathTracing.hlsl"

// DLSS-RR GBuffer output helpers
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/RayTracingGBufferOutput.hlsl"

//------------------------------------------------------------------------------
// GBuffer Ray Payload
//------------------------------------------------------------------------------
struct GBufferPayload
{
    // DLSS-RR outputs
    float3 diffuseAlbedo;
    float3 specularAlbedo;
    float3 normalWS;
    float roughness;
    float hitDistance;
    uint hitType; // 0 = miss, 1 = hit

    // Path Tracing outputs (raw material data)
    float3 rawAlbedo;
    float metallic;

    // Emission for self-illumination
    float3 emission;
};

//------------------------------------------------------------------------------
// Closest Hit Shader - Evaluates material for GBuffer output
//------------------------------------------------------------------------------
[shader("closesthit")]
void GBufferClosestHit(inout GBufferPayload payload : SV_RayPayload, AttributeData attributeData : SV_IntersectionAttributes)
{
    // Get hit distance
    float hitDistance = RayTCurrent();
    payload.hitDistance = hitDistance;
    payload.hitType = 1;

    // Compute hit position
    float3 rayOrigin = WorldRayOrigin();
    float3 rayDir = WorldRayDirection();
    float3 hitPositionWS = rayOrigin + rayDir * hitDistance;

    // Build fragment inputs from hit
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

    // Calculate texture LOD based on ray distance
    float textureLOD = ComputeTextureLODFromDistance(hitDistance, 1.0);
    textureLOD = min(textureLOD, 8.0);

    // Sample surface data with LOD
    SurfaceData surfaceData;
    InitializeStandardLitSurfaceDataRT(fragInput.texCoord0.xy, textureLOD, surfaceData);

    // Get interpolated vertex normal
    float3 normalWS = normalize(fragInput.tangentToWorld[2]);

    // Apply normal map if enabled
    #ifdef _NORMALMAP
        float3 normalTS = SampleNormalRT(fragInput.texCoord0.xy, textureLOD,
                                         TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap),
                                         _BumpScale).xyz;
        normalWS = normalize(TransformTangentToWorld(normalTS, fragInput.tangentToWorld));
    #endif

    // Validate normal (handle potential NaN from degenerate triangles)
    if (any(isnan(normalWS)) || any(isinf(normalWS)))
    {
        normalWS = normalize(fragInput.tangentToWorld[2]);
    }

    // Flip normal if back-facing
    float3 viewDir = -rayDir;
    if (dot(normalWS, viewDir) < 0.0)
    {
        normalWS = -normalWS;
    }

    // Sanitize material properties
    float3 albedo = saturate(surfaceData.albedo);
    float metallic = saturate(surfaceData.metallic);
    float smoothness = saturate(surfaceData.smoothness);

    // Compute DLSS-RR roughness format: sqrt(alphaRoughness)
    float dlssRoughness = ComputeDLSSRRRoughness(smoothness);

    // Compute NoV for specular albedo
    float NoV = saturate(dot(normalWS, viewDir));

    // Compute DLSS-RR albedos
    float3 diffuseAlbedo, specularAlbedo;
    ComputeDLSSRRAlbedos(albedo, metallic, dlssRoughness, NoV, diffuseAlbedo, specularAlbedo);

    // Fill payload - DLSS-RR outputs
    payload.diffuseAlbedo = diffuseAlbedo;
    payload.specularAlbedo = specularAlbedo;
    payload.normalWS = normalWS;
    payload.roughness = dlssRoughness;

    // Fill payload - Path Tracing raw material data
    payload.rawAlbedo = albedo;
    payload.metallic = metallic;

    // Fill payload - Emission for self-illumination
    payload.emission = surfaceData.emission;
}

//------------------------------------------------------------------------------
// Any Hit Shader - Alpha testing
//------------------------------------------------------------------------------
[shader("anyhit")]
void GBufferAnyHit(inout GBufferPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    #ifdef _ALPHATEST_ON
        // Build fragment inputs for alpha testing
        IntersectionVertex currentVertex;
        FragInputs fragInput;
        GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);

        // Sample surface data (LOD 0 for alpha testing)
        SurfaceData surfaceData;
        InitializeStandardLitSurfaceDataRT(fragInput.texCoord0.xy, 0.0, surfaceData);

        // Alpha test
        if (surfaceData.alpha < surfaceData.alphaClipThreshold)
        {
            IgnoreHit();  // Transparent pixel, continue ray
        }
    #endif
}

#endif // RAYTRACING_GBUFFER_HIT_INCLUDED
