//
// This file was automatically generated. Please don't edit by hand. Execute Editor command [ Edit > Rendering > Generate Shader Includes ] instead
//

#ifndef SHADOWDEF_CS_HLSL
#define SHADOWDEF_CS_HLSL
// Generated from UnityEngine.Rendering.Universal.VividShadowData
// PackingRules = Exact
struct VividShadowData
{
    float3 rot0;
    float3 rot1;
    float3 rot2;
    float3 pos;
    float4 proj;
    float2 atlasOffset;
    float worldTexelSize;
    float normalBias;
    real4 zBufferParam;
    float4 shadowMapSize;
    float4 shadowFilterParams0;
    float4 dirLightPCSSParams0;
    float4 dirLightPCSSParams1;
    float3 cacheTranslationDelta;
    float isInCachedAtlas;
    float4x4 shadowToWorld;
};


#endif
