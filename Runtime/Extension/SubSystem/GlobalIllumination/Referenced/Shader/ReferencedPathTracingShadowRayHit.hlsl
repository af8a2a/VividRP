// Path Tracing Shadow Ray Hit Shaders
// Contains: ClosestHit and AnyHit shaders for shadow rays
// Used by material shaders (Lit.shader, etc.) for shadow ray testing in path tracing
// Note: MissShadow is in ReferencedPathTracingRayGen.hlsl (.raytrace file only)

#ifndef REFERENCED_PATH_TRACING_SHADOW_RAY_HIT_INCLUDED
#define REFERENCED_PATH_TRACING_SHADOW_RAY_HIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/LitInputPathTracing.hlsl"

// Include common payload definitions
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/PathTracingCommon.hlsl"

//--------------------------------------------------------------------------------------------------
// Shadow Ray Closest Hit Shader
//--------------------------------------------------------------------------------------------------

[shader("closesthit")]
void ClosestHitShadow(inout ShadowRayPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    // Any closest hit means the light is occluded
    payload.visibility = 0.0;
}

//--------------------------------------------------------------------------------------------------
// Shadow Ray Any Hit Shader - Alpha testing for shadow rays
//--------------------------------------------------------------------------------------------------

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

    // For shadow rays, any opaque hit means the light is occluded
    payload.visibility = 0.0;
    AcceptHitAndEndSearch();
}

// Note: MissShadow is defined in ReferencedPathTracingRayGen.hlsl (included by .raytrace file only)
// Miss shaders can only be compiled in .raytrace files, not in regular .shader files

#endif // REFERENCED_PATH_TRACING_SHADOW_RAY_HIT_INCLUDED
