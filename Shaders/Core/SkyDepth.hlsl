#ifndef VIVIDRP_SKY_DEPTH_INCLUDED
#define VIVIDRP_SKY_DEPTH_INCLUDED

static const float VIVID_SKY_DEPTH_EPSILON = 1e-5;

float GetSkyDepthClearValue()
{
#if UNITY_REVERSED_Z
    return 0.0;
#else
    return 1.0;
#endif
}

bool IsSkyPixel(float deviceDepth)
{
#if UNITY_REVERSED_Z
    return deviceDepth <= (GetSkyDepthClearValue() + VIVID_SKY_DEPTH_EPSILON);
#else
    return deviceDepth >= (GetSkyDepthClearValue() - VIVID_SKY_DEPTH_EPSILON);
#endif
}

#endif
