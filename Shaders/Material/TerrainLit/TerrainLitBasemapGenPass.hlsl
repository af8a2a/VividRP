#ifndef VIVIDRP_TERRAIN_LIT_BASEMAP_GEN_PASS_INCLUDED
#define VIVIDRP_TERRAIN_LIT_BASEMAP_GEN_PASS_INCLUDED

#define VIVID_TERRAIN_LIGHTWEIGHT_INCLUDE 1
#include "Packages/com.vivid.render-pipelines/Shaders/Material/TerrainLit/TerrainLitSampling.hlsl"

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 splatUV : TEXCOORD0;
    float2 controlUV : TEXCOORD1;
};

float2 ComputeTerrainControlUV(float2 uv)
{
    return (uv * (_Control0_TexelSize.zw - 1.0) + 0.5) * _Control0_TexelSize.xy;
}

Varyings Vert(uint vertexID : SV_VertexID)
{
    Varyings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    output.splatUV = GetFullScreenTriangleTexCoord(vertexID) * _Control0_ST.xy + _Control0_ST.zw;
    output.controlUV = ComputeTerrainControlUV(output.splatUV);
    return output;
}

float4 FragMainTex(Varyings input) : SV_Target
{
    TerrainLitSurfaceData surfaceData;
    InitializeTerrainLitSurfaceData(surfaceData);
    TerrainSplatBlend(input.controlUV, input.splatUV, surfaceData);
    return float4(surfaceData.albedo, surfaceData.smoothness);
}

float2 FragMetallicTex(Varyings input) : SV_Target
{
    TerrainLitSurfaceData surfaceData;
    InitializeTerrainLitSurfaceData(surfaceData);
    TerrainSplatBlend(input.controlUV, input.splatUV, surfaceData);
    return float2(surfaceData.metallic, surfaceData.ao);
}

#endif
