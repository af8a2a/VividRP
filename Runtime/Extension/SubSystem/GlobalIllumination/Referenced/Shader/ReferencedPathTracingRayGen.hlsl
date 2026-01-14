// Referenced Path Tracing RayGen Shader
// Contains: RayGeneration shader, Miss shaders, and supporting functions
// This file should only be included by .raytrace files
// DXR 1.0 compatible - material evaluation in closesthit, path tracing loop in raygen

#ifndef REFERENCED_PATH_TRACING_RAYGEN_INCLUDED
#define REFERENCED_PATH_TRACING_RAYGEN_INCLUDED

#define SHADER_TARGET 50

//--------------------------------------------------------------------------------------------------
// SHARC Configuration
// SHARC_UPDATE and SHARC_QUERY are defined via multi_compile in .raytrace file
//--------------------------------------------------------------------------------------------------
#if __JETBRAINS_IDE__
#define SHARC_UPDATE 1
#define SHARC_QUERY 1
#endif
// Check if SHARC is enabled (either UPDATE or QUERY keyword is defined)
#if defined(SHARC_UPDATE) || defined(SHARC_QUERY)
    #define SHARC_ENABLE_SHARC              1
#else
    #define SHARC_ENABLE_SHARC              0
#endif

// SHARC compile-time settings (features that affect struct layouts)
#define SHARC_MATERIAL_DEMODULATION     1       // Enable material demodulation
#define SHARC_SEPARATE_EMISSIVE         1       // Handle emissive separately
#define SHARC_ENABLE_64_BIT_ATOMICS     1       // Use 64-bit atomics (SM 6.6+)

// SHARC settings that will be controlled via shader parameters from volume
// These defines provide defaults/fallbacks
#ifndef SHARC_PROPAGATION_DEPTH
#define SHARC_PROPAGATION_DEPTH         4       // Number of vertices stored for backpropagation
#endif

#ifndef SHARC_SAMPLE_NUM_THRESHOLD
#define SHARC_SAMPLE_NUM_THRESHOLD      2       // Minimum samples for valid cache entry
#endif

#ifndef SHARC_GRID_LOGARITHM_BASE
#define SHARC_GRID_LOGARITHM_BASE       2.0f
#endif

#ifndef SHARC_GRID_LEVEL_BIAS
#define SHARC_GRID_LEVEL_BIAS           0
#endif

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
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/RayTracingSystem/Shaders/RayTracingFragInputs.hlsl"

// World light cluster for light queries
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/WorldLightCluster.hlsl"

// Rectangle area light sampling for path tracing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/RectangleLightSampling.hlsl"

#include "LitInputPathTracing.hlsl"

// Include common payload definitions (shared with closesthit shaders)
#include "PathTracingCommon.hlsl"

// NVIDIA SER support
#define NV_HITOBJECT_USE_MACRO_API
#define NV_SHADER_EXTN_SLOT u1
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/ExtensionSystem/NVAPI_SER/nvHLSLExtns.h"

// SHARC (Spatially Hashed Radiance Cache)
#if SHARC_ENABLE_SHARC
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/SHARC/SharcCommon.hlsl"
#endif

//--------------------------------------------------------------------------------------------------
// Constants
//--------------------------------------------------------------------------------------------------

#define BOUNCES_MIN             3       // Minimum bounces before Russian roulette
#define MIN_ROUGHNESS           0.04    // Minimum roughness to avoid fireflies
#define THROUGHPUT_THRESHOLD    0.001   // Terminate paths with very low throughput
#define MAX_RADIANCE            10.0    // Maximum radiance per bounce

//--------------------------------------------------------------------------------------------------
// Shader Resources
//--------------------------------------------------------------------------------------------------

// DXR GBuffer inputs (from RayTracingGBufferPass)
TEXTURE2D_X(_DXRGBufferMaterialData);      // RGB = albedo, A = metallic
TEXTURE2D_X(_DXRGBufferNormalRoughness);   // RGB = world normal, A = sqrt(alphaRoughness)

// Output and History for temporal accumulation
RW_TEXTURE2D(float4, _PathTracingOutput);
TEXTURE2D_X(_PathTracingHistory);

// Separate diffuse/specular outputs for NRD denoising
// Format: RGB = radiance, A = normalized hit distance
RW_TEXTURE2D(float4, _PathTracingDiffuseOutput);
RW_TEXTURE2D(float4, _PathTracingSpecularOutput);
TEXTURE2D_X(_PathTracingDiffuseHistory);
TEXTURE2D_X(_PathTracingSpecularHistory);

// Material factors for NRD de-modulation/re-modulation
// Format: RGB = diffuse factor, A = specular luminance
RW_TEXTURE2D(float4, _PathTracingMaterialFactors);

// Path tracing specific parameters (not in ShaderVariablesRaytracing CB)
// Note: _PATH_TRACING_ACCUMULATE is now a multi_compile keyword, not a runtime parameter
float _PathTracingIntensity;
float _PathTracingEnvironmentIntensity;
int _PathTracingIncludeEmissive;
int _PathTracingIncludeDirectLighting;
int _PathTracingDebugVisualizeBounce;
int _PathTracingDebugMode;  // 0=Normal, 1=ShowFrameCount, 2=ShowNormals, 3=ShowAlbedo

// NRD parameters for hit distance normalization
float _NRDHitDistanceParams;  // Hit distance normalization parameter (view-z based)

//--------------------------------------------------------------------------------------------------
// SHARC Buffers and Parameters
//--------------------------------------------------------------------------------------------------

#if SHARC_ENABLE_SHARC
// SHARC buffers
RWStructuredBuffer<uint64_t>            _SharcHashEntriesBuffer     : register(u2);
RWStructuredBuffer<uint>                _SharcLockBuffer            : register(u3);
RWStructuredBuffer<SharcAccumulationData>   _SharcAccumulationBuffer    : register(u4);
RWStructuredBuffer<SharcPackedData>     _SharcResolvedBuffer        : register(u5);

// SHARC parameters from C# (volume-controlled)
float3 _SharcCameraPosition;
float _SharcSceneScale;
float _SharcRoughnessThreshold;
float _SharcRadianceScale;          // Quantization factor for atomic accumulation
int _SharcGridLevelBias;            // LOD bias for grid levels
int _SharcSampleThreshold;          // Minimum samples for valid cache entry
uint _SharcEntriesNum;
int _SharcEnableAntifirefly;
int _SharcDebug;

// Helper function to initialize SHARC parameters
SharcParameters GetSharcParameters()
{
    SharcParameters params;

    // Grid parameters
    params.gridParameters.cameraPosition = _SharcCameraPosition;
    params.gridParameters.sceneScale = _SharcSceneScale;
    params.gridParameters.logarithmBase = SHARC_GRID_LOGARITHM_BASE;
    params.gridParameters.levelBias = _SharcGridLevelBias;

    // Hash map data
    params.hashMapData.capacity = _SharcEntriesNum;
    params.hashMapData.hashEntriesBuffer = _SharcHashEntriesBuffer;

#if !SHARC_ENABLE_64_BIT_ATOMICS
    params.hashMapData.lockBuffer = _SharcLockBuffer;
#endif

    // Buffers
    params.accumulationBuffer = _SharcAccumulationBuffer;
    params.resolvedBuffer = _SharcResolvedBuffer;

    // Settings - use shader parameter instead of define
    params.radianceScale = _SharcRadianceScale;
    params.enableAntiFireflyFilter = _SharcEnableAntifirefly != 0;

    return params;
}

// Get sample threshold from shader parameter
int GetSharcSampleThreshold()
{
    return _SharcSampleThreshold;
}

// Helper function to create SharcHitData
SharcHitData CreateSharcHitData(float3 positionWS, float3 normalWS, float3 albedo, float metallic, float3 emission)
{
    SharcHitData hitData;
    hitData.positionWorld = positionWS;
    hitData.normalWorld = normalWS;

#if SHARC_MATERIAL_DEMODULATION
    // Demodulation preserves material details in the cache
    float3 specularF0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 specularFAvg = specularF0 + (1.0 - specularF0) * 0.047619; // 1/21
    float3 diffuseAlbedo = albedo * (1.0 - metallic);
    hitData.materialDemodulation = max(diffuseAlbedo, 0.05) + max(specularF0, 0.02) * Luminance(specularFAvg);
#endif

#if SHARC_SEPARATE_EMISSIVE
    hitData.emissive = emission;
#endif

    return hitData;
}
#endif // SHARC_ENABLE_SHARC

//--------------------------------------------------------------------------------------------------
// Random Number Generator (PCG)
//--------------------------------------------------------------------------------------------------

uint PCGHash(uint seed)
{
    uint state = seed * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float RandomFloat(inout uint rngState)
{
    rngState = PCGHash(rngState);
    return float(rngState) / 4294967296.0;
}

float2 RandomFloat2(inout uint rngState)
{
    return float2(RandomFloat(rngState), RandomFloat(rngState));
}

uint InitRNG(uint2 pixelCoord, uint frameIndex)
{
    return PCGHash(pixelCoord.x + pixelCoord.y * 16384 + frameIndex * 16384 * 16384);
}

//--------------------------------------------------------------------------------------------------
// Utility Functions
//--------------------------------------------------------------------------------------------------

bool IsFinite3(float3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }

float3 SanitizeRadiance(float3 radiance, float maxValue)
{
    if (!IsFinite3(radiance)) return float3(0, 0, 0);
    return clamp(radiance, 0.0, maxValue);
}

//--------------------------------------------------------------------------------------------------
// NRD Hit Distance Functions
// NRD expects normalized hit distances for temporal stability
//--------------------------------------------------------------------------------------------------

// Normalize hit distance for NRD (REBLUR front-end compatible)
// Uses view-z based normalization: hitDist / (hitDist + viewZ * A)
// where A is a scene-dependent constant (typically 0.1-1.0)
float NormalizeHitDistance(float hitDistance, float viewZ)
{
    // Simple linear normalization with view-z scaling
    // This helps maintain temporal stability across different depths
    float A = _NRDHitDistanceParams;
    return hitDistance / (hitDistance + abs(viewZ) * A + 0.0001);
}

// Denormalize hit distance (for debugging or if needed)
float DenormalizeHitDistance(float normalizedHitDist, float viewZ)
{
    float A = _NRDHitDistanceParams;
    float denom = 1.0 - normalizedHitDist;
    if (denom <= 0.0001) return 1e6; // Very large distance
    return normalizedHitDist * abs(viewZ) * A / denom;
}


//--------------------------------------------------------------------------------------------------
// BRDF Sampling Functions
//--------------------------------------------------------------------------------------------------

// Cosine-weighted hemisphere sampling for diffuse
float3 SampleCosineHemisphere(float2 u, float3 normal, out float pdf)
{
    float phi = 2.0 * PI * u.x;
    float cosTheta = sqrt(u.y);
    float sinTheta = sqrt(1.0 - u.y);

    float3 tangent, bitangent;
    if (abs(normal.y) < 0.999)
        tangent = normalize(cross(normal, float3(0, 1, 0)));
    else
        tangent = normalize(cross(normal, float3(1, 0, 0)));
    bitangent = cross(normal, tangent);

    float3 dir = normalize(sinTheta * cos(phi) * tangent +
                          sinTheta * sin(phi) * bitangent +
                          cosTheta * normal);

    pdf = cosTheta / PI;
    return dir;
}

// GGX importance sampling for specular
float3 SampleGGX(float2 u, float3 normal, float3 viewDir, float roughness, out float pdf, out float3 halfVector)
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
        tangent = normalize(cross(normal, float3(0, 1, 0)));
    else
        tangent = normalize(cross(normal, float3(1, 0, 0)));
    bitangent = cross(normal, tangent);

    halfVector = normalize(h.x * tangent + h.y * bitangent + h.z * normal);
    float3 dir = reflect(-viewDir, halfVector);

    // Compute PDF: p(h) = D(h) * NdotH, p(L) = p(h) / (4 * VdotH)
    float NdotH = saturate(dot(normal, halfVector));
    float VdotH = saturate(dot(viewDir, halfVector));
    float D = D_GGX(NdotH, roughness);
    pdf = (D * NdotH) / (4.0 * VdotH + 0.0001);

    return dir;
}

// Probability of selecting specular vs diffuse BRDF
float GetSpecularProbability(float3 albedo, float metallic, float roughness, float NdotV)
{
    // For perfect mirrors
    if (metallic >= 0.99 && roughness <= 0.01)
        return 1.0;

    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float fresnel = saturate(Luminance(F_Schlick(F0, NdotV)));
    float diffuseWeight = Luminance(albedo) * (1.0 - metallic) * (1.0 - fresnel);
    float specularWeight = fresnel;

    float probability = specularWeight / max(0.0001, specularWeight + diffuseWeight);
    return clamp(probability, 0.1, 0.9); // Avoid undersampling either lobe
}

//--------------------------------------------------------------------------------------------------
// BRDF Evaluation Functions (returns BRDF * NdotL / PDF for importance sampling)
//--------------------------------------------------------------------------------------------------

// Evaluate diffuse BRDF weight for cosine-weighted sampling
// BRDF = (1-F) * (1-metallic) * albedo / PI
// PDF = NdotL / PI
// Weight = BRDF * NdotL / PDF = (1-F) * (1-metallic) * albedo
float3 EvaluateDiffuseBRDFWeight(float3 albedo, float metallic, float3 F)
{
    return (1.0 - F) * (1.0 - metallic) * albedo;
}

// Evaluate specular BRDF weight for GGX importance sampling
// BRDF = F * D * G / (4 * NdotL * NdotV)
// PDF = D * NdotH / (4 * VdotH)
// Weight = BRDF * NdotL / PDF = F * G * VdotH / (NdotV * NdotH)
float3 EvaluateSpecularBRDFWeight(
    float3 F0,
    float roughness,
    float NdotL,
    float NdotV,
    float NdotH,
    float VdotH)
{
    // Fresnel
    float3 F = F_Schlick(F0, VdotH);

    // Geometry (visibility) term - using Smith Joint approximation
    float G = V_SmithJointGGX(NdotL, NdotV, roughness);

    // Weight = F * G * VdotH / (NdotV * NdotH)
    // Note: G already includes the 1/(4*NdotL*NdotV) factor in V_SmithJointGGX
    // So actual weight = F * G * 4 * NdotL * VdotH / NdotH
    float3 weight = F * G * 4.0 * NdotL * VdotH / max(NdotH, 0.0001);

    return weight;
}

// Combined BRDF evaluation - evaluates the sampled lobe and returns weight
float3 EvaluateBRDFWeight(
    float3 albedo,
    float metallic,
    float roughness,
    float3 normal,
    float3 viewDir,
    float3 lightDir,
    float3 halfVector,
    bool isSpecular)
{
    float NdotL = saturate(dot(normal, lightDir));
    float NdotV = max(dot(normal, viewDir), 0.001);
    float NdotH = saturate(dot(normal, halfVector));
    float VdotH = saturate(dot(viewDir, halfVector));

    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 F = F_Schlick(F0, VdotH);

    if (isSpecular)
    {
        return EvaluateSpecularBRDFWeight(F0, roughness, NdotL, NdotV, NdotH, VdotH);
    }
    else
    {
        return EvaluateDiffuseBRDFWeight(albedo, metallic, F);
    }
}

//--------------------------------------------------------------------------------------------------
// Shadow Ray Casting
//--------------------------------------------------------------------------------------------------

// Cast shadow ray to test visibility between hit point and light
// Returns 1.0 if light is visible, 0.0 if occluded
// Uses the same uber payload as path tracing rays
float CastShadowRay(float3 hitPosition, float3 surfaceNormal, float3 directionToLight, float lightDistance)
{
    // Offset ray origin to avoid self-intersection
    float rayBias = EvaluateRayTracingBias(hitPosition);

    RayDesc shadowRay;
    shadowRay.Origin = hitPosition + surfaceNormal * rayBias;
    shadowRay.Direction = directionToLight;
    shadowRay.TMin = 0.001;
    shadowRay.TMax = lightDistance - 0.001;  // Stop just before the light

    // Use uber payload initialized for shadow ray
    PathTracingPayload shadowPayload = InitShadowPayload();

    // Trace shadow ray using the same hit group as path tracing
    // The closesthit/anyhit shaders check payload.rayType to handle differently
    // RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH: terminate on first hit (optimization)
    // Note: We still go through closesthit for alpha testing support
    uint rayFlags = RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH;

    // Use the same hit group (index 0) - closesthit checks rayType
    TraceRay(_RaytracingAccelerationStructure,
             rayFlags,
             RAYTRACINGRENDERERFLAG_PATH_TRACING,
             0,     // Hit group index (same as path tracing)
             1,     // Hit group stride (only 1 group now)
             0,     // Miss shader index (unified miss shader)
             shadowRay,
             shadowPayload);

    return shadowPayload.visibility;
}

//--------------------------------------------------------------------------------------------------
// Direct Lighting Evaluation
//--------------------------------------------------------------------------------------------------

float3 EvaluateDirectLighting(
    float3 positionWS,
    float3 normalWS,
    float3 viewDirWS,
    float3 albedo,
    float metallic,
    float roughness,
    inout uint rngState)
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

        // Branch based on light type
        if (IsRectangleLight(light))
        {
            // Rectangle area light: use solid angle sampling
            float2 randomSample = RandomFloat2(rngState);

            float3 lightDir;
            float lightDist;
            float3 contribution = EvaluateRectangleLightDirect(
                light, positionWS, normalWS, viewDirWS,
                albedo, roughness, metallic,
                randomSample, lightDir, lightDist);

            if (any(contribution > 0))
            {
                // Cast shadow ray for visibility testing
                float visibility = CastShadowRay(positionWS, normalWS, lightDir, lightDist);
                if (visibility > 0.0)
                {
                    directLight += SanitizeRadiance(contribution * visibility, MAX_RADIANCE);
                }
            }
        }
        else
        {
            // Point/Spot lights: existing code path
            float3 lightVector = light.positionWS - positionWS;
            float lightDistSq = max(dot(lightVector, lightVector), 0.0001);
            float lightDist = sqrt(lightDistSq);
            float3 lightDir = lightVector / lightDist;

            float NdotL = dot(normalWS, lightDir);
            if (NdotL <= 0.0)
                continue;

            // Cast shadow ray for visibility testing
            float visibility = CastShadowRay(positionWS, normalWS, lightDir, lightDist);
            if (visibility <= 0.0)
                continue;  // Light is occluded

            // Evaluate BRDF
            float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
            float NdotV = max(dot(normalWS, viewDirWS), 0.001);
            float3 H = normalize(viewDirWS + lightDir);
            float NdotH = saturate(dot(normalWS, H));
            float LdotH = saturate(dot(lightDir, H));

            // Cook-Torrance BRDF
            float3 F = F_Schlick(F0, LdotH);
            float D = D_GGX(NdotH, max(roughness, MIN_ROUGHNESS));
            float G = V_SmithJointGGX(saturate(NdotL), NdotV, max(roughness, MIN_ROUGHNESS));

            float3 specular = min(F * D * G, MAX_RADIANCE);
            float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;

            float attenuation = GetWorldLightAttenuation(positionWS, light);
            float3 lightContrib = (diffuse + specular) * light.color * attenuation * saturate(NdotL) * visibility;

            directLight += SanitizeRadiance(lightContrib, MAX_RADIANCE);
        }
    }

    return directLight;
}

//--------------------------------------------------------------------------------------------------
// Unified Miss Shader
// Handles both path tracing rays (sky miss) and shadow rays (visibility = 1)
//--------------------------------------------------------------------------------------------------

[shader("miss")]
void MissShaderPathTracing(inout PathTracingPayload payload : SV_RayPayload)
{
    // Mark as miss (no geometry hit)
    payload.hitDistance = -1.0f;

    // For shadow rays, miss means the light is visible
    if (payload.IsShadowRay())
    {
        payload.visibility = 1.0f;
    }
    // For path tracing rays, hitDistance = -1 indicates sky sampling needed in raygen
}

//--------------------------------------------------------------------------------------------------
// Ray Generation Shader - Main Path Tracing Loop
//--------------------------------------------------------------------------------------------------

[shader("raygeneration")]
void RayGenPathTracing()
{
    uint2 launchIndex = DispatchRaysIndex().xy;
    float2 pixelCoord = float2(launchIndex) + 0.5;

    // Load depth for primary visibility
    float rawDepth = LoadSceneDepth(pixelCoord);

    // Background pixels - sample sky directly
    if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
    {
        float2 uv = pixelCoord * _ScreenSize.zw;
        float3 viewDir = normalize(mul(UNITY_MATRIX_I_VP, float4(uv * 2.0 - 1.0, 0.0, 1.0)).xyz);
        float3 skyColor = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, viewDir, 0).xyz;
        skyColor *= _PathTracingEnvironmentIntensity;

        // Apply temporal accumulation to sky pixels too
        float3 finalSkyColor = skyColor;
#if defined(_PATH_TRACING_ACCUMULATE)
        if (_RaytracingSampleIndex > 0)
        {
            float3 historySky = LOAD_TEXTURE2D_X(_PathTracingHistory, launchIndex).rgb;
            float blendWeight = 1.0 / (float)(_RaytracingSampleIndex + 1);
            finalSkyColor = lerp(historySky, skyColor, blendWeight);
        }
#endif

        _PathTracingOutput[launchIndex] = float4(finalSkyColor, 1.0);
        return;
    }

    // Get primary hit position from GBuffer
    PositionInputs posInput = GetPositionInput(pixelCoord, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float3 primaryHitPos = posInput.positionWS;
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(primaryHitPos);

    // Load DXR GBuffer data for primary hit - MUCH SIMPLER than URP GBuffer!
    // DXR GBuffer format:
    // - MaterialData: RGB = raw albedo, A = metallic
    // - NormalRoughness: RGB = world normal (already unpacked), A = sqrt(alphaRoughness)
    float4 materialData = LOAD_TEXTURE2D_X(_DXRGBufferMaterialData, launchIndex);
    float4 normalRoughness = LOAD_TEXTURE2D_X(_DXRGBufferNormalRoughness, launchIndex);

    // Direct extraction - no OctQuad decode or reflectivity conversion needed!
    float3 primaryAlbedo = materialData.rgb;
    float primaryMetallic = materialData.a;
    float3 primaryNormal = normalRoughness.rgb;  // Already world-space, already normalized
    float primaryOcclusion = 1.0;  // Occlusion not stored in DXR GBuffer, use default

    // Convert sqrt(alphaRoughness) back to smoothness
    // sqrt(alphaRoughness) = perceptualRoughness (because alphaRoughness = perceptualRoughness²)
    // smoothness = 1 - perceptualRoughness
    float primarySmoothness = 1.0 - normalRoughness.a;
    float primaryPerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(primarySmoothness);
    float primaryRoughness = max(PerceptualRoughnessToRoughness(primaryPerceptualRoughness), MIN_ROUGHNESS);

    // Initialize RNG
    uint rngState = InitRNG(launchIndex, _RaytracingSampleIndex);

#if SHARC_ENABLE_SHARC
    // Initialize SHARC parameters (shared across all samples)
    SharcParameters sharcParameters = GetSharcParameters();
#endif

    // Get view-space Z for hit distance normalization (NRD)
    float viewZ = posInput.linearDepth;

    // Compute material factors for NRD de-modulation at primary hit
    // These factors are used to divide out material properties before denoising
    // and re-modulate after denoising for better temporal stability
    float3 diffuseMaterialFactor, specularMaterialFactor;
    PT_ComputeMaterialFactors(
        primaryNormal,
        viewDirWS,
        primaryAlbedo,
        primaryMetallic,
        primaryRoughness,
        diffuseMaterialFactor,
        specularMaterialFactor);

    // Accumulate radiance over multiple samples (separate diffuse/specular for NRD)
    PathAccumulator accumulatorTotal = InitPathAccumulator();


    for (int sampleIdx = 0; sampleIdx < _RaytracingNumSamples; sampleIdx++)
    {
        // Initialize per-sample accumulator for diffuse/specular separation
        PathAccumulator sampleAccumulator = InitPathAccumulator();
        float3 throughput = float3(1, 1, 1);

        // Track first bounce for NRD hit distance attribution
        bool firstBounceWasSpecular = false;
        float firstBounceHitDist = 0.0;
        float accumulatedHitDist = 0.0;  // For diffuse: sum of all path segment lengths

#if SHARC_ENABLE_SHARC
        // Initialize SHARC state for this path
        SharcState sharcState;
        SharcInit(sharcState);
        float accumulatedRoughness = 0.0; // Track roughness for SHARC query validation
#endif

        // Current path state (start from primary hit from GBuffer)
        float3 currentPos = primaryHitPos;
        float3 currentNormal = primaryNormal;
        float3 currentAlbedo = primaryAlbedo;
        float currentOcclusion = primaryOcclusion;
        float currentMetallic = primaryMetallic;
        float currentRoughness = primaryRoughness;
        float3 currentViewDir = viewDirWS;

        // NOTE: Direct lighting at primary hit is NOT included in path tracer output!
        // Following NRD-Sample architecture:
        // - Path tracer outputs ONLY indirect lighting (secondary bounces)
        // - Direct lighting is handled by the deferred lighting pass
        // - Composition combines denoised indirect + direct lighting
        // This prevents luminance mismatch between denoised and undenoised results.

        //------------------------------------------------------------------
        // Path Tracing Loop - traces INDIRECT lighting only
        //------------------------------------------------------------------
        for (int bounce = 0; bounce < _RaytracingMaxRecursion; bounce++)
        {
            // Russian roulette for path termination (after minimum bounces)
            if (bounce >= BOUNCES_MIN)
            {
                float rrProbability = min(0.95, Luminance(throughput));
                if (RandomFloat(rngState) > rrProbability)
                    break;
                throughput /= rrProbability;
            }

            // Terminate if throughput is too low
            if (Luminance(throughput) < THROUGHPUT_THRESHOLD)
                break;

            // Sample BRDF to get next ray direction
            float NdotV = max(dot(currentNormal, currentViewDir), 0.001);
            float specularProb = GetSpecularProbability(currentAlbedo, currentMetallic, currentRoughness, NdotV);
            bool sampleSpecular = (RandomFloat(rngState) < specularProb);

            float2 u = RandomFloat2(rngState);
            float3 nextDirection;
            float3 halfVector;
            float pdf;

            if (sampleSpecular)
            {
                nextDirection = SampleGGX(u, currentNormal, currentViewDir, currentRoughness, pdf, halfVector);
            }
            else
            {
                nextDirection = SampleCosineHemisphere(u, currentNormal, pdf);
                // For diffuse, half vector is between view and light direction
                halfVector = normalize(currentViewDir + nextDirection);
            }

            // Validate direction and PDF
            float NdotL = dot(nextDirection, currentNormal);
            if (!IsFinite3(nextDirection) || NdotL <= 0.0 || pdf <= 0.0)
                break;

            // Evaluate BRDF weight (BRDF * NdotL / PDF) and apply MIS weight for lobe selection
            float3 brdfWeight = EvaluateBRDFWeight(
                currentAlbedo, currentMetallic, currentRoughness,
                currentNormal, currentViewDir, nextDirection, halfVector,
                sampleSpecular);

            // Apply probability of selecting this lobe (MIS between diffuse/specular)
            float lobeProb = sampleSpecular ? specularProb : (1.0 - specularProb);
            brdfWeight /= lobeProb;

            // Sanitize BRDF weight to avoid fireflies
            brdfWeight = SanitizeRadiance(brdfWeight, MAX_RADIANCE);

            // Update throughput with BRDF weight
            throughput *= brdfWeight;

            // Setup ray
            float rayBias = EvaluateRayTracingBias(currentPos);
            RayDesc rayDesc;
            rayDesc.Origin = currentPos + currentNormal * rayBias;
            rayDesc.Direction = nextDirection;
            rayDesc.TMin = 0.001;
            rayDesc.TMax = _RaytracingRayMaxLength;

            // Initialize payload using helper function
            PathTracingPayload payload = InitPathTracingPayload();

            // Trace ray - closesthit will populate payload with material data
            if (_nvSER)
            {
                NvHitObject hitObject;
                NvTraceRayHitObject(_RaytracingAccelerationStructure,
                                    RAY_FLAG_NONE,
                                    RAYTRACINGRENDERERFLAG_PATH_TRACING,
                                    0, 1, 0,
                                    rayDesc,
                                    payload,
                                    hitObject);
                NvReorderThread(hitObject);
                NvInvokeHitObject(_RaytracingAccelerationStructure, hitObject, payload);
            }
            else
            {
                TraceRay(_RaytracingAccelerationStructure,
                         RAY_FLAG_NONE,
                         RAYTRACINGRENDERERFLAG_PATH_TRACING,
                         0,     // Hit group index
                         1,     // Hit group stride (only 1 group now)
                         0,     // Miss shader index (unified)
                         rayDesc,
                         payload);
            }

            // Handle miss - sample environment
            if (!payload.Hit())
            {
                float3 skyColor = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_TrilinearClamp, nextDirection, 0).xyz;
                skyColor *= _PathTracingEnvironmentIntensity;

#if SHARC_UPDATE
                // Propagate sky radiance back through the path
                SharcUpdateMiss(sharcParameters, sharcState, skyColor);
#endif

                // Accumulate sky contribution to appropriate lobe based on first bounce type
                float3 skyContrib = throughput * skyColor;
                if (bounce == 0)
                {
                    // First bounce determines the lobe type
                    // For miss on first bounce, use infinite hit distance
                    if (sampleSpecular)
                    {
                        AccumulateSpecular(sampleAccumulator, skyContrib, _RaytracingRayMaxLength, 1.0);
                    }
                    else
                    {
                        AccumulateDiffuse(sampleAccumulator, skyContrib, _RaytracingRayMaxLength, 1.0);
                    }
                }
                else
                {
                    // Subsequent bounces: attribute based on first bounce type
                    if (firstBounceWasSpecular)
                    {
                        AccumulateSpecular(sampleAccumulator, skyContrib, firstBounceHitDist, 1.0);
                    }
                    else
                    {
                        AccumulateDiffuse(sampleAccumulator, skyContrib, accumulatedHitDist + _RaytracingRayMaxLength, 1.0);
                    }
                }
                break;
            }

            // Track hit distance
            float hitDist = payload.hitDistance;
            if (bounce == 0)
            {
                firstBounceWasSpecular = sampleSpecular;
                firstBounceHitDist = hitDist;
            }
            accumulatedHitDist += hitDist;

            // Hit - use material data from payload (evaluated in closesthit)
            float3 hitPos = payload.hitPositionWS;
            float3 hitNormal = payload.normalWS;
            float3 hitAlbedo = payload.albedo;
            float hitMetallic = payload.metallic;
            float hitRoughness = payload.roughness;
            float hitOcclusion = payload.occlusion;
            float3 hitEmission = payload.emission;

#if SHARC_ENABLE_SHARC
            // Create SHARC hit data for this hit
            SharcHitData sharcHitData = CreateSharcHitData(hitPos, hitNormal, hitAlbedo, hitMetallic, hitEmission);
#endif

#if SHARC_QUERY
            // Try to get cached radiance from SHARC for early path termination
            {
                uint gridLevel = HashGridGetLevel(hitPos, sharcParameters.gridParameters);
                float voxelSize = HashGridGetVoxelSize(gridLevel, sharcParameters.gridParameters);
                bool isValidHit = payload.hitDistance > voxelSize * sqrt(3.0);

                // Check roughness-based footprint for valid query
                accumulatedRoughness = min(accumulatedRoughness, 0.99);
                float alpha = accumulatedRoughness * accumulatedRoughness;
                float footprint = payload.hitDistance * sqrt(0.5 * alpha * alpha / (1.0 - alpha * alpha + 0.0001));
                isValidHit = isValidHit && (footprint > voxelSize);

                float3 cachedRadiance;
                if (isValidHit && SharcGetCachedRadiance(sharcParameters, sharcHitData, cachedRadiance, false))
                {
                    // Found valid cached radiance - use it and terminate path
                    sampleAccumulator.combinedRadiance += throughput * cachedRadiance;
                    break;
                }

                // Debug visualization for SHARC cache
                if (_SharcDebug != 0)
                {
                    float3 debugColor;
                    SharcGetCachedRadiance(sharcParameters, sharcHitData, debugColor, true);
                    _PathTracingOutput[launchIndex] = float4(debugColor, 1.0);
                    return;
                }
            }
#endif // SHARC_QUERY

            // Add emission (handled separately for SHARC)
#if SHARC_SEPARATE_EMISSIVE
            // Emission is stored separately in SHARC, so add it here
            if (_PathTracingIncludeEmissive)
            {
                float3 emissionContrib = throughput * hitEmission;
                // Attribute emission to the lobe type of first bounce
                if (bounce == 0)
                {
                    if (sampleSpecular)
                        AccumulateSpecular(sampleAccumulator, emissionContrib, hitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, emissionContrib, accumulatedHitDist, 1.0);
                }
                else
                {
                    if (firstBounceWasSpecular)
                        AccumulateSpecular(sampleAccumulator, emissionContrib, firstBounceHitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, emissionContrib, accumulatedHitDist, 1.0);
                }
            }
#else
            if (_PathTracingIncludeEmissive)
            {
                float3 emissionContrib = throughput * hitEmission;
                // Attribute emission to the lobe type of first bounce
                if (bounce == 0)
                {
                    if (sampleSpecular)
                        AccumulateSpecular(sampleAccumulator, emissionContrib, hitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, emissionContrib, accumulatedHitDist, 1.0);
                }
                else
                {
                    if (firstBounceWasSpecular)
                        AccumulateSpecular(sampleAccumulator, emissionContrib, firstBounceHitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, emissionContrib, accumulatedHitDist, 1.0);
                }
            }
#endif

            // Add direct lighting at this hit (Next Event Estimation)
            float3 directLightAtHit = float3(0, 0, 0);
            if (_PathTracingIncludeDirectLighting)
            {
                float3 hitViewDir = -nextDirection;
                directLightAtHit = EvaluateDirectLighting(
                    hitPos, hitNormal, hitViewDir,
                    hitAlbedo, hitMetallic, hitRoughness, rngState);
                // Apply occlusion to direct lighting only (throughput already accounts for BRDF)
                float3 directLightContrib = throughput * directLightAtHit * hitOcclusion;

                // Attribute direct lighting to the lobe type of first bounce
                if (bounce == 0)
                {
                    if (sampleSpecular)
                        AccumulateSpecular(sampleAccumulator, directLightContrib, hitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, directLightContrib, accumulatedHitDist, 1.0);
                }
                else
                {
                    if (firstBounceWasSpecular)
                        AccumulateSpecular(sampleAccumulator, directLightContrib, firstBounceHitDist, 1.0);
                    else
                        AccumulateDiffuse(sampleAccumulator, directLightContrib, accumulatedHitDist, 1.0);
                }
            }

#if SHARC_UPDATE
            // Update SHARC cache with direct lighting at this hit
            // SharcUpdateHit returns false if we should terminate (cache resampling found valid data)
            if (!SharcUpdateHit(sharcParameters, sharcState, sharcHitData, directLightAtHit * hitOcclusion, RandomFloat(rngState)))
            {
                break; // Cache resampling found valid cached radiance
            }
#endif

            // Clamp radiance in accumulator to avoid fireflies
            float maxRadiance = _RaytracingIntensityClamp > 0 ? _RaytracingIntensityClamp : 100.0;
            sampleAccumulator.diffuseRadiance = SanitizeRadiance(sampleAccumulator.diffuseRadiance, maxRadiance);
            sampleAccumulator.specularRadiance = SanitizeRadiance(sampleAccumulator.specularRadiance, maxRadiance);
            sampleAccumulator.combinedRadiance = SanitizeRadiance(sampleAccumulator.combinedRadiance, maxRadiance);

            // Update path state for next bounce
            currentPos = hitPos;
            currentNormal = hitNormal;
            currentAlbedo = hitAlbedo;
            currentOcclusion = hitOcclusion;
            currentMetallic = hitMetallic;
            currentRoughness = hitRoughness;
            currentViewDir = -nextDirection;

#if SHARC_ENABLE_SHARC
            // Track accumulated roughness for SHARC query validation
            // Diffuse surfaces add 1.0, specular adds roughness
            accumulatedRoughness += sampleSpecular ? hitRoughness : 1.0;

            // Update SHARC state with new throughput
            SharcSetThroughput(sharcState, throughput);
#endif
        }

        // Finalize and merge sample accumulator into total
        FinalizeAccumulator(sampleAccumulator);

        // Merge into total accumulator (weighted average across samples)
        accumulatorTotal.diffuseRadiance += sampleAccumulator.diffuseRadiance;
        accumulatorTotal.diffuseHitDist += sampleAccumulator.diffuseHitDist;
        accumulatorTotal.diffuseWeight += sampleAccumulator.diffuseWeight > 0 ? 1.0 : 0.0;

        accumulatorTotal.specularRadiance += sampleAccumulator.specularRadiance;
        accumulatorTotal.specularHitDist += sampleAccumulator.specularHitDist;
        accumulatorTotal.specularWeight += sampleAccumulator.specularWeight > 0 ? 1.0 : 0.0;

        accumulatorTotal.combinedRadiance += sampleAccumulator.combinedRadiance;
    }

    // Average over samples
    float invNumSamples = 1.0 / max(_RaytracingNumSamples, 1);
    accumulatorTotal.diffuseRadiance *= invNumSamples;
    accumulatorTotal.specularRadiance *= invNumSamples;
    accumulatorTotal.combinedRadiance *= invNumSamples;

    // Average hit distances across samples that had valid contributions
    if (accumulatorTotal.diffuseWeight > 0)
        accumulatorTotal.diffuseHitDist /= accumulatorTotal.diffuseWeight;
    if (accumulatorTotal.specularWeight > 0)
        accumulatorTotal.specularHitDist /= accumulatorTotal.specularWeight;

    // Apply intensity
    accumulatorTotal.diffuseRadiance *= _PathTracingIntensity;
    accumulatorTotal.specularRadiance *= _PathTracingIntensity;
    accumulatorTotal.combinedRadiance *= _PathTracingIntensity;

    //------------------------------------------------------------------
    // Temporal Accumulation
    //------------------------------------------------------------------
    float3 finalRadiance = accumulatorTotal.combinedRadiance;
    float3 finalDiffuse = accumulatorTotal.diffuseRadiance;
    float3 finalSpecular = accumulatorTotal.specularRadiance;
    float finalDiffuseHitDist = accumulatorTotal.diffuseHitDist;
    float finalSpecularHitDist = accumulatorTotal.specularHitDist;

#if defined(_PATH_TRACING_ACCUMULATE)
    // _RaytracingSampleIndex is used as the frame count for accumulation
    // Frame 0: just use current sample
    // Frame N: blend with history using running average
    int frameCount = _RaytracingSampleIndex;

    if (frameCount > 0)
    {
        // Sample history buffers
        float3 historyRadiance = LOAD_TEXTURE2D_X(_PathTracingHistory, launchIndex).rgb;
        float4 historyDiffuse = LOAD_TEXTURE2D_X(_PathTracingDiffuseHistory, launchIndex);
        float4 historySpecular = LOAD_TEXTURE2D_X(_PathTracingSpecularHistory, launchIndex);

        // Running average: result = (history * frameCount + current) / (frameCount + 1)
        // This is equivalent to: result = lerp(history, current, 1.0 / (frameCount + 1))
        float blendWeight = 1.0 / (float)(frameCount + 1);
        finalRadiance = lerp(historyRadiance, accumulatorTotal.combinedRadiance, blendWeight);
        finalDiffuse = lerp(historyDiffuse.rgb, accumulatorTotal.diffuseRadiance, blendWeight);
        finalSpecular = lerp(historySpecular.rgb, accumulatorTotal.specularRadiance, blendWeight);

        // Hit distance blending (use min for specular - first hit most important)
        finalDiffuseHitDist = lerp(historyDiffuse.a, finalDiffuseHitDist, blendWeight);
        finalSpecularHitDist = min(historySpecular.a, finalSpecularHitDist);
    }
    // else: frameCount == 0, use current sample directly (no history yet)
#endif

    // Sanitize final output to avoid NaN propagation in accumulation
    if (!IsFinite3(finalRadiance))
    {
        finalRadiance = float3(0, 0, 0);
    }
    if (!IsFinite3(finalDiffuse))
    {
        finalDiffuse = float3(0, 0, 0);
        finalDiffuseHitDist = 0.0;
    }
    if (!IsFinite3(finalSpecular))
    {
        finalSpecular = float3(0, 0, 0);
        finalSpecularHitDist = 0.0;
    }

    // Normalize hit distances for NRD
    float normalizedDiffuseHitDist = NormalizeHitDistance(finalDiffuseHitDist, viewZ);
    float normalizedSpecularHitDist = NormalizeHitDistance(finalSpecularHitDist, viewZ);

    // De-modulate radiance for NRD denoising
    // Dividing out material factors before denoising improves temporal stability
    // The denoised result will be re-modulated by multiplying these factors back
    float3 demodulatedDiffuse = PT_DemodulateRadiance(finalDiffuse, diffuseMaterialFactor);
    float3 demodulatedSpecular = PT_DemodulateRadiance(finalSpecular, specularMaterialFactor);

    //------------------------------------------------------------------
    // Debug Visualization
    //------------------------------------------------------------------
    float3 outputColor = finalRadiance;

    if (_PathTracingDebugMode > 0)
    {
        switch (_PathTracingDebugMode)
        {
            case 1: // Show Frame Count - Heatmap visualization
            {
                // Blue (0 frames) -> Cyan (32) -> Green (64) -> Yellow (128) -> Red (256+)
                float normalizedFrameCount = saturate(_RaytracingSampleIndex / 256.0);
                float3 heatmapColor;
                if (normalizedFrameCount < 0.25)
                {
                    // Blue to Cyan
                    heatmapColor = lerp(float3(0, 0, 1), float3(0, 1, 1), normalizedFrameCount * 4.0);
                }
                else if (normalizedFrameCount < 0.5)
                {
                    // Cyan to Green
                    heatmapColor = lerp(float3(0, 1, 1), float3(0, 1, 0), (normalizedFrameCount - 0.25) * 4.0);
                }
                else if (normalizedFrameCount < 0.75)
                {
                    // Green to Yellow
                    heatmapColor = lerp(float3(0, 1, 0), float3(1, 1, 0), (normalizedFrameCount - 0.5) * 4.0);
                }
                else
                {
                    // Yellow to Red
                    heatmapColor = lerp(float3(1, 1, 0), float3(1, 0, 0), (normalizedFrameCount - 0.75) * 4.0);
                }
                outputColor = heatmapColor;
                break;
            }

            case 2: // Show Primary Normals
            {
                outputColor = primaryNormal * 0.5 + 0.5;  // Remap [-1,1] to [0,1]
                break;
            }

            case 3: // Show Primary Albedo
            {
                outputColor = primaryAlbedo;
                break;
            }

            case 4: // Show Primary Metallic
            {
                outputColor = float3(primaryMetallic, primaryMetallic, primaryMetallic);
                break;
            }

            case 5: // Show Primary Roughness
            {
                outputColor = float3(primaryRoughness, primaryRoughness, primaryRoughness);
                break;
            }

            case 6: // Show Primary Occlusion
            {
                outputColor = float3(primaryOcclusion, primaryOcclusion, primaryOcclusion);
                break;
            }

            case 7: // Show Diffuse Radiance
            {
                outputColor = finalDiffuse;
                break;
            }

            case 8: // Show Specular Radiance
            {
                outputColor = finalSpecular;
                break;
            }

            case 9: // Show Diffuse Hit Distance
            {
                outputColor = float3(normalizedDiffuseHitDist, normalizedDiffuseHitDist, normalizedDiffuseHitDist);
                break;
            }

            case 10: // Show Specular Hit Distance
            {
                outputColor = float3(normalizedSpecularHitDist, normalizedSpecularHitDist, normalizedSpecularHitDist);
                break;
            }

            default:
                outputColor = finalRadiance;
                break;
        }
    }

    // Output results
    // Combined output (for compatibility and debug visualization)
    _PathTracingOutput[launchIndex] = float4(outputColor, 1.0);

    // Separate diffuse/specular outputs for NRD denoising
    // Format: RGB = de-modulated radiance, A = normalized hit distance
    // De-modulated radiance = radiance / material_factor for better denoising
    _PathTracingDiffuseOutput[launchIndex] = float4(demodulatedDiffuse, normalizedDiffuseHitDist);
    _PathTracingSpecularOutput[launchIndex] = float4(demodulatedSpecular, normalizedSpecularHitDist);

    // Material factors for re-modulation after denoising
    // Pack: RGB = diffuse factor, A = specular luminance
    _PathTracingMaterialFactors[launchIndex] = PT_PackMaterialFactors(diffuseMaterialFactor, specularMaterialFactor);
}


#endif // REFERENCED_PATH_TRACING_RAYGEN_INCLUDED
