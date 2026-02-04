#ifndef UNIVERSAL_LIT_INPUT_PATH_TRACING_RTXTF_INCLUDED
#define UNIVERSAL_LIT_INPUT_PATH_TRACING_RTXTF_INCLUDED

// RTXTF-enabled texture sampling for path tracing
// Wraps standard texture sampling with RTXTF stochastic filtering

// Initialize RTXTF sampler state
void InitRTXTF(inout STF_SamplerState stfSamplerState, uint2 pixelPosition)
{
    // Generate spatio-temporal white noise for RTXTF
    float3 u = RNG::SpatioTemporalWhiteNoise3D(pixelPosition, _RTXTFFrameIndex);

    // Create STF sampler state
    stfSamplerState = STF_SamplerState::Create(float4(u.x, u.y, 0 /*slice - unused*/, u.z));

    // Configure from shader parameters
    stfSamplerState.SetFilterType(_RTXTFFilterType);
    stfSamplerState.SetFrameIndex(_RTXTFFrameIndex);
    stfSamplerState.SetMagMethod(_RTXTFMagnificationMethod);
    stfSamplerState.SetAnisoMethod(_RTXTFAnisotropyMethod);
    stfSamplerState.SetSigma(_RTXTFGaussianSigma);
    stfSamplerState.SetReseedOnSample(_RTXTFReseedOnSample != 0);
}

// Sample texture with RTXTF or fallback to standard sampling
float4 SampleTextureRTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                          Texture2D texture, SamplerState textureSampler, float2 texCoord, float mipLevel)
{
    if (rtxtfEnabled && _RTXTFEnable)
    {
        return stfSamplerState.Texture2DSampleLevel(texture, textureSampler, texCoord, mipLevel);
    }
    else
    {
        return texture.SampleLevel(textureSampler, texCoord, mipLevel);
    }
}

// RTXTF-enabled versions of material sampling functions

inline half4 SampleAlbedoAlphaRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                        float2 uv, float lod, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return half4(SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, albedoAlphaMap, sampler_albedoAlphaMap, uv, lod));
}

inline half4 SampleNormalRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                   float2 uv, float lod, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
#ifdef _NORMALMAP
    half4 n = SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, bumpMap, sampler_bumpMap, uv, lod);
    #if BUMP_SCALE_NOT_SUPPORTED
        return half4(UnpackNormal(n), 0);
    #else
        return half4(UnpackNormalScale(n, scale), 0);
    #endif
#else
    return half4(0.0h, 0.0h, 1.0h, 0.0h);
#endif
}

inline half4 SampleMetallicSpecGlossRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                              float2 uv, float lod, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _MetallicGlossMap, sampler_MetallicGlossMap, uv, lod));
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

inline half SampleOcclusionRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled, float2 uv, float lod)
{
#ifdef _OCCLUSIONMAP
    #if defined(SHADER_API_GLES)
        return SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _OcclusionMap, sampler_OcclusionMap, uv, lod).g;
    #else
        half occ = SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _OcclusionMap, sampler_OcclusionMap, uv, lod).g;
        return LerpWhiteTo(occ, _OcclusionStrength);
    #endif
#else
    return half(1.0);
#endif
}

inline half3 SampleEmissionRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled, float2 uv, float lod)
{
#ifndef _EMISSION
    return 0;
#else
    return SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _EmissionMap, sampler_EmissionMap, uv, lod).rgb * _EmissionColor.rgb;
#endif
}

// Helper for detail maps with RTXTF
#if defined(_DETAIL)
inline half3 ApplyDetailAlbedo_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                      float2 detailUv, half3 albedo, half detailMask, float lod)
{
#if defined(_DETAIL_MULX2)
    half3 detailAlbedo = SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv, lod).rgb;
    albedo = albedo * LerpWhiteTo(detailAlbedo * 2.0, detailMask);
#elif defined(_DETAIL_SCALED)
    half3 detailAlbedo = SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _DetailAlbedoMap, sampler_DetailAlbedoMap, detailUv, lod).rgb;
    albedo = lerp(albedo, albedo * detailAlbedo * 2.0, detailMask);
#endif
    return albedo;
}

inline half3 ApplyDetailNormal_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                      float2 detailUv, half3 normalTS, half detailMask, float lod)
{
    half3 detailNormalTS = UnpackNormalScale(SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _DetailNormalMap, sampler_DetailNormalMap, detailUv, lod), _DetailNormalMapScale);
    normalTS = lerp(normalTS, BlendNormalRNM(normalTS, detailNormalTS), detailMask);
    return normalTS;
}
#endif

// RTXTF-enabled version of InitializeStandardLitSurfaceDataRT
inline void InitializeStandardLitSurfaceDataRT_RTXTF(inout STF_SamplerState stfSamplerState, bool rtxtfEnabled,
                                                      float2 uv, float lod, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlphaRT_RTXTF(stfSamplerState, rtxtfEnabled, uv, lod, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGlossRT_RTXTF(stfSamplerState, rtxtfEnabled, uv, lod, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;

#if _SPECULAR_SETUP
    outSurfaceData.metallic = half(1.0);
    outSurfaceData.specular = specGloss.rgb;
#else
    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);
#endif

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = SampleNormalRT_RTXTF(stfSamplerState, rtxtfEnabled, uv, lod, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale).xyz;
    outSurfaceData.occlusion = SampleOcclusionRT_RTXTF(stfSamplerState, rtxtfEnabled, uv, lod);
    outSurfaceData.emission = SampleEmissionRT_RTXTF(stfSamplerState, rtxtfEnabled, uv, lod);

#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half2 clearCoat = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoat.r;
    outSurfaceData.clearCoatSmoothness = clearCoat.g;
#else
    outSurfaceData.clearCoatMask       = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
#endif

#if defined(_DETAIL)
    half detailMask = SampleTextureRTXTF(stfSamplerState, rtxtfEnabled, _DetailMask, sampler_DetailMask, uv, lod).a;
    float2 detailUv = uv * _DetailAlbedoMap_ST.xy + _DetailAlbedoMap_ST.zw;
    outSurfaceData.albedo = ApplyDetailAlbedo_RTXTF(stfSamplerState, rtxtfEnabled, detailUv, outSurfaceData.albedo, detailMask, lod);
    outSurfaceData.normalTS = ApplyDetailNormal_RTXTF(stfSamplerState, rtxtfEnabled, detailUv, outSurfaceData.normalTS, detailMask, lod);
#endif
}

#endif // UNIVERSAL_LIT_INPUT_PATH_TRACING_RTXTF_INCLUDED
