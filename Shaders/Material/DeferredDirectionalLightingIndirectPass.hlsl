#ifndef VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED
#define VIVIDRP_DEFERRED_DIRECTIONAL_LIGHTING_INDIRECT_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"

TEXTURE2D_X(_GBuffer0);
TEXTURE2D_X(_GBuffer1);
TEXTURE2D_X(_GBuffer2);
TEXTURE2D_X(_GBuffer3);
TEXTURE2D_X_FLOAT(_DepthTexture);

StructuredBuffer<uint> _MaterialPixelIndices;

uint _LightingWidth;
uint _LightingHeight;

static const float3 kDielectricF0 = float3(0.04, 0.04, 0.04);

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

float3 EvaluateDirectionalLight(
    VividGBufferSurfaceData surfaceData,
    float3 viewDirectionWS,
    DirectionalLightData directionalLight)
{
    float3 normalWS = surfaceData.normalWS;
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
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
    float3 lighting = (diffuse + specular) * directionalLight.color * nDotL;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
    {
        lighting += EvaluateFabricFuzz(
            surfaceData.baseColor,
            normalWS,
            viewDirectionWS,
            lightDirectionWS,
            surfaceData.customData) * directionalLight.color;
    }
    else if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
    {
        lighting += EvaluateClearCoatSpecular(
            normalWS,
            viewDirectionWS,
            lightDirectionWS,
            surfaceData.customData) * directionalLight.color * nDotL;
    }

    return lighting;
}

float EvaluatePunctualLightDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float attenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
    return attenuation * attenuation;
}

float EvaluatePunctualLightSpotAttenuation(PunctualLightData punctualLight, float3 lightDirectionWS)
{
    if (punctualLight.lightType != VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
        return 1.0;

    float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
    float attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
    return attenuation * attenuation;
}

float3 EvaluatePunctualLight(
    VividGBufferSurfaceData surfaceData,
    float3 positionWS,
    float3 viewDirectionWS,
    PunctualLightData punctualLight)
{
    float3 lightVectorWS = punctualLight.positionWS - positionWS;
    float distanceSquared = dot(lightVectorWS, lightVectorWS);

    if (distanceSquared <= 1e-6)
        return 0.0;

    float inverseDistance = rsqrt(distanceSquared);
    float3 lightDirectionWS = lightVectorWS * inverseDistance;
    float3 halfVectorWS = SafeNormalize(viewDirectionWS + lightDirectionWS);
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));

    if (nDotL <= 0.0)
        return 0.0;

    float attenuation = EvaluatePunctualLightDistanceAttenuation(punctualLight, distanceSquared)
        * EvaluatePunctualLightSpotAttenuation(punctualLight, lightDirectionWS);

    if (attenuation <= 0.0)
        return 0.0;

    float nDotV = saturate(dot(surfaceData.normalWS, viewDirectionWS));
    float nDotH = saturate(dot(surfaceData.normalWS, halfVectorWS));
    float vDotH = saturate(dot(viewDirectionWS, halfVectorWS));
    float3 diffuseColor = surfaceData.baseColor * (1.0 - surfaceData.metallic);
    float3 f0 = lerp(kDielectricF0, surfaceData.baseColor, surfaceData.metallic);
    float3 fresnel = F_Schlick(vDotH, f0);
    float distribution = D_GGX(nDotH, surfaceData.linearRoughness);
    float visibility = V_SmithGGXCorrelated(nDotV, nDotL, surfaceData.linearRoughness);
    float3 specular = distribution * visibility * fresnel;
    float3 diffuse = diffuseColor * INV_PI;
    float3 lightColor = punctualLight.color * attenuation;
    float3 lighting = (diffuse + specular) * lightColor * nDotL;

    if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_FABRIC)
    {
        lighting += EvaluateFabricFuzz(
            surfaceData.baseColor,
            surfaceData.normalWS,
            viewDirectionWS,
            lightDirectionWS,
            surfaceData.customData) * lightColor;
    }
    else if (surfaceData.materialId == VIVID_GBUFFER_MATERIAL_CLEARCOAT)
    {
        lighting += EvaluateClearCoatSpecular(
            surfaceData.normalWS,
            viewDirectionWS,
            lightDirectionWS,
            surfaceData.customData) * lightColor * nDotL;
    }

    return lighting;
}

float3 EvaluateDeferredDirectionalLighting(VividGBufferSurfaceData surfaceData, uint2 pixelCoord, float3 positionWS)
{
    float3 viewDirectionWS = GetDeferredViewDirectionWS(positionWS);
    float3 lighting = 0.0;

    [loop]
    for (uint lightIndex = 0; lightIndex < _DirectionalLightCount; lightIndex++)
    {
        DirectionalLightData directionalLight = GetDirectionalLight(lightIndex);
        lighting += EvaluateDirectionalLight(surfaceData, viewDirectionWS, directionalLight);
    }

    if (HasPunctualLights())
    {
        uint2 clusterLightRange = GetClusterLightRange(pixelCoord, positionWS);

        [loop]
        for (uint lightOffset = 0; lightOffset < clusterLightRange.y; lightOffset++)
        {
            uint lightIndex = _ClusterLightIndices[clusterLightRange.x + lightOffset];
            PunctualLightData punctualLight = GetPunctualLight(lightIndex);
            lighting += EvaluatePunctualLight(surfaceData, positionWS, viewDirectionWS, punctualLight);
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
