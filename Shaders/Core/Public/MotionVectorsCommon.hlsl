#ifndef VIVIDRP_MOTION_VECTORS_COMMON_INCLUDED
#define VIVIDRP_MOTION_VECTORS_COMMON_INCLUDED

float2 CalcNdcMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS)
{
    bool forceNoMotion = unity_MotionVectorsParams.y == 0.0;
    if (forceNoMotion)
        return float2(0.0, 0.0);

    float2 positionNDC = positionCS.xy * rcp(positionCS.w);
    float2 previousPositionNDC = previousPositionCS.xy * rcp(previousPositionCS.w);
    float2 velocity = positionNDC - previousPositionNDC;

#if UNITY_UV_STARTS_AT_TOP
    velocity.y = -velocity.y;
#endif

    return velocity * 0.5;
}

#endif
