// Referenced Path Tracing for Multi-Bounce Global Illumination
// Uses WorldLightCluster for light queries at arbitrary world positions

#ifndef REFERENCED_PATH_TRACING_INCLUDED
#define REFERENCED_PATH_TRACING_INCLUDED

#define SHADER_TARGET 50

//--------------------------------------------------------------------------------------------------
// Includes
//--------------------------------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/ml.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/BlueNoise.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Environment/Environment.hlsl"

// Ray tracing common
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/ShaderVariablesRaytracing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RaytracingIntersection.hlsl"

// World light cluster for light queries
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/WorldLightCluster.hlsl"

// NVIDIA SER support
#define NV_HITOBJECT_USE_MACRO_API
#define NV_SHADER_EXTN_SLOT u1
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/ExtensionSystem/NVAPI_SER/nvHLSLExtns.h"

//--------------------------------------------------------------------------------------------------
// Constants
//--------------------------------------------------------------------------------------------------

#define MAX_PATH_DEPTH 4
#define MIN_PATH_ROUGHNESS 0.001
#define MAX_PATH_ROUGHNESS 0.999
#define FIREFLY_CLAMP_THRESHOLD 10.0

//--------------------------------------------------------------------------------------------------
// Shader Resources
//--------------------------------------------------------------------------------------------------
TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);

// Output
RW_TEXTURE2D(float4, _PathTracingOutput);

// Path tracing specific parameters (not in ShaderVariablesRaytracing CB)
int _PathTracingAccumulate;
float _PathTracingIntensity;
float _PathTracingEnvironmentIntensity;
int _PathTracingIncludeEmissive;
int _PathTracingIncludeDirectLighting;
int _PathTracingDebugVisualizeBounce;

//--------------------------------------------------------------------------------------------------
// Ray Payload
//--------------------------------------------------------------------------------------------------

struct PathTracingPayload
{
    float3 radiance; // Accumulated radiance
    float3 throughput; // Path throughput (attenuation)
    float3 origin; // Next ray origin
    float3 direction; // Next ray direction
    int bounceCount; // Current bounce count
    bool active; // Is path still active
    float pdf; // PDF of current bounce
    uint randomSeed; // Random seed for this path
};

//--------------------------------------------------------------------------------------------------
// Random Number Generator (PCG)
//--------------------------------------------------------------------------------------------------

uint PCGHash(uint seed)
{
    uint state = seed * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float RandomFloat(inout uint seed)
{
    seed = PCGHash(seed);
    return float(seed) / 4294967296.0;
}

float2 RandomFloat2(inout uint seed)
{
    return float2(RandomFloat(seed), RandomFloat(seed));
}

//--------------------------------------------------------------------------------------------------
// BRDF Sampling
//--------------------------------------------------------------------------------------------------

// Cosine-weighted hemisphere sampling
float3 SampleCosineHemisphere(float2 u, float3 normal)
{
    float phi = 2.0 * PI * u.x;
    float cosTheta = sqrt(u.y);
    float sinTheta = sqrt(1.0 - u.y);

    float3 tangent, bitangent;
    if (abs(normal.y) < 0.999)
    {
        tangent = normalize(cross(normal, float3(0, 1, 0)));
    }
    else
    {
        tangent = normalize(cross(normal, float3(1, 0, 0)));
    }
    bitangent = cross(normal, tangent);

    return normalize(
        sinTheta * cos(phi) * tangent +
        sinTheta * sin(phi) * bitangent +
        cosTheta * normal
    );
}

// GGX importance sampling for specular
float3 SampleGGXDirection(float2 u, float3 normal, float3 viewDir, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;

    float phi = 2.0 * PI * u.x;
    float cosTheta = sqrt((1.0 - u.y) / (1.0 + (a2 - 1.0) * u.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);

    // Half vector in tangent space
    float3 h = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);

    // Transform to world space
    float3 tangent, bitangent;
    if (abs(normal.y) < 0.999)
    {
        tangent = normalize(cross(normal, float3(0, 1, 0)));
    }
    else
    {
        tangent = normalize(cross(normal, float3(1, 0, 0)));
    }
    bitangent = cross(normal, tangent);

    float3 halfVector = normalize(h.x * tangent + h.y * bitangent + h.z * normal);

    // Reflect view direction around half vector
    return reflect(-viewDir, halfVector);
}

//--------------------------------------------------------------------------------------------------
// Direct Lighting from World Light Cluster
//--------------------------------------------------------------------------------------------------

float3 EvaluateDirectLighting(float3 positionWS, float3 normalWS, float3 viewDirWS,
                              float3 albedo, float metallic, float roughness, inout uint randomSeed)
{
    float3 directLight = float3(0, 0, 0);

    // Query lights from world light cluster
    WorldLightIterator iter = WorldLightIteratorInit(positionWS);
    uint lightIdx;

    while (WorldLightIteratorNext(iter, lightIdx))
    {
        GPULightData light = GetWorldLight(lightIdx);

        if (!IsInLightRange(positionWS, light))
            continue;

        // Light direction and distance
        float3 lightVector = light.positionWS - positionWS;
        float distanceSq = max(dot(lightVector, lightVector), 0.0001);
        float3 lightDir = normalize(lightVector);

        // Check visibility (simple dot product test)
        float NdotL = dot(normalWS, lightDir);
        if (NdotL <= 0.0)
            continue;

        // TODO: Shadow ray tracing
        // For now, assume visible

        // Evaluate BRDF
        float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
        float NdotV = saturate(dot(normalWS, viewDirWS));
        float3 H = normalize(viewDirWS + lightDir);
        float NdotH = saturate(dot(normalWS, H));
        float LdotH = saturate(dot(lightDir, H));

        // Cook-Torrance BRDF
        float3 F = F_Schlick(F0, LdotH);
        float D = D_GGX(NdotH, roughness);
        float G = V_SmithJointGGX(NdotL, NdotV, roughness);

        float3 specular = F * D * G;
        float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;

        // Attenuation
        float attenuation = GetWorldLightAttenuation(positionWS, light);

        // Accumulate
        directLight += (diffuse + specular) * light.color * attenuation * NdotL;
    }

    return directLight;
}

//--------------------------------------------------------------------------------------------------
// Path Tracing Shaders
//--------------------------------------------------------------------------------------------------

[shader("miss")]
void MissShaderPathTracing(inout PathTracingPayload payload : SV_RayPayload)
{
    // Sample environment
    float3 rayDirection = WorldRayDirection();

    // Sky
    float3 skyColor = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, rayDirection, 0).xyz;

    // Add to radiance
    payload.radiance += payload.throughput * skyColor;

    // Terminate path
    payload.active = false;
}

[shader("raygeneration")]
void RayGenPathTracing()
{
    // Get pixel coordinate
    uint2 launchIndex = DispatchRaysIndex().xy;
    float2 pixelCoord = float2(launchIndex) + 0.5;

    // Load depth
    float rawDepth = LoadSceneDepth(pixelCoord);

    // Background pixels - sample sky directly
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
    {
        float2 uv = pixelCoord * _ScreenSize.zw;
        float3 viewDir = normalize(mul(UNITY_MATRIX_I_VP, float4(uv * 2.0 - 1.0, 0.0, 1.0)).xyz);
        float3 skyColor = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, viewDir, 0).xyz;
        _PathTracingOutput[launchIndex] = float4(skyColor, 1.0);
        return;
    }

    // Get position and view direction
    PositionInputs posInput = GetPositionInput(pixelCoord, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float3 positionWS = posInput.positionWS;
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

    // Load GBuffer data
    float4 gbuffer0 = LOAD_TEXTURE2D_X(_GBuffer0, launchIndex);
    float4 gbuffer1 = LOAD_TEXTURE2D_X(_GBuffer1, launchIndex);
    float4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, launchIndex);

    float3 albedo = gbuffer0.rgb;
    float3 normalWS = normalize(UnpackNormal(gbuffer2.xyz));
    float metallic = gbuffer1.r;
    float smoothness = gbuffer2.a;
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    float roughness = clamp(PerceptualRoughnessToRoughness(perceptualRoughness), MIN_PATH_ROUGHNESS, MAX_PATH_ROUGHNESS);

    // Initialize random seed (using frame index from CB)
    Rng::Hash::Initialize(launchIndex, _RaytracingSampleIndex);

    uint randomSeed = Rng::Hash::GetFloat();

    // Accumulate radiance over multiple samples
    float3 accumulatedRadiance = float3(0, 0, 0);

    for (int sampleIdx = 0; sampleIdx < _RaytracingNumSamples; sampleIdx++)
    {
        // Initialize path payload
        PathTracingPayload payload;
        payload.radiance = float3(0, 0, 0);
        payload.throughput = float3(1, 1, 1);
        payload.origin = positionWS;
        payload.direction = viewDirWS;
        payload.bounceCount = 0;
        payload.active = true;
        payload.pdf = 1.0;
        payload.randomSeed = randomSeed;

        // Add direct lighting at camera hit point
        payload.radiance += EvaluateDirectLighting(positionWS, normalWS, viewDirWS, albedo, metallic, roughness, randomSeed);

        // Path tracing bounces (using max recursion from CB)
        for (int bounce = 0; bounce < _RaytracingMaxRecursion; bounce++)
        {
            if (!payload.active)
                break;

            // Russian roulette for path termination
            float survivalProbability = max(payload.throughput.r, max(payload.throughput.g, payload.throughput.b));
            if (survivalProbability < 0.1 && bounce > 2)
            {
                if (RandomFloat(randomSeed) > survivalProbability)
                    break;
                payload.throughput /= survivalProbability;
            }

            // Sample next direction
            float2 u = RandomFloat2(randomSeed);

            // Choose between diffuse and specular based on material
            float specularProbability = lerp(0.1, 0.9, metallic);
            bool sampleSpecular = RandomFloat(randomSeed) < specularProbability;

            float3 sampledDir;
            if (sampleSpecular)
            {
                sampledDir = SampleGGXDirection(u, normalWS, viewDirWS, roughness);
            }
            else
            {
                sampledDir = SampleCosineHemisphere(u, normalWS);
            }

            // Evaluate ray bias
            float rayBias = EvaluateRayTracingBias(payload.origin);

            // Trace ray
            RayDesc rayDesc;
            rayDesc.Origin = payload.origin + normalWS * rayBias;
            rayDesc.Direction = sampledDir;
            rayDesc.TMin = 0.001;
            rayDesc.TMax = _RaytracingRayMaxLength;

            RayIntersection rayIntersection;
            rayIntersection.color = float3(0, 0, 0);
            rayIntersection.t = -1.0;
            rayIntersection.remainingDepth = _RaytracingMaxRecursion - bounce - 1;
            rayIntersection.pixelCoord = pixelCoord;

            // NVIDIA SER: Shader Execution Reordering for improved coherence (using flag from CB)
            UNITY_BRANCH
            if (_nvSER)
            {
                // SER is extremely useful in path tracing for coherent ray execution
                NvHitObject hitObject;

                NvTraceRayHitObject(_RaytracingAccelerationStructure,
                                    RAY_FLAG_CULL_BACK_FACING_TRIANGLES,
                                    RAYTRACINGRENDERERFLAG_PATH_TRACING,
                                    0, 1, 0,
                                    rayDesc,
                                    rayIntersection,
                                    hitObject);

                // Reorder threads for better coherence
                NvReorderThread(hitObject);

                // Invoke the hit shader
                NvInvokeHitObject(_RaytracingAccelerationStructure, hitObject, rayIntersection);
            }
            else
            {
                // Standard ray tracing without SER
                TraceRay(_RaytracingAccelerationStructure,
                         RAY_FLAG_CULL_BACK_FACING_TRIANGLES,
                         RAYTRACINGRENDERERFLAG_PATH_TRACING,
                         0, 1, 0,
                         rayDesc,
                         rayIntersection);
            }

            // If hit, evaluate lighting at hit point
            if (rayIntersection.t > 0.0 && rayIntersection.t < _RaytracingRayMaxLength)
            {
                // TODO: Get hit surface properties and evaluate direct lighting
                // For now, use intersection color
                payload.radiance += payload.throughput * rayIntersection.color;
            }
            else
            {
                // Hit sky
                float3 skyColor = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, sampledDir, 0).xyz;
                payload.radiance += payload.throughput * skyColor;
                break;
            }

            // Update throughput (simplified)
            payload.throughput *= albedo;

            // Firefly clamping (using intensity clamp from CB)
            if (_RaytracingIntensityClamp > 0.0)
            {
                payload.radiance = min(payload.radiance, _RaytracingIntensityClamp);
            }
        }

        accumulatedRadiance += payload.radiance;
    }

    // Average over samples
    accumulatedRadiance /= max(_RaytracingNumSamples, 1);

    // Output
    _PathTracingOutput[launchIndex] = float4(accumulatedRadiance, 1.0);
}

#endif // REFERENCED_PATH_TRACING_INCLUDED
