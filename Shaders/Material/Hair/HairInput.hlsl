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

struct VividHairMaterialData
{
    float3 baseColor;
    float3 emission;
    uint absorptionModel;
    float melanin;
    float melaninRedness;
    float longitudinalRoughness;
    float azimuthalRoughness;
    float ior;
    float cuticleAngleInDegrees;
    uint useFresnelApproximation;
};

bool VividHairInputIsFinite(float value)
{
    return value == value && abs(value) < 3.402823466e+38;
}

float VividHairSanitizeMaterialScalar(
    float value,
    float minimum,
    float maximum,
    float fallback)
{
    return VividHairInputIsFinite(value)
        ? clamp(value, minimum, maximum)
        : fallback;
}

float3 VividHairSanitizeMaterialColor(
    float3 value,
    float3 fallback,
    bool allowHdr)
{
    value.x = VividHairInputIsFinite(value.x) ? value.x : fallback.x;
    value.y = VividHairInputIsFinite(value.y) ? value.y : fallback.y;
    value.z = VividHairInputIsFinite(value.z) ? value.z : fallback.z;
    return allowHdr ? max(value, 0.0) : saturate(value);
}

VividHairMaterialData VividHairLoadMaterialData()
{
    VividHairMaterialData material;
    material.baseColor = VividHairSanitizeMaterialColor(
        _HairBaseColor.rgb,
        float3(0.227, 0.130, 0.035),
        false);
    material.emission = VividHairSanitizeMaterialColor(
        _HairEmissionColor.rgb,
        0.0,
        true);
    material.absorptionModel = (uint)round(
        VividHairSanitizeMaterialScalar(
            _HairAbsorptionModel,
            0.0,
            2.0,
            1.0));
    material.melanin = VividHairSanitizeMaterialScalar(
        _HairMelanin,
        0.0,
        1.0,
        0.805);
    material.melaninRedness = VividHairSanitizeMaterialScalar(
        _HairMelaninRedness,
        0.0,
        1.0,
        0.05);
    material.longitudinalRoughness =
        VividHairSanitizeMaterialScalar(
            _HairLongitudinalRoughness,
            0.001,
            1.0,
            0.4);
    material.azimuthalRoughness =
        VividHairSanitizeMaterialScalar(
            _HairAzimuthalRoughness,
            0.001,
            1.0,
            0.6);
    material.ior = VividHairSanitizeMaterialScalar(
        _HairIor,
        1.0001,
        3.0,
        1.55);
    material.cuticleAngleInDegrees =
        VividHairSanitizeMaterialScalar(
            _HairCuticleAngleDegrees,
            0.0,
            10.0,
            3.0);
    material.useFresnelApproximation =
        VividHairInputIsFinite(_HairFresnelApproximation)
            && _HairFresnelApproximation > 0.5
                ? 1u
                : 0u;
    return material;
}

float3 VividHairGetSpecularF0(VividHairMaterialData material)
{
    float ratio = (material.ior - 1.0) / (material.ior + 1.0);
    return (ratio * ratio).xxx;
}

#endif
