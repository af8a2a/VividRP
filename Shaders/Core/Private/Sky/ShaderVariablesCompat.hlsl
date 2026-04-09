#ifndef VIVIDRP_SKY_SHADER_VARIABLES_COMPAT_INCLUDED
#define VIVIDRP_SKY_SHADER_VARIABLES_COMPAT_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"

float3 GetCameraPositionWS()
{
    return _WorldSpaceCameraPos;
}

float GetCurrentExposureMultiplier()
{
    return VividGetPreExposure();
}

#endif
