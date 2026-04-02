#ifndef VIVIDRP_AUTO_EXPOSURE_INCLUDED
#define VIVIDRP_AUTO_EXPOSURE_INCLUDED

StructuredBuffer<float4> _VividAutoExposurePreExposureBuffer;

float VividGetPreExposure()
{
    return max(_VividAutoExposurePreExposureBuffer[0].x, 1e-4);
}

float VividGetOneOverPreExposure()
{
    return rcp(VividGetPreExposure());
}

float3 VividApplyPreExposure(float3 color)
{
    return color * VividGetPreExposure();
}

#endif
