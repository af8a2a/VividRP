#ifndef VIVIDRP_INDIRECT_DIFFUSE_PASS_INCLUDED
#define VIVIDRP_INDIRECT_DIFFUSE_PASS_INCLUDED

#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Core.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/GBuffer.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Lighting.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/VividProbeVolume.hlsl"

#define ATTRIBUTES_NEED_TEXCOORD0
#if defined(LIGHTMAP_ON)
    #define ATTRIBUTES_NEED_TEXCOORD1
#endif
#if defined(_NORMALMAP)
    #define ATTRIBUTES_NEED_TANGENT
#endif
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Raytracing/RayTracingCommon.hlsl"
#include "Packages/com.af8a2a.vividrp/Shaders/Core/Public/Raytracing/RaytracingIntersection.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"

#ifndef VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS
    #define VIVIDRP_INDIRECT_DIFFUSE_DEFINE_RAYTRACING_SHADERS 1
#endif

#ifndef VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME
    #define VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME ClosestHitMain
#endif

#ifndef VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME
    #define VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME AnyHitMain
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float4 _BaseMap_ST;
    float4 _EmissionColor;
    float _Cutoff;
    float _Smoothness;
    float _SmoothnessTextureChannel;
    float _Metallic;
    float _BumpScale;
    float _OcclusionStrength;
    float _ClearCoatMask;
    float _ClearCoatSmoothness;
    float _AlphaClip;
    float _WorkflowMode;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_OpacityMap);
SAMPLER(sampler_OpacityMap);
TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_RoughnessMap);
SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap);
SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap);
SAMPLER(sampler_EmissionMap);

struct VividIndirectDiffuseHitGeometry
{
    float3 positionWS;
    float3 normalWS;
    float3 faceNormalWS;
    float3 tangentWS;
    float tangentSign;
    float2 uv;
    float2 lightmapUV;
    float hitDistance;
    bool isFrontFace;
};

struct VividIndirectDiffusePayload
{
    uint traceKind;
    uint hit;
    float signedHitDistance;
    float padding0;
    float4 lightingRadiance;
    float4 emissionRadiance;
    float4 mainDirectionalRadiance;
    float4 shadowNormalWS;
};

static const uint kVividIndirectDiffuseTraceKindRadiance = 0u;
static const uint kVividIndirectDiffuseTraceKindVisibility = 1u;

void VividIndirectDiffuseInitializePayload(out VividIndirectDiffusePayload payload, uint traceKind)
{
    payload.traceKind = traceKind;
    payload.hit = 0u;
    payload.signedHitDistance = -1.0;
    payload.padding0 = 0.0;
    payload.lightingRadiance = 0.0;
    payload.emissionRadiance = 0.0;
    payload.mainDirectionalRadiance = 0.0;
    payload.shadowNormalWS = 0.0;
}

void VividIndirectDiffuseInitializeRadiancePayload(out VividIndirectDiffusePayload payload)
{
    VividIndirectDiffuseInitializePayload(payload, kVividIndirectDiffuseTraceKindRadiance);
}

void VividIndirectDiffuseInitializeVisibilityPayload(out VividIndirectDiffusePayload payload)
{
    VividIndirectDiffuseInitializePayload(payload, kVividIndirectDiffuseTraceKindVisibility);
}

bool VividIndirectDiffuseIsVisibilityTrace(VividIndirectDiffusePayload payload)
{
    return payload.traceKind == kVividIndirectDiffuseTraceKindVisibility;
}

float3 UnpackVividNormalScale(float4 packedNormal, float scale)
{
    float3 normalTS;
    normalTS.xy = packedNormal.wy * 2.0 - 1.0;
    normalTS.xy *= scale;
    normalTS.z = sqrt(saturate(1.0 - dot(normalTS.xy, normalTS.xy)));
    return normalTS;
}

float2 VividIndirectDiffuseTransformUV(float2 uv)
{
    return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
}

float4 SampleBase(float2 uv)
{
    float4 baseSample = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uv, 0.0) * _BaseColor;
#if defined(_OPACITYMAP)
    baseSample.a *= SAMPLE_TEXTURE2D_LOD(_OpacityMap, sampler_OpacityMap, uv, 0.0).r;
#endif
    return baseSample;
}

bool VividIndirectDiffuseIsAlphaClipped(float alpha)
{
#if defined(_ALPHATEST_ON)
    return alpha < _Cutoff;
#else
    return false;
#endif
}

float2 SampleMetallicSmoothness(float2 uv, float baseAlpha)
{
    float metallic = saturate(_Metallic);
    float smoothness = saturate(_Smoothness);

#if defined(_METALLICSPECGLOSSMAP)
    float4 metallicGlossSample = SAMPLE_TEXTURE2D_LOD(_MetallicGlossMap, sampler_MetallicGlossMap, uv, 0.0);
    metallic = saturate(metallicGlossSample.r * _Metallic);

    #if defined(_ROUGHNESSMAP)
        float roughness = SAMPLE_TEXTURE2D_LOD(_RoughnessMap, sampler_RoughnessMap, uv, 0.0).r;
        smoothness = (1.0 - roughness) * _Smoothness;
    #elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
        smoothness = baseAlpha * _Smoothness;
    #else
        smoothness = metallicGlossSample.a * _Smoothness;
    #endif
#elif defined(_ROUGHNESSMAP)
    float roughness = SAMPLE_TEXTURE2D_LOD(_RoughnessMap, sampler_RoughnessMap, uv, 0.0).r;
    smoothness = (1.0 - roughness) * _Smoothness;
#elif defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    smoothness = baseAlpha * _Smoothness;
#endif

    return float2(metallic, saturate(smoothness));
}

float SampleAmbientOcclusion(float2 uv)
{
#if defined(_OCCLUSIONMAP)
    float occlusion = SAMPLE_TEXTURE2D_LOD(_OcclusionMap, sampler_OcclusionMap, uv, 0.0).g;
    return saturate(lerp(1.0, occlusion, _OcclusionStrength));
#else
    return 1.0;
#endif
}

float3 SampleEmission(float2 uv)
{
#if defined(_EMISSION)
    return max(SAMPLE_TEXTURE2D_LOD(_EmissionMap, sampler_EmissionMap, uv, 0.0).rgb * _EmissionColor.rgb, 0.0);
#else
    return float3(0.0, 0.0, 0.0);
#endif
}

float3 VividIndirectDiffuseTransformNormalToWorld(float3 normalOS)
{
    return normalize(mul(normalOS, (float3x3)WorldToObject3x4()));
}

float3 VividIndirectDiffuseTransformDirToWorld(float3 directionOS)
{
    return normalize(mul(directionOS, (float3x3)WorldToObject3x4()));
}

float2 VividIndirectDiffuseFetchUV(AttributeData attributeData)
{
    IntersectionVertex currentVertex;
    GetCurrentIntersectionVertex(attributeData, currentVertex);
    return VividIndirectDiffuseTransformUV(currentVertex.texCoord0.xy);
}

float2 VividIndirectDiffuseFetchLightmapUV(IntersectionVertex currentVertex)
{
#if defined(LIGHTMAP_ON)
    return TransformVividLightmapUV(currentVertex.texCoord1.xy);
#else
    return 0.0;
#endif
}

VividIndirectDiffuseHitGeometry VividIndirectDiffuseBuildHitGeometry(AttributeData attributeData)
{
    IntersectionVertex currentVertex;
    GetCurrentIntersectionVertex(attributeData, currentVertex);

    VividIndirectDiffuseHitGeometry geometry;
    geometry.positionWS = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    GetCurrentIntersectionGeometricNormal(attributeData, geometry.faceNormalWS);
    geometry.normalWS = VividIndirectDiffuseTransformNormalToWorld(currentVertex.normalOS);
    geometry.tangentWS = VividIndirectDiffuseTransformDirToWorld(currentVertex.tangentOS.xyz);
    geometry.tangentSign = sign(currentVertex.tangentOS.w);
    geometry.uv = VividIndirectDiffuseTransformUV(currentVertex.texCoord0.xy);
    geometry.lightmapUV = VividIndirectDiffuseFetchLightmapUV(currentVertex);
    geometry.hitDistance = RayTCurrent();
    geometry.isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

    if (dot(geometry.normalWS, geometry.faceNormalWS) < 0.0)
    {
        geometry.normalWS = -geometry.normalWS;
    }

    if (!geometry.isFrontFace)
    {
        geometry.faceNormalWS = -geometry.faceNormalWS;
        geometry.normalWS = -geometry.normalWS;
    }

    return geometry;
}

float3 VividIndirectDiffuseSampleNormalWS(VividIndirectDiffuseHitGeometry geometry)
{
    float3 normalWS = SafeNormalize(geometry.normalWS);

#if defined(_NORMALMAP)
    float tangentLengthSquared = dot(geometry.tangentWS, geometry.tangentWS);
    if (tangentLengthSquared > 1e-8)
    {
        float3 tangentWS = geometry.tangentWS * rsqrt(tangentLengthSquared);
        float3 bitangentWS = SafeNormalize(cross(normalWS, tangentWS) * geometry.tangentSign);
        float3 normalTS = UnpackVividNormalScale(SAMPLE_TEXTURE2D_LOD(_BumpMap, sampler_BumpMap, geometry.uv, 0.0), _BumpScale);
        normalWS = SafeNormalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * normalWS);
    }
#endif

    return normalWS;
}

float3 SampleStandardLitIndirectDiffuseBakedGI(VividIndirectDiffuseHitGeometry geometry, float3 normalWS)
{
#if defined(LIGHTMAP_ON)
    return SampleVividBakedGI(geometry.lightmapUV, normalWS);
#else
    return SampleVividProbeVolume(
        geometry.positionWS,
        normalWS,
        GetWorldSpaceNormalizeViewDir(geometry.positionWS),
        GetMeshRenderingLayerMask());
#endif
}

float HasStandardLitIndirectDiffuseBakedGI()
{
#if defined(LIGHTMAP_ON)
    return 1.0;
#else
    return VividHasProbeVolumeGI() ? 1.0 : 0.0;
#endif
}

VividGBufferSurfaceData BuildStandardLitSurfaceData(VividIndirectDiffuseHitGeometry geometry, out float4 baseSample)
{
    baseSample = SampleBase(geometry.uv);

    float2 metallicSmoothness = SampleMetallicSmoothness(geometry.uv, baseSample.a);

    VividGBufferSurfaceData surfaceData;
    surfaceData.baseColor = baseSample.rgb;
    surfaceData.normalWS = VividIndirectDiffuseSampleNormalWS(geometry);
    surfaceData.linearRoughness = (1.0 - metallicSmoothness.y) * (1.0 - metallicSmoothness.y);
    surfaceData.metallic = metallicSmoothness.x;
    surfaceData.ambientOcclusion = SampleAmbientOcclusion(geometry.uv);
    surfaceData.customData1 = 0.0;

#if defined(_CLEARCOAT)
    float clearCoatMask = saturate(_ClearCoatMask);
    surfaceData.customData = clearCoatMask;
    surfaceData.materialId = clearCoatMask > 0.0 ? VIVID_GBUFFER_MATERIAL_CLEARCOAT : VIVID_GBUFFER_MATERIAL_STANDARD;
#else
    surfaceData.customData = 0.0;
    surfaceData.materialId = VIVID_GBUFFER_MATERIAL_STANDARD;
#endif

    surfaceData.emissive = SampleEmission(geometry.uv);
    surfaceData.bakedGI = SampleStandardLitIndirectDiffuseBakedGI(geometry, surfaceData.normalWS);
    surfaceData.hasBakedGI = HasStandardLitIndirectDiffuseBakedGI();
    return surfaceData;
}

float3 VividIndirectDiffuseGetDiffuseColor(VividGBufferSurfaceData surfaceData)
{
    return saturate(surfaceData.baseColor) * (1.0 - saturate(surfaceData.metallic));
}

float3 VividIndirectDiffuseEvaluateDirectional(
    VividGBufferSurfaceData surfaceData,
    float3 diffuseColor,
    DirectionalLightData directionalLight,
    out float3 lightDirectionWS)
{
    lightDirectionWS = SafeNormalize(directionalLight.directionWS);
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));
    return diffuseColor * (INV_PI * nDotL) * directionalLight.color;
}

float VividIndirectDiffuseEvaluatePunctualDistanceAttenuation(PunctualLightData punctualLight, float distanceSquared)
{
    float attenuation = saturate(1.0 - distanceSquared * punctualLight.inverseRangeSquared);
    return attenuation * attenuation;
}

float VividIndirectDiffuseEvaluatePunctualSpotAttenuation(PunctualLightData punctualLight, float3 lightDirectionWS)
{
    if (punctualLight.lightType != VIVID_PUNCTUAL_LIGHT_TYPE_SPOT)
    {
        return 1.0;
    }

    float spotCosine = saturate(dot(punctualLight.directionWS, -lightDirectionWS));
    float attenuation = saturate(spotCosine * punctualLight.angleScale + punctualLight.angleOffset);
    return attenuation * attenuation;
}

float3 VividIndirectDiffuseEvaluatePunctual(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    float3 diffuseColor,
    PunctualLightData punctualLight,
    out float3 lightDirectionWS,
    out float lightDistance)
{
    float3 lightVectorWS = punctualLight.positionWS - geometry.positionWS;
    float distanceSquared = dot(lightVectorWS, lightVectorWS);
    lightDirectionWS = float3(0.0, 0.0, 0.0);
    lightDistance = 0.0;

    if (distanceSquared <= 1e-6)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float inverseDistance = rsqrt(distanceSquared);
    lightDistance = distanceSquared * inverseDistance;
    lightDirectionWS = lightVectorWS * inverseDistance;
    float nDotL = saturate(dot(surfaceData.normalWS, lightDirectionWS));
    if (nDotL <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float attenuation = VividIndirectDiffuseEvaluatePunctualDistanceAttenuation(punctualLight, distanceSquared)
        * VividIndirectDiffuseEvaluatePunctualSpotAttenuation(punctualLight, lightDirectionWS);

    return diffuseColor * (INV_PI * nDotL) * punctualLight.color * attenuation;
}

void VividIndirectDiffuseEvaluateFrontFaceRadiance(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    out float3 lightingRadiance,
    out float3 emissionRadiance,
    out float3 mainDirectionalDirectionWS,
    out float3 mainDirectionalRadiance)
{
    float3 diffuseColor = VividIndirectDiffuseGetDiffuseColor(surfaceData);
    lightingRadiance = float3(0.0, 0.0, 0.0);
    emissionRadiance = surfaceData.emissive;
    mainDirectionalDirectionWS = float3(0.0, 0.0, 0.0);
    mainDirectionalRadiance = float3(0.0, 0.0, 0.0);

    if (surfaceData.hasBakedGI > 0.5)
    {
        lightingRadiance += surfaceData.bakedGI * diffuseColor * INV_PI;
    }

    DirectionalLightData sunLight;
    if (TryGetMainDirectionalLight(sunLight))
    {
        mainDirectionalDirectionWS = sunLight.directionWS;
        float3 directionalRadiance = VividIndirectDiffuseEvaluateDirectional( surfaceData, diffuseColor, sunLight, mainDirectionalDirectionWS);

        mainDirectionalRadiance = directionalRadiance * saturate(sunLight.shadowStrength);
    }

    // for (uint lightIndex = 0u; lightIndex < _PunctualLightCount; lightIndex++)
    // {
    //     PunctualLightData punctualLight = _PunctualLights[lightIndex];
    //     float3 lightDirectionWS = float3(0.0, 1.0, 0.0);
    //     float lightDistance = 0.0;
    //     float3 punctualRadiance = VividIndirectDiffuseEvaluatePunctual(
    //         geometry,
    //         surfaceData,
    //         diffuseColor,
    //         punctualLight,
    //         lightDirectionWS,
    //         lightDistance);
    //     lightingRadiance += punctualRadiance;
    // }

    lightingRadiance *= surfaceData.ambientOcclusion;
    mainDirectionalRadiance *= surfaceData.ambientOcclusion;
}

void VividIndirectDiffuseWritePayload(
    VividIndirectDiffuseHitGeometry geometry,
    VividGBufferSurfaceData surfaceData,
    inout VividIndirectDiffusePayload payload)
{
    payload.hit = 1u;
    payload.lightingRadiance = 0.0;
    payload.emissionRadiance = 0.0;
    payload.mainDirectionalRadiance = 0.0;

    if (geometry.isFrontFace)
    {
        float3 lightingRadiance = float3(0.0, 0.0, 0.0);
        float3 emissionRadiance = float3(0.0, 0.0, 0.0);
        float3 mainDirectionalDirectionWS = float3(0.0, 0.0, 0.0);
        float3 mainDirectionalRadiance = float3(0.0, 0.0, 0.0);
        VividIndirectDiffuseEvaluateFrontFaceRadiance(
            geometry,
            surfaceData,
            lightingRadiance,
            emissionRadiance,
            mainDirectionalDirectionWS,
            mainDirectionalRadiance);
        payload.lightingRadiance = float4(lightingRadiance, 0.0);
        payload.emissionRadiance = float4(emissionRadiance, 0.0);
        payload.mainDirectionalRadiance = float4(mainDirectionalRadiance, 0.0);
        payload.shadowNormalWS = float4(SafeNormalize(surfaceData.normalWS), 0.0);
    }

    payload.signedHitDistance = geometry.isFrontFace ? geometry.hitDistance : -geometry.hitDistance;
}

bool VividIndirectDiffuseAnyHit(AttributeData attributeData)
{
    float2 uv = VividIndirectDiffuseFetchUV(attributeData);
    float alpha = SampleBase(uv).a;
    return VividIndirectDiffuseIsAlphaClipped(alpha) ? VIVID_RAYTRACING_HIT_IGNORE : VIVID_RAYTRACING_HIT_ACCEPT;
}

void VividIndirectDiffuseClosestHit(
    AttributeData attributeData,
    inout VividIndirectDiffusePayload payload)
{
    if (VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        return;
    }

    VividIndirectDiffuseHitGeometry geometry = VividIndirectDiffuseBuildHitGeometry(attributeData);
    float4 baseSample = float4(0.0, 0.0, 0.0, 0.0);
    VividGBufferSurfaceData surfaceData = BuildStandardLitSurfaceData(geometry, baseSample);
    VividIndirectDiffuseWritePayload(geometry, surfaceData, payload);
}
#ifndef SHADERSTAGE_RGS
[shader("closesthit")]
void VIVIDRP_INDIRECT_DIFFUSE_CLOSEST_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    VividIndirectDiffuseClosestHit(attributeData, payload);
}

[shader("anyhit")]
void VIVIDRP_INDIRECT_DIFFUSE_ANY_HIT_NAME(
    inout VividIndirectDiffusePayload payload : SV_RayPayload,
    AttributeData attributeData : SV_IntersectionAttributes)
{
    uint result = VividIndirectDiffuseAnyHit(attributeData);

    if (result == VIVID_RAYTRACING_HIT_ACCEPT && VividIndirectDiffuseIsVisibilityTrace(payload))
    {
        payload.hit = 1u;
        payload.signedHitDistance = RayTCurrent();
        AcceptHitAndEndSearch();
        return;
    }

    if (result == VIVID_RAYTRACING_HIT_IGNORE)
    {
        IgnoreHit();
    }
    else if (result == VIVID_RAYTRACING_HIT_ACCEPT_AND_END_SEARCH)
    {
        AcceptHitAndEndSearch();
    }
}
#endif

#endif
