#ifndef VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED
#define VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividMaterialNodeLibrary.hlsl"

// Unified vertex evaluation context for optional WPO hooks (@vivid-vertex-begin..end),
// shared across VividPreDepth, ShadowCaster, VividGBuffer, and VividVisibilityRaster.
struct VividSlabVertexContext
{
    float3 positionOS;
    float3 positionWS;
    float3 normalOS;
    float3 normalWS;
    float2 uv0;
    uint parameterAddress;
    uint resourceAddress;
};

// Unified surface evaluation context shared between Raster (.slab.shader Shader MainAsset),
// GPUDriven standalone binned ComputeShader, and GPUDriven Programmable VisibilityRaster.
struct VividSlabEvalContext
{
    float2 uv0;
    float2 uv0Ddx;
    float2 uv0Ddy;
    float3 geometryNormalWS;
    float4 geometryTangentWS;
    float3 positionWS;
    float4 positionCS;
    uint parameterAddress;
    uint resourceAddress;
};

// Multi-Closure evaluation result (Phase S2: up to 4 closures, 3 operators -> Substrate Tier 3).
struct VividSlabMultiClosureOutput
{
    uint closureCount;
    VividOpenPBRV2Inputs closures[VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL];
    VividSubstrateOperatorDesc operators[VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL];
    float ambientOcclusion;
};

VividSlabMultiClosureOutput VividCreateDefaultSlabMultiClosureOutput()
{
    VividSlabMultiClosureOutput mc;
    mc.closureCount = 1u;
    mc.ambientOcclusion = 1.0f;
    [unroll]
    for (uint i = 0u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
        mc.closures[i] = VividCreateDefaultOpenPBRV2Inputs();
    [unroll]
    for (uint j = 0u; j < VIVID_SUBSTRATE_MAX_OPERATORS_PER_PIXEL; ++j)
        mc.operators[j] = VividCreateDefaultSubstrateOperatorDesc(VIVID_SUBSTRATE_TOPOLOGY_HORIZONTAL_MIX, 0.5f);
    return mc;
}

// Builds a representative TopLayer Surface Summary from a Multi-Closure stack for Lumen GI,
// Lumen Reflections, SSR, and Tile Classification (authoritative multi-lobe shading runs in Tier 3).
VividSurfaceSummaryData VividBuildMultiClosureTopLayerSummary(
    VividSlabMultiClosureOutput mc,
    bool receiveSSR,
    bool receiveDecals)
{
    uint count = clamp(mc.closureCount, 1u, VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL);
    VividOpenPBRV2Inputs top = mc.closures[0];
    [unroll]
    for (uint i = 1u; i < VIVID_SUBSTRATE_MAX_CLOSURES_PER_PIXEL; ++i)
    {
        if (i < count)
        {
            VividSubstrateOperatorDesc op = mc.operators[i - 1u];
            float w = saturate(op.weight);
            top.core.baseColor = lerp(top.core.baseColor, mc.closures[i].core.baseColor, w);
            top.core.baseMetalness = lerp(top.core.baseMetalness, mc.closures[i].core.baseMetalness, w);
            top.core.specularRoughness = lerp(top.core.specularRoughness, mc.closures[i].core.specularRoughness, w);
            top.core.specularIor = lerp(top.core.specularIor, mc.closures[i].core.specularIor, w);
            top.core.normalWS = VividSanitizeSurfaceSummaryNormal(lerp(top.core.normalWS, mc.closures[i].core.normalWS, w));
            float3 curEm = top.core.emissionLuminance * top.core.emissionColor;
            float3 nextEm = mc.closures[i].core.emissionLuminance * mc.closures[i].core.emissionColor;
            float3 blendedEm = lerp(curEm, nextEm, w);
            top.core.emissionLuminance = any(blendedEm > 0.0f) ? 1.0f : 0.0f;
            top.core.emissionColor = blendedEm;
            top.coatWeight = max(top.coatWeight, mc.closures[i].coatWeight * w);
            top.coatRoughness = lerp(top.coatRoughness, mc.closures[i].coatRoughness, w);
        }
    }

    return VividBuildStandardLitSubstrateTopLayerSummary(
        top,
        mc.ambientOcclusion,
        receiveSSR,
        receiveDecals);
}

#if defined(VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE) || defined(VIVID_SLAB_TARGET_VISIBILITY_RASTER)

float4 VividSlabSampleResourceColor(
    const VividSlabEvalContext ctx,
    const uint resourceSlot,
    const float2 uv)
{
    const VividMaterialResourceData res = PullMaterialResourceData(ctx.resourceAddress + resourceSlot);
    VividSurfaceBindingData binding;
    binding.BaseColorResource = res.BaseColorResource;
    binding.NormalResource = res.NormalResource;
    binding.MaskResource = res.MaskResource;
    binding.Flags = res.SurfaceBindingFlags;
    binding.UVScaleBias = res.UVScaleBias;

    const float2 tiledUv = uv * res.TextureTilingOffset.xy + res.TextureTilingOffset.zw;
    const float2 tiledDdx = ctx.uv0Ddx * res.TextureTilingOffset.xy;
    const float2 tiledDdy = ctx.uv0Ddy * res.TextureTilingOffset.xy;
    #if defined(VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE)
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy,
        ctx.positionCS);
    #else
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy);
    #endif
    return VividSampleBaseColorGrad(binding, sampleCtx);
}

float4 VividSlabSampleResourceNormal(
    const VividSlabEvalContext ctx,
    const uint resourceSlot,
    const float2 uv)
{
    const VividMaterialResourceData res = PullMaterialResourceData(ctx.resourceAddress + resourceSlot);
    VividSurfaceBindingData binding;
    binding.BaseColorResource = res.BaseColorResource;
    binding.NormalResource = res.NormalResource;
    binding.MaskResource = res.MaskResource;
    binding.Flags = res.SurfaceBindingFlags;
    binding.UVScaleBias = res.UVScaleBias;

    const float2 tiledUv = uv * res.TextureTilingOffset.xy + res.TextureTilingOffset.zw;
    const float2 tiledDdx = ctx.uv0Ddx * res.TextureTilingOffset.xy;
    const float2 tiledDdy = ctx.uv0Ddy * res.TextureTilingOffset.xy;
    #if defined(VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE)
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy,
        ctx.positionCS);
    #else
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy);
    #endif
    return VividSampleNormalGrad(binding, sampleCtx);
}

#define VIVID_SLAB_SAMPLE_2D(texPropName, resourceSlot, uv) \
    VividSlabSampleResourceColor(ctx, (resourceSlot), (uv))

#define VIVID_SLAB_SAMPLE_NORMAL_2D(texPropName, resourceSlot, uv) \
    VividSlabSampleResourceNormal(ctx, (resourceSlot), (uv))

#define VIVID_SLAB_PARAM_FLOAT(propName, wordOffset) \
    VividLoadMaterialFloat(ctx.parameterAddress, (wordOffset))

#define VIVID_SLAB_PARAM_FLOAT2(propName, wordOffset) \
    VividLoadMaterialFloat2(ctx.parameterAddress, (wordOffset))

#define VIVID_SLAB_PARAM_FLOAT3(propName, wordOffset) \
    VividLoadMaterialFloat3(ctx.parameterAddress, (wordOffset))

#define VIVID_SLAB_PARAM_FLOAT4(propName, wordOffset) \
    VividLoadMaterialFloat4(ctx.parameterAddress, (wordOffset))

#else // VIVID_SLAB_TARGET_RASTER

#define VIVID_SLAB_SAMPLE_2D(texPropName, resourceSlot, uv) \
    SAMPLE_TEXTURE2D(texPropName, sampler##texPropName, (uv) * texPropName##_ST.xy + texPropName##_ST.zw)

#define VIVID_SLAB_SAMPLE_NORMAL_2D(texPropName, resourceSlot, uv) \
    SAMPLE_TEXTURE2D(texPropName, sampler##texPropName, (uv) * texPropName##_ST.xy + texPropName##_ST.zw)

#define VIVID_SLAB_PARAM_FLOAT(propName, wordOffset) (propName)
#define VIVID_SLAB_PARAM_FLOAT2(propName, wordOffset) ((propName).xy)
#define VIVID_SLAB_PARAM_FLOAT3(propName, wordOffset) ((propName).xyz)
#define VIVID_SLAB_PARAM_FLOAT4(propName, wordOffset) (propName)

#endif

#endif // VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED
