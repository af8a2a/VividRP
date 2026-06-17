#ifndef VIVIDRP_BUILTIN_DATA_INCLUDED
#define VIVIDRP_BUILTIN_DATA_INCLUDED

struct VividBuiltinData
{
    float3 bakeDiffuseLighting;
    float3 backBakeDiffuseLighting;
    float4 shadowMask;
    float hasBakedGI;
    float isLightmap;
};

VividBuiltinData InitVividBuiltinData()
{
    VividBuiltinData builtinData;
    builtinData.bakeDiffuseLighting = 0.0;
    builtinData.backBakeDiffuseLighting = 0.0;
    builtinData.shadowMask = 1.0;
    builtinData.hasBakedGI = 0.0;
    builtinData.isLightmap = 0.0;
    return builtinData;
}

VividBuiltinData CreateVividBuiltinData(
    float3 bakeDiffuseLighting,
    float hasBakedGI,
    float isLightmap,
    float4 shadowMask)
{
    VividBuiltinData builtinData = InitVividBuiltinData();
    builtinData.bakeDiffuseLighting = bakeDiffuseLighting;
    builtinData.hasBakedGI = hasBakedGI;
    builtinData.isLightmap = isLightmap;
    builtinData.shadowMask = shadowMask;
    return builtinData;
}

VividBuiltinData SanitizeVividBuiltinData(VividBuiltinData builtinData)
{
    builtinData.bakeDiffuseLighting = max(builtinData.bakeDiffuseLighting, 0.0);
    builtinData.backBakeDiffuseLighting = max(builtinData.backBakeDiffuseLighting, 0.0);
    builtinData.shadowMask = saturate(builtinData.shadowMask);
    builtinData.hasBakedGI = saturate(builtinData.hasBakedGI);
    builtinData.isLightmap = saturate(builtinData.isLightmap);
    return builtinData;
}

#endif
