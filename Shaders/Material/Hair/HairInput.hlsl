#ifndef VIVIDRP_HAIR_INPUT_INCLUDED
#define VIVIDRP_HAIR_INPUT_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _HairBaseColor;
    float4 _HairEmissionColor;
    float _HairAbsorptionModel;
    float _HairMelanin;
    float _HairMelaninRedness;
    float _HairLongitudinalRoughness;
    float _HairAzimuthalRoughness;
    float _HairIor;
    float _HairCuticleAngleDegrees;
    float _HairFresnelApproximation;
CBUFFER_END

float3 VividHairGetBaseColor()
{
    return saturate(_HairBaseColor.rgb);
}

float3 VividHairGetEmission()
{
    return max(_HairEmissionColor.rgb, 0.0);
}

float VividHairGetLongitudinalRoughness()
{
    return clamp(_HairLongitudinalRoughness, 0.001, 1.0);
}

float VividHairGetAzimuthalRoughness()
{
    return clamp(_HairAzimuthalRoughness, 0.001, 1.0);
}

float VividHairGetIor()
{
    return clamp(_HairIor, 1.0001, 3.0);
}

float3 VividHairGetSpecularF0()
{
    float ior = VividHairGetIor();
    float ratio = (ior - 1.0) / (ior + 1.0);
    return (ratio * ratio).xxx;
}

#endif
