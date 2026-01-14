//------------------------------------------------------------------------------
// RayTracingGBuffer.raytrace - DLSS-RR GBuffer Ray Generation Shader
//------------------------------------------------------------------------------
// Entry point for raytracing-based GBuffer generation.
// Outputs DLSS-RR native format directly, eliminating the need for
// DLSSRRResourcePrep.compute transformation pass.
//------------------------------------------------------------------------------

#pragma max_recursion_depth 1

// Minimal requirements - no SHARC, no accumulation needed for GBuffer
#pragma require WaveBasicOps

//------------------------------------------------------------------------------
// Includes
//------------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Ray tracing common
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"

// DLSS-RR GBuffer output helpers
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/RayTracingGBufferOutput.hlsl"

//------------------------------------------------------------------------------
// Output UAVs - DXR GBuffer Format (Extended for Path Tracing + DLSS-RR)
//------------------------------------------------------------------------------

// Path Tracing outputs
RWTexture2D<float4> _GBufferMaterialData;      // RGB = raw albedo, A = metallic (for path tracing)

// DLSS-RR outputs
RWTexture2D<float4> _GBufferDiffuseAlbedo;     // RGB = diffuse albedo (albedo * (1-metallic)), A = unused
RWTexture2D<float4> _GBufferSpecularAlbedo;   // RGB = specular albedo (EnvBRDF), A = unused
RWTexture2D<float4> _GBufferNormalRoughness;  // RGB = world normal, A = sqrt(alphaRoughness)
RWTexture2D<float>  _GBufferHitDistance;      // Hit distance for DLSS-RR

//------------------------------------------------------------------------------
// Camera Parameters
//------------------------------------------------------------------------------

float3 _CameraPositionWS;

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
    uint hitType;  // 0 = miss, 1 = hit

    // Path Tracing outputs (raw material data)
    float3 rawAlbedo;
    float metallic;
};

//------------------------------------------------------------------------------
// Miss Shader - Sky/background
//------------------------------------------------------------------------------

[shader("miss")]
void GBufferMiss(inout GBufferPayload payload : SV_RayPayload)
{
    payload.hitType = 0;
    payload.hitDistance = 65504.0;  // FP16 max for sky

    // Default values for sky pixels (DLSS-RR)
    payload.diffuseAlbedo = float3(0, 0, 0);
    payload.specularAlbedo = float3(0, 0, 0);
    payload.normalWS = float3(0, 0, 1);  // Default normal
    payload.roughness = 1.0;

    // Default values for sky pixels (Path Tracing)
    payload.rawAlbedo = float3(0, 0, 0);
    payload.metallic = 0.0;
}

//------------------------------------------------------------------------------
// Ray Generation Shader - Primary Visibility for GBuffer
//------------------------------------------------------------------------------

[shader("raygeneration")]
void GBufferRayGeneration()
{
    uint2 dispatchIdx = DispatchRaysIndex().xy;
    uint2 dispatchDim = DispatchRaysDimensions().xy;

    // Compute normalized screen coordinates
    float2 uv = (float2(dispatchIdx) + 0.5) / float2(dispatchDim);

    float znear = _ProjectionParams.y;

   float3 rayOrigin = ComputeWorldSpacePosition(uv, _WorldSpaceCameraPos, UNITY_MATRIX_I_VP);
    float3 znearPositionWS = ComputeWorldSpacePosition(uv, znear, UNITY_MATRIX_I_VP);
    float3 raydir = normalize(znearPositionWS - rayOrigin);
    // Setup ray descriptor
    RayDesc ray;
    ray.Origin = rayOrigin;
    ray.Direction = raydir;
    ray.TMin = 0.001;
    ray.TMax = _RaytracingRayMaxLength;

    // Initialize payload
    GBufferPayload payload;
    payload.hitType = 0;
    payload.hitDistance = 65504.0;
    payload.diffuseAlbedo = float3(0, 0, 0);
    payload.specularAlbedo = float3(0, 0, 0);
    payload.normalWS = float3(0, 0, 1);
    payload.roughness = 1.0;
    payload.rawAlbedo = float3(0, 0, 0);
    payload.metallic = 0.0;

    // Trace primary visibility ray
    TraceRay(_RaytracingAccelerationStructure,
              RAY_FLAG_NONE,
             RAYTRACINGRENDERERFLAG_PATH_TRACING,  // Use same instance mask as path tracing
             0,    // Hit group index (GBufferDXR pass)
             1,    // Hit group stride
             0,    // Miss shader index
             ray,
             payload);

    // Write outputs
    // Path Tracing raw material data
    _GBufferMaterialData[dispatchIdx] = float4(payload.rawAlbedo, payload.metallic);

    // DLSS-RR native format
    _GBufferDiffuseAlbedo[dispatchIdx] = float4(payload.diffuseAlbedo, 1.0);
    _GBufferSpecularAlbedo[dispatchIdx] = float4(payload.specularAlbedo, 1.0);
    _GBufferNormalRoughness[dispatchIdx] = float4(payload.normalWS, payload.roughness);
    _GBufferHitDistance[dispatchIdx] = payload.hitType == 1 ? payload.hitDistance : 0.0;
}

// Entry points:
// - [shader("raygeneration")] GBufferRayGeneration()
// - [shader("miss")] GBufferMiss()
//
// ClosestHit and AnyHit shaders are in:
// - RayTracingGBufferHit.hlsl
// These are included by Lit.shader's GBufferDXR pass
