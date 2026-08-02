#ifndef VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
#define VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED

#include_with_pragmas "Packages/com.vivid.render-pipelines/Shaders/Core/Public/GPUDriven/Bindless.hlsl"

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
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    UNITY_BRANCH
    if (VividSurfaceHasBaseColor(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.BaseColorResource));
        return SAMPLE_TEXTURE2D_GRAD(
            texture,
            sampler_LinearRepeat,
            VividApplySurfaceBindingUV(bindingData, uv),
            uvDdx * bindingData.UVScaleBias.xy,
            uvDdy * bindingData.UVScaleBias.xy);
    }

    return 1.0f.xxxx;
}

float4 VividSampleNormalGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    UNITY_BRANCH
    if (VividSurfaceHasNormal(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.NormalResource));
        return SAMPLE_TEXTURE2D_GRAD(
            texture,
            sampler_LinearRepeat,
            VividApplySurfaceBindingUV(bindingData, uv),
            uvDdx * bindingData.UVScaleBias.xy,
            uvDdy * bindingData.UVScaleBias.xy);
    }

    return float4(0.5f, 0.5f, 1.0f, 1.0f);
}

float4 VividSampleMaskGrad(
    const VividSurfaceBindingData bindingData,
    const float2 uv,
    const float2 uvDdx,
    const float2 uvDdy)
{
    UNITY_BRANCH
    if (VividSurfaceHasMask(bindingData))
    {
        Texture2D texture = GetBindlessTexture2D(NonUniformResourceIndex(bindingData.MaskResource));
        return SAMPLE_TEXTURE2D_GRAD(
            texture,
            sampler_LinearRepeat,
            VividApplySurfaceBindingUV(bindingData, uv),
            uvDdx * bindingData.UVScaleBias.xy,
            uvDdy * bindingData.UVScaleBias.xy);
    }

    return 1.0f.xxxx;
}

#endif // VIVIDRP_BINDLESS_SURFACE_SAMPLING_INCLUDED
