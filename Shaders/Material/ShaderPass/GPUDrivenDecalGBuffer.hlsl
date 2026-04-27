#ifndef VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER_INCLUDED
#define VIVIDRP_GPU_DRIVEN_DECAL_GBUFFER_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/ClusteredLighting.hlsl"

static const uint VIVID_DECAL_INVALID_TEXTURE_INDEX = 0xffffffffu;

struct VividDecalClusterData
{
    float4x4 worldToDecal;
    float4 baseColor;
    uint baseColorTextureIndex;
    uint normalTextureIndex;
    float blendDistance;
    float padding;
};

StructuredBuffer<VividDecalClusterData> _DecalData;

float3 UnpackVividDecalNormal(float4 packedNormal)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

bool TryGetVividDecalUV(VividDecalClusterData decal, float3 positionWS, out float3 positionDS, out float2 uv)
{
    positionDS = mul(decal.worldToDecal, float4(positionWS, 1.0)).xyz;
    float3 edgeDistance = 0.5 - abs(positionDS);

    UNITY_BRANCH
    if (any(edgeDistance < 0.0))
    {
        uv = 0.0.xx;
        return false;
    }

    uv = positionDS.xy + 0.5;
    return true;
}

float ComputeVividDecalVolumeFade(VividDecalClusterData decal, float3 positionDS)
{
    float3 edgeDistance = 0.5 - abs(positionDS);
    float edgeFade = min(edgeDistance.x, min(edgeDistance.y, edgeDistance.z));
    float blendDistance = max(decal.blendDistance, 0.0);
    return blendDistance > 1e-5 ? saturate(edgeFade / blendDistance) : step(0.0, edgeFade);
}

float4 SampleVividDecalBaseColor(VividDecalClusterData decal, float2 uv)
{
    float4 baseColor = decal.baseColor;

    UNITY_BRANCH
    if (decal.baseColorTextureIndex != VIVID_DECAL_INVALID_TEXTURE_INDEX)
    {
        Texture2D baseColorTexture = GetBindlessTexture2D(NonUniformResourceIndex(decal.baseColorTextureIndex));
        baseColor *= SAMPLE_TEXTURE2D(baseColorTexture, sampler_LinearClamp, uv);
    }

    return baseColor;
}

float3x3 CreateVividDecalTangentToWorld(VividDecalClusterData decal)
{
    float4x4 decalToWorld = Inverse(decal.worldToDecal);
    float3 tangentWS = normalize(mul((float3x3)decalToWorld, float3(1.0, 0.0, 0.0)));
    float3 bitangentWS = normalize(mul((float3x3)decalToWorld, float3(0.0, 1.0, 0.0)));
    float3 normalWS = normalize(mul((float3x3)decalToWorld, float3(0.0, 0.0, 1.0)));
    return float3x3(tangentWS, bitangentWS, normalWS);
}

float3 SampleVividDecalNormalWS(VividDecalClusterData decal, float2 uv)
{
    Texture2D normalTexture = GetBindlessTexture2D(NonUniformResourceIndex(decal.normalTextureIndex));
    float3 normalTS = UnpackVividDecalNormal(SAMPLE_TEXTURE2D(normalTexture, sampler_LinearClamp, uv));
    return normalize(mul(normalTS, CreateVividDecalTangentToWorld(decal)));
}

void ApplyVividGPUDrivenDecalsToGBufferSurfaceData(
    inout VividGBufferSurfaceData surfaceData,
    float3 positionWS,
    uint2 pixelCoord)
{
    VividClusteredLightCell decalCell = VividClusteredLighting::LoadDecalCell(pixelCoord, positionWS);

    UNITY_LOOP
    for (uint localIndex = 0u; localIndex < decalCell.count; localIndex++)
    {
        uint decalIndex = VividClusteredLighting::LoadLightIndex(decalCell, localIndex);
        VividDecalClusterData decal = _DecalData[decalIndex];
        
        float3 positionDS;
        float2 uv;
        if (!TryGetVividDecalUV(decal, positionWS, positionDS, uv))
            continue;
        
        float volumeFade = ComputeVividDecalVolumeFade(decal, positionDS);
        float4 baseColor = SampleVividDecalBaseColor(decal, uv);
        float blend = volumeFade * saturate(baseColor.a);
        
        surfaceData.baseColor = lerp(surfaceData.baseColor, baseColor.rgb, blend);
        
        UNITY_BRANCH
        if (blend > 0.0 && decal.normalTextureIndex != VIVID_DECAL_INVALID_TEXTURE_INDEX)
            surfaceData.normalWS = normalize(lerp(surfaceData.normalWS, SampleVividDecalNormalWS(decal, uv), blend));
    }
}

#endif
