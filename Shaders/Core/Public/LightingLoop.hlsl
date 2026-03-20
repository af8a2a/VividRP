#ifndef VIVIDRP_LIGHTING_LOOP_INCLUDED
#define VIVIDRP_LIGHTING_LOOP_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/ClusteredLighting.hlsl"

struct VividLightingLoopContext
{
    VividClusteredLightCell punctualLightCell;
};

struct VividLightingLoop
{
    static VividLightingLoopContext Create(uint2 pixelCoord, float viewDepth)
    {
        VividLightingLoopContext context = (VividLightingLoopContext)0;
        context.punctualLightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, viewDepth);
        return context;
    }

    static VividLightingLoopContext Create(uint2 pixelCoord, float3 positionWS)
    {
        VividLightingLoopContext context = (VividLightingLoopContext)0;
        context.punctualLightCell = VividClusteredLighting::LoadPunctualLightCell(pixelCoord, positionWS);
        return context;
    }

    static uint GetPunctualLightCount(VividLightingLoopContext context)
    {
        return context.punctualLightCell.count;
    }

    static bool HasPunctualLights(VividLightingLoopContext context)
    {
        return GetPunctualLightCount(context) > 0u;
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
};

#endif
