#ifndef VIVIDRP_VERT_MESH_INCLUDED
#define VIVIDRP_VERT_MESH_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/ShaderPass/VividVaryingMesh.hlsl"

VividVaryingsMesh VividVertMesh(VividAttributesMesh input)
{
    VividVaryingsMesh output = (VividVaryingsMesh)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

#if defined(VIVIDRP_SHADERPASS_META)
    output.positionCS = UnityMetaVertexPosition(
        input.positionOS.xyz,
        input.uv1,
        input.uv2,
        unity_LightmapST,
        unity_DynamicLightmapST);
#else
    output.positionCS = TransformWorldToHClip(positionWS);
#endif

#if defined(VIVIDRP_VARYINGS_NEED_POSITION_WS)
    output.positionWS = positionWS;
#endif

#if defined(VIVIDRP_VARYINGS_NEED_TANGENT_TO_WORLD)
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
#endif

#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD0)
    output.texCoord0 = input.uv0;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD1)
    output.texCoord1 = input.uv1;
#endif
#if defined(VIVIDRP_VARYINGS_NEED_TEXCOORD2)
    output.texCoord2 = input.uv2;
#endif

#if defined(VIVIDRP_VARYINGS_NEED_MOTION_POSITIONS)
    float4 worldPosMotion = mul(UNITY_MATRIX_M, input.positionOS);
    output.positionCSNoJitter = mul(_NonJitteredViewProjMatrix, worldPosMotion);

    float4 previousPositionOS = unity_MotionVectorsParams.x == 1.0
        ? float4(input.previousPositionOS, 1.0)
        : input.positionOS;

    float4 previousWorldPosMotion = mul(UNITY_PREV_MATRIX_M, previousPositionOS);
    output.previousPositionCSNoJitter = mul(_PrevViewProjMatrix, previousWorldPosMotion);
    output.objectMotion = any(abs(worldPosMotion.xyz - previousWorldPosMotion.xyz) > 1e-5) ? 1.0 : 0.0;
#endif

#if defined(VIVIDRP_VARYINGS_NEED_META_EDITOR_VIS) && defined(EDITOR_VISUALIZATION)
    UnityEditorVizData(
        input.positionOS.xyz,
        input.uv0,
        input.uv1,
        input.uv2,
        output.metaVizUV,
        output.metaLightCoord);
#endif

    return output;
}

#endif
