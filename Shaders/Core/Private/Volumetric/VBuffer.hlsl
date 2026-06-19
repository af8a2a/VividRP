#ifndef VIVIDRP_VBUFFER_INCLUDED
#define VIVIDRP_VBUFFER_INCLUDED

#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Private/Volumetric/ShaderVariablesVolumetric.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
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

float4 SampleVBuffer(TEXTURE3D_PARAM(VBuffer, clampSampler),
                     float2 positionNDC,
                     float linearDistance,
                     float4 vBufferViewportSize,
                     float2 vBufferRcpViewportSize,
                     float3 vBufferViewportScale,
                     float3 vBufferViewportLimit,
                     float4 vBufferDepthEncodingParams,
                     float4 vBufferDepthDecodingParams,
                     bool biasLookup,
                     bool quadraticFilterXY,
                     bool clampToBorder)
{
    float2 uv = positionNDC;
    float w = EncodeLogarithmicDepthGeneralized(linearDistance, vBufferDepthEncodingParams);

    if (biasLookup)
        w -= (sqrt(3.0) * 0.5) * _VBufferRcpSliceCount;

    bool coordIsInsideFrustum = true;
    if (clampToBorder)
    {
        float3 positionCS = float3(uv, w) * 2.0 - 1.0;
        coordIsInsideFrustum = max(max(abs(positionCS.x), abs(positionCS.y)), abs(positionCS.z)) < 1.0;
    }

    float4 result = 0.0;
    if (coordIsInsideFrustum)
    {
        if (quadraticFilterXY)
        {
            float2 xy = uv * vBufferViewportSize.xy;
            float2 ic = floor(xy);
            float2 fc = frac(xy);

            float2 weights[2], offsets[2];
            BiquadraticFilter(1.0 - fc, weights, offsets);

            float2 rcpBufferDim = vBufferViewportScale.xy * vBufferRcpViewportSize;
            float2 texUv0 = (ic + float2(offsets[0].x, offsets[0].y)) * rcpBufferDim;
            float2 texUv1 = (ic + float2(offsets[1].x, offsets[0].y)) * rcpBufferDim;
            float2 texUv2 = (ic + float2(offsets[0].x, offsets[1].y)) * rcpBufferDim;
            float2 texUv3 = (ic + float2(offsets[1].x, offsets[1].y)) * rcpBufferDim;
            float texW = w * vBufferViewportScale.z;

            result = (weights[0].x * weights[0].y) * SAMPLE_TEXTURE3D_LOD(VBuffer, clampSampler, min(float3(texUv0, texW), vBufferViewportLimit), 0)
                   + (weights[1].x * weights[0].y) * SAMPLE_TEXTURE3D_LOD(VBuffer, clampSampler, min(float3(texUv1, texW), vBufferViewportLimit), 0)
                   + (weights[0].x * weights[1].y) * SAMPLE_TEXTURE3D_LOD(VBuffer, clampSampler, min(float3(texUv2, texW), vBufferViewportLimit), 0)
                   + (weights[1].x * weights[1].y) * SAMPLE_TEXTURE3D_LOD(VBuffer, clampSampler, min(float3(texUv3, texW), vBufferViewportLimit), 0);
        }
        else
        {
            float3 texUVW = float3(uv, w) * vBufferViewportScale;
            result = SAMPLE_TEXTURE3D_LOD(VBuffer, clampSampler, min(texUVW, vBufferViewportLimit), 0);
        }
    }

    return result;
}

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
