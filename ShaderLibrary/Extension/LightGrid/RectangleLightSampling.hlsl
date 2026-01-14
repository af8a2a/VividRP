#ifndef RECTANGLE_LIGHT_SAMPLING_INCLUDED
#define RECTANGLE_LIGHT_SAMPLING_INCLUDED

// Rectangle Area Light Sampling for Path Tracing
// Uses solid angle sampling for physically correct light integration
// Requires: BSDF.hlsl (F_Schlick) and BRDF.hlsl (D_GGX, V_SmithJointGGX) to be included before this file

// Shared constants with path tracer (use existing defines if available)
#ifndef MIN_ROUGHNESS
#define MIN_ROUGHNESS 0.04
#endif
#ifndef MAX_RADIANCE
#define MAX_RADIANCE 10.0
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/SubSystem/LightCullingSystem/GPULights.cs.hlsl"

// ============================================================================
// Rectangle Light Sampling
// ============================================================================

// Sample a point on rectangle light surface using uniform area sampling
// Returns: sampled position in world space
// Outputs: PDF for the sample, light normal at sample point
float3 SampleRectangleLightSurface(
    GPULightData light,
    float2 u,               // Random numbers in [0,1]
    out float pdf,
    out float3 lightNormal)
{
    // Rectangle dimensions from light.size (x = width, y = height)
    float width = light.size.x;
    float height = light.size.y;
    float area = width * height;

    // Sample point on rectangle surface (uniform)
    float2 localPoint = float2(
        (u.x - 0.5) * width,
        (u.y - 0.5) * height
    );

    // Transform to world space using light's local axes
    float3 samplePos = light.positionWS
                     + light.right * localPoint.x
                     + light.up * localPoint.y;

    // Light normal (rectangle emits in forward direction, matching IsInRectangleLightRange)
    lightNormal = light.forward;

    // PDF for uniform area sampling
    pdf = 1.0 / area;

    return samplePos;
}

// ============================================================================
// Pillow Windowing (Soft Edge Falloff)
// ============================================================================

// Pillow window function for soft rectangle edges
// Creates smooth falloff at rectangle boundaries
float PillowWindowing(float2 uv)
{
    // Map UV to [-1, 1] range
    float2 d = abs(uv - 0.5) * 2.0;
    // Smooth quadratic falloff at edges
    float2 falloff = saturate(1.0 - d * d);
    return falloff.x * falloff.y;
}


// ============================================================================
// Simple BRDF Evaluation (DEPRECATED - kept for compatibility)
// Note: EvaluateRectangleLightDirect now uses full Cook-Torrance BRDF inline
// ============================================================================

// DEPRECATED: Simplified BRDF - use full BRDF for consistency with punctual lights
float3 EvaluateSimpleBRDF(
    float3 albedo,
    float roughness,
    float metallic,
    float3 N,
    float3 V,
    float3 L)
{
    // Diffuse (Lambertian)
    float3 diffuseColor = albedo * (1.0 - metallic);
    float3 diffuse = diffuseColor * (1.0 / PI);

    // Specular (simplified GGX)
    float3 H = normalize(V + L);
    float NdotH = saturate(dot(N, H));
    float NdotV = saturate(dot(N, V));
    float NdotL = saturate(dot(N, L));

    // Roughness to alpha
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;

    // GGX NDF
    float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
    float D = alpha2 / (PI * denom * denom);

    // Fresnel (Schlick approximation)
    float3 F0 = lerp(0.04, albedo, metallic);
    float3 F = F0 + (1.0 - F0) * pow(1.0 - saturate(dot(H, V)), 5.0);

    // Geometry term (Smith GGX)
    float k = alpha / 2.0;
    float G1_V = NdotV / (NdotV * (1.0 - k) + k);
    float G1_L = NdotL / (NdotL * (1.0 - k) + k);
    float G = G1_V * G1_L;

    // Specular BRDF
    float3 specular = (D * F * G) / max(4.0 * NdotV * NdotL, 0.001);

    return diffuse + specular;
}

// ============================================================================
// Rectangle Light Direct Lighting Evaluation
// ============================================================================

// Evaluate rectangle light contribution for path tracing direct lighting
// Uses solid angle sampling with proper PDF conversion
// Returns: contribution (BRDF * emission * cos / PDF)
// Outputs: lightDir and lightDist for shadow ray
float3 EvaluateRectangleLightDirect(
    GPULightData light,
    float3 surfacePos,
    float3 surfaceNormal,
    float3 viewDir,
    float3 albedo,
    float roughness,
    float metallic,
    float2 randomSample,
    out float3 lightDir,
    out float lightDist)
{
    // Initialize outputs
    lightDir = float3(0, 1, 0);
    lightDist = 0;

    // Sample point on rectangle surface
    float pdf;
    float3 lightNormal;
    float3 lightSamplePos = SampleRectangleLightSurface(light, randomSample, pdf, lightNormal);

    // Vector from surface to light sample
    float3 lightVec = lightSamplePos - surfacePos;
    float distSq = dot(lightVec, lightVec);
    lightDist = sqrt(distSq);
    lightDir = lightVec / lightDist;

    // Geometry term: cos at surface * cos at light
    float NdotL = saturate(dot(surfaceNormal, lightDir));
    float lightCos = saturate(dot(lightNormal, -lightDir));

    // Early out if light doesn't contribute
    if (NdotL <= 0.0 || lightCos <= 0.0)
    {
        return 0.0;
    }

    // Convert area PDF to solid angle PDF
    // pdf_solidAngle = pdf_area * distSq / lightCos
    // Clamp lightCos to avoid fireflies at grazing angles
    float pdfSolidAngle = pdf * distSq / max(lightCos, 1e-4);

    // Light emission (already in linear space)
    float3 emission = light.color;

    // Apply range attenuation
    // rangeAttenuationScale = sqrtHuge / (range * range), sqrtHuge = 4096
    float rangeSq = 4096.0 / max(light.rangeAttenuationScale, 0.0001);
    float distAtten = saturate(1.0 - distSq / rangeSq);
    distAtten *= distAtten;

    // Apply pillow windowing for soft edges
    // Calculate local UV from the sampled point on rectangle surface (NOT lightVec)
    float3 localSamplePos = lightSamplePos - light.positionWS;
    float2 localUV = float2(
        dot(localSamplePos, light.right) / light.size.x + 0.5,
        dot(localSamplePos, light.up) / light.size.y + 0.5
    );
    float pillowAtten = PillowWindowing(localUV);

    // Cook-Torrance BRDF (matching punctual light path in ReferencedPathTracingRayGen.hlsl)
    // This ensures energy/roughness consistency with punctual lights and correct NRD remodulation
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float NdotV = max(dot(surfaceNormal, viewDir), 0.001);
    float3 H = normalize(viewDir + lightDir);
    float NdotH = saturate(dot(surfaceNormal, H));
    float LdotH = saturate(dot(lightDir, H));

    float3 F = F_Schlick(F0, LdotH);
    float clampedRoughness = max(roughness, MIN_ROUGHNESS);
    float D = D_GGX(NdotH, clampedRoughness);
    float G = V_SmithJointGGX(NdotL, NdotV, clampedRoughness);

    float3 specular = min(F * D * G, MAX_RADIANCE);
    float3 diffuse = (1.0 - F) * (1.0 - metallic) * albedo / PI;
    float3 brdf = diffuse + specular;

    // Monte Carlo estimator: BRDF * emission * cos / pdf
    float3 contribution = brdf * emission * NdotL * distAtten * pillowAtten / pdfSolidAngle;

    return contribution;
}
// ============================================================================
// Rectangle Light Importance
// ============================================================================

// Calculate importance of a rectangle light for importance sampling
// Higher importance = more likely to be sampled
float GetRectangleLightImportance(
    GPULightData light,
    float3 surfacePos,
    float3 surfaceNormal)
{
    float3 lightCenter = light.positionWS;
    float3 toLight = lightCenter - surfacePos;
    float distSq = dot(toLight, toLight);
    float dist = sqrt(distSq);

    // Direction to light center
    float3 lightDir = toLight / dist;

    // Approximate solid angle
    float area = light.size.x * light.size.y;
    float solidAngle = area / distSq;

    // Cosine at surface
    float NdotL = max(dot(surfaceNormal, lightDir), 0.0);

    // Cosine at light (facing towards surface, using forward direction as emission normal)
    float lightCos = max(dot(light.forward, -lightDir), 0.0);

    // Luminance of light color
    float luminance = dot(light.color, float3(0.2126, 0.7152, 0.0722));

    // Combined importance
    return luminance * solidAngle * NdotL * lightCos;
}

// ============================================================================
// MIS (Multiple Importance Sampling) Utilities
// ============================================================================

// Power heuristic for MIS weight calculation (beta = 2)
float MISWeightPowerHeuristic(float pdf1, float pdf2)
{
    float pdf1Sq = pdf1 * pdf1;
    float pdf2Sq = pdf2 * pdf2;
    return pdf1Sq / max(pdf1Sq + pdf2Sq, 1e-8);
}

// Balance heuristic for MIS weight calculation
float MISWeightBalanceHeuristic(float pdf1, float pdf2)
{
    return pdf1 / max(pdf1 + pdf2, 1e-8);
}

// ============================================================================
// Punctual Light Importance (for Point/Spot lights)
// ============================================================================

// Calculate importance of a punctual light for importance sampling
float GetPunctualLightImportance(
    GPULightData light,
    float3 surfacePos,
    float3 surfaceNormal)
{
    float3 toLight = light.positionWS - surfacePos;
    float distSq = max(dot(toLight, toLight), 0.0001);
    float dist = sqrt(distSq);
    float3 lightDir = toLight / dist;

    // Cosine at surface
    float NdotL = max(dot(surfaceNormal, lightDir), 0.0);

    // Luminance of light color
    float luminance = dot(light.color, float3(0.2126, 0.7152, 0.0722));

    // Attenuation estimate (inverse square with range falloff)
    float rangeSq = 1.0 / max(light.lightAttenuation.x, 0.0001);
    float distAtten = saturate(1.0 - distSq / rangeSq);
    distAtten *= distAtten;

    return luminance * distAtten * NdotL / distSq;
}

// ============================================================================
// Rectangle Light PDF Evaluation (for MIS)
// ============================================================================

// Evaluate the solid angle PDF for sampling a specific point on rectangle light
// Used for MIS weight calculation when BSDF sampling hits the light
float EvaluateRectangleLightPDF(
    GPULightData light,
    float3 surfacePos,
    float3 lightSamplePos)
{
    float area = light.size.x * light.size.y;
    float areaPDF = 1.0 / area;

    // Convert area PDF to solid angle PDF
    float3 lightVec = lightSamplePos - surfacePos;
    float distSq = dot(lightVec, lightVec);
    float dist = sqrt(distSq);
    float3 lightDir = lightVec / dist;

    // Light normal (forward direction)
    float lightCos = max(dot(light.forward, -lightDir), 1e-4);

    // Solid angle PDF = area PDF * dist² / cos
    return areaPDF * distSq / lightCos;
}

// ============================================================================
// Unified Light Importance
// ============================================================================

// Get importance for any light type (for light selection)
float GetLightImportance(
    GPULightData light,
    float3 surfacePos,
    float3 surfaceNormal)
{
    if (IsRectangleLight(light))
    {
        return GetRectangleLightImportance(light, surfacePos, surfaceNormal);
    }
    else
    {
        return GetPunctualLightImportance(light, surfacePos, surfaceNormal);
    }
}

#endif // RECTANGLE_LIGHT_SAMPLING_INCLUDED
