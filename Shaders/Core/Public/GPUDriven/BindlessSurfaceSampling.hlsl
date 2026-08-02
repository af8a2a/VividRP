#ifndef VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
#define VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED

#include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"

struct VividSurfaceSampleContext
{
    float2 uv;
    float2 uvDdx;
    float2 uvDdy;
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
    return uv * bindingData.UVScaleBias.xy + bindingData.UVScaleBias.zw;
}

VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context;
    context.uv = VividApplySurfaceBindingUV(bindingData, uv);
    context.uvDdx = uvDdx * bindingData.UVScaleBias.xy;
    context.uvDdy = uvDdy * bindingData.UVScaleBias.xy;
    return context;
}

VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy,
    const float4 positionCS)
{
    return VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);
}

float4 VividSampleBaseColor(const VividSurfaceBindingData bindingData, const float2 uv)
{
    UNITY_BRANCH
    if (VividSurfaceHasBaseColor(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.BaseColorResource));
        return SAMPLE_TEXTURE2D(texture, sampler_LinearRepeat, VividApplySurfaceBindingUV(bindingData, uv));
    }

    return 1.0f.xxxx;
}

float4 VividSampleBaseColorGrad(
    const VividSurfaceBindingData bindingData,
    const VividSurfaceSampleContext context)
{
    UNITY_BRANCH
    if (VividSurfaceHasBaseColor(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.BaseColorResource));
        return SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
    }

    return 1.0f.xxxx;
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
    const VividSurfaceSampleContext context)
{
    UNITY_BRANCH
    if (VividSurfaceHasNormal(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.NormalResource));
        return SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
    }

    return float4(0.5f, 0.5f, 1.0f, 0.5f);
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
    const VividSurfaceSampleContext context)
{
    UNITY_BRANCH
    if (VividSurfaceHasMask(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.MaskResource));
        return SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
    }

    return 1.0f.xxxx;
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

#endif // VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
