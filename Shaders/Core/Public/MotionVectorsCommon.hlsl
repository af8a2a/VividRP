#ifndef VIVIDRP_MOTION_VECTORS_COMMON_INCLUDED
#define VIVIDRP_MOTION_VECTORS_COMMON_INCLUDED

#include "MotionVectorEncoding.hlsl"
#define VIVIDRP_MOTION_VECTOR_NO_MOTION_SENTINEL 0.0
#define VIVIDRP_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD (0.01 * _ScreenSize.zw)

void EncodeMotionVector(float2 motionVector, out float4 outBuffer)
{
    outBuffer = VividEncodeVelocity(float3(2.0 * motionVector, 0.0), 0u);
}

float4 EncodeNoMotionVector()
{
    return 0.0;
}

bool ForceNoMotionVector()
{
#if defined(DOTS_INSTANCING_ON)
    return false;
#else
    return unity_MotionVectorsParams.y == 0.0;
#endif
}

float2 CalcNdcMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS, float maxClipMotion)
{
    if (ForceNoMotionVector())
        return float2(0.0, 0.0);

    float2 positionNDC = positionCS.xy * rcp(positionCS.w);
    float2 previousPositionNDC = previousPositionCS.xy * rcp(previousPositionCS.w);
    float2 velocity = positionNDC - previousPositionNDC;

    velocity.x = abs(velocity.x) < VIVIDRP_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.x ? 0.0 : velocity.x;
    velocity.y = abs(velocity.y) < VIVIDRP_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD.y ? 0.0 : velocity.y;
    velocity = clamp(
        velocity,
        -maxClipMotion + VIVIDRP_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD,
        maxClipMotion - VIVIDRP_MOTION_VECTOR_MICRO_MOVEMENT_THRESHOLD);

#if UNITY_UV_STARTS_AT_TOP
    velocity.y = -velocity.y;
#endif

    return velocity * 0.5;
}

float2 CalcNdcMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS)
{
    return CalcNdcMotionVectorFromCsPositions(positionCS, previousPositionCS, 2.0);
}

float2 CalcCameraNdcMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS)
{
    return CalcNdcMotionVectorFromCsPositions(positionCS, previousPositionCS, 1.0);
}

float4 EncodeMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS)
{
    if (ForceNoMotionVector())
        return EncodeNoMotionVector();

    if (positionCS.w <= 0.0 || previousPositionCS.w <= 0.0)
        return EncodeNoMotionVector();
    float2 motionUV = CalcNdcMotionVectorFromCsPositions(positionCS, previousPositionCS);
    float deltaZ = positionCS.z / positionCS.w - previousPositionCS.z / previousPositionCS.w;
    return VividEncodeVelocity(float3(motionUV * 2.0, deltaZ), 1u);
}

float4 EncodeCameraMotionVectorFromCsPositions(float4 positionCS, float4 previousPositionCS)
{
    if (ForceNoMotionVector())
        return EncodeNoMotionVector();

    if (positionCS.w <= 0.0 || previousPositionCS.w <= 0.0)
        return EncodeNoMotionVector();
    float2 motionUV = CalcCameraNdcMotionVectorFromCsPositions(positionCS, previousPositionCS);
    float deltaZ = positionCS.z / positionCS.w - previousPositionCS.z / previousPositionCS.w;
    return VividEncodeVelocity(float3(motionUV * 2.0, deltaZ), 0u);
}

#endif
