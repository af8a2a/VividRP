#ifndef VIVIDRP_LUMEN_REFLECTIONS_COMBINE_INCLUDED
#define VIVIDRP_LUMEN_REFLECTIONS_COMBINE_INCLUDED

// Verbatim port of UE5 LumenReflectionsCombine.ush & LumenReflectionCommon.ush::ApplySmoothBias.

float _LumenReflectionMaxRoughnessToTrace;
float _LumenReflectionMaxRoughnessToTraceForFoliage;
float _LumenReflectionInvRoughnessFadeLength;
float _LumenReflectionSmoothBias;

// Weight of dedicated reflection rays for a pixel:
// 0 - specular should fall back to rough specular / environment approximation
// 1 - specular should only composite dedicated reflection rays
float LumenCombineReflectionsAlpha(float InRoughness, bool bHasBackfaceDiffuse)
{
    float FadeAlpha = saturate(((bHasBackfaceDiffuse ? _LumenReflectionMaxRoughnessToTraceForFoliage : _LumenReflectionMaxRoughnessToTrace) - InRoughness) * _LumenReflectionInvRoughnessFadeLength);
    return FadeAlpha;
}

bool NeedsLumenReflections(float InRoughness, float InTopRoughness, bool bHasBackfaceDiffuse, bool bIsClearCoat)
{
    return LumenCombineReflectionsAlpha(InRoughness, bHasBackfaceDiffuse) > 0.0f
        || (bIsClearCoat && LumenCombineReflectionsAlpha(InTopRoughness, false) > 0.0f);
}

float ApplyLumenReflectionSmoothBias(float Roughness)
{
    float NewRoughness = Roughness;

    if (_LumenReflectionSmoothBias > 0.0f)
    {
        // SmoothStep-like function up to SmoothBias, original value above
        float X = saturate(Roughness / _LumenReflectionSmoothBias);
        NewRoughness = Roughness * X * X * (3.0f - 2.0f * X);
    }

    return NewRoughness;
}

#endif
