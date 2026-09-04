#ifndef VIVIDRP_SKY_LIGHTING_INCLUDED
#define VIVIDRP_SKY_LIGHTING_INCLUDED

#include "Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

TEXTURECUBE(_SkyTexture);
SAMPLER(sampler_SkyTexture);
float4 _SkyTextureTint;
float4 _SkyTextureParams;

bool HasSkyTexture()
{
    return _SkyTextureParams.w > 0.5;
}

float3 VividRotateAroundYAxis(float3 directionWS, float rotationDegrees)
{
    float rotationRadians = radians(rotationDegrees);
    float s = 0.0;
    float c = 1.0;
    sincos(rotationRadians, s, c);

    return float3(
        c * directionWS.x - s * directionWS.z,
        directionWS.y,
        s * directionWS.x + c * directionWS.z);
}

float3 VividGetReflectionVector(float3 viewDirectionWS, float3 normalWS)
{
    return reflect(-viewDirectionWS, normalWS);
}

float3 SampleSkyTexture(float3 directionWS, float mipLevel)
{
    if (!HasSkyTexture())
        return float3(0.0, 0.0, 0.0);

    float skyMipLevel = min(mipLevel, max(_SkyTextureParams.z, 0.0));
    float3 rotatedDirectionWS = VividRotateAroundYAxis(directionWS, _SkyTextureParams.y);
    float3 envLighting = float3(0.0, 0.0, 0.0);
    envLighting = SAMPLE_TEXTURECUBE_LOD(_SkyTexture, sampler_SkyTexture, rotatedDirectionWS, skyMipLevel).rgb;
    return envLighting * _SkyTextureTint.rgb * _SkyTextureParams.x;
}

bool VividHasSkyIBL()
{
    return HasSkyTexture();
}

float3 VividSampleSkyIBL(float3 directionWS, float perceptualRoughness)
{
    uint maxMip = (uint)max(_SkyTextureParams.z, 0.0);
    float mipLevel = PerceptualRoughnessToMipmapLevel(saturate(perceptualRoughness), maxMip);
    return SampleSkyTexture(directionWS, mipLevel);
}

#endif
