#ifndef VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED
#define VIVIDRP_SIMPLE_DEFERRED_LIT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
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

static const float3 kDielectricF0 = float3(0.04, 0.04, 0.04);

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

float Pow5Fast(float value)
{
    float value2 = value * value;
    return value2 * value2 * value;
}

float D_GGX(float nDotH, float linearRoughness)
{
    float alpha = max(linearRoughness, 0.002);
    float alpha2 = alpha * alpha;
    float denominator = nDotH * nDotH * (alpha2 - 1.0) + 1.0;
    return alpha2 / max(PI * denominator * denominator, 1e-6);
}

float V_SmithGGXCorrelated(float nDotV, float nDotL, float linearRoughness)
{
    float alpha = max(linearRoughness, 0.002);
    float alpha2 = alpha * alpha;

    float lambdaV = nDotL * sqrt(max((nDotV - nDotV * alpha2) * nDotV + alpha2, 1e-6));
    float lambdaL = nDotV * sqrt(max((nDotL - nDotL * alpha2) * nDotL + alpha2, 1e-6));

    return 0.5 / max(lambdaV + lambdaL, 1e-6);
}

float3 F_Schlick(float vDotH, float3 f0)
{
    float fresnel = Pow5Fast(1.0 - vDotH);
    return f0 + (1.0 - f0) * fresnel;
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

float3 EvaluateClearCoatSpecular(float3 normalWS, float3 viewDirectionWS, float3 lightDirectionWS, float clearCoatMask)
{
    if (clearCoatMask <= 0.0)
        return 0.0;

    float3 halfVectorWS = SafeNormalize(viewDirectionWS + lightDirectionWS);
    float nDotV = saturate(dot(normalWS, viewDirectionWS));
    float nDotL = saturate(dot(normalWS, lightDirectionWS));
    float nDotH = saturate(dot(normalWS, halfVectorWS));
    float vDotH = saturate(dot(viewDirectionWS, halfVectorWS));

    float linearRoughness = 0.04;
    float distribution = D_GGX(nDotH, linearRoughness);
    float visibility = V_SmithGGXCorrelated(nDotV, nDotL, linearRoughness);
    float3 fresnel = F_Schlick(vDotH, kDielectricF0) * clearCoatMask;
    return distribution * visibility * fresnel;
}

float3 EvaluateFabricFuzz(float3 baseColor, float3 normalWS, float3 viewDirectionWS, float3 lightDirectionWS, float fuzzAmount)
{
    if (fuzzAmount <= 0.0)
        return 0.0;

    float nDotL = saturate(dot(normalWS, lightDirectionWS));
    float fresnel = Pow5Fast(1.0 - saturate(dot(normalWS, viewDirectionWS)));
    return baseColor * fuzzAmount * fresnel * nDotL * 0.25;
}

float3 EvaluateSimpleDeferredLighting(VividGBufferSurfaceData surfaceData, float3 positionWS)
{
    float3 normalWS = surfaceData.normalWS;
    float3 viewDirectionWS = GetDeferredViewDirectionWS(positionWS);
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    float3 ambientLighting = diffuseColor * _AmbientColor.rgb * surfaceData.ambientOcclusion;

    if (!HasDirectionalLights())
    {
        float3 lightDirectionWS = GetDeferredLightDirectionWS();
        float3 halfVectorWS = SafeNormalize(viewDirectionWS + lightDirectionWS);

        float nDotL = saturate(dot(normalWS, lightDirectionWS));
        float nDotV = saturate(dot(normalWS, viewDirectionWS));
        float nDotH = saturate(dot(normalWS, halfVectorWS));
        float vDotH = saturate(dot(viewDirectionWS, halfVectorWS));

        float3 f0 = lerp(kDielectricF0, surfaceData.baseColor, surfaceData.metallic);
        float3 fresnel = F_Schlick(vDotH, f0);
        float distribution = D_GGX(nDotH, surfaceData.linearRoughness);
        float visibility = V_SmithGGXCorrelated(nDotV, nDotL, surfaceData.linearRoughness);

        float3 specular = distribution * visibility * fresnel;
        float3 diffuse = diffuseColor * INV_PI;
        float3 lightColor = GetDeferredLightColor();
        float3 directLighting = (diffuse + specular) * lightColor * nDotL;
        float3 materialLighting = 0.0;

        if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
        {
            materialLighting += EvaluateFabricFuzz(
                surfaceData.baseColor,
                normalWS,
                viewDirectionWS,
                lightDirectionWS,
                surfaceData.customData) * lightColor;
        }
        else if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
        {
            materialLighting += EvaluateClearCoatSpecular(
                normalWS,
                viewDirectionWS,
                lightDirectionWS,
                surfaceData.customData) * lightColor * nDotL;
        }

        return directLighting + ambientLighting + materialLighting + surfaceData.emissive;
    }

    float3 accumulatedDirectionalLighting = 0.0;

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        float3 lightDirectionWS = SafeNormalize(directionalLight.directionWS);
        float3 halfVectorWS = SafeNormalize(viewDirectionWS + lightDirectionWS);

        float nDotL = saturate(dot(normalWS, lightDirectionWS));
        float nDotV = saturate(dot(normalWS, viewDirectionWS));
        float nDotH = saturate(dot(normalWS, halfVectorWS));
        float vDotH = saturate(dot(viewDirectionWS, halfVectorWS));

        float3 f0 = lerp(kDielectricF0, surfaceData.baseColor, surfaceData.metallic);
        float3 fresnel = F_Schlick(vDotH, f0);
        float distribution = D_GGX(nDotH, surfaceData.linearRoughness);
        float visibility = V_SmithGGXCorrelated(nDotV, nDotL, surfaceData.linearRoughness);

        float3 specular = distribution * visibility * fresnel;
        float3 diffuse = diffuseColor * INV_PI;
        float3 directionalLighting = (diffuse + specular) * directionalLight.color * nDotL;

        if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
        {
            directionalLighting += EvaluateFabricFuzz(
                surfaceData.baseColor,
                normalWS,
                viewDirectionWS,
                lightDirectionWS,
                surfaceData.customData) * directionalLight.color;
        }
        else if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
        {
            directionalLighting += EvaluateClearCoatSpecular(
                normalWS,
                viewDirectionWS,
                lightDirectionWS,
                surfaceData.customData) * directionalLight.color * nDotL;
        }

        accumulatedDirectionalLighting += directionalLighting;
    }

    return accumulatedDirectionalLighting + ambientLighting + surfaceData.emissive;
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
