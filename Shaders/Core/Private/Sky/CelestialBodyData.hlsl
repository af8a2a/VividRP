#ifndef VIVIDRP_CELESTIAL_BODY_DATA_INCLUDED
#define VIVIDRP_CELESTIAL_BODY_DATA_INCLUDED

struct CelestialBodyData
{
    float3 color;
    float radius;

    float3 forward;
    float distanceFromCamera;

    float3 right;
    float angularRadius;

    float3 up;
    int type;

    float3 surfaceColor;
    float earthshine;

    float4 surfaceTextureScaleOffset;

    float3 sunDirection;
    float flareCosInner;

    float2 phaseAngleSinCos;
    float flareCosOuter;
    float flareSize;

    float3 flareColor;
    float flareFalloff;

    uint surfaceTextureIndex;
    float2 padding;
    int shadowIndex;
};

StructuredBuffer<CelestialBodyData> _CelestialBodyDatas;

#endif
