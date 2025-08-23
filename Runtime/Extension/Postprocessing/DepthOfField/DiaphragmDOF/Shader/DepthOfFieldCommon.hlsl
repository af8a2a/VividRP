#ifndef DEPTH_OF_FIELD_COMMON
#define DEPTH_OF_FIELD_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
// IMPORTANT: This is expecting the corner not the center.
float2 FromOutputPosSSToPreupsampleUV(int2 posSS)
{
    return (posSS + 0.5f) * _ScreenSize.zw;
}

// IMPORTANT: This is expecting the corner not the center.
float2 FromOutputPosSSToPreupsamplePosSS(float2 posSS)
{
    float2 uv = FromOutputPosSSToPreupsampleUV(posSS);
    return floor(uv * _ScreenSize.xy);
}


struct TileData
{
    uint position;
};

struct CoCTileData
{
    float minFarCoC;
    float maxFarCoC;
    float minNearCoC;
    float maxNearCoC;
};

CoCTileData LoadCoCTileData(TEXTURE2D_X(tileTexture), uint2 coords)
{
    float4 data = tileTexture[coords];
    CoCTileData tileData = {data.x, data.y, data.z, data.w};
    return tileData;
}

float4 PackCoCTileData(CoCTileData data)
{
    return float4(data.minFarCoC, data.maxFarCoC, data.minNearCoC, data.maxNearCoC);
}

uint PackKernelCoord(float2 coords)
{
    return uint(f32tof16(coords.x) | f32tof16(coords.y) << 16);
}

float2 UnpackKernelCoord(StructuredBuffer<uint> kernel, uint id)
{
    uint coord = kernel[id];
    return float2(f16tof32(coord), f16tof32(coord >> 16));
}

uint PackTileCoord(uint2 coord)
{
    return (coord.x << 16u) | coord.y;
}

uint2 UnpackTileCoord(TileData tile)
{
    uint pos = tile.position;
    return uint2((pos >> 16u) & 0xffff, pos & 0xffff);
}

float CameraDepth(uint2 pixelCoords)
{
    pixelCoords = FromOutputPosSSToPreupsamplePosSS(pixelCoords);

    return LoadSceneDepth(pixelCoords);
}


float2 ClampAndScaleUVForBilinearPostProcessTexture(float2 UV)
{
    return ClampAndScaleUV(UV, _ScreenSize.zw, 0.5f, _RTHandleScale.xy);
}

// This is assuming an upsampled texture used in post processing, with original screen size and a half a texel offset for the clamping.
float2 ClampAndScaleUVForBilinearPostProcessTexture(float2 UV, float2 texelSize)
{
    return ClampAndScaleUV(UV, texelSize, 0.5f, _RTHandleScale.xy);
}



#endif // DEPTH_OF_FIELD_COMMON
