#ifndef VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
#define VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED

#include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"

struct VividSurfaceSampleContext
{
    float2 uv;
    float2 uvDdx;
    float2 uvDdy;
    bool clamp;
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

bool VividSurfaceUsesClamp(const VividSurfaceBindingData bindingData)
{
    return bindingData.UVScaleBias.x < 0.0f;
}

float2 VividGetSurfaceBindingUVScale(const VividSurfaceBindingData bindingData)
{
    return abs(bindingData.UVScaleBias.xy);
}

float2 VividApplySurfaceBindingUV(const VividSurfaceBindingData bindingData, const float2 uv)
{
    return uv * VividGetSurfaceBindingUVScale(bindingData) + bindingData.UVScaleBias.zw;
}

VividSurfaceSampleContext VividCreateSurfaceSampleContextGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context;
    context.uv = VividApplySurfaceBindingUV(bindingData, uv);
    float2 bindingScale = VividGetSurfaceBindingUVScale(bindingData);
    context.uvDdx = uvDdx * bindingScale;
    context.uvDdy = uvDdy * bindingScale;
    context.clamp = VividSurfaceUsesClamp(bindingData);
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
        float2 sampleUv = VividApplySurfaceBindingUV(bindingData, uv);
        return VividSurfaceUsesClamp(bindingData)
            ? SAMPLE_TEXTURE2D(texture, sampler_LinearClamp, sampleUv)
            : SAMPLE_TEXTURE2D(texture, sampler_LinearRepeat, sampleUv);
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
        return context.clamp
            ? SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearClamp, context.uv, context.uvDdx, context.uvDdy)
            : SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
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
        return context.clamp
            ? SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearClamp, context.uv, context.uvDdx, context.uvDdy)
            : SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
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
        return context.clamp
            ? SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearClamp, context.uv, context.uvDdx, context.uvDdy)
            : SAMPLE_TEXTURE2D_GRAD(texture, sampler_LinearRepeat, context.uv, context.uvDdx, context.uvDdy);
    }

    return 1.0f.xxxx;
}

// Raw is a semantic sampling class. It currently reuses the mask resource as
// its physical no-color-space carrier, without exposing that carrier choice to
// generated material programs.
float4 VividSampleRawGrad(
    const VividSurfaceBindingData bindingData,
    const VividSurfaceSampleContext context)
{
    return VividSampleMaskGrad(bindingData, context);
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

float4 VividSampleRawGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    VividSurfaceSampleContext context = VividCreateSurfaceSampleContextGrad(bindingData, uv, uvDdx, uvDdy);
    return VividSampleRawGrad(bindingData, context);
}

#endif // VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
