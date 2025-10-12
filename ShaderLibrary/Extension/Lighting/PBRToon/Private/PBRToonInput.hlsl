#ifndef PBR_TOON_INPUT
#define PBR_TOON_INPUT
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float4 _EmissionColor;
    float _NormalScale;
    float _MetallicStart;
    float _MetallicEnd;
    float _RoughnessStart;
    float _RoughnessEnd;
    float _OcclusionStart;
    float _OcclusionEnd;
    float _Cutoff;

    /// -------------------------------------
    ///  Outline
    /// -------------------------------------
    float _OutlineWidth;
    float _OutlineZBias;
    float _OutlineIntensity;
    float4 _OutlineColor;


CBUFFER_END

/// -------------------------------------
///  Outline Global Param
/// -------------------------------------

float _OutlineMaxOffsetMultiplier;



TEXTURE2D(_PBRMap);          SAMPLER(sampler_PBRMap);
TEXTURE2D(_NormalMap);       SAMPLER(sampler_NormalMap);
TEXTURE2D(_BaseMap);         SAMPLER(sampler_BaseMap);
#endif