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

struct VividDecalSampleContext
{
    float3 positionDS;
    float2 uv;
    float2 uvDdx;
    float2 uvDdy;
};

struct VividDecalBaseColorSample
{
    float3 color;
    float opacity;
};

float3 UnpackVividDecalNormal(float4 packedNormal)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

bool TryCreateVividDecalSampleContext(
    VividDecalClusterData decal,
    float3 positionWS,
    float3 positionWSDdx,
    float3 positionWSDdy,
    out VividDecalSampleContext sampleContext)
{
    float3 positionDS = mul(decal.worldToDecal, float4(positionWS, 1.0)).xyz;
    float3 edgeDistance = 0.5 - abs(positionDS);

    UNITY_BRANCH
    if (any(edgeDistance < 0.0))
    {
        sampleContext = (VividDecalSampleContext)0;
        return false;
    }

    float3 positionDSDdx = mul(decal.worldToDecal, float4(positionWSDdx, 0.0)).xyz;
    float3 positionDSDdy = mul(decal.worldToDecal, float4(positionWSDdy, 0.0)).xyz;

    sampleContext.positionDS = positionDS;
    sampleContext.uv = positionDS.xz + 0.5;
    sampleContext.uvDdx = positionDSDdx.xz;
    sampleContext.uvDdy = positionDSDdy.xz;
    return true;
}

float ComputeVividDecalVolumeFade(VividDecalClusterData decal, float3 positionDS)
{
    float2 edgeDistance = 0.5 - abs(positionDS.xz);
    float edgeFade = min(edgeDistance.x, edgeDistance.y);
    float blendDistance = clamp(decal.blendDistance, 0.0, 0.5);
    return blendDistance > 1e-5 ? saturate(edgeFade / blendDistance) : step(0.0, edgeFade);
}

VividDecalBaseColorSample SampleVividDecalBaseColor(
    VividDecalClusterData decal,
    float2 uv,
    float2 uvDdx,
    float2 uvDdy)
{
    VividDecalBaseColorSample result;
    float4 textureSample = 1.0.xxxx;

    UNITY_BRANCH
    if (decal.baseColorTextureIndex != VIVID_DECAL_INVALID_TEXTURE_INDEX)
    {
        Texture2D baseColorTexture = GetBindlessTexture2D(NonUniformResourceIndex(decal.baseColorTextureIndex));
        textureSample = SAMPLE_TEXTURE2D_GRAD(baseColorTexture, sampler_LinearClamp, uv, uvDdx, uvDdy);
    }

    result.color = decal.baseColor.rgb * textureSample.rgb;
    result.opacity = saturate(decal.baseColor.a * textureSample.a);
    return result;
}

float3x3 CreateVividDecalTangentToWorld(VividDecalClusterData decal)
{
    float4x4 decalToWorld = Inverse(decal.worldToDecal);
    float3 tangentWS = normalize(mul((float3x3)decalToWorld, float3(1.0, 0.0, 0.0)));
    float3 bitangentWS = normalize(mul((float3x3)decalToWorld, float3(0.0, 0.0, 1.0)));
    float3 normalWS = normalize(mul((float3x3)decalToWorld, float3(0.0, 1.0, 0.0)));
    return float3x3(tangentWS, bitangentWS, normalWS);
}

float3 SampleVividDecalNormalWS(VividDecalClusterData decal, float2 uv, float2 uvDdx, float2 uvDdy)
{
    Texture2D normalTexture = GetBindlessTexture2D(NonUniformResourceIndex(decal.normalTextureIndex));
    float3 normalTS = UnpackVividDecalNormal(SAMPLE_TEXTURE2D_GRAD(normalTexture, sampler_LinearClamp, uv, uvDdx, uvDdy));
    return normalize(mul(normalTS, CreateVividDecalTangentToWorld(decal)));
}

void ApplyVividGPUDrivenDecalsToGBufferSurfaceData(
    inout VividGBufferSurfaceData surfaceData,
    float3 positionWS,
    uint2 pixelCoord)
{
    VividClusteredLightCell decalCell = VividClusteredLighting::LoadDecalCell(pixelCoord, positionWS);
    float3 positionWSDdx = ddx(positionWS);
    float3 positionWSDdy = ddy(positionWS);

    UNITY_LOOP
    for (uint localIndex = 0u; localIndex < decalCell.count; localIndex++)
    {
        uint decalIndex = VividClusteredLighting::LoadLightIndex(decalCell, localIndex);
        VividDecalClusterData decal = _DecalData[decalIndex];
        
        VividDecalSampleContext sampleContext;
        if (!TryCreateVividDecalSampleContext(decal, positionWS, positionWSDdx, positionWSDdy, sampleContext))
            continue;
        
        float volumeFade = ComputeVividDecalVolumeFade(decal, sampleContext.positionDS);
        VividDecalBaseColorSample baseColor = SampleVividDecalBaseColor(
            decal,
            sampleContext.uv,
            sampleContext.uvDdx,
            sampleContext.uvDdy);
        float decalOpacity = volumeFade * baseColor.opacity;
        
        surfaceData.baseColor = lerp(surfaceData.baseColor, baseColor.color, decalOpacity);
        
        UNITY_BRANCH
        if (decalOpacity > 0.0 && decal.normalTextureIndex != VIVID_DECAL_INVALID_TEXTURE_INDEX)
            surfaceData.normalWS = normalize(lerp(
                surfaceData.normalWS,
                SampleVividDecalNormalWS(decal, sampleContext.uv, sampleContext.uvDdx, sampleContext.uvDdy),
                decalOpacity));
    }
}

#endif
