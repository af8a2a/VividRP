//--------------------------------------------------------------------------------------------------
// Included headers
//--------------------------------------------------------------------------------------------------
#pragma enable_ray_tracing_shader_debug_symbols
#pragma use_dxc
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"

#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/RayTracingFallbackHierarchy.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingSampling.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/ClusterLight.hlsl"
//--------------------------------------------------------------------------------------------------
// Inputs & outputs
//--------------------------------------------------------------------------------------------------
// Input
float radius;
int sampleCount;
int frameIndex;
// Output structure of the shadows raytrace shader
RW_TEXTURE2D(float2, _RayTracingShadowsTextureRW);

//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------


//--------------------------------------------------------------------------------------------------
// Implementation
//--------------------------------------------------------------------------------------------------

// Miss intersection
[shader("miss")]
void MissShaderShadows(inout RayIntersectionVisibility rayIntersection : SV_RayPayload)
{
    rayIntersection.color = float3(1.0, 1.0, 1.0);

    rayIntersection.t = _RaytracingRayMaxLength;
}

[shader("raygeneration")]
void SingleRayGen()
{
    // InDirect Dispatch Rays
    uint2 launchIndex = DispatchRaysIndex().xy;
    float2 coordSS = launchIndex;
    coordSS += 0.5f;

    // Load depth
    float rawDepth = LoadSceneDepth(coordSS);
    // Background, early out.
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return;


    // TODO: check stencil?
    // uint stencilVal = GetStencilValue(LOAD_TEXTURE2D_X(_StencilTexture, coordSS));

    PositionInputs posInput = GetPositionInput(coordSS, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float3 V = GetWorldSpaceNormalizeViewDir(posInput.positionWS);

    float3 normalWS = LoadSceneNormals(coordSS);

    // Evaluate the ray bias
    float rayBias = EvaluateRayTracingBias(posInput.positionWS);

    DirectionalLightData dirLight = g_DirectionalLightDatas[0];

    // TODO: Check this to different directional light.
    // Light dirLight = GetMainLight();

    // r: scene shadow, g: character selfshadow
    float visibility = 0;
    float rayDepth = 0.0;

    // Ray
    for (int i = 0; i < sampleCount; i++)
    {
        // float3 dir= SampleConeStrata(i,rcp(sampleCount),radius);

        float2 noiseValue;
        noiseValue.x = GetBNDSequenceSample(coordSS, frameIndex, 0);
        noiseValue.y = GetBNDSequenceSample(coordSS, frameIndex, 1);

        // Create the local ortho basis
        float3x3 localToWorld = GetLocalFrame(dirLight.dir);

        // We need to convert the diameter to a radius for our sampling
        float3 localDir = SampleConeUniform(noiseValue.x, noiseValue.y, cos(radius * 0.5));
        float3 wsDir = mul(localDir, localToWorld);

        // Create the ray descriptor for this pixel
        RayDesc rayDescriptor;
        rayDescriptor.Origin = posInput.positionWS + normalWS * rayBias * 1.5;
        rayDescriptor.Direction = wsDir;
        rayDescriptor.TMin = 0;
        rayDescriptor.TMax = _RaytracingRayMaxLength;

        // Create and init the RayIntersectionVisibility structure for this
        RayIntersectionVisibility rayIntersection;
        rayIntersection.color = float3(0.0,0.0, 0.0);
        rayIntersection.t = -1.0;
        rayIntersection.pixelCoord = coordSS;
        rayIntersection.velocity = 0;

        // First we cast scene shadow
        TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH,
                 RAYTRACINGRENDERERFLAG_CAST_SHADOW, 0, 1, 0, rayDescriptor, rayIntersection);
        float3 sceneShadowColor = rayIntersection.color;
        // Second we cast character shadow

        // Contribute to the pixel
        visibility += sceneShadowColor.x; //* rcp(sampleCount);
    }
    visibility /= max(1, sampleCount);
    // Combine scene shadow and character shadow for deferredlighting.

    _RayTracingShadowsTextureRW[coordSS] = visibility;
}
