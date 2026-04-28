#ifndef VIVIDRP_VBUFFER_INCLUDED
#define VIVIDRP_VBUFFER_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Volumetric/ShaderVariablesVolumetric.hlsl"
//has been define in Common.hlsl
// float EncodeLogarithmicDepthGeneralized(float distance, float4 encodingParams)
// {
//     return encodingParams.x + encodingParams.y * log2(max(0.0, distance - encodingParams.z));
// }
//
// float DecodeLogarithmicDepthGeneralized(float encodedDepth, float4 decodingParams)
// {
//     return decodingParams.x * exp2(encodedDepth * decodingParams.y) + decodingParams.z;
// }

float GetVBufferSliceDistance(float sliceCoord)
{
    float encodedDepth = saturate(sliceCoord * _VBufferRcpSliceCount);
    return DecodeLogarithmicDepthGeneralized(encodedDepth, _VBufferDepthDecodingParams);
}

float GetVBufferSliceBoundaryDistance(float sliceIndex)
{
    float encodedDepth = saturate(sliceIndex * _VBufferRcpSliceCount);
    return DecodeLogarithmicDepthGeneralized(encodedDepth, _VBufferDepthDecodingParams);
}

float GetVBufferSliceCoordFromLinearDepth(float linearDepth)
{
    return EncodeLogarithmicDepthGeneralized(linearDepth, _VBufferDepthEncodingParams) * _VBufferSliceCount;
}

float3 GetVBufferUVW(float2 uv, float linearDepth)
{
    float encodedDepth = EncodeLogarithmicDepthGeneralized(linearDepth, _VBufferDepthEncodingParams);
    encodedDepth -= (sqrt(3.0) * 0.5) * _VBufferRcpSliceCount;
    return float3(uv, saturate(encodedDepth));
}

bool IsVBufferFarDepth(float deviceDepth)
{
    return abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) <= 1e-5;
}

float3 GetVBufferRayDirectionWSFromPixelCoord(float2 pixelCoord)
{
    float3 rayDirectionWS = mul(-float4(pixelCoord, 1.0, 1.0), _VBufferCoordToViewDirWS).xyz;
    return SafeNormalize(rayDirectionWS);
}

float3 GetVBufferRayDirectionWSFromUV(float2 uv)
{
    return GetVBufferRayDirectionWSFromPixelCoord(uv * _VBufferViewportSize.xy);
}

float GetVBufferLinearDistanceFromEyeDepth(float2 uv, float linearEyeDepth)
{
    if (_VBufferIsOrthographic > 0.5)
        return linearEyeDepth;

    float3 rayDirectionWS = GetVBufferRayDirectionWSFromUV(uv);
    float forwardDistance = max(dot(rayDirectionWS, GetViewForwardDir()), 1e-4);
    return linearEyeDepth / forwardDistance;
}

float GetVBufferLinearDistanceFromDeviceDepth(float2 uv, float deviceDepth)
{
    return IsVBufferFarDepth(deviceDepth)
        ? _VBufferLastSliceDistance
        : GetVBufferLinearDistanceFromEyeDepth(uv, LinearEyeDepth(deviceDepth, _ZBufferParams));
}

float3 GetVBufferVoxelWorldPosition(uint3 voxelCoord)
{
    float2 pixelCoord = float2(voxelCoord.xy) + 0.5;
    float sliceDistance = GetVBufferSliceDistance((float)voxelCoord.z + 0.5);
    float3 rayDirectionWS = GetVBufferRayDirectionWSFromPixelCoord(pixelCoord);
    return _WorldSpaceCameraPos + rayDirectionWS * sliceDistance;
}

float GetVBufferSliceLength(uint sliceIndex)
{
    float startDistance = GetVBufferSliceBoundaryDistance((float)sliceIndex);
    float endDistance = GetVBufferSliceBoundaryDistance((float)sliceIndex + 1.0);
    return max(endDistance - startDistance, 1e-4);
}

bool IsInsideVBuffer(uint3 voxelCoord)
{
    return voxelCoord.x < (uint)_VBufferWidth
        && voxelCoord.y < (uint)_VBufferHeight
        && voxelCoord.z < (uint)_VBufferSliceCount;
}

#endif
