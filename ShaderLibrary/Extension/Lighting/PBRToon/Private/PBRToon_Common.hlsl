#ifndef PBRTOON_COMMON_INCLUDED
#define PBRTOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/LightingCommon.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Runtime/Extension/Filter/PreIntegratedFGD/Shader/PreIntegratedFGD.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/LightGrid/ClusterLight.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Extension/Lighting/Common/AreaLightCommon.hlsl"

#include "PBRToonInput.hlsl"



half3 SampleNormal(float2 uv, TEXTURE2D_PARAM(bumpMap, sampler_bumpMap), half scale = half(1.0))
{
    #ifdef _NORMALMAP
    half4 n = SAMPLE_TEXTURE2D(bumpMap, sampler_bumpMap, uv);
    #if BUMP_SCALE_NOT_SUPPORTED
    return UnpackNormal(n);
    #else
    return UnpackNormalScale(n, scale);
    #endif
    #else
    return half3(0.0h, 0.0h, 1.0h);
    #endif
}


float3 OctahedronToUnitVector(float2 uv)
{
    float3 n = float3(uv, 1.0 - dot(float2(1.0, 1.0), abs(uv)));
    if (n.z < 0.0)
    {
        n.xy = (1.0 - abs(n.yx)) * (step(0.0, n.xy) * 2.0 - 1.0);
    }
    return normalize(n);
}

#endif
