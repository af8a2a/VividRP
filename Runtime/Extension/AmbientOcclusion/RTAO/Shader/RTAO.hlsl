// We need only need 1 bounce for AO
#pragma max_recursion_depth 1
// HDRP include
#pragma use_dxc
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"

#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/RayTracingFallbackHierarchy.cs.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingSampling.hlsl"
// The target acceleration structure that we will evaluate the reflexion in
TEXTURE2D_X(SceneDepth);
TEXTURE2D_X(SceneNormal);

// Output structure of the reflection raytrace shader
RW_TEXTURE2D(float, AmbientOcclusionTexture);
RW_TEXTURE2D(float, _VelocityBuffer);


#define NV_HITOBJECT_USE_MACRO_API
#define NV_SHADER_EXTN_SLOT u1
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Plugin/NVAPI_SER/nvHLSLExtns.h"
float _UseNVSER;

float radius;
float intensity;
int sampleCount;
int frameIndex;


float3x3 GetTangentBasis(float3 TangentZ)
{
    const float Sign = TangentZ.z >= 0 ? 1 : -1;
    const float a = -rcp(Sign + TangentZ.z);
    const float b = TangentZ.x * TangentZ.y * a;

    float3 TangentX = {1 + Sign * a * pow(TangentZ.x, 2), Sign * b, -Sign * TangentZ.x};
    float3 TangentY = {b, Sign + a * pow(TangentZ.y, 2), -TangentZ.y};

    return float3x3(TangentX, TangentY, TangentZ);
}


[shader("miss")]
void MissShaderAmbientOcclusion(inout RayIntersectionVisibility rayIntersection : SV_RayPayload)
{
    rayIntersection.color += float3(1.0f, 1.0f, 1.0f);
}

[shader("raygeneration")]
void RayGenAmbientOcclusion()
{
    uint3 LaunchIndex = DispatchRaysIndex();
    uint2 LaunchDim = DispatchRaysDimensions().xy;

    // Pixel coordinate of the current pixel
    uint2 currentPixelCoord = uint2(LaunchIndex.x, LaunchIndex.y);

    // Reset the value of this pixel
    AmbientOcclusionTexture[(currentPixelCoord)] = 0.0f;

    // Read the depth value
    float depthValue = LOAD_TEXTURE2D(SceneDepth, currentPixelCoord).r;
    // This point is part of the background or is unlit, we don't really care
    if (depthValue == UNITY_RAW_FAR_CLIP_VALUE)
        return;

    // Convert this to a world space position
    PositionInputs posInput = GetPositionInput(currentPixelCoord, 1.0 / LaunchDim.xy, depthValue, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);

    // Decode the world space normal

    // float3 normalWS = UnpackGBufferNormal(SceneNormal[currentPixelCoord]);
    half2 remappedOctNormalWS = half2(Unpack888ToFloat2(SceneNormal[currentPixelCoord])); // values between [ 0, +1]
    half2 octNormalWS = remappedOctNormalWS.xy * half(2.0) - half(1.0); // values between [-1, +1]
    float3 normalWS = half3(UnpackNormalOctQuadEncode(octNormalWS)); // values between [-1, +1]



    // the number of samples based on the roughness
    int numSamples = sampleCount;

    // Count the number of rays that we will be traced
    // if (_RayCountEnabled > 0)
    // {
    //     uint3 counterIdx = uint3(currentPixelCoord, INDEX_TEXTURE2D_ARRAY_X(RAYCOUNTVALUES_AMBIENT_OCCLUSION));
    //     _RayCountTexture[counterIdx] = _RayCountTexture[counterIdx] + (uint)numSamples;
    // }

    // Evaluate the ray bias
    float rayBias = EvaluateRayTracingBias(posInput.positionWS);

    // Variable that accumulate the radiance
    float finalColor = 0.0;
    float velocity = 0.0;
    RayDesc rayDescriptor;
    RayIntersectionVisibility rayIntersection;

    // Let's loop through th e samples
    for (int i = 0; i < numSamples; ++i)
    {
        // Compute the current sample index
        int globalSampleIndex = frameIndex * sampleCount + i;

        // Generate the new sample (follwing values of the sequence)
        float2 noiseValue;
        noiseValue.x = GetBNDSequenceSample(currentPixelCoord, globalSampleIndex, 0);
        noiseValue.y = GetBNDSequenceSample(currentPixelCoord, globalSampleIndex, 1);

        // Importance sample the direction



        float3 sampleDir = SampleHemisphereCosine(noiseValue.x, noiseValue.y, normalWS);

        // Create the ray descriptor for this pixel
        rayDescriptor.Origin = posInput.positionWS + normalWS * rayBias;
        rayDescriptor.Direction = sampleDir;
        rayDescriptor.TMin = 0;
        rayDescriptor.TMax = radius;


        // Create and init the RayIntersection structure for this
        rayIntersection.color = float3(0.0, 0.0, 0.0);
        rayIntersection.t = 0.0;
        rayIntersection.pixelCoord = posInput.positionSS;
        rayIntersection.velocity = 0.0;


        UNITY_BRANCH
        if (_UseNVSER)
        {
            //note SER is good choice in RTGI or else shade
            //but no use when only get visibility
            //this just for SER test....
            NvHitObject hitObject;

            NvTraceRayHitObject(_RaytracingAccelerationStructure,
                                RAY_FLAG_CULL_BACK_FACING_TRIANGLES| RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH,
                                RAYTRACINGRENDERERFLAG_AMBIENT_OCCLUSION, 0, 1, 0, rayDescriptor, rayIntersection, hitObject);

            NvReorderThread(hitObject);

            NvInvokeHitObject(_RaytracingAccelerationStructure, hitObject, rayIntersection);
        }
        else
        {
            // Evaluate the ray intersection
            TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH,
                     RAYTRACINGRENDERERFLAG_AMBIENT_OCCLUSION, 0, 1, 0, rayDescriptor, rayIntersection);
        }

        

        // Accumulate this value
        velocity = max(velocity, rayIntersection.velocity);
        finalColor += rayIntersection.color.x;
    }

    // Normalize the radiance
    finalColor /= (float)numSamples;
    // Alright we are done
    AmbientOcclusionTexture[(currentPixelCoord)] = finalColor;
    _VelocityBuffer[(currentPixelCoord)] = velocity;
}
