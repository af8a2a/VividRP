#ifndef VIVIDRP_REFERENCED_PATH_TRACING_RTXTF_INCLUDED
#define VIVIDRP_REFERENCED_PATH_TRACING_RTXTF_INCLUDED

// Ray-tracing closest-hit shaders are DXIL library shaders. Collaborative
// magnification relies on coherent screen-space lanes, which a material hit
// group cannot guarantee, so this integration deliberately uses RTXTF's
// per-lane stochastic filter path without wave sharing.
#define STF_SHADER_STAGE 9
#define STF_SHADER_MODEL_MAJOR 6
#define STF_SHADER_MODEL_MINOR 5
#define STF_ALLOW_WAVE_READ 0
// Unity's ray-tracing frontend does not universally expose DXC's native
// 16-bit vector aliases. The affected helpers are wave-only and compiled out
// above, but their signatures still need portable types during parsing.
#define uint16_t uint
#define uint16_t2 uint2
#define uint16_t4 uint4
#include "Packages/com.vivid.render-pipelines/Shaders/ThirdParty/RTXTF/STFSamplerState.hlsli"
#undef uint16_t
#undef uint16_t2
#undef uint16_t4

int _ReferencedRTXTFEnabled;
int _ReferencedRTXTFMode;
float _ReferencedRTXTFGaussianSigma;
int _ReferencedFrameIndex;

bool ReferencedPathtracingRTXTFIsEnabled()
{
#if defined(_ALPHATEST_ON) \
    || defined(_SURFACE_TYPE_TRANSPARENT) \
    || defined(_VIRTUAL_TEXTURE_BASE_COLOR)
    return false;
#else
    return _ReferencedRTXTFEnabled != 0;
#endif
}

STF_SamplerState ReferencedPathtracingCreateRTXTFState(float3 randomSample)
{
    const float kMinimumRandom = 1.0 / 16777216.0;
    randomSample = clamp(
        randomSample,
        kMinimumRandom,
        1.0 - kMinimumRandom);
    STF_SamplerState samplerState = STF_SamplerState::Create(
        float4(randomSample.xy, 0.0, randomSample.z));
    samplerState.SetFilterType((uint)clamp(
        _ReferencedRTXTFMode,
        STF_FILTER_TYPE_LINEAR,
        STF_FILTER_TYPE_GAUSSIAN));
    samplerState.SetFrameIndex((uint)max(_ReferencedFrameIndex, 0));
    samplerState.SetAnisoMethod(STF_ANISO_LOD_METHOD_NONE);
    samplerState.SetMagMethod(STF_MAGNIFICATION_METHOD_NONE);
    samplerState.SetSigma(clamp(
        _ReferencedRTXTFGaussianSigma,
        0.05,
        4.0));
    samplerState.SetReseedOnSample(true);
    return samplerState;
}

float4 ReferencedPathtracingSampleRTXTFTexture2DLevel(
    inout STF_SamplerState samplerState,
    Texture2D textureObject,
    SamplerState textureSampler,
    float2 uv,
    float textureLod)
{
    if (ReferencedPathtracingRTXTFIsEnabled())
    {
        return samplerState.Texture2DSampleLevel(
            textureObject,
            textureSampler,
            uv,
            textureLod);
    }

    return textureObject.SampleLevel(textureSampler, uv, textureLod);
}

float4 ReferencedPathtracingSampleBaseRTXTF(
    inout STF_SamplerState samplerState,
    float2 uv,
    float textureLod)
{
    float4 baseSample = ReferencedPathtracingSampleRTXTFTexture2DLevel(
        samplerState,
        _BaseMap,
        sampler_BaseMap,
        uv,
        textureLod) * _BaseColor;
#if defined(_OPACITYMAP)
    baseSample.a *= ReferencedPathtracingSampleRTXTFTexture2DLevel(
        samplerState,
        _OpacityMap,
        sampler_OpacityMap,
        uv,
        textureLod).r;
#endif
    return baseSample;
}

float ReferencedPathtracingSampleTransmissionWeightRTXTF(
    inout STF_SamplerState samplerState,
    float2 uv,
    float textureLod)
{
    float transmissionWeight = saturate(_TransmissionWeight);
#if defined(_TRANSMISSIONMAP)
    transmissionWeight *= saturate(
        ReferencedPathtracingSampleRTXTFTexture2DLevel(
            samplerState,
            _TransmissionMap,
            sampler_TransmissionMap,
            uv,
            textureLod).r);
#endif
    return saturate(transmissionWeight);
}

float2 ReferencedPathtracingSampleMetallicSmoothnessRTXTF(
    inout STF_SamplerState samplerState,
    float2 uv,
    float baseAlpha,
    float textureLod)
{
    float metallic = saturate(_Metallic);
    float smoothness = saturate(_Smoothness);

#if defined(_RMOMAP)
    float3 rmoSample =
        ReferencedPathtracingSampleRTXTFTexture2DLevel(
            samplerState,
            _RMOMap,
            sampler_RMOMap,
            uv,
            textureLod).rgb;
    metallic = lerp(
        _MetallicRemapMin,
        _MetallicRemapMax,
        saturate(rmoSample.g));
    smoothness = lerp(
        _SmoothnessRemapMin,
        _SmoothnessRemapMax,
        saturate(1.0 - rmoSample.r));
#elif defined(_METALLICSPECGLOSSMAP)
    float4 metallicGlossSample =
        ReferencedPathtracingSampleRTXTFTexture2DLevel(
            samplerState,
            _MetallicGlossMap,
            sampler_MetallicGlossMap,
            uv,
            textureLod);
    metallic = lerp(
        _MetallicRemapMin,
        _MetallicRemapMax,
        saturate(metallicGlossSample.r));

    #if defined(_ROUGHNESSMAP)
        float roughness = ReferencedPathtracingSampleRTXTFTexture2DLevel(
            samplerState,
            _RoughnessMap,
            sampler_RoughnessMap,
            uv,
            textureLod).r;
        smoothness = lerp(
            _SmoothnessRemapMin,
            _SmoothnessRemapMax,
            saturate(1.0 - roughness));
    #elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
        smoothness = lerp(
            _SmoothnessRemapMin,
            _SmoothnessRemapMax,
            saturate(baseAlpha));
    #else
        smoothness = lerp(
            _SmoothnessRemapMin,
            _SmoothnessRemapMax,
            saturate(metallicGlossSample.a));
    #endif
#elif defined(_ROUGHNESSMAP)
    float roughness = ReferencedPathtracingSampleRTXTFTexture2DLevel(
        samplerState,
        _RoughnessMap,
        sampler_RoughnessMap,
        uv,
        textureLod).r;
    smoothness = lerp(
        _SmoothnessRemapMin,
        _SmoothnessRemapMax,
        saturate(1.0 - roughness));
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = lerp(
        _SmoothnessRemapMin,
        _SmoothnessRemapMax,
        saturate(baseAlpha));
#endif

    return float2(metallic, saturate(smoothness));
}

float3 ReferencedPathtracingSampleEmissionRTXTF(
    inout STF_SamplerState samplerState,
    float2 uv,
    float textureLod)
{
#if defined(_EMISSION)
    return max(
        ReferencedPathtracingSampleRTXTFTexture2DLevel(
            samplerState,
            _EmissionMap,
            sampler_EmissionMap,
            uv,
            textureLod).rgb
        * _EmissionColor.rgb,
        0.0);
#else
    return 0.0;
#endif
}

float3 ReferencedPathtracingSampleNormalWSRTXTF(
    inout STF_SamplerState samplerState,
    VividIndirectDiffuseHitGeometry geometry,
    float textureLod)
{
    float3 normalWS = SafeNormalize(geometry.normalWS);

#if defined(_NORMALMAP)
    float tangentLengthSquared = dot(
        geometry.tangentWS,
        geometry.tangentWS);
    if (tangentLengthSquared > 1e-8)
    {
        float3 tangentWS = geometry.tangentWS
            * rsqrt(tangentLengthSquared);
        float3 bitangentWS = SafeNormalize(
            cross(normalWS, tangentWS) * geometry.tangentSign);
        float3 normalTS = UnpackVividNormalScale(
            ReferencedPathtracingSampleRTXTFTexture2DLevel(
                samplerState,
                _BumpMap,
                sampler_BumpMap,
                geometry.uv,
                textureLod),
            _BumpScale);
        normalWS = SafeNormalize(
            normalTS.x * tangentWS
            + normalTS.y * bitangentWS
            + normalTS.z * normalWS);
    }
#endif

    return normalWS;
}

#endif
