#ifndef VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED
#define VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/LightingLoop.hlsl"

TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);
TEXTURE2D_X(_GBuffer3);
TEXTURE2D_X_FLOAT(_DepthTexture);

StructuredBuffer<uint> _MaterialPixelIndices;

uint _LightingWidth;
uint _LightingHeight;

struct Attributes
{
    uint vertexID : SV_VertexID;
    uint instanceID : SV_InstanceID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    nointerpolation uint2 pixelCoord : TEXCOORD0;
    nointerpolation float2 uv : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

bool IsSkyPixel(float deviceDepth)
{
    return deviceDepth == UNITY_RAW_FAR_CLIP_VALUE;
}

float3 GetDeferredViewDirectionWS(float3 positionWS)
{
    if (unity_OrthoParams.w > 0.5)
        return TransformViewToWorldDir(float3(0.0, 0.0, -1.0), true);

    return SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);
}

VividGBufferSurfaceData LoadVividGBuffer(uint2 pixelCoord)
{
    float4 rt0 = LOAD_TEXTURE2D_X(_GBuffer0, pixelCoord);
    float4 rt1 = LOAD_TEXTURE2D_X(_GBuffer1, pixelCoord);
    float4 rt2 = LOAD_TEXTURE2D_X(_GBuffer2, pixelCoord);
    float4 rt3 = LOAD_TEXTURE2D_X(_GBuffer3, pixelCoord);
    return UnpackVividGBufferSurfaceData(rt0, rt1, rt2, rt3);
}

float3 EvaluateDeferredDirectionalLighting(VividGBufferSurfaceData surfaceData, uint2 pixelCoord, float3 positionWS)
{
    float3 viewDirectionWS = GetDeferredViewDirectionWS(positionWS);
    VividLitBSDFData bsdfData = BuildVividHdrpLitBSDFData(surfaceData);
    float3 lighting = 0.0;

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        lighting += EvaluateDirectionalLight(surfaceData, bsdfData, viewDirectionWS, directionalLight);
    }

    if (HasPunctualLights())
    {
        VividLightingLoopContext lightLoop = VividLightingLoop::Create(pixelCoord, positionWS);
        uint punctualLightCount = VividLightingLoop::GetPunctualLightCount(lightLoop);

        [loop]
        for (uint localLightIndex = 0; localLightIndex < punctualLightCount; localLightIndex++)
        {
            PunctualLightData punctualLight = VividLightingLoop::LoadPunctualLight(lightLoop, localLightIndex);
            lighting += EvaluatePunctualLight(surfaceData, bsdfData, positionWS, viewDirectionWS, punctualLight);
        }
    }

    return lighting + surfaceData.emissive;
}

Varyings Vert(Attributes input)
{
    Varyings output;

    UNITY_SETUP_INSTANCE_ID(input);
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
    output.uv = uv;
    return output;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, input.pixelCoord).r;
    if (IsSkyPixel(deviceDepth))
        return float4(0.0, 0.0, 0.0, 1.0);

    VividGBufferSurfaceData surfaceData = LoadVividGBuffer(input.pixelCoord);
    float3 positionWS = ComputeWorldSpacePosition(input.uv, deviceDepth, UNITY_MATRIX_I_VP);
    float3 lighting = EvaluateDeferredDirectionalLighting(surfaceData, input.pixelCoord, positionWS);
    return float4(lighting, 1.0);
}

#endif
