#ifndef VIVIDRP_VIRTUAL_TEXTURE_SURFACE_SAMPLING_INCLUDED
#define VIVIDRP_VIRTUAL_TEXTURE_SURFACE_SAMPLING_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VirtualTexture/VirtualTexture.hlsl"

#define VIVID_GPU_DRIVEN_VT_RESOURCE_LAYER_MASK 0xFFu
#define VIVID_GPU_DRIVEN_VT_RESOURCE_MIP_SHIFT 8u

struct VividSurfaceSampleContext
{
    float2 virtualUv;
    float2 virtualUvDdx;
    float2 virtualUvDdy;
    VTMipRange requestedMips;
    VTResolvedAddress lowerResolved;
    VTResolvedAddress upperResolved;
};

bool VividSurfaceHasBaseColor(const VividSurfaceBindingData bindingData)
{
    return (bindingData.Flags & VIVIDSURFACEBINDINGFLAGS_BASE_COLOR) != 0u;
}

bool VividSurfaceHasNormal(const VividSurfaceBindingData bindingData)
{
    return (bindingData.Flags & VIVIDSURFACEBINDINGFLAGS_NORMAL) != 0u;
}

bool VividSurfaceHasMask(const VividSurfaceBindingData bindingData)
{
    return (bindingData.Flags & VIVIDSURFACEBINDINGFLAGS_MASK) != 0u;
}

float2 VividApplySurfaceBindingUV(const VividSurfaceBindingData bindingData, const float2 uv)
{
    return frac(uv) * bindingData.UVScaleBias.xy + bindingData.UVScaleBias.zw;
}

uint VividGetVirtualTextureResourceLayer(uint resource)
{
    return resource & VIVID_GPU_DRIVEN_VT_RESOURCE_LAYER_MASK;
}

uint VividGetVirtualTextureResourceMaxMip(uint resource)
{
    return resource >> VIVID_GPU_DRIVEN_VT_RESOURCE_MIP_SHIFT;
}

uint VividGetSurfaceVirtualTextureMaxMip(const VividSurfaceBindingData bindingData)
{
    if (VividSurfaceHasBaseColor(bindingData))
        return VividGetVirtualTextureResourceMaxMip(bindingData.BaseColorResource);
    if (VividSurfaceHasNormal(bindingData))
        return VividGetVirtualTextureResourceMaxMip(bindingData.NormalResource);
    if (VividSurfaceHasMask(bindingData))
        return VividGetVirtualTextureResourceMaxMip(bindingData.MaskResource);

    return 0u;
}

VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context;
    context.virtualUv = VividApplySurfaceBindingUV(bindingData, uv);
    context.virtualUvDdx = uvDdx * bindingData.UVScaleBias.xy;
    context.virtualUvDdy = uvDdy * bindingData.UVScaleBias.xy;
    context.requestedMips = (VTMipRange)0;
    context.lowerResolved = (VTResolvedAddress)0;
    context.upperResolved = (VTResolvedAddress)0;
    if (bindingData.Flags == 0u)
        return context;

    context.requestedMips = VTComputeRequestedMipRangeGrad(
        context.virtualUv,
        context.virtualUvDdx,
        context.virtualUvDdy,
        VividGetSurfaceVirtualTextureMaxMip(bindingData));
    context.lowerResolved = VTResolveAddress(context.virtualUv, context.requestedMips.lowerMip);
    context.upperResolved = VTResolveAddress(context.virtualUv, context.requestedMips.upperMip);
    return context;
}

VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy,
    const float4 positionCS)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);

    if (bindingData.Flags != 0u)
    {
        if (!context.lowerResolved.resident)
            VTWriteFeedback(context.virtualUv, context.requestedMips.lowerMip, positionCS);

        if (context.requestedMips.upperMip != context.requestedMips.lowerMip
            && !context.upperResolved.resident)
        {
            VTWriteFeedback(context.virtualUv, context.requestedMips.upperMip, positionCS);
        }

        VTWriteFallbackSample(
            context.virtualUv,
            context.requestedMips.lowerMip,
            context.lowerResolved,
            positionCS);
        if (!VTResolvedAddressMatches(context.lowerResolved, context.upperResolved))
        {
            VTWriteFallbackSample(
                context.virtualUv,
                context.requestedMips.upperMip,
                context.upperResolved,
                positionCS);
        }
    }

    return context;
}

float4 VividSampleVirtualTextureLayer(
    uint resource,
    const VividSurfaceSampleContext context,
    float4 fallback)
{
    if (resource == 0xFFFFFFFFu)
        return fallback;

    return VTSamplePhysicalCacheTrilinearLayer(
        context.virtualUv,
        context.lowerResolved,
        context.upperResolved,
        context.requestedMips.blend,
        VividGetVirtualTextureResourceLayer(resource));
}

float4 VividSampleBaseColorGrad(
    const VividSurfaceBindingData bindingData,
    const VividSurfaceSampleContext context)
{
    if (!VividSurfaceHasBaseColor(bindingData))
        return 1.0f.xxxx;

    uint layerIndex = VividGetVirtualTextureResourceLayer(bindingData.BaseColorResource);
    float4 color = VividSampleVirtualTextureLayer(bindingData.BaseColorResource, context, 1.0f.xxxx);
    color.rgb = VTApplyLayerColorSpace(color.rgb, layerIndex);
    return color;
}

float4 VividSampleNormalGrad(
    const VividSurfaceBindingData bindingData,
    const VividSurfaceSampleContext context)
{
    return VividSurfaceHasNormal(bindingData)
        ? VividSampleVirtualTextureLayer(
            bindingData.NormalResource,
            context,
            float4(0.5f, 0.5f, 1.0f, 0.5f))
        : float4(0.5f, 0.5f, 1.0f, 0.5f);
}

float4 VividSampleMaskGrad(
    const VividSurfaceBindingData bindingData,
    const VividSurfaceSampleContext context)
{
    return VividSurfaceHasMask(bindingData)
        ? VividSampleVirtualTextureLayer(bindingData.MaskResource, context, 1.0f.xxxx)
        : 1.0f.xxxx;
}

float4 VividSampleBaseColorGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);
    return VividSampleBaseColorGrad(bindingData, context);
}

float4 VividSampleNormalGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);
    return VividSampleNormalGrad(bindingData, context);
}

float4 VividSampleMaskGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);
    return VividSampleMaskGrad(bindingData, context);
}

float4 VividSampleBaseColor(const VividSurfaceBindingData bindingData, const float2 uv)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(
        bindingData,
        uv,
        ddx(uv),
        ddy(uv));
    return VividSampleBaseColorGrad(bindingData, context);
}

#endif // VIVIDRP_VIRTUAL_TEXTURE_SURFACE_SAMPLING_INCLUDED
