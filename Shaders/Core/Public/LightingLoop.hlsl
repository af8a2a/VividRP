#ifndef VIVIDRP_LIGHTING_LOOP_INCLUDED
#define VIVIDRP_LIGHTING_LOOP_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/ClusteredLighting.hlsl"

struct VividLightingLoopContext
{
    VividClusteredLightCell punctualLightCell;
    VividClusteredLightCell areaLightCell;
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

    static uint GetBigTileDecalIndex(VividBigTileLightingLoopContext context, uint localLightIndex)
    {
        return GetBigTileLightIndex(context, localLightIndex) - _PunctualLightCount - _AreaLightCount;
    }

    static uint GetBigTileLightCategory(uint lightIndex)
    {
        uint areaLightStart = _PunctualLightCount;
        uint decalStart = areaLightStart + _AreaLightCount;
        uint finiteLightEnd = decalStart + _DecalCount;

        if (lightIndex < areaLightStart)
            return LIGHTCATEGORY_PUNCTUAL;

        if (lightIndex < decalStart)
            return LIGHTCATEGORY_AREA;

        if (lightIndex < finiteLightEnd)
            return LIGHTCATEGORY_DECAL;

        return LIGHTCATEGORY_COUNT;
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

    static uint GetBigTileDecalCount(VividBigTileLightingLoopContext context)
    {
        return CountBigTileLightsByCategory(context, LIGHTCATEGORY_DECAL);
    }
};

#endif
