#ifndef VIVIDRP_SHADER_VARIABLES_VOLUMETRIC_INCLUDED
#define VIVIDRP_SHADER_VARIABLES_VOLUMETRIC_INCLUDED

CBUFFER_START(ShaderVariablesVolumetric)
    float4x4 _VBufferCoordToViewDirWS;
    float4 _VBufferViewportSize;
    float4 _VBufferViewportScale;
    float4 _VBufferDepthEncodingParams;
    float4 _VBufferDepthDecodingParams;
    float4 _VBufferGeometryParams;
    float4 _VBufferFogScattering;
    float4 _VBufferFogHeightParams;
    float4 _VBufferFogControlParams;
    float4 _VBufferLocalFogParams;
CBUFFER_END

#define _VBufferWidth                  _VBufferViewportSize.x
#define _VBufferHeight                 _VBufferViewportSize.y
#define _VBufferSliceCount             _VBufferViewportSize.z
#define _VBufferRcpSliceCount          _VBufferViewportSize.w
#define _VBufferCameraToVBufferScale   _VBufferViewportScale.xy
#define _VBufferRcpViewportSize        _VBufferViewportScale.zw
#define _VBufferUnitDepthTexelSpacing  _VBufferGeometryParams.x
#define _VBufferVoxelSize              _VBufferGeometryParams.y
#define _VBufferLastSliceDistance      _VBufferGeometryParams.z
#define _VBufferIsOrthographic         _VBufferGeometryParams.w
#define _VBufferFogBaseHeight          _VBufferFogHeightParams.x
#define _VBufferFogMaximumHeight       _VBufferFogHeightParams.y
#define _VBufferFogInvHeightRange      _VBufferFogHeightParams.z
#define _VBufferFogAnisotropy          _VBufferFogHeightParams.w
#define _VBufferFogEnabled             _VBufferFogControlParams.x
#define _VBufferFogDirectionalOnly     _VBufferFogControlParams.y
#define _VBufferDensityCutoff          _VBufferFogControlParams.z
#define _VBufferGlobalProbeDimmer      _VBufferFogControlParams.w
#define _VBufferLocalFogCount          _VBufferLocalFogParams.x
#define _VBufferGaussianFiltering      _VBufferLocalFogParams.y

#endif
