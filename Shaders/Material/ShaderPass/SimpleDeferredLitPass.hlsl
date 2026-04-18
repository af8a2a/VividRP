#ifndef VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED
#define VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/AutoExposure.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/HdrpLitLighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"

TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);
TEXTURE2D_X(_GBuffer3);
TEXTURE2D_X(_GBuffer4);
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
    float3 viewDirectionWS = SafeNormalize(_WorldSpaceCameraPos.xyz - positionWS);

    if (unity_OrthoParams.w > 0.5)
        viewDirectionWS = TransformViewToWorldDir(float3(0.0, 0.0, -1.0), true);

    return viewDirectionWS;
}

float3 GetDeferredLightDirectionWS()
{
    DirectionalLightData mainLight = (DirectionalLightData)0;
    if (TryGetMainDirectionalLight(mainLight))
        return SafeNormalize(mainLight.directionWS);

    return SafeNormalize(_MainLightDirection.xyz);
}

float3 GetDeferredLightColor()
{
    DirectionalLightData mainLight = (DirectionalLightData)0;
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
    float4 rt4 = LOAD_TEXTURE2D_X(_GBuffer4, pixelCoord);
    return UnpackVividGBufferSurfaceData(rt0, rt1, rt2, rt3, rt4);
}

float3 EvaluateSimpleDeferredLighting(VividGBufferSurfaceData surfaceData, float3 positionWS)
{
    float3 viewDirectionWS = SafeNormalize(GetDeferredViewDirectionWS(positionWS));
    VividLitBSDFData bsdfData = BuildVividHDRPLitBSDFData(surfaceData);
    VividPreLightData preLightData = GetVividPreLightData(viewDirectionWS, surfaceData, bsdfData);
    VividAggregateLighting aggregateLighting = (VividAggregateLighting)0;

    AccumulateIndirectLighting(
        EvaluateBSDF_Env(viewDirectionWS, preLightData, surfaceData, bsdfData),
        aggregateLighting);

    if (!VividHasSkyIBL() && surfaceData.hasBakedGI < 0.5)
        aggregateLighting.indirect.diffuse += _AmbientColor.rgb * surfaceData.ambientOcclusion;

    if (!HasDirectionalLights())
    {
        float3 lightDirectionWS = GetDeferredLightDirectionWS();
        float3 lightColor = GetDeferredLightColor();
        VividCBSDF cbsdf = EvaluateBSDF(viewDirectionWS, lightDirectionWS, preLightData, surfaceData, bsdfData);
        VividDirectLighting directLighting = (VividDirectLighting)0;
        directLighting.diffuse = cbsdf.diffuse * lightColor;
        directLighting.specular = cbsdf.specular * lightColor;
        AccumulateDirectLighting(directLighting, aggregateLighting);

        VividLightLoopOutput fallbackLightLoopOutput = (VividLightLoopOutput)0;
        PostEvaluateBSDF(surfaceData, bsdfData, preLightData, aggregateLighting, fallbackLightLoopOutput);
        return CombineVividLightLoopOutput(fallbackLightLoopOutput);
    }

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        AccumulateDirectLighting(
            EvaluateBSDF_Directional(
                surfaceData,
                bsdfData,
                preLightData,
                viewDirectionWS,
                directionalLight),
            aggregateLighting);
    }

    VividLightLoopOutput lightLoopOutput = (VividLightLoopOutput)0;
    PostEvaluateBSDF(surfaceData, bsdfData, preLightData, aggregateLighting, lightLoopOutput);
    return CombineVividLightLoopOutput(lightLoopOutput);
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
    return float4(VividApplyPreExposure(lighting), 1.0);
}

#endif
