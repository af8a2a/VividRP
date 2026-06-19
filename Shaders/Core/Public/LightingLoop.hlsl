#ifndef VIVIDRP_LIGHTING_LOOP_INCLUDED
#define VIVIDRP_LIGHTING_LOOP_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/ClusteredLighting.hlsl"

struct VividLightingLoopContext
{
    VividClusteredLightCell punctualLightCell;
    VividClusteredLightCell areaLightCell;
    VividClusteredLightCell reflectionProbeCell;
    VividClusteredLightCell decalCell;
};

struct VividBigTileLightingLoopContext
{
    VividBigTileLightCell lightCell;
};

uint _PunctualLightCount;
uint _AreaLightCount;
uint _DecalCount;

struct VividLightingLoop
{
    static VividLightingLoopContext Create(uint2 pixelCoord, float viewDepth)
    {
        VividLightingLoopContext context = (VividLightingLoopContext)0;
        context.punctualLightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, viewDepth);
        context.areaLightCell = VividClusteredLighting::LoadAreaLightCell(pixelCoord, viewDepth);
        context.reflectionProbeCell = VividClusteredLighting::LoadReflectionProbeCell(pixelCoord, viewDepth);
        context.decalCell = VividClusteredLighting::LoadDecalCell(pixelCoord, viewDepth);
        return context;
    }

    static VividBigTileLightingLoopContext CreateBigTile(uint2 pixelCoord)
    {
        VividBigTileLightingLoopContext context = (VividBigTileLightingLoopContext)0;
        context.lightCell = VividClusteredLighting::LoadBigTileLightCell(pixelCoord);
        return context;
    }

    static VividLightingLoopContext Create(uint2 pixelCoord, float3 positionWS)
    {
        VividLightingLoopContext context = (VividLightingLoopContext)0;
        context.punctualLightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, positionWS);
        context.areaLightCell = VividClusteredLighting::LoadAreaLightCell(pixelCoord, positionWS);
        context.reflectionProbeCell = VividClusteredLighting::LoadReflectionProbeCell(pixelCoord, positionWS);
        context.decalCell = VividClusteredLighting::LoadDecalCell(pixelCoord, positionWS);
        return context;
    }

    static uint GetPunctualLightCount(VividLightingLoopContext context)
    {
        return context.punctualLightCell.count;
    }

    static uint GetPunctualLightIndex(VividLightingLoopContext context, uint localLightIndex)
    {
        return VividClusteredLighting::LoadLightIndex(context.punctualLightCell, localLightIndex);
    }

    static PunctualLightData LoadPunctualLight(VividLightingLoopContext context, uint localLightIndex)
    {
        uint lightIndex = GetPunctualLightIndex(context, localLightIndex);
        return GetPunctualLight(lightIndex);
    }

    static uint GetAreaLightCount(VividLightingLoopContext context)
    {
        return context.areaLightCell.count;
    }

    static uint GetAreaLightIndex(VividLightingLoopContext context, uint localLightIndex)
    {
        return VividClusteredLighting::LoadLightIndex(context.areaLightCell, localLightIndex);
    }

    static AreaLightData LoadAreaLight(VividLightingLoopContext context, uint localLightIndex)
    {
        uint lightIndex = GetAreaLightIndex(context, localLightIndex);
        return GetAreaLight(lightIndex);
    }

    static uint GetReflectionProbeCount(VividLightingLoopContext context)
    {
        return context.reflectionProbeCell.count;
    }

    static uint GetReflectionProbeIndex(VividLightingLoopContext context, uint localLightIndex)
    {
        return VividClusteredLighting::LoadLightIndex(context.reflectionProbeCell, localLightIndex);
    }

    static ReflectionProbeData LoadReflectionProbe(VividLightingLoopContext context, uint localLightIndex)
    {
        uint lightIndex = GetReflectionProbeIndex(context, localLightIndex);
        return GetReflectionProbe(lightIndex);
    }

    static bool TryLoadReflectionProbe(
        VividLightingLoopContext context,
        uint localLightIndex,
        out ReflectionProbeData probe)
    {
        probe = (ReflectionProbeData)0;
        if (localLightIndex >= GetReflectionProbeCount(context))
            return false;

        probe = LoadReflectionProbe(context, localLightIndex);
        return true;
    }

    static bool TryEvaluateReflectionProbe(
        VividLightingLoopContext context,
        uint localLightIndex,
        float3 positionWS,
        float3 normalWS,
        float3 reflectionDirectionWS,
        float perceptualRoughness,
        out float3 radiance,
        out float weight)
    {
        ReflectionProbeData probe;
        if (!TryLoadReflectionProbe(context, localLightIndex, probe))
        {
            radiance = 0.0;
            weight = 0.0;
            return false;
        }

        return TryEvaluateReflectionProbeData(
            probe,
            positionWS,
            normalWS,
            reflectionDirectionWS,
            perceptualRoughness,
            radiance,
            weight);
    }

    static bool TryEvaluateReflectionProbes(
        VividLightingLoopContext context,
        float3 positionWS,
        float3 normalWS,
        float3 reflectionDirectionWS,
        float perceptualRoughness,
        out float3 radiance,
        out float weight)
    {
        radiance = 0.0;
        weight = 0.0;

        float remainingWeight = 1.0;
        uint probeCount = GetReflectionProbeCount(context);

        [loop]
        for (uint localProbeIndex = 0u; localProbeIndex < probeCount; localProbeIndex++)
        {
            if (remainingWeight <= 0.0)
                break;

            float3 probeRadiance;
            float probeWeight;
            if (!TryEvaluateReflectionProbe(
                    context,
                    localProbeIndex,
                    positionWS,
                    normalWS,
                    reflectionDirectionWS,
                    perceptualRoughness,
                    probeRadiance,
                    probeWeight))
            {
                continue;
            }

            float contributionWeight = min(probeWeight, remainingWeight);
            radiance += probeRadiance * contributionWeight;
            weight += contributionWeight;
            remainingWeight -= contributionWeight;
        }

        return weight > 0.0;
    }

    static uint GetDecalCount(VividLightingLoopContext context)
    {
        return context.decalCell.count;
    }

    static uint GetDecalIndex(VividLightingLoopContext context, uint localIndex)
    {
        return VividClusteredLighting::LoadLightIndex(context.decalCell, localIndex);
    }

    static uint GetBigTileLightCount(VividBigTileLightingLoopContext context)
    {
        return context.lightCell.count;
    }

    static uint GetBigTileLightIndex(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        return VividClusteredLighting::LoadBigTileLightIndex(context.lightCell, localLightIndex);
    }

    static uint GetBigTilePunctualLightIndex(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        return GetBigTileLightIndex(context, localLightIndex);
    }

    static PunctualLightData LoadBigTilePunctualLight(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        uint lightIndex = GetBigTilePunctualLightIndex(context, localLightIndex);
        return GetPunctualLight(lightIndex);
    }

    static uint GetBigTileAreaLightIndex(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        return GetBigTileLightIndex(context, localLightIndex) - _PunctualLightCount;
    }

    static AreaLightData LoadBigTileAreaLight(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        uint lightIndex = GetBigTileAreaLightIndex(context, localLightIndex);
        return GetAreaLight(lightIndex);
    }

    static uint GetBigTileLightCategory(uint lightIndex)
    {
        uint areaLightStart = _PunctualLightCount;
        uint reflectionProbeStart = areaLightStart + _AreaLightCount;
        uint decalStart = reflectionProbeStart + _ReflectionProbeCount;
        uint finiteLightEnd = decalStart + _DecalCount;

        if (lightIndex < areaLightStart)
            return LIGHTCATEGORY_PUNCTUAL;

        if (lightIndex < reflectionProbeStart)
            return LIGHTCATEGORY_AREA;

        if (lightIndex < decalStart)
            return LIGHTCATEGORY_ENV;

        if (lightIndex < finiteLightEnd)
            return LIGHTCATEGORY_DECAL;

        return LIGHTCATEGORY_COUNT;
    }

    static bool TryGetBigTileReflectionProbeIndex(
        VividBigTileLightingLoopContext context,
        uint localReflectionProbeIndex,
        out uint reflectionProbeIndex)
    {
        reflectionProbeIndex = 0u;
        uint lightCount = GetBigTileLightCount(context);
        uint reflectionProbeOrdinal = 0u;

        [loop]
        for (uint localLightIndex = 0u; localLightIndex < lightCount; localLightIndex++)
        {
            uint lightIndex = GetBigTileLightIndex(context, localLightIndex);
            if (GetBigTileLightCategory(lightIndex) != LIGHTCATEGORY_ENV)
                continue;

            if (reflectionProbeOrdinal == localReflectionProbeIndex)
            {
                reflectionProbeIndex = lightIndex - _PunctualLightCount - _AreaLightCount;
                return true;
            }

            reflectionProbeOrdinal++;
        }

        return false;
    }

    static uint GetBigTileReflectionProbeIndex(
        VividBigTileLightingLoopContext context,
        uint localReflectionProbeIndex)
    {
        uint reflectionProbeIndex = 0u;
        TryGetBigTileReflectionProbeIndex(context, localReflectionProbeIndex, reflectionProbeIndex);
        return reflectionProbeIndex;
    }

    static ReflectionProbeData LoadBigTileReflectionProbe(
        VividBigTileLightingLoopContext context,
        uint localReflectionProbeIndex)
    {
        uint reflectionProbeIndex = 0u;
        if (!TryGetBigTileReflectionProbeIndex(context, localReflectionProbeIndex, reflectionProbeIndex))
            return (ReflectionProbeData)0;

        return GetReflectionProbe(reflectionProbeIndex);
    }

    static bool TryLoadBigTileReflectionProbe(
        VividBigTileLightingLoopContext context,
        uint localReflectionProbeIndex,
        out ReflectionProbeData probe)
    {
        probe = (ReflectionProbeData)0;

        uint reflectionProbeIndex = 0u;
        if (!TryGetBigTileReflectionProbeIndex(context, localReflectionProbeIndex, reflectionProbeIndex))
            return false;

        probe = GetReflectionProbe(reflectionProbeIndex);
        return true;
    }

    static bool TryEvaluateBigTileReflectionProbe(
        VividBigTileLightingLoopContext context,
        uint localLightIndex,
        float3 positionWS,
        float3 normalWS,
        float3 reflectionDirectionWS,
        float perceptualRoughness,
        out float3 radiance,
        out float weight)
    {
        ReflectionProbeData probe;
        if (!TryLoadBigTileReflectionProbe(context, localLightIndex, probe))
        {
            radiance = 0.0;
            weight = 0.0;
            return false;
        }

        return TryEvaluateReflectionProbeData(
            probe,
            positionWS,
            normalWS,
            reflectionDirectionWS,
            perceptualRoughness,
            radiance,
            weight);
    }

    static bool TryEvaluateBigTileReflectionProbes(
        VividBigTileLightingLoopContext context,
        float3 positionWS,
        float3 normalWS,
        float3 reflectionDirectionWS,
        float perceptualRoughness,
        out float3 radiance,
        out float weight)
    {
        radiance = 0.0;
        weight = 0.0;

        float remainingWeight = 1.0;
        uint lightCount = GetBigTileLightCount(context);

        [loop]
        for (uint localLightIndex = 0u; localLightIndex < lightCount; localLightIndex++)
        {
            if (remainingWeight <= 0.0)
                break;

            uint lightIndex = GetBigTileLightIndex(context, localLightIndex);
            if (GetBigTileLightCategory(lightIndex) != LIGHTCATEGORY_ENV)
                continue;

            uint reflectionProbeIndex = lightIndex - _PunctualLightCount - _AreaLightCount;
            ReflectionProbeData probe = GetReflectionProbe(reflectionProbeIndex);
            float3 probeRadiance;
            float probeWeight;
            if (!TryEvaluateReflectionProbeData(
                    probe,
                    positionWS,
                    normalWS,
                    reflectionDirectionWS,
                    perceptualRoughness,
                    probeRadiance,
                    probeWeight))
            {
                continue;
            }

            float contributionWeight = min(probeWeight, remainingWeight);
            radiance += probeRadiance * contributionWeight;
            weight += contributionWeight;
            remainingWeight -= contributionWeight;
        }

        return weight > 0.0;
    }

    static uint GetBigTileDecalIndex(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        return GetBigTileLightIndex(context, localLightIndex) - _PunctualLightCount - _AreaLightCount - _ReflectionProbeCount;
    }

    static uint CountBigTileLightsByCategory(VividBigTileLightingLoopContext context, uint lightCategory)
    {
        uint selectedLightCount = 0u;
        uint lightCount = GetBigTileLightCount(context);

        [loop]
        for (uint localLightIndex = 0u; localLightIndex < lightCount; localLightIndex++)
        {
            uint lightIndex = GetBigTileLightIndex(context, localLightIndex);
            selectedLightCount += GetBigTileLightCategory(lightIndex) == lightCategory ? 1u : 0u;
        }

        return selectedLightCount;
    }

    static uint GetBigTilePunctualLightCount(VividBigTileLightingLoopContext context)
    {
        return CountBigTileLightsByCategory(context, LIGHTCATEGORY_PUNCTUAL);
    }

    static uint GetBigTileAreaLightCount(VividBigTileLightingLoopContext context)
    {
        return CountBigTileLightsByCategory(context, LIGHTCATEGORY_AREA);
    }

    static uint GetBigTileReflectionProbeCount(VividBigTileLightingLoopContext context)
    {
        return CountBigTileLightsByCategory(context, LIGHTCATEGORY_ENV);
    }

    static uint GetBigTileDecalCount(VividBigTileLightingLoopContext context)
    {
        return CountBigTileLightsByCategory(context, LIGHTCATEGORY_DECAL);
    }
};

#endif
