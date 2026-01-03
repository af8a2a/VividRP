#ifndef UNIVERSAL_LIT_INPUT_PATH_TRACING_INCLUDED
#define UNIVERSAL_LIT_INPUT_PATH_TRACING_INCLUDED

// Path Tracing specific version of Lit shader input sampling
// Uses explicit LOD sampling instead of implicit derivatives
// This is required because ray tracing shaders don't have screen-space gradients

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"

// Sample texture with explicit LOD (for ray tracing)
// LOD 0 = highest resolution, higher values = more blurry
inline half4 SampleAlbedoAlphaRT(float2 uv, float lod, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return half4(SAMPLE_TEXTURE2D_LOD(albedoAlphaMap, sampler_albedoAlphaMap, uv, lod));
}

inline half4 SampleNormalRT(float2 uv, float lod, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
#ifdef _NORMALMAP
    half4 n = SAMPLE_TEXTURE2D_LOD(bumpMap, sampler_bumpMap, uv, lod);
    #if BUMP_SCALE_NOT_SUPPORTED
        return half4(UnpackNormal(n), 0);
    #else
        return half4(UnpackNormalScale(n, scale), 0);
    #endif
#else
    return half4(0.0h, 0.0h, 1.0h, 0.0h);
#endif
}

inline half4 SampleMetallicSpecGlossRT(float2 uv, float lod, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_TEXTURE2D_LOD(_MetallicGlossMap, sampler_MetallicGlossMap, uv, lod));
    #ifdef _SPECULAR_SETUP
        specGloss.rgb = specGloss.rgb;
    #else
        specGloss.rgb = specGloss.rrr;
    #endif

    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else // _METALLICSPECGLOSSMAP
    #if _SPECULAR_SETUP
        specGloss.rgb = _SpecColor.rgb;
    #else
        specGloss.rgb = _Metallic.rrr;
    #endif

    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

inline half SampleOcclusionRT(float2 uv, float lod)
{
#ifdef _OCCLUSIONMAP
    #if defined(SHADER_API_GLES)
        return SAMPLE_TEXTURE2D_LOD(_OcclusionMap, sampler_OcclusionMap, uv, lod).g;
    #else
        half occ = SAMPLE_TEXTURE2D_LOD(_OcclusionMap, sampler_OcclusionMap, uv, lod).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #endif
#else
    return half(1.0);
#endif
}

inline half3 SampleEmissionRT(float2 uv, float lod)
{
#ifndef _EMISSION
    return 0;
#else
    return SAMPLE_TEXTURE2D_LOD(_EmissionMap, sampler_EmissionMap, uv, lod).rgb * _EmissionColor.rgb;
#endif
}

// Path Tracing version of InitializeStandardLitSurfaceData
// Uses explicit LOD sampling suitable for ray tracing shaders
inline void InitializeStandardLitSurfaceDataRT(float2 uv, float lod, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlphaRT(uv, lod, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGlossRT(uv, lod, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

#if _SPECULAR_SETUP
    outSurfaceData.metallic = half(1.0);
    outSurfaceData.specular = specGloss.rgb;
#else
    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = SampleNormalRT(uv, lod, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale).xyz;
    outSurfaceData.occlusion = SampleOcclusionRT(uv, lod);
    outSurfaceData.emission = SampleEmissionRT(uv, lod);

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoat = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoat.r;
    outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
    outSurfaceData.clearCoatMask       = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
#endif

#if defined(_DETAIL)
    half detailMask = SAMPLE_TEXTURE2D_LOD(_DetailMask, sampler_DetailMask, uv, lod).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask, lod);
    outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask, lod);
#endif
}

// Helper for detail maps with explicit LOD
#if defined(_DETAIL)
inline half3 ApplyDetailAlbedo(float2 detailUv, half3 albedo, half detailMask, float lod)
{
#if defined(_DETAIL_MULX2)
    half3 detailAlbedo = SAMPLE_TEXTURE2D_LOD(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv, lod).rgb;
    albedo = albedo * LerpWhiteTo(detailAlbedo * 2.0, detailMask);
#elif defined(_DETAIL_SCALED)
    half3 detailAlbedo = SAMPLE_TEXTURE2D_LOD(_DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv, lod).rgb;
    albedo = lerp(albedo, albedo * detailAlbedo * 2.0, detailMask);
#endif
    return albedo;
}

inline half3 ApplyDetailNormal(float2 detailUv, half3 normalTS, half detailMask, float lod)
{
    half3 detailNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D_LOD(_DetailNormalMap, sampler_DetailNormalMap, detailUv, lod), _DetailNormalMapScale);
    normalTS = lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask);
    return normalTS;
}
#endif

// Ray differential approximation for automatic LOD calculation
// Based on "Texture Level of Detail Strategies for Real-Time Ray Tracing" (Akenine-Möller et al.)
// This provides better quality than fixed LOD by approximating texture footprint
struct RayDifferential
{
    float3 dOdx; // Ray origin derivative in x
    float3 dOdy; // Ray origin derivative in y
    float3 dDdx; // Ray direction derivative in x
    float3 dDdy; // Ray direction derivative in y
};

// Initialize ray differentials from camera
RayDifferential InitRayDifferential(float3 cameraPos, float3 rayDir, float2 pixelCoord, float2 screenSize)
{
    RayDifferential diff;
    
    // Simple approximation: neighboring pixel rays
    float pixelSpread = 1.0 / min(screenSize.x, screenSize.y);
    
    diff.dOdx = float3(0, 0, 0); // Camera origin doesn't change
    diff.dOdy = float3(0, 0, 0);
    diff.dDdx = float3(pixelSpread, 0, 0); // Approximate direction change
    diff.dDdy = float3(0, pixelSpread, 0);
    
    return diff;
}

// Compute texture LOD from ray differential at hit point
float ComputeTextureLODFromRayDifferential(RayDifferential diff, float t, float3 normalWS, float2 texCoord, float2 texelSize)
{
    // Propagate differential to hit point
    float3 dPdx = diff.dOdx + t * diff.dDdx;
    float3 dPdy = diff.dOdy + t * diff.dDdy;
    
    // Project to texture space (simplified - assumes planar mapping)
    float2 dUVdx = abs(float2(dot(dPdx, float3(1, 0, 0)), dot(dPdx, float3(0, 1, 0)))) / texelSize;
    float2 dUVdy = abs(float2(dot(dPdy, float3(1, 0, 0)), dot(dPdy, float3(0, 1, 0)))) / texelSize;
    
    // Compute LOD
    float deltaMax = max(length(dUVdx), length(dUVdy));
    float lod = log2(max(deltaMax, 1e-8));
    
    return max(0.0, lod);
}

// Simple distance-based LOD fallback (faster but less accurate)
float ComputeTextureLODFromDistance(float distance, float referenceDistance = 1.0)
{
    // LOD increases linearly with distance
    // referenceDistance is the distance where LOD = 0
    return log2(max(distance / referenceDistance, 1.0));
}

#endif // UNIVERSAL_LIT_INPUT_PATH_TRACING_INCLUDED

