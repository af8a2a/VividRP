#ifndef VIVIDRP_TERRAIN_LIT_SAMPLING_INCLUDED
#define VIVIDRP_TERRAIN_LIT_SAMPLING_INCLUDED

#if defined(VIVID_TERRAIN_LIGHTWEIGHT_INCLUDE)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#else
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#endif
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#if defined(_TERRAIN_8_LAYERS)
    #define VIVID_TERRAIN_LAYER_COUNT 8
#else
    #define VIVID_TERRAIN_LAYER_COUNT 4
#endif

struct TerrainLitSurfaceData
{
    float3 albedo;
    float3 normalTS;
    float smoothness;
    float metallic;
    float ao;
};

void InitializeTerrainLitSurfaceData(out TerrainLitSurfaceData surfaceData)
{
    surfaceData.albedo = 0.0;
    surfaceData.normalTS = float3(0.0, 0.0, 1.0);
    surfaceData.smoothness = 0.0;
    surfaceData.metallic = 0.0;
    surfaceData.ao = 1.0;
}

#define VIVID_DECLARE_TERRAIN_LAYER_PROPS(index) \
    float4 _Splat##index##_ST;                   \
    float4 _Splat##index##_TexelSize;            \
    float _Metallic##index;                      \
    float _Smoothness##index;                    \
    float _NormalScale##index;                   \
    float4 _DiffuseRemapScale##index;            \
    float4 _MaskMapRemapOffset##index;           \
    float4 _MaskMapRemapScale##index;            \
    float _LayerHasMask##index;                  \
    float _SmoothnessSource##index;

CBUFFER_START(UnityTerrain)
#if !defined(VIVID_TERRAIN_BASEMAP)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(0)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(1)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(2)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(3)
    float4 _Control0_TexelSize;
#if defined(_TERRAIN_8_LAYERS)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(4)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(5)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(6)
    VIVID_DECLARE_TERRAIN_LAYER_PROPS(7)
    float4 _Control1_TexelSize;
#endif
    float _HeightTransition;
    uint _NumLayersCount;
    float4 _Control0_ST;
#endif
#if defined(UNITY_INSTANCING_ENABLED)
    float4 _TerrainHeightmapRecipSize;
#endif
    float4 _TerrainHeightmapScale;
    float _ReceivesSSR;
    float _SupportDecals;
CBUFFER_END

#undef VIVID_DECLARE_TERRAIN_LAYER_PROPS

#if defined(UNITY_INSTANCING_ENABLED)
TEXTURE2D(_TerrainHeightmapTexture);
TEXTURE2D(_TerrainNormalmapTexture);
SAMPLER(sampler_TerrainNormalmapTexture);
#endif

#if defined(_ALPHATEST_ON)
TEXTURE2D(_TerrainHolesTexture);
SAMPLER(sampler_TerrainHolesTexture);
#endif

#if defined(VIVID_TERRAIN_BASEMAP)
TEXTURE2D(_MainTex);
TEXTURE2D(_MetallicTex);
SAMPLER(sampler_MainTex);
#else
TEXTURE2D(_Control0);
SAMPLER(sampler_Control0);

#define VIVID_DECLARE_TERRAIN_LAYER_TEXS(index) \
    TEXTURE2D(_Splat##index);                   \
    TEXTURE2D(_Normal##index);                  \
    TEXTURE2D(_Mask##index);

VIVID_DECLARE_TERRAIN_LAYER_TEXS(0)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(1)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(2)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(3)
#if defined(_TERRAIN_8_LAYERS)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(4)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(5)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(6)
VIVID_DECLARE_TERRAIN_LAYER_TEXS(7)
TEXTURE2D(_Control1);
#endif

#undef VIVID_DECLARE_TERRAIN_LAYER_TEXS

SAMPLER(sampler_Splat0);
#endif

void TerrainApplyHoleClip(float2 terrainUV)
{
#if defined(_ALPHATEST_ON)
    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, terrainUV).r;
    clip(hole - 0.5);
#endif
}

float VividTerrainSumWeights(float4 weights0, float4 weights1)
{
    float sum = weights0.x + weights0.y + weights0.z + weights0.w;
#if defined(_TERRAIN_8_LAYERS)
    sum += weights1.x + weights1.y + weights1.z + weights1.w;
#endif
    return sum;
}

void VividTerrainNormalizeWeights(inout float4 weights0, inout float4 weights1)
{
    float sum = VividTerrainSumWeights(weights0, weights1);
    if (sum > 1.0e-5)
    {
        float invSum = rcp(sum);
        weights0 *= invSum;
#if defined(_TERRAIN_8_LAYERS)
        weights1 *= invSum;
#endif
    }
    else
    {
        weights0 = float4(1.0, 0.0, 0.0, 0.0);
        weights1 = 0.0;
    }
}

float4 VividTerrainRemapMask(float4 maskSample, float blendWeight, float4 remapOffset, float4 remapScale)
{
    maskSample.b *= blendWeight;
    return maskSample * remapScale + remapOffset;
}

float4 VividTerrainDefaultMask(float metallic, float smoothness, float4 remapOffset, float4 remapScale)
{
    return float4(
        metallic,
        remapOffset.y + remapScale.y,
        remapOffset.z + 0.5 * remapScale.z,
        smoothness);
}

#if !defined(VIVID_TERRAIN_BASEMAP)
void TerrainSplatBlend(float2 controlUV, float2 splatBaseUV, inout TerrainLitSurfaceData surfaceData)
{
    float4 albedo[VIVID_TERRAIN_LAYER_COUNT];
    float3 normalTS[VIVID_TERRAIN_LAYER_COUNT];
    float4 masks[VIVID_TERRAIN_LAYER_COUNT];

#if defined(SHADER_STAGE_RAY_TRACING)
    float2 dxuv = 0.0;
    float2 dyuv = 0.0;
#else
    float2 dxuv = ddx(splatBaseUV);
    float2 dyuv = ddy(splatBaseUV);
#endif

    float2 controlSampleUV0 = (controlUV * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D(_Control0, sampler_Control0, controlSampleUV0);
#if defined(_TERRAIN_8_LAYERS)
    float2 controlSampleUV1 = (controlUV * (_Control1_TexelSize.zw - 1.0) + 0.5) * _Control1_TexelSize.xy;
    float4 blendMasks1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control0, controlSampleUV1);
#else
    float4 blendMasks1 = 0.0;
#endif

#if defined(_NORMALMAP)
    #define VIVID_TERRAIN_SAMPLE_NORMAL(index, uv, ddxuv, ddyuv) \
        UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_Normal##index, sampler_Splat0, uv, ddxuv, ddyuv), _NormalScale##index)
#else
    #define VIVID_TERRAIN_SAMPLE_NORMAL(index, uv, ddxuv, ddyuv) float3(0.0, 0.0, 1.0)
#endif

#if defined(_MASKMAP)
    #define VIVID_TERRAIN_SAMPLE_MASK(index, uv, ddxuv, ddyuv, blendWeight, smoothness) \
        lerp( \
            VividTerrainDefaultMask(_Metallic##index, smoothness, _MaskMapRemapOffset##index, _MaskMapRemapScale##index), \
            VividTerrainRemapMask(SAMPLE_TEXTURE2D_GRAD(_Mask##index, sampler_Splat0, uv, ddxuv, ddyuv), blendWeight, _MaskMapRemapOffset##index, _MaskMapRemapScale##index), \
            _LayerHasMask##index)
    #define VIVID_TERRAIN_NULL_MASK(index) float4(0.0, 1.0, _MaskMapRemapOffset##index.z, 0.0)
#else
    #define VIVID_TERRAIN_SAMPLE_MASK(index, uv, ddxuv, ddyuv, blendWeight, smoothness) \
        VividTerrainDefaultMask(_Metallic##index, smoothness, _MaskMapRemapOffset##index, _MaskMapRemapScale##index)
    #define VIVID_TERRAIN_NULL_MASK(index) float4(0.0, 1.0, 0.0, 0.0)
#endif

    #define VIVID_TERRAIN_SAMPLE_LAYER(index, blendWeight)                                                \
        UNITY_BRANCH                                                                                      \
        if ((blendWeight) > 0.0)                                                                          \
        {                                                                                                 \
            float2 layerUV = splatBaseUV * _Splat##index##_ST.xy + _Splat##index##_ST.zw;                 \
            float2 layerDx = dxuv * _Splat##index##_ST.x;                                                 \
            float2 layerDy = dyuv * _Splat##index##_ST.y;                                                 \
            albedo[index] = SAMPLE_TEXTURE2D_GRAD(_Splat##index, sampler_Splat0, layerUV, layerDx, layerDy); \
            albedo[index].rgb *= _DiffuseRemapScale##index.rgb;                                          \
            normalTS[index] = VIVID_TERRAIN_SAMPLE_NORMAL(index, layerUV, layerDx, layerDy);              \
            float layerSmoothness;                                                                        \
            if (_SmoothnessSource##index < 0.5)                                                           \
                layerSmoothness = albedo[index].a * _Smoothness##index;                                  \
            else if (_SmoothnessSource##index < 1.5)                                                      \
                layerSmoothness = albedo[index].a;                                                        \
            else                                                                                          \
                layerSmoothness = _Smoothness##index;                                                     \
            masks[index] = VIVID_TERRAIN_SAMPLE_MASK(index, layerUV, layerDx, layerDy, blendWeight, layerSmoothness); \
        }                                                                                                 \
        else                                                                                              \
        {                                                                                                 \
            albedo[index] = 0.0;                                                                          \
            normalTS[index] = 0.0;                                                                        \
            masks[index] = VIVID_TERRAIN_NULL_MASK(index);                                                \
        }

    VIVID_TERRAIN_SAMPLE_LAYER(0, blendMasks0.x)
    VIVID_TERRAIN_SAMPLE_LAYER(1, blendMasks0.y)
    VIVID_TERRAIN_SAMPLE_LAYER(2, blendMasks0.z)
    VIVID_TERRAIN_SAMPLE_LAYER(3, blendMasks0.w)
#if defined(_TERRAIN_8_LAYERS)
    VIVID_TERRAIN_SAMPLE_LAYER(4, blendMasks1.x)
    VIVID_TERRAIN_SAMPLE_LAYER(5, blendMasks1.y)
    VIVID_TERRAIN_SAMPLE_LAYER(6, blendMasks1.z)
    VIVID_TERRAIN_SAMPLE_LAYER(7, blendMasks1.w)
#endif

    #undef VIVID_TERRAIN_SAMPLE_LAYER
    #undef VIVID_TERRAIN_SAMPLE_MASK
    #undef VIVID_TERRAIN_NULL_MASK
    #undef VIVID_TERRAIN_SAMPLE_NORMAL

#if defined(_MASKMAP) && defined(_TERRAIN_BLEND_HEIGHT)
    float maxHeight = max(max(masks[0].z, masks[1].z), max(masks[2].z, masks[3].z));
#if defined(_TERRAIN_8_LAYERS)
    maxHeight = max(maxHeight, max(max(masks[4].z, masks[5].z), max(masks[6].z, masks[7].z)));
#endif
    float transition = max(_HeightTransition, 1.0e-5);
    float4 weightedHeights0 = max(0.0, float4(masks[0].z, masks[1].z, masks[2].z, masks[3].z) - maxHeight + transition);
    weightedHeights0 = (weightedHeights0 + 1.0e-6) * blendMasks0;
#if defined(_TERRAIN_8_LAYERS)
    float4 weightedHeights1 = max(0.0, float4(masks[4].z, masks[5].z, masks[6].z, masks[7].z) - maxHeight + transition);
    weightedHeights1 = (weightedHeights1 + 1.0e-6) * blendMasks1;
#else
    float4 weightedHeights1 = 0.0;
#endif
    blendMasks0 = weightedHeights0;
    blendMasks1 = weightedHeights1;
#elif defined(_MASKMAP)
    float4 densityWeights0 = saturate((float4(albedo[0].a, albedo[1].a, albedo[2].a, albedo[3].a) - (1.0 - blendMasks0)) * 20.0);
    densityWeights0 += 0.001 * blendMasks0;
    float4 useDefaultBlend0 = float4(_DiffuseRemapScale0.w, _DiffuseRemapScale1.w, _DiffuseRemapScale2.w, _DiffuseRemapScale3.w);
    blendMasks0 = lerp(densityWeights0, blendMasks0, useDefaultBlend0);
#if defined(_TERRAIN_8_LAYERS)
    float4 densityWeights1 = saturate((float4(albedo[4].a, albedo[5].a, albedo[6].a, albedo[7].a) - (1.0 - blendMasks1)) * 20.0);
    densityWeights1 += 0.001 * blendMasks1;
    float4 useDefaultBlend1 = float4(_DiffuseRemapScale4.w, _DiffuseRemapScale5.w, _DiffuseRemapScale6.w, _DiffuseRemapScale7.w);
    blendMasks1 = lerp(densityWeights1, blendMasks1, useDefaultBlend1);
#endif
#endif

    VividTerrainNormalizeWeights(blendMasks0, blendMasks1);

    float weights[VIVID_TERRAIN_LAYER_COUNT];
    weights[0] = blendMasks0.x;
    weights[1] = blendMasks0.y;
    weights[2] = blendMasks0.z;
    weights[3] = blendMasks0.w;
#if defined(_TERRAIN_8_LAYERS)
    weights[4] = blendMasks1.x;
    weights[5] = blendMasks1.y;
    weights[6] = blendMasks1.z;
    weights[7] = blendMasks1.w;
#endif

    surfaceData.albedo = 0.0;
    surfaceData.normalTS = 0.0;
    float3 packedMasks = 0.0;
    UNITY_UNROLL
    for (int layerIndex = 0; layerIndex < VIVID_TERRAIN_LAYER_COUNT; layerIndex++)
    {
        surfaceData.albedo += albedo[layerIndex].rgb * weights[layerIndex];
        surfaceData.normalTS += normalTS[layerIndex] * weights[layerIndex];
        packedMasks += masks[layerIndex].xyw * weights[layerIndex];
    }

#if defined(_NORMALMAP)
    surfaceData.normalTS = SafeNormalize(surfaceData.normalTS);
#else
    surfaceData.normalTS = float3(0.0, 0.0, 1.0);
#endif
    surfaceData.metallic = packedMasks.x;
    surfaceData.ao = packedMasks.y;
    surfaceData.smoothness = packedMasks.z;
}
#endif

void TerrainLitShade(float2 terrainUV, inout TerrainLitSurfaceData surfaceData)
{
#if defined(VIVID_TERRAIN_BASEMAP)
    float4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, terrainUV);
    float4 metallicTex = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MainTex, terrainUV);
    surfaceData.albedo = mainTex.rgb;
    surfaceData.normalTS = float3(0.0, 0.0, 1.0);
    surfaceData.smoothness = mainTex.a;
    surfaceData.metallic = metallicTex.r;
    surfaceData.ao = metallicTex.g;
#else
    TerrainSplatBlend(terrainUV, terrainUV, surfaceData);
#endif
}

#endif
