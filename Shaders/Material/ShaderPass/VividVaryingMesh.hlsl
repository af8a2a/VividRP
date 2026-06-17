#ifndef VIVIDRP_VARYING_MESH_INCLUDED
#define VIVIDRP_VARYING_MESH_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"

#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS)
#define FRAG_INPUTS_USE_META_EDITOR_VIS
#endif
#include "Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/FragInputs.hlsl"

#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD) && !defined(VIVIDRP_ATTRIBUTES_NEED_NORMAL)
#error VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD requires VIVIDRP_ATTRIBUTES_NEED_NORMAL.
#endif

#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD) && !defined(VIVIDRP_ATTRIBUTES_NEED_TANGENT)
#error VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD requires VIVIDRP_ATTRIBUTES_NEED_TANGENT.
#endif

#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1) && !defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0)
#define VIVIDRP_VARYINGS_NEED_TEXCOORD0
#endif

#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2) && !defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
#define VIVIDRP_VARYINGS_NEED_TEXCOORD1
#endif

struct VividAttributesMesh
{
    float4 positionOS : POSITION;
#if defined(VIVIDRP_ATTRIBUTES_NEED_NORMAL)
    float3 normalOS : NORMAL;
#endif
#if defined(VIVIDRP_ATTRIBUTES_NEED_TANGENT)
    float4 tangentOS : TANGENT;
#endif
#if defined(VIVIDRP_ATTRIBUTES_NEED_TEXCOORD0)
    float2 uv0 : TEXCOORD0;
#endif
#if defined(VIVIDRP_ATTRIBUTES_NEED_TEXCOORD1)
    float2 uv1 : TEXCOORD1;
#endif
#if defined(VIVIDRP_ATTRIBUTES_NEED_TEXCOORD2)
    float2 uv2 : TEXCOORD2;
#endif
#if defined(VIVIDRP_ATTRIBUTES_NEED_PREVIOUS_POSITION)
    float3 previousPositionOS : TEXCOORD4;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VividVaryingsMesh
{
    float4 positionCS;
#if defined(VIVIDRP_VARYINGS_NEED_POSITION_WS)
    float3 positionWS;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD)
    float3 normalWS;
    float4 tangentWS;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0)
    float2 texCoord0;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
    float2 texCoord1;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2)
    float2 texCoord2;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS)
    float4 positionCSNoJitter;
    float4 previousPositionCSNoJitter;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS) && defined(EDITOR_VISUALIZATION)
    float2 metaVizUV;
    float4 metaLightCoord;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VividPackedVaryingsMesh
{
    float4 positionCS : SV_POSITION;
#if defined(VIVIDRP_VARYINGS_NEED_POSITION_WS)
    float3 positionWS : TEXCOORD0;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD)
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0) || defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
    float4 texCoord01 : TEXCOORD3;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2)
    float2 texCoord2 : TEXCOORD4;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS)
    float4 positionCSNoJitter : TEXCOORD5;
    float4 previousPositionCSNoJitter : TEXCOORD6;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS) && defined(EDITOR_VISUALIZATION)
    float2 metaVizUV : TEXCOORD5;
    float4 metaLightCoord : TEXCOORD6;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VividPackedVaryingsMesh PackVividVaryingsMesh(VividVaryingsMesh input)
{
    VividPackedVaryingsMesh output = (VividPackedVaryingsMesh)0;

    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = input.positionCS;

#if defined(VIVIDRP_VARYINGS_NEED_POSITION_WS)
    output.positionWS = input.positionWS;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD)
    output.normalWS = input.normalWS;
    output.tangentWS = input.tangentWS;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0)
    output.texCoord01.xy = input.texCoord0;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
    output.texCoord01.zw = input.texCoord1;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2)
    output.texCoord2 = input.texCoord2;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS)
    output.positionCSNoJitter = input.positionCSNoJitter;
    output.previousPositionCSNoJitter = input.previousPositionCSNoJitter;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS) && defined(EDITOR_VISUALIZATION)
    output.metaVizUV = input.metaVizUV;
    output.metaLightCoord = input.metaLightCoord;
#endif

    return output;
}

FragInputs UnpackVividVaryingsMeshToFragInputs(VividPackedVaryingsMesh input)
{
    FragInputs output;
    ZERO_INITIALIZE(FragInputs, output);

    output.positionSS = input.positionCS;
    output.tangentToWorld = float3x3(
        1.0, 0.0, 0.0,
        0.0, 1.0, 0.0,
        0.0, 0.0, 1.0);

#if defined(VIVIDRP_VARYINGS_NEED_POSITION_WS)
    output.positionRWS = input.positionWS;
    output.positionPredisplacementRWS = input.positionWS;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD)
    output.tangentToWorld = BuildTangentToWorld(input.tangentWS, input.normalWS);
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0)
    output.texCoord0.xy = input.texCoord01.xy;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
    output.texCoord1.xy = input.texCoord01.zw;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2)
    output.texCoord2.xy = input.texCoord2;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS) && defined(EDITOR_VISUALIZATION)
    output.metaVizUV = input.metaVizUV;
    output.metaLightCoord = input.metaLightCoord;
#endif

    return output;
}

#endif
