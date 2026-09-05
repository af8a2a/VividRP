#ifndef VIVIDRP_SHADER_VARIABLES_GLOBAL_INCLUDED
#define VIVIDRP_SHADER_VARIABLES_GLOBAL_INCLUDED

GLOBAL_CBUFFER_START(ShaderVariablesGlobal, b0)
    float4 _VividTime;
    float4 _VividSinTime;
    float4 _VividCosTime;
    float4 _VividDeltaTime;
    float4 _VividTimeParameters;
    float4 _VividLastTimeParameters;
    float4 _VividWorldSpaceCameraPos;
    float4 _VividProjectionParams;
    float4 _VividScreenParams;
    float4 _VividZBufferParams;
    float4 _VividOrthoParams;
    float4 _VividScaleBias;
    float4 _VividScaleBiasRt;
    float4 _VividRTHandleScale;
    float4x4 _VividCameraProjection;
    float4x4 _VividCameraInvProjection;
    float4x4 _VividWorldToCamera;
    float4x4 _VividCameraToWorld;
    float4x4 _VividGlstateMatrixProjection;
    float4x4 _VividMatrixV;
    float4x4 _VividMatrixInvV;
    float4x4 _VividMatrixInvP;
    float4x4 _VividMatrixVP;
    float4x4 _VividMatrixInvVP;
    float4x4 _VividPrevViewProjMatrix;
    float4x4 _VividNonJitteredViewProjMatrix;
    float4x4 _VividViewProjMatrix;
    float4x4 _VividViewMatrix;
    float4x4 _VividProjMatrix;
    float4x4 _VividInvViewProjMatrix;
    float4x4 _VividInvViewMatrix;
    float4x4 _VividInvProjMatrix;
    float4 _VividInvProjParam;
    float4 _VividScreenSize;
    float4 _VividGlobalMipBias;
    float4 _VividScaledScreenParams;
    float4x4 _VividPrevViewMatrix;
    float4x4 _VividPrevProjMatrix;
    float4 _VividJitterParams;
    float4 _VividLightModelAmbient;
    float4 _VividAmbientSky;
    float4 _VividAmbientEquator;
    float4 _VividAmbientGround;
    float4 _VividIndirectSpecColor;
    float4 _VividFogParams;
    float4 _VividFogColor;
    float4 _VividShadowColor;
    float4 _VividPlanetCenterRadius;
    float4 _VividPlanetUpAltitude;
    float4 _VividSHAr;
    float4 _VividSHAg;
    float4 _VividSHAb;
    float4 _VividSHBr;
    float4 _VividSHBg;
    float4 _VividSHBb;
    float4 _VividSHC;
CBUFFER_END

#if defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
CBUFFER_START(ShaderVariablesShadowMatrices)
    float4x4 _VividShadowVP[4];
CBUFFER_END
int _VividShadowCascadeIndex;
// Runtime branch intentionally also works with fragment-only VSM shader variants.
int _VSMUnityRasterEnabled;
float4x4 _VSMRasterViewProjection;
#endif

#define _Time _VividTime
#define _SinTime _VividSinTime
#define _CosTime _VividCosTime
#define unity_DeltaTime _VividDeltaTime
#define _TimeParameters _VividTimeParameters
#define _LastTimeParameters _VividLastTimeParameters
#define _WorldSpaceCameraPos _VividWorldSpaceCameraPos.xyz
#define _ProjectionParams _VividProjectionParams
#define _ScreenParams _VividScreenParams
#define _ZBufferParams _VividZBufferParams
#define unity_OrthoParams _VividOrthoParams
#define _ScaleBias _VividScaleBias
#define _ScaleBiasRt _VividScaleBiasRt
#define _RTHandleScale _VividRTHandleScale
#define unity_CameraProjection _VividCameraProjection
#define unity_CameraInvProjection _VividCameraInvProjection
#define unity_WorldToCamera _VividWorldToCamera
#define unity_CameraToWorld _VividCameraToWorld
#define glstate_matrix_projection _VividGlstateMatrixProjection
#define unity_MatrixV _VividMatrixV
#define unity_MatrixInvV _VividMatrixInvV
#define unity_MatrixInvP _VividMatrixInvP
#if defined(VIVIDRP_SHADERPASS_SHADOW_CASTER)
#define unity_MatrixVP (_VSMUnityRasterEnabled != 0 ? _VSMRasterViewProjection : _VividShadowVP[_VividShadowCascadeIndex])
#else
#define unity_MatrixVP _VividMatrixVP
#endif
#define unity_MatrixInvVP _VividMatrixInvVP
#define _PrevViewProjMatrix _VividPrevViewProjMatrix
#define _NonJitteredViewProjMatrix _VividNonJitteredViewProjMatrix
#define _ViewProjMatrix _VividViewProjMatrix
#define _ViewMatrix _VividViewMatrix
#define _ProjMatrix _VividProjMatrix
#define _InvViewProjMatrix _VividInvViewProjMatrix
#define _InvViewMatrix _VividInvViewMatrix
#define _InvProjMatrix _VividInvProjMatrix
#define _InvProjParam _VividInvProjParam
#define _ScreenSize _VividScreenSize
#define _GlobalMipBias _VividGlobalMipBias.xy
#define _ScaledScreenParams _VividScaledScreenParams
#define _PrevViewMatrix _VividPrevViewMatrix
#define _PrevProjMatrix _VividPrevProjMatrix
#define _JitterParams _VividJitterParams
#define glstate_lightmodel_ambient _VividLightModelAmbient
#define unity_AmbientSky _VividAmbientSky
#define unity_AmbientEquator _VividAmbientEquator
#define unity_AmbientGround _VividAmbientGround
#define unity_IndirectSpecColor _VividIndirectSpecColor
#define unity_FogParams _VividFogParams
#define unity_FogColor _VividFogColor
#define unity_ShadowColor _VividShadowColor
#define _PlanetCenterRadius _VividPlanetCenterRadius
#define _PlanetUpAltitude _VividPlanetUpAltitude
#endif
