// Definitions
//--------------------------------------------------------------------------------------------------

// Uncomment this pragma for debugging the HLSL code in PIX. GPU performance will be impacted.
//#pragma enable_ray_tracing_shader_debug_symbols

/*
 *  1. max_recursion_depth must positive > 0
 *  2. If TraceRay() inside a closest hit shader, must > 1
 */

// Always use this in DX12 RayTrace
#define SHADER_TARGET 50

//--------------------------------------------------------------------------------------------------
// Included headers
//--------------------------------------------------------------------------------------------------


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/BlueNoise.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Sampling.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Environment/Sky.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Raytracing/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Raytracing/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Raytracing/Shaders/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Raytracing/RayTracingFallbackHierarchy.cs.hlsl"

//--------------------------------------------------------------------------------------------------
// Inputs & outputs
//--------------------------------------------------------------------------------------------------

// Input
TEXTURE2D_X(_GBuffer2);
TEXTURE2D(_DispatchRayDirTexture);
RWStructuredBuffer<uint> _DispatchRayCoordBuffer;

// Output structure of the reflection raytrace shader
RW_TEXTURE2D(float4, _RayTracingLightingTextureRW);
RW_TEXTURE2D(float3, _SSRRayInfoTexture);

//--------------------------------------------------------------------------------------------------
// Helpers
//--------------------------------------------------------------------------------------------------

#define MIN_GGX_ROUGHNESS           0.00001f
#define MAX_GGX_ROUGHNESS           0.99999f

#define NEED_RAYTRACED_REFLECTIONS(perceptualRoughness) (perceptualRoughness < _SsrRoughnessFadeEnd)
#define IS_MIRROR_REFLECTIONS(perceptualRoughness) (perceptualRoughness < 0.01)

void GetNormalAndPerceptualRoughness(uint2 coordSS, out float3 normalWS, out float perceptualRoughness)
{
    // Load normal and perceptualRoughness.
    float4 normalGBuffer = LOAD_TEXTURE2D_X(_GBuffer2, coordSS);
    
    normalWS = normalize(UnpackNormal(normalGBuffer.xyz)); // normalize() is required because terrain shaders use additive blending for normals (not unit-length anymore)
    perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(normalGBuffer.a);
}

// Ray Tracing rays coord buffer
#define RAY_COORD_MASK (0xffffu)
#define RAY_COORD_SHIFT_X (0)
#define RAY_COORD_SHIFT_Y (16)

uint2 DecodeRayCoord(uint encoded)
{
    return uint2((encoded >> RAY_COORD_SHIFT_X) & RAY_COORD_MASK, (encoded >> RAY_COORD_SHIFT_Y) & RAY_COORD_MASK);
}

uint EncodeRayCoord(uint2 tileID)
{
    return (tileID.y << RAY_COORD_SHIFT_Y) | (tileID.x << RAY_COORD_SHIFT_X);
}

//--------------------------------------------------------------------------------------------------
// Implementation
//--------------------------------------------------------------------------------------------------

// Miss intersection
[shader("miss")]
void MissShaderReflections(inout RayIntersection rayIntersection : SV_RayPayload)
{
    float3 rayOrigin = WorldRayOrigin();
    float3 rayDirection = WorldRayDirection();

    float weight = 0.0f;

    // TODO: RayTracing Light Cluster for reflection probe.
    // if (RAYTRACINGFALLBACKHIERACHY_REFLECTION_PROBES & _RayTracingRayMissFallbackHierarchy)
    //     rayIntersection.color = RayTraceReflectionProbes(rayOrigin, rayDirection, weight);

    // if((RAYTRACINGFALLBACKHIERACHY_SKY & _RayTracingRayMissFallbackHierarchy) && weight < 1.0)
    {
        rayIntersection.color += SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, rayDirection, 0).xyz * (1.0 - weight);
        weight = 1.0f;
    }

    // TODO: Apply Atmospheric Fog.
    // if (weight > 0.0)
    //     ApplyFogAttenuation(rayOrigin, rayDirection, rayIntersection.color);



    rayIntersection.t = _RaytracingRayMaxLength;
}


[shader("raygeneration")]
void SingleRayGen()
{
    // InDirect Dispatch Rays
    uint launchIndex = DispatchRaysIndex().x;
    float2 coordSS = DecodeRayCoord(_DispatchRayCoordBuffer[launchIndex]);
    coordSS += 0.5f;


    // Load depth
    float rawDepth = LoadSceneDepth(coordSS);
    // Background, early out.
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return;

    // TODO: check stencil?


    PositionInputs posInput = GetPositionInput(coordSS, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float distanceToCamera = length(posInput.positionWS);
    float3 V = GetWorldSpaceNormalizeViewDir(posInput.positionWS);

    float3 N;
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(coordSS, N, perceptualRoughness);
    

    // TODO: check roughness


    // Evaluate the ray bias
    float rayBias = EvaluateRayTracingBias(posInput.positionWS);

    float3x3 localToWorld = GetLocalFrame(N);
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // roughness = lerp(roughness, 0.0f, _SsrPBRBias);
    roughness = clamp(roughness, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);


    float3 finalColor = float3(0.0, 0.0, 0.0);
    float rayDepth = 0.0;

    // Load ray dir from texture.
    float4 rayDirInfo = _DispatchRayDirTexture[coordSS];
    float3 R = rayDirInfo.xyz;
    float  ConeAngle = rayDirInfo.w;

    // Ray
    {
        // Create the ray descriptor for this pixel
        RayDesc rayDescriptor;
        rayDescriptor.Origin = posInput.positionWS + N * rayBias;
        rayDescriptor.Direction = R;
        rayDescriptor.TMin = 0.0;
        rayDescriptor.TMax = _RaytracingRayMaxLength;

        // Create and init the RayIntersection structure for this
        RayIntersection rayIntersection;
        rayIntersection.color = float3(0.0, 0.0, 0.0);
        rayIntersection.t = -1.0;
        rayIntersection.remainingDepth = 1;
        rayIntersection.sampleIndex = 0;
        rayIntersection.pixelCoord = coordSS;

        // In order to achieve filtering for the textures, we need to compute the spread angle of the pixel
        rayIntersection.cone.spreadAngle = _RaytracingPixelSpreadAngle + roughnessToSpreadAngle(roughness);
        rayIntersection.cone.width = distanceToCamera * _RaytracingPixelSpreadAngle;


        TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, RAYTRACINGRENDERERFLAG_REFLECTION, 0, 1, 0, rayDescriptor, rayIntersection);


        // TODO: Clamp result?
        // if (rayIntersection.t == _RaytracingRayMaxLength) // sky and rest different?
        float3 sampleColor = rayIntersection.color;

        // Contribute to the pixel
        finalColor += sampleColor;
        rayDepth = rayIntersection.t;
    }



    // TODO: reflection weight
    float weightValue = 1;

    float4 rayColorOpacity = _RayTracingLightingTextureRW[coordSS];

    finalColor = lerp(finalColor, rayColorOpacity.rgb, rayColorOpacity.a);
    _RayTracingLightingTextureRW[coordSS] = float4(finalColor, weightValue);

    _SSRRayInfoTexture[coordSS] = float3(max(ConeAngle, HALF_MIN), 1 / max(rayDepth, 0.0001f), 0);
}


[shader("raygeneration")]
void MainRayGenShader()
{
    // Direct Dispatch Rays
    // // Grab the dimensions of the current dispatch
    // uint2 launchIndex = DispatchRaysIndex().xy;
    // uint2 launchDim = DispatchRaysDimensions().xy;
    // // Compute the pixel coordinate
    // float2 coordSS = float2(launchIndex.x, launchDim.y - launchIndex.y - 1);

    // InDirect Dispatch Rays
    uint launchIndex = DispatchRaysIndex().x;
    float2 coordSS = DecodeRayCoord(_DispatchRayCoordBuffer[launchIndex]);
    coordSS += 0.5f;


    // Load depth
    float rawDepth = LoadSceneDepth(coordSS);
    // Background, early out.
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return;

    // TODO: check stencil?


    PositionInputs posInput = GetPositionInput(coordSS, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float3 V = GetWorldSpaceNormalizeViewDir(posInput.positionWS);

    float3 N;
    float perceptualRoughness;
    GetNormalAndPerceptualRoughness(coordSS, N, perceptualRoughness);
    

    // TODO: check roughness


    // Evaluate the ray bias
    float rayBias = EvaluateRayTracingBias(posInput.positionWS);

    float3x3 localToWorld = GetLocalFrame(N);
    float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // roughness = lerp(roughness, 0.0f, _SsrPBRBias);
    roughness = clamp(roughness, MIN_GGX_ROUGHNESS, MAX_GGX_ROUGHNESS);


    // one bounce, multiple bounces samplecCount.
    // int realSampleCount = _RaytracingMaxRecursion == 1 ? (perceptualRoughness < 0.01 ? 1 : _RaytracingNumSamples) : _RaytracingNumSamples;
    int realSampleCount = 1; // Current Only one bounce.

    float3 finalColor = float3(0.0, 0.0, 0.0);
    float averageDistance = 0.0;


    float ConeAngle = 0.0f;
    float3 R = reflect(-V, N); //default

    // Loop through the samples and add their contribution
    for (int sampleIndex = 0; sampleIndex < realSampleCount; ++sampleIndex)
    {

        float2 blueNoiseRandom = 0;
        blueNoiseRandom.xy = GetSpatiotemporalBlueNoiseVec2(coordSS);

        float NdotL, VdotH, NdotV;
        float3 localV, localH;
        SampleGGXVisibleNormalSphericalCaps(blueNoiseRandom, V, localToWorld, roughness, localV, localH, VdotH);
        float rayPDF = VisibleGGXPDF(localV, localH, roughness);
        float3 worldH = mul(localH, localToWorld);
        R = roughness == 0 ? R : reflect(-V, worldH);
        NdotL = dot(N, R);
        NdotV = dot(N, V);

        ConeAngle = 1.0f / max(rayPDF, 0.0001f);

        if (NdotL < 0.001f)
        {
            continue;
        }

        if (_RayCountEnabled > 0)
        {
            // uint3 counterIdx = uint3(currentCoord, INDEX_TEXTURE2D_ARRAY_X(RAYCOUNTVALUES_REFLECTION_FORWARD));
            // _RayCountTexture[counterIdx] = _RayCountTexture[counterIdx] + 1;
        }

        // Create the ray descriptor for this pixel
        RayDesc rayDescriptor;
        rayDescriptor.Origin = posInput.positionWS + N * rayBias;
        rayDescriptor.Direction = R;
        rayDescriptor.TMin = 0.0;
        rayDescriptor.TMax = _RaytracingRayMaxLength;

        // Create and init the RayIntersection structure for this
        RayIntersection rayIntersection;
        rayIntersection.color = float3(0.0, 0.0, 0.0);
        rayIntersection.t = -1.0;
        rayIntersection.remainingDepth = 1;
        rayIntersection.sampleIndex = 0;
        rayIntersection.pixelCoord = coordSS;

        // In order to achieve filtering for the textures, we need to compute the spread angle of the pixel
        // rayIntersection.cone.spreadAngle = _RaytracingPixelSpreadAngle + roughnessToSpreadAngle(roughness);
        // rayIntersection.cone.width = distanceToCamera * _RaytracingPixelSpreadAngle;


        TraceRay(_RaytracingAccelerationStructure, RAY_FLAG_CULL_BACK_FACING_TRIANGLES, RAYTRACINGRENDERERFLAG_REFLECTION, 0, 1, 0, rayDescriptor, rayIntersection);


        // TODO: Clamp result?
        // if (rayIntersection.t == _RaytracingRayMaxLength) // sky and rest different?
        float3 sampleColor = rayIntersection.color;

        // Contribute to the pixel
        finalColor += sampleColor;
        averageDistance += rayIntersection.t;

    }


    // Normalize the value
    if (realSampleCount != 0.0)
    {
        float normalizationFactor = 1.0 / realSampleCount;
        finalColor *= normalizationFactor;
        averageDistance *= normalizationFactor;
    }

    // TODO: reflection weight
    float weightValue = 1;


    _RayTracingLightingTextureRW[coordSS] = float4(finalColor, weightValue);
}