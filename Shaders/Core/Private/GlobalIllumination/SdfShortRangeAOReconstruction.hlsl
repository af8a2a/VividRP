#ifndef VIVID_SDF_SHORT_RANGE_AO_RECONSTRUCTION_INCLUDED
#define VIVID_SDF_SHORT_RANGE_AO_RECONSTRUCTION_INCLUDED

#include "../../Public/VividBentNormalPacking.hlsl"

// UE's stochastic lighting coordinates start at the top-left of the view.
uint _SdfBentFlipY;

uint2 BentScreenPixel(uint2 Pixel)
{
    if (_SdfBentFlipY != 0u)
        Pixel.y = uint(_SdfDiffuseScreenSize.y) - 1u - Pixel.y;
    return Pixel;
}

// StochasticLightingUpsample.ush, 2x2 permutation.
int2 GetDownsampleJitter2x2(int2 DownsampledScreenCoord)
{
    uint2 CellIndex = uint2(DownsampledScreenCoord) % 2u;
    uint LinearIndex = CellIndex.x + CellIndex.y * 2u;
    LinearIndex = (LinearIndex + _SdfDiffuseFrame) % 4u;
    int2 Jitter;
    Jitter.x = LinearIndex & 0x02 ? 1 : 0;
    Jitter.y = LinearIndex & 0x01 ? 0 : 1;
    return Jitter;
}

uint2 BentDownsampledReceiver(int2 Cell)
{
    int2 Pixel = Cell * 2 + GetDownsampleJitter2x2(Cell);
    return BentScreenPixel(uint2(clamp(Pixel, 0, int2(_SdfDiffuseScreenSize.xy) - 1)));
}

#endif
