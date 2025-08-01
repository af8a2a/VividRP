#ifndef REPLICA_VOLUMETRIC_LIGHTING_INCLUDED
#define REPLICA_VOLUMETRIC_LIGHTING_INCLUDED

#define PREFER_HALF 0

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

TEXTURE3D(_VBufferLighting);

struct VolumetricFogRenderingData {
    float4 viewSpaceBounds;
    uint startSliceIndex;
    uint sliceCount;
    uint padding0;
    uint padding1;
    // float4 obbVertexPositionWS[8];
};

// VBuffer
CBUFFER_START(VolumetricLightingBuffer)
float4x4 _VBufferCoordToViewDirWS;

float4 _VBufferViewportSize;           // { w, h, 1/w, 1/h }
float4 _VBufferLightingViewportScale;  // Necessary us to work with sub-allocation (resource aliasing) in the RTHandle system
float4 _VBufferLightingViewportLimit;  // Necessary us to work with sub-allocation (resource aliasing) in the RTHandle system
float4 _VBufferDistanceEncodingParams; // See the call site for description
float4 _VBufferDistanceDecodingParams; // See the call site for description
float4 _GlobalFogDensity;

uint _VBufferSliceCount;
float _VBufferRcpSliceCount;
float _VBufferVoxelSize;
uint _VisibleCount;

// public float _VBufferRcpInstancedViewCount;  // Used to remap VBuffer coordinates for XR
// public float _VBufferLastSliceDist;          // The distance to the middle of the last slice
CBUFFER_END

#include "./VBuffer.hlsl"

void EvaluateScattering(PositionInputs posInput, out float3 color, out float opacity) {
    color = 0;
    opacity = 0;
    // TODO: do not recompute this, but rather pass it directly.
    // Note1: remember the hacked value of 'posInput.positionWS'.
    // Note2: we do not adjust it anymore to account for the distance to the planet. This can lead to wrong results (since the planet does not write depth).
    float fogFragDist = distance(posInput.positionWS, GetCurrentViewPosition());

    float4 value = SampleVBuffer(TEXTURE3D_ARGS(_VBufferLighting, sampler_LinearClamp),
                                 posInput.positionNDC, fogFragDist,
                                 _VBufferViewportSize, 
                                 _VBufferLightingViewportScale.xyz,
                                 _VBufferLightingViewportLimit.xyz,
                                 _VBufferDistanceEncodingParams,
                                 _VBufferDistanceDecodingParams,
                                 true, true, false);
    float4 volFog = DelinearizeRGBA(value);
    color = volFog.rgb;
    opacity = volFog.a;
}

#endif