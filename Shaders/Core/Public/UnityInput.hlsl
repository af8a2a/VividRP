// UNITY_SHADER_NO_UPGRADE

#ifndef UNIVERSAL_SHADER_VARIABLES_INCLUDED
#define UNIVERSAL_SHADER_VARIABLES_INCLUDED

#if defined(STEREO_INSTANCING_ON) && (defined(SHADER_API_D3D11) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_PSSL) || defined(SHADER_API_VULKAN) || (defined(SHADER_API_METAL) && !defined(UNITY_COMPILER_DXC)))
#define UNITY_STEREO_INSTANCING_ENABLED
#endif

#if defined(STEREO_MULTIVIEW_ON) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE) || defined(SHADER_API_VULKAN)) && !(defined(SHADER_API_SWITCH)) && !(defined(SHADER_API_SWITCH2))
    #define UNITY_STEREO_MULTIVIEW_ENABLED
#endif

#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
#define USING_STEREO_MATRICES
#endif

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/ShaderVariablesGlobal.hlsl"

#define UNITY_LIGHTMODEL_AMBIENT (glstate_lightmodel_ambient * 2)

float4 unity_CameraWorldClipPlanes[6];
float4 _FrustumPlanes[6];

#ifndef DOTS_INSTANCING_ON
CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade;
real4 unity_WorldTransformParams;
float4 unity_RenderingLayer;
half4 unity_LightData;
half4 unity_LightIndices[2];
float4 unity_ProbesOcclusion;
real4 unity_SpecCube0_HDR;
real4 unity_SpecCube1_HDR;
float4 unity_SpecCube0_BoxMax;
float4 unity_SpecCube0_BoxMin;
float4 unity_SpecCube0_ProbePosition;
float4 unity_SpecCube0_Rotation;
float4 unity_SpecCube1_BoxMax;
float4 unity_SpecCube1_BoxMin;
float4 unity_SpecCube1_ProbePosition;
float4 unity_SpecCube1_Rotation;
float4 unity_LightmapST;
float4 unity_DynamicLightmapST;
real4 unity_SHAr;
real4 unity_SHAg;
real4 unity_SHAb;
real4 unity_SHBr;
real4 unity_SHBg;
real4 unity_SHBb;
real4 unity_SHC;
float4 unity_RendererBounds_Min;
float4 unity_RendererBounds_Max;
float4x4 unity_MatrixPreviousM;
float4x4 unity_MatrixPreviousMI;
float4 unity_MotionVectorsParams;
float4 unity_SpriteColor;
float4 unity_SpriteProps;
CBUFFER_END

static const uint unity_RendererUserValue = asuint(unity_RenderingLayer.y);
#endif

#define unity_SHAr _VividSHAr
#define unity_SHAg _VividSHAg
#define unity_SHAb _VividSHAb
#define unity_SHBr _VividSHBr
#define unity_SHBg _VividSHBg
#define unity_SHBb _VividSHBb
#define unity_SHC _VividSHC

uint unity_RendererUserValuesPropertyEntry;

#if defined(UNITY_STEREO_MULTIVIEW_ENABLED) && defined(SHADER_STAGE_VERTEX)
#if !defined(UNITY_DECLARE_MULTIVIEW)
#define UNITY_DECLARE_MULTIVIEW(number_of_views) CBUFFER_START(OVR_multiview) uint gl_ViewID; uint numViews_##number_of_views; CBUFFER_END
#define UNITY_VIEWID gl_ViewID
#endif
#endif

#if defined(UNITY_STEREO_MULTIVIEW_ENABLED) && defined(SHADER_STAGE_VERTEX)
#define unity_StereoEyeIndex UNITY_VIEWID
UNITY_DECLARE_MULTIVIEW(2);
#elif defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
static uint unity_StereoEyeIndex;
#else
static const uint unity_StereoEyeIndex = 0u;
#endif

TEXTURECUBE(unity_SpecCube0);
SAMPLER(samplerunity_SpecCube0);
TEXTURECUBE(unity_SpecCube1);
SAMPLER(samplerunity_SpecCube1);

TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_Lightmap);
TEXTURE2D_ARRAY(unity_Lightmaps);
SAMPLER(samplerunity_Lightmaps);

TEXTURE2D(unity_DynamicLightmap);
SAMPLER(samplerunity_DynamicLightmap);

TEXTURE2D(unity_LightmapInd);
TEXTURE2D_ARRAY(unity_LightmapsInd);
TEXTURE2D(unity_DynamicDirectionality);

TEXTURE2D(unity_ShadowMask);
SAMPLER(samplerunity_ShadowMask);
TEXTURE2D_ARRAY(unity_ShadowMasks);
SAMPLER(samplerunity_ShadowMasks);

TEXTURE2D(unity_MipmapStreaming_DebugTex);

float4x4 OptimizeProjectionMatrix(float4x4 M)
{
    M._21_41 = 0;
    M._12_42 = 0;
    return M;
}

#endif
