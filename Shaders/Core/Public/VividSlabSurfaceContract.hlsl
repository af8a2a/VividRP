#ifndef VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED
#define VIVIDRP_SLAB_SURFACE_CONTRACT_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividMaterialNodeLibrary.hlsl"

// Unified surface evaluation context shared between Raster (.slab.shader Shader MainAsset)
// and GPUDriven standalone binned ComputeShader (.slab.shader ComputeShader SubAsset).
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

#if defined(VIVID_SLAB_TARGET_GPUDRIVEN_COMPUTE)

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
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy,
        ctx.positionCS);
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
    const VividSurfaceSampleContext sampleCtx = VividCreateSurfaceSampleContextGrad(
        binding,
        tiledUv,
        tiledDdx,
        tiledDdy,
        ctx.positionCS);
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
