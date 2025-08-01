//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef LOCALVOLUMETRICFOG_CS_HLSL
#define LOCALVOLUMETRICFOG_CS_HLSL
// Generated from UnityEngine.Rendering.Universal.P10.LocalVolumetricFogBuffer
// PackingRules = Exact
CBUFFER_START (LocalVolumetricFogBuffer)
float4 _VolumetricFogObbRight;
float4 _VolumetricFogObbUp;
float4 _VolumetricFogObbForward;
float4 _VolumetricFogObbCenter;
float4 _VolumetricFogObbExtents;
float4 _VolumetricFogRcpPosFaceFade;
float4 _VolumetricFogRcpNegFaceFade;
float4 _VolumetricFogProperty;
uint _VolumetricFogGlobalIndex;
CBUFFER_END


#endif
