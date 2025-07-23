
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"


CBUFFER_START(UnityPerMaterial)
    float4  _BaseMap_ST;
    float4  _BaseColor;
    float4  _EmissionColor;
    float   _NormalScale;
    float   _MetallicStart;
    float   _MetallicEnd;
    float   _RoughnessStart;
    float   _RoughnessEnd;
    float   _OcclusionStart;
    float   _OcclusionEnd;
    float   _Cutoff;

CBUFFER_END

TEXTURE2D(_PBRMap);
TEXTURE2D(_NormalMap);
TEXTURE2D(_BaseMap);




