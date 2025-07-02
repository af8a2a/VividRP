#ifndef UNITY_NORMAL_BUFFER_INCLUDED
#define UNITY_NORMAL_BUFFER_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"
// ----------------------------------------------------------------------------
// Encoding/decoding normal buffer functions
// ----------------------------------------------------------------------------

struct NormalData
{
    float3 normalWS;
    float  perceptualRoughness;
};

// NormalBuffer texture declaration
TEXTURE2D_X(_NormalBufferTexture);


void DecodeFromNormalBuffer(float4 normalBuffer, out NormalData normalData)
{
    float3 packNormalWS = normalBuffer.rgb;
    normalData.normalWS = UnpackGBufferNormal(packNormalWS);
    normalData.perceptualRoughness = normalBuffer.a;
}

void DecodeFromNormalBuffer(uint2 positionSS, out NormalData normalData)
{
    float4 normalBuffer = LOAD_TEXTURE2D_X(_NormalBufferTexture, positionSS);
    DecodeFromNormalBuffer(normalBuffer, normalData);
}


#endif // UNITY_NORMAL_BUFFER_INCLUDED
