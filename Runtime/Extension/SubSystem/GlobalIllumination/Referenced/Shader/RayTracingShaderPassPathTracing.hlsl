#ifndef UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED
#define UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED

// This shader pass handles the closest hit for path tracing rays
// It evaluates material properties and direct lighting from WorldLightCluster

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/GlobalIllumination/Referenced/Shader/LitInputPathTracing.hlsl"

// Path tracing payload structure (defined in ReferencedPathTracing.hlsl)
struct PathTracingPayload
{
    float3 radiance;        // Accumulated radiance
    float3 throughput;      // Path throughput (attenuation)
    float3 origin;          // Next ray origin
    float3 direction;       // Next ray direction
    int bounceCount;        // Current bounce count
    bool active;            // Is path still active
    float pdf;              // PDF of current bounce
    uint randomSeed;        // Random seed for this path
};

// PCG Random number generator
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

// Evaluate direct lighting from WorldLightCluster at hit point
float3 EvaluatePathTracingDirectLighting(float3 positionWS, float3 normalWS, float3 viewDirWS,
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
        
        // TODO: Shadow ray tracing for accurate shadows
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

[shader("closesthit")]
void ClosestHitPathTracing(inout PathTracingPayload payload : SV_RayPayload,  AttributeData attributeData : SV_IntersectionAttributes)
{
    // Get hit position in world space
    float3 hitPositionWS = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    float hitDistance = RayTCurrent();
    
    // Build fragment inputs
    IntersectionVertex currentVertex;
    FragInputs fragInput;
    GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);
    
    // Get position inputs
    PositionInputs posInput;
    posInput.positionWS = hitPositionWS;
    posInput.positionSS = float2(0, 0); // Not used in path tracing
    
    // Calculate texture LOD based on ray distance
    // Uses distance-based heuristic: farther hits use lower resolution textures
    float textureLOD = ComputeTextureLODFromDistance(hitDistance, 1.0);
    
    // For secondary bounces, increase LOD to reduce aliasing and improve performance
    textureLOD += payload.bounceCount * 0.5;
    
    // Sample material properties with explicit LOD (ray tracing compatible)
    SurfaceData surfaceData;
    InitializeStandardLitSurfaceDataRT(fragInput.texCoord0, textureLOD, surfaceData);
    
    // Apply normal mapping if enabled
    float3 normalWS = fragInput.tangentToWorld[2];
    #ifdef _NORMALMAP
        float3 normalTS = SampleNormal(fragInput.texCoord0, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
        normalWS = TransformTangentToWorld(normalTS, fragInput.tangentToWorld);
    #endif
    normalWS = normalize(normalWS);
    
    // Get view direction
    float3 viewDirWS = -WorldRayDirection();
    
    // Extract material properties
    float3 albedo = surfaceData.albedo;
    float metallic = surfaceData.metallic;
    float smoothness = surfaceData.smoothness;
    float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
    float roughness = max(PerceptualRoughnessToRoughness(perceptualRoughness), 0.001);
    float occlusion = surfaceData.occlusion;
    float3 emission = surfaceData.emission;
    
    // Alpha testing
    #ifdef _ALPHATEST_ON
        clip(surfaceData.alpha - surfaceData.alphaClipThreshold);
    #endif
    
    // Evaluate direct lighting from WorldLightCluster
    float3 directLight = EvaluatePathTracingDirectLighting(hitPositionWS, normalWS, viewDirWS, 
                                                           albedo, metallic, roughness, payload.randomSeed);
    
    // Add emission
    directLight += emission;
    
    // Accumulate direct lighting
    payload.radiance += payload.throughput * directLight;
    
    // Update throughput for next bounce (simplified diffuse BRDF)
    payload.throughput *= albedo * occlusion;
    
    // Sample next bounce direction (diffuse for now)
    float2 u = RandomFloat2(payload.randomSeed);
    float3 nextDirection = SampleCosineHemisphere(u, normalWS);
    
    // Update payload for next bounce
    float rayBias = EvaluateRayTracingBias(hitPositionWS);
    payload.origin = hitPositionWS + normalWS * rayBias;
    payload.direction = nextDirection;
    payload.bounceCount++;
    payload.pdf = 1.0 / PI; // Cosine hemisphere PDF
    
    // Check if we should continue tracing
    // The raygen shader will handle Russian roulette and max bounces
}

[shader("anyhit")]
void AnyHitPathTracing(inout PathTracingPayload payload : SV_RayPayload, in AttributeData attributeData : SV_IntersectionAttributes)
{
    // Alpha testing for transparent materials
    #ifdef _ALPHATEST_ON
        IntersectionVertex currentVertex;
        FragInputs fragInput;
        GetCurrentVertexAndBuildFragInputs(attributeData, currentVertex, fragInput);
        
        // Use LOD 0 for alpha testing (need accurate alpha values)
        SurfaceData surfaceData;
        InitializeStandardLitSurfaceDataRT(fragInput.texCoord0, 0.0, surfaceData);
        
        // Ignore this hit if alpha is below threshold
        if (surfaceData.alpha < surfaceData.alphaClipThreshold)
        {
            IgnoreHit();
        }
    #endif
}

#endif // UNIVERSAL_RAYTRACING_SHADER_PASS_PATH_TRACING_INCLUDED


