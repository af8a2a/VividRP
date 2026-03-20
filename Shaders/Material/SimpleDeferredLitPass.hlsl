#ifndef VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED
#define VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"

TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);
TEXTURE2D_X(_GBuffer3);
TEXTURE2D_X_FLOAT(_DepthTexture);

float4 _MainLightDirection;
float4 _MainLightColor;
float4 _AmbientColor;

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

float3 GetDeferredViewDirectionWS(float3 positionWS)
{
    if (unity_OrthoParams.w > 0.5)
        return TransformViewToWorldDir(float3(0.0, 0.0, -1.0), true);

    return SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);
}

float3 GetDeferredLightDirectionWS()
{
    DirectionalLightData mainLight;
    if (TryGetMainDirectionalLight(mainLight))
        return SafeNormalize(mainLight.directionWS);

    return SafeNormalize(_MainLightDirection.xyz);
}

float3 GetDeferredLightColor()
{
    DirectionalLightData mainLight;
    if (TryGetMainDirectionalLight(mainLight))
        return mainLight.color;

    return _MainLightColor.rgb;
}

uint2 GetPixelCoord(Varyings input)
{
    return uint2(input.positionCS.xy);
}

float2 GetPixelUV(uint2 pixelCoord)
{
    float2 invScaledScreenSize = rcp(max(_ScaledScreenParams.xy, float2(1.0, 1.0)));
    return (float2(pixelCoord) + 0.5) * invScaledScreenSize;
}

VividGBufferSurfaceData LoadVividGBuffer(uint2 pixelCoord)
{
    float4 rt0 = LOAD_TEXTURE2D_X(_GBuffer0, pixelCoord);
    float4 rt1 = LOAD_TEXTURE2D_X(_GBuffer1, pixelCoord);
    float4 rt2 = LOAD_TEXTURE2D_X(_GBuffer2, pixelCoord);
    float4 rt3 = LOAD_TEXTURE2D_X(_GBuffer3, pixelCoord);
    return UnpackVividGBufferSurfaceData(rt0, rt1, rt2, rt3);
}

float3 EvaluateSimpleDeferredLighting(VividGBufferSurfaceData surfaceData, float3 positionWS)
{
    float3 viewDirectionWS = GetDeferredViewDirectionWS(positionWS);
    VividLitBSDFData bsdfData = BuildVividHdrpLitBSDFData(surfaceData);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    float3 indirectLighting = EvaluateIndirectLighting(surfaceData, bsdfData, viewDirectionWS);

    if (!VividHasSkyIBL())
        indirectLighting += diffuseColor * _AmbientColor.rgb * surfaceData.ambientOcclusion;

    if (!HasDirectionalLights())
    {
        float3 lightDirectionWS = GetDeferredLightDirectionWS();
        float3 lightColor = GetDeferredLightColor();
        float3 directLighting = surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC
            ? EvaluateVividFabricDirectLight(surfaceData, viewDirectionWS, lightDirectionWS) * lightColor
            : EvaluateVividLitDirectLight(surfaceData, bsdfData, viewDirectionWS, lightDirectionWS) * lightColor;
        return directLighting + indirectLighting + surfaceData.emissive;
    }

    float3 accumulatedDirectionalLighting = 0.0;

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        accumulatedDirectionalLighting += EvaluateDirectionalLight(surfaceData, bsdfData, viewDirectionWS, directionalLight);
    }

    return accumulatedDirectionalLighting + indirectLighting + surfaceData.emissive;
}

float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    uint2 pixelCoord = GetPixelCoord(input);
    float deviceDepth = LOAD_TEXTURE2D_X(_DepthTexture, pixelCoord).r;
    if (deviceDepth == UNITY_RAW_FAR_CLIP_VALUE)
        return float4(0.0, 0.0, 0.0, 1.0);

    float2 uv = GetPixelUV(pixelCoord);
    VividGBufferSurfaceData surfaceData = LoadVividGBuffer(pixelCoord);
    float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, _InvViewProjMatrix);
    float3 lighting = EvaluateSimpleDeferredLighting(surfaceData, positionWS);
    return float4(lighting, 1.0);
}

#endif
