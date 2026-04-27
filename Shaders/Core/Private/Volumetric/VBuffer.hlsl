#ifndef VIVIDRP_VBUFFER_INCLUDED
#define VIVIDRP_VBUFFER_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Private/Volumetric/ShaderVariablesVolumetric.hlsl"

float GetVBufferSliceDistance(float sliceCoord)
{
    float normalizedDepth = saturate(sliceCoord * _VBufferRcpSliceCount);
    float distributedDepth = pow(normalizedDepth, max(_VBufferDepthPower, 0.0001));
    return distributedDepth * _VBufferDepthExtent;
}

float GetVBufferSliceCoordFromLinearDepth(float linearDepth)
{
    float normalizedDepth = saturate(linearDepth * _VBufferRcpDepthExtent);
    return pow(normalizedDepth, max(_VBufferInvDepthPower, 0.0001)) * _VBufferSliceCount;
}

float3 GetVBufferUVW(float2 uv, float linearDepth)
{
    float sliceCoord = GetVBufferSliceCoordFromLinearDepth(linearDepth);
    return float3(uv, saturate((sliceCoord + 0.5) * _VBufferRcpSliceCount));
}

float3 GetVBufferVoxelWorldPosition(uint3 voxelCoord)
{
    float2 uv = (float2(voxelCoord.xy) + 0.5) * _VBufferRcpViewportSize;
    float sliceDistance = GetVBufferSliceDistance((float)voxelCoord.z + 0.5);
    float rawFarDepth = UNITY_RAW_FAR_CLIP_VALUE;
    float3 farPositionWS = ComputeWorldSpacePosition(uv, rawFarDepth, UNITY_MATRIX_I_VP);
    float3 rayDirectionWS = SafeNormalize(farPositionWS - _WorldSpaceCameraPos);
    return _WorldSpaceCameraPos + rayDirectionWS * sliceDistance;
}

float GetVBufferSliceLength()
{
    return _VBufferDepthExtent * _VBufferRcpSliceCount;
}

bool IsInsideVBuffer(uint3 voxelCoord)
{
    return voxelCoord.x < (uint)_VBufferWidth
        && voxelCoord.y < (uint)_VBufferHeight
        && voxelCoord.z < (uint)_VBufferSliceCount;
}

#endif
