#ifndef VIVIDRP_TERRAIN_LIT_PASS_INCLUDED
#define VIVIDRP_TERRAIN_LIT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/VividProbeVolume.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/MotionVectorsCommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Material/TerrainLit/TerrainLitSampling.hlsl"
#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)
#include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/GPUDrivenDecalGBuffer.hlsl"
#endif

float4 _ShadowBias;

#if defined(UNITY_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
UNITY_INSTANCING_BUFFER_END(Terrain)
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    float3 positionOld : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 terrainUV : TEXCOORD2;
    float2 lightmapUV : TEXCOORD3;
    float2 terrainNormalUV : TEXCOORD4;
    float4 positionCSNoJitter : TEXCOORD5;
    float4 previousPositionCSNoJitter : TEXCOORD6;
#if defined(EDITOR_VISUALIZATION)
    float2 vizUV : TEXCOORD7;
    float4 lightCoord : TEXCOORD8;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct TerrainVertexData
{
    float3 positionOS;
    float3 previousPositionOS;
    float3 normalOS;
    float2 terrainUV;
    float2 terrainNormalUV;
};

float4 ApplyVividTerrainShadowClamping(float4 positionCS)
{
#if UNITY_REVERSED_Z
    float clampedZ = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    float clampedZ = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif

    positionCS.z = lerp(positionCS.z, clampedZ, round(_ShadowBias.z) == 1.0 ? 1.0 : 0.0);
    return positionCS;
}

float4 ConstructTerrainTangentWS(float3 normalWS)
{
    float3 positiveZWS = normalize(TransformObjectToWorldDir(float3(0.0, 0.0, 1.0)));
    float3 tangentWS = SafeNormalize(cross(normalWS, positiveZWS));
    return float4(tangentWS, -1.0);
}

TerrainVertexData BuildTerrainVertexData(Attributes input)
{
    TerrainVertexData data;
    data.positionOS = input.positionOS.xyz;
    data.previousPositionOS = unity_MotionVectorsParams.x == 1.0 ? input.positionOld : input.positionOS.xyz;
    data.normalOS = input.normalOS;
    data.terrainUV = input.uv;
    data.terrainNormalUV = input.uv;

#if defined(UNITY_INSTANCING_ENABLED)
    float2 patchVertex = input.positionOS.xy;
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (patchVertex + instanceData.xy) * instanceData.z;
    float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

    data.positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    data.positionOS.y = height * _TerrainHeightmapScale.y;
    data.previousPositionOS = data.positionOS;
    data.terrainUV = sampleCoords * _TerrainHeightmapRecipSize.zw;
    data.terrainNormalUV = (sampleCoords + 0.5) * _TerrainHeightmapRecipSize.xy;
    data.normalOS = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2.0 - 1.0;
#endif

    return data;
}

float3 GetTerrainBaseNormalWS(Varyings input)
{
    float3 normalWS = SafeNormalize(input.normalWS);
#if defined(UNITY_INSTANCING_ENABLED) && defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL)
    float3 normalOS = SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, input.terrainNormalUV).rgb * 2.0 - 1.0;
    normalWS = SafeNormalize(TransformObjectToWorldNormal(normalOS));
#endif
    return normalWS;
}

float3 TransformTerrainNormalToWorld(Varyings input, float3 normalTS)
{
    float3 normalWS = GetTerrainBaseNormalWS(input);
#if defined(_NORMALMAP)
    float4 tangentWS = ConstructTerrainTangentWS(normalWS);
    float3 bitangentWS = SafeNormalize(cross(normalWS, tangentWS.xyz) * tangentWS.w);
    return SafeNormalize(normalTS.x * tangentWS.xyz + normalTS.y * bitangentWS + normalTS.z * normalWS);
#else
    return normalWS;
#endif
}

float3 SampleTerrainBakedGI(float2 lightmapUV, float3 normalWS, float3 positionWS)
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

float HasTerrainBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return VividHasProbeVolumeGI() ? 1.0 : 0.0;
#endif
}

uint GetTerrainMaterialFeatures()
{
    uint materialFeatures = VIVID_MATERIALFEATURE_LIT;

    if (_ReceivesSSR > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_SSR_RECEIVE;

    if (_SupportDecals > 0.5)
        materialFeatures |= VIVID_MATERIALFEATURE_DECAL_RECEIVE;

    return materialFeatures;
}

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    TerrainVertexData terrainData = BuildTerrainVertexData(input);
    output.positionWS = TransformObjectToWorld(terrainData.positionOS);
    output.normalWS = TransformObjectToWorldNormal(terrainData.normalOS);
    output.terrainUV = terrainData.terrainUV;
    output.terrainNormalUV = terrainData.terrainNormalUV;
    output.lightmapUV = TransformVividLightmapUV(terrainData.terrainUV);
    output.positionCS = TransformWorldToHClip(output.positionWS);

#if defined(VIVID_TERRAIN_PASS_SHADOW)
    output.positionCS = ApplyVividTerrainShadowClamping(output.positionCS);
#endif

    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, float4(terrainData.positionOS, 1.0)));
    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, float4(terrainData.previousPositionOS, 1.0)));

#if defined(VIVID_TERRAIN_PASS_META)
    output.positionCS = UnityMetaVertexPosition(
        terrainData.positionOS,
        terrainData.terrainUV,
        terrainData.terrainUV,
        unity_LightmapST,
        unity_DynamicLightmapST);
#if defined(EDITOR_VISUALIZATION)
    UnityEditorVizData(terrainData.positionOS, terrainData.terrainUV, terrainData.terrainUV, terrainData.terrainUV, output.vizUV, output.lightCoord);
#endif
#endif

    return output;
}

VividGBufferSurfaceData BuildTerrainGBufferSurfaceData(Varyings input)
{
    TerrainApplyHoleClip(input.terrainUV);

    TerrainLitSurfaceData terrainSurface;
    InitializeTerrainLitSurfaceData(terrainSurface);
    TerrainLitShade(input.terrainUV, terrainSurface);

    float smoothness = saturate(terrainSurface.smoothness);
    float3 normalWS = TransformTerrainNormalToWorld(input, terrainSurface.normalTS);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = terrainSurface.albedo;
    surfaceData.normalWS = normalWS;
    surfaceData.linearRoughness = (1.0 - smoothness) * (1.0 - smoothness);
    surfaceData.metallic = terrainSurface.metallic;
    surfaceData.ambientOcclusion = terrainSurface.ao;
    surfaceData.customData = 0.0;
    surfaceData.customData1 = 0.0;
    surfaceData.materialFeatures = GetTerrainMaterialFeatures();
    surfaceData.emissive = 0.0;
    surfaceData.builtinData = BuildVividBuiltinData(
        SampleTerrainBakedGI(input.lightmapUV, normalWS, input.positionWS),
        HasTerrainBakedGI(),
        input.lightmapUV,
        input.positionWS);
    return surfaceData;
}

VividGBufferFragmentOutput FragGBuffer(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    VividGBufferSurfaceData surfaceData = BuildTerrainGBufferSurfaceData(input);
#if defined(VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER)
    ApplyVividGPUDrivenDecalsToGBufferSurfaceData(surfaceData, input.positionWS, (uint2)input.positionCS.xy);
#endif
    return PackVividGBufferSurfaceData(surfaceData);
}

half4 FragPreDepth(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    TerrainApplyHoleClip(input.terrainUV);
    return 0.0;
}

half4 FragShadow(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);

    TerrainApplyHoleClip(input.terrainUV);
    return 0.0;
}

float4 FragMeta(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    TerrainApplyHoleClip(input.terrainUV);

    TerrainLitSurfaceData terrainSurface;
    InitializeTerrainLitSurfaceData(terrainSurface);
    TerrainLitShade(input.terrainUV, terrainSurface);

    UnityMetaInput metaInput;
    metaInput.Albedo = saturate(terrainSurface.albedo) * (1.0 - saturate(terrainSurface.metallic));
    metaInput.Emission = 0.0;
#if defined(EDITOR_VISUALIZATION)
    metaInput.VizUV = input.vizUV;
    metaInput.LightCoord = input.lightCoord;
#endif

    return UnityMetaFragment(metaInput);
}

float4 FragMotionVectors(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    TerrainApplyHoleClip(input.terrainUV);
    return EncodeMotionVectorFromCsPositions(input.positionCSNoJitter, input.previousPositionCSNoJitter);
}

#endif
