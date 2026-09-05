#ifndef VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED
#define VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED

// Retained raster entrypoint; use the same resources and Surface Summary ABI
// as DeferredLit.compute, including _DiffuseIrradiance, Sidecar and Slab LUT.
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividDeferredLighting.hlsl"

StructuredBuffer<uint> _MaterialPixelIndices;
uint _LightingWidth;
uint _LightingHeight;

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    nointerpolation uint2 pixelCoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    uint width = max(_LightingWidth, 1u);
    uint height = max(_LightingHeight, 1u);
    uint pixelIndex = _MaterialPixelIndices[input.instanceID];
    uint2 pixelCoord = uint2(pixelIndex % width, pixelIndex / width);
    float2 uv = (float2(pixelCoord) + 0.5) / float2(width, height);
    float2 positionNDC = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);

    output.positionCS = float4(positionNDC, UNITY_NEAR_CLIP_VALUE, 1.0);
#ifdef UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION
    output.positionCS = ApplyPretransformRotation(output.positionCS);
#endif
    output.pixelCoord = pixelCoord;
    return output;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    uint2 pixelCoord = input.pixelCoord;
    float deviceDepth = _DepthTexture.Load(int3(pixelCoord, 0));
    // These entrypoints shade surfaces; the caller owns sky/background rendering.
    if (deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return float4(0.0, 0.0, 0.0, 1.0);

    float4 debugLighting;
    return float4(VividEvaluateDeferredSurfacePixel(pixelCoord, deviceDepth, debugLighting), 1.0);
}

#endif
