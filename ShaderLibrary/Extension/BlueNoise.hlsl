#ifndef UNITY_BLUENOISE_INCLUDED
#define UNITY_BLUENOISE_INCLUDED

Texture2DArray<float>  _STBNVec1Texture;
Texture2DArray<float2> _STBNVec2Texture;

int _STBNIndex;

float GetSpatiotemporalBlueNoiseVec1(uint2 pixelCoord)
{
    return _STBNVec1Texture[uint3(pixelCoord.x % 128, pixelCoord.y % 128, _STBNIndex)].x;
}

float2 GetSpatiotemporalBlueNoiseVec2(uint2 pixelCoord)
{
    return _STBNVec2Texture[uint3(pixelCoord.x % 128, pixelCoord.y % 128, _STBNIndex)].xy;
}


#endif //UNITY_BLUENOISE_INCLUDED