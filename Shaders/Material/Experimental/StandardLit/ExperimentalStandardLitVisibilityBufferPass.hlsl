#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_VISIBILITY_BUFFER_PASS_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_VISIBILITY_BUFFER_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/VividVisibilityBuffer.hlsl"

struct VividExperimentalVisibilityAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv0 : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VividExperimentalVisibilityVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv0 : TEXCOORD0;
    float3 geometricNormalWS : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VividExperimentalVisibilityVaryings Vert(
    VividExperimentalVisibilityAttributes input)
{
    VividExperimentalVisibilityVaryings output =
        (VividExperimentalVisibilityVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv0 = input.uv0;
    output.geometricNormalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

VividVisibilityBufferFragmentOutput FragVisibilityBuffer(
    VividExperimentalVisibilityVaryings input,
    uint primitiveID : SV_PrimitiveID,
    linear float3 barycentrics : SV_Barycentrics)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    uint materialSlot = (uint)max(
        round(_VividExperimentalVBufferMaterialIndex),
        0.0);
    return PackVividVisibilityBufferFragmentOutput(
        uint2(materialSlot + 1u, primitiveID),
        input.uv0,
        input.geometricNormalWS,
        barycentrics);
}

#endif
