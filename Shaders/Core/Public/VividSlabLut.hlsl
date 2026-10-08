#ifndef VIVIDRP_SLAB_LUT_INCLUDED
#define VIVIDRP_SLAB_LUT_INCLUDED

#include "VividSimpleSlabEnergy.hlsl"

Texture2D<float4> _VividSlabLut;
SamplerState sampler_VividSlabLut_linear_clamp;

float4 VividSampleSlabLut(float nDotV, float perceptualRoughness)
{
    float2 uv = float2(sqrt(saturate(nDotV)), saturate(perceptualRoughness));
    uv = (uv * (VIVID_SLAB_LUT_RESOLUTION - 1.0f) + 0.5f)
        / VIVID_SLAB_LUT_RESOLUTION;
    return _VividSlabLut.SampleLevel(sampler_VividSlabLut_linear_clamp, uv, 0);
}

VividSimpleSlabEnergy VividLoadSimpleSlabEnergy(VividSimpleSlabData slab, float3 viewDirectionWS)
{
    return VividPrepareSimpleSlabEnergy(slab.specularF0,
        VividSampleSlabLut(dot(slab.normalWS, viewDirectionWS), slab.perceptualRoughness));
}

#endif
