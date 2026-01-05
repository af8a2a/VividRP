// Referenced Path Tracing for Multi-Bounce Global Illumination
// DXR 1.0 compatible - material evaluation in closesthit, path tracing loop in raygen

#ifndef REFERENCED_PATH_TRACING_INCLUDED
#define REFERENCED_PATH_TRACING_INCLUDED

#define SHADER_TARGET 50

//--------------------------------------------------------------------------------------------------
// SHARC Configuration
// SHARC_UPDATE and SHARC_QUERY are defined via multi_compile in .raytrace file
//--------------------------------------------------------------------------------------------------

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

#include "LitInputPathTracing.hlsl"

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

TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);

// Output and History for temporal accumulation
RW_TEXTURE2D(float4, _PathTracingOutput);
TEXTURE2D_X(_PathTracingHistory);

// Path tracing specific parameters (not in ShaderVariablesRaytracing CB)
int _PathTracingAccumulate;
float _PathTracingIntensity;
float _PathTracingEnvironmentIntensity;
int _PathTracingIncludeEmissive;
int _PathTracingIncludeDirectLighting;
int _PathTracingDebugVisualizeBounce;
int _PathTracingDebugMode;  // 0=Normal, 1=ShowFrameCount, 2=ShowNormals, 3=ShowAlbedo

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
// Path Tracing Payload - carries material data from closesthit (DXR 1.0 compatible)
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
// Shadow Ray Payload (must match RayTracingShaderPassPathTracing.hlsl)
//--------------------------------------------------------------------------------------------------

struct ShadowRayPayload
{
    float visibility;  // 1.0 = visible, 0.0 = shadowed
};


//--------------------------------------------------------------------------------------------------
// Shadow Ray Casting
//--------------------------------------------------------------------------------------------------

// Cast shadow ray to test visibility between hit point and light
// Returns 1.0 if light is visible, 0.0 if occluded
float CastShadowRay(float3 hitPosition, float3 surfaceNormal, float3 directionToLight, float lightDistance)
{
    // Offset ray origin to avoid self-intersection
    float rayBias = EvaluateRayTracingBias(hitPosition);

    RayDesc shadowRay;
    shadowRay.Origin = hitPosition + surfaceNormal * rayBias;
    shadowRay.Direction = directionToLight;
    shadowRay.TMin = 0.001;
    shadowRay.TMax = lightDistance - 0.001;  // Stop just before the light

    ShadowRayPayload shadowPayload;
    shadowPayload.visibility = 0.0;

    // Trace shadow ray
    // RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH: terminate on first hit
    // RAY_FLAG_SKIP_CLOSEST_HIT_SHADER: we only need anyhit for alpha testing
    uint rayFlags = RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER;

    // Use hit group index 1 for shadow rays (0=main path tracing, 1=shadow)
    // missShaderIndex 1 for shadow miss shader
    TraceRay(_RaytracingAccelerationStructure,
             rayFlags,
            RAYTRACINGRENDERERFLAG_PATH_TRACING ,  // Instance mask - trace against all geometry
             1,     // Ray contribution to hit group index (shadow shaders)
             2,     // Hit group stride (2 groups: 0=main, 1=shadow)
             1,     // Miss shader index (shadow miss)
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

        // Light direction and distance
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

    return directLight;
}

//--------------------------------------------------------------------------------------------------
// Miss Shaders
//--------------------------------------------------------------------------------------------------

[shader("miss")]
void MissShaderPathTracing(inout PathTracingPayload payload : SV_RayPayload)
{
    payload.hitDistance = -1.0f;
}

[shader("miss")]
void MissShadow(inout ShadowRayPayload payload : SV_RayPayload)
{
    payload.visibility = 1.0;
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
        if (_PathTracingAccumulate && _RaytracingSampleIndex > 0)
        {
            float3 historySky = LOAD_TEXTURE2D_X(_PathTracingHistory, launchIndex).rgb;
            float blendWeight = 1.0 / (float)(_RaytracingSampleIndex + 1);
            finalSkyColor = lerp(historySky, skyColor, blendWeight);
        }

        _PathTracingOutput[launchIndex] = float4(finalSkyColor, 1.0);
        return;
    }

    // Get primary hit position from GBuffer
    PositionInputs posInput = GetPositionInput(pixelCoord, _ScreenSize.zw, rawDepth, UNITY_MATRIX_I_VP, GetWorldToViewMatrix(), 0);
    float3 primaryHitPos = posInput.positionWS;
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(primaryHitPos);

    // Load GBuffer data for primary hit
    // GBuffer format (see ShaderLibrary/GBufferOutput.hlsl):
    // gBuffer0: RGB = albedo, A = material flags
    // gBuffer1: RGB = specular color, A = occlusion
    // gBuffer2: RGB = packed normal, A = smoothness
    float4 gbuffer0 = LOAD_TEXTURE2D_X(_GBuffer0, launchIndex);
    float4 gbuffer1 = LOAD_TEXTURE2D_X(_GBuffer1, launchIndex);
    float4 gbuffer2 = LOAD_TEXTURE2D_X(_GBuffer2, launchIndex);

    float3 primaryAlbedo = gbuffer0.rgb;
    float primaryOcclusion = gbuffer1.a;  // Occlusion is in gBuffer1.a
    float3 primaryNormal = normalize(UnpackGBufferNormal(gbuffer2.xyz));  // Use correct unpack function

    // gBuffer1.r contains reflectivity in metallic workflow, convert to metallic
    float reflectivity = gbuffer1.r;
    float primaryMetallic = MetallicFromReflectivity(reflectivity);  // Convert reflectivity to metallic

    float primarySmoothness = gbuffer2.a;
    float primaryPerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(primarySmoothness);
    float primaryRoughness = max(PerceptualRoughnessToRoughness(primaryPerceptualRoughness), MIN_ROUGHNESS);

    // Initialize RNG
    uint rngState = InitRNG(launchIndex, _RaytracingSampleIndex);

#if SHARC_ENABLE_SHARC
    // Initialize SHARC parameters (shared across all samples)
    SharcParameters sharcParameters = GetSharcParameters();
#endif

    // Accumulate radiance over multiple samples
    float3 accumulatedRadiance = float3(0, 0, 0);


    for (int sampleIdx = 0; sampleIdx < _RaytracingNumSamples; sampleIdx++)
    {
        float3 sampleRadiance = float3(0, 0, 0);
        float3 throughput = float3(1, 1, 1);

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

        // Add direct lighting at primary hit (Next Event Estimation)
        if (_PathTracingIncludeDirectLighting)
        {
            float3 directLight = EvaluateDirectLighting(
                primaryHitPos, primaryNormal, viewDirWS,
                primaryAlbedo, primaryMetallic, primaryRoughness, rngState);
            // Apply occlusion to direct lighting
            sampleRadiance += throughput * directLight * primaryOcclusion;
        }

        //------------------------------------------------------------------
        // Path Tracing Loop
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

            // Initialize payload
            PathTracingPayload payload;
            payload.hitDistance = -1.0f;
            payload.albedo = float3(0, 0, 0);
            payload.normalWS = float3(0, 1, 0);
            payload.emission = float3(0, 0, 0);
            payload.metallic = 0.0;
            payload.roughness = 1.0;
            payload.occlusion = 1.0;
            payload.hitPositionWS = float3(0, 0, 0);

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
                         0, 1, 0,
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

                sampleRadiance += throughput * skyColor;
                break;
            }

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
                    sampleRadiance += throughput * cachedRadiance;
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
                sampleRadiance += throughput * hitEmission;
            }
#else
            if (_PathTracingIncludeEmissive)
            {
                sampleRadiance += throughput * hitEmission;
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
                sampleRadiance += throughput * directLightAtHit * hitOcclusion;
            }

#if SHARC_UPDATE
            // Update SHARC cache with direct lighting at this hit
            // SharcUpdateHit returns false if we should terminate (cache resampling found valid data)
            if (!SharcUpdateHit(sharcParameters, sharcState, sharcHitData, directLightAtHit * hitOcclusion, RandomFloat(rngState)))
            {
                break; // Cache resampling found valid cached radiance
            }
#endif

            // Clamp radiance to avoid fireflies
            sampleRadiance = SanitizeRadiance(sampleRadiance,
                _RaytracingIntensityClamp > 0 ? _RaytracingIntensityClamp : 100.0);

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

        accumulatedRadiance += sampleRadiance;
    }

    // Average over samples
    accumulatedRadiance /= max(_RaytracingNumSamples, 1);

    // Apply intensity
    accumulatedRadiance *= _PathTracingIntensity;

    //------------------------------------------------------------------
    // Temporal Accumulation
    //------------------------------------------------------------------
    float3 finalRadiance = accumulatedRadiance;

    if (_PathTracingAccumulate)
    {
        // _RaytracingSampleIndex is used as the frame count for accumulation
        // Frame 0: just use current sample
        // Frame N: blend with history using running average
        int frameCount = _RaytracingSampleIndex;

        if (frameCount > 0)
        {
            // Sample history buffer
            float3 historyRadiance = LOAD_TEXTURE2D_X(_PathTracingHistory, launchIndex).rgb;

            // Running average: result = (history * frameCount + current) / (frameCount + 1)
            // This is equivalent to: result = lerp(history, current, 1.0 / (frameCount + 1))
            float blendWeight = 1.0 / (float)(frameCount + 1);
            finalRadiance = lerp(historyRadiance, accumulatedRadiance, blendWeight);
        }
        // else: frameCount == 0, use current sample directly (no history yet)
    }

    // Sanitize final output to avoid NaN propagation in accumulation
    if (!IsFinite3(finalRadiance))
    {
        finalRadiance = float3(0, 0, 0);
    }

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

            default:
                outputColor = finalRadiance;
                break;
        }
    }

    // Output result
    _PathTracingOutput[launchIndex] = float4(outputColor, 1.0);
}

#endif // REFERENCED_PATH_TRACING_INCLUDED
