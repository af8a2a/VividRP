#ifndef VIVIDRP_EXPERIMENTAL_STANDARD_LIT_VISIBILITY_BUFFER_PASS_INCLUDED
#define VIVIDRP_EXPERIMENTAL_STANDARD_LIT_VISIBILITY_BUFFER_PASS_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Material/StandardLit/StandardLitInput.hlsl"

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

struct VividExperimentalVisibilityOutput
{
    uint2 visibility : SV_Target0;
    float4 attributes0 : SV_Target1;
    float4 attributes1 : SV_Target2;
};

float2 VividExperimentalEncodeNormalOct(float3 normalWS)
{
    float3 normal = normalize(normalWS);
    normal /= max(abs(normal.x) + abs(normal.y) + abs(normal.z), 1e-6);
    float2 encoded = normal.xy;
    if (normal.z < 0.0)
    {
        float2 signs = float2(
            encoded.x >= 0.0 ? 1.0 : -1.0,
            encoded.y >= 0.0 ? 1.0 : -1.0);
        encoded = (1.0 - abs(encoded.yx)) * signs;
    }
    return encoded * 0.5 + 0.5;
}

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

VividExperimentalVisibilityOutput FragVisibilityBuffer(
    VividExperimentalVisibilityVaryings input,
    uint primitiveID : SV_PrimitiveID)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    VividExperimentalVisibilityOutput output;
    uint materialSlot = (uint)max(
        round(_VividExperimentalVBufferMaterialIndex),
        0.0);
    float2 uvDx = ddx(input.uv0);
    float2 uvDy = ddy(input.uv0);
    output.visibility = uint2(materialSlot + 1u, primitiveID);
    output.attributes0 = float4(input.uv0, uvDx);
    output.attributes1 = float4(
        uvDy,
        VividExperimentalEncodeNormalOct(input.geometricNormalWS));
    return output;
}

#endif
