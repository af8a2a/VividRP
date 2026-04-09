#ifndef VIVIDRP_SKY_LIGHTDEFINITION_CS_HLSL
#define VIVIDRP_SKY_LIGHTDEFINITION_CS_HLSL

#define ENVCONSTANTS_CONVOLUTION_MIP_COUNT (7)

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
    float3 padding;
    int shadowIndex;
};

#endif
