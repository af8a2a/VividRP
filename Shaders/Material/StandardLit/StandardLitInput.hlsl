#ifndef VIVIDRP_STANDARD_LIT_INPUT_INCLUDED
#define VIVIDRP_STANDARD_LIT_INPUT_INCLUDED

#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS)
#define FRAG_INPUTS_USE_META_EDITOR_VIS
#endif

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/FragInputs.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividProbeVolume.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#if defined(VIVIDRP_SHADERPASS_META)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
#endif
#if defined(_VIRTUAL_TEXTURE_BASE_COLOR)
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl"
#endif
#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)
#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/GPUDrivenDecalGBuffer.hlsl"
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float4 _EmissionColor;
    float4 _TransmissionColor;
    float _Cutoff;
    float _Smoothness;
    float _SmoothnessTextureChannel;
    float _Metallic;
    float _MetallicRemapMin;
    float _MetallicRemapMax;
    float _SmoothnessRemapMin;
    float _SmoothnessRemapMax;
    float _AORemapMin;
    float _AORemapMax;
    float _BumpScale;
    float _OcclusionStrength;
    float _ClearCoatMask;
    float _ClearCoatSmoothness;
    float _AlphaClip;
    float _WorkflowMode;
    float _ReceiveSSR;
    float _ReceiveDecals;
    float _ThinWalledTransmission;
    float _TransmissionWeight;
    float _SpecularIOR;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);
TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_RoughnessMap);
SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

float2 TransformStandardLitBaseUV(float2 uv)
{
    return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
}

float2 GetStandardLitBaseUV(FragInputs input)
{
    return TransformStandardLitBaseUV(input.texCoord0.xy);
}

float3 UnpackVividNormalScale(float4 packedNormal, float scale)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.xy *= scale;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

#if defined(_VIRTUAL_TEXTURE_BASE_COLOR)
float4 SampleVirtualTextureBase(float2 uv, float4 positionSS)
{
    VTMipRange requestedMips = VTComputeRequestedMipRange(uv);
    VTResolvedAddress lowerResolved = VTResolveAddress(uv, requestedMips.lowerMip);
    VTResolvedAddress upperResolved = VTResolveAddress(uv, requestedMips.upperMip);

    if (!lowerResolved.resident)
        VTWriteFeedback(uv, requestedMips.lowerMip, positionSS);

    if (requestedMips.upperMip != requestedMips.lowerMip && !upperResolved.resident)
        VTWriteFeedback(uv, requestedMips.upperMip, positionSS);

    VTWriteFallbackSample(uv, requestedMips.lowerMip, lowerResolved, positionSS);
    if (!VTResolvedAddressMatches(lowerResolved, upperResolved))
        VTWriteFallbackSample(uv, requestedMips.upperMip, upperResolved, positionSS);

    return VTSampleBaseColor(uv, lowerResolved, upperResolved, requestedMips.blend);
}

float4 SampleVirtualTextureBase(float2 uv)
{
    return SampleVirtualTextureBase(uv, float4(0.0, 0.0, 0.0, 0.0));
}
#endif

float4 SampleBase(float2 uv, float4 positionSS)
{
#if defined(_VIRTUAL_TEXTURE_BASE_COLOR)
    float4 baseSample = SampleVirtualTextureBase(uv, positionSS) * _BaseColor;
#else
    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
#endif
#if defined(_OPACITYMAP)
    baseSample.a *= SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r;
#endif
    return baseSample;
}

float4 SampleBase(float2 uv)
{
    return SampleBase(uv, float4(0.0, 0.0, 0.0, 0.0));
}

void ApplyAlphaClip(float alpha)
{
#if defined(_ALPHATEST_ON)
    clip(alpha - _Cutoff);
#endif
}

void VividApplyAlphaClip(FragInputs input)
{
    ApplyAlphaClip(SampleBase(GetStandardLitBaseUV(input), input.positionSS).a);
}

float2 SampleMetallicSmoothness(float2 uv, float baseAlpha)
{
    float metallic = saturate(_Metallic);
    float smoothness = saturate(_Smoothness);

#if defined(_METALLICSPECGLOSSMAP)
    float4 metallicGlossSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    metallic = lerp(_MetallicRemapMin, _MetallicRemapMax, saturate(metallicGlossSample.r));

#if defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r;
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(1.0 - roughness));
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(baseAlpha));
#else
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(metallicGlossSample.a));
#endif
#elif defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r;
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(1.0 - roughness));
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, saturate(baseAlpha));
#endif

    return float2(metallic, saturate(smoothness));
}

float SampleMetallic(float2 uv)
{
    float metallic = saturate(_Metallic);
#if defined(_METALLICSPECGLOSSMAP)
    metallic = lerp(_MetallicRemapMin, _MetallicRemapMax, saturate(SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).r));
#endif
    return metallic;
}

float SampleAmbientOcclusion(float2 uv)
{
#if defined(_OCCLUSIONMAP)
    float occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
    return saturate(lerp(_AORemapMin, _AORemapMax, occlusion));
#else
    return 1.0;
#endif
}

float3 SampleEmission(float2 uv)
{
#if defined(_EMISSION)
    return max(SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb, 0.0);
#else
    return 0.0;
#endif
}

float3 SampleNormalWS(FragInputs input, float2 uv)
{
    float3 normalWS = normalize(input.tangentToWorld[2]);
#if defined(_NORMALMAP)
    float3 normalTS = UnpackVividNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
    return normalize(
        normalTS.x * input.tangentToWorld[0]
        + normalTS.y * input.tangentToWorld[1]
        + normalTS.z * normalWS);
#else
    return normalWS;
#endif
}

float3 SampleStandardLitBakedGI(float2 lightmapUV, float3 normalWS, float3 positionWS)
{
#if defined(LIGHTMAP_ON)
    return SampleVividBakedGI(lightmapUV, normalWS);
#else
    return SampleVividProbeVolume(
        positionWS,
        normalWS,
        GetWorldSpaceNormalizeViewDir(positionWS),
        GetMeshRenderingLayerMask());
#endif
}

float HasStandardLitBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return VividHasProbeVolumeGI() ? 1.0 : 0.0;
#endif
}

uint GetStandardLitMaterialFeatures(float clearCoatMask)
{
    uint materialFeatures = VIVID_MATERIALFEATURE_LIT;

    if (_ReceiveSSR > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_SSR_RECEIVE;

    if (_ReceiveDecals > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_DECAL_RECEIVE;

    if (clearCoatMask > 0.0)
        materialFeatures |= VIVID_MATERIALFEATURE_CLEAR_COAT;

    return materialFeatures;
}

VividGBufferSurfaceData BuildStandardLitSurfaceData(FragInputs input)
{
    float2 uv = GetStandardLitBaseUV(input);
    float4 baseSample = SampleBase(uv, input.positionSS);
    ApplyAlphaClip(baseSample.a);

    float2 metallicSmoothness = SampleMetallicSmoothness(uv, baseSample.a);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = baseSample.rgb;
    surfaceData.normalWS = SampleNormalWS(input, uv);
    surfaceData.linearRoughness = (1.0 - metallicSmoothness.y) * (1.0 - metallicSmoothness.y);
    surfaceData.metallic = metallicSmoothness.x;
    surfaceData.ambientOcclusion = SampleAmbientOcclusion(uv);
    surfaceData.customData1 = 0.0;

#if defined(_CLEARCOAT)
    float clearCoatMask = saturate(_ClearCoatMask);
    surfaceData.customData = clearCoatMask;
#else
    float clearCoatMask = 0.0;
    surfaceData.customData = 0.0;
#endif
    surfaceData.materialFeatures = GetStandardLitMaterialFeatures(clearCoatMask);

    surfaceData.emissive = SampleEmission(uv);
    float2 lightmapUV = TransformVividLightmapUV(input.texCoord1.xy);
    surfaceData.builtinData = BuildVividBuiltinData(
        SampleStandardLitBakedGI(lightmapUV, surfaceData.normalWS, input.positionRWS),
        HasStandardLitBakedGI(),
        lightmapUV,
        input.positionRWS);
    return surfaceData;
}

VividGBufferSurfaceData VividBuildGBufferSurfaceData(FragInputs input)
{
    return BuildStandardLitSurfaceData(input);
}

float3 VividGetDebugColor(FragInputs input)
{
    VividGBufferSurfaceData surfaceData = BuildStandardLitSurfaceData(input);
    return surfaceData.baseColor + surfaceData.emissive;
}

float3 GetLightTransportDiffuseColor(float3 baseColor, float metallic)
{
    return saturate(baseColor) * (1.0 - saturate(metallic));
}

#if defined(VIVIDRP_SHADERPASS_META)
UnityMetaInput VividBuildMetaInput(FragInputs input)
{
    float2 uv = GetStandardLitBaseUV(input);
    float4 baseSample = SampleBase(uv);
    ApplyAlphaClip(baseSample.a);

    UnityMetaInput metaInput;
    metaInput.Albedo = GetLightTransportDiffuseColor(baseSample.rgb, SampleMetallic(uv));
    metaInput.Emission = SampleEmission(uv);
#if defined(EDITOR_VISUALIZATION)
    metaInput.VizUV = input.metaVizUV;
    metaInput.LightCoord = input.metaLightCoord;
#endif

    return metaInput;
}
#endif

#endif
