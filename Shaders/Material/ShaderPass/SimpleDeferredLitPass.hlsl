#ifndef VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED
#define VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED

// Retained raster entrypoint; use the same resources and Surface Summary ABI
// as DeferredLit.compute, including _DiffuseIrradiance, Sidecar and Slab LUT.
#include "Packages/com.vivid.render-pipelines/Shaders/Core/Public/VividDeferredLighting.hlsl"

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    return output;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    uint2 pixelCoord = uint2(input.positionCS.xy);
    float deviceDepth = _DepthTexture.Load(int3(pixelCoord, 0));
    // These entrypoints shade surfaces; the caller owns sky/background rendering.
    if (deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return float4(0.0, 0.0, 0.0, 1.0);

    float4 debugLighting;
    return float4(VividEvaluateDeferredSurfacePixel(pixelCoord, deviceDepth, debugLighting), 1.0);
}

#endif
