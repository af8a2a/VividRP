#ifndef VIVIDRP_OPENPBR_TEXTURE_LUTS_INCLUDED
#define VIVIDRP_OPENPBR_TEXTURE_LUTS_INCLUDED

#include "Vendor/openpbr_data_constants.h"

Texture3D<float> _OpenPBRIdealDielectricEnergy;
Texture3D<float> _OpenPBROpaqueDielectricEnergy;
// Layers 0..5 correspond to vendor IDs 1, 2, 4, 5, 6, 7.
Texture2DArray<float4> _OpenPBRLuts2D;
SamplerState sampler_OpenPBRLinearClamp;

float4 VividOpenPBRSample2D(int lutId, float2 uv)
{
    // IDs are compile-time constants. Skip the two 3D-table IDs (0 and 3).
    int layer = lutId > OpenPBR_LutId_OpaqueDielectricEnergyComplement
        ? lutId - 2 : lutId - 1;
    return _OpenPBRLuts2D.SampleLevel(sampler_OpenPBRLinearClamp, float3(uv, layer), 0);
}

float4 VividOpenPBRSample3D(int lutId, float3 uvw)
{
    if (lutId == OpenPBR_LutId_IdealDielectricEnergyComplement)
        return _OpenPBRIdealDielectricEnergy.SampleLevel(sampler_OpenPBRLinearClamp, uvw, 0).xxxx;
    if (lutId == OpenPBR_LutId_OpaqueDielectricEnergyComplement)
        return _OpenPBROpaqueDielectricEnergy.SampleLevel(sampler_OpenPBRLinearClamp, uvw, 0).xxxx;
    return 0.0;
}

#define OPENPBR_SAMPLE_2D_TEXTURE(lutId, uv) VividOpenPBRSample2D(lutId, uv)
#define OPENPBR_SAMPLE_3D_TEXTURE(lutId, uvw) VividOpenPBRSample3D(lutId, uvw)

#endif
