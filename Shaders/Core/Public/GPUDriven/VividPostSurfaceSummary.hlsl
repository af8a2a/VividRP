#ifndef VIVIDRP_GPU_DRIVEN_POST_SURFACE_SUMMARY_INCLUDED
#define VIVIDRP_GPU_DRIVEN_POST_SURFACE_SUMMARY_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/SurfaceSummaryGBuffer.hlsl"

struct VividPostSurfaceSummaryInput
{
    float3 baseColor;
    float3 topBaseColor;
    float3 normalWS;
    float perceptualRoughness;
    float metallic;
    float ambientOcclusion;
    float3 emissive;
    float3 diffuseIrradiance;
    float topPerceptualRoughness;
    float topMetallic;
    float layerWeight;
    uint failedSurface;
    uint unlitSurface;
    uint hasVisibleTopLayer;
    uint exportDualSlab;
    uint horizontalMix;
    uint verticalLayer;
    uint hasDiffuseIrradiance;
    uint receiveSSR;
    uint receiveDecals;
};

struct VividPostSurfaceSummaryOutput
{
    VividSurfaceSummaryData surfaceData;
    VividDualSlabLayerData dualSlabLayerData;
};

VividPostSurfaceSummaryOutput VividPostSurfaceSummary(
    const VividPostSurfaceSummaryInput input)
{
    VividPostSurfaceSummaryOutput output =
        (VividPostSurfaceSummaryOutput) 0;
    output.surfaceData.normalWS = input.normalWS;
    output.surfaceData.perceptualRoughness = input.perceptualRoughness;
    output.surfaceData.ambientOcclusion = input.ambientOcclusion;
    output.surfaceData.diffuseIrradiance = input.diffuseIrradiance;

    UNITY_BRANCH
    if (input.failedSurface != 0u)
    {
        output.surfaceData.diffuseAlbedo = float3(1.0f, 0.0f, 1.0f);
        output.surfaceData.specularF0 = 0.0f;
        output.surfaceData.emissive = float3(1.0f, 0.0f, 1.0f);
        output.surfaceData.deferredExportHeader =
            VividBuildDeferredExportHeader(
                VIVID_DEFERRED_EXPORT_CLASS_ERROR,
                false,
                false,
                false,
                false);
        return output;
    }

    UNITY_BRANCH
    if (input.unlitSurface != 0u)
    {
        float3 unlitColor = input.baseColor;
        if (input.hasVisibleTopLayer != 0u)
        {
            const float layerWeight = saturate(input.layerWeight);
            if (input.horizontalMix != 0u)
            {
                unlitColor = lerp(
                    input.baseColor,
                    input.topBaseColor,
                    layerWeight);
            }
            else
            {
                const float topMetallic = saturate(input.topMetallic);
                const float3 topDiffuseAlbedo = input.topBaseColor
                    * (1.0f - topMetallic);
                const float topOpacity = saturate(
                    max(
                        topDiffuseAlbedo.x,
                        max(topDiffuseAlbedo.y, topDiffuseAlbedo.z))
                    + topMetallic);
                unlitColor = input.topBaseColor * layerWeight
                    + input.baseColor * lerp(
                        1.0f,
                        1.0f - topOpacity,
                        layerWeight);
            }
        }

        output.surfaceData.diffuseAlbedo = 0.0f;
        output.surfaceData.specularF0 = 0.0f;
        output.surfaceData.emissive = max(
            unlitColor + input.emissive,
            0.0f);
        output.surfaceData.deferredExportHeader =
            VividBuildDeferredExportHeader(
                VIVID_DEFERRED_EXPORT_CLASS_UNLIT,
                false,
                false,
                false,
                input.receiveDecals != 0u);
        return output;
    }

    const float saturatedMetallic = saturate(input.metallic);
    output.surfaceData.diffuseAlbedo = input.baseColor
        * (1.0f - saturatedMetallic);
    output.surfaceData.specularF0 = lerp(
        0.04f.xxx,
        input.baseColor,
        saturatedMetallic);
    output.surfaceData.emissive = max(input.emissive, 0.0f);

    const bool exportDualSlab = input.exportDualSlab != 0u;
    if (exportDualSlab)
    {
        const float topMetallic = saturate(input.topMetallic);
        output.dualSlabLayerData.diffuseAlbedo = input.topBaseColor
            * (1.0f - topMetallic);
        output.dualSlabLayerData.specularF0 = lerp(
            0.04f.xxx,
            input.topBaseColor,
            topMetallic);
        output.dualSlabLayerData.perceptualRoughness =
            input.topPerceptualRoughness;
        output.dualSlabLayerData.layerWeight = input.layerWeight;
    }

    output.surfaceData.deferredExportHeader =
        VividBuildDeferredExportHeader(
            exportDualSlab
                ? VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB
                : VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB,
            exportDualSlab && input.verticalLayer != 0u,
            input.hasDiffuseIrradiance != 0u,
            input.receiveSSR != 0u && !exportDualSlab,
            input.receiveDecals != 0u);
    return output;
}

#endif
